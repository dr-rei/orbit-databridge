namespace Dbms.Core.Models;

public enum DatabaseEngine
{
    PostgreSql,
    MySql,
    SqlServer,
    Sqlite
}

public sealed class ConnectionProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New connection";
    public DatabaseEngine Engine { get; set; } = DatabaseEngine.PostgreSql;
    public string Server { get; set; } = "localhost";
    public int Port { get; set; }
    public string Database { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;

    // Password is intentionally kept out of the profile JSON by ProfileStore.
    // It is populated only for the lifetime of an operation or an unsaved draft.
    public string Password { get; set; } = string.Empty;
    public bool SavePassword { get; set; }

    // Used as the target name in the operating system credential vault.
    public string CredentialTarget => $"DbmsTransfer/{Id:N}";

    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? $"{Engine} / {Database}"
        : Name;

    public ConnectionProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        Engine = Engine,
        Server = Server,
        Port = Port,
        Database = Database,
        Username = Username,
        Password = Password,
        SavePassword = SavePassword
    };
}
