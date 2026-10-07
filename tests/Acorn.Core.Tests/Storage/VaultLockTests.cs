using Acorn.Core.Storage;

namespace Acorn.Core.Tests.Storage;

public class VaultLockTests
{
    [Fact]
    public void Second_instance_in_same_process_is_rejected()
    {
        using var dir = new TempDirectory();

        Assert.True(VaultLock.TryAcquire(dir.File("vault.acorn.lock"), out var first));
        using (first)
        {
            Assert.False(VaultLock.TryAcquire(dir.File("vault.acorn.lock"), out var second));
            Assert.Null(second);
        }
    }

    [Fact]
    public void Released_lock_can_be_acquired_again()
    {
        using var dir = new TempDirectory();
        Assert.True(VaultLock.TryAcquire(dir.File("vault.acorn.lock"), out var first));
        first.Dispose();

        Assert.True(VaultLock.TryAcquire(dir.File("vault.acorn.lock"), out var second));
        second.Dispose();
    }

    [Fact]
    public void Leftover_lock_file_without_an_open_handle_does_not_block()
    {
        using var dir = new TempDirectory();
        // Simulates a crash: the file exists but no process holds it.
        File.WriteAllText(dir.File("vault.acorn.lock"), "stale");

        Assert.True(VaultLock.TryAcquire(dir.File("vault.acorn.lock"), out var vaultLock));
        vaultLock.Dispose();
    }

    [Fact]
    public void Two_stores_on_the_same_directory_cannot_both_lock()
    {
        using var dir = new TempDirectory();
        using var first = new VaultStore(new VaultPaths(dir.Path), TimeProvider.System);
        using var second = new VaultStore(new VaultPaths(dir.Path), TimeProvider.System);

        Assert.True(first.TryAcquireLock());
        Assert.False(second.TryAcquireLock());

        first.Dispose();
        Assert.True(second.TryAcquireLock());
    }
}
