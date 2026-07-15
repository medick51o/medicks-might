using D4BuildFilter.Core;
using System.Text;
using Xunit;

namespace D4BuildFilter.Tests;

public class FilterColorRegistryTests
{
    private const string CustomColorAdvisory =
        "A rule uses an unregistered color; its rule-name suffix was safely shown as (Custom).";

    public static TheoryData<uint, string, string, bool> AllColors => new()
    {
        { FilterColors.Make(220, 0, 0), "Red", "Red", false },
        { FilterColors.Make(255, 105, 180), "Pink", "Pink", false },
        { FilterColors.Make(255, 215, 0), "Gold", "Gold", false },
        { FilterColors.Make(170, 170, 170), "Silver", "Silver", false },
        { FilterColors.Make(255, 140, 0), "Orange", "Orange", false },
        { FilterColors.Make(0, 255, 255), "Cyan", "Cyan", false },
        { FilterColors.Make(34, 81, 232), "Blue", "Blue", false },
        { FilterColors.Make(0, 200, 0), "Green", "Green", false },
        { FilterColors.Make(255, 255, 255), "White", "White", false },
        { FilterColors.Make(160, 32, 240), "Purple", "Purple", false },
        { FilterColors.Make(64, 224, 208), "Turquoise", "Turq", false },
        { FilterColors.Make(144, 238, 144), "Light Green", "LtGrn", false },
        { FilterColors.Make(173, 216, 230), "Light Blue", "LtBlu", false },
        { FilterColors.Make(135, 206, 235), "Sky Blue", "SkyBlu", false },
        { FilterColors.Make(31, 81, 255), "Neon Blue", "NeonBl", true },
        { FilterColors.Make(200, 158, 240), "Light Purple", "LtPurp", false },
        { FilterColors.Make(255, 179, 71), "Light Orange", "LtOrng", false },
        { FilterColors.Make(16, 16, 16), "Black", "Black", true },
    };

    [Theory]
    [MemberData(nameof(AllColors))]
    public void Registry_round_trips_every_entry(uint value, string fullName, string shortLabel,
        bool darkGroundRisk)
    {
        var entry = Assert.Single(FilterColors.Registry, candidate => candidate.Value == value);

        Assert.Equal(fullName, entry.FullName);
        Assert.Equal(shortLabel, entry.ShortLabel);
        Assert.Equal(darkGroundRisk, entry.DarkGroundRisk);
        Assert.Equal(shortLabel, FilterColors.NameOf(value));
    }

    [Fact]
    public void Existing_NameOf_outputs_are_byte_identical()
    {
        var expected = new (uint Value, string Label)[]
        {
            (FilterColors.Red, "Red"),
            (FilterColors.Pink, "Pink"),
            (FilterColors.Gold, "Gold"),
            (FilterColors.Silver, "Silver"),
            (FilterColors.Orange, "Orange"),
            (FilterColors.Cyan, "Cyan"),
            (FilterColors.Blue, "Blue"),
            (FilterColors.Green, "Green"),
            (FilterColors.White, "White"),
            (FilterColors.Purple, "Purple"),
        };

        foreach (var (value, label) in expected)
            Assert.Equal(label, FilterColors.NameOf(value));
    }

    [Fact]
    public void Unregistered_color_throw_mode_is_loud_in_every_configuration()
    {
        var custom = FilterColors.Make(1, 2, 3);
        var build = new CompiledBuild("Custom Color", "Rogue", custom, custom,
            [1u], new Dictionary<uint, string> { [1u] = "Test Affix" }, [], [], [], [], [], [], []);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FilterCompiler.Compile([build], new FilterOptions(), "Custom Color", "Custom Color",
                UnregisteredColorBehavior.Throw));
    }

    [Fact]
    public void Unregistered_color_fallback_mode_is_safe_and_named_in_every_configuration()
    {
        var custom = FilterColors.Make(1, 2, 3);
        var build = new CompiledBuild("Custom Color", "Rogue", custom, custom,
            [1u], new Dictionary<uint, string> { [1u] = "Test Affix" }, [], [], [], [], [], [], []);

        var output = FilterCompiler.Compile([build], new FilterOptions(), "Custom Color", "Custom Color",
            UnregisteredColorBehavior.Fallback);

        Assert.True(output.IsCopyable);
        Assert.Empty(output.Diagnostics);
        Assert.Equal([CustomColorAdvisory], output.Advisories);
        Assert.Contains("(Custom)", Encoding.UTF8.GetString(Convert.FromBase64String(output.ImportCode)));
    }

    [Fact]
    public void Registry_enforces_suffix_bounds_and_unique_values_and_labels()
    {
        Assert.Equal(18, FilterColors.Registry.Count);
        Assert.All(FilterColors.Registry, entry =>
        {
            Assert.InRange(entry.ShortLabel.Length, 1, 6);
            Assert.InRange($"({entry.ShortLabel})".Length, 3, 8);
        });
        Assert.Equal(FilterColors.Registry.Count,
            FilterColors.Registry.Select(entry => entry.Value & 0x00FFFFFFu).Distinct().Count());
        Assert.Equal(FilterColors.Registry.Count,
            FilterColors.Registry.Select(entry => entry.ShortLabel).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
