using D4BuildFilter.Core;
using Xunit;

namespace D4BuildFilter.Core.Tests;

/// <summary>In-game filter exports are base64 protobuf. A string the game accepts must import,
/// re-export with the same rules, and merge into the shared 25-rule layout.</summary>
public class FilterImportTests
{
    public static IEnumerable<object[]> ExportFiles =>
    [
        ["dok_5variant_target.b64"],
        ["dok_endgame_push_25.b64"],
        ["dok_spin2w_app_export.b64"],
        ["dok_5variant_plus_spin2w.b64"],
    ];

    [Theory]
    [MemberData(nameof(ExportFiles))]
    public void Each_attached_export_imports_and_round_trips(string file)
    {
        var text = Fixture(file);
        Assert.True(FilterExchange.LooksLikeImportCode(text));
        Assert.True(FilterExchange.TryImport(text, out var decoded));
        Assert.InRange(decoded.Rules.Count, 1, 25);
        Assert.Contains(decoded.Rules, rule => rule.Visibility is 0 or 2 or 3);

        var again = FilterDecoder.Decode(FilterExchange.Export(decoded));
        Assert.True(FilterExchange.SemanticallyEqual(decoded, again));
    }

    [Fact]
    public void Affix_paste_is_not_treated_as_a_filter_export()
    {
        Assert.False(FilterExchange.LooksLikeImportCode("Maximum Life\nCritical Strike Chance\n"));
        Assert.False(FilterExchange.LooksLikeImportCode("not a filter"));
    }

    [Fact]
    public void Merging_the_five_variant_export_with_the_app_export_matches_the_hand_merge()
    {
        var fiveVariant = Import("dok_5variant_target.b64");
        var appExport = Import("dok_spin2w_app_export.b64");
        var want = Import("dok_5variant_plus_spin2w.b64");
        var merged = FilterExchange.MergeIntoLayout([fiveVariant, appExport]);

        Assert.True(merged.IsCopyable);
        Assert.InRange(merged.RuleCount, 1, 25);
        var got = FilterDecoder.Decode(merged.ImportCode);
        Assert.Equal(want.Rules.Select(rule => rule.Name), got.Rules.Select(rule => rule.Name));
        for (int i = 0; i < want.Rules.Count; i++)
            AssertSuperset(want.Rules[i], got.Rules[i]);
    }

    private static DecodedFilter Import(string file)
    {
        Assert.True(FilterExchange.TryImport(Fixture(file), out var decoded));
        return decoded;
    }

    internal static void AssertSuperset(DecodedRule want, DecodedRule got)
    {
        Assert.Equal(want.Name, got.Name);
        Assert.Equal(want.Visibility, got.Visibility);
        Assert.Equal(want.Conditions.Select(condition => condition.Type),
            got.Conditions.Select(condition => condition.Type));
        for (int i = 0; i < want.Conditions.Count; i++)
        {
            var expected = want.Conditions[i];
            var actual = got.Conditions[i];
            if (expected.Type is 5 or 6 or 8 or 9)
            {
                var missing = expected.Ids.Except(actual.Ids).ToArray();
                Assert.True(missing.Length == 0,
                    $"{want.Name} type {expected.Type} is missing {string.Join(",", missing.Select(id => "0x" + id.ToString("x")))}");
            }
        }
    }

    internal static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)).Trim();
}
