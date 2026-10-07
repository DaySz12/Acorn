namespace Acorn.Core;

// Exception messages in this file are deliberately generic. They must never contain
// passwords, keys, recovery keys or decrypted vault content.

/// <summary>Base type for all expected vault failures.</summary>
public class VaultException : Exception
{
    public VaultException(string message) : base(message) { }
    public VaultException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Wrong password, wrong recovery key, or a file whose authenticated data was modified.
/// These cases are intentionally indistinguishable.
/// </summary>
public sealed class VaultAuthenticationException : VaultException
{
    public const string GenericMessage = "Unable to unlock the vault with the provided credentials.";

    public VaultAuthenticationException() : base(GenericMessage) { }
}

/// <summary>The file is not a valid Acorn vault, is truncated, or has out-of-range header values.</summary>
public class VaultFormatException : VaultException
{
    public VaultFormatException(string message) : base(message) { }
}

/// <summary>The file was written by a newer version of Acorn. It is never modified.</summary>
public sealed class UnsupportedVaultVersionException : VaultFormatException
{
    public UnsupportedVaultVersionException(int version)
        : base($"This vault was written by a newer version of Acorn (version {version}). Update Acorn to open it.")
    {
        Version = version;
    }

    public int Version { get; }
}

/// <summary>An operation required an unlocked vault.</summary>
public sealed class VaultLockedException : VaultException
{
    public VaultLockedException() : base("The vault is locked.") { }
}

/// <summary>Another window or process holds the vault lock.</summary>
public sealed class VaultInUseException : VaultException
{
    public VaultInUseException() : base("The vault is in use by another window or process.") { }
}
