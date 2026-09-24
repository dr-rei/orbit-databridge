using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.Versioning;
using Dbms.Core.Abstractions;

namespace Dbms.Infrastructure.Storage;

[SupportedOSPlatform("windows")]
public sealed class ProtectedDataCredentialStore : ICredentialStore
{
    private readonly string directory;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    public ProtectedDataCredentialStore()
    {
        directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DbmsTransfer", "credentials");
        Directory.CreateDirectory(directory);
    }

    public async Task SaveAsync(string target, string username, string password, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new CredentialPayload(username, password), jsonOptions);
        var protectedBytes = ProtectedData.Protect(payload, null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(PathFor(target), protectedBytes, cancellationToken);
    }

    public async Task<(string Username, string Password)?> ReadAsync(string target, CancellationToken cancellationToken = default)
    {
        var path = PathFor(target);
        if (!File.Exists(path)) return null;
        var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var payload = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        var credential = JsonSerializer.Deserialize<CredentialPayload>(payload, jsonOptions);
        return credential is null ? null : (credential.Username, credential.Password);
    }

    public Task DeleteAsync(string target, CancellationToken cancellationToken = default)
    {
        var path = PathFor(target);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string PathFor(string target)
    {
        var safeName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(target))).ToLowerInvariant();
        return Path.Combine(directory, $"{safeName}.bin");
    }

    private sealed record CredentialPayload(string Username, string Password);
}
