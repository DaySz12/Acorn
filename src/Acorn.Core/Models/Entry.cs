namespace Acorn.Core.Models;

public static class EntryTypes
{
    public const string Server = "server";
    public const string Login = "login";
    public const string Note = "note";

    public static IReadOnlyList<string> All { get; } = [Server, Login, Note];

    public static bool IsValid(string? type) => type is Server or Login or Note;
}

/// <summary>One stored item: a server, a login or a secure note.</summary>
public sealed class Entry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = EntryTypes.Server;
    public string Name { get; set; } = "";
    public string Env { get; set; } = "";
    public string Host { get; set; } = "";
    public int? Port { get; set; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    /// <summary>Path to a private key on disk. The key itself is never stored in the vault.</summary>
    public string SshKeyPath { get; set; } = "";

    public List<string> Tags { get; set; } = [];
    public List<CustomField> CustomFields { get; set; } = [];
    public string Notes { get; set; } = "";
    public bool Favorite { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Entry Clone() => new()
    {
        Id = Id,
        Type = Type,
        Name = Name,
        Env = Env,
        Host = Host,
        Port = Port,
        Username = Username,
        Password = Password,
        SshKeyPath = SshKeyPath,
        Tags = [.. Tags],
        CustomFields = CustomFields.Select(f => f.Clone()).ToList(),
        Notes = Notes,
        Favorite = Favorite,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
    };
}

public sealed class CustomField
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
    public bool IsSecret { get; set; }

    public CustomField Clone() => new() { Id = Id, Label = Label, Value = Value, IsSecret = IsSecret };
}
