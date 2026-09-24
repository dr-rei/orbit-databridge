namespace Dbms.Core.Models;

public sealed class RowSnapshot
{
    public RowSnapshot(IReadOnlyDictionary<string, object?> values)
    {
        Values = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, object?> Values { get; }

    public object? this[string column] => Values.TryGetValue(column, out var value) ? value : null;
}

public enum RowDifferenceKind
{
    SourceOnly,
    DestinationOnly,
    Different
}

public sealed record RowDifference(
    RowDifferenceKind Kind,
    string Key,
    IReadOnlyDictionary<string, object?> SourceValues,
    IReadOnlyDictionary<string, object?> DestinationValues)
{
    public string KindText => Kind switch
    {
        RowDifferenceKind.SourceOnly => "Source only",
        RowDifferenceKind.DestinationOnly => "Destination only",
        RowDifferenceKind.Different => "Different values",
        _ => Kind.ToString()
    };

    public string SourceSummary => FormatValues(SourceValues);
    public string DestinationSummary => FormatValues(DestinationValues);

    private static string FormatValues(IReadOnlyDictionary<string, object?> values)
    {
        if (values.Count == 0)
        {
            return "—";
        }

        return string.Join(", ", values.Take(4).Select(pair => $"{pair.Key}={ValueFormatter.Format(pair.Value)}"));
    }
}

public sealed class DataComparisonResult
{
    public bool IsReliable { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<string> KeyColumns { get; init; } = Array.Empty<string>();
    public long SourceRowCount { get; init; }
    public long DestinationRowCount { get; init; }
    public long SourceOnlyCount { get; init; }
    public long DestinationOnlyCount { get; init; }
    public long DifferentCount { get; init; }
    public bool WasTruncated { get; init; }
    public IReadOnlyList<RowDifference> Differences { get; init; } = Array.Empty<RowDifference>();
}

public sealed record TransferColumnMapping(
    string SourceColumn,
    string DestinationColumn,
    string SourceType,
    string DestinationType);

public sealed class TransferTableSelection
{
    public required TableDefinition SourceTable { get; init; }
    public required TableDefinition DestinationTable { get; init; }
}

public sealed class TransferTablePlan
{
    public required TableDefinition SourceTable { get; init; }
    public required TableDefinition DestinationTable { get; init; }
    public IReadOnlyList<string> KeyColumns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<TransferColumnMapping> Mappings { get; init; } = Array.Empty<TransferColumnMapping>();
    public long SourceRowCount { get; init; }
    public long DestinationRowCount { get; init; }
    public long RowsToInsert { get; init; }
    public long RowsToSkip { get; init; }
    public long ConflictCount { get; init; }
    public IReadOnlyList<RowSnapshot> SampleRowsToInsert { get; init; } = Array.Empty<RowSnapshot>();
    public bool CanExecute { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class TransferPlan
{
    public IReadOnlyList<TransferTablePlan> Tables { get; init; } = Array.Empty<TransferTablePlan>();
    public bool CanExecute => Tables.Count > 0 && Tables.All(table => table.CanExecute);
    public long RowsToInsert => Tables.Sum(table => table.RowsToInsert);
    public long RowsToSkip => Tables.Sum(table => table.RowsToSkip);
    public long Conflicts => Tables.Sum(table => table.ConflictCount);
}

public sealed record TransferProgress(
    string TableName,
    long ProcessedRows,
    long TotalRows,
    long InsertedRows,
    long SkippedRows,
    long FailedRows,
    string Message);

public sealed class TransferResult
{
    public bool Completed { get; init; }
    public bool Cancelled { get; init; }
    public long ProcessedRows { get; init; }
    public long InsertedRows { get; init; }
    public long SkippedRows { get; init; }
    public long FailedRows { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

public static class ValueFormatter
{
    public static string Format(object? value)
    {
        if (value is null || value is DBNull)
        {
            return "NULL";
        }

        if (value is byte[] bytes)
        {
            return $"0x{Convert.ToHexString(bytes.AsSpan(0, Math.Min(bytes.Length, 12)))}{(bytes.Length > 12 ? "…" : string.Empty)}";
        }

        return value switch
        {
            DateTime dateTime => dateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
        };
    }
}
