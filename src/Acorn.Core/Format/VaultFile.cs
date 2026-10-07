using Acorn.Core.Crypto;

namespace Acorn.Core.Format;

/// <summary>A parsed vault file: header, the exact header bytes (payload AAD) and the encrypted payload.</summary>
public sealed class VaultFile
{
    internal VaultFile(VaultHeader header, byte[] headerBytes, AeadBox payload)
    {
        Header = header;
        HeaderBytes = headerBytes;
        Payload = payload;
    }

    public VaultHeader Header { get; }

    /// <summary>Header bytes exactly as stored in the file. Used verbatim as the payload AAD.</summary>
    public ReadOnlyMemory<byte> HeaderBytes { get; }

    public AeadBox Payload { get; }
}
