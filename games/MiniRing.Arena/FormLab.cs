using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 回避の「形」を体の動きとして測る: 回避を始めた時の頭を原点に、相手へ向いた体の向きで
/// 頭の沈み・横・前後の軌跡と、腰の沈み、拳と頭の距離（拳が頭から離れて上がる「万歳」）を 1 回ごとに取り、平均する。
/// 点（外した・頭に当たった）は <see cref="DefenseDrill.Punch"/> と同じ数え方。目標の値ではなく、体が実際に追った結果を見る（§1.2）。
/// </summary>
internal static class FormLab
{
    /// <summary>1 回の回避の観測（cm・ms）。</summary>
    internal readonly record struct Episode(
        float Drop, float PelvisDrop, float Side, float SideRange, float SideStart, float SideEnd, float ForwardAtBottom,
        float DropTimeMs, float DurationMs, float EndDropShare, float GloveFar, float GloveAbove, Vector3[] Path);

    /// <summary>軌跡を並べる間隔 [s] と本数（0〜0.7 秒）。</summary>
    private const float PathStep = 0.05f;
    private const int PathCount = 15;

    public static string Run(string punch, Defense slot, BotStyle style, DefenseMove move, float? cue, int bouts, float seconds, int seed)
    {
        var book = style.Defenses.With(slot, move);
        var defender = DefenseDrill.Defender(book, defend: true, counters: false) with
        {
            Name = style.Name, GuardHeight = style.GuardHeight, GuardWidth = style.GuardWidth, GuardReach = style.GuardReach,
        };
        var episodes = new List<Episode>();
        var total = PunchDrillResult.Empty;
        for (var b = 0; b < bouts; b++)
        {
            var recorder = new Recorder(episodes);
            total += DefenseDrill.Punch(punch, slot, defender, seed + (b * 101), seconds, cue: cue, observe: recorder.Step);
            recorder.Flush(); // 試合の終わりで途中の回避も 1 回に数える
        }

        return Table(punch, slot, move, episodes, total);
    }

    /// <summary>"HeadY=-0.6,HeadZ=0.2" のように、動きの値を書き換える（JSON の名前で）。</summary>
    public static DefenseMove Apply(DefenseMove move, string? sets)
    {
        if (string.IsNullOrWhiteSpace(sets))
        {
            return move;
        }

        var node = JsonSerializer.SerializeToNode(move)!.AsObject();
        foreach (var pair in sets.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length != 2 || !node.ContainsKey(kv[0]))
            {
                throw new ArgumentException($"--set の '{pair}' が分かりません（DefenseMove の名前=値）。");
            }

            node[kv[0]] = bool.TryParse(kv[1], out var flag) ? JsonValue.Create(flag)
                : JsonValue.Create(float.Parse(kv[1], CultureInfo.InvariantCulture));
        }

        return (node.Deserialize<DefenseMove>() ?? move).Validated();
    }

    private static string Table(string punch, Defense slot, DefenseMove move, List<Episode> e, PunchDrillResult r)
    {
        static float Avg(List<Episode> list, Func<Episode, float> f) => list.Count > 0 ? list.Average(f) : 0f;
        var sb = new StringBuilder();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"### {slot} 対 {punch}（{e.Count} 回）: 外した {r.EvadeRate:P0}・頭 {r.HeadRate:P0}（回避中の頭 {r.HeadHits} / ガード {r.GuardHits} / ほか {r.OtherHits} / 気づけず頭 {r.OpenHeadHits}、拳 {r.Punches}）"));
        sb.AppendLine("動き: " + BasicsLab.Describe(move));
        sb.AppendLine();
        sb.AppendLine("| 頭の沈み | 腰の沈み | 頭の横（最大） | 横の振れ幅 | 横 始め→終わり | 底での頭の前 | 沈みの 8 割まで | 記録の長さ（終わり +0.3 s） | 最後の沈み / 最大 | 拳と頭の最大距離 | 拳が頭より上 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"| {Avg(e, x => x.Drop):0.0} cm | {Avg(e, x => x.PelvisDrop):0.0} cm | {Avg(e, x => x.Side):0.0} cm | {Avg(e, x => x.SideRange):0.0} cm | {Avg(e, x => x.SideStart):0.0} → {Avg(e, x => x.SideEnd):0.0} cm | {Avg(e, x => x.ForwardAtBottom):0.0} cm | {Avg(e, x => x.DropTimeMs):0} ms | {Avg(e, x => x.DurationMs):0} ms | {Avg(e, x => x.EndDropShare):0.00} | {Avg(e, x => x.GloveFar):0.0} cm | {Avg(e, x => x.GloveAbove):0.0} cm |"));
        sb.AppendLine();
        sb.AppendLine("頭の軌跡（回避の始めから、横 / 上下 / 前 [cm]。横は回避の側が正）:");
        sb.AppendLine();
        sb.AppendLine("| 時刻 | " + string.Join(" | ", Enumerable.Range(0, PathCount).Select(i => $"{i * PathStep * 1000f:0} ms")) + " |");
        sb.AppendLine("|---|" + string.Concat(Enumerable.Repeat("---|", PathCount)));
        string Cell(int i, Func<Vector3, float> f)
        {
            var have = e.Where(x => x.Path.Length > i).ToList();
            return have.Count > 0 ? have.Average(x => f(x.Path[i])).ToString("0.0", CultureInfo.InvariantCulture) : "";
        }

        foreach (var (label, f) in new (string, Func<Vector3, float>)[] { ("横", v => v.X), ("上下", v => v.Y), ("前", v => v.Z) })
        {
            sb.AppendLine($"| {label} | " + string.Join(" | ", Enumerable.Range(0, PathCount).Select(i => Cell(i, f))) + " |");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 1 試合ぶんの観測。回避が始まった歩から終わる歩までを 1 回とする。
    /// 横は「回避の側」へ正（ウィービングは始めの横と終わりの横の符号で U 字の抜け方が分かる）。
    /// </summary>
    private sealed class Recorder(List<Episode> sink)
    {
        private readonly List<(float T, Vector3 Head, float PelvisY, float GloveFar, float GloveAbove)> _samples = [];
        private Vector3 _origin, _forward, _right;
        private float _pelvis0, _time;
        private bool _active;
        private float? _after;

        /// <summary>回避の終わりから記録を続ける長さ [s]（起き上がりを見る）。</summary>
        private const float AfterSeconds = 0.3f;

        public void Flush()
        {
            if (_active)
            {
                _active = false;
                _after = null;
                Close();
            }
        }

        public void Step(BoxingGame game, BoxerBot bot)
        {
            _time += DefenseDrill.StepDt;
            var me = game.Player;
            var defending = bot.CurrentDefense != Defense.None;
            // 起き上がりを見ている間に次の回避が始まったら、前の回は閉じて新しい回にする（連打を 1 回にまとめない）。
            if (defending && _active && _after is not null)
            {
                Flush();
            }

            if (defending && !_active)
            {
                _active = true;
                _samples.Clear();
                _origin = me.HeadPosition;
                _pelvis0 = me.PelvisPosition.Y;
                var toward = game.Cpu.HeadPosition - me.HeadPosition;
                toward.Y = 0f;
                _forward = toward.LengthSquared() > 1e-6f ? Vector3.Normalize(toward) : Vector3.UnitZ;
                _right = Vector3.Cross(_forward, Vector3.UnitY);
            }

            // 回避が終わってからも起き上がり（U 字の後半）を見るため、AfterSeconds だけ記録を続ける。
            if (_active && !defending)
            {
                _after ??= _time;
                if (_time - _after.Value >= AfterSeconds)
                {
                    _active = false;
                    _after = null;
                    Close();
                    return;
                }
            }
            else if (_active)
            {
                _after = null;
            }

            if (!_active)
            {
                return;
            }

            var head = me.HeadPosition;
            var d = head - _origin;
            var local = new Vector3(Vector3.Dot(d, _right), d.Y, Vector3.Dot(d, _forward));
            var far = 0f;
            var above = float.MinValue;
            foreach (var hand in new[] { Hand.Left, Hand.Right })
            {
                var g = me.GloveCenter(hand);
                far = MathF.Max(far, Vector3.Distance(g, head));
                above = MathF.Max(above, g.Y - head.Y);
            }

            _samples.Add((_time, local, me.PelvisPosition.Y, far, above));
        }

        private void Close()
        {
            if (_samples.Count < 4)
            {
                return;
            }

            var bottom = _samples.MinBy(s => s.Head.Y);
            var drop = -bottom.Head.Y;
            // 回避の側: いちばん横へ出た向き（スリップの外・内、ウィービングの抜ける側）を正にそろえる。
            var widest = _samples.MaxBy(s => MathF.Abs(s.Head.X));
            var sign = widest.Head.X >= 0f ? 1f : -1f;
            var xs = _samples.Select(s => s.Head.X * sign).ToList();
            var third = Math.Max(1, _samples.Count / 3);
            var start = _samples[0].T;
            var reach80 = drop > 1e-4f ? _samples.FirstOrDefault(s => -s.Head.Y >= 0.8f * drop) : default;
            var end = -_samples[^1].Head.Y;
            sink.Add(new Episode(
                Drop: 100f * drop,
                PelvisDrop: 100f * (_pelvis0 - _samples.Min(s => s.PelvisY)),
                Side: 100f * xs.Max(),
                SideRange: 100f * (xs.Max() - xs.Min()),
                SideStart: 100f * xs.Take(third).Average(),
                SideEnd: 100f * xs.Skip(_samples.Count - third).Average(),
                ForwardAtBottom: 100f * bottom.Head.Z,
                DropTimeMs: 1000f * ((reach80 == default ? bottom.T : reach80.T) - start),
                DurationMs: 1000f * (_samples[^1].T - start),
                EndDropShare: drop > 1e-4f ? end / drop : 0f,
                GloveFar: 100f * _samples.Max(s => s.GloveFar),
                GloveAbove: 100f * _samples.Max(s => s.GloveAbove),
                Path: Enumerable.Range(0, PathCount)
                    .Select(i => _samples.FirstOrDefault(s => s.T - start >= (i * PathStep) - 1e-4f))
                    .TakeWhile(s => s != default)
                    .Select(s => 100f * new Vector3(s.Head.X * sign, s.Head.Y, s.Head.Z)).ToArray()));
        }
    }
}
