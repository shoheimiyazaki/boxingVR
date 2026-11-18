using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using MiniRing.Motion.Stick;

namespace MiniRing.Arena;

/// <summary>
/// 棒人形で、きれいな片手剣の振りを断面ごとに探す（<c>stick --swing</c>、docs/STICK_MOTION.md §11）。ランダムに引いた振りの分布と、磨いた最良を表にし、
/// 最良の振りを断面を正面から見る視点と体の正面の視点で動画にする。
/// </summary>
internal static class StickSwingLab
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private const string DefaultPlanes = "vertical,horizontal,kesa,gyaku-kesa,kiriage,gyaku-kiriage";

    /// <summary>名前に値の無い <c>--swing</c> を <c>--swing yes</c> として読めるようにする（ほかのコマンドの <c>--detail yes</c> と同じ形）。</summary>
    public static string[] WithYes(string[] args)
    {
        var list = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            list.Add(args[i]);
            if (args[i] == "--swing" && (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal)))
            {
                list.Add("yes");
            }
        }

        return [.. list];
    }

    public static void Run(Options o, Action<string> log)
    {
        var planes = o.List("planes", DefaultPlanes.Split(',')).Select(SwingPlane.Get).ToList();
        var weights = SwingWeights.Parse(o.List("weights", []));
        var options = new SwingSearchOptions(o.Int("samples", 2000, min: 1), o.Int("polish", 8, min: 1), o.Int("iterations", 3000, min: 0), o.Int("seed", 1), weights);
        var character = o.Get("character") ?? "jin";
        var videoDir = o.Get("video");
        var watch = Stopwatch.StartNew();
        var limitsName = o.Get("limits") ?? "grip";
        var limits = limitsName switch
        {
            "grip" => new SwingLimits(),
            "loose" => SwingLimits.Loose,
            _ => throw new ArgumentException($"--limits は grip（剣をがっちり握った腕。既定）か loose（今までの広い範囲）: {limitsName}"),
        };
        var rig = SwingRig.Create(character, limits);
        log($"## 棒人形で片手剣の振りを探す（{character}、鉄の剣、右手）");
        log("");
        log($"- 体: 腕の長さ {rig.ArmLength * 100f:0.0} cm・剣の長さ {rig.BladeLength * 100f:0.0} cm。動かすのは右肩（玉）・右肘・右手首だけで、ほかは構えのまま。");
        log($"- 探索: 一様に {options.Samples} 本 → 上位 {options.Polish} 本を (1+1) 進化戦略で {options.Iterations} 回ずつ磨く（種 {options.Seed}）。点の重み: 速さ {weights.Speed}・滑らか {weights.Smooth}・断面 {weights.Plane}・刃筋 {weights.Edge}・向き {weights.Direction}・断面に数える全体の割合 {weights.Whole}。");
        log("- `速さ` = 剣先の最大の速さ。`LDLJ`・`SPARC` = 主な 1 本の動き（最大の速さの山、谷から谷）の滑らかさ（大きい=0 に近いほど滑らか。躍度最小の 1 本で LDLJ −4.9・SPARC 約 −1.4）。`余山` = 主な山以外の速さの山。`断面外れ` = 肘・手首・剣先の軌跡の断面からの外れ（距離の RMS ÷ 広がり。左 = 主な 1 本の動きの始めから振り抜きまで、右 = 構えから振り抜きまで全体）。`刃筋` = 剣先の進む向きが刃の面から外れた角。`向き` = 最大の速さのときの進む向きと断面の斬る向きとの角。`負担` = 肩・肘・手首のトルク ÷ 上限の最大（1 超えは失格）。");
        log(string.Create(CultureInfo.InvariantCulture, $"- 関節の範囲（{limitsName}）: 手首の屈曲 {Degrees(limits.WristFlexLow):0}〜{Degrees(limits.WristFlexHigh):0}°・橈尺屈 {Degrees(limits.WristDeviationLow):0}〜{Degrees(limits.WristDeviationHigh):0}°・前腕のねじれ {Degrees(limits.ForearmRollLow):0}〜{Degrees(limits.ForearmRollHigh):0}°・肘 {Degrees(rig.ElbowLow):0}〜{Degrees(rig.ElbowHigh):0}°・肩のねじれ {Degrees(rig.ShoulderTwistLow):0}〜{Degrees(rig.ShoulderTwistHigh):0}°・上腕の後ろ向き sin ≤ {limits.ShoulderBack:0.00}・反対の脇へ sin ≤ {limits.ShoulderAcross:0.00}。補間のはみ出しが 1° を超えたら失格。"));
        log("");
        var results = new List<SwingSearchResult>();
        foreach (var plane in planes)
        {
            watch.Restart();
            var result = SwingSearch.Run(rig, plane, options, line => log(line));
            results.Add(result);
            log($"  （{plane.Name}: {watch.Elapsed.TotalSeconds:0.0} 秒）");
        }

        log("");
        log("| 断面 | 行 | 速さ [m/s] | LDLJ | SPARC | 余山 | 断面外れ（動き/全体） | 刃筋 [°] | 向き [°] | 負担（関節） | 時間 [s] | 点 | 有効 |");
        log("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in results)
        {
            log($"| {r.Plane.Name}（{r.Plane.Label}） | ランダム {r.Random.Count} 本の中央値 | {r.Median(e => e.PeakSpeed):0.00} | {r.Median(e => e.Ldlj):0.0} | {r.Median(e => e.Sparc):0.00} | {r.Median(e => e.ExtraPeaks):0} | {r.Median(e => e.PlaneMisfit):0.000} / {r.Median(e => e.PlaneMisfitAll):0.000} | {Degrees(r.Median(e => e.EdgeAngle)):0} | {Degrees(r.Median(e => e.DirectionAngle)):0} | {r.Median(e => e.MaxLoad):0.00} | {r.Median(e => e.Duration):0.00} | {r.Median(e => e.Score):0.0} | {r.ValidFraction * 100f:0} % |");
            log(Row(string.Empty, "ランダムの最良", r.BestRandom));
            log(Row(string.Empty, "磨いた最良", r.Best));
        }

        log("");
        foreach (var r in results)
        {
            var trace = rig.Simulate(r.BestGenes);
            var review = rig.Review(trace);
            var kinds = review.Alerts.Where(a => !a.Kind.Contains("捕捉点", StringComparison.Ordinal) && !a.Kind.Contains('床')).ToList();
            log($"### {r.Plane.Name}（{r.Plane.Label}）の最良");
            log("");
            log($"- {r.Best.Breakdown(weights)}");
            log($"- 滑らかさの内訳: 曲率の変化 {r.Best.CurvatureVariation:0.00}・余山 {r.Best.ExtraPeaks}・主な動きは {r.Best.StrokeFrom * SwingRig.Dt:0.00}〜{r.Best.StrokeTo * SwingRig.Dt:0.00} 秒。伸び {r.Best.ReachFraction:0.00}・前向き {r.Best.Forward:0.00}。刃の自傷 {r.Best.SelfCut * 100f:0.0} cm・腕の胴への入り込み {r.Best.ArmOverlap * 100f:0.0} cm。");
            log($"- 標準の見立て（バランスを除く）: 警告 {kinds.Count(a => a.Grade == StickGrade.Warning)}・注意 {kinds.Count(a => a.Grade == StickGrade.Caution)}");
            foreach (var a in kinds.Take(6))
            {
                log($"  - {a}");
            }

            log($"- 遺伝子（度・秒、K1〜K3 = 振りかぶり・打ち抜き・振り抜き）: {Describe(rig, r.BestGenes)}");
            log("");
        }

        if (videoDir is not null)
        {
            Directory.CreateDirectory(videoDir);
            foreach (var r in results)
            {
                var path = Path.Combine(videoDir, $"stick_swing_{r.Plane.Name}.mp4");
                StickSwingVideo.Save(rig, r.Plane, r.BestGenes, path);
                log($"動画: {path}");
            }
        }

        if (o.Get("bvh") is { } bvhDir)
        {
            Directory.CreateDirectory(bvhDir);
            foreach (var r in results)
            {
                var trace = rig.Simulate(r.BestGenes);
                var path = Path.Combine(bvhDir, $"stick_swing_{r.Plane.Name}.bvh");
                File.WriteAllText(path, StickBvh.Write(rig.Body, trace.Frames, trace.Dt));
                log($"BVH: {path}");
            }
        }

        if (o.Get("log") is { } md)
        {
            var json = Path.ChangeExtension(md, ".json");
            var payload = results.ToDictionary(r => r.Plane.Name, r => new { r.BestGenes, r.Best.Score, r.Best.PeakSpeed });
            File.WriteAllText(json, JsonSerializer.Serialize(payload, JsonOptions));
        }
    }

    private static float Degrees(float radians) => radians * 180f / MathF.PI;

    private static string Row(string plane, string label, SwingEval e)
        => string.Create(CultureInfo.InvariantCulture, $"| {plane} | {label} | {e.PeakSpeed:0.00} | {e.Ldlj:0.0} | {e.Sparc:0.00} | {e.ExtraPeaks} | {e.PlaneMisfit:0.000} / {e.PlaneMisfitAll:0.000} | {Degrees(e.EdgeAngle):0} | {Degrees(e.DirectionAngle):0} | {e.MaxLoad:0.00}（{e.MaxLoadJoint}） | {e.Duration:0.00} | {e.Score:0.0} | {(e.Valid ? "○" : "×")} |");

    private static string Describe(SwingRig rig, float[] genes)
    {
        var parts = new List<string>();
        for (var k = 1; k <= SwingRig.KeyCount; k++)
        {
            var at = (k - 1) * SwingRig.PerKey;
            string D(int i) => (genes[at + i] * 180f / MathF.PI).ToString("0", CultureInfo.InvariantCulture);
            parts.Add($"K{k} 肩({D(0)},{D(1)},{D(2)}) 肘{D(3)} 手首({D(4)},{D(5)},{D(6)})");
        }

        parts.Add($"時間 {genes[SwingRig.KeyCount * SwingRig.PerKey]:0.00}/{genes[(SwingRig.KeyCount * SwingRig.PerKey) + 1]:0.00}/{genes[(SwingRig.KeyCount * SwingRig.PerKey) + 2]:0.00}");
        return string.Join("、", parts);
    }
}

/// <summary>
/// 振りの動画: 1 歩を 1 コマ（歩 1/120 秒 → 30 fps で 0.25 倍のスロー）。左に断面を少し斜めから見る視点、右に体の右前の視点。棒は陰影つき・透視投影の円柱（<see cref="StickSolidRenderer"/>）、断面は半透明の円盤。
/// 肩（黒い点）・肘（緑）・手（青）・剣先（赤）の軌跡を残し（薄い線 = 振り全体、濃い線 = ここまで）、断面の軸と斬る向きの矢印を描く。
/// 下の帯は剣先の速さ。GPU は使わず、ffmpeg へ生のコマを渡す。
/// </summary>
internal static class StickSwingVideo
{
    private const int Panel = 480;
    private const int Strip = 40;
    private const int Width = Panel * 2;
    private const int Height = Panel + Strip;
    private const int Fps = 30;
    private const int Hold = 15;

    public static void Save(SwingRig rig, SwingPlane plane, float[] genes, string mp4)
    {
        var trace = rig.Simulate(genes);
        var frames = trace.Frames;
        var tip = frames.Select(f => f.Tip(StickBody.Ids.Weapon)).ToArray();
        var elbow = frames.Select(f => f.Joints[StickBody.Ids.ForearmR]).ToArray();
        var hand = frames.Select(f => f.Joints[StickBody.Ids.Weapon]).ToArray();
        var speeds = StickBeauty.Speeds(tip, trace.Dt);
        var vmax = Math.Max(1e-3f, speeds.Max());
        var views = new[] { SideView(plane), FrontView() };
        var renderer = new StickSolidRenderer(Panel, Panel);
        var floor = rig.GuardFrame.Joints.Concat(Enumerable.Range(1, rig.Body.Segments.Count - 1).Select(rig.GuardFrame.Tip)).Min(p => p.Y) - 0.03f;
        var arguments = $"-y -loglevel error -f rawvideo -pix_fmt rgb24 -s {Width}x{Height} -r {Fps} -i - -c:v libx264 -pix_fmt yuv420p -crf 18 \"{mp4}\"";
        using var ffmpeg = Process.Start(new ProcessStartInfo("ffmpeg", arguments) { RedirectStandardInput = true, UseShellExecute = false })
            ?? throw new IOException("ffmpeg を起動できません（PATH にありますか）。");
        var image = new byte[Width * Height * 3];
        var canvas = new Canvas(image);
        var total = frames.Count + Hold;
        for (var f = 0; f < total; f++)
        {
            var i = Math.Min(f, frames.Count - 1);
            Array.Fill(image, (byte)255);
            for (var v = 0; v < 2; v++)
            {
                canvas.Clip = (v * Panel, (v + 1) * Panel, Panel);
                DrawPanel(renderer, views[v], rig, plane, frames[i], i, tip, elbow, hand, floor);
                renderer.CopyTo(image, Width, v * Panel, 0);
                Marker(canvas, v);
            }

            canvas.Clip = (0, Width, Height);
            DrawStrip(canvas, speeds, vmax, i);
            ffmpeg.StandardInput.BaseStream.Write(image, 0, image.Length);
        }

        ffmpeg.StandardInput.Close();
        ffmpeg.WaitForExit();
        if (ffmpeg.ExitCode != 0)
        {
            throw new IOException($"ffmpeg が失敗しました（{ffmpeg.ExitCode}）。");
        }

        // 一覧（6 コマ、3×2）。
        var sheet = Path.ChangeExtension(mp4, null) + "_sheet.png";
        var seconds = total / (float)Fps;
        var sheetArguments = string.Create(CultureInfo.InvariantCulture, $"-y -loglevel error -i \"{mp4}\" -vf \"fps={6f / seconds:0.0000},scale=640:-1,tile=3x2\" -frames:v 1 \"{sheet}\"");
        using var sheeter = Process.Start(new ProcessStartInfo("ffmpeg", sheetArguments) { UseShellExecute = false }) ?? throw new IOException("ffmpeg を起動できません。");
        sheeter.WaitForExit();
    }

    /// <summary>視点: カメラから見る向き（単位）。</summary>
    private readonly record struct View(Vector3 Look);

    /// <summary>
    /// 断面を正面から見る視点を、少し斜め上から: 見る向きは法線（ファイターの右側（−X）か上から）に、画面の右へ 0.3・下へ 0.25 を足す（奥行きが分かるように）。
    /// </summary>
    private static View SideView(SwingPlane plane)
    {
        var look = plane.Normal;
        if (look.X < -0.1f || (MathF.Abs(look.X) <= 0.1f && look.Y > 0f))
        {
            look = -look;
        }

        var hint = MathF.Abs(look.Y) > 0.9f ? Vector3.UnitZ : Vector3.UnitY;
        var right = Vector3.Normalize(Vector3.Cross(look, hint));
        var up = Vector3.Cross(right, look);
        return new View(Vector3.Normalize(look + (right * 0.3f) - (up * 0.25f)));
    }

    /// <summary>体の正面の、右前の少し上から（ファイターは +Z を向き、右腕は −X の側）。</summary>
    private static View FrontView() => new(-Vector3.Normalize(new Vector3(-0.45f, 0.35f, 1f)));

    /// <summary>画角 [度]（縦）。肩から剣先までの 1.75 倍が画面の半分に入る距離に置く（頭の上へ振りかぶった剣先も入るように）。</summary>
    private const float Fov = 50f;

    private static readonly Vector3 BodyColor = new(0.62f, 0.62f, 0.66f);
    private static readonly Vector3 ArmColor = new(0.25f, 0.42f, 0.88f);
    private static readonly Vector3 SwordColor = new(0.92f, 0.45f, 0.12f);
    private static readonly Vector3 ElbowColor = new(0.15f, 0.62f, 0.25f);
    private static readonly Vector3 HandColor = new(0.18f, 0.35f, 0.92f);
    private static readonly Vector3 TipColor = new(0.88f, 0.15f, 0.15f);

    /// <summary>
    /// 1 つの視点のコマ: 床と影 → 体（節は円柱、太さは部位の箱の細い 2 辺の平均）・右腕（青）・剣（橙）・関節の球 → 軌跡の管（濃い = ここまで、薄い = 全体）
    /// → 斬る向きの矢印 → 断面の半透明の円盤（肩が中心）。
    /// </summary>
    private static void DrawPanel(StickSolidRenderer r, View view, SwingRig rig, SwingPlane plane, StickFrame frame, int now, Vector3[] tip, Vector3[] elbow, Vector3[] hand, float floor)
    {
        var body = rig.Body;
        var target = rig.Shoulder - new Vector3(0f, 0.1f, 0f);
        var distance = rig.Reach * 1.75f / MathF.Tan(Fov * MathF.PI / 360f);
        r.Begin(target - (view.Look * distance), target, Fov);

        var casters = new List<(Vector3 A, Vector3 B, float Radius)>();
        for (var i = 1; i < body.Segments.Count; i++)
        {
            var isArm = i is StickBody.Ids.UpperArmR or StickBody.Ids.ForearmR;
            var isSword = i == StickBody.Ids.Weapon;
            var radius = isSword ? 0.014f : Thickness(body.Segments[i]);
            var color = isSword ? SwordColor : isArm ? ArmColor : BodyColor;
            r.Capsule(frame.Joints[i], frame.Tip(i), radius, color, isSword ? 0.6f : 0.2f);
            casters.Add((frame.Joints[i], frame.Tip(i), radius));
        }

        r.Sphere(rig.Shoulder, 0.04f, new Vector3(0.12f, 0.12f, 0.12f));
        r.Sphere(frame.Joints[StickBody.Ids.ForearmR], 0.035f, ElbowColor);
        r.Sphere(frame.Joints[StickBody.Ids.Weapon], 0.035f, HandColor);
        r.Sphere(tip[now], 0.03f, TipColor);

        void Trail(Vector3[] path, Vector3 color)
        {
            var pale = Vector3.Lerp(color, Vector3.One, 0.6f);
            for (var i = 1; i < path.Length; i++)
            {
                if (i <= now)
                {
                    r.Capsule(path[i - 1], path[i], 0.008f, color, 0.1f);
                }
                else
                {
                    r.Capsule(path[i - 1], path[i], 0.004f, pale, 0f);
                }
            }
        }

        Trail(elbow, ElbowColor);
        Trail(hand, HandColor);
        Trail(tip, TipColor);

        // 斬る向きの矢印（断面の中、肩を通る）。
        var half = rig.Reach * 1.1f;
        var arrowColor = new Vector3(0.45f, 0.45f, 0.48f);
        var side = Vector3.Cross(plane.Normal, plane.Direction);
        var head = rig.Shoulder + (plane.Direction * half);
        r.Capsule(rig.Shoulder - (plane.Direction * half), head, 0.007f, arrowColor, 0f);
        r.Capsule(head, head - (plane.Direction * 0.15f) + (side * 0.08f), 0.007f, arrowColor, 0f);
        r.Capsule(head, head - (plane.Direction * 0.15f) - (side * 0.08f), 0.007f, arrowColor, 0f);

        r.Floor(floor, casters);
        r.Disc(rig.Shoulder, plane.Normal, rig.Reach * 1.15f, new Vector3(0.35f, 0.55f, 0.95f), 0.12f);
    }

    /// <summary>節の太さ: 部位の箱の、長い辺を除く 2 辺の半分の平均（腕・脚の太さ）。</summary>
    private static float Thickness(StickSegment s)
    {
        var e = new[] { s.HalfExtents.X, s.HalfExtents.Y, s.HalfExtents.Z };
        Array.Sort(e);
        return Math.Clamp((e[0] + e[1]) * 0.5f, 0.02f, 0.14f);
    }

    /// <summary>左上の印: 1 本 = 断面を正面から見る視点、2 本 = 体の正面から見る視点。</summary>
    private static void Marker(Canvas c, int view)
    {
        var left = view * Panel;
        c.Line(new Vector2(left + 6, 6), new Vector2(left + 36, 6), (0, 0, 0), 3);
        if (view > 0)
        {
            c.Line(new Vector2(left + 6, 14), new Vector2(left + 36, 14), (0, 0, 0), 3);
        }
    }

    private static void DrawStrip(Canvas c, float[] speeds, float vmax, int now)
    {
        var top = Panel;
        c.Line(new Vector2(0, top), new Vector2(Width, top), (180, 180, 180), 1);
        Vector2 P(int i) => new((i / (float)Math.Max(1, speeds.Length - 1) * (Width - 20)) + 10, top + Strip - 4 - (speeds[i] / vmax * (Strip - 10)));
        for (var i = 1; i < speeds.Length; i++)
        {
            c.Line(P(i - 1), P(i), i <= now ? (220, 40, 40) : (235, 190, 190), 2);
        }

        c.Line(new Vector2(P(now).X, top + 2), new Vector2(P(now).X, top + Strip - 2), (0, 0, 0), 1);
    }

    /// <summary>RGB の画素の並びへ点と線を描く（<see cref="Clip"/> の外は描かない）。</summary>
    private sealed class Canvas(byte[] image)
    {
        /// <summary>描いてよい範囲: x の下限（含む）・上限（含まない）、y の上限（含まない）。</summary>
        public (int X0, int X1, int Y1) Clip { get; set; } = (0, Width, Height);

        public void Dot(Vector2 p, (int R, int G, int B) color, int radius)
        {
            for (var y = -radius; y <= radius; y++)
            {
                for (var x = -radius; x <= radius; x++)
                {
                    if ((x * x) + (y * y) <= radius * radius)
                    {
                        Put((int)p.X + x, (int)p.Y + y, color);
                    }
                }
            }
        }

        public void Line(Vector2 a, Vector2 b, (int R, int G, int B) color, int thickness)
        {
            var steps = (int)MathF.Max(1f, Vector2.Distance(a, b));
            for (var i = 0; i <= steps; i++)
            {
                Dot(Vector2.Lerp(a, b, i / (float)steps), color, thickness / 2);
            }
        }

        private void Put(int x, int y, (int R, int G, int B) color)
        {
            if (x < Clip.X0 || x >= Clip.X1 || y < 0 || y >= Clip.Y1)
            {
                return;
            }

            var at = ((y * Width) + x) * 3;
            (image[at], image[at + 1], image[at + 2]) = ((byte)color.R, (byte)color.G, (byte)color.B);
        }
    }
}
