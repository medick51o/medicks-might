using System.Text;

namespace D4BuildFilter.Core;

/// <summary>
/// Protobuf wire-format primitives for Diablo 4's loot-filter import code.
/// The game stores filters as base64(protobuf). These helpers emit the three
/// wire types the format uses: varint (0), 32-bit little-endian (5), and
/// length-delimited (2). Field/value semantics live in <see cref="D4Filter"/> types.
/// </summary>
public static class Wire
{
    public static byte[] Varint(ulong value)
    {
        var bytes = new List<byte>(10);
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0) b |= 0x80;
            bytes.Add(b);
        }
        while (value != 0);
        return bytes.ToArray();
    }

    private static byte[] Tag(int field, int wireType) => Varint(((ulong)field << 3) | (uint)wireType);

    /// <summary>Varint field (wire type 0).</summary>
    public static byte[] Efv(int field, ulong value) => Concat(Tag(field, 0), Varint(value));

    /// <summary>Fixed 32-bit field, little-endian (wire type 5).</summary>
    public static byte[] Ef32(int field, uint value) => Concat(Tag(field, 5), new[]
    {
        (byte)(value & 0xFF),
        (byte)((value >> 8) & 0xFF),
        (byte)((value >> 16) & 0xFF),
        (byte)((value >> 24) & 0xFF),
    });

    /// <summary>Length-delimited bytes field (wire type 2).</summary>
    public static byte[] Efb(int field, byte[] data) => Concat(Tag(field, 2), Varint((ulong)data.Length), data);

    /// <summary>Length-delimited UTF-8 string field (wire type 2).</summary>
    public static byte[] Efs(int field, string value) => Efb(field, Encoding.UTF8.GetBytes(value));

    public static byte[] Concat(params byte[][] arrays)
    {
        int len = 0;
        foreach (var a in arrays) len += a.Length;
        var result = new byte[len];
        int pos = 0;
        foreach (var a in arrays) { Buffer.BlockCopy(a, 0, result, pos, a.Length); pos += a.Length; }
        return result;
    }
}
