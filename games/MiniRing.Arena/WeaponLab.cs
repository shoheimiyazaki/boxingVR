using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using Aigis.Foundation.Voxels;
using Aigis.Render;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// 武器の実験場（<see cref="WeaponDrills.Trials"/>）を、スローモーションの mp4 と結果の表にする。各項目は、構えて立つ CPU に
/// 武器を飛ばすところを、当たる所の横から撮る（GPU のヘッドレス描画 → ffmpeg）。表は失ったセルを組織ごとに数える。
/// </summary>
internal sealed class WeaponLab(int width, int height, int fps, float slow)
{
    /// <summary>各項目の題名の画面 [s]。</summary>
    private const float TitleSeconds = 1.5f;

    /// <summary>各項目で物理を進める長さ [s]（スローで <c>slow</c> 倍に伸びる）。</summary>
    private const float TrialSeconds = 0.4f;

    private static readonly Vector4 White = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 Grey = new(0.75f, 0.78f, 0.85f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.85f, 0.3f, 1f);

    /// <summary>全項目を走らせ、動画を <paramref name="videoPath"/> へ（null なら撮らない）、表を返す。</summary>
    public string Run(string? videoPath, TextWriter log)
    {
        var outcomes = new List<WeaponOutcome>();
        var watch = Stopwatch.StartNew();
        using var renderer = videoPath is null ? null : new HeadlessRenderer((uint)width, (uint)height);
        using var ffmpeg = videoPath is null ? null : StartFfmpeg(videoPath);
        var instances = new List<PrimitiveInstance>();
        var lines = new List<DebugLine>();
        foreach (var trial in WeaponDrills.Trials)
        {
            using var game = WeaponDrills.StillMatch();
            var shot = trial.Setup(game);
            var dt = 1f / (fps * slow);
            var frames = (int)MathF.Ceiling(TrialSeconds / dt);
            void Emit(IReadOnlyList<ScreenTextDraw> text, float dim)
            {
                BoxingRenderer.Fill(game, showTargetGhosts: false, instances, lines);
                var (vp, eye) = Camera(shot);
                var shapes = dim > 0f ? new[] { new UiShapeDraw(new Vector2(width * 0.5f, height * 0.5f), new Vector2(width, height), new Vector4(0f, 0f, 0f, dim)) } : [];
                var rgba = renderer!.RenderPrimitiveScene(ArenaLook.Background, vp, instances, lines, text, particles: game.BuildParticles(), lighting: game.BuildLighting(),
                    shadow: game.BuildShadow(), cameraPosition: eye, shapes: shapes, postProcess: game.BuildPostProcess(), voxels: game.BuildVoxelVolumes());
                ffmpeg!.StandardInput.BaseStream.Write(rgba);
            }

            if (ffmpeg is not null)
            {
                for (var t = 0f; t < TitleSeconds; t += 1f / fps)
                {
                    Emit(TitleCard(trial), 0.55f);
                }
            }

            var outcome = WeaponDrills.Run(trial, game, shot, frames, dt, ffmpeg is null ? null : f => Emit(Caption(trial, f * dt), 0f));
            outcomes.Add(outcome);
            log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{trial.Name}: body {outcome.BodyLost.Values.Sum()} cells, weapon {outcome.WeaponLost}, target {outcome.TargetLost}, severed {outcome.Severed}, snapped {outcome.Snapped} ({watch.Elapsed.TotalSeconds:0}s)"));
        }

        if (ffmpeg is not null)
        {
            ffmpeg.StandardInput.Close();
            ffmpeg.WaitForExit();
            if (ffmpeg.ExitCode != 0)
            {
                throw new IOException($"ffmpeg failed with exit code {ffmpeg.ExitCode}.");
            }
        }

        return Table(outcomes);
    }

    /// <summary>当たる所を、飛ぶ向きの横・少し上から見る（武器が画面を横切る）。</summary>
    private (Matrix4x4 ViewProjection, Vector3 Eye) Camera(WeaponShot shot)
    {
        var side = Vector3.Cross(shot.Heading, Vector3.UnitY);
        side = side.LengthSquared() > 1e-4f ? Vector3.Normalize(side) : Vector3.UnitZ; // 真上から打つときは、リングの内側（+Z）から見る
        var eye = shot.Focus + (side * 0.32f) + new Vector3(0f, 0.1f, 0f) - (shot.Heading * 0.05f);
        return (Aigis.Render.Camera.ViewProjection(eye, shot.Focus, Vector3.UnitY, 40f * MathF.PI / 180f, (float)width / height, 0.01f, 30f), eye);
    }

    private ScreenTextDraw[] TitleCard(WeaponTrial trial) =>
    [
        new("武器の実験場", new Vector2(width * 0.5f, height * 0.3f), 34f, Grey, TextAnchor.Center),
        new(trial.Title, new Vector2(width * 0.5f, height * 0.45f), 60f, White, TextAnchor.Center),
        new(trial.Detail, new Vector2(width * 0.5f, height * 0.58f), 32f, Gold, TextAnchor.Center),
        new("壊れるかどうかは、触れた形（応力の集中）と材質の強さだけで決まる", new Vector2(width * 0.5f, height * 0.82f), 26f, Grey, TextAnchor.Center),
    ];

    private ScreenTextDraw[] Caption(WeaponTrial trial, float time) =>
    [
        new(trial.Title, new Vector2(40f, 40f), 36f, White, TextAnchor.Left),
        new(trial.Detail, new Vector2(40f, 86f), 26f, Gold, TextAnchor.Left),
        new(string.Create(CultureInfo.InvariantCulture, $"{time * 1000f:0} ms  （{slow:0} 倍スロー）"), new Vector2(width - 40f, height - 40f), 26f, Grey, TextAnchor.Right),
    ];

    /// <summary>結果の表（Markdown）。</summary>
    public static string Table(IReadOnlyList<WeaponOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        var text = new StringBuilder();
        text.AppendLine("| 項目 | 打ち方 | 最初の接触で壊れた | 皮膚・肉 | 骨 | 臓器 | 腕が落ちた | 武器が失った | 相手の武器が失った | 折れた破片 | 最大圧力 [MPa] | 壊すのに使ったエネルギー [J] | 武器の最大の回転 [rad/s] |");
        text.AppendLine("|---|---|---:|---:|---:|---:|---|---:|---:|---:|---:|---:|---:|");
        foreach (var o in outcomes)
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {o.Trial.Title} | {o.Trial.Detail} | {o.FirstContactLost} | {o.Lost(VoxelTissue.Flesh)} | {o.Lost(VoxelTissue.Bone)} | {o.Lost(VoxelTissue.Organ)} | {(o.Severed ? "○" : "—")} | {o.WeaponLost} | {o.TargetLost} | {o.Snapped} | {o.PeakPressure / 1e6f:0.0} | {o.EnergySpent:0.00} | {o.MaxSpin:0} |"));
        }

        return text.ToString();
    }

    private Process StartFfmpeg(string videoPath)
    {
        var info = new ProcessStartInfo("ffmpeg",
            $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {width}x{height} -r {fps} -i - -c:v libx264 -pix_fmt yuv420p -crf 18 -preset medium \"{videoPath}\"")
        {
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        return Process.Start(info) ?? throw new IOException("ffmpeg を起動できません（PATH にありますか）。");
    }
}
