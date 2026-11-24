using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Aigis.Render;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// 記録した VR の動き（<c>.vrmotion</c>、<see cref="VrMotionClip"/>）を、ラグドールがなぞる様子を mp4 にする（<see cref="VrMotionImitation"/>）。
/// 赤が武器を両手で握り（記録の見出しの武器、または <c>--weapon</c>）、相手に背を向けて、記録した両手の目標・武器の向き・頭のずれ・歩きを
/// 自分の腕の長さで当てはめてなぞる。金の線は両手の目標（右手 = 黄、左手 = 水色の点）、画面の隅に両手の目標からのずれと、左手のトリガー（滑り）の状態を出す。
/// </summary>
internal sealed class VrMotionVideo(int width, int height, int fps, float slow, HandWear wear = HandWear.Glove)
{
    private const float LeadIn = 1.343f;
    private const float TitleSeconds = 1.5f;
    private static readonly Vector4 White = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 Grey = new(0.75f, 0.78f, 0.85f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.85f, 0.3f, 1f);
    private static readonly Vector4 Ghost = new(0.6f, 0.85f, 1f, 1f);

    /// <summary>なぞれ方: 両手の目標からの平均のずれ（腕の長さ = 1）と、なぞった人形が自分の刃で失った体のセルの数（自分を斬る記録を見分ける）。</summary>
    internal readonly record struct Tracking(float MeanRightError, float MeanLeftError, int Steps, int SelfCellsLost = 0);

    public Tracking Make(VrMotionClip clip, WeaponKind? weapon, float rate, string title, string outPath, TextWriter log)
    {
        using var renderer = new HeadlessRenderer((uint)width, (uint)height);
        using var ffmpeg = Process.Start(new ProcessStartInfo("ffmpeg",
            $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {width}x{height} -r {fps} -i - -c:v libx264 -pix_fmt yuv420p -crf 18 -preset medium \"{outPath}\"")
        {
            RedirectStandardInput = true,
            UseShellExecute = false,
        }) ?? throw new IOException("ffmpeg を起動できません（PATH にありますか）。");
        var result = Run(clip, weapon, rate, title, (game, text, lines, dim) => Emit(renderer, ffmpeg, game, text, lines, dim));
        ffmpeg.StandardInput.Close();
        ffmpeg.WaitForExit();
        if (ffmpeg.ExitCode != 0)
        {
            throw new IOException($"ffmpeg failed with exit code {ffmpeg.ExitCode}.");
        }

        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{title}: right hand error {result.MeanRightError:0.00} left hand error {result.MeanLeftError:0.00} (arm lengths, {result.Steps} steps), self cells lost {result.SelfCellsLost}"));
        return result;
    }

    /// <summary>なぞらせ、<paramref name="emit"/> へ絵を出す（null なら測るだけ）。</summary>
    internal Tracking Run(VrMotionClip clip, WeaponKind? weapon, float rate, string title,
        Action<BoxingGame, IReadOnlyList<ScreenTextDraw>, IReadOnlyList<DebugLine>, float>? emit)
    {
        VrMotionImitation? imitation = null;
        var twoHanded = MiniRing.Bots.FormSwordsman.Handles(weapon);
        using var game = new BoxingGame(Motion.SwordStance.Tuning, seed: 1, corners: new BoxingCorners("imitate", _ => new StandGuard(),
            _ => imitation = new VrMotionImitation(clip, rate, faceAway: true, leadIn: LeadIn), PlayerWeapon: weapon, PlayerTwoHanded: twoHanded, PlayerWear: wear));
        var control = imitation!;
        var reach = game.Player.ArmReach;
        var cellsBefore = BodyCells(game.Player);
        var dt = 1f / 240f;
        var length = LeadIn + (clip.Duration / rate) + 1.007f;
        var lines = new List<DebugLine>();
        var nextFrame = 0f;
        var simPerFrame = 1f / (fps * slow);
        float rightSum = 0f, leftSum = 0f;
        var count = 0;
        if (emit is not null)
        {
            for (var k = 0f; k < TitleSeconds; k += 1f / fps)
            {
                emit(game, [
                    new("VR の動きをなぞる", new Vector2(width * 0.5f, height * 0.3f), 34f, Grey, TextAnchor.Center),
                    new(title, new Vector2(width * 0.5f, height * 0.46f), 48f, White, TextAnchor.Center),
                    new(string.Create(CultureInfo.InvariantCulture, $"{weapon?.ToString() ?? "素手"}・{clip.Duration:0.0} 秒の記録を {rate:0.00} 倍の速さで（腕の長さ = 1 に正規化して当てはめ）"), new Vector2(width * 0.5f, height * 0.6f), 26f, Gold, TextAnchor.Center),
                ], [], 0.55f);
            }
        }

        for (var t = 0f; t < length; t += dt)
        {
            game.Step(default, dt);
            var frame = game.Player.Frame;
            var target = control.Current;
            var rightTarget = frame.LocalToWorld(target.RightTarget * reach);
            var leftTarget = frame.LocalToWorld(target.LeftTarget * reach);
            if (control.Started && !control.Finished)
            {
                rightSum += Vector3.Distance(game.Player.GloveCenter(Hand.Right), rightTarget) / reach;
                leftSum += Vector3.Distance(game.Player.GloveCenter(Hand.Left), leftTarget) / reach;
                count++;
            }

            if (emit is null || t < nextFrame)
            {
                continue;
            }

            nextFrame = t + simPerFrame;
            lines.Clear();
            lines.Add(new DebugLine(leftTarget, rightTarget, Gold));
            AddCross(lines, rightTarget, Gold, reach * 0.12f);
            AddCross(lines, leftTarget, Ghost, reach * 0.12f);
            var rightError = Vector3.Distance(game.Player.GloveCenter(Hand.Right), rightTarget) / reach;
            var leftError = Vector3.Distance(game.Player.GloveCenter(Hand.Left), leftTarget) / reach;
            emit(game, [
                new(title, new Vector2(40f, 40f), 32f, White, TextAnchor.Left),
                new(string.Create(CultureInfo.InvariantCulture, $"右手の目標からのずれ {rightError:0.00}  左手 {leftError:0.00}（腕の長さ = 1）"), new Vector2(40f, 84f), 24f, Gold, TextAnchor.Left),
                new(string.Create(CultureInfo.InvariantCulture, $"左手のトリガー: {(target.LeftSlide ? "離している（棒を滑らせる）" : "握っている")}  右手: {(target.RightSlide ? "離している" : "握っている")}"), new Vector2(40f, 116f), 24f, Ghost, TextAnchor.Left),
                new(string.Create(CultureInfo.InvariantCulture, $"記録 {control.ClipTime:0.00} s  /  {t:0.00} s  （{slow:0.#} 倍スロー）"), new Vector2(width - 40f, height - 40f), 24f, Grey, TextAnchor.Right),
            ], lines, 0f);
        }

        var selfCells = cellsBefore - BodyCells(game.Player);
        return count == 0 ? new Tracking(float.NaN, float.NaN, 0, selfCells) : new Tracking(rightSum / count, leftSum / count, count, selfCells);
    }

    private static int BodyCells(Fighter fighter) => fighter.Voxels?.Parts.Sum(p => p.Body.Grid.FilledCount) ?? 0;

    private static void AddCross(List<DebugLine> lines, Vector3 p, Vector4 color, float size)
    {
        lines.Add(new DebugLine(p - new Vector3(size, 0f, 0f), p + new Vector3(size, 0f, 0f), color));
        lines.Add(new DebugLine(p - new Vector3(0f, size, 0f), p + new Vector3(0f, size, 0f), color));
        lines.Add(new DebugLine(p - new Vector3(0f, 0f, size), p + new Vector3(0f, 0f, size), color));
    }

    private void Emit(HeadlessRenderer renderer, Process ffmpeg, BoxingGame game, IReadOnlyList<ScreenTextDraw> text, IReadOnlyList<DebugLine> extra, float dim)
    {
        var instances = new List<PrimitiveInstance>();
        var lines = new List<DebugLine>();
        BoxingRenderer.Fill(game, showTargetGhosts: false, instances, lines);
        lines.AddRange(extra);
        var frame = game.Player.Frame;
        var center = game.Player.ChestPosition + (frame.Right * 0.4225f) + new Vector3(0f, -0.141f, 0f);
        // 横から: 体の右（−X）から、少し前寄り。
        var eye = center + (frame.Right * 2.676f) + (frame.Forward * 0.704f) + new Vector3(0f, 0.338f, 0f);
        var (vp, displayEye) = DisplaySpace.Standard.Camera(eye, center, Vector3.UnitY, 45f * MathF.PI / 180f, (float)width / height, 0.02f, 30f);
        UiShapeDraw[] shapes = dim > 0f ? [new UiShapeDraw(new Vector2(width * 0.5f, height * 0.5f), new Vector2(width, height), new Vector4(0f, 0f, 0f, dim))] : [];
        var rgba = renderer.RenderPrimitiveScene(ArenaLook.Background, vp, instances, lines, text, particles: game.BuildParticles(), lighting: game.BuildLighting(),
            shadow: game.BuildShadow(), cameraPosition: displayEye, shapes: shapes, postProcess: game.BuildPostProcess(), voxels: game.BuildVoxelVolumes());
        ffmpeg.StandardInput.BaseStream.Write(rgba);
    }
}
