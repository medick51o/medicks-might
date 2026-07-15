namespace D4BuildFilter.Core;

/// <summary>Keeps known-rejected filter code out of the clipboard. The UI must explain a refusal:
/// a silent no-op looks like a successful copy and sends the player to a doomed in-game import.</summary>
public static class CopySafety
{
    public static string? BlockReason(FilterOutput output, int maxRules)
    {
        if (!output.IsCopyable || string.IsNullOrEmpty(output.ImportCode))
        {
            var diagnostic = output.Diagnostics.LastOrDefault(message =>
                message.StartsWith("No filter code was produced", StringComparison.Ordinal));
            return diagnostic is null
                ? "⚠ Not copied — no safe filter code was generated. Review the warning and keep 'Hide the rest' off until the build has mapped affixes."
                : $"⚠ Not copied — {diagnostic}";
        }

        if (output.RuleCount > maxRules)
            return $"⚠ Not copied — this code has {output.RuleCount} rules; Diablo 4 rejects filters over {maxRules} on import.";

        if (!output.RoundTripOk)
            return "⚠ Not copied — this code failed its corruption check. Regenerate before importing.";

        if (output.Diagnostics.Count > 0)
            return $"⚠ Not copied — {output.Diagnostics[0]}";

        return null;
    }
}
