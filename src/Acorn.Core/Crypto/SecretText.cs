using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Acorn.Core.Crypto;

/// <summary>Converts typed secrets (passwords) to bytes for key derivation.</summary>
public static class SecretText
{
    /// <summary>
    /// Returns UTF-8 bytes of the NFC-normalized text in a pinned array. The caller must zero it.
    /// NFC keeps a password typed on Windows and macOS byte-identical when the keyboard
    /// produces composed vs. decomposed characters.
    /// </summary>
    public static byte[] ToUtf8(ReadOnlySpan<char> text)
    {
        char[]? normalized = null;
        var source = text;
        if (!text.IsNormalized(NormalizationForm.FormC))
        {
            normalized = GC.AllocateArray<char>(text.GetNormalizedLength(NormalizationForm.FormC), pinned: true);
            if (!text.TryNormalize(normalized, out var written, NormalizationForm.FormC))
            {
                throw new ArgumentException("The text could not be normalized.", nameof(text));
            }
            source = normalized.AsSpan(0, written);
        }

        try
        {
            var bytes = Secrets.AllocatePinned(Encoding.UTF8.GetByteCount(source));
            Encoding.UTF8.GetBytes(source, bytes);
            return bytes;
        }
        finally
        {
            if (normalized is not null)
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(normalized.AsSpan()));
            }
        }
    }
}
