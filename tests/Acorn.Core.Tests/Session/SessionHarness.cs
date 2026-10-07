using Acorn.Core.Crypto;
using Acorn.Core.Storage;
using Microsoft.Extensions.Time.Testing;

namespace Acorn.Core.Tests.Session;

/// <summary>A store + session on a temp directory, holding the process lock.</summary>
internal sealed class SessionHarness : IDisposable
{
    private readonly bool _ownsDirectory;

    public SessionHarness(Argon2idParameters? recommendedKdf = null, TempDirectory? directory = null)
    {
        _ownsDirectory = directory is null;
        Directory = directory ?? new TempDirectory();
        Store = new VaultStore(new VaultPaths(Directory.Path), Time);
        Assert.True(Store.TryAcquireLock());
        Session = new VaultSession(Store, recommendedKdf ?? TestKeys.FastKdf);
    }

    public TempDirectory Directory { get; }
    public FakeTimeProvider Time { get; } = new();
    public VaultStore Store { get; }
    public VaultSession Session { get; }

    public byte[] VaultBytes => File.ReadAllBytes(Store.Paths.VaultFile);

    /// <summary>Simulates closing and restarting the app on the same data directory.</summary>
    public SessionHarness Reopen(Argon2idParameters? recommendedKdf = null)
    {
        Session.Dispose();
        Store.Dispose();
        return new SessionHarness(recommendedKdf, Directory);
    }

    public void Dispose()
    {
        Session.Dispose();
        Store.Dispose();
        if (_ownsDirectory)
        {
            Directory.Dispose();
        }
    }
}
