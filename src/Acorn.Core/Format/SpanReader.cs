using System.Buffers.Binary;

namespace Acorn.Core.Format;

/// <summary>Bounds-checked little-endian reader. Any overrun is reported as a format error.</summary>
internal ref struct SpanReader
{
    private readonly ReadOnlySpan<byte> _data;

    public SpanReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        Position = 0;
    }

    public int Position { get; private set; }

    public readonly int Remaining => _data.Length - Position;

    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        if (count < 0 || count > Remaining)
        {
            throw new VaultFormatException("The vault file is truncated or malformed.");
        }
        var slice = _data.Slice(Position, count);
        Position += count;
        return slice;
    }

    public byte ReadByte() => ReadBytes(1)[0];

    public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(ReadBytes(2));

    public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(ReadBytes(4));
}
