using System.Numerics;
using MiniRing.Bots;
using MiniRing.Voxels;

namespace MiniRing.Weapons;

/// <summary>
/// 剣の技を 1 回出した結果（<see cref="SwordDrill.RunAround"/> では、間合い ± <see cref="SwingTest.RangeSpread"/> の 3 点の平均。
/// 切り落とした・殺したは、その割合）。
/// </summary>
/// <param name="Technique">技（間合いはドリルで試した値）。</param>
/// <param name="TargetCells">狙った部位から斬り取ったボクセルの数（切り落とした先の破片に残るセルは含まない: 落とした分は <paramref name="Severed"/>）。</param>
/// <param name="Severed">狙った部位を切り落とした（1 = 落とした）。</param>
/// <param name="Killed">相手が死んだ（臓器・脳が半分以上壊れた）。</param>
/// <param name="Concussion">相手に溜まった脳しんとう。</param>
/// <param name="OtherCells">狙った部位の外から斬り取ったボクセルの数。</param>
/// <param name="Miss">打ちの間に刃（握りから切っ先まで）が狙った部位の中心へいちばん近づいた距離 [m]。</param>
/// <param name="TipSpeed">打ちの間の切っ先のいちばん速い速さ [m/s]（体に対して）。</param>
/// <param name="FirstPart">最初に斬った部位（何も斬らなければ null）。</param>
/// <param name="SelfConcussion">自分の剣で自分に溜めた脳しんとう。</param>
/// <param name="SelfCells">自分の剣で自分の体から失ったボクセルの数（斬り取った分 + 切り落とされた先）。</param>
public sealed record SwordResult(SwordTechnique Technique, float TargetCells, float Severed, float Killed, float Concussion, float OtherCells,
    float Miss, float TipSpeed, BodyPartKind? FirstPart, float SelfConcussion, float SelfCells)
{
    /// <summary>
    /// 技の効き: 狙った部位をどれだけ壊したか。斬り取ったボクセル 60 個で 1、狙った部位を切り落とせば 2、殺せば 3 を足し、頭を狙う技
    /// なら脳しんとうもそのまま足す（ほかの部位を狙う技が、狙いを外れて頭を打った分は数えない）。
    /// </summary>
    public float Effect => (TargetCells / 60f) + (2f * Severed) + (3f * Killed) + (Technique.Target == BodyPartKind.Head ? Concussion : 0f);

    /// <summary>
    /// ドリルで上る坂: 効きが主。狙った部位の外を斬った分は 1/4 だけ数え（ガードの上からでも削れば次につながる）、狙った部位へ
    /// 届かない振りは近づいた距離で比べる。自分を斬った・打った分は引く。
    /// </summary>
    public float Fitness => Effect + (0.25f * OtherCells / 60f) - Miss - SelfConcussion - (SelfCells / 60f);
}

/// <summary>
/// 剣の技のドリル: 赤（プレイヤー）に剣を握らせ、構えて立つ青（CPU、両手のグローブで顔を守る）へ技の間合いまで詰めて、
/// 技を 1 回だけ出させ、狙った部位に何が起きたかを測る。技は <see cref="ClubBot"/> がそのまま実行する（試合と同じ動き）。
/// </summary>
public static class SwordDrill
{
    private const float Dt = 1f / 240f;

    /// <summary>間合い ± <see cref="SwingTest.RangeSpread"/> の 3 点で技を出し、平均した結果（最初に斬った部位は真ん中の間合いのもの）。</summary>
    public static SwordResult RunAround(SwordTechnique technique)
    {
        ArgumentNullException.ThrowIfNull(technique);
        var ranges = new[] { technique.Range - SwingTest.RangeSpread, technique.Range, technique.Range + SwingTest.RangeSpread };
        var results = new SwordResult[ranges.Length];
        Parallel.For(0, ranges.Length, i => results[i] = Run(technique with { Range = ranges[i] }));
        return results[1] with
        {
            Technique = technique,
            TargetCells = results.Average(r => r.TargetCells),
            Severed = results.Average(r => r.Severed),
            Killed = results.Average(r => r.Killed),
            Concussion = results.Average(r => r.Concussion),
            OtherCells = results.Average(r => r.OtherCells),
            Miss = results.Average(r => r.Miss),
            TipSpeed = results.Average(r => r.TipSpeed),
            SelfConcussion = results.Average(r => r.SelfConcussion),
            SelfCells = results.Average(r => r.SelfCells),
        };
    }

    /// <summary>技を 1 回出す。<paramref name="afterStep"/> は物理の 1 歩ごとに、時刻と技を出している最中かを添えて呼ぶ（動画を撮るため）。</summary>
    public static SwordResult Run(SwordTechnique technique, Action<BoxingGame, float, bool>? afterStep = null)
    {
        ArgumentNullException.ThrowIfNull(technique);
        // 技の間合いぴったりで出す（始めの立ち位置が近ければ、下がってから）。
        var bot = new ClubBot([technique.Swing], seed: 1, range: technique.Range, rest: 1000f, exact: true);
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("stand", _ => new StandGuard(), _ => bot, PlayerWeapon: WeaponKind.IronSword));
        var sword = game.WeaponOf(game.Player)!;
        var grip = Weapon.GripOf(sword.Kind);
        var tip = Weapon.TipOf(sword.Kind);
        var victim = game.Cpu.Voxels!;
        var target = technique.Target;
        var targetStart = Cells(victim, target, fragments: true);
        var allStart = Cells(victim, null, fragments: true);
        var ownStart = Cells(game.Player.Voxels!, null, fragments: true);
        float concussionBefore = 0f, selfBefore = 0f, tipSpeed = 0f, miss = float.MaxValue;
        BodyPartKind? first = null;
        var started = false;
        var finishedAt = float.MaxValue;
        for (var t = 0f; t < 6f && t < finishedAt + 0.3f; t += Dt)
        {
            game.Step(default, Dt);
            var s = game.World.GetState(sword.Body.BodyId);
            var torso = game.World.GetState(game.Player.Parts.Chest);
            var tipWorld = s.Position + Vector3.Transform(tip, s.Orientation);
            var gripWorld = s.Position + Vector3.Transform(grip, s.Orientation);
            if (bot.Current is not null)
            {
                if (!started)
                {
                    started = true;
                    concussionBefore = game.Cpu.Concussion;
                    selfBefore = game.Player.Concussion;
                }

                tipSpeed = MathF.Max(tipSpeed, (s.LinearVelocity + Vector3.Cross(s.AngularVelocity, tipWorld - s.Position) - torso.LinearVelocity).Length());
                foreach (var part in victim.Parts.Where(p => p.Kind == target))
                {
                    miss = MathF.Min(miss, DistanceToSegment(game.World.GetState(part.Body.BodyId).Position, gripWorld, tipWorld));
                }
            }
            else if (started && finishedAt == float.MaxValue)
            {
                finishedAt = t;
            }

            if (first is null)
            {
                foreach (var cut in game.RecentCuts)
                {
                    if (cut.RemovedB.Count > 0 && victim.Parts.Concat(victim.Fragments).FirstOrDefault(p => p.Body == cut.B) is { } struck)
                    {
                        first = struck.Kind;
                        break;
                    }
                }
            }

            afterStep?.Invoke(game, t, bot.Current is not null);
        }

        var targetLost = targetStart - Cells(victim, target, fragments: true);
        var allLost = allStart - Cells(victim, null, fragments: true);
        return new SwordResult(technique, targetLost, victim.Fragments.Any(f => f.Kind == target) ? 1f : 0f, game.Cpu.IsDead ? 1f : 0f,
            started ? MathF.Max(0f, game.Cpu.Concussion - concussionBefore) : 0f, allLost - targetLost, started ? miss : 1f, tipSpeed, first,
            started ? MathF.Max(0f, game.Player.Concussion - selfBefore) : 0f, ownStart - Cells(game.Player.Voxels!, null, fragments: false)); // 自分の体を切り落としたら、落ちた分も自分を斬った分
    }

    /// <summary>体に残っているボクセルの数（<paramref name="kind"/> が null なら全部。<paramref name="fragments"/> なら切り落とされた破片も）。</summary>
    private static int Cells(BoxerBody body, BodyPartKind? kind, bool fragments)
    {
        var parts = fragments ? body.Parts.Concat(body.Fragments) : body.Parts;
        return parts.Where(p => kind is null || p.Kind == kind).Sum(p => p.Body.Grid.FilledCount);
    }

    private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var t = ab.LengthSquared() > 1e-12f ? Math.Clamp(Vector3.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f) : 0f;
        return Vector3.Distance(p, a + (ab * t));
    }
}
