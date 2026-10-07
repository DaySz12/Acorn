using System.Security.Cryptography;
using System.Text;
using Acorn.Application.Abstractions;

namespace Acorn.Application.Services;

/// <summary>
/// Clipboard policy (SPEC R5): secrets are cleared after a timeout, on lock and on exit, but only
/// if the clipboard still holds the value Acorn put there. The value itself is not retained; only an
/// HMAC under a per-process random key, so the comparison needs no copy of the secret.
/// </summary>
public sealed class ClipboardGuard : IDisposable
{
    private readonly IClipboardService _clipboard;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly byte[] _hmacKey = RandomNumberGenerator.GetBytes(32);
    private byte[]? _ownedFingerprint;
    private ITimer? _clearTimer;

    public ClipboardGuard(IClipboardService clipboard, TimeProvider time)
    {
        _clipboard = clipboard;
        _time = time;
    }

    /// <summary>True while a secret copied by Acorn may still be on the clipboard.</summary>
    public bool OwnsClipboard
    {
        get
        {
            lock (_gate)
            {
                return _ownedFingerprint is not null;
            }
        }
    }

    public async Task CopySecretAsync(string value, TimeSpan clearAfter)
    {
        ArgumentNullException.ThrowIfNull(value);
        var fingerprint = Fingerprint(value);
        // Claim ownership before the value reaches the clipboard, so a lock that races with this
        // copy still clears it.
        lock (_gate)
        {
            _clearTimer?.Dispose();
            _ownedFingerprint = fingerprint;
            _clearTimer = _time.CreateTimer(_ => _ = ClearIfOwnedSafeAsync(), null, clearAfter, Timeout.InfiniteTimeSpan);
        }

        try
        {
            await _clipboard.SetTextAsync(value, isSecret: true).ConfigureAwait(false);
        }
        catch
        {
            lock (_gate)
            {
                if (ReferenceEquals(_ownedFingerprint, fingerprint))
                {
                    ForgetOwnership();
                }
            }
            throw;
        }
    }

    /// <summary>Copies a non-secret value (host, username, ssh command). No clear timer.</summary>
    public async Task CopyTextAsync(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        await _clipboard.SetTextAsync(value, isSecret: false).ConfigureAwait(false);
        lock (_gate)
        {
            // The secret we owned has just been overwritten by our own copy.
            ForgetOwnership();
        }
    }

    /// <summary>Clears the clipboard if it still holds the secret Acorn copied. Returns true if it was cleared.</summary>
    public async Task<bool> ClearIfOwnedAsync()
    {
        byte[]? owned;
        lock (_gate)
        {
            owned = _ownedFingerprint;
        }
        if (owned is null)
        {
            return false;
        }

        var current = await _clipboard.GetTextAsync().ConfigureAwait(false);
        var matches = current is not null && CryptographicOperations.FixedTimeEquals(Fingerprint(current), owned);
        if (matches)
        {
            await _clipboard.ClearAsync().ConfigureAwait(false);
        }

        lock (_gate)
        {
            if (ReferenceEquals(_ownedFingerprint, owned))
            {
                ForgetOwnership();
            }
        }
        return matches;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            ForgetOwnership();
        }
        CryptographicOperations.ZeroMemory(_hmacKey);
    }

    private async Task ClearIfOwnedSafeAsync()
    {
        try
        {
            await ClearIfOwnedAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Timer callback: nothing to report to; the next lock/exit will try again.
        }
    }

    private void ForgetOwnership()
    {
        _clearTimer?.Dispose();
        _clearTimer = null;
        _ownedFingerprint = null;
    }

    private byte[] Fingerprint(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            return HMACSHA256.HashData(_hmacKey, bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
