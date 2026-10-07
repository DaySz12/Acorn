using Acorn.Core.Storage;

namespace Acorn.Core.Tests.Storage;

public class AtomicFileWriterTests
{
    private static readonly byte[] Original = "original vault"u8.ToArray();
    private static readonly byte[] Updated = "updated vault"u8.ToArray();

    [Fact]
    public void Creates_target_when_missing()
    {
        using var dir = new TempDirectory();
        var writer = new AtomicFileWriter();

        writer.Write(dir.File("vault"), dir.File("vault.tmp"), Original, _ => { });

        Assert.Equal(Original, File.ReadAllBytes(dir.File("vault")));
        Assert.False(File.Exists(dir.File("vault.tmp")));
    }

    [Fact]
    public void Replaces_existing_target_after_verification_sees_written_bytes()
    {
        using var dir = new TempDirectory();
        File.WriteAllBytes(dir.File("vault"), Original);
        byte[]? verified = null;

        new AtomicFileWriter().Write(dir.File("vault"), dir.File("vault.tmp"), Updated, bytes => verified = bytes);

        Assert.Equal(Updated, verified);
        Assert.Equal(Updated, File.ReadAllBytes(dir.File("vault")));
        Assert.False(File.Exists(dir.File("vault.tmp")));
    }

    [Theory]
    [InlineData(AtomicWriteStage.TempWritten)]
    [InlineData(AtomicWriteStage.TempFlushed)]
    [InlineData(AtomicWriteStage.Verified)]
    public void Failure_at_any_stage_leaves_original_untouched(AtomicWriteStage failAt)
    {
        using var dir = new TempDirectory();
        File.WriteAllBytes(dir.File("vault"), Original);
        var writer = new AtomicFileWriter
        {
            FaultInjector = stage =>
            {
                if (stage == failAt)
                {
                    throw new IOException("simulated failure");
                }
            },
        };

        Assert.Throws<IOException>(() => writer.Write(dir.File("vault"), dir.File("vault.tmp"), Updated, _ => { }));

        Assert.Equal(Original, File.ReadAllBytes(dir.File("vault")));
        Assert.False(File.Exists(dir.File("vault.tmp")));
    }

    [Fact]
    public void Failed_verification_leaves_original_untouched()
    {
        using var dir = new TempDirectory();
        File.WriteAllBytes(dir.File("vault"), Original);

        Assert.Throws<VaultAuthenticationException>(() =>
            new AtomicFileWriter().Write(dir.File("vault"), dir.File("vault.tmp"), Updated, _ => throw new VaultAuthenticationException()));

        Assert.Equal(Original, File.ReadAllBytes(dir.File("vault")));
        Assert.False(File.Exists(dir.File("vault.tmp")));
    }

    [Fact]
    public void Written_file_is_owner_only_on_unix()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows relies on the user-profile ACL.");
        using var dir = new TempDirectory();

        new AtomicFileWriter().Write(dir.File("vault"), dir.File("vault.tmp"), Original, _ => { });

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(dir.File("vault")));
        }
    }
}
