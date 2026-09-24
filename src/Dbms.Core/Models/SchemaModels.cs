namespace Dbms.Core.Models;

public sealed record ColumnDefinition(
    string Name,
    string DataType,
    bool IsNullable,
    bool IsPrimaryKey,
    bool IsUnique,
    bool IsGenerated,
    bool HasDefault,
    int Ordinal = 0);

public sealed record KeyDefinition(
    string Name,
    string Kind,
    IReadOnlyList<string> Columns,
    string? ReferencedTable = null,
    IReadOnlyList<string>? ReferencedColumns = null);

public sealed record IndexDefinition(
    string Name,
    bool IsUnique,
    IReadOnlyList<string> Columns);

public sealed class TableDefinition
{
    public TableDefinition(
        string schema,
        string name,
        IReadOnlyList<ColumnDefinition> columns,
        IReadOnlyList<KeyDefinition>? keys = null,
        IReadOnlyList<IndexDefinition>? indexes = null)
    {
        Schema = schema;
        Name = name;
        Columns = columns;
        Keys = keys ?? Array.Empty<KeyDefinition>();
        Indexes = indexes ?? Array.Empty<IndexDefinition>();
    }

    public string Schema { get; }
    public string Name { get; }
    public IReadOnlyList<ColumnDefinition> Columns { get; }
    public IReadOnlyList<KeyDefinition> Keys { get; }
    public IReadOnlyList<IndexDefinition> Indexes { get; }

    public string QualifiedName => string.IsNullOrWhiteSpace(Schema) ? Name : $"{Schema}.{Name}";

    public IReadOnlyList<string> PrimaryKeyColumns =>
        Keys.FirstOrDefault(k => string.Equals(k.Kind, "PRIMARY KEY", StringComparison.OrdinalIgnoreCase))?.Columns
        ?? Columns.Where(c => c.IsPrimaryKey).OrderBy(c => c.Ordinal).Select(c => c.Name).ToArray();

    public IReadOnlyList<string> PreferredKeyColumns
    {
        get
        {
            var primaryKey = PrimaryKeyColumns;
            if (primaryKey.Count > 0)
            {
                return primaryKey;
            }

            var unique = Keys.FirstOrDefault(k => string.Equals(k.Kind, "UNIQUE", StringComparison.OrdinalIgnoreCase));
            return unique?.Columns
                ?? Columns.Where(c => c.IsUnique).OrderBy(c => c.Ordinal).Select(c => c.Name).ToArray();
        }
    }

    public ColumnDefinition? FindColumn(string name) =>
        Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed class SchemaSnapshot
{
    public SchemaSnapshot(string databaseName, IReadOnlyList<TableDefinition> tables)
    {
        DatabaseName = databaseName;
        Tables = tables;
    }

    public string DatabaseName { get; }
    public IReadOnlyList<TableDefinition> Tables { get; }
}

public enum TableComparisonStatus
{
    Matching,
    Different,
    SourceOnly,
    DestinationOnly
}

public sealed record TableComparison(
    TableDefinition? SourceTable,
    TableDefinition? DestinationTable,
    TableComparisonStatus Status,
    IReadOnlyList<string> Differences)
{
    public string Name => SourceTable?.Name ?? DestinationTable?.Name ?? string.Empty;
    public string StatusText => Status switch
    {
        TableComparisonStatus.Matching => "Matching",
        TableComparisonStatus.Different => "Different",
        TableComparisonStatus.SourceOnly => "Source only",
        TableComparisonStatus.DestinationOnly => "Destination only",
        _ => Status.ToString()
    };
}

public sealed class SchemaComparisonResult
{
    public SchemaComparisonResult(IReadOnlyList<TableComparison> tables)
    {
        Tables = tables;
    }

    public IReadOnlyList<TableComparison> Tables { get; }
    public int DifferenceCount => Tables.Count(t => t.Status != TableComparisonStatus.Matching);
}
