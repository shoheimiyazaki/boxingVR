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
        log("## 手形 × 急所の損傷");
        log("");
        log($"{bouts} 本 × 上限 {seconds:0} 秒、種 {seed}〜。素手、受け手 StandGuard、正拳 Seiken が対照。KO/ラウンド終了で停止し、新しい体を混ぜない。物理版 {Fighter.PhysicsVersion}。");
        log("発動 = BotStats の Thrown、命中 = HitEvent（空振りは含まない。同じ打ち出しの複数接触を含みうる）。/命中は全命中イベント数で割った量。終値は各本の平均。欠損は材質の残数の差、HP 低下は視界に別計上。");
        log("局所 = 初期 TargetMap の狙い点を中心に頭は半径 42 mm、腹は 85 mm の球内で接触により削れ・潰れたセル。部位ローカルに固定して追跡（初期の側を固定）。外 = 全身欠損 − 局所。局所欠損は急所内部の破壊を意味しない。");
        log("意識最大落ちは命中を含む 1/240 秒の前後差の最大（同じ歩に複数命中なら合計、回復分を含む）。ねじれは全歩の LastHeadTwist 最大（歩内で最後の呼び出し値。採点未満の接触も含む） [rad/s]。供給は Breath（心肺由来の供給上限の比）で、実回復 W ではない。打撲は心/肺/肝/腸の順。");
        log("");
        log("| 形 | 急所 | 発動 | 命中 | 全欠損/命中 | 局所/命中 | 外/命中 | 全欠損 終 | 局所 終 | 外 終 | 指骨/命中 | 指骨 終 | 皮膚 終 | 秒 平均 |");
        log("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        var results = new List<ShapeResult>();
        foreach (var shape in Enum.GetValues<HandShape>())
        {
            foreach (var goal in Goals)
            {
                var runs = Enumerable.Range(0, bouts).Select(b => ShapeBout(shape, goal, seed + b)).ToArray();
                results.AddRange(runs);
                var hits = runs.Sum(r => r.Hits);
                string Per(float sum) => hits > 0 ? F(sum / hits, "0.0000") : "-";
                string Avg(Func<ShapeResult, float> f) => F(runs.Average(f), "0.0000");
                log($"| {shape} | {goal} | {runs.Sum(r => r.Thrown)} | {hits} | {Per(runs.Sum(r => r.End.Total))} | {Per(runs.Sum(r => r.End.Near))} | {Per(runs.Sum(r => r.End.Outside))} | {Avg(r => r.End.Total)} | {Avg(r => r.End.Near)} | {Avg(r => r.End.Outside)} | {Per(runs.Sum(r => r.Bone))} | {Avg(r => r.Bone)} | {Avg(r => r.Skin)} | {Avg(r => r.Elapsed)} |");
            }
        }

        log("");
        log("| 形 | 急所 | 目欠損/命中;終 | 視界低下/命中;終(左/右) | 脳欠損/命中;終 | 意識落ち/命中;最大;終意識 | ねじれ最大 | 臓器欠損 心/肺/肝/腸 /命中;終 | 打撲増加 心/肺/肝/腸 /命中;終 | 蓄え低下/命中;終 | 供給低下/命中;終 | 腹機能 終 | 非命中歩の意識落ち 累計平均;最大 |");
        log("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var group in results.GroupBy(r => (r.Shape, r.Goal)))
        {
            var runs = group.ToArray();
            var hits = runs.Sum(r => r.Hits);
            string Per(float sum) => hits > 0 ? F(sum / hits, "0.0000") : "-";
            string Avg(Func<ShapeResult, float> f) => F(runs.Average(f), "0.0000");
            string Org(Func<ShapeResult, int, float> f, bool per) => string.Join("/", Enumerable.Range(0, 4).Select(i => per ? Per(runs.Sum(r => f(r, i))) : Avg(r => f(r, i))));
            log($"| {group.Key.Shape} | {group.Key.Goal} | {Per(runs.Sum(r => r.End.Eye))};{Avg(r => r.End.Eye)} | {Per(runs.Sum(r => 1f - r.End.Vision.Binocular))};{Avg(r => r.End.Vision.Left)}/{Avg(r => r.End.Vision.Right)} | {Per(runs.Sum(r => r.End.Brain))};{Avg(r => r.End.Brain)} | {Per(runs.Sum(r => r.ConsciousDrop))};{F(runs.Max(r => r.MaxDrop), "0.0000")};{Avg(r => r.End.Consciousness)} | {F(runs.Max(r => r.MaxTwist), "0.0000")} | {Org((r, i) => r.End.Organs[i], true)};{Org((r, i) => r.End.Organs[i], false)} | {Org((r, i) => r.BruiseAdded[i], true)};{Org((r, i) => r.End.Bruises[i], false)} | {Per(runs.Sum(r => r.StaminaDrop))};{Avg(r => r.End.Stamina)} | {Per(runs.Sum(r => r.BreathDrop))};{Avg(r => r.End.Breath)} | {Avg(r => r.End.Core)} | {Avg(r => r.UnscoredConsciousDrop)};{F(runs.Max(r => r.MaxUnscoredDrop), "0.0000")} |");
        }

        log("");
        log("視界は /命中 が両眼平均の低下、終は残存機能の左/右。蓄え・供給の /命中 は試合中の正の低下の累計（疲労・回復の影響を含み、打撃単独の因果量ではない）。打撲 /命中 は回復で消えた分も含む正の増加の累計。");
        log("以下の JSON は各本の生値。最大値・終値を再集計できるように残す。");
        log("```json");
        log(System.Text.Json.JsonSerializer.Serialize(results));
        log("```");
    }

    internal static BotStyle ShapeStyle(HandShape shape, AimGoal goal)
    {
        var aim = goal switch
        {
            AimGoal.Chin => new AimWeights(Chin: 1f),
            AimGoal.Temple => new AimWeights(Temple: 1f),
            AimGoal.SolarPlexus => new AimWeights(SolarPlexus: 1f),
            AimGoal.Liver => new AimWeights(Liver: 1f),
            _ => new AimWeights(Eye: 1f),
        };
        return (BotCatalog.Karate with
        {
            Name = "karate-vitals",
            KickRate = 0f,
            KickWeights = new Dictionary<KickTechnique, float>(),
            HandShapeWeights = new Dictionary<HandShape, float> { [shape] = 1f },
            Aim = aim,
            ComboWeights = new Dictionary<string, float> { ["1"] = 1f, ["2"] = 1f },
            CounterRate = 0f,
        }).Validated();
    }

    internal sealed record ShapeResult(HandShape Shape, AimGoal Goal, int Seed, int Thrown, int Hits,
        KarateVitals.Reading End, float ConsciousDrop, float MaxDrop, float MaxTwist,
        float[] BruiseAdded, float StaminaDrop, float BreathDrop, float Bone, float Skin, float Elapsed,
        float UnscoredConsciousDrop, float MaxUnscoredDrop);

    internal ShapeResult ShapeBout(HandShape shape, AimGoal goal, int boutSeed, Action<BoxingGame, float, bool>? afterStep = null)
    {
        var attacker = ShapeStyle(shape, goal);
        BoxerBot? bot = null;
        var corners = new BoxingCorners(attacker.Name, s => bot = new BoxerBot(attacker, s, forcedAim: goal), _ => new StandGuard(),
            PlayerWear: HandWear.None, CpuWear: HandWear.None);
        using var game = new BoxingGame(seed: boutSeed, corners: corners);
        var victim = game.Player;
        var meter = new KarateVitals(victim, goal, game.Cpu.ShoulderCenter);
        var previous = meter.Read();
        var hits = 0;
        float drop = 0f, maxDrop = 0f, twist = 0f, staminaDrop = 0f, breathDrop = 0f, elapsed = 0f;
        float unscoredDrop = 0f, maxUnscoredDrop = 0f;
        var bruises = new float[4];
        // 1 物理歩ごとに観測し、回復前後の差を 60 Hz の複数歩にまとめない。
        const float sampleDt = 1f / 240f;
        for (var step = 0; step < (int)MathF.Ceiling(seconds / sampleDt); step++)
        {
            game.Step(default, sampleDt);
            elapsed = (step + 1) * sampleDt;
            meter.Observe(game.RecentCuts);
            var current = meter.Read();
            var landed = game.RecentHits.Where(h => h.Attacker == game.Cpu && !h.Kick).ToArray();
            hits += landed.Length;
            var d = MathF.Max(0f, previous.Consciousness - current.Consciousness);
            // 採点に満たない接触も TwistHead を呼ぶので、全歩の最終呼び出し値を観測する。
            twist = MathF.Max(twist, victim.LastHeadTwist);
            if (landed.Length > 0)
            {
                drop += d;
                maxDrop = MathF.Max(maxDrop, d);
            }
            else
            {
                unscoredDrop += d;
                maxUnscoredDrop = MathF.Max(maxUnscoredDrop, d);
            }

            for (var i = 0; i < bruises.Length; i++)
            {
                bruises[i] += MathF.Max(0f, current.Bruises[i] - previous.Bruises[i]);
            }

            staminaDrop += MathF.Max(0f, previous.Stamina - current.Stamina);
            breathDrop += MathF.Max(0f, previous.Breath - current.Breath);
            previous = current;
            afterStep?.Invoke(game, elapsed, landed.Length > 0);
            if (game.RoundOver)
            {
                break;
            }
        }

        float bone = 0f, skin = 0f;
        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            if (game.GloveOf(game.Cpu, hand) is { HandState: { } state } glove)
            {
                var now = state.Capture(glove.Body.Grid);
                for (var f = 0; f < BareHandState.FingerCount; f++)
                {
                    foreach (var segment in new[] { FingerSegment.Proximal, FingerSegment.Middle, FingerSegment.Distal })
                    {
                        var part = now.Part(f, segment);
                        bone += part.Cracked;
                        skin += 1f - part.Skin;
                    }
                }
            }
        }

        return new ShapeResult(shape, goal, boutSeed, bot!.Stats.Techniques.Values.Sum(c => c.Thrown), hits,
            previous, drop, maxDrop, twist, bruises, staminaDrop, breathDrop, bone, skin, elapsed, unscoredDrop, maxUnscoredDrop);
    }
    // ── パリー ──

    private void Parry()
    {
        log("## パリー（手と腕の接触で拳の軌道を外へ曲げる）");
        log("");
        log($"基本動作のドリル（`basics` と同じ攻め手・受け手）。攻め手は 1 つの型（1 = ジャブ、2 = 右ストレート、3 = 左フック、4 = 右フック）だけを打ち続け、受け手は攻め手が打ち始めて合図の遅れだけ後に、決めた守りを必ず始める（反応の遅れを除き、守りの動きだけを比べる）。{bouts} 本 × {seconds:0} 秒。");
        log("`最接近` = 打つたびに、拳が受け手の頭の中心へ最も近づいた距離の平均 [cm]（守らなければほぼ 0 = 当たる）。守りが拳の軌道を曲げた・頭を外した量。`手が触れた` = 受け手の手と攻め手の拳が 11 cm 以内に入った打つ回数（目標経路は手の置き場だけで、拳をそらすのは接触の力）。");
        log("");
        log("| 合図の遅れ [s] | 守り | 拳 | 打った数 | 守りを出した | 頭に当たった/打 | きれいに外した率 | 最接近 [cm] | 手が触れた/打 |");
        log("|---|---|---|---|---|---|---|---|---|");
        foreach (var cue in cues ?? [0f, 0.101f])
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
                touchedNow |= MathF.Min(Vector3.Distance(fist, game.Player.GloveCenter(Hand.Left)), Vector3.Distance(fist, game.Player.GloveCenter(Hand.Right))) < 0.113f;
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
        log("| 蹴り | 出した数 | 当たり | 相手の欠けセル/当たり | 蹴りの速さ [m/s] | 乗った質量 [kg] | 蹴った側が倒れた本数 | 当たった足の部位 |");
        log("|---|---|---|---|---|---|---|---|");
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
            var partCount = new Dictionary<KickPart, int>();
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
                            partCount[hit.KickerPart] = partCount.GetValueOrDefault(hit.KickerPart) + 1;
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
            log($"| {kind} | {thrown} | {hits} | {F((float)lost / n)} | {F(speed / n)} | {F(mass / n)} | {fell} | {string.Join(" ", partCount.OrderByDescending(p => p.Value).Select(p => $"{p.Key}:{p.Value}"))} |");
        }

        log("");
    }
}
