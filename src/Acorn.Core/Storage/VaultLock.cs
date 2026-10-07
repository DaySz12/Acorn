using System.Diagnostics.CodeAnalysis;

namespace Acorn.Core.Storage;

/// <summary>
/// Exclusive OS-level lock on vault.acorn.lock, held for the lifetime of the app (SPEC R4).
/// The OS releases it when the process dies, so a leftover lock file never blocks a restart:
/// only the open handle matters, not whether the file exists.
/// </summary>
public sealed class VaultLock : IDisposable
{
    private FileStream? _stream;

    private VaultLock(FileStream stream) => _stream = stream;

    public static bool TryAcquire(string lockPath, [NotNullWhen(true)] out VaultLock? vaultLock)
    {
        vaultLock = null;
        try
        {
            // FileShare.None: sharing violation on Windows, flock(LOCK_EX | LOCK_NB) on macOS/Linux.
            var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            vaultLock = new VaultLock(stream);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }
}
