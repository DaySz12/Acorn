namespace Acorn.Core.Storage;

/// <summary>Locations of the vault and its companion files. Always outside the project folder.</summary>
public sealed class VaultPaths
{
    public const string VaultFileName = "vault.acorn";

    /// <summary>Development/testing override for the data directory. Documented in README.</summary>
    public const string DataDirectoryVariable = "ACORN_DATA_DIR";

    public VaultPaths(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        DataDirectory = Path.GetFullPath(dataDirectory);
        VaultFile = Path.Combine(DataDirectory, VaultFileName);
        TempFile = VaultFile + ".tmp";
        LockFile = VaultFile + ".lock";
        BackupsDirectory = Path.Combine(DataDirectory, "backups");
    }

    public string DataDirectory { get; }
    public string VaultFile { get; }
    public string TempFile { get; }
    public string LockFile { get; }
    public string BackupsDirectory { get; }

    /// <summary>
    /// Windows: %APPDATA%\Acorn. macOS: ~/Library/Application Support/Acorn.
    /// Linux: $XDG_DATA_HOME/Acorn or ~/.local/share/Acorn.
    /// </summary>
    public static VaultPaths ForCurrentUser()
    {
        var overrideDirectory = Environment.GetEnvironmentVariable(DataDirectoryVariable);
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            return new VaultPaths(overrideDirectory);
        }

        // On macOS ApplicationData maps to ~/.config; LocalApplicationData is ~/Library/Application Support
        // (since .NET 8) and on Linux it honours $XDG_DATA_HOME.
        var root = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new VaultPaths(Path.Combine(root, "Acorn"));
    }

    /// <summary>Creates the data and backup directories, owner-only on macOS/Linux.</summary>
    public void EnsureDirectories()
    {
        CreateOwnerOnlyDirectory(DataDirectory);
        CreateOwnerOnlyDirectory(BackupsDirectory);
    }

    private static void CreateOwnerOnlyDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
