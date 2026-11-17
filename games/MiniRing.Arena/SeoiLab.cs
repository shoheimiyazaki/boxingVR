using System.Globalization;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 背負い投げを鍛えるドリル（docs/GRAPPLING.md §11）: 引かずに自分の体を運ぶ背負い投げの経路を、数個の値から作り、乱数で候補を作って試し、
/// 「きれいな投げ」（受けが倒れ、取りはその 0.5 秒後も立っている）が多く、取りが倒れないものを選ぶ。選んだものは、選ぶのに使わなかった種と、
/// 動いて打つ相手（チャンピオン）で比べ直す（CLAUDE.md §1.8）。結果は技の経路の値（教科書のデータ）として出すだけ。
/// </summary>
internal sealed class SeoiLab(int seed, Action<string> log)
{
    private const float Dt = 1f / 60f;

    /// <summary>背負い投げの経路の値（長さは腕の長さ = 1、時間 [s]）。</summary>
    internal sealed record Params(
        float Entry,     // 掴んでから背を向けて入りきるまで [s]
        float Depth,     // 入りきった立ち位置: 相手の腰から、取りの前向き（背を向けた後は相手と反対）へこれだけの所に取りの肩の真ん中（小さいほど背中が相手のお腹に深く付く）
        float Side,      // 立ち位置を左右へずらす（+ で取りの左）
        float Crouch,    // 入ったときの頭の下げ（腰を落とす深さ。負）
        float Hold,      // 入りきってから跳ね上げまで、腰を落としたまま止まる [s]
        float Lift,      // 跳ね上げで頭を上げる所（膝を伸ばす。負が浅い）
        float Bow,       // 跳ね上げで頭を前へ出す量（前へ体を折る）
        float HandHigh,  // 入る間の両手の高さ（取りの体の座標。相手の袖を高く釣る）
        float Turn)      // 背を向ける角度 [rad]
    {
        public static Params Initial { get; } = new(0.4f, 0.15f, 0f, -0.45f, 0.15f, 0f, 0.75f, 0.05f, 3.0f);

        public string Short => string.Create(CultureInfo.InvariantCulture,
            $"entry {Entry:0.00} depth {Depth:0.00} side {Side:0.00} crouch {Crouch:0.00} hold {Hold:0.00} lift {Lift:0.00} bow {Bow:0.00} hand {HandHigh:0.00} turn {Turn:0.00}");
    }

    /// <summary>
    /// 経路: 掴む（0.25 秒）→ 両手を取りの体に固定し（左手は胸の前の高い所で袖を釣り、右手は右肩の前で襟を持つ）、回って背を向けながら、
    /// 取りの立ち位置を相手の腰のすぐ前（背中が相手のお腹に付く所）へ運び、腰を落とす。手は体と一緒に動くので、受けは腕で引かれるのでなく
    /// 取りの体の回転と踏み込みで運ばれる（Entry）→ 背中を付けたまま止まる（Hold）→ 膝を伸ばして腰で跳ね上げ、前へ体を折る → 両手を前下へ → 離す。
    /// </summary>
    internal static GrappleTechnique Technique(Params p, string name = "seoi-nage")
    {
        GrappleHand Sleeve(bool grip = true) => new(GrappleAnchor.ElbowL, null, grip);
        GrappleHand Lapel(bool grip = true) => new(GrappleAnchor.ShoulderR, [0f, -0.15f, 0f], grip);
        GrappleHand BodyLeft() => new(GrappleAnchor.Self, [0.2f, p.HandHigh, 0.35f], true);
        GrappleHand BodyRight() => new(GrappleAnchor.Self, [-0.1f, p.HandHigh + 0.05f, 0.2f], true);
        GrappleHand Spot() => new(GrappleAnchor.Pelvis, [p.Side, 0f, p.Depth]);
        var t0 = 0.25f;
        var entry = t0 + p.Entry;
        var hold = entry + p.Hold;
        var lift = hold + 0.35f;
        var drop = lift + 0.3f;
        var keys = new List<GrappleKey>
        {
            new(0f, Sleeve(false), Lapel(false)),
            new(t0, Sleeve(), Lapel()),
            new(t0 + (0.5f * p.Entry), BodyLeft(), BodyRight(), Head: [0f, 0.5f * p.Crouch, 0f], Turn: 0.5f * p.Turn, Stand: Spot()),
            new(entry, BodyLeft(), BodyRight(), Head: [0f, p.Crouch, 0.05f], Turn: p.Turn, Stand: Spot()),
            new(hold, BodyLeft(), BodyRight(), Head: [0f, p.Crouch, 0.1f], Turn: p.Turn, Stand: Spot()),
            // 跳ね上げ: 膝を伸ばして腰を上げ、前へ体を折る（手は体に付いたまま、体と一緒に前下へ回る）。
            new(lift, BodyLeft(), BodyRight(), Head: [0f, p.Lift, p.Bow], Turn: p.Turn, Spring: 0.6f),
            new(drop, new GrappleHand(GrappleAnchor.Self, [0.1f, -0.55f, 0.4f], true), new GrappleHand(GrappleAnchor.Self, [-0.1f, -0.55f, 0.45f], true),
                Head: [0f, p.Lift - 0.1f, p.Bow + 0.1f], Turn: p.Turn, Spring: 0.6f),
            new(drop + 0.2f, new GrappleHand(GrappleAnchor.Self, [0.2f, -0.1f, 0.5f]), new GrappleHand(GrappleAnchor.Self, [-0.2f, -0.1f, 0.5f]), Turn: p.Turn),
        };
        return new GrappleTechnique(name, 0.8f, [0f, -1f], keys);
    }

    /// <summary>1 つの経路の成績: きれいな投げ・投げ・取りの転倒・掴めず・かけた。</summary>
    internal readonly record struct Score(int Clean, int Throws, int ToriFalls, int Missed, int Attempts, float Gap, float Load, float Lifted)
    {
        public float Value => Clean - (0.5f * ToriFalls) - (0.1f * Missed) + Load;

        public override string ToString() => string.Create(CultureInfo.InvariantCulture,
            $"きれい {Clean} / 投げ {Throws} / 取り倒れ {ToriFalls} / 掴めず {Missed} / かけた {Attempts} / 背とお腹のすき間 {Gap * 100:0.0} cm / 乗った重さ {Load:0.00} / 足が浮いた {Lifted:0.00} s");
    }

    internal static Score Evaluate(Params p, string victim, int seed, int bouts, float seconds)
    {
        var technique = Technique(p);
        var book = GrappleBook.Jujutsu with { Rate = 50f, Techniques = [technique] };
        var (clean, throws, falls, missed, attempts) = (0, 0, 0, 0, 0);
        var (gap, load, lifted) = (float.PositiveInfinity, 0f, 0f);
        for (var b = 0; b < bouts; b++)
        {
            JujutsuBot? bot = null;
            var corners = new BoxingCorners("seoi", s => bot = new JujutsuBot(new Closer(), book, s, only: technique.Name), s => ThrowLab.Victim(victim, s),
                CpuWear: MiniRing.Weapons.HandWear.None);
            using var game = new BoxingGame(seed: seed + b, corners: corners);
            var pending = new List<float>();
            var time = 0f;
            game.Thrown += (by, _) =>
            {
                if (by == game.Cpu)
                {
                    throws++;
                    pending.Add(time);
                }
            };
            var wasUp = true;
            for (; time < seconds; time += Dt)
            {
                game.Step(default, Dt);
                var up = game.Cpu.State == FighterState.Standing;
                var uke = game.Player;
                var tori = game.Cpu;
                if (bot?.Current is not null && up && uke.State == FighterState.Standing)
                {
                    // 背中とお腹のすき間: 取りの胸・腰と受けの胸・腰の、箱の厚みを引いた水平の距離の最小。
                    foreach (var tp in (ReadOnlySpan<int>)[tori.Parts.Chest, tori.Parts.Pelvis])
                    {
                        foreach (var vp in (ReadOnlySpan<int>)[uke.Parts.Chest, uke.Parts.Pelvis])
                        {
                            var a = game.World.GetState(tp).Position;
                            var c = game.World.GetState(vp).Position;
                            var d = new System.Numerics.Vector2(a.X - c.X, a.Z - c.Z).Length() - tori.HalfExtentsOf(tp).Z - uke.HalfExtentsOf(vp).Z;
                            gap = MathF.Min(gap, d);
                        }
                    }

                    load = MathF.Max(load, 1f - uke.VerticalBalance);
                    lifted += uke.SupportHull.Count == 0 ? Dt : 0f;
                }

                falls += wasUp && !up ? 1 : 0;
                wasUp = up;
                for (var i = pending.Count - 1; i >= 0; i--)
                {
                    if (!up)
                    {
                        pending.RemoveAt(i); // 取りも倒れた: きれいでない
                    }
                    else if (time - pending[i] >= 0.5f)
                    {
                        clean++;
                        pending.RemoveAt(i);
                    }
                }
            }

            attempts += bot?.Attempts.GetValueOrDefault(technique.Name) ?? 0;
            missed += bot?.Missed.GetValueOrDefault(technique.Name) ?? 0;
        }

        return new Score(clean, throws, falls, missed, attempts, gap, load, lifted);
    }

    /// <summary>値の組を、構えて立つ受けとチャンピオンで比べる（使わなかった種で）。</summary>
    public void Compare(IReadOnlyList<(string Name, Params P)> sets, int validationSeed, int bouts, float seconds)
    {
        log($"| 経路 | 値 | 構えて立つ受け（種 {validationSeed}〜、{bouts} 本 × {seconds:0} 秒） | チャンピオン |");
        log("|---|---|---|---|");
        foreach (var (name, p) in sets)
        {
            log($"| {name} | {p.Short} | {Evaluate(p, "stand", validationSeed, bouts, seconds)} | {Evaluate(p, "champion", validationSeed, bouts, seconds)} |");
        }

        log("");
    }

    public void Train(int candidates, int bouts, float seconds, int validationSeed)
    {
        var random = new Random(seed);
        float R(float lo, float hi) => lo + ((hi - lo) * random.NextSingle());
        var start = Params.Initial;
        var best = (P: start, S: Evaluate(start, "stand", seed, bouts, seconds));
        log($"| 候補 | 値 | 構えて立つ受け（種 {seed}〜） | 点 |");
        log("|---|---|---|---|");
        log($"| 0（初め） | {start.Short} | {best.S} | {best.S.Value:0.0} |");
        for (var i = 1; i <= candidates; i++)
        {
            // 半分は全体から、半分は今の最良の近くから。
            var near = i % 2 == 0;
            var b0 = best.P;
            var candidate = near
                ? new Params(
                    Math.Clamp(b0.Entry + R(-0.1f, 0.1f), 0.25f, 0.8f), Math.Clamp(b0.Depth + R(-0.08f, 0.08f), -0.1f, 0.4f), Math.Clamp(b0.Side + R(-0.1f, 0.1f), -0.3f, 0.3f),
                    Math.Clamp(b0.Crouch + R(-0.08f, 0.08f), -0.6f, -0.3f), Math.Clamp(b0.Hold + R(-0.08f, 0.08f), 0f, 0.4f), Math.Clamp(b0.Lift + R(-0.1f, 0.1f), -0.3f, 0.15f),
                    Math.Clamp(b0.Bow + R(-0.12f, 0.12f), 0.3f, 0.9f), Math.Clamp(b0.HandHigh + R(-0.1f, 0.1f), -0.2f, 0.3f), Math.Clamp(b0.Turn + R(-0.25f, 0.25f), 2.6f, 3.5f))
                : new Params(R(0.25f, 0.8f), R(-0.1f, 0.4f), R(-0.3f, 0.3f), R(-0.6f, -0.3f), R(0f, 0.4f), R(-0.3f, 0.15f), R(0.3f, 0.9f), R(-0.2f, 0.3f), R(2.6f, 3.5f));
            var score = Evaluate(candidate, "stand", seed, bouts, seconds);
            log($"| {i}{(near ? "（近く）" : "")} | {candidate.Short} | {score} | {score.Value:0.0} |");
            if (score.Value > best.S.Value)
            {
                best = (candidate, score);
            }
        }

        log("");
        log($"選んだ値: {best.P.Short}");
        log("");
        log($"### 比べ直し（使わなかった種 {validationSeed}〜、{bouts} 本 × {seconds:0} 秒）");
        log("");
        log("| 経路 | 構えて立つ受け | チャンピオン |");
        log("|---|---|---|");
        foreach (var (name, p) in new[] { ("初め", start), ("選んだ", best.P) })
        {
            log($"| {name} | {Evaluate(p, "stand", validationSeed, bouts, seconds)} | {Evaluate(p, "champion", validationSeed, bouts, seconds)} |");
        }

        log("");
        log("選んだ経路の JSON（Bots/grapple/jujutsu.json の seoi-nage へ）:");
        log("```json");
        log(Technique(best.P).ToJsonKeys());
        log("```");
    }
}

internal static class GrappleTechniqueJson
{
    private static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public static string ToJsonKeys(this GrappleTechnique technique) => System.Text.Json.JsonSerializer.Serialize(technique, Options);
}
