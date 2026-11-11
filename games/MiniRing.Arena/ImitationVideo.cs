using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Aigis.Render;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// ラグドールが剣の動き（<see cref="SwordMotion"/>）をなぞる様子を mp4 にする。赤が両手で剣を握り、<see cref="MotionImitation"/> で動きを
/// なぞる（相手に背を向けて、相手から離れる向きへ）。金の線は剣の目標（右手の目標から剣の向きの目標へ）。モーションキャプチャの動きなら、
/// 元の人の動きを同じ大きさに縮めた棒人間を横に並べる。画面の隅に、剣の先の目標からのずれ（体フレームで）を出す。
/// </summary>
internal sealed class ImitationVideo(int width, int height, int fps, float slow, FighterTuning? tuning = null, bool hitSelf = true, float anticipation = 0f)
{
    private readonly FighterTuning _tuning = tuning ?? SwordStance;

    private const float TitleSeconds = 1.5f;
    private static readonly Vector4 White = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 Grey = new(0.75f, 0.78f, 0.85f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.85f, 0.3f, 1f);
    private static readonly Vector4 Ghost = new(0.6f, 0.85f, 1f, 1f);

    /// <summary>動き始める前に、最初の構えを取って向きを合わせる時間 [s]。</summary>
    private const float LeadIn = 0.8f;

    /// <summary>剣の構え（<see cref="Motion.SwordStance.Tuning"/>）。</summary>
    internal static FighterTuning SwordStance => Motion.SwordStance.Tuning;

    /// <summary>撮る 1 本: 題名・武器・動き・再生の速さ。元のモーションキャプチャ（棒人間に描く）とその始まりの時刻があれば添える。</summary>
    internal sealed record Shot(string Title, WeaponKind Weapon, SwordMotion Motion, float Rate, BvhClip? Source = null, float SourceFrom = 0f);

    /// <summary>なぞった結果: 剣の先の目標からのずれ（体フレーム、腕の長さ = 1）の平均と最大。</summary>
    internal readonly record struct Tracking(float MeanTipError, float MaxTipError, float MeanHandError);

    public void Make(IReadOnlyList<Shot> shots, string outPath, TextWriter log)
    {
        using var renderer = new HeadlessRenderer((uint)width, (uint)height);
        using var ffmpeg = Process.Start(new ProcessStartInfo("ffmpeg",
            $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {width}x{height} -r {fps} -i - -c:v libx264 -pix_fmt yuv420p -crf 18 -preset medium \"{outPath}\"")
        {
            RedirectStandardInput = true,
            UseShellExecute = false,
        }) ?? throw new IOException("ffmpeg を起動できません（PATH にありますか）。");
        foreach (var shot in shots)
        {
            var t = Run(shot, (game, frameText, lines, dim) => Emit(renderer, ffmpeg, game, frameText, lines, dim));
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{shot.Title}: tip error mean {t.MeanTipError:0.00} max {t.MaxTipError:0.00} hand error {t.MeanHandError:0.00} (arm lengths)"));
        }

        ffmpeg.StandardInput.Close();
        ffmpeg.WaitForExit();
        if (ffmpeg.ExitCode != 0)
        {
            throw new IOException($"ffmpeg failed with exit code {ffmpeg.ExitCode}.");
        }
    }

    /// <summary>1 本をなぞらせ、<paramref name="emit"/> へ絵を出す（null なら測るだけ）。</summary>
    internal Tracking Run(Shot shot, Action<BoxingGame, IReadOnlyList<ScreenTextDraw>, IReadOnlyList<DebugLine>, float>? emit, TextWriter? trace = null)
    {
        var nextTrace = 0f;
        // 相手に背を向けて稽古する（動きの踏み込みで相手にぶつからないように）。
        MotionImitation? imitation = null;
        using var game = new BoxingGame(_tuning, seed: 1, corners: new BoxingCorners("imitate", _ => new StandGuard(),
            _ => imitation = new MotionImitation(shot.Motion, shot.Rate, faceAway: true, leadIn: LeadIn, anticipation: anticipation), PlayerWeapon: shot.Weapon, PlayerTwoHanded: true));
        var control = imitation!;
        game.WeaponsHitHolder = hitSelf;
        var sword = game.WeaponOf(game.Player)!;
        var tipLocal = Weapon.TipOf(sword.Kind);
        var (top, _) = Weapon.TwoHandGripsOf(sword.Kind);
        var swordLength = Vector3.Distance(tipLocal, top);
        // 元の人の棒人間: 始まりの腰の位置と向きを、動き始めたときのラグドールの肩の真下・向きに合わせ、腕の長さの比で縮めて横に置く。
        var reach = game.Player.ArmReach;
        var lines = new List<DebugLine>();
        var dt = 1f / 240f;
        var length = LeadIn + (shot.Motion.Duration / shot.Rate) + 0.6f;
        BodyFrame? anchor = null;
        var nextFrame = 0f;
        var simPerFrame = 1f / (fps * slow);
        float sum = 0f, max = 0f, handSum = 0f;
        var count = 0;
        if (emit is not null)
        {
            for (var k = 0f; k < TitleSeconds; k += 1f / fps)
            {
                emit(game, [
                    new("剣の基本（動きをなぞる）", new Vector2(width * 0.5f, height * 0.3f), 34f, Grey, TextAnchor.Center),
                    new(shot.Title, new Vector2(width * 0.5f, height * 0.46f), 52f, White, TextAnchor.Center),
                    new(string.Create(CultureInfo.InvariantCulture, $"{WeaponName(shot.Weapon)}・両手持ち・{shot.Motion.Duration:0.0} 秒の動きを {shot.Rate:0.00} 倍の速さで"), new Vector2(width * 0.5f, height * 0.6f), 28f, Gold, TextAnchor.Center),
                ], [], 0.55f);
            }
        }

        for (var t = 0f; t < length; t += dt)
        {
            game.Step(default, dt);
            var frame = game.Player.Frame;
            var target = control.Current;
            var s = game.World.GetState(sword.Body.BodyId);
            var tipWorld = s.Position + Vector3.Transform(tipLocal, s.Orientation);
            var targetHand = frame.LocalToWorld(target.Right * reach);
            var targetTip = targetHand + (frame.Rotate(target.Blade) * swordLength);
            if (control.Started && !control.Finished)
            {
                anchor ??= frame;
                var error = Vector3.Distance(tipWorld, targetTip) / reach;
                sum += error;
                max = MathF.Max(max, error);
                handSum += Vector3.Distance(game.Player.GloveCenter(Hand.Right), targetHand) / reach;
                count++;
            }

            if (trace is not null && control.Started && t >= nextTrace)
            {
                nextTrace = t + 0.04f;
                var actualBlade = frame.InverseRotate(Vector3.Normalize(tipWorld - (s.Position + Vector3.Transform(top, s.Orientation))));
                var leftTarget = game.Player.HandTargetWorld(Hand.Left);
                trace.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"t {control.MotionTime:0.00} target blade ({target.Blade.X:0.00},{target.Blade.Y:0.00},{target.Blade.Z:0.00}) actual ({actualBlade.X:0.00},{actualBlade.Y:0.00},{actualBlade.Z:0.00}) R err {Vector3.Distance(game.Player.GloveCenter(Hand.Right), targetHand) / reach:0.00} L err {Vector3.Distance(game.Player.GloveCenter(Hand.Left), leftTarget) / reach:0.00} L target ({frame.WorldToLocal(leftTarget).X / reach:0.00},{frame.WorldToLocal(leftTarget).Y / reach:0.00},{frame.WorldToLocal(leftTarget).Z / reach:0.00}) support {game.Player.SupportOf(Hand.Right)}"));
            }

            if (emit is null || t < nextFrame)
            {
                continue;
            }

            nextFrame = t + simPerFrame;
            lines.Clear();
            lines.Add(new DebugLine(targetHand, targetTip, Gold));
            if (shot.Source is { } source)
            {
                AddStickFigure(lines, source, shot.SourceFrom + control.MotionTime, shot.SourceFrom, anchor ?? frame, reach);
            }

            var tipError = Vector3.Distance(tipWorld, targetTip) / reach;
            emit(game, [
                new(shot.Title, new Vector2(40f, 40f), 34f, White, TextAnchor.Left),
                new(string.Create(CultureInfo.InvariantCulture, $"剣先の目標からのずれ {tipError:0.00}（腕の長さ = 1）  金の線 = 剣の目標"), new Vector2(40f, 84f), 24f, Gold, TextAnchor.Left),
                new(shot.Source is null ? "型（キーから作った動き）" : "水色 = 元のモーションキャプチャ（同じ大きさに縮めた）", new Vector2(40f, 116f), 24f, Ghost, TextAnchor.Left),
                new(string.Create(CultureInfo.InvariantCulture, $"{t:0.00} s  （{slow:0.#} 倍スロー）"), new Vector2(width - 40f, height - 40f), 24f, Grey, TextAnchor.Right),
            ], lines, 0f);
        }

        return count == 0 ? default : new Tracking(sum / count, max, handSum / count);
    }

    private static string WeaponName(WeaponKind kind) => kind switch
    {
        WeaponKind.Katana => "日本刀",
        WeaponKind.Longsword => "ロングソード",
        _ => kind.ToString(),
    };

    /// <summary>元の人の棒人間を、ラグドールの始まりの位置の横（右へ肩幅 3 つぶん）に、腕の長さの比で縮めて描く。</summary>
    private static void AddStickFigure(List<DebugLine> lines, BvhClip clip, float time, float from, BodyFrame start, float reach)
    {
        var pose = clip.Pose((int)(time / clip.FrameTime));
        var first = clip.Pose((int)(from / clip.FrameTime));
        var hips = clip.IndexOf("Hips");
        var (la, lf, lh) = (clip.IndexOf("LeftArm"), clip.IndexOf("LeftForeArm"), clip.IndexOf("LeftHand"));
        var arm = Vector3.Distance(first[la].Position, first[lf].Position) + Vector3.Distance(first[lf].Position, first[lh].Position);
        var scale = reach / arm;
        var f = Vector3.Transform(Vector3.UnitZ, first[hips].Rotation);
        var yaw0 = MathF.Atan2(f.X, f.Z);
        var anchor = first[hips].Position with { Y = 0f };
        var side = start.Forward * 0.5f; // 横から撮るので、前に置くと隣に並んで見える
        var ground = start.Origin with { Y = BoxingGame.RingSurfaceHeight };
        Vector3 Place(Vector3 p)
        {
            var local = p - anchor;
            var (sn, cs) = MathF.SinCos(-yaw0);
            local = new Vector3((cs * local.X) + (sn * local.Z), local.Y, (-sn * local.X) + (cs * local.Z));
            return ground + side + start.Rotate(local * scale);
        }

        for (var j = 0; j < pose.Length; j++)
        {
            if (clip.ParentOf(j) is var parent and >= 0)
            {
                lines.Add(new DebugLine(Place(pose[parent].Position), Place(pose[j].Position), Ghost));
            }
        }

        var r = Place(Vector3.Lerp(pose[clip.IndexOf("RightHand")].Position, pose[clip.IndexOf("R_Wrist_End")].Position, MotionRetarget.PalmShare));
        var l = Place(Vector3.Lerp(pose[lh].Position, pose[clip.IndexOf("L_Wrist_End")].Position, MotionRetarget.PalmShare));
        lines.Add(new DebugLine(l, r + (Vector3.Normalize(r - l + new Vector3(1e-6f)) * (MotionRetarget.BladeLength / MotionRetarget.Inch * scale)), Ghost));
    }

    private void Emit(HeadlessRenderer renderer, Process ffmpeg, BoxingGame game, IReadOnlyList<ScreenTextDraw> text, IReadOnlyList<DebugLine> extra, float dim)
    {
        var instances = new List<PrimitiveInstance>();
        var lines = new List<DebugLine>();
        BoxingRenderer.Fill(game, showTargetGhosts: false, instances, lines);
        lines.AddRange(extra);
        var frame = game.Player.Frame;
        var center = game.Player.ChestPosition + (frame.Right * 0.15f) + new Vector3(0f, -0.05f, 0f);
        // 横から: 体の右（−X）から、少し前寄り。
        var eye = center + (frame.Right * 0.95f) + (frame.Forward * 0.25f) + new Vector3(0f, 0.12f, 0f);
        var vp = Camera.ViewProjection(eye, center, Vector3.UnitY, 45f * MathF.PI / 180f, (float)width / height, 0.02f, 30f);
        UiShapeDraw[] shapes = dim > 0f ? [new UiShapeDraw(new Vector2(width * 0.5f, height * 0.5f), new Vector2(width, height), new Vector4(0f, 0f, 0f, dim))] : [];
        var rgba = renderer.RenderPrimitiveScene(ArenaLook.Background, vp, instances, lines, text, particles: game.BuildParticles(), lighting: game.BuildLighting(),
            shadow: game.BuildShadow(), cameraPosition: eye, shapes: shapes, postProcess: game.BuildPostProcess(), voxels: game.BuildVoxelVolumes());
        ffmpeg.StandardInput.BaseStream.Write(rgba);
    }
}
