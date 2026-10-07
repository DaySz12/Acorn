using System.Globalization;
using System.Text.RegularExpressions;

namespace Acorn.Core.Storage;

public sealed record BackupInfo(string FileName, DateTime CreatedAtUtc, long SizeBytes);

/// <summary>
/// Copies the vault to backups/vault-YYYYMMDD-HHmmss.acorn.bak (UTC) before each overwrite
/// and keeps only the newest N. Files that do not match the pattern are never touched.
/// </summary>
public sealed partial class BackupRotator
{
    private const string TimestampFormat = "yyyyMMdd-HHmmss";

    private readonly string _directory;
    private readonly TimeProvider _time;

    public BackupRotator(string directory, TimeProvider time)
    {
        _directory = directory;
        _time = time;
    }

    [GeneratedRegex(@"^vault-(\d{8}-\d{6})(?:-(\d{1,4}))?\.acorn\.bak$", RegexOptions.CultureInvariant)]
    private static partial Regex BackupName();

    public static bool IsBackupFileName(string fileName) => BackupName().IsMatch(fileName);

    public BackupInfo CreateBackup(string sourcePath)
    {
        Directory.CreateDirectory(_directory);
        var stamp = _time.GetUtcNow().UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        var fileName = $"vault-{stamp}.acorn.bak";
        for (var sequence = 2; File.Exists(Path.Combine(_directory, fileName)); sequence++)
        {
            fileName = $"vault-{stamp}-{sequence}.acorn.bak";
        }

        var destination = Path.Combine(_directory, fileName);
        File.Copy(sourcePath, destination, overwrite: false);
        FilePermissions.RestrictToOwner(destination);
        return ToInfo(fileName, new FileInfo(destination).Length)!;
    }

    /// <summary>Backups, newest first.</summary>
    public IReadOnlyList<BackupInfo> List()
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(_directory)
            .Select(path => new FileInfo(path))
            .Select(file => ToInfo(file.Name, file.Length))
            .OfType<BackupInfo>()
            .OrderByDescending(b => b, BackupOrder.Instance)
            .ToList();
    }

    /// <summary>Deletes the oldest backups beyond <paramref name="keep"/>. Returns how many were deleted.</summary>
    public int Prune(int keep)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(keep, 1);
        var deleted = 0;
        foreach (var backup in List().Skip(keep))
        {
            File.Delete(Path.Combine(_directory, backup.FileName));
            deleted++;
        }
        return deleted;
    }

    /// <summary>Maps a backup file name chosen in the UI to its path, rejecting anything else (e.g. "../").</summary>
    public string ResolvePath(string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || !IsBackupFileName(fileName) || Path.GetFileName(fileName) != fileName)
        {
            throw new ArgumentException("Not a valid backup file name.", nameof(fileName));
        }
        return Path.Combine(_directory, fileName);
    }

    private static BackupInfo? ToInfo(string fileName, long size)
    {
        var match = BackupName().Match(fileName);
        if (!match.Success ||
            !DateTime.TryParseExact(match.Groups[1].Value, TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var createdAt))
        {
            return null;
        }
        return new BackupInfo(fileName, createdAt, size);
    }

    private static int Sequence(string fileName)
    {
        var group = BackupName().Match(fileName).Groups[2];
        return group.Success ? int.Parse(group.Value, CultureInfo.InvariantCulture) : 1;
    }

    private sealed class BackupOrder : IComparer<BackupInfo>
    {
        public static readonly BackupOrder Instance = new();

        public int Compare(BackupInfo? x, BackupInfo? y)
        {
            ArgumentNullException.ThrowIfNull(x);
            ArgumentNullException.ThrowIfNull(y);
            var byTime = x.CreatedAtUtc.CompareTo(y.CreatedAtUtc);
            return byTime != 0 ? byTime : Sequence(x.FileName).CompareTo(Sequence(y.FileName));
        }
    }
}
