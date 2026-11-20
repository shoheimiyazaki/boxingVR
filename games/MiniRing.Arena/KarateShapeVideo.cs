using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Aigis.Foundation;
using Aigis.Render;
using MiniRing.Audio;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>測定と同じ台本を再実行し、狙いに最も近い命中の前後を撮る（4 倍スロー）。</summary>
internal static class KarateShapeVideo
{
    /// <summary>手形ごとの手だけを、関節の曲げ角どおりに並べた姿で撮る（横から・上から・斜め上から）。</summary>
    internal static void HandSheet(string directory, Action<string> log)
    {
        Directory.CreateDirectory(directory);
        using var renderer = new HeadlessRenderer(480, 360);
        var views = new (string Name, Vector3 Eye, Vector3 Up)[]
        {
            ("side", new Vector3(0f, 0f, 0.075f), Vector3.UnitY),
            ("top", new Vector3(0f, 0.075f, 0f), -Vector3.UnitZ),
            ("oblique", new Vector3(0.025f, 0.045f, 0.055f), Vector3.UnitY),
        };
        foreach (var shape in Enum.GetValues<HandShape>())
        {
            using var world = new Aigis.Physics.PhysicsWorld();
            var hand = Weapon.CreateWorn(world, WeaponKind.Fist, BoxerAnatomy.RedGlove, new Vector3(0f, 1f, 0f), Quaternion.Identity);
            hand.SetHandShape(shape);
            var center = new Vector3(0f, 1f, 0f) + new Vector3(0.01f, 0.005f, 0f);
            foreach (var (name, offset, up) in views)
            {
                var eye = center + offset;
                var vp = Aigis.Render.Camera.ViewProjection(eye, center, up, 40f * MathF.PI / 180f, 480f / 360f, 0.005f, 5f);
                var rgba = renderer.RenderPrimitiveScene(ArenaLook.Background, vp, [], [],
                    [new($"{shape} {name}", new Vector2(10f, 20f), 18f, Vector4.Zero, TextAnchor.Left)],
                    voxels: [hand.Draw()]);
                PngWriter.WriteRgba(Path.Combine(directory, $"hand_{shape}_{name}.png"), 480, 360, rgba);
            }

            log($"hand_{shape}: {hand.Body.Grid.FilledCount} cells");
        }
    }

    internal static void Make(string directory, float seconds, int seed, Action<string> log)
    {
        Directory.CreateDirectory(directory);
        using var renderer = new HeadlessRenderer(960, 720);
        foreach (var (shape, goal) in new[] { (HandShape.SpearHand, AimGoal.Eye), (HandShape.OneKnuckle, AimGoal.SolarPlexus), (HandShape.Seiken, AimGoal.SolarPlexus) })
        {
            var lab = new KarateLab(1, seconds, seed, log);
            var time = -1f;
            var distance = float.MaxValue;
            HitEvent? chosen = null;
            var contacts = new List<object>();
            var eyeDistance = float.MaxValue;
            var probe = lab.ShapeBout(shape, goal, seed, (game, t, _) =>
            {
                foreach (var contact in game.RecentCuts)
                {
                    var part = game.Player.Voxels!.PartOf(contact.B.BodyId);
                    if (part is null || part.Kind is not (BodyPartKind.Head or BodyPartKind.Chest or BodyPartKind.Pelvis))
                    {
                        continue;
                    }

                    var aim = game.Player.AsTarget.Point(goal, game.Cpu.ShoulderCenter)!.Value;
                    contacts.Add(new { t, part.Kind, contact.Pressure, contact.Force, contact.PassedForce, contact.ApproachSpeed,
                        contact.FaceA, contact.FaceB, Removed = contact.RemovedFor(part.Body).Count, Crushed = contact.CrushedFor(part.Body).Count,
                        AimDistance = Vector3.Distance(aim, contact.Point) });
                }

                foreach (var hit in game.RecentHits.Where(h => h.Attacker == game.Cpu))
                {
                    var aim = game.Player.AsTarget.Point(goal, game.Cpu.ShoulderCenter)!.Value;
                    var d = Vector3.Distance(aim, hit.Point);
                    if (d < distance)
                    {
                        (time, distance, chosen) = (t, d, hit);
                        var head = (game.Player.Voxels!.Face ?? game.Player.Voxels.PartOf(game.Player.Parts.Head))!.Body; // 目は顔の層
                        var grid = head.Grid;
                        eyeDistance = Enumerable.Range(0, grid.CellCount)
                            .Where(i => grid.MaterialAt(i) is BoxerAnatomy.Eye or BoxerAnatomy.Pupil)
                            .Select(i =>
                            {
                                var (x, y, z) = grid.Coordinates(i);
                                return Vector3.Distance(hit.Point, Vector3.Transform(grid.CellCenter(x, y, z), head.GridToWorld()));
                            }).DefaultIfEmpty(float.MaxValue).Min();
                    }
                }
            });
            if (time < 0f)
            {
                throw new InvalidOperationException($"{shape}/{goal}: {seconds}s に命中がありません。");
            }

            var folder = Path.Combine(directory, $"{shape}_{goal}");
            Directory.CreateDirectory(folder);
            const float slow = 4f;
            const int fps = 30;
            var start = MathF.Max(0f, time - 0.25f);
            var end = MathF.Min(probe.Elapsed, time + 0.35f);
            var frame = 0;
            var next = start;
            var audio = new float[(int)((end - start + 1f) * slow * HitSound.SampleRate)];
            var impact = new ImpactAudio();
            var instances = new List<PrimitiveInstance>();
            var lines = new List<DebugLine>();
            var frameTimes = new List<float>();
            var result = lab.ShapeBout(shape, goal, seed, (game, t, _) =>
            {
                foreach (var voice in impact.Push(t, ImpactAudio.Gather(game.RecentHits, game.RecentContacts, game.RecentShatters)))
                {
                    if (voice.OccurredAt >= start && voice.OccurredAt <= end)
                    {
                        VideoMaker.Mix(audio, voice.Pcm, ((float)voice.OccurredAt - start) * slow, 1f);
                    }
                }

                if (t < next || t > end)
                {
                    return;
                }

                BoxingRenderer.Fill(game, showTargetGhosts: false, instances, lines);
                // 打つ手の横（打つ向きに垂直）に据えたカメラ。手と狙いの点の真ん中を見る（手の形と、狙いへの当たり方が見える）。狙いの点に小さな十字を描く。
                var aimNow = game.Player.AsTarget.Point(goal, game.Cpu.ShoulderCenter)!.Value;
                var handAt = game.Cpu.GloveCenter(chosen!.Value.Hand);
                var target = (handAt + aimNow) * 0.5f;
                var axis = Vector3.Normalize((game.Cpu.ChestPosition - game.Player.ChestPosition) with { Y = 0f });
                var side = Vector3.Normalize(Vector3.Cross(axis, Vector3.UnitY));
                var eye = target + (side * 0.11f) + new Vector3(0f, 0.035f, 0f);
                var arm = 0.006f;
                lines.Add(new DebugLine(aimNow - (Vector3.UnitX * arm), aimNow + (Vector3.UnitX * arm), new Vector4(1f, 1f, 0f, 1f)));
                lines.Add(new DebugLine(aimNow - (Vector3.UnitY * arm), aimNow + (Vector3.UnitY * arm), new Vector4(1f, 1f, 0f, 1f)));
                lines.Add(new DebugLine(aimNow - (Vector3.UnitZ * arm), aimNow + (Vector3.UnitZ * arm), new Vector4(1f, 1f, 0f, 1f)));
                var (vp, displayEye) = DisplaySpace.Standard.Camera(eye, target, Vector3.UnitY, 40f * MathF.PI / 180f, 960f / 720f, 0.005f, 30f);
                ScreenTextDraw[] text = [new($"{shape} -> {goal}   4x slow", new Vector2(20f, 25f), 24f, Vector4.One, TextAnchor.Left),
                    new(string.Create(CultureInfo.InvariantCulture, $"t={t:0.000}s  hit={time:0.000}s  nearest={distance * 1000f:0.0}mm ({chosen!.Value.Zone})"), new Vector2(20f, 62f), 20f, Vector4.One, TextAnchor.Left)];
                var rgba = renderer.RenderPrimitiveScene(ArenaLook.Background, vp, instances, lines, text,
                    particles: game.BuildParticles(), lighting: game.BuildLighting(), shadow: game.BuildShadow(),
                    cameraPosition: displayEye, postProcess: game.BuildPostProcess(), voxels: game.BuildVoxelVolumes());
                PngWriter.WriteRgba(Path.Combine(folder, $"frame_{frame:0000}.png"), 960, 720, rgba);
                frameTimes.Add(t);
                frame++;
                next = start + frame / (fps * slow);
            });
            if (result.Hits != probe.Hits || result.End.Total != probe.End.Total)
            {
                throw new InvalidOperationException("撮影時の再実行が測定と一致しません。");
            }

            VideoMaker.WriteWav(Path.Combine(folder, "audio.wav"), audio, (float)frame / fps);
            File.WriteAllText(Path.Combine(folder, "frames.json"), System.Text.Json.JsonSerializer.Serialize(new { shape, goal, seed, time, distance, eyeDistance, Zone = chosen!.Value.Zone, frameTimes, result, contacts }));
            var info = new ProcessStartInfo("ffmpeg") { UseShellExecute = false };
            foreach (var arg in new[] { "-y", "-loglevel", "error", "-framerate", "30", "-i", Path.Combine(folder, "frame_%04d.png"), "-i", Path.Combine(folder, "audio.wav"), "-frames:v", frame.ToString(CultureInfo.InvariantCulture), "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "20", "-c:a", "aac", "-shortest", folder + ".mp4" })
            {
                info.ArgumentList.Add(arg);
            }

            using var ffmpeg = Process.Start(info) ?? throw new IOException("ffmpeg を起動できません。");
            ffmpeg.WaitForExit();
            if (ffmpeg.ExitCode != 0)
            {
                throw new IOException("ffmpeg が失敗しました。");
            }

            log(string.Create(CultureInfo.InvariantCulture, $"{folder}.mp4: 命中 {time:0.000}s、狙いとの差 {distance * 1000f:0.0} mm、{chosen.Value.Zone}、{frame} frames"));
        }
    }
}
