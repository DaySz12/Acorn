using System.Buffers.Binary;
using System.Diagnostics;
using Acorn.Core.Crypto;
using Acorn.Core.Format;

namespace Acorn.Core.Tests.Format;

public class VaultFormatTests
{
    private static readonly byte[] Plaintext = """{"schemaVersion":2,"entries":[]}"""u8.ToArray();

    // Layout offsets of the v2 header (see docs/FORMAT.md).
    private const int VersionOffset = 5;
    private const int MemoryOffset = 8;
    private const int IterationsOffset = 12;
    private const int HeaderLength = 173;

    private static (byte[] File, RecoveryKey Recovery) CreateVault()
    {
        var dek = VaultCrypto.GenerateDek();
        var recovery = RecoveryKey.Generate();
        var header = VaultCrypto.CreateHeader(dek, TestKeys.Utf8(TestKeys.Password), TestKeys.FastKdf, recovery);
        return (VaultCrypto.Seal(header, dek, Plaintext), recovery);
    }

    private static byte[] OpenWithPassword(byte[] fileBytes, string password)
    {
        var file = VaultCodec.Parse(fileBytes);
        var dek = VaultCrypto.UnwrapWithPassword(file.Header, TestKeys.Utf8(password));
        return VaultCrypto.OpenPayload(file, dek);
    }

    [Fact]
    public void Sealed_vault_opens_with_password_and_recovery_key()
    {
        var (fileBytes, recovery) = CreateVault();

        Assert.Equal(Plaintext, OpenWithPassword(fileBytes, TestKeys.Password));

        var file = VaultCodec.Parse(fileBytes);
        var dek = VaultCrypto.UnwrapWithRecoveryKey(file.Header, recovery);
        Assert.Equal(Plaintext, VaultCrypto.OpenPayload(file, dek));
    }

    [Fact]
    public void Header_has_documented_layout()
    {
        var (fileBytes, _) = CreateVault();
        var file = VaultCodec.Parse(fileBytes);

        Assert.Equal("ACORN"u8.ToArray(), fileBytes[..5]);
        Assert.Equal(VaultFormat.CurrentVersion, BinaryPrimitives.ReadUInt16LittleEndian(fileBytes.AsSpan(VersionOffset)));
        Assert.Equal(HeaderLength, file.HeaderBytes.Length);
        Assert.Equal(TestKeys.FastKdf, file.Header.PasswordKdf);
        Assert.Equal(fileBytes.Length, HeaderLength + Aead.NonceSize + Aead.TagSize + 4 + Plaintext.Length);
    }

    [Fact]
    public void Wrong_password_and_wrong_recovery_key_fail_with_the_same_generic_message()
    {
        var (fileBytes, _) = CreateVault();
        var file = VaultCodec.Parse(fileBytes);
        using var wrongRecovery = RecoveryKey.Generate();

        var byPassword = Assert.Throws<VaultAuthenticationException>(() => OpenWithPassword(fileBytes, TestKeys.OtherPassword));
        var byRecovery = Assert.Throws<VaultAuthenticationException>(() => VaultCrypto.UnwrapWithRecoveryKey(file.Header, wrongRecovery));

        Assert.Equal(byPassword.Message, byRecovery.Message);
        Assert.DoesNotContain(TestKeys.OtherPassword, byPassword.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Flipping_any_header_bit_or_sampled_payload_bit_prevents_opening()
    {
        var (fileBytes, _) = CreateVault();
        var positions = Enumerable.Range(0, HeaderLength)
            .Concat(Enumerable.Range(HeaderLength, fileBytes.Length - HeaderLength).Where(i => i % 3 == 0));

        foreach (var position in positions)
        {
            var tampered = (byte[])fileBytes.Clone();
            tampered[position] ^= (byte)(1 << (position % 8));

            // Either the parser rejects it, or authentication fails. It must never open.
            var ex = Record.Exception(() => OpenWithPassword(tampered, TestKeys.Password));
            Assert.True(ex is VaultAuthenticationException or VaultFormatException, $"Byte {position} was not detected.");
        }
    }

    [Fact]
    public void Lowering_kdf_iterations_in_header_is_detected()
    {
        var dek = VaultCrypto.GenerateDek();
        using var recovery = RecoveryKey.Generate();
        var header = VaultCrypto.CreateHeader(dek, TestKeys.Utf8(TestKeys.Password), TestKeys.FastKdf with { Iterations = 2 }, recovery);
        var fileBytes = VaultCrypto.Seal(header, dek, Plaintext);

        BinaryPrimitives.WriteUInt32LittleEndian(fileBytes.AsSpan(IterationsOffset), 1);

        Assert.Throws<VaultAuthenticationException>(() => OpenWithPassword(fileBytes, TestKeys.Password));
    }

    [Theory]
    [InlineData(MemoryOffset, uint.MaxValue)]
    [InlineData(MemoryOffset, (uint)Argon2idParameters.MaxMemoryKiB + 1)]
    [InlineData(IterationsOffset, 1_000_000u)]
    [InlineData(IterationsOffset, 0u)]
    public void Out_of_range_kdf_parameters_are_rejected_at_parse_time(int offset, uint value)
    {
        var (fileBytes, _) = CreateVault();
        BinaryPrimitives.WriteUInt32LittleEndian(fileBytes.AsSpan(offset), value);
        var stopwatch = Stopwatch.StartNew();

        Assert.Throws<VaultFormatException>(() => VaultCodec.Parse(fileBytes));
        Assert.True(stopwatch.ElapsedMilliseconds < 1000);
    }

    [Fact]
    public void Newer_format_version_is_rejected_with_clear_error()
    {
        var (fileBytes, _) = CreateVault();
        BinaryPrimitives.WriteUInt16LittleEndian(fileBytes.AsSpan(VersionOffset), VaultFormat.CurrentVersion + 1);

        var ex = Assert.Throws<UnsupportedVaultVersionException>(() => VaultCodec.Parse(fileBytes));
        Assert.Equal(VaultFormat.CurrentVersion + 1, ex.Version);
        Assert.Contains("newer", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Truncated_or_padded_files_are_rejected()
    {
        var (fileBytes, _) = CreateVault();

        Assert.Throws<VaultFormatException>(() => VaultCodec.Parse(fileBytes[..^1]));
        Assert.Throws<VaultFormatException>(() => VaultCodec.Parse([.. fileBytes, 0]));
        Assert.Throws<VaultFormatException>(() => VaultCodec.Parse(fileBytes[..10]));
        Assert.Throws<VaultFormatException>(() => VaultCodec.Parse("NOTAVAULT"u8));
    }

    [Fact]
    public void Each_seal_uses_a_new_payload_nonce()
    {
        var dek = VaultCrypto.GenerateDek();
        using var recovery = RecoveryKey.Generate();
        var header = VaultCrypto.CreateHeader(dek, TestKeys.Utf8(TestKeys.Password), TestKeys.FastKdf, recovery);
        var nonces = new HashSet<string>();

        for (var i = 0; i < 1000; i++)
        {
            var file = VaultCodec.Parse(VaultCrypto.Seal(header, dek, Plaintext));
            Assert.True(nonces.Add(Convert.ToHexString(file.Payload.Nonce)));
        }
    }

    [Fact]
    public void Rewrapping_password_keeps_recovery_wrap_working()
    {
        var dek = VaultCrypto.GenerateDek();
        using var recovery = RecoveryKey.Generate();
        var header = VaultCrypto.CreateHeader(dek, TestKeys.Utf8(TestKeys.Password), TestKeys.FastKdf, recovery);

        var rewrapped = VaultCrypto.RewrapPassword(header, dek, TestKeys.Utf8(TestKeys.OtherPassword), TestKeys.FastKdf with { Iterations = 2 });
        var fileBytes = VaultCrypto.Seal(rewrapped, dek, Plaintext);

        Assert.Equal(Plaintext, OpenWithPassword(fileBytes, TestKeys.OtherPassword));
        Assert.Throws<VaultAuthenticationException>(() => OpenWithPassword(fileBytes, TestKeys.Password));
        var file = VaultCodec.Parse(fileBytes);
        Assert.Equal(Plaintext, VaultCrypto.OpenPayload(file, VaultCrypto.UnwrapWithRecoveryKey(file.Header, recovery)));
    }
}
