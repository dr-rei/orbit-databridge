namespace Dbms.Core.Models;

public sealed record JobHistoryEntry(
    DateTimeOffset StartedAt,
    string Source,
    string Destination,
    string Tables,
    long InsertedRows,
    long SkippedRows,
    long FailedRows,
    bool Completed,
    string Message);
