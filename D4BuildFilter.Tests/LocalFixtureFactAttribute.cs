using Xunit;

namespace D4BuildFilter.Tests;

/// <summary>A <see cref="FactAttribute"/> that SKIPS (reported as skipped, never a false pass) when
/// a machine-local fixture file isn't present. Replaces the old `if (!File.Exists(...)) return;`
/// pattern, which reported "Passed" while asserting nothing — a vacuous green that hid the fact
/// that the only full-real-page parser coverage wasn't running.</summary>
public sealed class LocalFixtureFactAttribute : FactAttribute
{
    public LocalFixtureFactAttribute(string fixturePath)
    {
        if (!File.Exists(fixturePath))
            Skip = $"Local fixture not present: {fixturePath} (stash one from a live fetch to run this).";
    }
}
