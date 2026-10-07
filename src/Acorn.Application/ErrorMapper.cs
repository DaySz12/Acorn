using Acorn.Core;

namespace Acorn.Application;

/// <summary>Turns exceptions into generic user messages. Exception text is never shown.</summary>
internal static class ErrorMapper
{
    public static string ToMessage(Exception exception, string authenticationMessage = Messages.UnlockFailed) => exception switch
    {
        VaultAuthenticationException => authenticationMessage,
        UnsupportedVaultVersionException => Messages.VaultNewer,
        VaultFormatException => Messages.VaultCorrupt,
        VaultInUseException => Messages.VaultInUse,
        VaultLockedException => Messages.VaultLocked,
        VaultException => Messages.VaultMissing,
        IOException or UnauthorizedAccessException => Messages.StorageError,
        _ => Messages.Unexpected,
    };
}
