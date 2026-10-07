using System.Runtime.CompilerServices;
using Acorn.Core.Crypto;
using Acorn.Core.Format;

namespace Acorn.Core.Tests.Format;

/// <summary>
/// Writer for the legacy v1 container, used only to (re)generate the committed fixture.
/// v1 differs from v2 by having no algorithm-id bytes in the header, and its payload uses
/// schema 1 (tags as a comma-separated string). See docs/FORMAT.md.
/// </summary>
internal static class LegacyFixtures
{
    public const string V1Password = "acorn-fixture-v1-password";
    public const string V1RecoveryKey = "ACRN0-FXTR1-VKEY1-TEST0-ABCDE-FGHJK-MNPQR-STVWX";
    public static readonly Guid V1EntryId = Guid.Parse("6f1c1a52-8d3e-4c2b-9a51-0d5c3e2b7a10");

    public const string V1PayloadJson =
        """
        {"schemaVersion":1,"entries":[{"id":"6f1c1a52-8d3e-4c2b-9a51-0d5c3e2b7a10","type":"server","name":"Legacy web","env":"prod","host":"10.0.0.5","port":2222,"username":"deploy","password":"fixture-secret","sshKeyPath":"","tags":"web, legacy ,","customFields":[],"notes":"migrated from v1","favorite":true,"createdAt":"2025-01-02T03:04:05Z","updatedAt":"2025-01-02T03:04:05Z"}],"settings":{"clipboardClearSeconds":30,"autoLockMinutes":5,"lockOnMinimize":true,"screenCaptureProtection":true,"revealSeconds":12,"backupKeep":10,"theme":"dark","environments":["dev","staging","prod"]}}
        """;

    public static string V1FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "vault-v1.acorn");

    public static string SourceFixturePath([CallerFilePath] string thisFile = "") =>
        Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(thisFile))!, "Fixtures", "vault-v1.acorn");

    public static byte[] WriteV1()
    {
        Assert.True(RecoveryKey.TryParse(V1RecoveryKey, out var recovery));
        using (recovery)
        {
            var dek = VaultCrypto.GenerateDek();
            var header = VaultCrypto.CreateHeader(dek, TestKeys.Utf8(V1Password), TestKeys.FastKdf, recovery);

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write("ACORN"u8);
                writer.Write((ushort)1);
                writer.Write((uint)header.PasswordKdf.MemoryKiB);
                writer.Write((uint)header.PasswordKdf.Iterations);
                writer.Write((uint)header.PasswordKdf.Parallelism);
                writer.Write(header.PasswordSalt);
                writer.Write(header.RecoverySalt);
                foreach (var box in new[] { header.WrappedDekByPassword, header.WrappedDekByRecovery })
                {
                    writer.Write(box.Nonce);
                    writer.Write(box.Ciphertext);
                    writer.Write(box.Tag);
                }
            }

            var headerBytes = stream.ToArray();
            var payload = Aead.Encrypt(dek, System.Text.Encoding.UTF8.GetBytes(V1PayloadJson), headerBytes);
            return VaultCodec.WriteFile(headerBytes, payload);
        }
    }
}

public class LegacyFixtureGenerator
{
    /// <summary>Run explicitly to regenerate tests/Acorn.Core.Tests/Fixtures/vault-v1.acorn.</summary>
    [Fact(Explicit = true)]
    public void Regenerate_v1_fixture()
    {
        var path = LegacyFixtures.SourceFixturePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, LegacyFixtures.WriteV1());
    }
}
