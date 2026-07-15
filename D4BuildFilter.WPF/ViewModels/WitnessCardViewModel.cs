using System.Reflection;
using D4BuildFilter.Core;

namespace D4BuildFilter.WPF.ViewModels;

/// <summary>One truthful row in the share card's color key. Rows are projected from the decoded
/// filter, not from checkbox intent, so a skipped or disabled rule can never be advertised.</summary>
public sealed record WitnessLegendRow(string Label, string ColorName, string ColorHex);

public sealed record WitnessBuildIdentity(string BuildName, string ClassName, string ClassColorHex);

/// <summary>The complete, identity-safe payload consumed by the fixed-size card control.</summary>
public sealed record WitnessCardViewModel(
    string BuildName,
    string ClassName,
    string ClassColorHex,
    string? ProvenanceChip,
    IReadOnlyList<WitnessLegendRow> LegendRows,
    string ImportCode,
    string RuleCountLabel,
    string VersionStamp,
    string DiscordInvite,
    IReadOnlyList<WitnessBuildIdentity> Builds);

public sealed record WitnessCardRequest(
    string BuildName,
    string ClassName,
    string SourceName,
    string? TierKind,
    string? Tier,
    FilterOutput Output,
    int MaxRules,
    string DiscordInvite,
    string? ActiveWarning = null,
    string? VersionOverride = null,
    string? SecondBuildName = null,
    string? SecondClassName = null,
    IReadOnlyList<WitnessBuildIdentity>? BuildIdentities = null);

public sealed record WitnessCardComposition(WitnessCardViewModel? Card, string? BlockReason)
{
    public bool IsBlocked => Card is null;
}

/// <summary>Composes the externally shared card from compiler facts only. The hard copy guard owns
/// the three rejection reasons; an active result warning is an additional fail-closed gate.</summary>
public static class WitnessCardComposer
{
    private static readonly IReadOnlyDictionary<string, string> ClassColors =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Barbarian"] = "#E0845A", ["Druid"] = "#C89655", ["Necromancer"] = "#9ACF7B",
            ["Rogue"] = "#E4CC4A", ["Sorcerer"] = "#7CB6E6", ["Spiritborn"] = "#6FC9B8",
            ["Paladin"] = "#F48CBA", ["Warlock"] = "#B58AE6",
        };

    public static WitnessCardComposition Compose(WitnessCardRequest request)
    {
        var block = CopySafety.BlockReason(request.Output, request.MaxRules);
        if (block is not null) return new(null, block);
        if (request.Output.Diagnostics.Count > 0)
            return new(null, $"⚠ Share card unavailable — {request.Output.Diagnostics[0]}");
        if (!string.IsNullOrWhiteSpace(request.ActiveWarning))
            return new(null, $"⚠ Share card unavailable — {TrimWarning(request.ActiveWarning)}");

        List<WitnessBuildIdentity> builds;
        if (request.BuildIdentities is { Count: > 0 } identities)
            builds = identities.ToList();
        else
        {
            bool hasSecondBuild = request.SecondBuildName is not null;
            bool hasSecondClass = request.SecondClassName is not null;
            if (hasSecondBuild != hasSecondClass)
                return new(null, "⚠ Share card unavailable — the second build identity is incomplete.");
            builds = [Identity(request.BuildName, request.ClassName)];
            if (hasSecondBuild)
                builds.Add(Identity(request.SecondBuildName!, request.SecondClassName!));
        }

        var provenance = builds.Count > 1
            ? $"Super Build · {builds.Count} builds · "
                + (request.Output.HasCustomBuildColors
                    ? "custom colors"
                    : builds.Count == 2 ? "colors by build" : "colors by tier")
            : !string.IsNullOrWhiteSpace(request.SourceName)
            && !string.IsNullOrWhiteSpace(request.TierKind)
            && !string.IsNullOrWhiteSpace(request.Tier)
                ? $"{request.SourceName} · {request.TierKind} · {request.Tier}"
                : null;
        var version = request.VersionOverride ?? ReadVersionStamp();
        var card = new WitnessCardViewModel(
            string.Join(" + ", builds.Select(build => build.BuildName)),
            string.Join(" + ", builds.Select(build => build.ClassName)),
            ClassColors.TryGetValue(request.ClassName, out var classColor) ? classColor : "#D4AF37",
            provenance,
            BuildLegend(request.Output.ImportCode),
            request.Output.ImportCode,
            $"{request.Output.RuleCount} / {request.MaxRules} rules",
            version,
            request.DiscordInvite,
            builds);
        return new(card, null);
    }

    internal static WitnessBuildIdentity Identity(string buildName, string className) => new(
        buildName,
        className,
        ClassColors.TryGetValue(className, out var classColor) ? classColor : "#D4AF37");

    private static string TrimWarning(string warning) => warning.Trim().TrimStart('⚠', ' ');

    private static string ReadVersionStamp()
    {
        var assembly = typeof(WitnessCardComposer).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3) ?? "?";
        var metadata = version.IndexOf('+');
        if (metadata >= 0) version = version[..metadata];
        return $"MedicK's Might v{version}";
    }

    private static IReadOnlyList<WitnessLegendRow> BuildLegend(string importCode)
    {
        var rows = new List<WitnessLegendRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in FilterDecoder.Decode(importCode).Rules.Where(r => r.Visibility == (int)Visibility.Recolor))
        {
            var colorName = FilterColors.TryGetEntry(rule.Color, out var entry)
                ? entry!.FullName
                : FilterColors.NameOf(rule.Color);
            var label = LegendLabel(rule, colorName);
            var key = $"{label}|{rule.Color}";
            if (!seen.Add(key)) continue;
            rows.Add(new(label, colorName, $"#{rule.Color & 0xFFFFFF:X6}"));
        }
        return rows;
    }

    private static string LegendLabel(DecodedRule rule, string colorName)
    {
        var affix = rule.Conditions.FirstOrDefault(c => c.Type == 6 && c.MaskOrCount.HasValue);
        var rarity = rule.Conditions.FirstOrDefault(c => c.Type == 1)?.MaskOrCount;
        if (affix?.MaskOrCount is { } minimum && rarity is { } mask)
        {
            var kind = mask switch
            {
                Rarity.Rare => "rares",
                Rarity.Legendary => "legendaries",
                Rarity.Rare | Rarity.Legendary => "rare / legendary items",
                _ => "items",
            };
            var tierShortLabel = FilterColors.TryGetEntry(rule.Color, out var tierEntry)
                ? tierEntry!.ShortLabel
                : colorName;
            var shortSuffix = string.IsNullOrEmpty(tierShortLabel) ? "" : $" ({tierShortLabel})";
            var baseName = rule.Name.EndsWith(shortSuffix, StringComparison.OrdinalIgnoreCase)
                ? rule.Name[..^shortSuffix.Length]
                : rule.Name;
            var tag = baseName.EndsWith(" Leg", StringComparison.OrdinalIgnoreCase)
                ? baseName[..^4]
                : baseName.EndsWith(" Rare", StringComparison.OrdinalIgnoreCase)
                    ? baseName[..^5]
                    : null;
            return tag is null
                ? $"{minimum}+ build affixes · {kind}"
                : $"{tag} · {minimum}+ build affixes · {kind}";
        }

        var shortLabel = FilterColors.TryGetEntry(rule.Color, out var entry)
            ? entry!.ShortLabel
            : colorName;
        var suffix = $" ({shortLabel})";
        return rule.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? rule.Name[..^suffix.Length]
            : rule.Name;
    }
}
