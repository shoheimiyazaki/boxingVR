using System.Numerics;
using MiniRing.Bots;
using MiniRing.Weapons;
using MiniRing.Voxels;

namespace MiniRing.Motion;

/// <summary>
/// 動きの技を 1 回出した結果: 相手の部位ごとに失わせたボクセル（斬り取った分と、切り落とした先）、切り落とした部位、死んだか、
/// 最初に斬った部位、自分を斬った分、出した時の間合い。<c>MonouchiCells</c> は刃の物打ち（鍔から切っ先までの先の 4 割）で斬り取った
/// ボクセル（剣は物打ちで斬る: 鍔元や手で押し付けて削った分は数えない）。
/// </summary>
public sealed record StrikeResult(MotionTechnique Technique, IReadOnlyDictionary<BodyPartKind, int> CellsByPart, IReadOnlyList<BodyPartKind> Severed,
    bool Killed, BodyPartKind? FirstPart, int SelfCells, float Concussion, float DistanceAtStart, int MonouchiCells = 0)
{
    /// <summary>
    /// 間合いの決め手: 物打ちで斬ったボクセル / 60 + 脳しんとう − 自分を斬った分 / 60。剣士は物打ちが相手に届く間合いから打つ
    /// （近すぎれば鍔元で押し付けるだけになる）。切り落とし・死は数えない: 近すぎる間合いで鍔元を押し付けても腕は落ちるので、それで
    /// 間合いを選ぶと近すぎる間合いを選んでしまう。
    /// </summary>
    public float MaaiScore => (MonouchiCells / 60f) + Concussion - (SelfCells / 60f);

    /// <summary>失わせたボクセルの合計。</summary>
    public int Cells => CellsByPart.Values.Sum();

    /// <summary>いちばん多く失わせた部位（何も斬らなければ null）。</summary>
    public BodyPartKind? MostCut => CellsByPart.Count == 0 ? null : CellsByPart.MaxBy(kv => kv.Value).Key;

    /// <summary>効き: 失わせたボクセル / 60 + 切り落とし × 2 + 死 × 3 + 脳しんとう − 自分を斬った分 / 60。</summary>
    public float Effect => (Cells / 60f) + (2f * Severed.Count) + (Killed ? 3f : 0f) + Concussion - (SelfCells / 60f);
}

/// <summary>
/// 動きの技のドリル: 赤（プレイヤー）に剣を両手で握らせ、構えて立つ青（両手のグローブで顔を守る）へ、技の間合いまで構えのまま詰めて
/// （近すぎれば下がって）、技を 1 回だけなぞらせる。技は <see cref="SwordsmanBot"/> がそのまま出す（試合と同じ動き）。
/// </summary>
public static class StrikeDrill
{
    private const float Dt = 1f / 240f;

    /// <summary>技を 1 回出す。<paramref name="afterStep"/> は物理の 1 歩ごとに、時刻と技を出している最中かを添えて呼ぶ（動画を撮るため）。</summary>
    public static StrikeResult Run(MotionTechnique technique, MotionFrame guard, float rate, FighterTuning? tuning = null, Action<BoxingGame, float, bool>? afterStep = null)
    {
        ArgumentNullException.ThrowIfNull(technique);
        var bot = new SwordsmanBot([technique], guard, seed: 1, rate, rest: 1000f, exact: true, tuning: tuning);
        using var game = new BoxingGame(tuning, seed: 1, corners: new BoxingCorners("stand", _ => new StandGuard(), _ => bot, PlayerWeapon: technique.Weapon, PlayerTwoHanded: true));
        var victim = game.Cpu.Voxels!;
        var start = Count(victim);
        var sword = game.WeaponOf(game.Player)!;
        var guardX = Weapon.TwoHandGripsOf(sword.Kind).Top.X + 0.012f;
        var tipX = Weapon.TipOf(sword.Kind).X;
        var monouchi = 0;
        var own = Attached(game.Player.Voxels!);
        BodyPartKind? first = null;
        var started = false;
        var concussionBefore = 0f;
        var finishedAt = float.MaxValue;
        for (var t = 0f; t < 8f && t < finishedAt + 0.4f; t += Dt)
        {
            game.Step(default, Dt);
            if (bot.Current is not null && !started)
            {
                started = true;
                concussionBefore = game.Cpu.Concussion;
            }
            else if (bot.Current is null && started && finishedAt == float.MaxValue)
            {
                finishedAt = t;
            }

            foreach (var cut in game.RecentCuts.Where(c => c.A == sword.Body && c.RemovedB.Count > 0 && victim.Parts.Any(p => p.Body == c.B)))
            {
                var s = game.World.GetState(sword.Body.BodyId);
                var along = Vector3.Transform(cut.Point - s.Position, Quaternion.Conjugate(s.Orientation)).X;
                monouchi += (along - guardX) / (tipX - guardX) >= 0.6f ? cut.RemovedB.Count : 0;
            }

            if (first is null)
            {
                foreach (var cut in game.RecentCuts)
                {
                    if (cut.RemovedB.Count > 0 && victim.Parts.FirstOrDefault(p => p.Body == cut.B) is { } struck)
                    {
                        first = struck.Kind;
                        break;
                    }
                }
            }

            afterStep?.Invoke(game, t, bot.Current is not null);
        }

        var now = Count(victim);
        var lost = start.Where(kv => kv.Value > now.GetValueOrDefault(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value - now.GetValueOrDefault(kv.Key));
        return new StrikeResult(technique, lost, [.. victim.Fragments.Select(f => f.Kind)], game.Cpu.IsDead, first,
            own - Attached(game.Player.Voxels!), started ? MathF.Max(0f, game.Cpu.Concussion - concussionBefore) : 0f, bot.UsedAt.FirstOrDefault(), monouchi);
    }

    /// <summary>
    /// 間合い <paramref name="ranges"/> のそれぞれで技を出し、物打ちでいちばん効いた（<see cref="StrikeResult.MaaiScore"/>）間合いと、その結果の一覧を返す
    /// （技の間合いを決める）。
    /// </summary>
    public static (MotionTechnique Best, IReadOnlyList<StrikeResult> Results) FindRange(MotionTechnique technique, MotionFrame guard, float rate, IReadOnlyList<float> ranges, FighterTuning? tuning = null)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        var results = new StrikeResult[ranges.Count];
        Parallel.For(0, ranges.Count, i => results[i] = Run(technique with { Range = ranges[i] }, guard, rate, tuning));
        var best = results.MaxBy(r => r.MaaiScore)!;
        return (best.Technique with { Target = best.MostCut }, results);
    }

    /// <summary>
    /// 技の間合い（一足一刀）: 打ちの間（剣の先がいちばん速い時から、技の終わりまで）に、物打ち（鍔から切っ先までの 8 割の所）が始めの胸の
    /// 位置からいちばん前へ届いた距離 [m]（そこまでに踏み込んだ分も足す）。相手の胸がそこにあれば、打ちの物打ちが相手の体の面にちょうど届く。
    /// 動きは人の動きを縮めたものなので、人の剣士が打ち込む距離をそのまま縮めた間合いになる。
    /// </summary>
    /// <param name="technique">技。</param>
    /// <param name="armReach">体の腕の長さ [m]（動きの長さの単位）。</param>
    public static float ReachOf(MotionTechnique technique, float armReach)
    {
        ArgumentNullException.ThrowIfNull(technique);
        var (top, _) = Weapon.TwoHandGripsOf(technique.Weapon);
        var guardX = top.X + 0.012f;
        var tipX = Weapon.TipOf(technique.Weapon).X;
        var monouchi = ((guardX - top.X) + (0.8f * (tipX - guardX))) / armReach;
        var tip = (tipX - top.X) / armReach;
        var frames = technique.Motion.Frames;
        var dt = technique.Motion.FrameTime;
        var advance = new float[frames.Count];
        var speed = new float[frames.Count];
        for (var i = 0; i < frames.Count; i++)
        {
            advance[i] = (i > 0 ? advance[i - 1] : 0f) + (frames[i].Velocity.Z * dt);
            if (i > 0)
            {
                Vector3 Tip(int k) => frames[k].Right + (frames[k].Blade * tip) + new Vector3(0f, 0f, advance[k]);
                speed[i] = Vector3.Distance(Tip(i), Tip(i - 1)) / dt;
            }
        }

        var peak = Array.IndexOf(speed, speed.Max());
        var reach = float.MinValue;
        for (var i = peak; i < frames.Count; i++)
        {
            reach = MathF.Max(reach, (frames[i].Right + (frames[i].Blade * monouchi)).Z + advance[i]);
        }

        return reach * armReach;
    }

    /// <summary>体の部位（胴につながっている所）ごとに残っているボクセル。</summary>
    private static Dictionary<BodyPartKind, int> Count(BoxerBody body) =>
        body.Parts.GroupBy(p => p.Kind).ToDictionary(g => g.Key, g => g.Sum(p => p.Body.Grid.FilledCount));

    private static int Attached(BoxerBody body) => body.Parts.Sum(p => p.Body.Grid.FilledCount);
}
