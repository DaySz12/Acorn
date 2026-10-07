namespace Acorn.Application.ViewModels;

public enum EntryFilterKind
{
    All,
    Favorites,
    Environment,
    Tag,
}

public sealed record EntryFilter(EntryFilterKind Kind, string? Value)
{
    public static EntryFilter All { get; } = new(EntryFilterKind.All, null);
    public static EntryFilter Favorites { get; } = new(EntryFilterKind.Favorites, null);

    public static EntryFilter ForEnvironment(string env) => new(EntryFilterKind.Environment, env);

    public static EntryFilter ForTag(string tag) => new(EntryFilterKind.Tag, tag);
}

/// <summary>Identifies one secret value: an entry's password, or one of its secret custom fields.</summary>
public readonly record struct SecretRef(Guid EntryId, Guid? CustomFieldId)
{
    public static SecretRef Password(Guid entryId) => new(entryId, null);

    public static SecretRef Field(Guid entryId, Guid fieldId) => new(entryId, fieldId);
}

/// <summary>
/// A row in the entry list. Deliberately has no password value, only <see cref="HasPassword"/>
/// (SPEC 3.1). Enforced by a test.
/// </summary>
public sealed record EntryListItemViewModel(
    Guid Id,
    string Type,
    string Name,
    string Env,
    string Host,
    int? Port,
    string Username,
    bool HasPassword,
    IReadOnlyList<string> Tags,
    bool Favorite,
    DateTime UpdatedAt);

public sealed record EntryListViewModel(IReadOnlyList<EntryListItemViewModel> Items, int TotalCount)
{
    public static EntryListViewModel Empty { get; } = new([], 0);
}

/// <summary>A custom field for display. <see cref="Value"/> is null when the field is secret.</summary>
public sealed record CustomFieldViewModel(Guid Id, string Label, bool IsSecret, string? Value, bool HasValue);

/// <summary>Details pane. No secret values; they are fetched one at a time via RevealSecretAsync.</summary>
public sealed record EntryDetailViewModel(
    Guid Id,
    string Type,
    string Name,
    string Env,
    string Host,
    int? Port,
    string Username,
    bool HasPassword,
    string SshKeyPath,
    string? SshCommand,
    IReadOnlyList<string> Tags,
    IReadOnlyList<CustomFieldViewModel> CustomFields,
    string Notes,
    bool Favorite,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>
/// Editable form for an entry. Existing secrets are not loaded into it: an empty
/// <see cref="NewPassword"/> means "keep the stored password".
/// </summary>
public sealed class EntryEditViewModel
{
    public Guid? Id { get; init; }
    public bool IsNew => Id is null;
    public string Type { get; set; } = "server";
    public string Name { get; set; } = "";
    public string Env { get; set; } = "";
    public string Host { get; set; } = "";
    public string PortText { get; set; } = "";
    public string Username { get; set; } = "";
    public bool HasExistingPassword { get; init; }
    public string NewPassword { get; set; } = "";
    public bool RemovePassword { get; set; }
    public string SshKeyPath { get; set; } = "";
    public string TagsText { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool Favorite { get; set; }
    public List<CustomFieldEditViewModel> CustomFields { get; init; } = [];
    public IReadOnlyList<string> AvailableEnvironments { get; init; } = [];
    public IReadOnlyList<string> AvailableTypes { get; init; } = [];
    public IReadOnlyList<string> Errors { get; internal set; } = [];
}

public sealed class CustomFieldEditViewModel
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Label { get; set; } = "";
    public bool IsSecret { get; set; }

    /// <summary>For an existing secret field, empty means "keep the stored value".</summary>
    public string Value { get; set; } = "";

    public bool HasExistingSecret { get; init; }
}

public sealed record SidebarItem(string Name, int Count);

public sealed record SidebarViewModel(
    int AllCount,
    int FavoriteCount,
    IReadOnlyList<SidebarItem> Environments,
    IReadOnlyList<SidebarItem> Tags)
{
    public static SidebarViewModel Empty { get; } = new(0, 0, [], []);
}

public enum CopyField
{
    Host,
    Username,
    Password,
    SshCommand,
    CustomField,
}

/// <summary>Options for the password generator panel. Not secret.</summary>
public sealed class PasswordGeneratorViewModel
{
    public const int MinLength = 8;
    public const int MaxLength = 64;

    public int Length { get; set; } = 20;
    public bool Lowercase { get; set; } = true;
    public bool Uppercase { get; set; } = true;
    public bool Digits { get; set; } = true;
    public bool Symbols { get; set; } = true;
    public bool ExcludeAmbiguous { get; set; } = true;
}