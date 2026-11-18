using System.Globalization;
using System.Numerics;
using System.Text;
using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// 首・肩・肘・腰など、関節の隙間の調査と、首を薙ぐドリル（docs/VOXEL_DAMAGE.md「関節のつなぎ」）。
/// <c>geom</c>: つながった部位のボクセルどうしの距離（隙間の幅）を測る。
/// <c>cut</c>: 日本刀の水平の薙ぎを、手の高さを変えて構えて立つ相手の首・肩の高さへ出し、どの部位が何セル削れたか・首が断たれたか・頭が落ちたかを数える。
/// </summary>
internal static class NeckLab
{
    public static string Geometry()
    {
        using var game = new BoxingGame(SwordStance.Tuning);
        var f = game.Cpu;
        var p = f.Parts;
        var body = f.Voxels!;
        VoxelPart Part(int id) => body.Parts.Single(x => x.Body.BodyId == id);
        var pairs = new (string, int, int)[]
        {
            ("neck (chest-head)", p.Chest, p.Head), ("spine (pelvis-chest)", p.Pelvis, p.Chest),
            ("shoulder L", p.Chest, p.LeftUpperArm), ("shoulder R", p.Chest, p.RightUpperArm),
            ("elbow L", p.LeftUpperArm, p.LeftForearm), ("elbow R", p.RightUpperArm, p.RightForearm),
            ("hip L", p.Pelvis, p.LeftThigh), ("hip R", p.Pelvis, p.RightThigh),
            ("knee L", p.LeftThigh, p.LeftShin), ("knee R", p.RightThigh, p.RightShin),
        };
        var text = new StringBuilder();
        text.AppendLine("| joint | cell size [mm] | nearest filled-cell distance [mm] | in cells | A cells within 1.5 cells of B |");
        text.AppendLine("|---|---:|---:|---:|---:|");
        foreach (var (name, a, b) in pairs)
        {
            var (gap, near, cell) = Gap(Part(a).Body, Part(b).Body);
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {name} | {cell * 1000f:0.0} | {gap * 1000f:0.0} | {gap / cell:0.0} | {near} |"));
        }

        var head = Part(p.Head).Body.Grid;
        var chest = Part(p.Chest).Body.Grid;
        text.AppendLine();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"head grid {head.Dimensions} half {head.HalfExtents}, chest grid {chest.Dimensions} half {chest.HalfExtents}"));
        var headBody = Part(p.Head).Body;
        var chestBody = Part(p.Chest).Body;
        text.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"head box world Y {headBody.GridToWorld().Translation.Y - head.HalfExtents.Y:0.0000} .. {headBody.GridToWorld().Translation.Y + head.HalfExtents.Y:0.0000}; chest box world Y {chestBody.GridToWorld().Translation.Y - chest.HalfExtents.Y:0.0000} .. {chestBody.GridToWorld().Translation.Y + chest.HalfExtents.Y:0.0000}"));
        text.AppendLine();
        text.AppendLine("head slices (y from the bottom), x across, z down; chars = material index (. empty)");
        text.AppendLine("```");
        for (var y = 0; y < 6; y++)
        {
            text.AppendLine("head y=" + y);
            AppendSlice(text, head, y);
        }

        for (var y = chest.Dimensions.Y - 6; y < chest.Dimensions.Y; y++)
        {
            text.AppendLine("chest y=" + y);
            AppendSlice(text, chest, y);
        }

        text.AppendLine("```");
        return text.ToString();
    }

    private static void AppendSlice(StringBuilder text, Aigis.Foundation.Voxels.VoxelGrid grid, int y)
    {
        for (var z = 0; z < grid.Dimensions.Z; z++)
        {
            var line = new StringBuilder();
            for (var x = 0; x < grid.Dimensions.X; x++)
            {
                var m = grid.At(x, y, z);
                line.Append(m == 0 ? '.' : (char)('0' + (m % 10)));
            }

            text.AppendLine(line.ToString());
        }
    }

    private static (float Gap, int Near, float Cell) Gap(Aigis.Physics.Voxels.VoxelBody a, Aigis.Physics.Voxels.VoxelBody b)
    {
        var ga = a.Grid;
        var gb = b.Grid;
        var cell = MathF.Min(ga.CellSize.X, gb.CellSize.X);
        const int R = 6;
        var toWorld = a.GridToWorld();
        var best = float.MaxValue;
        var near = 0;
        for (var i = 0; i < ga.CellCount; i++)
        {
            if (ga.MaterialAt(i) == 0)
            {
                continue;
            }

            var (x, y, z) = ga.Coordinates(i);
            var local = b.WorldToGrid(Vector3.Transform(ga.CellCenter(x, y, z), toWorld));
            var mine = float.MaxValue;
            for (var dx = -R; dx <= R; dx++)
            {
                for (var dy = -R; dy <= R; dy++)
                {
                    for (var dz = -R; dz <= R; dz++)
                    {
                        var off = new Vector3(dx * gb.CellSize.X, dy * gb.CellSize.Y, dz * gb.CellSize.Z);
                        if (gb.FilledCellAt(local + off) >= 0)
                        {
                            mine = MathF.Min(mine, off.Length());
                        }
                    }
                }
            }

            best = MathF.Min(best, mine);
            near += mine <= 1.5f * cell ? 1 : 0;
        }

        return (best, near, cell);
    }

    /// <summary>首・肩の高さの薙ぎ: 手の高さ × 側 × 間合いのずれで、1 回ずつ。</summary>
    public static string Cut(IReadOnlyList<float> heights, IReadOnlyList<float> offsets, float armReachScale = 1f)
    {
        var tuning = SwordStance.Tuning;
        var guard = FormSwordsman.Guard(WeaponKind.Katana);
        float armReach;
        using (var probe = new BoxingGame(tuning))
        {
            armReach = probe.Player.ArmReach * armReachScale;
        }

        var rows = new List<(float Height, float Side, float Offset, Row Row)>();
        var jobs = new List<(float, float, float)>();
        foreach (var h in heights)
        {
            foreach (var side in new[] { -1f, 1f })
            {
                foreach (var o in offsets)
                {
                    jobs.Add((h, side, o));
                }
            }
        }

        var results = new Row[jobs.Count];
        Parallel.For(0, jobs.Count, i =>
        {
            var (h, side, o) = jobs[i];
            results[i] = One(h, side, o, tuning, guard, armReach);
        });
        var text = new StringBuilder();
        text.AppendLine("| hand height | side | range offset | head cells | chest cells | neck broken | head off | dead (cause) | first part | other parts (cells) | neck-slice cells (start/end) |");
        text.AppendLine("|---:|---:|---:|---:|---:|---|---|---|---|---|---|");
        for (var i = 0; i < jobs.Count; i++)
        {
            var (h, side, o) = jobs[i];
            var r = results[i];
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {h:0.00} | {side:+0;-0} | {o * 100f:+0;-0;0} cm | {r.Head} | {r.Chest} | {(r.NeckBroken ? "yes" : "")} | {(r.HeadOff ? "yes" : "")} | {r.Cause ?? ""} | {r.First?.ToString() ?? "-"} | {r.Others} | {r.NeckStart}/{r.NeckEnd} |"));
        }

        return text.ToString();
    }

    /// <summary>
    /// 首を薙ぐ 1 振りの動画（斜め前から首へ寄る。<paramref name="slow"/> 倍のスロー）。音なし。
    /// </summary>
    public static void Video(float height, float side, float offset, float slow, string outPath)
    {
        const int Width = 960, Height = 540, Fps = 30;
        var tuning = SwordStance.Tuning;
        var guard = FormSwordsman.Guard(WeaponKind.Katana);
        float armReach;
        using (var probe = new BoxingGame(tuning))
        {
            armReach = probe.Player.ArmReach;
        }

        var motion = SwordForms.KatanaHorizontalAt($"nagi-h{height:0.00}", side, height);
        var technique = new MotionTechnique(motion.Name, WeaponKind.Katana, motion, 0.5f);
        technique = technique with { Range = StrikeDrill.ReachOf(technique, armReach) + offset };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        using var renderer = new Aigis.Render.HeadlessRenderer(Width, Height);
        using var ffmpeg = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ffmpeg",
            $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {Width}x{Height} -r {Fps} -i - -c:v libx264 -pix_fmt yuv420p -crf 23 -preset fast \"{outPath}\"")
        {
            RedirectStandardInput = true,
            UseShellExecute = false,
        }) ?? throw new IOException("ffmpeg を起動できません（PATH にありますか）。");
        var next = 0f;
        var perFrame = 1f / (Fps * slow);
        var started = float.MaxValue;
        StrikeDrill.Run(technique, guard, SwordStance.Rate, tuning, (game, t, striking) =>
        {
            if (striking && started == float.MaxValue)
            {
                started = t;
            }

            if (t < started - 0.1f || t < next)
            {
                return;
            }

            next = t + perFrame;
            var instances = new List<Aigis.Render.PrimitiveInstance>();
            var lines = new List<Aigis.Render.DebugLine>();
            BoxingRenderer.Fill(game, showTargetGhosts: false, instances, lines);
            // 的（青）の首へ寄る。攻める赤の右（赤から見て左前）の斜め前から。
            var victim = game.Cpu;
            var center = victim.HeadPosition + new Vector3(0f, -0.03f, 0f);
            var toward = game.Player.ChestPosition - victim.ChestPosition;
            toward.Y = 0f;
            toward = toward.LengthSquared() > 1e-6f ? Vector3.Normalize(toward) : Vector3.UnitZ;
            var across = Vector3.Cross(Vector3.UnitY, toward);
            var eye = center + (toward * 0.12f) + (across * 0.38f * -side) + new Vector3(0f, 0.04f, 0f);
            var vp = Aigis.Render.Camera.ViewProjection(eye, center, Vector3.UnitY, 40f * MathF.PI / 180f, (float)Width / Height, 0.02f, 30f);
            var rgba = renderer.RenderPrimitiveScene(ArenaLook.Background, vp, instances, lines, [], particles: game.BuildParticles(), lighting: game.BuildLighting(),
                shadow: game.BuildShadow(), cameraPosition: eye, shapes: [], postProcess: game.BuildPostProcess(), voxels: game.BuildVoxelVolumes());
            ffmpeg.StandardInput.BaseStream.Write(rgba);
        }, target: ArmsDown ? () => new StandGuard(new GuardPosture("down", Vector3.Zero, new Vector3(0.45f, -0.75f, 0.05f))) : null);
        ffmpeg.StandardInput.Close();
        ffmpeg.WaitForExit();
    }

    private sealed record Row(int Head, int Chest, bool NeckBroken, bool HeadOff, string? Cause, BodyPartKind? First, string Others, int NeckStart, int NeckEnd);

    /// <summary>的の両腕を体の脇へ下ろすか（<c>--arms down</c>。既定は顔の前に上げた構え）。</summary>
    public static bool ArmsDown { get; set; }

    /// <summary>頭の格子の下の 5 段（首の段。<see cref="BoxerSpine.NeckSlices"/> を含む）に残っているセルの数。</summary>
    private static int NeckCells(BoxerBody body)
    {
        var grid = body.Parts.Single(p => p.Kind == BodyPartKind.Head).Body.Grid;
        var n = 0;
        for (var y = 0; y < 5; y++)
        {
            for (var x = 0; x < grid.Dimensions.X; x++)
            {
                for (var z = 0; z < grid.Dimensions.Z; z++)
                {
                    n += grid.At(x, y, z) != 0 ? 1 : 0;
                }
            }
        }

        return n;
    }

    private static Row One(float height, float side, float offset, FighterTuning tuning, MotionFrame guard, float armReach)
    {
        var motion = SwordForms.KatanaHorizontalAt($"nagi-h{height:0.00}", side, height);
        var technique = new MotionTechnique(motion.Name, WeaponKind.Katana, motion, 0.5f);
        var reach = StrikeDrill.ReachOf(technique, armReach);
        technique = technique with { Range = reach + offset };
        var neckBroken = false;
        var headOff = false;
        string? cause = null;
        int neckStart = -1, neckNow = 0;
        // 的は両腕を体の脇に下ろして立つ（腕が首の前を覆わない）。
        var result = StrikeDrill.Run(technique, guard, SwordStance.Rate, tuning, (game, _, _) =>
        {
            neckBroken |= game.Cpu.Voxels!.NeckBroken;
            cause = game.Cpu.DeathCause;
            headOff |= game.Cpu.Voxels!.NeckBroken && game.Cpu.IsDead && game.Cpu.DeathCause == "neck";
            neckNow = NeckCells(game.Cpu.Voxels!);
            if (neckStart < 0)
            {
                neckStart = neckNow;
            }
        }, target: ArmsDown ? () => new StandGuard(new GuardPosture("down", Vector3.Zero, new Vector3(0.45f, -0.75f, 0.05f))) : null);
        var by = result.CellsByPart;
        var others = string.Join(" ", by.Where(kv => kv.Key is not (BodyPartKind.Head or BodyPartKind.Chest)).Select(kv => $"{kv.Key}:{kv.Value}"));
        return new Row(by.GetValueOrDefault(BodyPartKind.Head), by.GetValueOrDefault(BodyPartKind.Chest), neckBroken, headOff, cause, result.FirstPart, others, neckStart, neckNow);
    }
}
