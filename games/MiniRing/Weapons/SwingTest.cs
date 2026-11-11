using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Weapons;

/// <summary>1 振りの結果。</summary>
/// <param name="Swing">振り。</param>
/// <param name="Range">振り始めた間合い [m]。</param>
/// <param name="Landed">相手の体に当たった。</param>
/// <param name="Part">最初に当たった部位。</param>
/// <param name="TipSpeed">打ちの間の、棍棒の頭のいちばん速い速さ [m/s]（体に対して。歩く速さは除く）。</param>
/// <param name="HitTipSpeed">最初の打撃の、当たった所の棍棒の速さ [m/s]。</param>
/// <param name="Impulse">相手へ運んだ運動量の合計 [N·s]（打撃ごと: 体の重さの分 + 棍棒そのものの分）。</param>
/// <param name="HeadTwist">頭がねじられたいちばん速い速さ [rad/s]。</param>
/// <param name="Concussion">この 1 振りで相手に溜まった脳しんとう（意識から引く量。0.25 で ふらつく、0.7 で意識が途切れる）。</param>
/// <param name="HeadMiss">打ちの間に棍棒の頭が相手の頭の中心へいちばん近づいた距離 [m]（当たらなかった振りの、外れ方）。</param>
/// <param name="Cells">相手の体から壊したボクセルの数（潰して掘った分 + 切った分）。</param>
/// <param name="SelfConcussion">振った本人に溜まった脳しんとう（自分の棍棒で自分の頭を打った分）。</param>
/// <param name="SelfCells">振った本人の体から、自分の棍棒で壊したボクセルの数。</param>
public sealed record SwingResult(WeaponSwing Swing, float Range, bool Landed, BodyPartKind? Part, float TipSpeed, float HitTipSpeed, float Impulse, float HeadTwist, float Concussion, float HeadMiss, float Cells = 0f, float SelfConcussion = 0f, float SelfCells = 0f)
{
    /// <summary>威力: この振りが相手の意識から奪った量（試合で効く量そのもの）。</summary>
    public float Power => Concussion;

    /// <summary>
    /// 研究で上る坂: 威力が主。頭に当たらない振りは、渡した力積と、頭へどれだけ近づいたかで比べる（当たらない振り同士にも差を付け、
    /// 頭へ向かう方へ探索を導く）。自分を打って自分の意識を削る振りは、その分だけ引く。
    /// </summary>
    public float Fitness => Concussion - SelfConcussion + (0.002f * Impulse) - (0.05f * HeadMiss) - (0.0005f * SelfCells);
}

/// <summary>
/// 振りの実験: 赤（プレイヤー）に棍棒を握らせ、構えて立つ青（CPU）へ間合いを詰めて 1 回だけ振らせ、何が起きたかを測る。
/// 振りは <see cref="ClubBot"/> がそのまま実行する（試合と同じ動き）。
/// </summary>
public static class SwingTest
{
    private const float Dt = 1f / 240f;

    /// <summary>研究で見る、間合いのずれ [m]: 振りは間合い ± これでも試し、平均で比べる（1 点の間合いでしか効かない振りを選ばない）。</summary>
    public const float RangeSpread = 0.02f;

    /// <summary>
    /// 間合い <paramref name="range"/> と、その ± <see cref="RangeSpread"/> の 3 点で振り、数値を平均した結果（当たった部位などは真ん中の
    /// 間合いのもの）。試合では間合いはぴったりには合わないので、研究はこれで比べる。
    /// </summary>
    public static SwingResult RunAround(WeaponSwing swing, float range, bool? twoHanded = null)
    {
        var ranges = new[] { range - RangeSpread, range, range + RangeSpread };
        var results = new SwingResult[ranges.Length];
        Parallel.For(0, ranges.Length, i => results[i] = Run(swing, ranges[i], twoHanded: twoHanded));
        return results[1] with
        {
            TipSpeed = results.Average(r => r.TipSpeed),
            HitTipSpeed = results.Average(r => r.HitTipSpeed),
            Impulse = results.Average(r => r.Impulse),
            HeadTwist = results.Average(r => r.HeadTwist),
            Concussion = results.Average(r => r.Concussion),
            HeadMiss = results.Average(r => r.HeadMiss),
            Cells = results.Average(r => r.Cells),
            SelfConcussion = results.Average(r => r.SelfConcussion),
            SelfCells = results.Average(r => r.SelfCells),
        };
    }

    /// <summary>
    /// 1 振りを試す。<paramref name="afterStep"/> は物理の 1 歩ごとに、時刻と振っている最中かを添えて呼ぶ（動画を撮るため）。
    /// <paramref name="twoHanded"/> で握り方を振りの型と変えられる（両手の振りを片手で振ると、など）。<paramref name="gloves"/> が false なら
    /// 両者ともボクセルのグローブをはめない（構えたグローブの詰め物が打撃を受け止めない）。
    /// </summary>
    public static SwingResult Run(WeaponSwing swing, float range, Action<BoxingGame, float, bool>? afterStep = null, bool? twoHanded = null, bool gloves = true)
    {
        ArgumentNullException.ThrowIfNull(swing);
        var bot = new ClubBot([swing], seed: 1, range: range, rest: 1000f);
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("stand", _ => new StandGuard(), _ => bot, PlayerWeapon: WeaponKind.WoodenClub, PlayerTwoHanded: twoHanded ?? swing.TwoHanded, VoxelGloves: gloves));
        var club = game.WeaponOf(game.Player)!;
        var tip = Weapon.TipOf(club.Kind);
        var concussionBefore = 0f;
        var selfBefore = 0f;
        var selfCells = 0f;
        float tipSpeed = 0f, hitTipSpeed = 0f, impulse = 0f, twist = 0f, miss = float.MaxValue, cells = 0f;
        BodyPartKind? part = null;
        var started = false;
        var finishedAt = float.MaxValue;
        for (var t = 0f; t < 6f && t < finishedAt + 0.3f; t += Dt)
        {
            game.Step(default, Dt);
            var s = game.World.GetState(club.Body.BodyId);
            var torso = game.World.GetState(game.Player.Parts.Chest);
            var tipWorld = s.Position + Vector3.Transform(tip, s.Orientation);
            var speed = (s.LinearVelocity + Vector3.Cross(s.AngularVelocity, tipWorld - s.Position) - torso.LinearVelocity).Length();
            if (bot.Current is not null)
            {
                if (!started)
                {
                    started = true;
                    concussionBefore = game.Cpu.Concussion;
                    selfBefore = game.Player.Concussion;
                }

                tipSpeed = MathF.Max(tipSpeed, speed);
                miss = MathF.Min(miss, Vector3.Distance(tipWorld, game.Cpu.HeadPosition));
            }
            else if (started && finishedAt == float.MaxValue)
            {
                finishedAt = t;
            }

            foreach (var hit in game.WeaponHits.Where(h => h.Attacker == game.Player))
            {
                if (part is null)
                {
                    part = hit.Part;
                    hitTipSpeed = hit.Speed;
                }

                impulse += hit.Momentum;
                cells += hit.Cells;
                twist = MathF.Max(twist, hit.HeadTwist);
            }

            selfCells += game.SelfWeaponHits.Sum(h => h.Cells);
            afterStep?.Invoke(game, t, bot.Current is not null);
        }

        return new SwingResult(swing, range, part is not null, part, tipSpeed, hitTipSpeed, impulse, twist, started ? MathF.Max(0f, game.Cpu.Concussion - concussionBefore) : 0f, started ? miss : 1f, cells,
            started ? MathF.Max(0f, game.Player.Concussion - selfBefore) : 0f, selfCells);
    }
}
