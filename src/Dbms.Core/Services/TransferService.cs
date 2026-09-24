using Dbms.Core.Abstractions;
using Dbms.Core.Models;

namespace Dbms.Core.Services;

public sealed class TransferService
{
    private const int BatchSize = 100;

    public async Task<TransferPlan> BuildPlanAsync(
        IDatabaseProvider sourceProvider,
        ConnectionProfile sourceProfile,
        IDatabaseProvider destinationProvider,
        ConnectionProfile destinationProfile,
        IReadOnlyList<TransferTableSelection> selections,
        CancellationToken cancellationToken = default)
    {
        if (selections.Count == 0)
        {
            return new TransferPlan();
        }

        await using var sourceConnection = await sourceProvider.OpenConnectionAsync(sourceProfile, cancellationToken);
        await using var destinationConnection = await destinationProvider.OpenConnectionAsync(destinationProfile, cancellationToken);
        var plans = new List<TransferTablePlan>();

        var ordering = OrderSelections(selections);
        if (ordering.HasCycle)
        {
            return new TransferPlan
            {
                Tables = selections.Select(selection => new TransferTablePlan
                {
                    SourceTable = selection.SourceTable,
                    DestinationTable = selection.DestinationTable,
                    ConflictCount = 1,
                    CanExecute = false,
                    Message = "A foreign-key dependency cycle exists among the selected tables. Transfer is blocked until the cycle is handled manually."
                }).ToArray()
            };
        }

        foreach (var selection in ordering.OrderedSelections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            plans.Add(await BuildTablePlanAsync(
                sourceProvider,
                sourceConnection,
                selection.SourceTable,
                destinationProvider,
                destinationConnection,
                selection.DestinationTable,
                cancellationToken));
        }

        return new TransferPlan { Tables = plans };
    }

    public async Task<TransferResult> ExecuteAsync(
        IDatabaseProvider sourceProvider,
        ConnectionProfile sourceProfile,
        IDatabaseProvider destinationProvider,
        ConnectionProfile destinationProfile,
        TransferPlan plan,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!plan.CanExecute)
        {
            return new TransferResult { Completed = false, Message = "The transfer plan contains conflicts and cannot be executed." };
        }

        await using var sourceConnection = await sourceProvider.OpenConnectionAsync(sourceProfile, cancellationToken);
        await using var destinationConnection = await destinationProvider.OpenConnectionAsync(destinationProfile, cancellationToken);
        var errors = new List<string>();
        var processed = 0L;
        var inserted = 0L;
        var skipped = 0L;
        var failed = 0L;

        try
        {
            foreach (var tablePlan in plan.Tables)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var keyColumns = tablePlan.KeyColumns;
                var destinationKeyColumns = keyColumns
                    .Select(column => tablePlan.DestinationTable.FindColumn(column)?.Name ?? column)
                    .ToArray();
                var destinationKeys = new HashSet<string>(StringComparer.Ordinal);
                await foreach (var destinationRow in destinationProvider.ReadRowsAsync(
                                   destinationConnection,
                                   tablePlan.DestinationTable,
                                   destinationKeyColumns,
                                   null,
                                   cancellationToken))
                {
                    destinationKeys.Add(DataComparisonService.BuildKey(destinationRow, destinationKeyColumns));
                }

                var sourceColumns = tablePlan.Mappings.Select(mapping => mapping.SourceColumn).ToArray();
                var destinationColumns = tablePlan.Mappings.Select(mapping => mapping.DestinationColumn).ToArray();
                var batch = new List<RowSnapshot>(BatchSize);
                await foreach (var sourceRow in sourceProvider.ReadRowsAsync(
                                   sourceConnection,
                                   tablePlan.SourceTable,
                                   sourceColumns,
                                   null,
                                   cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var key = DataComparisonService.BuildKey(sourceRow, keyColumns);
                    if (destinationKeys.Contains(key))
                    {
                        skipped++;
                        processed++;
                        progress?.Report(new TransferProgress(tablePlan.SourceTable.QualifiedName, processed,
                            tablePlan.SourceRowCount, inserted, skipped, failed, "Existing row skipped."));
                        continue;
                    }

                    var destinationValues = tablePlan.Mappings.ToDictionary(
                        mapping => mapping.DestinationColumn,
                        mapping => sourceRow[mapping.SourceColumn],
                        StringComparer.OrdinalIgnoreCase);
                    batch.Add(new RowSnapshot(destinationValues));
                    destinationKeys.Add(key);

                    if (batch.Count >= BatchSize)
                    {
                        var result = await destinationProvider.InsertBatchAsync(
                            destinationConnection,
                            tablePlan.DestinationTable,
                            destinationColumns,
                            batch,
                            cancellationToken);
                        inserted += result.InsertedRows;
                        skipped += result.SkippedRows;
                        failed += result.FailedRows;
                        errors.AddRange(result.Errors);
                        processed += batch.Count;
                        batch.Clear();
                        progress?.Report(new TransferProgress(tablePlan.SourceTable.QualifiedName, processed,
                            tablePlan.SourceRowCount, inserted, skipped, failed, "Batch committed."));
                    }
                }

                if (batch.Count > 0)
                {
                    var result = await destinationProvider.InsertBatchAsync(
                        destinationConnection,
                        tablePlan.DestinationTable,
                        destinationColumns,
                        batch,
                        cancellationToken);
                    inserted += result.InsertedRows;
                    skipped += result.SkippedRows;
                    failed += result.FailedRows;
                    errors.AddRange(result.Errors);
                    processed += batch.Count;
                    progress?.Report(new TransferProgress(tablePlan.SourceTable.QualifiedName, processed,
                        tablePlan.SourceRowCount, inserted, skipped, failed, "Final batch committed."));
                }
            }

            return new TransferResult
            {
                Completed = true,
                ProcessedRows = processed,
                InsertedRows = inserted,
                SkippedRows = skipped,
                FailedRows = failed,
                Errors = errors,
                Message = failed == 0 ? "Transfer completed." : "Transfer completed with row-level failures."
            };
        }
        catch (OperationCanceledException)
        {
            return new TransferResult
            {
                Completed = false,
                Cancelled = true,
                ProcessedRows = processed,
                InsertedRows = inserted,
                SkippedRows = skipped,
                FailedRows = failed,
                Errors = errors,
                Message = "Transfer cancelled. Committed batches remain in the destination; no rollback across servers is attempted."
            };
        }
    }

    private static async Task<TransferTablePlan> BuildTablePlanAsync(
        IDatabaseProvider sourceProvider,
        System.Data.Common.DbConnection sourceConnection,
        TableDefinition sourceTable,
        IDatabaseProvider destinationProvider,
        System.Data.Common.DbConnection destinationConnection,
        TableDefinition destinationTable,
        CancellationToken cancellationToken)
    {
        var keyColumns = SelectKeyColumns(sourceTable, destinationTable);
        var mappings = BuildMappings(sourceTable, destinationTable, out var mappingConflict);
        var sourceCount = await sourceProvider.CountRowsAsync(sourceConnection, sourceTable, cancellationToken);
        var destinationCount = await destinationProvider.CountRowsAsync(destinationConnection, destinationTable, cancellationToken);

        if (keyColumns.Count == 0)
        {
            return FailedPlan(sourceTable, destinationTable, keyColumns, mappings, sourceCount, destinationCount,
                "No primary key or unique key is shared by the source and destination table. Transfer is blocked to prevent unreliable matching.");
        }

        if (mappingConflict is not null)
        {
            return FailedPlan(sourceTable, destinationTable, keyColumns, mappings, sourceCount, destinationCount, mappingConflict);
        }

        var unmappedKey = keyColumns.FirstOrDefault(column => mappings.All(mapping =>
            !string.Equals(mapping.SourceColumn, column, StringComparison.OrdinalIgnoreCase)));
        if (unmappedKey is not null)
        {
            return FailedPlan(sourceTable, destinationTable, keyColumns, mappings, sourceCount, destinationCount,
                $"Key column '{unmappedKey}' is not part of a safe insert mapping. Generated or identity key values are not copied automatically.");
        }

        var destinationKeys = new HashSet<string>(StringComparer.Ordinal);
        var destinationKeyNames = keyColumns.Select(column => destinationTable.FindColumn(column)!.Name).ToArray();
        await foreach (var destinationRow in destinationProvider.ReadRowsAsync(destinationConnection, destinationTable, destinationKeyNames, null, cancellationToken))
        {
            destinationKeys.Add(DataComparisonService.BuildKey(destinationRow, destinationKeyNames));
        }

        var sourceColumns = mappings.Select(mapping => mapping.SourceColumn).ToArray();
        var missingCount = 0L;
        var skippedCount = 0L;
        var sample = new List<RowSnapshot>();
        await foreach (var sourceRow in sourceProvider.ReadRowsAsync(sourceConnection, sourceTable, sourceColumns, null, cancellationToken))
        {
            var key = DataComparisonService.BuildKey(sourceRow, keyColumns);
            if (destinationKeys.Contains(key))
            {
                skippedCount++;
                continue;
            }

            missingCount++;
            if (sample.Count < 25)
            {
                sample.Add(sourceRow);
            }
        }

        return new TransferTablePlan
        {
            SourceTable = sourceTable,
            DestinationTable = destinationTable,
            KeyColumns = keyColumns,
            Mappings = mappings,
            SourceRowCount = sourceCount,
            DestinationRowCount = destinationCount,
            RowsToInsert = missingCount,
            RowsToSkip = skippedCount,
            SampleRowsToInsert = sample,
            CanExecute = true,
            Message = missingCount == 0 ? "No missing rows were found." : "Ready for insert-only transfer."
        };
    }

    private static TransferTablePlan FailedPlan(
        TableDefinition sourceTable,
        TableDefinition destinationTable,
        IReadOnlyList<string> keys,
        IReadOnlyList<TransferColumnMapping> mappings,
        long sourceCount,
        long destinationCount,
        string message) => new()
        {
            SourceTable = sourceTable,
            DestinationTable = destinationTable,
            KeyColumns = keys,
            Mappings = mappings,
            SourceRowCount = sourceCount,
            DestinationRowCount = destinationCount,
            ConflictCount = 1,
            CanExecute = false,
            Message = message
        };

    private static IReadOnlyList<string> SelectKeyColumns(TableDefinition source, TableDefinition destination)
    {
        var sourceKey = source.PreferredKeyColumns;
        if (sourceKey.Count > 0 && sourceKey.All(column => destination.FindColumn(column) is not null)) return sourceKey;
        var destinationKey = destination.PreferredKeyColumns;
        if (destinationKey.Count > 0 && destinationKey.All(column => source.FindColumn(column) is not null)) return destinationKey;
        return Array.Empty<string>();
    }

    private static IReadOnlyList<TransferColumnMapping> BuildMappings(
        TableDefinition source,
        TableDefinition destination,
        out string? conflict)
    {
        conflict = null;
        var mappings = new List<TransferColumnMapping>();
        foreach (var sourceColumn in source.Columns.Where(column => !column.IsGenerated))
        {
            var destinationColumn = destination.FindColumn(sourceColumn.Name);
            if (destinationColumn is null || destinationColumn.IsGenerated)
            {
                continue;
            }

            if (!AreCompatible(sourceColumn.DataType, destinationColumn.DataType))
            {
                conflict = $"Column '{sourceColumn.Name}' has incompatible types ({sourceColumn.DataType} -> {destinationColumn.DataType}).";
                return mappings;
            }

            mappings.Add(new TransferColumnMapping(sourceColumn.Name, destinationColumn.Name,
                sourceColumn.DataType, destinationColumn.DataType));
        }

        foreach (var destinationColumn in destination.Columns.Where(column => !column.IsNullable && !column.HasDefault && !column.IsGenerated))
        {
            if (mappings.All(mapping => !string.Equals(mapping.DestinationColumn, destinationColumn.Name, StringComparison.OrdinalIgnoreCase)))
            {
                conflict = $"Required destination column '{destinationColumn.Name}' has no source mapping or default. Transfer is blocked.";
                return mappings;
            }
        }

        if (mappings.Count == 0)
        {
            conflict = "No compatible source-to-destination columns were found.";
        }

        return mappings;
    }

    private static bool AreCompatible(string sourceType, string destinationType)
    {
        var sourceFamily = TypeFamily(sourceType);
        var destinationFamily = TypeFamily(destinationType);
        return sourceFamily == destinationFamily
               || sourceFamily == "numeric" && destinationFamily == "numeric"
               || sourceFamily == "text" && destinationFamily == "text";
    }

    private static string TypeFamily(string type)
    {
        var normalized = type.ToLowerInvariant();
        if (normalized.Contains("char") || normalized.Contains("text") || normalized.Contains("clob") || normalized.Contains("json")) return "text";
        if (normalized.Contains("int") || normalized.Contains("numeric") || normalized.Contains("decimal") || normalized.Contains("real") || normalized.Contains("double") || normalized.Contains("float") || normalized.Contains("money")) return "numeric";
        if (normalized.Contains("date") || normalized.Contains("time")) return "datetime";
        if (normalized.Contains("bool")) return "boolean";
        if (normalized.Contains("binary") || normalized.Contains("blob") || normalized.Contains("bytea") || normalized.Contains("image")) return "binary";
        return normalized;
    }

    private static SelectionOrdering OrderSelections(IReadOnlyList<TransferTableSelection> selections)
    {
        var ordered = new List<TransferTableSelection>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byName = selections.ToArray();
        var hasCycle = false;

        void Visit(TransferTableSelection selection)
        {
            var key = selection.DestinationTable.QualifiedName;
            if (visited.Contains(key)) return;
            if (!visiting.Add(key))
            {
                hasCycle = true;
                return;
            }

            foreach (var foreignKey in selection.DestinationTable.Keys.Where(keyDefinition =>
                         string.Equals(keyDefinition.Kind, "FOREIGN KEY", StringComparison.OrdinalIgnoreCase)))
            {
                if (string.IsNullOrWhiteSpace(foreignKey.ReferencedTable)) continue;
                var dependencyName = foreignKey.ReferencedTable!.Split('.').Last();
                var dependency = byName.FirstOrDefault(candidate =>
                    string.Equals(candidate.DestinationTable.Name, dependencyName, StringComparison.OrdinalIgnoreCase)
                    && (foreignKey.ReferencedTable!.Contains('.', StringComparison.Ordinal)
                        ? string.Equals(candidate.DestinationTable.QualifiedName, foreignKey.ReferencedTable, StringComparison.OrdinalIgnoreCase)
                        : true));
                if (dependency is not null) Visit(dependency);
            }

            visiting.Remove(key);
            visited.Add(key);
            ordered.Add(selection);
        }

        foreach (var selection in selections) Visit(selection);
        return new SelectionOrdering(ordered, hasCycle);
    }

    private sealed record SelectionOrdering(IReadOnlyList<TransferTableSelection> OrderedSelections, bool HasCycle);
}
