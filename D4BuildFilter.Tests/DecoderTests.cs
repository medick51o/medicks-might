using System.Linq;
using D4BuildFilter.Core;
using Xunit;

namespace D4BuildFilter.Tests;

/// <summary>Locks our decoder against a REAL in-game crit filter (a community export). A rule
/// literally named "Crit Chance" encodes 0x1beace — this is the ground truth that proved our
/// earlier IDs were mislabeled. If the decoder or IDs regress, these break.</summary>
public class DecoderTests
{
    // Real in-game "GR Crit Filter" (rules trimmed to the crit ones).
    private const string CritFilter =
        "CiMKDkFuY2VzdHJhbCBHZWFyEAAdAAD//yIICAAghAcohAcoAQorChBDRG1nIGFuZCBDQ2hhbmNlEAId6CLY" +
        "/yIOCAYVzuobABXS6hsAIAIoAQohCgtDcml0IERhbWFnZRACHeg6Iv8iCQgGFdLqGwAgASgACiEKC0NyaXQg" +
        "Q2hhbmNlEAIdAAD//yIJCAYVzuobACABKAEKUAoXTm8gQ3JpdCBETUcgTXVsdGlwbGllcnMQAh3U6CL/Iiw" +
        "IBhWA/BsAFdAKJwAV+QonABX1CicAFfsKJwAV9wonABX/CicAFf0KJwAgASgA";

    [Fact]
    public void Crit_Chance_rule_uses_0x1beace()
    {
        var f = FilterDecoder.Decode(CritFilter);
        var rule = f.Rules.Single(r => r.Name == "Crit Chance");
        var cond = rule.Conditions.Single(c => c.Type == 6);
        Assert.Contains(0x001beaceu, cond.Ids);
    }

    [Fact]
    public void Crit_Damage_rule_uses_0x1bead2()
    {
        var f = FilterDecoder.Decode(CritFilter);
        var rule = f.Rules.Single(r => r.Name == "Crit Damage");
        Assert.Contains(0x001bead2u, rule.Conditions.Single(c => c.Type == 6).Ids);
    }

    [Fact]
    public void Item_power_range_decodes_min_and_max()
    {
        // The "Ancestral Gear" rule is an Item Power Range (type 0) min 900 / max 900.
        var f = FilterDecoder.Decode(CritFilter);
        var rule = f.Rules.Single(r => r.Name == "Ancestral Gear");
        var cond = rule.Conditions.Single(c => c.Type == 0);
        Assert.Equal(900u, (uint)cond.MaskOrCount!.Value);   // field4 = min
        Assert.Equal(900u, (uint)cond.Max!.Value);            // field5 = max
    }
}
