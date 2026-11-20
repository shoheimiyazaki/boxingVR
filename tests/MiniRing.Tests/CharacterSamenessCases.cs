using MiniRing.Characters;
using MiniRing.Desktop;

namespace MiniRing.Tests;

/// <summary>
/// 名簿のキャラ同士の試合をゲームと同じ道（<see cref="MatchSelection.Corners"/>）で作り、固定の入力で回して、状態のチェックサムを返す。
/// <see cref="CharacterSamenessTests"/>（同じビルドの中の相対比較）と、調べもの（<see cref="CharacterSamenessProbe"/>・LegLossProbe）が使う。
/// 2026-11-20 まではここの組の最終チェックサムを金型として持っていたが、版ごとの取り直しをやめた（金型は無い）。
/// </summary>
internal static class CharacterSamenessCases
{
    internal sealed record Case(string Name, MatchMode Mode, string Red, string Blue, bool Referee, float Seconds);

    /// <summary>拳同士・空手・剣（刀・ロングソード）・こんぼう・カタール・籠手を含む組、レフェリーあり/なし、観戦と 1P。</summary>
    internal static readonly Case[] All =
    [
        new("glove-glove-referee", MatchMode.Spectate, "marcus", "dmitri", true, 20.14f),
        new("glove-glove-noref", MatchMode.Spectate, "marcus", "dmitri", false, 33.57f),
        new("karate-swarmer-noref", MatchMode.Spectate, "akira", "ramon", false, 33.57f),
        new("katana-longsword-noref", MatchMode.Spectate, "jin", "konrad", false, 20.14f),
        new("club-bare-noref", MatchMode.Spectate, "sean", "albert", false, 25.18f),
        new("katar-gauntlet-referee", MatchMode.Spectate, "vikram", "gaston", true, 20.14f),
        new("single-counter-vs-knuckles-noref", MatchMode.Single, "yoandry", "joao", false, 25.18f),
        new("single-karate-vs-katana", MatchMode.Single, "akira", "jin", false, 16.78f),
    ];

    /// <summary>武器の扱いの教科書を JSON へ移す前後で同じ試合になることの物差し（2026-11-16、weapon handling 段 1）。刀・ロングソード・こんぼうの全組み合わせ（自分同士を含む）、レフェリーあり/なし。</summary>
    internal static readonly Case[] Weapons =
    [
        new("w-katana-katana-referee", MatchMode.Spectate, "jin", "jin", true, 33.57f),
        new("w-longsword-longsword-noref", MatchMode.Spectate, "konrad", "konrad", false, 33.57f),
        new("w-katana-longsword-referee", MatchMode.Spectate, "jin", "konrad", true, 41.96f),
        new("w-club-katana-noref", MatchMode.Spectate, "sean", "jin", false, 41.96f),
        new("w-longsword-club-referee", MatchMode.Spectate, "konrad", "sean", true, 41.96f),
        new("w-club-club-noref", MatchMode.Spectate, "sean", "sean", false, 33.57f),
    ];

    /// <summary>
    /// 武器の教科書を JSON へ移した時点（LogicVersion 30）までの値（技の時計なし）。この値で戦わせると、教科書を JSON へ移す前の
    /// 試合とビット単位で同じ（<see cref="Run"/> の <c>legacyWeapons</c>）。今の教科書は、刀・ロングソードが LogicVersion 31 で鍛えた値（技の時計）なので、剣の試合は同じにならない（こんぼうは同じ）。
    /// </summary>
    internal static MiniRing.Bots.WeaponStyle? LegacyWeaponStyle(MiniRing.Weapons.WeaponKind? weapon) =>
        MiniRing.Bots.WeaponCatalog.HasBot(weapon) ? MiniRing.Bots.WeaponCatalog.Textbook(weapon!.Value) with { Pacing = null } : null;

    /// <summary>
    /// 教育から戦略の層（<c>education.style.GamePlan</c>、LogicVersion 50）を外したキャラ。名簿の教育は GamePlan だけなので、外せば教科書そのもので戦う
    /// （GamePlan が無ければボットの判断はビット単位で今までと同じことを、今までのチェックサムで確かめるため）。
    /// </summary>
    internal static FighterCharacter WithoutGamePlan(FighterCharacter c)
    {
        var style = (System.Text.Json.Nodes.JsonObject)c.Education.Style.DeepClone();
        style.Remove("GamePlan");
        return c with { Education = c.Education with { Style = style } };
    }

    /// <param name="c">組。</param>
    /// <param name="legacyWeapons">武器の教科書を鍛える前の値で戦わせる。</param>
    /// <param name="gamePlan">名簿の教育の戦略の層を使う（false なら外す: <see cref="WithoutGamePlan"/>）。</param>
    internal static (ulong Half, ulong End) Run(Case c, bool legacyWeapons = false, bool gamePlan = true)
    {
        FighterCharacter Pick(string id) => gamePlan ? CharacterRoster.Get(id) : WithoutGamePlan(CharacterRoster.Get(id));
        var selection = new MatchSelection(c.Mode, Pick(c.Red), Pick(c.Blue), c.Referee);
        var corners = selection.Corners;
        if (legacyWeapons)
        {
            corners = corners with { PlayerWeaponStyle = LegacyWeaponStyle(corners.PlayerWeapon), CpuWeaponStyle = LegacyWeaponStyle(corners.CpuWeapon) };
        }

        return Play(corners, c.Seconds);
    }

    /// <summary>この両コーナーの試合を、入力なしで <paramref name="seconds"/> 秒（1/60 秒のホスト歩）回し、途中（半分）と最後のチェックサムを返す。</summary>
    internal static (ulong Half, ulong End) Play(BoxingCorners corners, float seconds)
    {
        using var game = new BoxingGame(corners: corners);
        var steps = (int)(seconds * 60f);
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
