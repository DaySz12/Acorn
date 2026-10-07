using System.Globalization;

namespace Acorn.Core.Storage;

/// <summary>
/// File-level operations on the vault directory: process lock, reads, atomic writes with
/// automatic backups, restore, and recovery of a temp file left by an interrupted save.
/// Only ciphertext passes through this class.
/// </summary>
public sealed class VaultStore : IDisposable
{
    private readonly TimeProvider _time;
    private VaultLock? _lock;

    public VaultStore(VaultPaths paths, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(time);
        Paths = paths;
        _time = time;
        Backups = new BackupRotator(paths.BackupsDirectory, time);
    }

    public VaultPaths Paths { get; }

    public BackupRotator Backups { get; }

    internal AtomicFileWriter Writer { get; } = new();

    public bool HasLock => _lock is not null;

    public bool VaultExists => File.Exists(Paths.VaultFile);

    /// <summary>Acquires the process lock. Returns false if another window or process holds it.</summary>
    public bool TryAcquireLock()
    {
        if (_lock is not null)
        {
            return true;
        }
        Paths.EnsureDirectories();
        return VaultLock.TryAcquire(Paths.LockFile, out _lock);
    }

    public byte[] ReadVault() => File.ReadAllBytes(Paths.VaultFile);

    /// <summary>
    /// Moves a leftover vault.acorn.tmp from an interrupted save into the backups folder,
    /// never touching vault.acorn. Returns the new file name, or null if there was none.
    /// </summary>
    public string? QuarantineLeftoverTemp()
    {
        EnsureLock();
        if (!File.Exists(Paths.TempFile))
        {
            return null;
        }

        Paths.EnsureDirectories();
        var stamp = _time.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var fileName = $"unfinished-{stamp}.acorn.tmp";
        for (var sequence = 2; File.Exists(Path.Combine(Paths.BackupsDirectory, fileName)); sequence++)
        {
            fileName = $"unfinished-{stamp}-{sequence}.acorn.tmp";
        }
        File.Move(Paths.TempFile, Path.Combine(Paths.BackupsDirectory, fileName));
        return fileName;
    }

    /// <summary>
    /// Backs up the current vault (if any), then writes atomically. <paramref name="verify"/> must throw
    /// if the bytes read back from disk do not decrypt. Pass <paramref name="backupKeep"/> = null to skip pruning.
    /// </summary>
    public void Write(ReadOnlySpan<byte> content, Action<byte[]> verify, int? backupKeep)
    {
        EnsureLock();
        Paths.EnsureDirectories();
        if (VaultExists)
        {
            Backups.CreateBackup(Paths.VaultFile);
        }

        Writer.Write(Paths.VaultFile, Paths.TempFile, content, verify);

        if (backupKeep is { } keep)
        {
            Backups.Prune(keep);
        }
    }

    /// <summary>Replaces the vault with a backup (after backing up the current vault).</summary>
    public void RestoreBackup(string fileName, Action<byte[]> verify, int? backupKeep)
    {
        EnsureLock();
        var source = Backups.ResolvePath(fileName);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("The selected backup no longer exists.");
        }
        Write(File.ReadAllBytes(source), verify, backupKeep);
    }

    public void Dispose()
    {
        _lock?.Dispose();
        _lock = null;
    }

    private void EnsureLock()
    {
        if (_lock is null)
        {
            throw new VaultInUseException();
        }
    }
}
