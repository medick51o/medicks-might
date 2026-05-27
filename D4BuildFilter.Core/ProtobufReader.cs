namespace D4BuildFilter.Core;

/// <summary>One decoded protobuf field (the value lives in whichever property matches WireType).</summary>
public sealed class ProtoField
{
    public int Field { get; init; }
    public int WireType { get; init; }
    public ulong VarintValue { get; init; }
    public uint Fixed32Value { get; init; }
    public byte[] Bytes { get; init; } = Array.Empty<byte>();
}

/// <summary>
/// Order-agnostic protobuf reader. Used to round-trip our own output and, later,
/// to decode real in-game filter exports (Phase 0c). Supports the wire types the
/// loot-filter format uses: 0 (varint), 2 (length-delimited), 5 (fixed32).
/// </summary>
public static class ProtobufReader
{
    public static List<ProtoField> Read(byte[] data)
    {
        var fields = new List<ProtoField>();
        int i = 0;
        while (i < data.Length)
        {
            ulong tag = ReadVarint(data, ref i);
            int field = (int)(tag >> 3);
            int wire = (int)(tag & 0x7);
            switch (wire)
            {
                case 0:
                    fields.Add(new ProtoField { Field = field, WireType = 0, VarintValue = ReadVarint(data, ref i) });
                    break;
                case 5:
                    uint v = (uint)(data[i] | (data[i + 1] << 8) | (data[i + 2] << 16) | (data[i + 3] << 24));
                    i += 4;
                    fields.Add(new ProtoField { Field = field, WireType = 5, Fixed32Value = v });
                    break;
                case 2:
                    int len = (int)ReadVarint(data, ref i);
                    var slice = new byte[len];
                    Buffer.BlockCopy(data, i, slice, 0, len);
                    i += len;
                    fields.Add(new ProtoField { Field = field, WireType = 2, Bytes = slice });
                    break;
                default:
                    throw new InvalidDataException($"Unsupported protobuf wire type {wire} at byte {i}");
            }
        }
        return fields;
    }

    public static ulong ReadVarint(byte[] data, ref int i)
    {
        ulong result = 0;
        int shift = 0;
        while (true)
        {
            byte b = data[i++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        return result;
    }
}
