using Acorn.Core.Crypto;
using Acorn.Core.Models;
using Acorn.Core.Storage;

namespace Acorn.Core;

/// <param name="Migrated">The file was upgraded to the current format and written back (with a backup).</param>
/// <param name="KdfBelowRecommended">The header's Argon2 parameters are weaker than the current recommendation.</param>
public sealed record UnlockResult(bool Migrated, bool KdfBelowRecommended);

/// <summary>
/// The Model's entry point: owns the decrypted vault while unlocked. Implemented by
/// <see cref="VaultSession"/>; an interface so controllers can be tested with mocks.
/// Methods taking passwords accept the string the UI produced, convert it to bytes once and zero them.
/// </summary>
public interface IVaultSession
{
    bool HasProcessLock { get; }
    bool VaultExists { get; }
    bool IsUnlocked { get; }

    /// <summary>Folder that holds the vault, its lock file and backups.</summary>
    string StorageLocation { get; }

    /// <summary>Live decrypted data. Throws <see cref="VaultLockedException"/> when locked.</summary>
    VaultData Data { get; }

    /// <summary>Argon2 parameters currently in the header. Throws when locked.</summary>
    Argon2idParameters CurrentKdf { get; }

    bool TryAcquireProcessLock();

    /// <summary>Moves a temp file left by an interrupted save into backups. Returns its name or null.</summary>
    string? RecoverInterruptedSave();

    /// <summary>Creates a new vault and leaves it unlocked. The caller shows and then disposes the recovery key.</summary>
    RecoveryKey Create(string masterPassword);

    UnlockResult UnlockWithPassword(string masterPassword);

    /// <summary>Opens with the recovery key, sets a new master password and issues a new recovery key.</summary>
    RecoveryKey RecoverWithKey(string recoveryKey, string newMasterPassword);

    /// <summary>Zeroes the DEK and drops all decrypted data.</summary>
    void Lock();

    /// <summary>Writes the vault if the data changed since the last write. Returns false when skipped.</summary>
    bool Save();

    /// <summary>Verifies the current password, rotates the DEK and both wraps. Returns the new recovery key.</summary>
    RecoveryKey ChangePassword(string currentPassword, string newPassword);

    /// <summary>Verifies the password, rotates the DEK and issues a new recovery key; the old key stops working.</summary>
    RecoveryKey RegenerateRecoveryKey(string currentPassword);

    /// <summary>Re-wraps the DEK under the password with stronger Argon2 parameters.</summary>
    void UpgradeKdf(string currentPassword, Argon2idParameters parameters);

    IReadOnlyList<BackupInfo> ListBackups();

    /// <summary>Replaces the vault with a backup (backing up the current file first) and locks.</summary>
    void RestoreBackup(string fileName);
}
