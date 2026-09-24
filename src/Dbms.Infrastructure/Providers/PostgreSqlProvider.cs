using System.Data.Common;
using Dbms.Core.Models;
using Npgsql;

namespace Dbms.Infrastructure.Providers;

public sealed class PostgreSqlProvider : RelationalProviderBase
{
    public override DatabaseEngine Engine => DatabaseEngine.PostgreSql;
    public override string DisplayName => "PostgreSQL";

    protected override DbConnection CreateConnection(ConnectionProfile profile)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = string.IsNullOrWhiteSpace(profile.Server) ? "localhost" : profile.Server,
            Port = profile.Port > 0 ? profile.Port : 5432,
            Database = profile.Database,
            Username = profile.Username,
            Password = profile.Password,
            ApplicationName = "Orbit DataBridge"
        };
        return new NpgsqlConnection(builder.ConnectionString);
    }

    public override string QuoteIdentifier(string identifier) => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    public override async Task<SchemaSnapshot> ReadSchemaAsync(DbConnection connection, CancellationToken cancellationToken = default)
    {
        var tableNames = new List<(string Schema, string Name)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT table_schema, table_name
                FROM information_schema.tables
                WHERE table_type = 'BASE TABLE'
                  AND table_schema NOT IN ('pg_catalog', 'information_schema')
                ORDER BY table_schema, table_name
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                tableNames.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        var tables = new List<TableDefinition>();
        foreach (var table in tableNames)
        {
            var columns = await ReadColumnsAsync(connection, table.Schema, table.Name, cancellationToken);
            var keys = await ReadKeysAsync(connection, table.Schema, table.Name, cancellationToken);
            var indexes = await ReadIndexesAsync(connection, table.Schema, table.Name, cancellationToken);
            tables.Add(new TableDefinition(table.Schema, table.Name, columns, keys, indexes));
        }

        var databaseName = connection.Database ?? string.Empty;
        return new SchemaSnapshot(databaseName, tables);
    }

    protected override bool IsDuplicateKey(Exception exception) => exception is PostgresException postgres && postgres.SqlState == PostgresErrorCodes.UniqueViolation;

    private static async Task<IReadOnlyList<ColumnDefinition>> ReadColumnsAsync(DbConnection connection, string schema, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT column_name, data_type, is_nullable, ordinal_position,
                   column_default, is_identity, is_generated
            FROM information_schema.columns
            WHERE table_schema = @schema AND table_name = @table
            ORDER BY ordinal_position
            """;
        AddParameter(command, "@schema", schema);
        AddParameter(command, "@table", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new List<ColumnDefinition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(new ColumnDefinition(
                reader.GetString(0),
                reader.GetString(1),
                string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase),
                false,
                false,
                string.Equals(reader.GetString(6), "ALWAYS", StringComparison.OrdinalIgnoreCase) || reader.GetString(5) == "YES",
                !reader.IsDBNull(4),
                reader.GetInt32(3)));
        }

        return columns;
    }

    private static async Task<IReadOnlyList<KeyDefinition>> ReadKeysAsync(DbConnection connection, string schema, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT tc.constraint_name, tc.constraint_type, kcu.column_name,
                   ccu.table_schema, ccu.table_name, ccu.column_name
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
              ON tc.constraint_schema = kcu.constraint_schema
             AND tc.constraint_name = kcu.constraint_name
             AND tc.table_name = kcu.table_name
            LEFT JOIN information_schema.constraint_column_usage ccu
              ON tc.constraint_schema = ccu.constraint_schema
             AND tc.constraint_name = ccu.constraint_name
            WHERE tc.table_schema = @schema AND tc.table_name = @table
              AND tc.constraint_type IN ('PRIMARY KEY', 'UNIQUE', 'FOREIGN KEY')
            ORDER BY tc.constraint_name, kcu.ordinal_position
            """;
        AddParameter(command, "@schema", schema);
        AddParameter(command, "@table", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var keyRows = new List<(string Name, string Kind, string Column, string? RefSchema, string? RefTable, string? RefColumn)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            keyRows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return keyRows.GroupBy(row => new { row.Name, row.Kind })
            .Select(group => new KeyDefinition(
                group.Key.Name,
                group.Key.Kind,
                group.Select(row => row.Column).ToArray(),
                group.First().RefTable is null ? null : $"{group.First().RefSchema}.{group.First().RefTable}",
                group.Where(row => row.RefColumn is not null).Select(row => row.RefColumn!).ToArray()))
            .ToArray();
    }

    private static async Task<IReadOnlyList<IndexDefinition>> ReadIndexesAsync(DbConnection connection, string schema, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT indexname, indexdef
            FROM pg_indexes
            WHERE schemaname = @schema AND tablename = @table
            ORDER BY indexname
            """;
        AddParameter(command, "@schema", schema);
        AddParameter(command, "@table", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var indexes = new List<IndexDefinition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(0);
            var definition = reader.GetString(1);
            var isUnique = definition.Contains("CREATE UNIQUE INDEX", StringComparison.OrdinalIgnoreCase);
            indexes.Add(new IndexDefinition(name, isUnique, Array.Empty<string>()));
        }

        return indexes;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
