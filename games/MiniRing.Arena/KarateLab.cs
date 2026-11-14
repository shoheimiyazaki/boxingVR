using System.Globalization;
using System.Numerics;
using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// 空手のドリル（karatelab）: 手の形 × 急所、パリー、蹴りを、試合と同じ物理・同じボットで測る。
/// <list type="bullet">
/// <item>手形 × 急所: 素手の攻め手が 1 つの形で 1 つの急所だけを打ち続け、構えて立つだけの相手の欠けたセルと、自分の指の骨の欠けを測る。</item>
/// <item>パリー: 防御ドリルで、パリーが拳の軌道を手と腕の接触でどれだけ曲げたか（接触前後の拳の向きの変化）と、被ダメージの減り・カウンターを測る。</item>
/// <item>蹴り: 5 種の蹴りを 1 つずつ打つ空手家が、相手に当て（接触・打撃として数えた数）、軸足で立ち続けられるかを測る。</item>
/// </list>
/// 結果はそのままスタイル JSON（<c>karate.json</c> の手形・パリー・蹴りの重み）を磨く材料になる。
/// </summary>
internal sealed class KarateLab(int bouts, float seconds, int seed, Action<string> log, IReadOnlyList<float>? cues = null)
{
    private const float StepDt = 1f / 60f;

    public void Run(bool shapes, bool parry, bool kicks)
    {
        if (shapes)
        {
            Shapes();
        }

        if (parry)
        {
            Parry();
        }

        if (kicks)
        {
            Kicks();
        }
    }

    private static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    // ── 手形 × 急所 ──

    private static readonly AimGoal[] Goals = [AimGoal.Chin, AimGoal.Temple, AimGoal.SolarPlexus, AimGoal.Liver, AimGoal.Eye];

    private void Shapes()
    {
        log("## 手形 × 急所");
        log("");
        log($"素手の攻め手が 1 つの形で 1 つの急所だけを狙い続ける。受け手は構えて立つだけ。{bouts} 本 × {seconds:0} 秒、種 {seed}〜。");
        log("`相手の欠け/打` = 打った 1 発あたりに相手から欠けたセル数。`自分の指の骨/打` = 打った 1 発あたりに自分の指（4 本・拳頭と畳んだ節）から失った骨の割合（0..1 を指の数で合計）。");
        log("");
        log("| 形 | 急所 | 打った数 | 相手の欠け/打 | 自分の指の骨/打 | 手の皮膚の欠け/打 |");
        log("|---|---|---|---|---|---|");
        foreach (var shape in Enum.GetValues<HandShape>())
        {
            foreach (var goal in Goals)
            {
                var (punches, lost, bone, skin) = (0, 0, 0f, 0f);
                for (var b = 0; b < bouts; b++)
                {
                    var r = ShapeBout(shape, goal, seed + b);
                    punches += r.Punches;
                    lost += r.Lost;
                    bone += r.Bone;
                    skin += r.Skin;
                }

                var n = Math.Max(1, punches);
                log($"| {shape} | {goal} | {punches} | {F((float)lost / n)} | {F(bone / n, "0.000")} | {F(skin / n)} |");
            }
        }

        log("");
    }

    private (int Punches, int Lost, float Bone, float Skin) ShapeBout(HandShape shape, AimGoal goal, int boutSeed)
    {
        var aim = goal switch
        {
            AimGoal.Chin => new AimWeights(Chin: 1f),
            AimGoal.Temple => new AimWeights(Temple: 1f),
            AimGoal.SolarPlexus => new AimWeights(SolarPlexus: 1f),
            AimGoal.Liver => new AimWeights(Liver: 1f),
            _ => new AimWeights(Eye: 1f),
        };
        var attacker = (BotCatalog.Karate with
        {
            Name = $"shape-{shape}-{goal}",
            KickRate = 0f,
            KickWeights = new Dictionary<KickTechnique, float>(),
            HandShapeWeights = new Dictionary<HandShape, float> { [shape] = 1f },
            Aim = aim,
            ComboWeights = new Dictionary<string, float> { ["1"] = 1f, ["2"] = 1f },
            CounterRate = 0f,
        }).Validated();
        var corners = new BoxingCorners(attacker.Name, s => new BoxerBot(attacker, s), _ => new StandGuard(),
            PlayerWear: HandWear.None, CpuWear: HandWear.None);
        using var game = new BoxingGame(seed: boutSeed, corners: corners);
        var victim = game.Player;
        var before = victim.Voxels!.Parts.Concat(victim.Voxels.Fragments).Sum(p => p.Body.Grid.FilledCount);
        var punches = 0;
        var seen = new HashSet<HitEvent>();
        var states = new List<BareHandState>();
        for (var t = 0f; t < seconds; t += StepDt)
        {
            game.Step(default, StepDt);
            foreach (var hit in game.RecentHits)
            {
                if (hit.Attacker == game.Cpu && !hit.Kick && seen.Add(hit))
                {
                    punches++;
                }
            }
        }

        // 自分の指: 両手の素手の親データから、骨の欠けと皮膚の欠けを集める。
        float bone = 0f, skin = 0f;
        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            if (game.GloveOf(game.Cpu, hand) is { HandState: { } state } glove)
            {
                var now = state.Capture(glove.Body.Grid);
                for (var f = 0; f < BareHandState.FingerCount; f++)
                {
                    foreach (var segment in new[] { FingerSegment.Knuckle, FingerSegment.Folded })
                    {
                        var part = now.Part(f, segment);
                        bone += part.Cracked;
                        skin += 1f - part.Skin;
                    }
                }
            }
        }

        var lost = before - victim.Voxels.Parts.Concat(victim.Voxels.Fragments).Sum(p => p.Body.Grid.FilledCount);
        return (punches, lost, bone, skin);
    }

    // ── パリー ──

    private void Parry()
    {
        log("## パリー（手と腕の接触で拳の軌道を外へ曲げる）");
        log("");
        log($"基本動作のドリル（`basics` と同じ攻め手・受け手）。攻め手は 1 つの型（1 = ジャブ、2 = 右ストレート、3 = 左フック、4 = 右フック）だけを打ち続け、受け手は攻め手が打ち始めて合図の遅れだけ後に、決めた守りを必ず始める（反応の遅れを除き、守りの動きだけを比べる）。{bouts} 本 × {seconds:0} 秒。");
        log("`最接近` = 打つたびに、拳が受け手の頭の中心へ最も近づいた距離の平均 [cm]（守らなければほぼ 0 = 当たる）。守りが拳の軌道を曲げた・頭を外した量。`手が触れた` = 受け手の手と攻め手の拳が 4 cm 以内に入った打つ回数（目標経路は手の置き場だけで、拳をそらすのは接触の力）。");
        log("");
        log("| 合図の遅れ [s] | 守り | 拳 | 打った数 | 守りを出した | 頭に当たった/打 | きれいに外した率 | 最接近 [cm] | 手が触れた/打 |");
        log("|---|---|---|---|---|---|---|---|---|");
        foreach (var cue in cues ?? [0f, 0.06f])
        {
        foreach (var punch in new[] { "1", "2", "3", "4" })
        {
            foreach (var defense in new[] { Defense.None, Defense.Parry, Defense.Block, Defense.Slip })
            {
                var open = defense == Defense.None; // 守らない受け手（比べる基準）
                var (punches, defenses, failed, headHits, touched) = (0, 0, 0, 0, 0);
                var closest = new List<float>();
                for (var b = 0; b < bouts; b++)
                {
                    var style = DefenseDrill.Defender(DefenseBook.Default, defend: !open, counters: false);
                    var r = DefenseDrill.Punch(punch, defense, style, seed + b, seconds, cue: open ? null : cue);
                    punches += r.Punches;
                    defenses += r.Defenses;
                    failed += r.Failed;
                    headHits += r.HeadHits + r.OpenHeadHits;
                    var (near, touch) = ParryTrace(punch, defense, style, seed + b, open ? null : cue);
                    closest.AddRange(near);
                    touched += touch;
                }

                var n = Math.Max(1, punches);
                log($"| {cue:0.00} | {defense} | {punch} | {punches} | {defenses} | {F((float)headHits / n)} | {F(defenses > 0 ? 1f - ((float)failed / defenses) : 0f)} | {(closest.Count > 0 ? F(closest.Average() * 100f, "0.0") : "-")} | {F((float)touched / n)} |");
            }
        }
        }

        log("");
    }

    /// <summary>
    /// 基本動作のドリルの 1 本（<see cref="DefenseDrill.Punch"/> と同じ条件・同じ種）をもう一度回し、打つたびの、拳が受け手の頭の中心へ最も近づいた
    /// 距離と、受け手の手が拳に触れた回数を測る。
    /// </summary>
    private (List<float> Closest, int Touched) ParryTrace(string punch, Defense defense, BotStyle defender, int boutSeed, float? cue)
    {
        var attackers = new List<BoxerBot>();
        var defenders = new List<BoxerBot>();
        var attackerStyle = DefenseDrill.PunchAttacker(punch);
        var style = (defender with { DefenseRate = 0f }).Validated();
        var drillCue = cue is { } delay ? DefenseDrill.DrillCue.For(punch, attackerStyle, delay) : null;
        var corners = new BoxingCorners(attackerStyle.Name,
            s => Keep(attackers, new BoxerBot(attackerStyle, s)),
            s => Keep(defenders, new BoxerBot(style, s, defense)));
        using var game = new BoxingGame(seed: boutSeed, corners: corners);
        var closest = new List<float>();
        var touched = 0;
        var current = float.MaxValue;
        var touchedNow = false;
        var lastThrown = 0;
        for (var t = 0f; t < seconds; t += StepDt)
        {
            game.Step(default, StepDt);
            drillCue?.Update(t + StepDt, attackers, defenders[^1]);
            var thrown = attackers.Sum(a => a.Stats.Techniques.Values.Sum(c => c.Thrown));
            if (thrown != lastThrown)
            {
                if (current < float.MaxValue)
                {
                    closest.Add(current);
                    touched += touchedNow ? 1 : 0;
                }

                (current, touchedNow, lastThrown) = (float.MaxValue, false, thrown);
            }

            foreach (var hand in new[] { Hand.Left, Hand.Right })
            {
                var fist = game.Cpu.GloveCenter(hand);
                current = MathF.Min(current, Vector3.Distance(fist, game.Player.HeadPosition));
                touchedNow |= MathF.Min(Vector3.Distance(fist, game.Player.GloveCenter(Hand.Left)), Vector3.Distance(fist, game.Player.GloveCenter(Hand.Right))) < 0.04f;
            }
        }

        return (closest, touched);
    }

    private static BoxerBot Keep(List<BoxerBot> list, BoxerBot bot)
    {
        list.Add(bot);
        return bot;
    }

    // ── 蹴り ──

    private void Kicks()
    {
        log("## 蹴り");
        log("");
        log($"空手家が 1 種の蹴りだけを出し続け、相手は構えて立つだけ。{bouts} 本 × {seconds:0} 秒。");
        log("`当たり` = 蹴りとして数えた打撃（`HitEvent.Kick`）。`倒れた` = 蹴った側が立てなくなった本数（軸足で立ち続けられたか）。");
        log("");
        log("| 蹴り | 出した数 | 当たり | 相手の欠けセル/当たり | 蹴りの速さ [m/s] | 乗った質量 [kg] | 蹴った側が倒れた本数 |");
        log("|---|---|---|---|---|---|---|");
        foreach (var kind in Enum.GetValues<KickTechnique>())
        {
            var style = (BotCatalog.Karate with
            {
                Name = $"kick-{kind}",
                KickRate = 1f,
                KickWeights = new Dictionary<KickTechnique, float> { [kind] = 1f },
                Aggression = 1f,
            }).Validated();
            var (hits, lost, speed, mass, fell, thrown) = (0, 0, 0f, 0f, 0, 0);
            for (var b = 0; b < bouts; b++)
            {
                BoxerBot? bot = null;
                var corners = new BoxingCorners(style.Name, s => bot = new BoxerBot(style, s), _ => new StandGuard(), PlayerWear: HandWear.None, CpuWear: HandWear.None);
                using var game = new BoxingGame(seed: seed + b, corners: corners);
                var victim = game.Player;
                var before = victim.Voxels!.Parts.Concat(victim.Voxels.Fragments).Sum(p => p.Body.Grid.FilledCount);
                var seen = new HashSet<HitEvent>();
                var wasKicking = false;
                for (var t = 0f; t < seconds; t += StepDt)
                {
                    game.Step(default, StepDt);
                    var kicking = bot?.CurrentKick is not null;
                    if (kicking && !wasKicking)
                    {
                        thrown++;
                    }

                    wasKicking = kicking;
                    foreach (var hit in game.RecentHits)
                    {
                        if (hit.Kick && hit.Attacker == game.Cpu && seen.Add(hit))
                        {
                            hits++;
                            speed += hit.Speed;
                            mass += hit.ImpactMass;
                        }
                    }
                }

                lost += before - victim.Voxels.Parts.Concat(victim.Voxels.Fragments).Sum(p => p.Body.Grid.FilledCount);
                if (game.Cpu.State != FighterState.Standing)
                {
                    fell++;
                }
            }

            var n = Math.Max(1, hits);
            log($"| {kind} | {thrown} | {hits} | {F((float)lost / n)} | {F(speed / n)} | {F(mass / n)} | {fell} |");
        }

        log("");
    }
}
