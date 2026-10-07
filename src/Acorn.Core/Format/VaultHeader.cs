using Acorn.Core.Crypto;

namespace Acorn.Core.Format;

/// <summary>Parsed vault header. Everything in it is authenticated as AAD of the payload.</summary>
public sealed record VaultHeader(
    int FormatVersion,
    Argon2idParameters PasswordKdf,
    byte[] PasswordSalt,
    byte[] RecoverySalt,
    AeadBox WrappedDekByPassword,
    AeadBox WrappedDekByRecovery);
