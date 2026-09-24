using System.Data.Common;
using Dbms.Core.Models;
using MySqlConnector;

namespace Dbms.Infrastructure.Providers;

public sealed class MySqlProvider : RelationalProviderBase
{
    public override DatabaseEngine Engine => DatabaseEngine.MySql;
    public override string DisplayName => "MySQL / MariaDB";

    protected override DbConnection CreateConnection(ConnectionProfile profile)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = string.IsNullOrWhiteSpace(profile.Server) ? "localhost" : profile.Server,
            Port = (uint)(profile.Port > 0 ? profile.Port : 3306),
            Database = profile.Database,
            UserID = profile.Username,
            Password = profile.Password,
            AllowUserVariables = false
        };
        return new MySqlConnection(builder.ConnectionString);
    }

    public override string QuoteIdentifier(string identifier) => $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`";

    protected override bool IsDuplicateKey(Exception exception) => exception is MySqlException mySql && (mySql.Number == 1062 || mySql.Number == 1022);

    public override async Task<SchemaSnapshot> ReadSchemaAsync(DbConnection connection, CancellationToken cancellationToken = default)
    {
        var tableNames = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT TABLE_NAME
                FROM information_schema.tables
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE'
                ORDER BY TABLE_NAME
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) tableNames.Add(reader.GetString(0));
        }

        var tables = new List<TableDefinition>();
        foreach (var table in tableNames)
        {
            var columns = await ReadColumnsAsync(connection, table, cancellationToken);
            var keys = await ReadKeysAsync(connection, table, cancellationToken);
            var indexes = await ReadIndexesAsync(connection, table, cancellationToken);
            tables.Add(new TableDefinition(connection.Database ?? string.Empty, table, columns, keys, indexes));
        }

        return new SchemaSnapshot(connection.Database ?? string.Empty, tables);
    }

    private static async Task<IReadOnlyList<ColumnDefinition>> ReadColumnsAsync(DbConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_KEY, EXTRA, COLUMN_DEFAULT, ORDINAL_POSITION
            FROM information_schema.columns
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table
            ORDER BY ORDINAL_POSITION
            """;
        AddParameter(command, "@table", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new List<ColumnDefinition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = reader.GetString(3);
            var extra = reader.GetString(4);
            columns.Add(new ColumnDefinition(
                reader.GetString(0),
                reader.GetString(1),
                string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase),
                string.Equals(key, "PRI", StringComparison.OrdinalIgnoreCase),
                string.Equals(key, "UNI", StringComparison.OrdinalIgnoreCase),
                extra.Contains("auto_increment", StringComparison.OrdinalIgnoreCase) || extra.Contains("GENERATED", StringComparison.OrdinalIgnoreCase),
                !reader.IsDBNull(5),
                reader.GetInt32(6)));
        }

        return columns;
    }

    private static async Task<IReadOnlyList<KeyDefinition>> ReadKeysAsync(DbConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT kcu.CONSTRAINT_NAME, tc.CONSTRAINT_TYPE, kcu.COLUMN_NAME,
                   kcu.REFERENCED_TABLE_NAME, kcu.REFERENCED_COLUMN_NAME
            FROM information_schema.KEY_COLUMN_USAGE kcu
            JOIN information_schema.TABLE_CONSTRAINTS tc
              ON tc.CONSTRAINT_SCHEMA = kcu.CONSTRAINT_SCHEMA
             AND tc.TABLE_NAME = kcu.TABLE_NAME
             AND tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
            WHERE kcu.CONSTRAINT_SCHEMA = DATABASE() AND kcu.TABLE_NAME = @table
              AND tc.CONSTRAINT_TYPE IN ('PRIMARY KEY', 'UNIQUE', 'FOREIGN KEY')
            ORDER BY kcu.CONSTRAINT_NAME, kcu.ORDINAL_POSITION
            """;
        AddParameter(command, "@table", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<(string Name, string Kind, string Column, string? RefTable, string? RefColumn)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return rows.GroupBy(row => new { row.Name, row.Kind })
            .Select(group => new KeyDefinition(group.Key.Name, group.Key.Kind,
                group.Select(row => row.Column).ToArray(), group.First().RefTable,
                group.Where(row => row.RefColumn is not null).Select(row => row.RefColumn!).ToArray()))
            .ToArray();
    }

    private static async Task<IReadOnlyList<IndexDefinition>> ReadIndexesAsync(DbConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT INDEX_NAME, NON_UNIQUE, COLUMN_NAME
            FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table
            ORDER BY INDEX_NAME, SEQ_IN_INDEX
            """;
        AddParameter(command, "@table", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<(string Name, bool Unique, string Column)>();
        while (await reader.ReadAsync(cancellationToken)) rows.Add((reader.GetString(0), reader.GetInt32(1) == 0, reader.GetString(2)));
        return rows.GroupBy(row => new { row.Name, row.Unique })
            .Select(group => new IndexDefinition(group.Key.Name, group.Key.Unique, group.Select(row => row.Column).ToArray()))
            .ToArray();
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
