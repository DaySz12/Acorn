using Acorn.Core.Crypto;

namespace Acorn.Core.Tests;

/// <summary>Shared test inputs. Values are synthetic and never printed to test output.</summary>
internal static class TestKeys
{
    /// <summary>Lowest accepted cost so tests stay fast. Production uses <see cref="Argon2idParameters.Recommended"/>.</summary>
    public static readonly Argon2idParameters FastKdf = new(Argon2idParameters.MinMemoryKiB, 1, 1);

    public const string Password = "correct horse battery staple";
    public const string OtherPassword = "Tr0ub4dor&3-but-longer";

    public static byte[] Utf8(string text) => SecretText.ToUtf8(text);
}
