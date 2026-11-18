using System.Diagnostics;
using MiniRing.Bots;
using MiniRing.Motion.Stick;

namespace MiniRing.Arena;

/// <summary>
/// 棒人形の道具（stick、docs/STICK_MOTION.md §6）: 蹴りの経路を棒人形で評価する・体重移しの値を探す。
/// </summary>
internal sealed class StickLab(Action<string> log)
{
    /// <summary>外れ [cm] の表示（宙に浮く = 床が上へ押さない歩は「宙」）。</summary>
    private static string Cm(float meters) => float.IsPositiveInfinity(meters) ? "宙" : $"{meters * 100f:0.0}";

    public void Kicks(string styleName, IReadOnlyList<KickTechnique> kinds, IReadOnlyList<StickKick.Teaching> teachings, string? pngDir, float approach = 0f)
    {
        var watch = Stopwatch.StartNew();
        var kick = StickKick.Standard();
        var style = BotCatalog.Style(styleName);
        log($"## 棒人形で蹴りを評価する（{styleName}、蹴り始めの前への速さ {approach:0.00} m/s、体の写しに {watch.Elapsed.TotalSeconds:0.0} 秒）");
        log("");
        log("`ZMP 外` = 床反力の作用点が着いた足の外へ出た最大 [cm]・外にいた時間 [s]。`捕捉点 外` = 同じく捕捉点（ゲームの立つ力の物差し）。`負担` = 関節のトルク ÷ 上限の最大。`仕事` = 筋の正の仕事 [J]。`外れ` = 足（膝）の目標の外れの最大 [cm]。");
        log("");
        log("| 蹴り | 教育の値 | ZMP 外 [cm] / [s] | 捕捉点 外 [cm] / [s] | 負担（関節） | 仕事 [J] | 無理 | 外れ [cm] | 計算 [ms] |");
        log("|---|---|---|---|---|---|---|---|---|");
        foreach (var kind in kinds)
        {
            foreach (var teaching in teachings)
            {
                watch.Restart();
                var result = kick.Run(kind, style, teaching, approach);
                var ms = watch.Elapsed.TotalMilliseconds;
                var (ratio, joint) = result.PeakLoad;
                log($"| {kind} | {teaching.Shift:0.00}:{teaching.Forward:0.00}:{teaching.Side:0.00} | {Cm(result.MaxZmpOutside)} / {result.ZmpOutsideSeconds:0.00} | {result.MaxCaptureOutside * 100f:0.0} / {result.CaptureOutsideSeconds:0.00} | {ratio:0.00}（{joint}） | {result.Work:0.000} | {result.Strain:0.000} | {result.MaxTargetError * 100f:0.0} | {ms:0} |");
                if (pngDir is not null)
                {
                    Directory.CreateDirectory(pngDir);
                    var name = $"{kind}_{teaching.Shift:0.00}_{teaching.Forward:0.00}_{teaching.Side:0.00}";
                    StickPicture.Save(result, Path.Combine(pngDir, name + ".png"));
                    File.WriteAllText(Path.Combine(pngDir, name + ".csv"), result.Csv());
                }
            }
        }

        log("");
    }

    /// <summary>
    /// 名簿のキャラ <paramref name="id"/> の武器の型（<see cref="FormSwordsman.Forms"/>）を棒人形で走らせ、人間らしさの点と不自然さの警報を出す
    /// （docs/STICK_MOTION.md §10）。<paramref name="detail"/> なら型ごとの警報を全部書く。
    /// </summary>
    public void Forms(string id, string? pngDir, bool detail, string? bvhDir = null, float bvhScale = 1f)
    {
        var ground = StickGround.For(id);
        var weapon = MiniRing.Characters.CharacterRoster.Get(id).Weapon ?? throw new ArgumentException($"{id} は武器を握りません。", nameof(id));
        var settings = StickReviewSettings.From(ground.Tuning);
        log($"## 棒人形で型を見る（{id}、{weapon}）");
        log("");
        log("| 型 | 点 | 警告 | 注意 | 主な警報 | 滑らかさ（最小 LDLJ） | 仕事率 | ブレーキ | 偏り | 計算 [ms] |");
        log("|---|---|---|---|---|---|---|---|---|---|");
        var reviews = new List<(string Name, StickReview Review)>();
        foreach (var form in MiniRing.Bots.FormSwordsman.Forms(weapon))
        {
            var watch = Stopwatch.StartNew();
            var result = StickForm.Run(ground, form);
            var review = StickReview.Of(result, settings);
            var ms = watch.Elapsed.TotalMilliseconds;
            reviews.Add((form.Name, review));
            var top = string.Join("／", review.Alerts.Where(a => a.Grade == StickGrade.Warning).Select(a => $"{a.Kind}（{a.Where}）").Distinct().Take(3));
            var jerk = review.Smoothness.Count > 0 ? review.Smoothness.Min(x => x.Ldlj) : 0f;
            log($"| {form.Name} | {review.Score:0} | {review.Warnings} | {review.Cautions} | {top} | {jerk:0.0} | {review.WorkRate:0.00} | {review.Braking:0.00} | {review.LoadSpread:0.00} | {ms:0} |");
            if (pngDir is not null)
            {
                Directory.CreateDirectory(pngDir);
                StickPicture.Save(result, Path.Combine(pngDir, $"{id}_{form.Name}.png"));
            }

            if (bvhDir is not null)
            {
                Directory.CreateDirectory(bvhDir);
                File.WriteAllText(Path.Combine(bvhDir, $"{id}_{form.Name}.bvh"), StickBvh.Write(result.Body, result.Frames, result.Dt, bvhScale));
            }
        }

        log("");
        if (detail)
        {
            foreach (var (name, review) in reviews)
            {
                log($"### {name}");
                log("");
                log(review.Report());
            }
        }
    }

#if STICK_GROUND // 倒れた相手への攻め（GroundLab・BotStyle.Ground）は投げの枝 claude/throwing-grappling-mechanics-de662d にだけある。その枝を master へ入れたらこの #if を外す。
    /// <summary>名簿のキャラ <paramref name="id"/> のとどめを、経路（<paramref name="groundPath"/> の JSON か、今の教科書）で棒人形に打たせる（技ごと）。</summary>
    public void Ground(string id, IReadOnlyList<string> moves, string? pngDir, string? groundPath)
    {
        var ground = StickGround.For(id);
        var tuning = groundPath is null ? GroundLab.GroundOf(id) : LoadGround(groundPath);
        log($"## 棒人形でとどめを打つ（{id}、経路 {groundPath ?? "今の教科書"}）");
        log("");
        log("| 技 | 打ち始め | 当たりの速さ [m/s] | 外れ [cm] | 自傷の深さ [cm]（部位）・時間 [s] | ZMP 外 [cm] | 捕捉点 外 [cm] | 負担（関節） | 仕事 [J] | 人間らしさ（警告・注意） | 計算 [ms] |");
        log("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var move in moves)
        {
            var watch = Stopwatch.StartNew();
            var s = ground.Run(move, tuning);
            var (ratio, joint) = s.Result.PeakLoad;
            var cut = s.Result.MaxSelfCut;
            var review = StickReview.Of(s.Result, StickReviewSettings.From(ground.Tuning));
            log($"| {s.Name} | {(s.Started ? "○" : "×")} | {s.ContactSpeed:0.00} | {s.Miss * 100f:0.0} | {cut.Depth * 100f:0.0}（{cut.Part}）・{s.Result.SelfCutSeconds:0.00} | {Cm(s.Result.MaxZmpOutside)} | {s.Result.MaxCaptureOutside * 100f:0.0} | {ratio:0.00}（{joint}） | {s.Result.Work:0.000} | {review.Score:0}（{review.Warnings}・{review.Cautions}） | {watch.Elapsed.TotalMilliseconds:0} |");
            if (pngDir is not null)
            {
                Directory.CreateDirectory(pngDir);
                StickPicture.Save(s.Result, Path.Combine(pngDir, $"{id}_{move}{(groundPath is null ? "" : "_" + Path.GetFileNameWithoutExtension(groundPath))}.png"));
            }
        }

        log("");
    }
#endif

#if STICK_GROUND // 倒れた相手への攻め（GroundLab・BotStyle.Ground）は投げの枝 claude/throwing-grappling-mechanics-de662d にだけある。その枝を master へ入れたらこの #if を外す。
    /// <summary>
    /// とどめの経路（技 <paramref name="move"/> の <see cref="GroundMove"/>）を棒人形で探す（(1+1) 進化戦略）。点 = 先が相手の高さを通る速さ
    /// − 外れ − 捕捉点が足の外へ出た罰 − 関節の上限を超えた罰。立ったまま打つ（倒れない）候補を、物理の前に棒人形で絞る。
    /// 結果は JSON（物理は <c>groundlab --chars id --ground x.json</c> で確かめる）。
    /// </summary>
    public GroundTuning DesignGround(string id, string move, int candidates, int seed, string? output)
    {
        var ground = StickGround.For(id);
        var start = GroundLab.GroundOf(id);
        var random = new Random(seed);
        // 自傷: 刃が自分の体へ入り込む深さ 1 cm で 5（当たりの速さ 5 m/s ぶん）、入り込む時間 0.1 s で 2 を引く（自分を斬るとどめは採らない）。
        static float Score(StickGround.Strike s) => !s.Started ? -10f
            : s.ContactSpeed - (20f * s.Miss) - (60f * MathF.Max(0f, s.Result.MaxCaptureOutside)) - (2f * MathF.Max(0f, s.Result.PeakLoad.Ratio - 1f))
              - (500f * MathF.Max(0f, s.Result.MaxSelfCut.Depth)) - (20f * s.Result.SelfCutSeconds);
        var watch = Stopwatch.StartNew();
        var best = (G: start, S: ground.Run(move, start));
        var step = 0.08f;
        var successes = 0;
        log($"## 棒人形で {id} の {move} を探す（{candidates} 候補）");
        log("");
        log("| 候補 | 当たりの速さ [m/s] | 外れ [cm] | 自傷 [cm] | 捕捉点 外 [cm] | 負担 | 点 | 採った |");
        log("|---|---|---|---|---|---|---|---|");
        log($"| 0 | {best.S.ContactSpeed:0.00} | {best.S.Miss * 100f:0.0} | {best.S.Result.MaxSelfCut.Depth * 100f:0.0} | {best.S.Result.MaxCaptureOutside * 100f:0.0} | {best.S.Result.PeakLoad.Ratio:0.00} | {Score(best.S):0.00} | - |");
        float N() => (float)(Math.Sqrt(-2.0 * Math.Log(1.0 - random.NextDouble())) * Math.Cos(2.0 * Math.PI * random.NextDouble()));
        for (var i = 1; i <= candidates; i++)
        {
            var m = GroundLab.MoveOf(best.G, move);
            float J(float v, float lo, float hi) => Math.Clamp(v + (step * N()), lo, hi);
            var blade = m.WindElevation != 0f || m.StrikeElevation != 0f;
            var candidate = m with
            {
                Duration = Math.Clamp(m.Duration * (1f + (0.1f * N())), 0.35f, 1.4f),
                Stand = J(m.Stand, 0.05f, 1.2f),
                Reach = J(m.Reach, 0.6f, 1.2f),
                Rise = J(m.Rise, 0.1f, 1.2f),
                Pull = J(m.Pull, -0.3f, 0.6f),
                WindElevation = blade ? J(m.WindElevation, -1.5f, 2.8f) : 0f,
                StrikeElevation = blade ? J(m.StrikeElevation, -1.5f, 0.3f) : 0f,
                Overshoot = J(m.Overshoot, 0f, 0.6f),
                Dip = J(m.Dip, 0f, 0.9f),
                Lean = J(m.Lean, -0.2f, 0.5f),
                MaxSink = J(m.MaxSink ?? 0.8f, 0f, 1.2f),
            };
            var g = GroundLab.With(best.G, move, candidate);
            var s = ground.Run(move, g);
            var taken = Score(s) > Score(best.S);
            log($"| {i} | {s.ContactSpeed:0.00} | {s.Miss * 100f:0.0} | {s.Result.MaxSelfCut.Depth * 100f:0.0} | {s.Result.MaxCaptureOutside * 100f:0.0} | {s.Result.PeakLoad.Ratio:0.00} | {Score(s):0.00} | {(taken ? "採った" : "")} |");
            if (taken)
            {
                best = (g, s);
                successes++;
            }

            if (i % 5 == 0)
            {
                step *= successes > 1 ? 1.3f : 0.75f;
                successes = 0;
            }
        }

        log("");
        log($"見つけた経路（{watch.Elapsed.TotalSeconds:0} 秒）: `{GroundLab.MoveOf(best.G, move)}`");
        if (output is not null)
        {
            File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(best.G, Indented));
            log($"JSON: {output}（`groundlab --chars {id} --moves {move} --ground {output}` で物理を確かめる）");
        }

        log("");
        return best.G;
    }
#endif

    private static readonly System.Text.Json.JsonSerializerOptions Indented = new() { WriteIndented = true };

#if STICK_GROUND // 倒れた相手への攻め（GroundLab・BotStyle.Ground）は投げの枝 claude/throwing-grappling-mechanics-de662d にだけある。その枝を master へ入れたらこの #if を外す。
    internal static GroundTuning LoadGround(string path) =>
        (System.Text.Json.JsonSerializer.Deserialize<GroundTuning>(File.ReadAllText(path)) ?? throw new InvalidDataException($"{path} が空です。")).Validated();
#endif

    /// <summary>
    /// 体重移しの値（間・前・横）を棒人形で探す（(1+1) 進化戦略）。点 = ZMP・捕捉点の外れの罰 ＋ 無理 ＋ 仕事。
    /// 結果は物理のドリル（kickbalance）で確かめる候補として出すだけ。
    /// </summary>
    public StickKick.Teaching Design(string styleName, KickTechnique kind, int candidates, int seed, float approach = 0f)
    {
        var kick = StickKick.Standard();
        var style = BotCatalog.Style(styleName);
        var random = new Random(seed);
        // 宙（床が上へ押さない歩）の外れは +∞ なので、0.5 m で丸める（探索が比べられるように）。
        float Cost(StickResult r) => (400f * Math.Clamp(r.MaxZmpOutside, 0f, 0.5f)) + (40f * r.ZmpOutsideSeconds) + (400f * MathF.Max(0f, r.MaxCaptureOutside)) + (40f * r.CaptureOutsideSeconds)
            + r.Strain + (10f * r.Work) + (50f * MathF.Max(0f, r.MaxTargetError - 0.01f));
        var best = (T: new StickKick.Teaching(0f, 0f, 0f), C: 0f);
        best.C = Cost(kick.Run(kind, style, best.T, approach));
        var step = 0.15f;
        var successes = 0;
        log($"## 棒人形で {kind} の体重移しを探す（{styleName}、{candidates} 候補）");
        log("");
        log("| 候補 | 間 | 前 | 横 | 点 | 採った |");
        log("|---|---|---|---|---|---|");
        log($"| 0 | {best.T.Shift:0.00} | {best.T.Forward:0.00} | {best.T.Side:0.00} | {best.C:0.000} | - |");
        float N() => (float)(Math.Sqrt(-2.0 * Math.Log(1.0 - random.NextDouble())) * Math.Cos(2.0 * Math.PI * random.NextDouble()));
        for (var i = 1; i <= candidates; i++)
        {
            var t = new StickKick.Teaching(Math.Clamp(best.T.Shift + (step * N()), 0f, 0.6f), Math.Clamp(best.T.Forward + (step * N()), -0.4f, 0.4f), Math.Clamp(best.T.Side + (step * N()), -0.4f, 0.4f));
            var c = Cost(kick.Run(kind, style, t, approach));
            var taken = c < best.C;
            log($"| {i} | {t.Shift:0.00} | {t.Forward:0.00} | {t.Side:0.00} | {c:0.000} | {(taken ? "採った" : "")} |");
            if (taken)
            {
                best = (t, c);
                successes++;
            }

            if (i % 5 == 0)
            {
                step *= successes > 1 ? 1.3f : 0.75f;
                successes = 0;
            }
        }

        log("");
        log($"見つけた値: 間 {best.T.Shift:0.00}・前 {best.T.Forward:0.00}・横 {best.T.Side:0.00}（`kickbalance --teachings {best.T.Shift:0.00}:{best.T.Forward:0.00}:{best.T.Side:0.00}` で物理を確かめる）");
        log("");
        return best.T;
    }
}
