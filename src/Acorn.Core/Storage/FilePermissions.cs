namespace Acorn.Core.Storage;

internal static class FilePermissions
{
    public const UnixFileMode OwnerReadWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>chmod 600 on macOS/Linux. On Windows the user-profile ACL already restricts access.</summary>
    public static void RestrictToOwner(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, OwnerReadWrite);
        }
    }

    public static FileStreamOptions CreateNewOptions()
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerReadWrite;
        }
        return options;
    }
}
