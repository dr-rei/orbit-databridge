using System.Data.Common;
using Dbms.Core.Models;
using Microsoft.Data.SqlClient;

namespace Dbms.Infrastructure.Providers;

public sealed class SqlServerProvider : RelationalProviderBase
{
    public override DatabaseEngine Engine => DatabaseEngine.SqlServer;
    public override string DisplayName => "SQL Server";

    protected override DbConnection CreateConnection(ConnectionProfile profile)
    {
        var dataSource = string.IsNullOrWhiteSpace(profile.Server) ? "localhost" : profile.Server;
        if (profile.Port > 0 && !dataSource.Contains(',', StringComparison.Ordinal)) dataSource = $"{dataSource},{profile.Port}";
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = dataSource,
            InitialCatalog = profile.Database,
            UserID = profile.Username,
            Password = profile.Password,
            Encrypt = true,
            TrustServerCertificate = true,
            ConnectTimeout = 10
        };
        return new SqlConnection(builder.ConnectionString);
    }

    public override string QuoteIdentifier(string identifier) => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    protected override string LimitClause(int limit) => $" OFFSET 0 ROWS FETCH NEXT {limit} ROWS ONLY";

    protected override string BuildSelectSql(TableDefinition table, IReadOnlyList<string> columns, int? limit)
    {
        var projection = string.Join(", ", columns.Select(QuoteIdentifier));
        var sql = $"SELECT {projection} FROM {QualifiedTableName(table)}";
        return limit is > 0 ? $"{sql} ORDER BY (SELECT NULL){LimitClause(limit.Value)}" : sql;
    }

    protected override bool IsDuplicateKey(Exception exception) => exception is SqlException sql && (sql.Number == 2601 || sql.Number == 2627);

    public override async Task<SchemaSnapshot> ReadSchemaAsync(DbConnection connection, CancellationToken cancellationToken = default)
    {
        var tableNames = new List<(string Schema, string Name)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT s.name, t.name
                FROM sys.tables t
                JOIN sys.schemas s ON s.schema_id = t.schema_id
                WHERE t.is_ms_shipped = 0
                ORDER BY s.name, t.name
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) tableNames.Add((reader.GetString(0), reader.GetString(1)));
        }

        var tables = new List<TableDefinition>();
        foreach (var table in tableNames)
        {
            var columns = await ReadColumnsAsync(connection, table.Schema, table.Name, cancellationToken);
            var keys = await ReadKeysAsync(connection, table.Schema, table.Name, cancellationToken);
            var indexes = await ReadIndexesAsync(connection, table.Schema, table.Name, cancellationToken);
            tables.Add(new TableDefinition(table.Schema, table.Name, columns, keys, indexes));
        }

        return new SchemaSnapshot(connection.Database ?? string.Empty, tables);
    }

    private static async Task<IReadOnlyList<ColumnDefinition>> ReadColumnsAsync(DbConnection connection, string schema, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.name, ty.name, c.is_nullable, c.column_id, c.is_identity, dc.definition
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.columns c ON c.object_id = t.object_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            LEFT JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            WHERE s.name = @schema AND t.name = @table
            ORDER BY c.column_id
            """;
        AddParameter(command, "@schema", schema);
        AddParameter(command, "@table", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new List<ColumnDefinition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(new ColumnDefinition(reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), false, false,
                reader.GetBoolean(4), !reader.IsDBNull(5), reader.GetInt32(3)));
        }

        return columns;
    }

    private static async Task<IReadOnlyList<KeyDefinition>> ReadKeysAsync(DbConnection connection, string schema, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT i.name, CASE WHEN i.is_primary_key = 1 THEN 'PRIMARY KEY' ELSE 'UNIQUE' END,
                   col.name, ic.key_ordinal
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.indexes i ON i.object_id = t.object_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
            WHERE s.name = @schema AND t.name = @table AND (i.is_primary_key = 1 OR i.is_unique_constraint = 1)
            ORDER BY i.name, ic.key_ordinal
            """;
        AddParameter(command, "@schema", schema);
        AddParameter(command, "@table", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<(string Name, string Kind, string Column)>();
        while (await reader.ReadAsync(cancellationToken)) rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        var keys = rows.GroupBy(row => new { row.Name, row.Kind })
            .Select(group => new KeyDefinition(group.Key.Name, group.Key.Kind, group.Select(row => row.Column).ToArray()))
            .ToList();

        await using var foreignCommand = connection.CreateCommand();
        foreignCommand.CommandText = """
            SELECT fk.name, parentColumn.name,
                   referencedSchema.name + '.' + referencedTable.name,
                   referencedColumn.name, fkc.constraint_column_id
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.tables parentTable ON parentTable.object_id = fk.parent_object_id
            JOIN sys.schemas parentSchema ON parentSchema.schema_id = parentTable.schema_id
            JOIN sys.columns parentColumn ON parentColumn.object_id = parentTable.object_id AND parentColumn.column_id = fkc.parent_column_id
            JOIN sys.tables referencedTable ON referencedTable.object_id = fk.referenced_object_id
            JOIN sys.schemas referencedSchema ON referencedSchema.schema_id = referencedTable.schema_id
            JOIN sys.columns referencedColumn ON referencedColumn.object_id = referencedTable.object_id AND referencedColumn.column_id = fkc.referenced_column_id
            WHERE parentSchema.name = @schema AND parentTable.name = @table
            ORDER BY fk.name, fkc.constraint_column_id
            """;
        AddParameter(foreignCommand, "@schema", schema);
        AddParameter(foreignCommand, "@table", table);
        await using var foreignReader = await foreignCommand.ExecuteReaderAsync(cancellationToken);
        var foreignRows = new List<(string Name, string Column, string ReferenceTable, string ReferenceColumn)>();
        while (await foreignReader.ReadAsync(cancellationToken))
        {
            foreignRows.Add((foreignReader.GetString(0), foreignReader.GetString(1), foreignReader.GetString(2), foreignReader.GetString(3)));
        }

        keys.AddRange(foreignRows.GroupBy(row => row.Name).Select(group => new KeyDefinition(
            group.Key,
            "FOREIGN KEY",
            group.Select(row => row.Column).ToArray(),
            group.First().ReferenceTable,
            group.Select(row => row.ReferenceColumn).ToArray())));
        return keys;
    }

    private static async Task<IReadOnlyList<IndexDefinition>> ReadIndexesAsync(DbConnection connection, string schema, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT i.name, i.is_unique, col.name, ic.key_ordinal
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.indexes i ON i.object_id = t.object_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
            WHERE s.name = @schema AND t.name = @table AND i.type > 0
            ORDER BY i.name, ic.key_ordinal
            """;
        AddParameter(command, "@schema", schema);
        AddParameter(command, "@table", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<(string Name, bool Unique, string Column)>();
        while (await reader.ReadAsync(cancellationToken)) rows.Add((reader.GetString(0), reader.GetBoolean(1), reader.GetString(2)));
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
