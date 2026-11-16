using MiniRing.Characters;
using MiniRing.Desktop;

namespace MiniRing.Tests;

/// <summary>
/// E0（キャラクターを 1 人 1 ファイルに）の「教育が空なら今と同じ試合」の物差し。名簿のキャラ同士の試合を
/// ゲームと同じ道（<see cref="MatchSelection.Corners"/>）で作り、固定の入力で回して、状態のチェックサムを返す。
/// 期待値は、名簿を 1 人 1 ファイルにする前（characters.json の時代）に <see cref="CharacterSamenessProbe"/> で取ったもの。
/// </summary>
internal static class CharacterSamenessCases
{
    internal sealed record Case(string Name, MatchMode Mode, string Red, string Blue, bool Referee, float Seconds);

    /// <summary>拳同士・空手・剣（刀・ロングソード）・こんぼう・カタール・籠手を含む組、レフェリーあり/なし、観戦と 1P。</summary>
    internal static readonly Case[] All =
    [
        new("glove-glove-referee", MatchMode.Spectate, "marcus", "dmitri", true, 12f),
        new("glove-glove-noref", MatchMode.Spectate, "marcus", "dmitri", false, 20f),
        new("karate-swarmer-noref", MatchMode.Spectate, "akira", "ramon", false, 20f),
        new("katana-longsword-noref", MatchMode.Spectate, "jin", "konrad", false, 12f),
        new("club-bare-noref", MatchMode.Spectate, "sean", "albert", false, 15f),
        new("katar-gauntlet-referee", MatchMode.Spectate, "vikram", "gaston", true, 12f),
        new("single-counter-vs-knuckles-noref", MatchMode.Single, "yoandry", "joao", false, 15f),
        new("single-karate-vs-katana", MatchMode.Single, "akira", "jin", false, 10f),
    ];

    /// <summary>武器の扱いの教科書を JSON へ移す前後で同じ試合になることの物差し（2026-11-16、weapon handling 段 1）。刀・ロングソード・こんぼうの全組み合わせ（自分同士を含む）、レフェリーあり/なし。</summary>
    internal static readonly Case[] Weapons =
    [
        new("w-katana-katana-referee", MatchMode.Spectate, "jin", "jin", true, 20f),
        new("w-longsword-longsword-noref", MatchMode.Spectate, "konrad", "konrad", false, 20f),
        new("w-katana-longsword-referee", MatchMode.Spectate, "jin", "konrad", true, 25f),
        new("w-club-katana-noref", MatchMode.Spectate, "sean", "jin", false, 25f),
        new("w-longsword-club-referee", MatchMode.Spectate, "konrad", "sean", true, 25f),
        new("w-club-club-noref", MatchMode.Spectate, "sean", "sean", false, 20f),
    ];

    /// <summary>
    /// 武器の教科書を JSON へ移した時点（LogicVersion 30）までの値（技の時計なし）。この値で戦わせると、教科書を JSON へ移す前の
    /// 試合とビット単位で同じ（<see cref="Run"/> の <c>legacyWeapons</c>）。今の教科書は、刀・ロングソードが LogicVersion 31 で鍛えた値（技の時計）なので、剣の試合は同じにならない（こんぼうは同じ）。
    /// </summary>
    internal static MiniRing.Bots.WeaponStyle? LegacyWeaponStyle(MiniRing.Weapons.WeaponKind? weapon) =>
        MiniRing.Bots.WeaponCatalog.HasBot(weapon) ? MiniRing.Bots.WeaponCatalog.Textbook(weapon!.Value) with { Pacing = null } : null;

    internal static (ulong Half, ulong End) Run(Case c, bool legacyWeapons = false)
    {
        var selection = new MatchSelection(c.Mode, CharacterRoster.Get(c.Red), CharacterRoster.Get(c.Blue), c.Referee);
        var corners = selection.Corners;
        if (legacyWeapons)
        {
            corners = corners with { PlayerWeaponStyle = LegacyWeaponStyle(corners.PlayerWeapon), CpuWeaponStyle = LegacyWeaponStyle(corners.CpuWeapon) };
        }

        using var game = new BoxingGame(corners: corners);
        var steps = (int)(c.Seconds * 60f);
        var half = 0UL;
        for (var i = 1; i <= steps; i++)
        {
            game.Step(default, 1f / 60f);
            if (i == steps / 2)
            {
                half = game.StateChecksum();
            }
        }

        return (half, game.StateChecksum());
    }
}
