using System.Text.Json;
using Dbms.Core.Abstractions;
using Dbms.Core.Models;

namespace Dbms.Infrastructure.Storage;

public sealed class FileProfileStore : IProfileStore
{
    private readonly ICredentialStore credentialStore;
    private readonly string filePath;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public FileProfileStore(ICredentialStore credentialStore)
    {
        this.credentialStore = credentialStore;
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DbmsTransfer");
        Directory.CreateDirectory(directory);
        filePath = Path.Combine(directory, "profiles.json");
    }

    public async Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath)) return Array.Empty<ConnectionProfile>();
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var stored = JsonSerializer.Deserialize<List<StoredProfile>>(json, jsonOptions) ?? new List<StoredProfile>();
        var profiles = new List<ConnectionProfile>();
        foreach (var profile in stored)
        {
            var result = new ConnectionProfile
            {
                Id = profile.Id,
                Name = profile.Name,
                Engine = profile.Engine,
                Server = profile.Server,
                Port = profile.Port,
                Database = profile.Database,
                Username = profile.Username,
                SavePassword = profile.SavePassword
            };

            if (profile.SavePassword)
            {
                try
                {
                    var credential = await credentialStore.ReadAsync(result.CredentialTarget, cancellationToken);
                    if (credential is not null)
                    {
                        result.Username = credential.Value.Username;
                        result.Password = credential.Value.Password;
                    }
                }
                catch
                {
                    // A profile remains usable with a blank password if the user vault is unavailable.
                }
            }

            profiles.Add(result);
        }

        return profiles;
    }

    public async Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        var profiles = (await LoadStoredAsync(cancellationToken)).ToList();
        var stored = StoredProfile.From(profile);
        var existingIndex = profiles.FindIndex(item => item.Id == profile.Id);
        if (existingIndex >= 0) profiles[existingIndex] = stored;
        else profiles.Add(stored);
        await SaveStoredAsync(profiles, cancellationToken);

        if (profile.SavePassword && !string.IsNullOrEmpty(profile.Password))
        {
            await credentialStore.SaveAsync(profile.CredentialTarget, profile.Username, profile.Password, cancellationToken);
        }
        else
        {
            await credentialStore.DeleteAsync(profile.CredentialTarget, cancellationToken);
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var profiles = (await LoadStoredAsync(cancellationToken)).Where(item => item.Id != id).ToList();
        await SaveStoredAsync(profiles, cancellationToken);
        await credentialStore.DeleteAsync($"DbmsTransfer/{id:N}", cancellationToken);
    }

    private async Task<List<StoredProfile>> LoadStoredAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath)) return new List<StoredProfile>();
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        return JsonSerializer.Deserialize<List<StoredProfile>>(json, jsonOptions) ?? new List<StoredProfile>();
    }

    private async Task SaveStoredAsync(List<StoredProfile> profiles, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(profiles, jsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    private sealed class StoredProfile
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DatabaseEngine Engine { get; set; }
        public string Server { get; set; } = string.Empty;
        public int Port { get; set; }
        public string Database { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public bool SavePassword { get; set; }

        public static StoredProfile From(ConnectionProfile profile) => new()
        {
            Id = profile.Id,
            Name = profile.Name,
            Engine = profile.Engine,
            Server = profile.Server,
            Port = profile.Port,
            Database = profile.Database,
            Username = profile.Username,
            SavePassword = profile.SavePassword
        };
    }
}
