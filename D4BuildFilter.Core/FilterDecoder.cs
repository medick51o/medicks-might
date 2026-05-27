using System.Text;

namespace D4BuildFilter.Core;

public sealed class DecodedCondition
{
    public int Type { get; set; }
    public List<uint> Ids { get; } = new();
    public ulong? MaskOrCount { get; set; }   // field 4: rarity bitmask, or affix min-count
    public ulong? Count { get; set; }           // field 6: greater-affix count
    public List<string> Unknown { get; } = new(); // any field we don't recognize (discovery aid)
}

public sealed class DecodedRule
{
    public string Name { get; set; } = "";
    public int Visibility { get; set; }
    public uint Color { get; set; }
    public bool Enabled { get; set; }
    public List<DecodedCondition> Conditions { get; } = new();
}

public sealed class DecodedFilter
{
    public string Name { get; set; } = "";
    public int Version { get; set; }
    public int DeclaredRuleCount { get; set; }
    public List<DecodedRule> Rules { get; } = new();
}

/// <summary>
/// Decodes a real or generated D4 import code into a readable structure.
/// Validates our encoder against actual game exports, and — because it records
/// unrecognized condition fields — is how we discover not-yet-mapped condition
/// types (e.g. a per-unique selector). Inverse of <see cref="FilterBuilder"/>.
/// </summary>
public static class FilterDecoder
{
    public static DecodedFilter Decode(string base64) => Decode(Convert.FromBase64String(base64.Trim()));

    public static DecodedFilter Decode(byte[] bytes)
    {
        var f = new DecodedFilter();
        foreach (var fld in ProtobufReader.Read(bytes))
            switch (fld)
            {
                case { Field: 1, WireType: 2 }: f.Rules.Add(DecodeRule(fld.Bytes)); break;
                case { Field: 2, WireType: 2 }: f.Name = Encoding.UTF8.GetString(fld.Bytes); break;
                case { Field: 3, WireType: 0 }: f.DeclaredRuleCount = (int)fld.VarintValue; break;
                case { Field: 4, WireType: 0 }: f.Version = (int)fld.VarintValue; break;
            }
        return f;
    }

    private static DecodedRule DecodeRule(byte[] b)
    {
        var r = new DecodedRule();
        foreach (var fld in ProtobufReader.Read(b))
            switch (fld)
            {
                case { Field: 1, WireType: 2 }: r.Name = Encoding.UTF8.GetString(fld.Bytes); break;
                case { Field: 2, WireType: 0 }: r.Visibility = (int)fld.VarintValue; break;
                case { Field: 3, WireType: 5 }: r.Color = fld.Fixed32Value; break;
                case { Field: 4, WireType: 2 }: r.Conditions.Add(DecodeCondition(fld.Bytes)); break;
                case { Field: 5, WireType: 0 }: r.Enabled = fld.VarintValue != 0; break;
            }
        return r;
    }

    private static DecodedCondition DecodeCondition(byte[] b)
    {
        var c = new DecodedCondition();
        foreach (var fld in ProtobufReader.Read(b))
            switch (fld)
            {
                case { Field: 1, WireType: 0 }: c.Type = (int)fld.VarintValue; break;
                case { Field: 2, WireType: 5 }: c.Ids.Add(fld.Fixed32Value); break;
                case { Field: 4, WireType: 0 }: c.MaskOrCount = fld.VarintValue; break;
                case { Field: 6, WireType: 0 }: c.Count = fld.VarintValue; break;
                default:
                    c.Unknown.Add($"field{fld.Field}/wt{fld.WireType}=" +
                        (fld.WireType == 0 ? fld.VarintValue.ToString()
                         : fld.WireType == 5 ? "0x" + fld.Fixed32Value.ToString("x8")
                         : Convert.ToHexString(fld.Bytes)));
                    break;
            }
        return c;
    }

    // Verified against real published filters (rootsxo Minion Barb).
    private static readonly Dictionary<int, string> TypeNames = new()
    {
        [1] = "Rarity", [2] = "ItemPower/Ancestral", [3] = "Codex", [4] = "GreaterAffix",
        [5] = "ItemTypes", [6] = "Affixes", [8] = "Unique(byId)", [9] = "CharmSet(byId)",
    };

    public static string Describe(DecodedFilter f)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Filter \"{f.Name}\"  version={f.Version}  declaredRules={f.DeclaredRuleCount}  actualRules={f.Rules.Count}");
        for (int i = 0; i < f.Rules.Count; i++)
        {
            var r = f.Rules[i];
            string vis = r.Visibility switch { 0 => "Show", 2 => "Recolor", 3 => "HideAll", _ => $"vis{r.Visibility}" };
            // D4 color is ARGB (0xAARRGGBB) — match the encoder so decoded colors are true-to-game.
            byte rr = (byte)(r.Color >> 16), g = (byte)(r.Color >> 8), bb = (byte)(r.Color), a = (byte)(r.Color >> 24);
            sb.AppendLine($"  [{i}] \"{r.Name}\"  {vis}  color=#{rr:X2}{g:X2}{bb:X2} a={a}  enabled={r.Enabled}");
            foreach (var c in r.Conditions)
            {
                string tn = TypeNames.GetValueOrDefault(c.Type, $"UNKNOWN({c.Type})");
                var parts = new List<string> { $"type={c.Type}({tn})" };
                if (c.Ids.Count > 0) parts.Add($"ids=[{string.Join(",", c.Ids.Select(id => "0x" + id.ToString("x6")))}]");
                if (c.Type == 1 && c.MaskOrCount is { } m) parts.Add($"rarityMask=0x{m:x2}");
                else if (c.MaskOrCount is { } mc) parts.Add($"minCount={mc}");
                if (c.Count is { } cc) parts.Add($"count={cc}");
                if (c.Unknown.Count > 0) parts.Add($"UNKNOWN[{string.Join("; ", c.Unknown)}]");
                sb.AppendLine($"        {string.Join("  ", parts)}");
            }
        }
        return sb.ToString();
    }
}
