using System.Security.Cryptography;
using Acorn.Core.Crypto;
using Acorn.Core.Format;
using Acorn.Core.Models;
using Acorn.Core.Security;
using Acorn.Core.Storage;

namespace Acorn.Core;

/// <summary>
/// Holds the unlocked vault: header, DEK (pinned, zeroed on lock) and decrypted data.
/// Every write goes through <see cref="VaultStore.Write"/> (backup + atomic replace + read-back verify).
/// All public members are thread-safe; auto-lock may call <see cref="Lock"/> from a timer thread.
/// </summary>
public sealed class VaultSession : IVaultSession, IDisposable
{
    private readonly object _gate = new();
    private readonly VaultStore _store;
    private readonly Argon2idParameters _recommendedKdf;

    private VaultHeader? _header;
    private byte[]? _dek;
    private VaultData? _data;
    private byte[]? _lastSavedHash;

    /// <param name="recommendedKdf">Parameters for new vaults and the bar for "below recommended". Defaults to <see cref="Argon2idParameters.Recommended"/>.</param>
    public VaultSession(VaultStore store, Argon2idParameters? recommendedKdf = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _recommendedKdf = recommendedKdf ?? Argon2idParameters.Recommended;
        _recommendedKdf.EnsureWithinBounds();
    }

    public bool HasProcessLock => _store.HasLock;

    public bool VaultExists => _store.VaultExists;

    public string StorageLocation => _store.Paths.DataDirectory;

    public bool IsUnlocked
    {
        get
        {
            lock (_gate)
            {
                return _dek is not null;
            }
        }
    }

    public VaultData Data
    {
        get
        {
            lock (_gate)
            {
                return _data ?? throw new VaultLockedException();
            }
        }
    }

    public Argon2idParameters CurrentKdf
    {
        get
        {
            lock (_gate)
            {
                return _header?.PasswordKdf ?? throw new VaultLockedException();
            }
        }
    }

    /// <summary>Exposes the live DEK array so tests can assert it is zeroed on lock.</summary>
    internal byte[]? DekForTesting => _dek;

    public bool TryAcquireProcessLock() => _store.TryAcquireLock();

    public string? RecoverInterruptedSave() => _store.QuarantineLeftoverTemp();

    public RecoveryKey Create(string masterPassword)
    {
        PasswordPolicy.EnsureAcceptable(masterPassword);
        lock (_gate)
        {
            EnsureProcessLock();
            if (_store.VaultExists)
            {
                throw new InvalidOperationException("A vault already exists.");
            }
            ClearState();

            var password = SecretText.ToUtf8(masterPassword);
            var dek = VaultCrypto.GenerateDek();
            var recovery = RecoveryKey.Generate();
            try
            {
                _header = VaultCrypto.CreateHeader(dek, password, _recommendedKdf, recovery);
                _dek = dek;
                _data = new VaultData { RecoveryKeyConfirmed = false };
                WriteCurrent(force: true, verifyPassword: password);
                return recovery;
            }
            catch
            {
                ClearState();
                Secrets.Zero(dek);
                recovery.Dispose();
                throw;
            }
            finally
            {
                Secrets.Zero(password);
            }
        }
    }

    public UnlockResult UnlockWithPassword(string masterPassword)
    {
        ArgumentNullException.ThrowIfNull(masterPassword);
        lock (_gate)
        {
            EnsureProcessLock();
            ClearState();
            var file = ReadFile();
            var password = SecretText.ToUtf8(masterPassword);
            try
            {
                var dek = VaultCrypto.UnwrapWithPassword(file.Header, password);
                return OpenPayload(file, dek, persistMigration: true);
            }
            finally
            {
                Secrets.Zero(password);
            }
        }
    }

    public RecoveryKey RecoverWithKey(string recoveryKey, string newMasterPassword)
    {
        ArgumentNullException.ThrowIfNull(recoveryKey);
        PasswordPolicy.EnsureAcceptable(newMasterPassword);
        lock (_gate)
        {
            EnsureProcessLock();
            ClearState();
            var file = ReadFile();
            if (!RecoveryKey.TryParse(recoveryKey, out var key))
            {
                throw new VaultAuthenticationException();
            }

            byte[] dek;
            using (key)
            {
                dek = VaultCrypto.UnwrapWithRecoveryKey(file.Header, key);
            }
            // The rekey below writes the file, which also persists any migration.
            OpenPayload(file, dek, persistMigration: false);

            var password = SecretText.ToUtf8(newMasterPassword);
            try
            {
                return Rekey(password, StrongerOf(file.Header.PasswordKdf));
            }
            catch
            {
                ClearState();
                throw;
            }
            finally
            {
                Secrets.Zero(password);
            }
        }
    }

    public void Lock()
    {
        lock (_gate)
        {
            ClearState();
        }
    }

    public bool Save()
    {
        lock (_gate)
        {
            EnsureUnlocked();
            return WriteCurrent(force: false, verifyPassword: null);
        }
    }

    public RecoveryKey ChangePassword(string currentPassword, string newPassword)
    {
        PasswordPolicy.EnsureAcceptable(newPassword);
        lock (_gate)
        {
            EnsureUnlocked();
            Secrets.Zero(VerifyCurrentPassword(currentPassword));
            var password = SecretText.ToUtf8(newPassword);
            try
            {
                return Rekey(password, StrongerOf(_header!.PasswordKdf));
            }
            finally
            {
                Secrets.Zero(password);
            }
        }
    }

    public RecoveryKey RegenerateRecoveryKey(string currentPassword)
    {
        lock (_gate)
        {
            EnsureUnlocked();
            var password = VerifyCurrentPassword(currentPassword);
            try
            {
                return Rekey(password, _header!.PasswordKdf);
            }
            finally
            {
                Secrets.Zero(password);
            }
        }
    }

    public void UpgradeKdf(string currentPassword, Argon2idParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.EnsureWithinBounds();
        lock (_gate)
        {
            EnsureUnlocked();
            var password = VerifyCurrentPassword(currentPassword);
            var previous = _header;
            try
            {
                _header = VaultCrypto.RewrapPassword(_header!, _dek!, password, parameters);
                WriteCurrent(force: true, verifyPassword: password);
            }
            catch
            {
                _header = previous;
                throw;
            }
            finally
            {
                Secrets.Zero(password);
            }
        }
    }

    public IReadOnlyList<BackupInfo> ListBackups() => _store.Backups.List();

    public void RestoreBackup(string fileName)
    {
        lock (_gate)
        {
            EnsureProcessLock();
            // Only prune when unlocked; while locked we do not know the user's retention setting.
            var keep = _data?.Settings.BackupKeep;
            _store.RestoreBackup(fileName, bytes => VaultCodec.Parse(bytes), keep);
            ClearState();
        }
    }

    public void Dispose() => Lock();

    private VaultFile ReadFile()
    {
        if (!_store.VaultExists)
        {
            throw new VaultException("No vault file was found.");
        }
        return VaultCodec.Parse(_store.ReadVault());
    }

    /// <summary>Decrypts and loads the payload. Takes ownership of <paramref name="dek"/>.</summary>
    private UnlockResult OpenPayload(VaultFile file, byte[] dek, bool persistMigration)
    {
        byte[]? plaintext = null;
        try
        {
            plaintext = VaultCrypto.OpenPayload(file, dek);
            var (data, migrated) = VaultSerializer.Deserialize(plaintext);
            _header = file.Header;
            _dek = dek;
            _data = data;

            var upgrade = migrated || file.Header.FormatVersion < VaultFormat.CurrentVersion;
            if (upgrade && persistMigration)
            {
                // Store.Write backs up the old-format file before replacing it.
                WriteCurrent(force: true, verifyPassword: null);
            }
            else if (!upgrade)
            {
                _lastSavedHash = HashOf(data);
            }
            return new UnlockResult(upgrade, file.Header.PasswordKdf.IsWeakerThan(_recommendedKdf));
        }
        catch
        {
            ClearState();
            Secrets.Zero(dek);
            throw;
        }
        finally
        {
            Secrets.Zero(plaintext);
        }
    }

    /// <summary>
    /// New DEK, new salts, new recovery key, payload re-encrypted. Old DEK is zeroed only after the
    /// new file is verified on disk; on failure the previous state is restored.
    /// </summary>
    private RecoveryKey Rekey(byte[] passwordUtf8, Argon2idParameters kdf)
    {
        var previousHeader = _header;
        var previousDek = _dek;
        var previousConfirmed = _data!.RecoveryKeyConfirmed;
        var newDek = VaultCrypto.GenerateDek();
        var recovery = RecoveryKey.Generate();
        try
        {
            _header = VaultCrypto.CreateHeader(newDek, passwordUtf8, kdf, recovery);
            _dek = newDek;
            // The new key has not been shown to the user yet; the UI clears this after confirmation.
            _data.RecoveryKeyConfirmed = false;
            WriteCurrent(force: true, verifyPassword: passwordUtf8);
        }
        catch
        {
            _header = previousHeader;
            _dek = previousDek;
            _data.RecoveryKeyConfirmed = previousConfirmed;
            Secrets.Zero(newDek);
            recovery.Dispose();
            throw;
        }

        Secrets.Zero(previousDek);
        return recovery;
    }

    private bool WriteCurrent(bool force, byte[]? verifyPassword)
    {
        var plaintext = VaultSerializer.Serialize(_data!);
        try
        {
            var hash = SHA256.HashData(plaintext);
            if (!force && _lastSavedHash is not null && CryptographicOperations.FixedTimeEquals(hash, _lastSavedHash))
            {
                return false;
            }

            var header = _header! with { FormatVersion = VaultFormat.CurrentVersion };
            var dek = _dek!;
            var fileBytes = VaultCrypto.Seal(header, dek, plaintext);
            _store.Write(fileBytes, written => VerifyWritten(written, dek, verifyPassword), _data!.Settings.BackupKeep);

            _header = header;
            _lastSavedHash = hash;
            return true;
        }
        finally
        {
            Secrets.Zero(plaintext);
        }
    }

    /// <summary>Read-back check before the temp file replaces the vault (SPEC R3 step 3).</summary>
    private static void VerifyWritten(byte[] written, byte[] dek, byte[]? passwordUtf8)
    {
        var file = VaultCodec.Parse(written);
        Secrets.Zero(VaultCrypto.OpenPayload(file, dek));
        if (passwordUtf8 is null)
        {
            return;
        }

        var unwrapped = VaultCrypto.UnwrapWithPassword(file.Header, passwordUtf8);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(unwrapped, dek))
            {
                throw new VaultAuthenticationException();
            }
        }
        finally
        {
            Secrets.Zero(unwrapped);
        }
    }

    /// <summary>Returns the password bytes (caller zeroes) if they unwrap the current DEK.</summary>
    private byte[] VerifyCurrentPassword(string currentPassword)
    {
        ArgumentNullException.ThrowIfNull(currentPassword);
        var password = SecretText.ToUtf8(currentPassword);
        try
        {
            var dek = VaultCrypto.UnwrapWithPassword(_header!, password);
            try
            {
                if (!CryptographicOperations.FixedTimeEquals(dek, _dek))
                {
                    throw new VaultAuthenticationException();
                }
            }
            finally
            {
                Secrets.Zero(dek);
            }
            return password;
        }
        catch
        {
            Secrets.Zero(password);
            throw;
        }
    }

    private Argon2idParameters StrongerOf(Argon2idParameters current) =>
        current.IsWeakerThan(_recommendedKdf) ? _recommendedKdf : current;

    private static byte[] HashOf(VaultData data)
    {
        var plaintext = VaultSerializer.Serialize(data);
        try
        {
            return SHA256.HashData(plaintext);
        }
        finally
        {
            Secrets.Zero(plaintext);
        }
    }

    private void ClearState()
    {
        Secrets.Zero(_dek);
        _dek = null;
        _header = null;
        Secrets.Zero(_lastSavedHash);
        _lastSavedHash = null;
        if (_data is not null)
        {
            _data.Entries.Clear();
            _data = null;
        }
    }

    private void EnsureProcessLock()
    {
        if (!_store.HasLock)
        {
            throw new VaultInUseException();
        }
    }

    private void EnsureUnlocked()
    {
        if (_dek is null || _data is null || _header is null)
        {
            throw new VaultLockedException();
        }
    }
}
