using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace D4BuildFilter.WPF.ViewModels;

/// <summary>One class's filter checkbox on the landing page. All classes are enabled by default;
/// unchecking hides every chip of that class across all three tier-list sources. Label color is
/// the build chip's class color so the checkbox row reads as a class palette legend.</summary>
public sealed partial class ClassFilterVM : ObservableObject
{
    public string ClassName { get; }
    public Brush ClassColor { get; }
    /// <summary>Unicode glyph picked to evoke the class — fits Segoe UI Symbol on Windows.
    /// Doubles up the class-color cue with a SHAPE so the filter row reads at a glance even
    /// for players who can't quickly distinguish, say, gold-Paladin from yellow-Rogue.</summary>
    public string Glyph { get; }

    // Field-initialised to true so the OnIsEnabledChanged partial doesn't fire during ctor
    // (and trigger N spurious refreshes as MainViewModel builds the filter strip).
    [ObservableProperty] private bool _isEnabled = true;

    private readonly Action _onChanged;

    public ClassFilterVM(string className, Action onChanged)
    {
        ClassName = className;
        ClassColor = TierBuildVM.ClassBrush(className);
        Glyph = GlyphFor(className);
        _onChanged = onChanged;
    }

    partial void OnIsEnabledChanged(bool value) => _onChanged();

    private static string GlyphFor(string cls) => cls switch
    {
        "Barbarian"   => "⚔",   // crossed swords (raw melee)
        "Druid"       => "❀",   // flower (nature/shapeshift)
        "Necromancer" => "☠",   // skull
        "Rogue"       => "⚹",   // sextile (daggers/strike)
        "Sorcerer"    => "✦",   // 4-point star (spell spark)
        "Spiritborn"  => "❂",   // sun with rays (spirit guardians)
        "Paladin"     => "⚜",   // fleur-de-lis (holy order)
        "Warlock"     => "⛤",   // pentagram (occult pact)
        _             => "●",
    };
}
