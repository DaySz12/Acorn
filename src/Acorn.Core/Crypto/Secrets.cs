using System.Security.Cryptography;

namespace Acorn.Core.Crypto;

/// <summary>Helpers for buffers that hold key material.</summary>
public static class Secrets
{
    /// <summary>
    /// Allocates a pinned array so the GC never moves (and therefore never copies) the key material.
    /// </summary>
    public static byte[] AllocatePinned(int length) => GC.AllocateArray<byte>(length, pinned: true);

    public static void Zero(byte[]? buffer)
    {
        if (buffer is not null)
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }
}
