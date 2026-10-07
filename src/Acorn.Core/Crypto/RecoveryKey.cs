using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Acorn.Core.Crypto;

/// <summary>
/// 200 random bits shown once as 8 groups of 5 Crockford Base32 characters
/// (no I, L, O or U). Never stored in plaintext.
/// </summary>
public sealed class RecoveryKey : IDisposable
{
    public const int ByteLength = 25;
    public const int GroupSize = 5;
    public const int GroupCount = 8;
    private const int SymbolCount = GroupSize * GroupCount;
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private readonly byte[] _bytes;
    private bool _disposed;

    private RecoveryKey(byte[] bytes) => _bytes = bytes;

    public static RecoveryKey Generate()
    {
        var bytes = Secrets.AllocatePinned(ByteLength);
        RandomNumberGenerator.Fill(bytes);
        return new RecoveryKey(bytes);
    }

    /// <summary>
    /// Parses user input. Case, spaces and hyphens are ignored; O is read as 0 and I/L as 1.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> text, [NotNullWhen(true)] out RecoveryKey? key)
    {
        key = null;
        var bytes = Secrets.AllocatePinned(ByteLength);
        int bitBuffer = 0, bitCount = 0, byteIndex = 0, symbols = 0;

        foreach (var c in text)
        {
            if (c is '-' or ' ' or '\t' or '\r' or '\n')
            {
                continue;
            }

            var value = DecodeSymbol(c);
            if (value < 0 || symbols == SymbolCount)
            {
                Secrets.Zero(bytes);
                return false;
            }

            symbols++;
            bitBuffer = (bitBuffer << 5) | value;
            bitCount += 5;
            if (bitCount >= 8)
            {
                bitCount -= 8;
                bytes[byteIndex++] = (byte)(bitBuffer >> bitCount);
                bitBuffer &= (1 << bitCount) - 1;
            }
        }

        if (symbols != SymbolCount)
        {
            Secrets.Zero(bytes);
            return false;
        }

        key = new RecoveryKey(bytes);
        return true;
    }

    /// <summary>Formats as XXXXX-XXXXX-... for display. The returned string cannot be wiped; show it once.</summary>
    public string ToDisplayString()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Span<char> chars = stackalloc char[SymbolCount + GroupCount - 1];
        int bitBuffer = 0, bitCount = 0, written = 0, symbols = 0;

        foreach (var b in _bytes)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitCount += 8;
            while (bitCount >= 5)
            {
                bitCount -= 5;
                if (symbols > 0 && symbols % GroupSize == 0)
                {
                    chars[written++] = '-';
                }
                chars[written++] = Alphabet[(bitBuffer >> bitCount) & 31];
                symbols++;
            }
            bitBuffer &= (1 << bitCount) - 1;
        }

        var result = new string(chars[..written]);
        chars.Clear();
        return result;
    }

    internal ReadOnlySpan<byte> Bytes
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _bytes;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Secrets.Zero(_bytes);
            _disposed = true;
        }
    }

    /// <summary>
    /// Canonical form of typed recovery-key text (upper case, no separators, O→0, I/L→1) so a
    /// confirmation step can compare what the user typed with the displayed groups.
    /// </summary>
    public static string NormalizeInput(ReadOnlySpan<char> text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '-' or ' ' or '\t' or '\r' or '\n')
            {
                continue;
            }
            var value = DecodeSymbol(c);
            builder.Append(value < 0 ? char.ToUpperInvariant(c) : Alphabet[value]);
        }
        return builder.ToString();
    }

    private static int DecodeSymbol(char c)
    {
        var upper = char.ToUpperInvariant(c);
        return upper switch
        {
            'O' => 0,
            'I' or 'L' => 1,
            _ => Alphabet.IndexOf(upper),
        };
    }
}
