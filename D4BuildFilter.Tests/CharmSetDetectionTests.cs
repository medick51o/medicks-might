using D4BuildFilter.Core;
using D4BuildFilter.WPF.ViewModels;
using Xunit;

namespace D4BuildFilter.Tests;

/// <summary>
/// v1.0.5: charm-SET detection for the two sources that historically shipped none.
/// Mobalytics carries equipped charms as builder slots (gameEntity.type "charms", the charm's
/// ITEM name on gameEntity.title); D4Builds carries a per-variant `charms` array naming the set
/// verbatim. Both must land in ResolvedVariant.TalismanSets like Maxroll's planner extraction.
/// </summary>
public class CharmSetDetectionTests
{
    // ── the item-name → set bridge (Mobalytics path) ──

    [Theory]
    [InlineData("Phoba of the Crucible", "Berserker's Crucible")]
    [InlineData("Berú of Sescheron's Fury", "Sescheron's Fury")]  // non-ASCII item name
    [InlineData("phoba of the crucible", "Berserker's Crucible")] // case-insensitive
    public void TryGetByItemName_ResolvesMemberCharmToItsSet(string item, string expectedSet)
    {
        Assert.True(TalismanSetDatabase.TryGetByItemName(item, out var set));
        Assert.Equal(expectedSet, set.Name);
    }

    [Theory]
    [InlineData("Endurant Faith")]   // a UNIQUE charm — not a set member
    [InlineData("Seal")]
    [InlineData("")]
    public void TryGetByItemName_RejectsNonSetItems(string item)
        => Assert.False(TalismanSetDatabase.TryGetByItemName(item, out _));

    // ── Mobalytics: charm slots → TalismanSets ──

    [Fact]
    public void Mobalytics_ExtractsCharmSets_FromBuilderSlots()
    {
        // minimal page: one variant, one set charm + one unique charm + one unique item slot
        const string html = """
            <html><script>window.__PRELOADED_STATE__ = {
              "x": { "userGeneratedDocumentBySlug": { "data": {
                "tags": { "data": [ { "groupSlug": "class", "name": "Barbarian" } ] },
                "data": {
                  "name": "Test WW",
                  "buildVariants": { "values": [ {
                    "id": "v1",
                    "genericBuilder": { "slots": [
                      { "gameSlotSlug": "season-12-charm-1",
                        "gameEntity": { "type": "charms", "slug": "phoba-of-the-crucible",
                                        "title": "Phoba of the Crucible", "entity": {} } },
                      { "gameSlotSlug": "season-12-charm-2",
                        "gameEntity": { "type": "charms", "slug": "endurant-faith",
                                        "title": "Endurant Faith", "entity": {} } },
                      { "gameSlotSlug": "helm",
                        "gameEntity": { "type": "uniqueItems",
                                        "entity": { "title": "Harlequin Crest" },
                                        "modifiers": { "gearStats": [ { "id": "maximum-life" } ] } } }
                    ] }
                  } ] }
                }
              } } }
            };</script></html>
            """;

        var build = MobalyticsFetcher.Parse(html);

        var v = Assert.Single(build.Variants);
        Assert.NotNull(v.TalismanSets);
        var set = Assert.Single(v.TalismanSets!);           // unique charm must NOT produce a set
        Assert.Equal("Berserker's Crucible", set);
        Assert.Contains("Harlequin Crest", v.Uniques);      // unique extraction untouched
        Assert.Contains("maximum life", v.Affixes);         // affix extraction untouched
    }

    // ── D4Builds: per-variant charms[] → TalismanSets ──

    [Fact]
    public void D4Builds_ExtractsCharmSets_SkippingHiddenAndDupes()
    {
        // minimal Firestore doc: two visible charms of the same set + one hidden other-set charm
        const string doc = """
            { "fields": {
                "name": { "stringValue": "Test WW" },
                "class": { "stringValue": "Barbarian" },
                "variants": { "arrayValue": { "values": [ { "mapValue": { "fields": {
                    "variantName": { "stringValue": "Endgame" },
                    "charms": { "arrayValue": { "values": [
                        { "mapValue": { "fields": {
                            "name": { "stringValue": "Fer of the Crucible" },
                            "set":  { "stringValue": "Berserker's Crucible" },
                            "hide": { "booleanValue": false } } } },
                        { "mapValue": { "fields": {
                            "name": { "stringValue": "Linta of the Crucible" },
                            "set":  { "stringValue": "Berserker's Crucible" },
                            "hide": { "booleanValue": false } } } },
                        { "mapValue": { "fields": {
                            "name": { "stringValue": "Phoba of Arreat" },
                            "set":  { "stringValue": "Arms of Arreat" },
                            "hide": { "booleanValue": true } } } }
                    ] } }
                } } } ] } }
            } }
            """;

        var build = D4BuildsFetcher.Parse(doc);

        var v = Assert.Single(build.Variants);
        Assert.NotNull(v.TalismanSets);
        var set = Assert.Single(v.TalismanSets!);           // deduped + hidden one skipped
        Assert.Equal("Berserker's Crucible", set);
    }

    [Fact]
    public void D4Builds_NoCharms_YieldsNullTalismanSets()
    {
        const string doc = """
            { "fields": {
                "name": { "stringValue": "Old Doc" },
                "class": { "stringValue": "Barbarian" },
                "variants": { "arrayValue": { "values": [ { "mapValue": { "fields": {
                    "variantName": { "stringValue": "Endgame" }
                } } } ] } }
            } }
            """;

        var build = D4BuildsFetcher.Parse(doc);
        Assert.Null(Assert.Single(build.Variants).TalismanSets);   // old fallback behavior intact
    }

    [Fact]
    public void D4Builds_UnknownSet_DegradesToUndetectedAndPreservesWarningName()
    {
        const string doc = """
            { "fields": {
                "name": { "stringValue": "Future Build" },
                "class": { "stringValue": "Barbarian" },
                "variants": { "arrayValue": { "values": [ { "mapValue": { "fields": {
                    "variantName": { "stringValue": "Endgame" },
                    "charms": { "arrayValue": { "values": [
                        { "mapValue": { "fields": {
                            "set": { "stringValue": "Renamed Crucible" },
                            "hide": { "booleanValue": false } } } }
                    ] } }
                } } } ] } }
            } }
            """;

        var build = D4BuildsFetcher.Parse(doc);
        var variant = Assert.Single(build.Variants);

        Assert.Null(variant.TalismanSets); // drives MainViewModel's all-class-sets checked fallback
        Assert.Equal(new[] { "Renamed Crucible" }, variant.UnknownTalismanSets);
        var selected = TalismanSetDatabase.DefaultSelectionForClass(
            "Barbarian", variant.TalismanSets ?? [], out bool noneDetected);
        Assert.True(noneDetected);
        Assert.Equal(TalismanSetDatabase.ForClass("Barbarian"), selected); // every box starts checked
        Assert.Equal(
            "Build lists charm set 'Renamed Crucible' this app doesn't know — showing all sets; app data may be a season behind.",
            TalismanSetDatabase.UnknownSetWarning(variant.UnknownTalismanSets!));
    }

    [Fact]
    public void D4Builds_KnownSet_IsCanonicalizedAndRemainsDetected()
    {
        const string doc = """
            { "fields": {
                "name": { "stringValue": "Known Build" },
                "class": { "stringValue": "Barbarian" },
                "variants": { "arrayValue": { "values": [ { "mapValue": { "fields": {
                    "variantName": { "stringValue": "Endgame" },
                    "charms": { "arrayValue": { "values": [
                        { "mapValue": { "fields": {
                            "set": { "stringValue": "berserker's crucible" } } } }
                    ] } }
                } } } ] } }
            } }
            """;

        var build = D4BuildsFetcher.Parse(doc);
        var compiled = FilterCompiler.Analyze(build, FilterColors.Red, FilterColors.Pink);

        Assert.Equal(new[] { "Berserker's Crucible" }, compiled.TalismanSets);
        Assert.Null(Assert.Single(build.Variants).UnknownTalismanSets);
        var selected = TalismanSetDatabase.DefaultSelectionForClass(
            "Barbarian", compiled.TalismanSets, out bool noneDetected);
        Assert.False(noneDetected);
        Assert.Equal("Berserker's Crucible", Assert.Single(selected).Name);
    }

    [Fact]
    public void MainViewModel_MixedKnownAndUnknownSets_FailsOpenAndNamesUnknownSet()
    {
        var vm = new MainViewModel(startTierListFetches: false);
        var build = new ResolvedBuild("Drifted Build", "Barbarian",
        [
            new ResolvedVariant("Endgame", ["Strength"], [], TalismanSets: ["Berserker's Crucible"],
                UnknownTalismanSets: ["Renamed Crucible"])
        ]);

        vm.Ingest(build, "Test");

        Assert.NotEmpty(vm.TalismanSetOptions);
        Assert.All(vm.TalismanSetOptions, option => Assert.True(option.IsChecked));
        Assert.True(vm.CharmSetsUndetected);
        Assert.Contains("Renamed Crucible", vm.CharmSetSafetyNote);
    }

    [Fact]
    public void MainViewModel_TransitionIntoUnknownSetFallback_DiscardsPriorChecks()
    {
        var vm = new MainViewModel(startTierListFetches: false);
        var build = new ResolvedBuild("Variant Drift", "Barbarian",
        [
            new ResolvedVariant("Known", ["Strength"], [],
                TalismanSets: ["Berserker's Crucible"]),
            new ResolvedVariant("Drifted", ["Maximum Life"], [],
                UnknownTalismanSets: ["Renamed Crucible"])
        ]);

        vm.Ingest(build, "Test");
        var drifted = vm.Variants.Single(v => v.Variant.Name == "Drifted");
        drifted.IsSelected = false;

        var narrowed = vm.TalismanSetOptions.First(o => o.Set.Name != "Berserker's Crucible");
        narrowed.IsChecked = false;
        Assert.False(vm.TalismanSetOptions.Single(o => o.Set.Id == narrowed.Set.Id).IsChecked);
        Assert.False(vm.CharmSetsUndetected);

        drifted.IsSelected = true;

        Assert.True(vm.CharmSetsUndetected);
        Assert.All(vm.TalismanSetOptions, option => Assert.True(option.IsChecked));
        Assert.Contains("Renamed Crucible", vm.CharmSetSafetyNote);
    }

    [Fact]
    public void Mobalytics_MixedKnownAndUnknownCharmTitles_RecordDriftAndFailOpen()
    {
        const string html = """
            <html><script>window.__PRELOADED_STATE__ = {
              "x": { "userGeneratedDocumentBySlug": { "data": {
                "tags": { "data": [ { "groupSlug": "class", "name": "Barbarian" } ] },
                "data": { "name": "Future Build", "buildVariants": { "values": [ {
                  "id": "v1", "genericBuilder": { "slots": [
                    { "gameSlotSlug": "season-charm-0",
                      "gameEntity": { "type": "charms", "title": "Phoba of Applied Alchemy" } },
                    { "gameSlotSlug": "season-charm-1",
                      "gameEntity": { "type": "charms", "title": "Future Set Charm" } },
                    { "gameSlotSlug": "helm",
                      "gameEntity": { "type": "aspects", "entity": {},
                        "modifiers": { "gearStats": [ { "id": "maximum-life" } ] } } }
                  ] } } ] } }
              } } }
            };</script></html>
            """;

        var build = MobalyticsFetcher.Parse(html);
        var variant = Assert.Single(build.Variants);
        Assert.Equal(["Applied Alchemy"], variant.TalismanSets);
        Assert.Equal(["Future Set Charm"], variant.UnknownTalismanSets);

        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(build, "Test");
        Assert.True(vm.CharmSetsUndetected);
        Assert.All(vm.TalismanSetOptions, option => Assert.True(option.IsChecked));
        var green = FilterDecoder.Decode(vm.ImportCode).Rules
            .Single(r => r.Name == "Charms & Seals (Green)");
        Assert.Contains(green.Conditions, c => c.Type == 5
            && c.Ids.Contains(ItemTypes.Charm) && c.Ids.Contains(ItemTypes.Seal));
        Assert.DoesNotContain(green.Conditions, c => c.Type == 9);
    }

    [Fact]
    public void Maxroll_SetShapedUnknownId_IsRecordedAlongsideKnownSet()
    {
        const string inner = """
            {"profiles":[{"name":"Main","items":{"0":"1","1":"2"}}],
             "items":{
               "1":{"id":"Talisman_Charm_Set_Rogue_03_01","explicits":[]},
               "2":{"id":"Talisman_Charm_Set_Rogue_99_01","explicits":[]}}}
            """;
        var raw = $$"""{"name":"Future Rogue","class":"Rogue","data":{{System.Text.Json.JsonSerializer.Serialize(inner)}}}""";

        var variant = Assert.Single(MaxrollFetcher.Parse(
            raw, NameLookup.Default(), UniqueLookup.Default()).Variants);

        Assert.Equal(["Applied Alchemy"], variant.TalismanSets);
        Assert.Equal(["Talisman_Charm_Set_Rogue_99_01"], variant.UnknownTalismanSets);
    }
}
