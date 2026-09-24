using Dbms.Core.Models;

namespace Dbms.Core.Services;

public sealed class SchemaComparisonService
{
    public SchemaComparisonResult Compare(SchemaSnapshot source, SchemaSnapshot destination)
    {
        var sourceTables = source.Tables.ToDictionary(table => table.QualifiedName, StringComparer.OrdinalIgnoreCase);
        var destinationTables = destination.Tables.ToDictionary(table => table.QualifiedName, StringComparer.OrdinalIgnoreCase);
        var names = sourceTables.Keys.Concat(destinationTables.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

        var comparisons = new List<TableComparison>();
        foreach (var name in names)
        {
            sourceTables.TryGetValue(name, out var sourceTable);
            destinationTables.TryGetValue(name, out var destinationTable);

            if (sourceTable is null)
            {
                comparisons.Add(new TableComparison(null, destinationTable, TableComparisonStatus.DestinationOnly,
                    new[] { "The table exists only in the destination database." }));
                continue;
            }

            if (destinationTable is null)
            {
                comparisons.Add(new TableComparison(sourceTable, null, TableComparisonStatus.SourceOnly,
                    new[] { "The table exists only in the source database." }));
                continue;
            }

            var differences = CompareTables(sourceTable, destinationTable);
            comparisons.Add(new TableComparison(
                sourceTable,
                destinationTable,
                differences.Count == 0 ? TableComparisonStatus.Matching : TableComparisonStatus.Different,
                differences));
        }

        return new SchemaComparisonResult(comparisons);
    }

    private static List<string> CompareTables(TableDefinition source, TableDefinition destination)
    {
        var differences = new List<string>();
        var sourceColumns = source.Columns.ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);
        var destinationColumns = destination.Columns.ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var columnName in sourceColumns.Keys.Concat(destinationColumns.Keys)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            sourceColumns.TryGetValue(columnName, out var sourceColumn);
            destinationColumns.TryGetValue(columnName, out var destinationColumn);

            if (sourceColumn is null)
            {
                differences.Add($"Column '{destinationColumn!.Name}' exists only in the destination.");
                continue;
            }

            if (destinationColumn is null)
            {
                differences.Add($"Column '{sourceColumn.Name}' exists only in the source.");
                continue;
            }

            if (!string.Equals(NormalizeType(sourceColumn.DataType), NormalizeType(destinationColumn.DataType), StringComparison.OrdinalIgnoreCase))
            {
                differences.Add($"Column '{sourceColumn.Name}' type differs ({sourceColumn.DataType} vs {destinationColumn.DataType}).");
            }

            if (sourceColumn.IsNullable != destinationColumn.IsNullable)
            {
                differences.Add($"Column '{sourceColumn.Name}' nullability differs.");
            }

            if (sourceColumn.IsPrimaryKey != destinationColumn.IsPrimaryKey)
            {
                differences.Add($"Column '{sourceColumn.Name}' primary-key membership differs.");
            }

            if (sourceColumn.IsGenerated != destinationColumn.IsGenerated)
            {
                differences.Add($"Column '{sourceColumn.Name}' generated/identity behavior differs.");
            }
        }

        CompareKeys(source, destination, differences);
        CompareIndexes(source, destination, differences);
        return differences;
    }

    private static void CompareKeys(TableDefinition source, TableDefinition destination, List<string> differences)
    {
        var sourceKeys = source.Keys.Select(KeySignature).OrderBy(value => value).ToArray();
        var destinationKeys = destination.Keys.Select(KeySignature).OrderBy(value => value).ToArray();
        if (!sourceKeys.SequenceEqual(destinationKeys, StringComparer.OrdinalIgnoreCase))
        {
            differences.Add("Primary, unique, or foreign-key constraints differ.");
        }
    }

    private static void CompareIndexes(TableDefinition source, TableDefinition destination, List<string> differences)
    {
        var sourceIndexes = source.Indexes.Select(IndexSignature).OrderBy(value => value).ToArray();
        var destinationIndexes = destination.Indexes.Select(IndexSignature).OrderBy(value => value).ToArray();
        if (!sourceIndexes.SequenceEqual(destinationIndexes, StringComparer.OrdinalIgnoreCase))
        {
            differences.Add("Indexes differ.");
        }
    }

    private static string KeySignature(KeyDefinition key) =>
        $"{key.Kind}|{key.Name}|{string.Join(",", key.Columns)}|{key.ReferencedTable}|{string.Join(",", key.ReferencedColumns ?? Array.Empty<string>())}";

    private static string IndexSignature(IndexDefinition index) =>
        $"{index.Name}|{index.IsUnique}|{string.Join(",", index.Columns)}";

    private static string NormalizeType(string dataType) =>
        dataType.Trim().ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
}
