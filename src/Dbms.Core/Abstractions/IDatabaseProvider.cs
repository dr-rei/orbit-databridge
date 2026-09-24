using System.Data.Common;
using Dbms.Core.Models;

namespace Dbms.Core.Abstractions;

public interface IDatabaseProvider
{
    DatabaseEngine Engine { get; }
    string DisplayName { get; }

    Task<DbConnection> OpenConnectionAsync(ConnectionProfile profile, CancellationToken cancellationToken = default);
    Task<SchemaSnapshot> ReadSchemaAsync(DbConnection connection, CancellationToken cancellationToken = default);
    Task<long> CountRowsAsync(DbConnection connection, TableDefinition table, CancellationToken cancellationToken = default);

    IAsyncEnumerable<RowSnapshot> ReadRowsAsync(
        DbConnection connection,
        TableDefinition table,
        IReadOnlyList<string> columns,
        int? limit = null,
        CancellationToken cancellationToken = default);

    Task<InsertBatchResult> InsertBatchAsync(
        DbConnection connection,
        TableDefinition table,
        IReadOnlyList<string> destinationColumns,
        IReadOnlyList<RowSnapshot> rows,
        CancellationToken cancellationToken = default);

    string QuoteIdentifier(string identifier);
}

public sealed record InsertBatchResult(
    long InsertedRows,
    long SkippedRows,
    long FailedRows,
    IReadOnlyList<string> Errors);

public interface IDatabaseProviderRegistry
{
    IReadOnlyList<IDatabaseProvider> Providers { get; }
    IDatabaseProvider Get(DatabaseEngine engine);
}

public interface ICredentialStore
{
    Task SaveAsync(string target, string username, string password, CancellationToken cancellationToken = default);
    Task<(string Username, string Password)?> ReadAsync(string target, CancellationToken cancellationToken = default);
    Task DeleteAsync(string target, CancellationToken cancellationToken = default);
}

public interface IProfileStore
{
    Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
