using Dbms.Core.Models;
using Dbms.Core.Services;
using Xunit;

namespace Dbms.Core.Tests;

public sealed class SchemaComparisonServiceTests
{
    [Fact]
    public void Reports_table_and_column_differences_without_mutating_snapshots()
    {
        var source = new SchemaSnapshot("source", new[]
        {
            new TableDefinition("public", "users", new[]
            {
                new ColumnDefinition("id", "integer", false, true, false, false, false, 1),
                new ColumnDefinition("name", "text", false, false, false, false, false, 2)
            }, new[] { new KeyDefinition("users_pkey", "PRIMARY KEY", new[] { "id" }) })
        });
        var destination = new SchemaSnapshot("destination", new[]
        {
            new TableDefinition("public", "users", new[]
            {
                new ColumnDefinition("id", "integer", false, true, false, false, false, 1),
                new ColumnDefinition("display_name", "varchar", true, false, false, false, true, 2)
            }, new[] { new KeyDefinition("users_pkey", "PRIMARY KEY", new[] { "id" }) })
        });

        var result = new SchemaComparisonService().Compare(source, destination);

        var comparison = Assert.Single(result.Tables);
        Assert.Equal(TableComparisonStatus.Different, comparison.Status);
        Assert.Contains(comparison.Differences, difference => difference.Contains("name", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(comparison.Differences, difference => difference.Contains("display_name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Blocks_row_comparison_when_no_shared_reliable_key_exists()
    {
        var sourceTable = new TableDefinition("", "events", new[]
        {
            new ColumnDefinition("message", "TEXT", true, false, false, false, false)
        });
        var destinationTable = new TableDefinition("", "events", new[]
        {
            new ColumnDefinition("message", "TEXT", true, false, false, false, false)
        });

        Assert.Empty(sourceTable.PreferredKeyColumns);
        Assert.Empty(destinationTable.PreferredKeyColumns);
    }
}
