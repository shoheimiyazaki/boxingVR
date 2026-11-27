using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Aigis.Render;

namespace MiniRing.Arena;

/// <summary>
/// 腰の駆動の比べ（2026-11-27、調べ用）: 同じ右ストレート（守らない相手・間合い 1.05・種 1）を、強い腰（既定）と弱い腰（既定の骨盤の回旋の 0.4 倍）で撮る。
/// 横（打ち手の右手の側から寄って）と上から、胸（赤）と骨盤（青）の向きの線つきで、1 本ずつの mp4（<c>strong_side</c>・<c>weak_side</c>・<c>strong_top</c>・<c>weak_top</c>）。打ち出しの前後は 1/4 のスロー。
/// 版ごとに同じコマの並びになる（時計は打ち出しからの時間だけで決める）ので、別のビルドで撮った物を ffmpeg で並べられる。
/// </summary>
internal static class HipCompare
{
    private const int Fps = 30;
    private const float Slow = 4f;
    private const float SlowFrom = -0.2014f;
    private const float SlowTo = 0.6714f;
    private const float After = 1.5f;

    /// <param name="label">画面の左上に出す名前（版・体）。</param>
    /// <param name="outDir">mp4 を書くフォルダ。</param>
    /// <param name="range">間合い: 数（胸と胸、腕の長さ倍）か <c>chin</c>（腕を伸ばしきったグローブの先がちょうどあごに触れる所。体の深さが違う体どうしを比べるとき）。</param>
    /// <param name="width">画の幅。</param>
    /// <param name="height">画の高さ。</param>
    public static int Run(string label, string outDir, string range = "1.05", int width = 640, int height = 480)
    {
        Directory.CreateDirectory(outDir);
        foreach (var (name, title, tuning) in new[]
        {
            ("strong", "強い腰", FighterTuning.Default),
            // 弱い腰は既定の骨盤の回旋の 0.4 倍（前の基準の体の 150 → 60 N·m と同じ割合。基準の体の筋の置き直しの前後で同じ比べになる）。
            ("weak", "弱い腰（骨盤の回旋 0.4 倍）", FighterTuning.Default with { PelvisRotatorTorque = FighterTuning.Default.PelvisRotatorTorque * 0.4f }),
        })
        {
            foreach (var view in new[] { "side", "top" })
            {
                Film(label, title, tuning, view, range, Path.Combine(outDir, $"{name}_{view}.mp4"), width, height);
            }
        }

        return 0;
    }

    private static void Film(string label, string title, FighterTuning tuning, string view, string range, string path, int width, int height)
    {
        using var renderer = new HeadlessRenderer((uint)width, (uint)height);
        using var ffmpeg = Process.Start(new ProcessStartInfo("ffmpeg",
            $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {width}x{height} -r {Fps} -i - -c:v libx264 -pix_fmt yuv420p -crf 20 -preset medium \"{path}\"")
        {
            RedirectStandardInput = true,
            UseShellExecute = false,
        }) ?? throw new IOException("ffmpeg を起動できません（PATH にありますか）。");
        var chin = range == "chin";
        using var bout = new PunchLab.Bout(new(PunchLabPath.Standard, "still", chin ? "chin" : "reach", chin ? 0f : float.Parse(range, CultureInfo.InvariantCulture), false), 1, 0.06f, tuning);
        while (bout.PunchTime is null)
        {
            bout.Step();
        }

        var f = bout.Game.Player;
        var note = "";
        var next = float.NegativeInfinity;
        while (bout.PunchTime is { } t && t < After)
        {
            bout.Step();
            foreach (var hit in bout.Game.RecentHits.Where(h => h.Attacker == f && h.Hand == Hand.Right && !h.Kick && !h.Elbow))
            {
                note = string.Create(CultureInfo.InvariantCulture, $"当たり {hit.Speed:0.00} m/s  {hit.ImpactMass:0.00} kg  {hit.Energy:0} J");
            }

            var clock = bout.PunchTime ?? 0f;
            if (clock < next)
            {
                continue;
            }

            var slow = clock >= SlowFrom && clock <= SlowTo;
            next = clock + ((slow ? 1f / Slow : 1f) / Fps);
            var chestYaw = f.ChestAngularVelocity.Y;
            ScreenTextDraw[] text =
            [
                new($"{label}  {title}", new Vector2(16f, 28f), 22f, new Vector4(1f, 1f, 1f, 1f), TextAnchor.Left),
                new(note, new Vector2(16f, 58f), 20f, new Vector4(1f, 0.85f, 0.3f, 1f), TextAnchor.Left),
                new(string.Create(CultureInfo.InvariantCulture, $"拳 {f.GloveVelocity(Hand.Right).Length():0.00} m/s  胸のひねり {MathF.Abs(chestYaw):0.0} rad/s"), new Vector2(16f, 86f), 18f, new Vector4(0.8f, 0.8f, 0.8f, 1f), TextAnchor.Left),
                new(string.Create(CultureInfo.InvariantCulture, $"{clock:0.00} s{(slow ? "  1/4 のスロー" : "")}  {(view == "side" ? "横から" : "上から")}"), new Vector2(width - 16f, height - 24f), 18f, new Vector4(0.8f, 0.8f, 0.8f, 1f), TextAnchor.Right),
            ];
            ffmpeg.StandardInput.BaseStream.Write(Render(renderer, width, height, bout.Game, view, text));
        }

        ffmpeg.StandardInput.Close();
        ffmpeg.WaitForExit();
        Console.WriteLine($"{path}: {note}");
    }

    private static byte[] Render(HeadlessRenderer renderer, int width, int height, BoxingGame game, string view, IReadOnlyList<ScreenTextDraw> text)
    {
        var instances = new List<PrimitiveInstance>();
        var lines = new List<DebugLine>();
        BoxingRenderer.Fill(game, showTargetGhosts: false, instances, lines);
        // 胸（肩の線）と骨盤（腰の線）の向き: 部位の左右の軸を、部位の中心から前へ少し出して描く（体に隠れない）。上から見ると 2 本の角度の差が胴のひねり。
        foreach (var (part, color) in new[] { (game.Player.Parts.Chest, new Vector4(1f, 0.3f, 0.2f, 1f)), (game.Player.Parts.Pelvis, new Vector4(0.2f, 0.6f, 1f, 1f)) })
        {
            var s = game.World.GetState(part);
            var across = Vector3.Transform(Vector3.UnitX, s.Orientation) * 0.3f;
            var front = Vector3.Transform(Vector3.UnitZ, s.Orientation) * 0.15f;
            // Fill は線を表示の空間へ移し終えているので、足す線も同じ写しで表示の空間へ。
            // 線は 1 画素なので、少しずつずらした 5 本を重ねて太く見せる（縦と横に ±4 mm）。
            foreach (var shift in new[] { Vector3.Zero, new Vector3(0f, 0.004f, 0f), new Vector3(0f, -0.004f, 0f), new Vector3(0.004f, 0f, 0f), new Vector3(0f, 0f, 0.004f) })
            {
                lines.Add(DisplaySpace.Standard.Line(new DebugLine(s.Position + front - across + shift, s.Position + front + across + shift, color)));
                lines.Add(DisplaySpace.Standard.Line(new DebugLine(s.Position + front + shift, s.Position + (front * 3f) + shift, color)));
            }
        }

        var a = game.Player.ChestPosition;
        var v = game.Cpu.ChestPosition;
        var direction = Vector3.Normalize((v - a) with { Y = 0f });
        var side = Vector3.Cross(Vector3.UnitY, direction);
        var middle = ((a + v) * 0.5f) + new Vector3(0f, -0.1408f, 0f);
        Vector3 eye;
        var up = Vector3.UnitY;
        if (view == "side")
        {
            middle = Vector3.Lerp(a, v, 0.4f) + new Vector3(0f, -0.3f, 0f); // 打ち手に少し寄せる（頭から膝まで入る）
            eye = middle - (side * 2.2f) + new Vector3(0f, 0.05f, 0f); // 打ち手の右手の側から真横に
        }
        else
        {
            middle = (a + v) * 0.5f;
            eye = middle + new Vector3(0f, 1.9f, 0f);
            up = direction; // 上から: 画面の上が相手の方
        }

        var (vp, displayEye) = DisplaySpace.Standard.Camera(eye, middle, up, 45f * MathF.PI / 180f, (float)width / height, 0.02f, 30f);
        return renderer.RenderPrimitiveScene(ArenaLook.Background, vp, instances, lines, text, particles: game.BuildParticles(), lighting: game.BuildLighting(),
            shadow: game.BuildShadow(), cameraPosition: displayEye, shapes: [], postProcess: game.BuildPostProcess(), voxels: game.BuildVoxelVolumes());
    }
}
