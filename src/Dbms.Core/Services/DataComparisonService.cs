using Dbms.Core.Abstractions;
using Dbms.Core.Models;

namespace Dbms.Core.Services;

public sealed class DataComparisonService
{
    public async Task<DataComparisonResult> CompareAsync(
        IDatabaseProvider sourceProvider,
        ConnectionProfile sourceProfile,
        TableDefinition sourceTable,
        IDatabaseProvider destinationProvider,
        ConnectionProfile destinationProfile,
        TableDefinition destinationTable,
        int maxRowsToInspect = 10_000,
        int maxDifferences = 250,
        CancellationToken cancellationToken = default)
    {
        var keyColumns = SelectKeyColumns(sourceTable, destinationTable);
        if (keyColumns.Count == 0)
        {
            return new DataComparisonResult
            {
                IsReliable = false,
                Message = "No primary key or unique key is shared by the two tables. Choose comparison columns before comparing rows."
            };
        }

        var commonColumns = sourceTable.Columns
            .Select(column => new { Source = column, Destination = destinationTable.FindColumn(column.Name) })
            .Where(pair => pair.Destination is not null)
            .ToArray();

        await using var sourceConnection = await sourceProvider.OpenConnectionAsync(sourceProfile, cancellationToken);
        await using var destinationConnection = await destinationProvider.OpenConnectionAsync(destinationProfile, cancellationToken);
        var sourceCountTask = sourceProvider.CountRowsAsync(sourceConnection, sourceTable, cancellationToken);
        var destinationCountTask = destinationProvider.CountRowsAsync(destinationConnection, destinationTable, cancellationToken);
        await Task.WhenAll(sourceCountTask, destinationCountTask);

        var destinationRows = new Dictionary<string, RowSnapshot>(StringComparer.Ordinal);
        var destinationInspected = 0;
        var wasTruncated = false;
        await foreach (var row in destinationProvider.ReadRowsAsync(destinationConnection, destinationTable, destinationTable.Columns.Select(c => c.Name).ToArray(), maxRowsToInspect, cancellationToken))
        {
            destinationInspected++;
            destinationRows[BuildKey(row, keyColumns)] = row;
        }

        if (destinationInspected >= maxRowsToInspect && destinationCountTask.Result > destinationInspected)
        {
            wasTruncated = true;
        }

        var differences = new List<RowDifference>();
        var sourceOnlyCount = 0L;
        var differentCount = 0L;
        var sourceInspected = 0;
        await foreach (var sourceRow in sourceProvider.ReadRowsAsync(sourceConnection, sourceTable, sourceTable.Columns.Select(c => c.Name).ToArray(), maxRowsToInspect, cancellationToken))
        {
            sourceInspected++;
            var key = BuildKey(sourceRow, keyColumns);
            if (!destinationRows.TryGetValue(key, out var destinationRow))
            {
                sourceOnlyCount++;
                AddDifference(differences, maxDifferences, new RowDifference(
                    RowDifferenceKind.SourceOnly,
                    key,
                    sourceRow.Values,
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)));
                continue;
            }

            if (commonColumns.Any(pair => !ValuesEqual(sourceRow[pair.Source.Name], destinationRow[pair.Destination!.Name])))
            {
                differentCount++;
                AddDifference(differences, maxDifferences, new RowDifference(
                    RowDifferenceKind.Different,
                    key,
                    sourceRow.Values,
                    destinationRow.Values));
            }

            destinationRows.Remove(key);
        }

        if (sourceInspected >= maxRowsToInspect && sourceCountTask.Result > sourceInspected)
        {
            wasTruncated = true;
        }

        var destinationOnlyCount = destinationRows.Count;
        foreach (var pair in destinationRows.Take(Math.Max(0, maxDifferences - differences.Count)))
        {
            differences.Add(new RowDifference(
                RowDifferenceKind.DestinationOnly,
                pair.Key,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
                pair.Value.Values));
        }

        return new DataComparisonResult
        {
            IsReliable = !wasTruncated,
            Message = wasTruncated
                ? $"The sample was capped at {maxRowsToInspect:N0} rows. Counts are exact, but difference counts are limited to the inspected sample."
                : "Comparison completed.",
            KeyColumns = keyColumns,
            SourceRowCount = sourceCountTask.Result,
            DestinationRowCount = destinationCountTask.Result,
            SourceOnlyCount = sourceOnlyCount,
            DestinationOnlyCount = destinationOnlyCount,
            DifferentCount = differentCount,
            WasTruncated = wasTruncated,
            Differences = differences
        };
    }

    private static IReadOnlyList<string> SelectKeyColumns(TableDefinition source, TableDefinition destination)
    {
        var sourceKey = source.PreferredKeyColumns;
        if (sourceKey.Count > 0 && sourceKey.All(column => destination.FindColumn(column) is not null))
        {
            return sourceKey;
        }

        var destinationKey = destination.PreferredKeyColumns;
        if (destinationKey.Count > 0 && destinationKey.All(column => source.FindColumn(column) is not null))
        {
            return destinationKey;
        }

        return Array.Empty<string>();
    }

    internal static string BuildKey(RowSnapshot row, IReadOnlyList<string> columns) =>
        string.Join("\u001f", columns.Select(column => ValueFormatter.Format(row[column])));

    internal static bool ValuesEqual(object? left, object? right)
    {
        if (left is DBNull) left = null;
        if (right is DBNull) right = null;
        if (left is null || right is null) return left is null && right is null;
        if (left is byte[] leftBytes && right is byte[] rightBytes) return leftBytes.AsSpan().SequenceEqual(rightBytes);
        if (left is IConvertible && right is IConvertible)
        {
            return string.Equals(ValueFormatter.Format(left), ValueFormatter.Format(right), StringComparison.OrdinalIgnoreCase);
        }

        return Equals(left, right);
    }

    private static void AddDifference(List<RowDifference> differences, int maximum, RowDifference difference)
    {
        if (differences.Count < maximum)
        {
            differences.Add(difference);
        }
    }
}
