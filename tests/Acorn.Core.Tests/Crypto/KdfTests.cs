using System.Diagnostics;
using System.Security.Cryptography;
using Acorn.Core.Crypto;

namespace Acorn.Core.Tests.Crypto;

public class KdfTests
{
    private static readonly byte[] Salt = RandomNumberGenerator.GetBytes(Kdf.SaltSize);

    [Fact]
    public void Password_key_is_deterministic_for_same_inputs()
    {
        var a = Kdf.DerivePasswordKey(TestKeys.Utf8(TestKeys.Password), Salt, TestKeys.FastKdf);
        var b = Kdf.DerivePasswordKey(TestKeys.Utf8(TestKeys.Password), Salt, TestKeys.FastKdf);

        Assert.Equal(Kdf.KeySize, a.Length);
        Assert.True(CryptographicOperations.FixedTimeEquals(a, b));
    }

    [Fact]
    public void Password_key_changes_with_salt_password_and_parameters()
    {
        var baseline = Kdf.DerivePasswordKey(TestKeys.Utf8(TestKeys.Password), Salt, TestKeys.FastKdf);

        var otherSalt = Kdf.DerivePasswordKey(TestKeys.Utf8(TestKeys.Password), RandomNumberGenerator.GetBytes(Kdf.SaltSize), TestKeys.FastKdf);
        var otherPassword = Kdf.DerivePasswordKey(TestKeys.Utf8(TestKeys.OtherPassword), Salt, TestKeys.FastKdf);
        var otherParams = Kdf.DerivePasswordKey(TestKeys.Utf8(TestKeys.Password), Salt, TestKeys.FastKdf with { Iterations = 2 });

        Assert.False(CryptographicOperations.FixedTimeEquals(baseline, otherSalt));
        Assert.False(CryptographicOperations.FixedTimeEquals(baseline, otherPassword));
        Assert.False(CryptographicOperations.FixedTimeEquals(baseline, otherParams));
    }

    [Theory]
    [InlineData(Argon2idParameters.MaxMemoryKiB + 1, 1, 1)]
    [InlineData(int.MaxValue, 1, 1)]
    [InlineData(Argon2idParameters.MinMemoryKiB - 1, 1, 1)]
    [InlineData(Argon2idParameters.MinMemoryKiB, 0, 1)]
    [InlineData(Argon2idParameters.MinMemoryKiB, Argon2idParameters.MaxIterations + 1, 1)]
    [InlineData(Argon2idParameters.MinMemoryKiB, 1, 0)]
    [InlineData(Argon2idParameters.MinMemoryKiB, 1, Argon2idParameters.MaxParallelism + 1)]
    public void Out_of_range_parameters_are_rejected_before_derivation(int memoryKiB, int iterations, int parallelism)
    {
        var stopwatch = Stopwatch.StartNew();

        Assert.Throws<VaultFormatException>(() =>
            Kdf.DerivePasswordKey(TestKeys.Utf8(TestKeys.Password), Salt, new Argon2idParameters(memoryKiB, iterations, parallelism)));

        // Rejection must not run Argon2 at all; generous bound to stay stable on slow CI machines.
        Assert.True(stopwatch.ElapsedMilliseconds < 1000);
    }

    [Fact]
    public void Recovery_key_derivation_depends_on_salt()
    {
        using var recovery = RecoveryKey.Generate();

        var a = Kdf.DeriveRecoveryKey(recovery.Bytes, Salt);
        var b = Kdf.DeriveRecoveryKey(recovery.Bytes, Salt);
        var c = Kdf.DeriveRecoveryKey(recovery.Bytes, RandomNumberGenerator.GetBytes(Kdf.SaltSize));

        Assert.True(CryptographicOperations.FixedTimeEquals(a, b));
        Assert.False(CryptographicOperations.FixedTimeEquals(a, c));
    }

    [Fact]
    public void Passwords_are_nfc_normalized_before_encoding()
    {
        // "é" precomposed vs. "e" + combining acute accent.
        Assert.Equal(TestKeys.Utf8("café-password"), TestKeys.Utf8("café-password"));
    }

    [Fact]
    public void Weaker_than_compares_memory_and_iterations()
    {
        Assert.True(TestKeys.FastKdf.IsWeakerThan(Argon2idParameters.Recommended));
        Assert.False(Argon2idParameters.Recommended.IsWeakerThan(Argon2idParameters.Recommended));
        Assert.False(Argon2idParameters.Strong.IsWeakerThan(Argon2idParameters.Recommended));
    }
}
