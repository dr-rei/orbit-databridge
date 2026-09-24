using Dbms.Core.Abstractions;
using Dbms.Core.Models;

namespace Dbms.Infrastructure.Providers;

public sealed class DatabaseProviderRegistry : IDatabaseProviderRegistry
{
    private readonly IReadOnlyDictionary<DatabaseEngine, IDatabaseProvider> providers;

    public DatabaseProviderRegistry(IEnumerable<IDatabaseProvider> providers)
    {
        var list = providers.ToArray();
        this.providers = list.ToDictionary(provider => provider.Engine);
        Providers = list;
    }

    public IReadOnlyList<IDatabaseProvider> Providers { get; }

    public IDatabaseProvider Get(DatabaseEngine engine) => providers.TryGetValue(engine, out var provider)
        ? provider
        : throw new NotSupportedException($"No database provider is registered for {engine}.");
}
