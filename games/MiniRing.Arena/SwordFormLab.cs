using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>試合と同じ手作りの型をドリルへ渡す。古いモーションキャプチャの一覧と混同しない。</summary>
internal static class SwordFormLab
{
    public static SwordLibraryLab.Library Library()
    {
        using var game = new BoxingGame(SwordStance.Tuning);
        var weapons = new[] { WeaponKind.Katana, WeaponKind.Longsword };
        return new(weapons.ToDictionary(w => w, FormSwordsman.Guard),
            [.. weapons.SelectMany(w => FormSwordsman.Techniques(w, game.Player.ArmReach, 0f))]);
    }
}
