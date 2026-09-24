using Dbms.Core.Models;
using Dbms.Core.Services;
using Dbms.Infrastructure.Providers;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dbms.Core.Tests;

public sealed class SqliteTransferTests
{
    [Fact]
    public async Task Preview_and_transfer_insert_missing_rows_without_overwriting_destination_data()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dbms-transfer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.db");
        var destinationPath = Path.Combine(directory, "destination.db");
        try
        {
            await CreateDatabaseAsync(sourcePath, new[] { (1, "source-one"), (2, "source-two") });
            await CreateDatabaseAsync(destinationPath, new[] { (2, "destination-two"), (3, "destination-only") });

            var sourceProfile = new ConnectionProfile { Name = "source", Engine = DatabaseEngine.Sqlite, Database = sourcePath };
            var destinationProfile = new ConnectionProfile { Name = "destination", Engine = DatabaseEngine.Sqlite, Database = destinationPath };
            var provider = new SqliteProvider();
            IReadOnlyList<(int Id, string Name)> rows;

            {
                await using var sourceConnection = await provider.OpenConnectionAsync(sourceProfile);
                await using var destinationConnection = await provider.OpenConnectionAsync(destinationProfile);
                var sourceTable = Assert.Single((await provider.ReadSchemaAsync(sourceConnection)).Tables);
                var destinationTable = Assert.Single((await provider.ReadSchemaAsync(destinationConnection)).Tables);

                var service = new TransferService();
                var plan = await service.BuildPlanAsync(provider, sourceProfile, provider, destinationProfile,
                    new[] { new TransferTableSelection { SourceTable = sourceTable, DestinationTable = destinationTable } });

                var tablePlan = Assert.Single(plan.Tables);
                Assert.True(plan.CanExecute);
                Assert.Equal(1, tablePlan.RowsToInsert);
                Assert.Equal(1, tablePlan.RowsToSkip);

                var result = await service.ExecuteAsync(provider, sourceProfile, provider, destinationProfile, plan);

                Assert.True(result.Completed);
                Assert.Equal(1, result.InsertedRows);
                Assert.Equal(1, result.SkippedRows);
            }
            rows = await ReadItemsAsync(destinationPath);
            Assert.Equal(new[] { (1, "source-one"), (2, "destination-two"), (3, "destination-only") }, rows);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Transfer_plan_blocks_a_keyless_table()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dbms-transfer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.db");
        var destinationPath = Path.Combine(directory, "destination.db");
        try
        {
            await CreateKeylessDatabaseAsync(sourcePath);
            await CreateKeylessDatabaseAsync(destinationPath);
            var sourceProfile = new ConnectionProfile { Engine = DatabaseEngine.Sqlite, Database = sourcePath };
            var destinationProfile = new ConnectionProfile { Engine = DatabaseEngine.Sqlite, Database = destinationPath };
            var provider = new SqliteProvider();
            {
                await using var sourceConnection = await provider.OpenConnectionAsync(sourceProfile);
                await using var destinationConnection = await provider.OpenConnectionAsync(destinationProfile);
                var sourceTable = Assert.Single((await provider.ReadSchemaAsync(sourceConnection)).Tables);
                var destinationTable = Assert.Single((await provider.ReadSchemaAsync(destinationConnection)).Tables);

                var plan = await new TransferService().BuildPlanAsync(provider, sourceProfile, provider, destinationProfile,
                    new[] { new TransferTableSelection { SourceTable = sourceTable, DestinationTable = destinationTable } });

                Assert.False(plan.CanExecute);
                Assert.Contains("No primary key", Assert.Single(plan.Tables).Message, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task CreateDatabaseAsync(string path, IReadOnlyList<(int Id, string Name)> rows)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Foreign Keys=True");
        await connection.OpenAsync();
        await ExecuteAsync(connection, "CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT NOT NULL)");
        foreach (var row in rows)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO items (id, name) VALUES ($id, $name)";
            command.Parameters.AddWithValue("$id", row.Id);
            command.Parameters.AddWithValue("$name", row.Name);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task CreateKeylessDatabaseAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Foreign Keys=True");
        await connection.OpenAsync();
        await ExecuteAsync(connection, "CREATE TABLE events (message TEXT)");
        await ExecuteAsync(connection, "INSERT INTO events (message) VALUES ('hello')");
    }

    private static async Task<List<(int Id, string Name)>> ReadItemsAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Foreign Keys=True");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name FROM items ORDER BY id";
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(int, string)>();
        while (await reader.ReadAsync()) rows.Add((reader.GetInt32(0), reader.GetString(1)));
        return rows;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
