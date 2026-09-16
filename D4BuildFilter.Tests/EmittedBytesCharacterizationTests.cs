using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using D4BuildFilter.Core;
using Xunit;

namespace D4BuildFilter.Tests;

/// <summary>
/// Pins SHA-256 of the exact UTF-8 ImportCode string, not decoded or normalized bytes.
/// Run on the pre-change revision first, inspect emitted-bytes.observed.json, and manually
/// promote it to emitted-bytes.golden.json before running on the changed revision.
/// This test never creates or updates the golden file. Equal hashes characterize emission;
/// they do not establish that a filter is correct or accepted by the game.
/// </summary>
public class EmittedBytesCharacterizationTests
{
    private static readonly object ObservedFileLock = new();
    private static bool observedFileWritten;

    // Options and ordered inputs are recreated for each observation. Catalogs used here are
    // compiled-in tables; BuildTagger uses its embedded resource, not a local override file.
    private static IReadOnlyList<(string Name, FilterOptions Options)> Cases() =>
    [
        ("defaults", new FilterOptions()),
        ("per-slot", new FilterOptions { PerSlotRules = true }),
        ("leveling", new FilterOptions { Leveling = true, PerSlotRules = true }),
        ("talisman-empty", new FilterOptions { TalismanSets = Array.Empty<TalismanSet>() }),
        ("talisman-one", new FilterOptions
        {
            TalismanSets = new[] { TalismanSetDatabase.ById[0x22fb15u] },
        }),
        ("talisman-two", new FilterOptions
        {
            TalismanSets = new[]
            {
                TalismanSetDatabase.ById[0x22fb15u],
                TalismanSetDatabase.ById[0x22fb41u],
            },
        }),
        ("charms-non-ancestral", new FilterOptions { CharmsSealsAncestral = false }),
        ("unique-targeting-off", new FilterOptions { BuildUniques = false }),
        ("unique-hide-list", new FilterOptions
        {
            // Includes a build unique: the purple rescue must stay ahead of the hide.
            HideUniques = new[] { 0x17d2dcu, 0x02d7abu },
        }),
        ("unique-charms-subset", new FilterOptions
        {
            // Accord of the Wilds, Anathema of the Primes, Arcadia (catalog order).
            UniqueCharms = new[] { 0x276d9eu, 0x2790c7u, 0x27824du },
        }),
        ("unique-charms-empty", new FilterOptions { UniqueCharms = Array.Empty<uint>() }),
        ("cube-bases", new FilterOptions { CubeBases = true }),
        ("tier-gates", new FilterOptions
        {
            RedAncestralOnly = true, RedMinPower900 = true,
            PinkAncestralOnly = true, PinkMinPower900 = true,
        }),
        ("nine-hundred-only", NineHundredOnlyOptions()),
        ("nine-hundred-only-per-slot", NineHundredOnlyOptions() with { PerSlotRules = true }),
        ("super-two", new FilterOptions()),
        ("super-three", new FilterOptions()),
        ("super-custom-colors", new FilterOptions()),
    ];

    // Only strings go through xUnit discovery/serialization; no compilation or file writes
    // happen during discovery, and every row gets a stable, individually selectable name.
    public static IEnumerable<object[]> CaseNames =>
        Cases().Select(c => new object[] { c.Name });

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Import_code_matches_pre_cleanup_golden(string caseName)
    {
        var cases = Cases();
        var testCase = cases.Single(c => c.Name == caseName);
        var goldensDirectory = FindGoldensDirectory();
        var goldenPath = Path.Combine(goldensDirectory, "emitted-bytes.golden.json");

        if (!File.Exists(goldenPath))
        {
            var observedPath = Path.Combine(goldensDirectory, "emitted-bytes.observed.json");
            WriteObservedGoldens(observedPath);
            Assert.True(false,
                $"Case '{caseName}': golden file is absent: {goldenPath}. " +
                $"All observed hashes were written to {observedPath}. " +
                "A human must review and promote observations from the pre-cleanup revision; " +
                "this test never seeds its own expectations.");
            return;
        }

        var actual = Observe(testCase.Name, testCase.Options);
        using var document = JsonDocument.Parse(File.ReadAllText(goldenPath));
        Assert.True(document.RootElement.ValueKind == JsonValueKind.Object,
            $"Case '{caseName}': {goldenPath} must be a JSON object mapping case names to hashes.");
        var root = document.RootElement;
        var knownNames = cases.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            Assert.True(knownNames.Contains(property.Name),
                $"Case '{caseName}': golden file contains unknown case '{property.Name}'.");
            Assert.True(seenNames.Add(property.Name),
                $"Case '{caseName}': golden file contains duplicate case '{property.Name}'.");
        }

        Assert.True(root.TryGetProperty(caseName, out var stored),
            $"Case '{caseName}': missing golden hash; actual SHA-256: {actual}.");
        Assert.True(stored.ValueKind == JsonValueKind.String,
            $"Case '{caseName}': expected a SHA-256 hex string; actual SHA-256: {actual}.");
        var expected = stored.GetString();
        Assert.True(expected is { Length: 64 } && expected.All(Uri.IsHexDigit),
            $"Case '{caseName}': invalid expected SHA-256 '{expected}'; actual SHA-256: {actual}.");
        Assert.True(string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase),
            $"Case '{caseName}': emitted ImportCode changed. Expected SHA-256: {expected}; actual SHA-256: {actual}.");
    }

    private static void WriteObservedGoldens(string observedPath)
    {
        lock (ObservedFileLock)
        {
            if (observedFileWritten) return;
            // Write the entire table even if only one theory row was selected. Never rely on
            // execution order or read/merge an old observed file from a previous revision.
            var observed = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var (name, options) in Cases())
                observed.Add(name, Observe(name, options));
            Directory.CreateDirectory(Path.GetDirectoryName(observedPath)!);
            File.WriteAllText(observedPath,
                JsonSerializer.Serialize(observed, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            observedFileWritten = true;
        }
    }

    private static string Observe(string caseName, FilterOptions options)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            // Compiler rule names interpolate numbers. Pin only this execution context,
            // never DefaultThreadCurrentCulture, and restore even when an assertion fails.
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            var output = FilterCompiler.Compile(BuildsFor(caseName), options,
                "characterization", "Emitted bytes");
            Assert.True(output.IsCopyable && !string.IsNullOrEmpty(output.ImportCode),
                $"Case '{caseName}': no payload to characterize. {string.Join("; ", output.Diagnostics)}");
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(output.ImportCode)));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static IReadOnlyList<CompiledBuild> BuildsFor(string caseName)
    {
        var primary = SlottedBarbarian();
        if (caseName is not ("super-two" or "super-three" or "super-custom-colors"))
            return new[] { primary };

        // Same Analyze/ResolvedBuild fixture shape as FilterCompilerTests.MultiBuilds
        // and SuperBuildTests.BuildWithLoot, with fixed order and no ViewModel/fetcher.
        var rogue = FilterCompiler.Analyze(
            new ResolvedBuild("Heartseeker", "Rogue", new[]
            {
                new ResolvedVariant("v1",
                    new[] { "Dexterity", "Maximum Life", "Critical Strike Chance",
                            "Vulnerable Damage Multiplier", "Movement Speed" },
                    new[] { "Harlequin Crest", "Banished Lord's Talisman" }),
            }), FilterColors.Red, FilterColors.Pink);
        if (caseName == "super-custom-colors")
            return new[]
            {
                primary,
                rogue with
                {
                    ChaseColorOverride = FilterColors.LightBlue,
                    KeeperColorOverride = FilterColors.LightPurple,
                    ChaseColorUserChosen = true,
                    KeeperColorUserChosen = true,
                },
            };
        if (caseName == "super-two") return new[] { primary, rogue };

        var sorcerer = FilterCompiler.Analyze(
            new ResolvedBuild("Lightning", "Sorcerer", new[]
            {
                new ResolvedVariant("v1",
                    new[] { "Intelligence", "Cooldown Reduction", "Maximum Life" },
                    new[] { "Tyrael's Might" }),
            }), FilterColors.Red, FilterColors.Pink);
        return new[] { primary, rogue, sorcerer };
    }

    // Adapt FilterCompilerTests.LevelingSampleBuild and SampleBuild (both are private).
    // Every pooled affix is covered by these slots, so PerSlotRules really emits slot rules.
    private static CompiledBuild SlottedBarbarian() => FilterCompiler.Analyze(
        new ResolvedBuild("Berserker", "Barbarian", new[]
        {
            new ResolvedVariant("v1",
                new[] { "Strength", "Maximum Life", "Critical Strike Chance", "Vulnerable Damage Multiplier" },
                new[] { "Banished Lord's Talisman" },
                new[]
                {
                    new ResolvedSlot("Boots", new[] { "Strength", "Maximum Life", "Armor", "Movement Speed" }),
                    new ResolvedSlot("Ring", new[] { "Critical Strike Chance", "Vulnerable Damage Multiplier", "Attack Speed" }),
                }),
        }), FilterColors.Red, FilterColors.Pink);

    // Same enabled selections as FilterCompilerTests.NineHundredOnlyOptions (private).
    private static FilterOptions NineHundredOnlyOptions() => new()
    {
        NineHundredOnly = true,
        NineHundredOnlyLegendaries = true,
        NineHundredOnlyRares = true,
        NineHundredOnlyUniques = true,
        NineHundredOnlyHelm = true,
        NineHundredOnlyChest = true,
        NineHundredOnlyTwoHandedWeapon = true,
        NineHundredOnlyOneHandedWeapon = true,
        NineHundredOnlyGloves = true,
        NineHundredOnlyPants = true,
        NineHundredOnlyBoots = true,
        NineHundredOnlyRing = true,
        NineHundredOnlyAmulet = true,
    };

    private static string FindGoldensDirectory()
    {
        // Normal dotnet test output lives below the checkout. Resolve from this assembly,
        // not the runner's working directory; no csproj content/copy entry is necessary.
        for (DirectoryInfo? directory = new DirectoryInfo(
                 Path.GetDirectoryName(typeof(EmittedBytesCharacterizationTests).Assembly.Location)!);
             directory is not null; directory = directory.Parent)
        {
            var projectDirectory = Path.Combine(directory.FullName, "D4BuildFilter.Tests");
            if (File.Exists(Path.Combine(projectDirectory, "D4BuildFilter.Tests.csproj")))
                return Path.Combine(projectDirectory, "Goldens");
        }
        throw new DirectoryNotFoundException(
            "Cannot locate D4BuildFilter.Tests above the test assembly. Run this test from a checkout's build output.");
    }
}
