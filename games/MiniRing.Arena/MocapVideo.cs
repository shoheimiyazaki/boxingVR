using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Aigis.Render;
using MiniRing.Motion;

namespace MiniRing.Arena;

/// <summary>
/// モーションキャプチャ（BVH）を棒人間の mp4 にする（読めているかと、何が入っているかを見るため）。両手を結ぶ線を剣の向きとして、
/// 右手の先へ刀身（実寸 0.75 m）を描く。画面の隅に時刻と、剣先の速さ・高さ。BVH の単位はインチとして m に直す。
/// </summary>
internal sealed class MocapVideo(int width, int height, int fps)
{
    private const float Inch = 0.0254f;
    private const float Blade = 0.75f;
    private static readonly Vector4 Bone = new(0.9f, 0.9f, 0.95f, 1f);
    private static readonly Vector4 Steel = new(1f, 0.85f, 0.3f, 1f);
    private static readonly Vector4 White = new(1f, 1f, 1f, 1f);

    public void Make(string bvhPath, string outPath, float from, float to, float speed, TextWriter log)
    {
        var clip = BvhClip.Load(bvhPath);
        var left = clip.IndexOf("LeftHand");
        var right = clip.IndexOf("RightHand");
        var hips = clip.IndexOf("Hips");
        using var renderer = new HeadlessRenderer((uint)width, (uint)height);
        using var ffmpeg = Process.Start(new ProcessStartInfo("ffmpeg",
            $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {width}x{height} -r {fps} -i - -c:v libx264 -pix_fmt yuv420p -crf 20 -preset medium \"{outPath}\"")
        {
            RedirectStandardInput = true,
            UseShellExecute = false,
        }) ?? throw new IOException("ffmpeg を起動できません（PATH にありますか）。");
        var lines = new List<DebugLine>();
        var end = MathF.Min(to, clip.Duration);
        Vector3? lastTip = null;
        var lastLog = -1f;
        for (var t = from; t < end; t += speed / fps)
        {
            var frame = (int)(t / clip.FrameTime);
            var pose = clip.Pose(frame);
            Vector3 P(int j) => pose[j].Position * Inch;
            lines.Clear();
            for (var j = 0; j < pose.Length; j++)
            {
                if (clip.ParentOf(j) is var p and >= 0)
                {
                    lines.Add(new DebugLine(P(p), P(j), Bone));
                }
            }

            var (l, r) = (Vector3.Lerp(P(left), P(clip.IndexOf("L_Wrist_End")), MotionRetarget.PalmShare), Vector3.Lerp(P(right), P(clip.IndexOf("R_Wrist_End")), MotionRetarget.PalmShare));
            var axis = Vector3.Normalize(r - l + new Vector3(1e-6f));
            var tip = r + (axis * Blade);
            lines.Add(new DebugLine(l, tip, Steel));
            var tipSpeed = lastTip is { } before ? Vector3.Distance(tip, before) / (speed / fps) : 0f;
            lastTip = tip;
            // 床の印（1 m ごと）。
            for (var g = -3; g <= 3; g++)
            {
                lines.Add(new DebugLine(new Vector3(g, 0f, -3f), new Vector3(g, 0f, 3f), new Vector4(0.3f, 0.3f, 0.35f, 1f)));
                lines.Add(new DebugLine(new Vector3(-3f, 0f, g), new Vector3(3f, 0f, g), new Vector4(0.3f, 0.3f, 0.35f, 1f)));
            }

            var center = P(hips) with { Y = 1.0f };
            var eye = center + new Vector3(-3.2f, 0.6f, 0.4f);
            var vp = Camera.ViewProjection(eye, center, Vector3.UnitY, 45f * MathF.PI / 180f, (float)width / height, 0.05f, 50f);
            var elevation = MathF.Asin(Math.Clamp(axis.Y, -1f, 1f)) * 180f / MathF.PI;
            ScreenTextDraw[] text =
            [
                new(Path.GetFileNameWithoutExtension(bvhPath), new Vector2(30f, 36f), 30f, White, TextAnchor.Left),
                new(string.Create(CultureInfo.InvariantCulture, $"t {t:0.00} s   剣先 {tipSpeed:0.0} m/s   剣の仰角 {elevation:0}°   剣先の高さ {tip.Y:0.00} m"), new Vector2(30f, 76f), 24f, Steel, TextAnchor.Left),
            ];
            var rgba = renderer.RenderPrimitiveScene(new Vector4(0.08f, 0.09f, 0.12f, 1f), vp, [], lines, text, cameraPosition: eye);
            ffmpeg.StandardInput.BaseStream.Write(rgba);
            if (t - lastLog >= 0.5f)
            {
                lastLog = t;
                log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"t {t,6:0.0}  hips ({P(hips).X:0.00},{P(hips).Y:0.00},{P(hips).Z:0.00})  R ({r.X:0.00},{r.Y:0.00},{r.Z:0.00})  L ({l.X:0.00},{l.Y:0.00},{l.Z:0.00})  el {elevation,4:0}°  tip {tipSpeed:0.0} m/s"));
            }
        }

        ffmpeg.StandardInput.Close();
        ffmpeg.WaitForExit();
        if (ffmpeg.ExitCode != 0)
        {
            throw new IOException($"ffmpeg failed with exit code {ffmpeg.ExitCode}.");
        }
    }
}
