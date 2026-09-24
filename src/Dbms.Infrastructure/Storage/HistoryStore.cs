using System.Text.Json;
using Dbms.Core.Models;

namespace Dbms.Infrastructure.Storage;

public sealed class HistoryStore
{
    private readonly string filePath;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public HistoryStore()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DbmsTransfer");
        Directory.CreateDirectory(directory);
        filePath = Path.Combine(directory, "history.json");
    }

    public async Task<IReadOnlyList<JobHistoryEntry>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath)) return Array.Empty<JobHistoryEntry>();
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        return JsonSerializer.Deserialize<List<JobHistoryEntry>>(json, jsonOptions) ?? new List<JobHistoryEntry>();
    }

    public async Task AddAsync(JobHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        var entries = (await LoadAsync(cancellationToken)).Take(99).ToList();
        entries.Insert(0, entry);
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(entries, jsonOptions), cancellationToken);
    }
}
