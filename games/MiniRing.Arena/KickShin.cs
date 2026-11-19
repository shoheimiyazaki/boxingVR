using System.Numerics;
using System.Globalization;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>回し蹴りをすねの下の方で当てる練習と、ロー・ミドル・ハイの表・左右の差の調べ（kickshin）。</summary>
internal static class KickShin
{
    private static float Dist(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Z), new(b.X, b.Z));

    private static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>練習する高さ。狙う所の部位（<see cref="HitZone"/>）の集まりと技。</summary>
    internal sealed record Height(string Name, KickTechnique Technique, HitZone[] Zones, string File, bool ShinBand = true);

    internal static readonly Height[] Heights =
    [
        new("ロー（腿）", KickTechnique.RoundhouseLow, [HitZone.Leg, HitZone.Knee], "roundhouse_low"),
        new("ミドル（胴）", KickTechnique.Roundhouse, [HitZone.Liver, HitZone.SolarPlexus, HitZone.Body, HitZone.Heart], "roundhouse_mid"),
        new("ハイ（頭）", KickTechnique.RoundhouseHigh, [HitZone.Chin, HitZone.Temple, HitZone.Head, HitZone.Neck], "roundhouse_high"),
        new("前蹴り（胴）", KickTechnique.Front, [HitZone.Liver, HitZone.SolarPlexus, HitZone.Body, HitZone.Heart], "front_mid", ShinBand: false),
    ];

    /// <summary>1 つの条件: 蹴る体・受ける体・試合の種。受ける体の違いと種が、調整に使った条件と使っていない条件を分ける。</summary>
    internal sealed record Condition(string Attacker, string Victim, int Seed);

    /// <summary>練習の差分の探索の範囲（正規化 [0, 1] ↔ 実際の値）。</summary>
    internal static readonly (string Name, float Low, float High)[] Ranges =
    [
        ("間合い", 0.75f, 1.9f), ("長さ[s]", 0.4f, 0.95f), ("方位のずれ", -0.5f, 0.5f), ("仰角のずれ", -0.5f, 0.5f), ("膝を開く拍子", -0.15f, 0.15f), ("足首の曲げ", -1.0f, 0.4f),
    ];

    internal static KickTune Decode(IReadOnlyList<double> x)
    {
        float V(int i) => Ranges[i].Low + ((Ranges[i].High - Ranges[i].Low) * (float)Math.Clamp(x[i], 0.0, 1.0));
        return new KickTune(V(0), V(1), V(2), V(3), V(4), V(5));
    }

    internal static double[] Encode(KickTune t)
    {
        double N(int i, float v) => (v - Ranges[i].Low) / (Ranges[i].High - Ranges[i].Low);
        return [N(0, t.Standoff), N(1, t.Seconds), N(2, t.AzimuthShift), N(3, t.ElevationShift), N(4, t.BendShift), N(5, t.FootPitch)];
    }

    private static BodyBuild Body(string id) => id == "standard" ? BodyBuild.Standard : Characters.CharacterRoster.Get(id).Build;

    /// <summary>1 本の蹴り。<paramref name="rear"/> が true なら Rear = 右脚（構えの後ろの足）、Front = 左脚（前の足）。</summary>
    internal static KneePractice.Outcome Kick(Height h, bool rear, KickTune tune, Condition c)
    {
        var spec = KickSpec.Of(h.Technique) with { Tunes = new KickTunes(tune, tune) };
        return KickMocap.Doll(Body(c.Attacker), spec, rear ? Hand.Right : Hand.Left, tune.Standoff, tune.Seconds, c.Seed, null, null, Body(c.Victim));
    }

    internal const float ShinLow = 0.55f;
    internal const float ShinHigh = 0.9f;

    /// <summary>
    /// 点数（大きいほど良い）: 狙った所（<see cref="Height.Zones"/>）に、すねの 0.55〜0.9 の点で当たることが先で、その中でダメージ（主）・運動量。
    /// 条件を満たす蹴り（◎）だけがダメージ（主）・運動量で点を得る。満たさない蹴りはダメージの 1 割と、外れ −40・狙った所でない −30・すねの帯から外れた −15 と外れた距離 −100・蹴った側が倒れた −50。
    /// </summary>
    internal static double Score(Height h, KneePractice.Outcome o)
    {
        if (!o.Landed)
        {
            return -40;
        }

        var band = !h.ShinBand ? 0f : o.Fraction < ShinLow ? ShinLow - o.Fraction : o.Fraction > ShinHigh ? o.Fraction - ShinHigh : 0f;
        var ok = InBand(h, o);
        return (ok ? o.Damage + (0.2 * o.Momentum) : 0.1 * o.Damage) - (band > 0 ? 15 + (100 * band) : 0) - (h.Zones.Contains(o.Zone) ? 0 : 30) - (o.Fell ? 50 : 0);
    }

    internal static bool InBand(Height h, KneePractice.Outcome o) => o.Landed && (!h.ShinBand || o.Fraction is >= ShinLow and <= ShinHigh) && h.Zones.Contains(o.Zone) && !o.Fell;

    /// <summary>高さ × 脚ごとの練習: CMA-ES で調整の条件の平均点を最大にする。</summary>
    internal static (KickTune Tune, double Start, double Best) Practice(Height h, bool rear, KickTune start, IReadOnlyList<Condition> conditions, int gens, int lambda, int parallel, Action<string> log, int seed = 7)
    {
        double ScoreOf(KickTune t)
        {
            return conditions.Select(c => Score(h, Kick(h, rear, t, c))).Average();
        }

        var x0 = Encode(start);
        var cma = new Evolution.CmaEs(x0, seed: seed, initialSigma: 0.15, maximize: true, lambda: lambda, maxGenerations: gens);
        var startScore = ScoreOf(start);
        var best = (Score: startScore, X: x0);
        log($"始め: {F((float)startScore)}");
        while (cma.Generation < gens)
        {
            var points = cma.Ask();
            var scores = new double[points.Length];
            Parallel.For(0, points.Length, new ParallelOptions { MaxDegreeOfParallelism = parallel }, i => scores[i] = ScoreOf(Decode(points[i])));
            cma.Tell(scores);
            var k = Array.IndexOf(scores, scores.Max());
            if (scores[k] > best.Score)
            {
                best = (scores[k], points[k]);
            }

            log($"世代 {cma.Generation}: 最良 {F((float)scores.Max())} 全体の最良 {F((float)best.Score)} σ {F((float)cma.Sigma, "0.000")}");
        }

        return (Decode(best.X), startScore, best.Score);
    }

    internal static readonly Condition[] TuneConditions = [new("taeyun", "standard", 1), new("standard", "standard", 1), new("taeyun", "joao", 2), new("standard", "joao", 2), new("taeyun", "yoandry", 3), new("standard", "yoandry", 3)];

    internal static readonly Condition[] ConfirmConditions = [new("taeyun", "akira", 11), new("standard", "akira", 11), new("taeyun", "gaston", 12), new("standard", "gaston", 12), new("taeyun", "edgar", 13), new("standard", "edgar", 13)];

    /// <summary>練習の前の差分（人の動きの手本のまま。間合い・長さは kick_mocap_doll.md で振って選んだ値、中段は下段と上段の間）。</summary>
    internal static KickTune Baseline(Height h) => h.Technique switch
    {
        KickTechnique.RoundhouseHigh => new KickTune(1.2f, 0.55f, 0f, 0f, 0f, -0.5f),
        KickTechnique.Roundhouse => new KickTune(1.0f, 0.6f, 0f, 0f, 0f, -0.5f),
        KickTechnique.Front => new KickTune(0.95f, 0.6f, 0f, 0f, 0f, -0.45f),
        _ => new KickTune(1.0f, 0.65f, 0f, 0f, 0f, -0.5f),
    };

    /// <summary>練習して表を作る。<paramref name="practice"/> が false なら練習せず、組み込みの差分（練習の結果）と練習の前を比べる。</summary>
    public static void Run(IReadOnlyList<Height> heights, bool practice, int gens, int lambda, int parallel, string? jsonDir, Action<string> log, int seed = 7)
    {
        log($"# kick_shin_lmh: 回し蹴りをロー・ミドル・ハイに分けて、すねの下の方（{F(ShinLow)}〜{F(ShinHigh)}）で当てる練習（PhysicsVersion {Fighter.PhysicsVersion}）");
        log("");
        log("相手は構えて立つだけ（`StandGuard`）。蹴る人の「後ろ（右）」は構えの後ろの足、「前（左）」は前の足（原因: docs/arena/kick_shin_lmh_cause.md）。すねの点 = 当たった瞬間の膝 → 足首の割合。◎ = 狙った所に、すねの帯の中で当たり、蹴った側は立ったまま。");
        var tuneText = string.Join("、", TuneConditions.Select(c => c.Attacker + "→" + c.Victim));
        var confirmText = string.Join("、", ConfirmConditions.Select(c => c.Attacker + "→" + c.Victim + " 種 " + c.Seed));
        log($"調整に使った条件: {tuneText}。使っていない条件（比べ直し）: {confirmText}。");
        log("");
        var summary = new List<string>();
        var rows = new List<string>();
        var all = new List<KneePractice.Outcome>();
        foreach (var h in heights)
        {
            var embedded = practice ? null : KickTunes.Load(h.File);
            var results = new Dictionary<bool, KickTune>();
            foreach (var rear in new[] { true, false })
            {
                var start = Baseline(h);
                if (practice)
                {
                    log($"## 練習: {h.Name} {(rear ? "後ろ" : "前")}");
                    var (tuned, _, _) = Practice(h, rear, start, TuneConditions, gens, lambda, parallel, log, seed);
                    results[rear] = tuned;
                }
                else
                {
                    results[rear] = rear ? embedded!.Rear : embedded!.Front;
                }
            }

            if (practice && jsonDir is not null)
            {
                File.WriteAllText(Path.Combine(jsonDir, h.File + ".tune.json"), new KickTunes(results[true], results[false]).ToJson());
            }

            foreach (var rear in new[] { true, false })
            {
                var before = Baseline(h);
                var after = results[rear];
                log($"- {h.Name} {(rear ? "後ろ（右）" : "前（左）")}: 前 間合い {F(before.Standoff)} 長さ {F(before.Seconds)} → 後 間合い {F(after.Standoff)} 長さ {F(after.Seconds)} 方位 {F(after.AzimuthShift)} 仰角 {F(after.ElevationShift)} 拍子 {F(after.BendShift)} 足首 {F(after.FootPitch)}");
                foreach (var (setName, set) in new[] { ("調整", TuneConditions), ("使っていない", ConfirmConditions) })
                {
                    foreach (var (label, tune) in new[] { ("前", before), ("後", after) })
                    {
                        var outcomes = set.Select(c => (c, o: Kick(h, rear, tune, c))).ToArray();
                        foreach (var (c, o) in outcomes)
                        {
                            rows.Add(Row($"{label}（{setName}）", h, rear, tune, c, o));
                            all.Add(o);
                        }

                        var hit = outcomes.Where(p => p.o.Landed).ToArray();
                        summary.Add($"| {h.Name} | {(rear ? "後ろ（右）" : "前（左）")} | {setName} | {label} | {outcomes.Count(p => InBand(h, p.o))} / {outcomes.Length} | {(hit.Length == 0 ? "-" : F(hit.Average(p => p.o.Fraction)))} | {(hit.Length == 0 ? "-" : F(hit.Average(p => p.o.Momentum)))} | {(hit.Length == 0 ? "-" : F(hit.Average(p => p.o.Damage)))} | {(hit.Length == 0 ? "-" : F(hit.Average(p => p.o.Energy)))} |");
                    }
                }
            }
        }

        log("");
        log("## まとめ（条件の平均。当たった蹴りだけの平均）");
        log("| 高さ | 脚 | 条件 | 差分 | ◎（狙った所・すねの帯・立ったまま） | すねの点 | 運動量 | ダメージ | エネルギー [J] |");
        log("|---|---|---|---|---|---|---|---|---|");
        foreach (var line in summary)
        {
            log(line);
        }

        log("");
        log("## 足の部位ごと（上の全部の蹴りを、最初に当たった足の部位で分けた平均。すねの方がダメージが小さい理由を見る）");
        log("| 足の部位 | 本数 | 当たった瞬間の足の速さ [m/s] | 運動量 | ダメージ | エネルギー [J] |");
        log("|---|---|---|---|---|---|");
        foreach (var g in all.Where(o => o.Landed).GroupBy(o => o.Part).OrderBy(g => g.Key))
        {
            log($"| {g.Key} | {g.Count()} | {F(g.Average(o => o.Speed))} | {F(g.Average(o => o.Momentum))} | {F(g.Average(o => o.Damage))} | {F(g.Average(o => o.Energy))} |");
        }

        log("");
        log("## 1 本ずつ");
        log("| 高さ | 脚 | 差分 | 条件 | 当たった | すねの点 | 足の部位 | 相手の所 | 運動量 | ダメージ | エネルギー [J] | 判定 |");
        log("|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var line in rows)
        {
            log(line);
        }
    }

    /// <summary>人の蹴り（棒人間）の 1 本を 0.25 倍のスローで書き出す。</summary>
    private static void Stick(Aigis.Render.HeadlessRenderer renderer, System.Diagnostics.Process ffmpeg, string file, string title, int width, int height, int fps, float slow)
    {
        var track = Motion.KickCapture.Load(file);
        var analysis = track.Analyze();
        var origin = (track.Point("RHJC", 0)!.Value + track.Point("LHJC", 0)!.Value) * 0.5f;
        var forward = Vector3.Normalize(new Vector3(analysis.Mitt.X - origin.X, 0f, analysis.Mitt.Z - origin.Z));
        var outward = -Vector3.Cross(Vector3.UnitY, forward);
        var center = origin + (forward * 0.45f) + new Vector3(0f, -0.05f, 0f);
        var eye = center + (outward * 3.0f) + new Vector3(0f, 0.15f, 0f);
        var vp = Aigis.Render.Camera.ViewProjection(eye, center, Vector3.UnitY, 40f * MathF.PI / 180f, (float)width / height, 0.05f, 30f);
        var white = new Vector4(1f);
        var gold = new Vector4(1f, 0.85f, 0.3f, 1f);
        var green = new Vector4(0.5f, 1f, 0.6f, 1f);
        var blue = new Vector4(0.6f, 0.8f, 1f, 1f);
        var step = 200f * slow / fps;
        for (var f = 0f; f < track.FrameCount; f += step)
        {
            var i = (int)f;
            var lines = new List<Aigis.Render.DebugLine>();
            void Seg(string a, string b, Vector4 color)
            {
                if (track.Point(a, i) is { } p && track.Point(b, i) is { } q)
                {
                    lines.Add(new Aigis.Render.DebugLine(p, q, color));
                }
            }

            foreach (var (a, b) in new[] { ("RHJC", "RKJC"), ("RKJC", "RAJC"), ("RAJC", "RTOE"), ("RAJC", "RHEE") })
            {
                Seg(a, b, gold);
            }

            foreach (var (a, b) in new[] { ("LHJC", "LKJC"), ("LKJC", "LAJC"), ("LAJC", "LTOE"), ("LAJC", "LHEE") })
            {
                Seg(a, b, blue);
            }

            foreach (var (a, b) in new[] { ("LHJC", "RHJC"), ("LSJC", "RSJC"), ("LHJC", "LSJC"), ("RHJC", "RSJC"), ("C7", "CLAV") })
            {
                Seg(a, b, green);
            }

            for (var m = 1; m <= 6; m++)
            {
                if (track.Point($"Tarcza{m}", i) is { } mp)
                {
                    lines.Add(new Aigis.Render.DebugLine(mp - new Vector3(0.03f, 0f, 0f), mp + new Vector3(0.03f, 0f, 0f), white));
                    lines.Add(new Aigis.Render.DebugLine(mp - new Vector3(0f, 0.03f, 0f), mp + new Vector3(0f, 0.03f, 0f), white));
                }
            }

            lines.Add(new Aigis.Render.DebugLine(origin with { Y = 0f } - (forward * 1f), origin with { Y = 0f } + (forward * 2f), new Vector4(0.4f, 0.4f, 0.45f, 1f)));
            var text = new Aigis.Render.ScreenTextDraw[]
            {
                new($"人の蹴り（棒人間）: {title}", new Vector2(40f, 40f), 28f, white, Aigis.Render.TextAnchor.Left),
                new($"{i / 200f:0.00} s  金 = 蹴る右脚  {slow:0.##} 倍スロー  白の十字 = ミット  出典 Szczęsna ら 2021 (CC0)", new Vector2(40f, 76f), 22f, gold, Aigis.Render.TextAnchor.Left),
            };
            ffmpeg.StandardInput.BaseStream.Write(renderer.RenderPrimitiveScene(new Vector4(0.08f, 0.09f, 0.12f, 1f), vp, [], lines, text, cameraPosition: eye));
        }
    }

    /// <summary>
    /// 人形の蹴り 1 本を、蹴る脚の側から真横と斜め前の 2 つの向きで 0.25 倍のスローで撮る。カメラはリングの中（ロープ・柱の内側）に収める。
    /// 1 本目に当たりを調べ（描かない）、2 本目で当たった所の字幕を付けて描く。
    /// </summary>
    private static void DollShot(Aigis.Render.HeadlessRenderer renderer, System.Diagnostics.Process ffmpeg, Height h, bool rear, string bodyId, string victimId, int width, int height, int fps, float slow)
    {
        var tune = KickSpec.Of(h.Technique).TuneFor(rear ? Hand.Right : Hand.Left)!;
        var spec = KickSpec.Of(h.Technique);
        var side = rear ? Hand.Right : Hand.Left;
        var outcome = Kick(h, rear, tune, new Condition(bodyId, victimId, 1));
        var white = new Vector4(1f);
        var gold = new Vector4(1f, 0.85f, 0.3f, 1f);
        foreach (var oblique in new[] { false, true })
        {
            var next = 0f;
            var hitText = outcome.Landed ? $"当たった: 足は {outcome.Part}・すねの点 {F(outcome.Fraction)}・相手は {outcome.Zone}・運動量 {F(outcome.Momentum)}・ダメージ {F(outcome.Damage)}" : "外れ";
            KickMocap.Doll(Body(bodyId), spec, side, tune.Standoff, tune.Seconds, 1, (game, goal, t) =>
            {
                if (t < next)
                {
                    return;
                }

                next = t + (slow / fps);
                var lines = new List<Aigis.Render.DebugLine>();
                if (goal is { } gp)
                {
                    lines.Add(new Aigis.Render.DebugLine(game.Player.HipPosition(side), gp, gold));
                }

                var a = game.Player.ChestPosition;
                var v = game.Cpu.ChestPosition;
                var middle = ((a + v) * 0.5f) + new Vector3(0f, -0.14f, 0f);
                var direction = Vector3.Normalize((v - a) with { Y = 0f });
                var toKickingSide = Vector3.Cross(Vector3.UnitY, direction) * (rear ? -1f : 1f);
                var offset = oblique ? (toKickingSide * 0.95f) + (direction * 0.4f) : toKickingSide * 1.25f;
                var eye = middle + offset + new Vector3(0f, 0.12f, 0f);
                var half = (game.Ring.Size * 0.5f) - 0.06f;
                eye = new Vector3(Math.Clamp(eye.X, game.Ring.Center.X - half, game.Ring.Center.X + half), eye.Y, Math.Clamp(eye.Z, game.Ring.Center.Z - half, game.Ring.Center.Z + half));
                var instances = new List<Aigis.Render.PrimitiveInstance>();
                BoxingRenderer.Fill(game, showTargetGhosts: false, instances, lines);
                var vp = Aigis.Render.Camera.ViewProjection(eye, middle, Vector3.UnitY, 55f * MathF.PI / 180f, (float)width / height, 0.02f, 30f);
                var text = new Aigis.Render.ScreenTextDraw[]
                {
                    new($"{h.Name}・{(rear ? "後ろの足（右）" : "前の足（左）")}・{(oblique ? "斜め前" : "真横")}（蹴る人 {bodyId}、受ける人 {victimId}）", new Vector2(40f, 40f), 26f, white, Aigis.Render.TextAnchor.Left),
                    new($"{t:0.00} s  {slow:0.##} 倍スロー  金の線 = 膝の制御点  間合い {F(tune.Standoff)}・長さ {F(tune.Seconds)} s", new Vector2(40f, 76f), 22f, gold, Aigis.Render.TextAnchor.Left),
                    new(hitText, new Vector2(40f, height - 40f), 24f, white, Aigis.Render.TextAnchor.Left),
                };
                var rgba = renderer.RenderPrimitiveScene(ArenaLook.Background, vp, instances, lines, text, particles: game.BuildParticles(), lighting: game.BuildLighting(),
                    shadow: game.BuildShadow(), cameraPosition: eye, postProcess: game.BuildPostProcess(), voxels: game.BuildVoxelVolumes());
                ffmpeg.StandardInput.BaseStream.Write(rgba);
            }, null, Body(victimId));
        }
    }

    /// <summary>ロー・ミドル・ハイ（と前蹴り）を、人の棒人間 → 人形（脚ごと・真横と斜め前）の順に 1 本の mp4 へ。</summary>
    public static void Video(string dir, string outPath, string bodyId, string victimId, IReadOnlyList<Height> heights, Action<string> log)
    {
        const int width = 1280, height = 720, fps = 30;
        const float slow = 0.25f;
        using var renderer = new Aigis.Render.HeadlessRenderer(width, height);
        using var ffmpeg = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ffmpeg",
            $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {width}x{height} -r {fps} -i - -c:v libx264 -pix_fmt yuv420p -crf 20 -preset medium \"{outPath}\"")
        { RedirectStandardInput = true, UseShellExecute = false }) ?? throw new IOException("ffmpeg を起動できません。");
        foreach (var h in heights)
        {
            var human = h.File switch { "roundhouse_low" => "kick_S03-T01_01.csv", "roundhouse_high" => "kick_S04-T01_01.csv", "front_mid" => "kick_S02-T01_01.csv", _ => null };
            if (human is not null)
            {
                Stick(renderer, ffmpeg, Path.Combine(dir, human), h.Name, width, height, fps, slow);
            }

            foreach (var rear in new[] { true, false })
            {
                log($"video {h.Name} {(rear ? "rear" : "front")}");
                DollShot(renderer, ffmpeg, h, rear, bodyId, victimId, width, height, fps, slow);
            }
        }

        ffmpeg.StandardInput.Close();
        ffmpeg.WaitForExit();
    }

    private static string Row(string label, Height h, bool rear, KickTune t, Condition c, KneePractice.Outcome o)
        => $"| {h.Name} | {(rear ? "後ろ（右）" : "前（左）")} | {label} | {c.Attacker} → {c.Victim} 種 {c.Seed} | {(o.Landed ? "当たった" : "外れ")} | {F(o.Fraction)} | {o.Part} | {o.Zone} | {F(o.Momentum)} | {F(o.Damage)} | {F(o.Energy)} | {(InBand(h, o) ? "◎" : "")}{(o.Fell ? "倒れた" : "")} |";

    /// <summary>左右の脚で、指令と実際の膝・曲げ・ねじり・体の向きを時間ごとに並べる（左右の差の原因を探す）。</summary>
    public static void Diag(string bodyId, KickSpec spec, float standoff, float duration, Action<string> log)
    {
        var body = bodyId == "standard" ? BodyBuild.Standard : Characters.CharacterRoster.Get(bodyId).Build;
        var baseline = Baseline(Heights.First(h => h.Technique == spec.Technique));
        spec = spec with { Tunes = new KickTunes(baseline, baseline) }; // 練習の前の差分で調べる
        standoff = baseline.Standoff;
        duration = baseline.Seconds;
        foreach (var side in new[] { Hand.Left, Hand.Right })
        {
            var mirror = -side.LocalSide(); // 右脚 +1
            log($"## {side}（x を右脚の向きへ鏡にして出す）");
            log("| t | 指令 膝(x,y,z)/脚 | 実 膝(x,y,z)/脚 | 指令曲げ | 実曲げ | 指令ねじり | 実ねじり | 体の向き[°] | 足首(x,y,z) | 股→狙い水平 |");
            log("|---|---|---|---|---|---|---|---|---|---|");
            var next = 0f;
            KickMocap.Doll(body, spec, side, standoff, duration, 1, null, (game, leg, t) =>
            {
                if (t == 0f)
                {
                    var p = game.Player;
                    var aimLocal = p.Frame.WorldToLocal(spec.AimWorld(game.PlayerSense));
                    var hipL = p.Frame.WorldToLocal(p.HipPosition(Hand.Left));
                    var hipR = p.Frame.WorldToLocal(p.HipPosition(Hand.Right));
                    var footL = p.Frame.WorldToLocal(p.AnklePosition(Hand.Left));
                    var footR = p.Frame.WorldToLocal(p.AnklePosition(Hand.Right));
                    var h = (Vector3 v) => $"({F(v.X)},{F(v.Y)},{F(v.Z)})";
                    log($"構え（体フレーム、+x = 左）: 狙い {h(aimLocal)}  左股 {h(hipL)}  右股 {h(hipR)}  左足首 {h(footL)}  右足首 {h(footR)}  股→狙いの水平距離 左 {F(Vector2.Distance(new(hipL.X, hipL.Z), new(aimLocal.X, aimLocal.Z)))} 右 {F(Vector2.Distance(new(hipR.X, hipR.Z), new(aimLocal.X, aimLocal.Z)))}");
                }

                if (t < next || leg is null)
                {
                    return;
                }

                next = t + 0.05f;
                var a = game.Player;
                var lb = a.LegBodyOf(side);
                var cmd = (leg.Value.KneeLocal!.Value - lb.Hip) / lb.Thigh;
                var knee = (a.Frame.WorldToLocal(a.KneePosition(side)) - lb.Hip) / lb.Thigh;
                var ankle = a.Frame.WorldToLocal(a.AnklePosition(side)) - lb.Hip;
                var yawDeg = a.Frame.YawRadians * 180f / MathF.PI;
                log($"| {F(t)} | ({F(mirror * cmd.X)},{F(cmd.Y)},{F(cmd.Z)}) | ({F(mirror * knee.X)},{F(knee.Y)},{F(knee.Z)}) | {F(leg.Value.KneeBend ?? 0)} | {F(a.KneeBend(side))} | {F(leg.Value.KneeTwist ?? 0)} | {F(a.ThighTwist(side))} | {F(yawDeg)} | ({F(mirror * ankle.X)},{F(ankle.Y)},{F(ankle.Z)}) | {F(Dist(a.HipPosition(side), spec.AimWorld(game.PlayerSense)))} |");
            });
        }
    }
}
