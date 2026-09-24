using System.Data.Common;
using Dbms.Core.Models;
using Microsoft.Data.Sqlite;

namespace Dbms.Infrastructure.Providers;

public sealed class SqliteProvider : RelationalProviderBase
{
    public override DatabaseEngine Engine => DatabaseEngine.Sqlite;
    public override string DisplayName => "SQLite";

    protected override DbConnection CreateConnection(ConnectionProfile profile)
    {
        var path = string.IsNullOrWhiteSpace(profile.Database) ? profile.Server : profile.Database;
        if (string.IsNullOrWhiteSpace(path)) path = "dbms.sqlite";
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        };
        return new SqliteConnection(builder.ConnectionString);
    }

    public override string QuoteIdentifier(string identifier) => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    public override async Task<SchemaSnapshot> ReadSchemaAsync(DbConnection connection, CancellationToken cancellationToken = default)
    {
        var tableNames = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) tableNames.Add(reader.GetString(0));
        }

        var tables = new List<TableDefinition>();
        foreach (var table in tableNames)
        {
            var columns = await ReadColumnsAsync(connection, table, cancellationToken);
            var keys = await ReadKeysAsync(connection, table, columns, cancellationToken);
            var indexes = await ReadIndexesAsync(connection, table, cancellationToken);
            tables.Add(new TableDefinition(string.Empty, table, columns, keys, indexes));
        }

        return new SchemaSnapshot(connection.Database ?? string.Empty, tables);
    }

    protected override bool IsDuplicateKey(Exception exception) => exception is SqliteException sqlite && sqlite.SqliteErrorCode == 19;

    private async Task<IReadOnlyList<ColumnDefinition>> ReadColumnsAsync(DbConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({QuoteIdentifier(table)})";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new List<ColumnDefinition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var defaultValue = reader.IsDBNull(4) ? null : reader.GetValue(4)?.ToString();
            columns.Add(new ColumnDefinition(reader.GetString(1), reader.GetString(2), reader.GetInt32(3) == 0,
                reader.GetInt32(5) > 0, false, false, defaultValue is not null, reader.GetInt32(0)));
        }

        return columns;
    }

    private async Task<IReadOnlyList<KeyDefinition>> ReadKeysAsync(DbConnection connection, string table, IReadOnlyList<ColumnDefinition> columns, CancellationToken cancellationToken)
    {
        var keys = new List<KeyDefinition>();
        var primaryColumns = columns.Where(column => column.IsPrimaryKey).OrderBy(column => column.Ordinal).Select(column => column.Name).ToArray();
        if (primaryColumns.Length > 0) keys.Add(new KeyDefinition("PRIMARY KEY", "PRIMARY KEY", primaryColumns));

        var uniqueIndexNames = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA index_list({QuoteIdentifier(table)})";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetInt32(2) == 1) uniqueIndexNames.Add(reader.GetString(1));
            }
        }

        foreach (var indexName in uniqueIndexNames)
        {
            var indexColumns = await ReadIndexColumnsAsync(connection, indexName, cancellationToken);
            keys.Add(new KeyDefinition(indexName, "UNIQUE", indexColumns));
        }

        await using var foreignCommand = connection.CreateCommand();
        foreignCommand.CommandText = $"PRAGMA foreign_key_list({QuoteIdentifier(table)})";
        await using var foreignReader = await foreignCommand.ExecuteReaderAsync(cancellationToken);
        while (await foreignReader.ReadAsync(cancellationToken))
        {
            keys.Add(new KeyDefinition($"FK_{foreignReader.GetInt32(0)}", "FOREIGN KEY",
                new[] { foreignReader.GetString(3) }, foreignReader.GetString(2), new[] { foreignReader.GetString(4) }));
        }

        return keys;
    }

    private async Task<IReadOnlyList<IndexDefinition>> ReadIndexesAsync(DbConnection connection, string table, CancellationToken cancellationToken)
    {
        var indexes = new List<IndexDefinition>();
        var indexNames = new List<(string Name, bool Unique)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA index_list({QuoteIdentifier(table)})";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) indexNames.Add((reader.GetString(1), reader.GetInt32(2) == 1));
        }

        foreach (var (name, unique) in indexNames)
            indexes.Add(new IndexDefinition(name, unique, await ReadIndexColumnsAsync(connection, name, cancellationToken)));

        return indexes;
    }

    private async Task<IReadOnlyList<string>> ReadIndexColumnsAsync(DbConnection connection, string indexName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA index_info({QuoteIdentifier(indexName)})";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new List<(int Sequence, string Name)>();
        while (await reader.ReadAsync(cancellationToken)) columns.Add((reader.GetInt32(0), reader.GetString(2)));
        return columns.OrderBy(column => column.Sequence).Select(column => column.Name).ToArray();
    }
}
