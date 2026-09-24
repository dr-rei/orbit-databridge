using System.Data;
using System.Data.Common;
using Dbms.Core.Abstractions;
using Dbms.Core.Models;

namespace Dbms.Infrastructure.Providers;

public abstract class RelationalProviderBase : IDatabaseProvider
{
    public abstract DatabaseEngine Engine { get; }
    public abstract string DisplayName { get; }

    public async Task<DbConnection> OpenConnectionAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        var connection = CreateConnection(profile);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public abstract Task<SchemaSnapshot> ReadSchemaAsync(DbConnection connection, CancellationToken cancellationToken = default);

    public async Task<long> CountRowsAsync(DbConnection connection, TableDefinition table, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {QualifiedTableName(table)}";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public async IAsyncEnumerable<RowSnapshot> ReadRowsAsync(
        DbConnection connection,
        TableDefinition table,
        IReadOnlyList<string> columns,
        int? limit = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (columns.Count == 0)
        {
            yield break;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = BuildSelectSql(table, columns, limit);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
            {
                values[reader.GetName(ordinal)] = await reader.IsDBNullAsync(ordinal, cancellationToken)
                    ? null
                    : reader.GetValue(ordinal);
            }

            yield return new RowSnapshot(values);
        }
    }

    public async Task<InsertBatchResult> InsertBatchAsync(
        DbConnection connection,
        TableDefinition table,
        IReadOnlyList<string> destinationColumns,
        IReadOnlyList<RowSnapshot> rows,
        CancellationToken cancellationToken = default)
    {
        if (rows.Count == 0)
        {
            return new InsertBatchResult(0, 0, 0, Array.Empty<string>());
        }

        try
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var inserted = 0L;
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var command = CreateInsertCommand(connection, transaction, table, destinationColumns, row);
                await command.ExecuteNonQueryAsync(cancellationToken);
                inserted++;
            }

            await transaction.CommitAsync(cancellationToken);
            return new InsertBatchResult(inserted, 0, 0, Array.Empty<string>());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception batchException) when (batchException is DbException || batchException is InvalidOperationException)
        {
            // A constraint violation can put PostgreSQL transactions into an aborted state. Retry the bounded batch
            // one row at a time so duplicate keys are visible and non-conflicting rows are still insertable.
            return await InsertRowsIndividuallyAsync(connection, table, destinationColumns, rows, batchException, cancellationToken);
        }
    }

    public abstract string QuoteIdentifier(string identifier);

    protected abstract DbConnection CreateConnection(ConnectionProfile profile);

    protected string QualifiedTableName(TableDefinition table) => string.IsNullOrWhiteSpace(table.Schema)
        ? QuoteIdentifier(table.Name)
        : $"{QuoteIdentifier(table.Schema)}.{QuoteIdentifier(table.Name)}";

    protected virtual string BuildSelectSql(TableDefinition table, IReadOnlyList<string> columns, int? limit)
    {
        var projection = string.Join(", ", columns.Select(QuoteIdentifier));
        var sql = $"SELECT {projection} FROM {QualifiedTableName(table)}";
        return limit is > 0 ? $"{sql}{LimitClause(limit.Value)}" : sql;
    }

    protected virtual string LimitClause(int limit) => $" LIMIT {limit}";

    protected virtual bool IsDuplicateKey(Exception exception) => false;

    private DbCommand CreateInsertCommand(
        DbConnection connection,
        DbTransaction transaction,
        TableDefinition table,
        IReadOnlyList<string> destinationColumns,
        RowSnapshot row)
    {
        var parameters = destinationColumns.Select((_, index) => $"@p{index}").ToArray();
        var columns = string.Join(", ", destinationColumns.Select(QuoteIdentifier));
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"INSERT INTO {QualifiedTableName(table)} ({columns}) VALUES ({string.Join(", ", parameters)})";

        for (var index = 0; index < destinationColumns.Count; index++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = parameters[index];
            parameter.Value = row[destinationColumns[index]] ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        return command;
    }

    private async Task<InsertBatchResult> InsertRowsIndividuallyAsync(
        DbConnection connection,
        TableDefinition table,
        IReadOnlyList<string> destinationColumns,
        IReadOnlyList<RowSnapshot> rows,
        Exception batchException,
        CancellationToken cancellationToken)
    {
        var inserted = 0L;
        var skipped = 0L;
        var failed = 0L;
        var errors = new List<string>();

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                await using var command = CreateInsertCommand(connection, transaction, table, destinationColumns, row);
                await command.ExecuteNonQueryAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                inserted++;
            }
            catch (Exception exception) when (exception is DbException || exception is InvalidOperationException)
            {
                if (IsDuplicateKey(exception))
                {
                    skipped++;
                    continue;
                }

                failed++;
                errors.Add(SanitizeError(exception));
            }
        }

        if (inserted == 0 && skipped == 0 && failed == 0)
        {
            errors.Add(SanitizeError(batchException));
        }

        return new InsertBatchResult(inserted, skipped, failed, errors);
    }

    private static string SanitizeError(Exception exception)
    {
        var message = System.Text.RegularExpressions.Regex.Replace(
            exception.Message,
            "(?i)(password|pwd)\\s*=\\s*[^;\\s]+",
            "$1=***");
        return string.IsNullOrWhiteSpace(message) ? exception.GetType().Name : message;
    }
}
