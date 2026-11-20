using System.Globalization;
using System.Numerics;
using System.Text;
using Aigis.Physics;
using MiniRing.Motion;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// どの関節でも、隙間を刃が通り抜けるかを測る（<see cref="NeckLab"/> の首の薙ぎを、関節を選べるようにしたもの。docs/VOXEL_DAMAGE.md「関節のつなぎ」）。
/// 静止した的の関節（親の剛体の座標の中心）の少し上下の高さを、日本刀が水平に通る。子の部位を関節のまわりに回して隙間を開けた姿勢でも測る
/// （首の <c>--tilt</c> と同じ）。<c>--bridges off</c> で関節のつなぎ（<see cref="JointBridge"/>）を切って、つなぎの効きを比べる。
/// </summary>
internal static class JointLab
{
    /// <summary>NearLost = 関節の中心から Radius 以内で削れたセル（両側の部位の合計）、ParentLost・ChildLost = 部位ぜんぶで削れたセル。</summary>
    internal sealed record JointResult(int NearLost, int ParentLost, int ChildLost, bool Severed, bool Touched, int BridgeCells, int EdgeCells);

    /// <summary>関節の「そば」の半径 [m]（つなぎが壊す親・子の近いセルは 1 セル = 14 mm 以内）。</summary>
    internal const float Radius = 0.0563f;

    /// <summary>名前（<c>Neck</c>・<c>LeftElbow</c>…）から関節を引く。</summary>
    internal static HumanoidRagdoll.RagdollJoint Parse(string name) =>
        Enum.TryParse<HumanoidRagdoll.RagdollJoint>(name, ignoreCase: true, out var joint) ? joint : throw new ArgumentException($"知らない関節: {name}（{string.Join(", ", Enum.GetNames<HumanoidRagdoll.RagdollJoint>())}）");

    /// <summary>薙ぎの前の場面: 的を立たせ、子の部位を回して隙間を開け、刃を関節の高さへ置いたところ。</summary>
    internal sealed record Stage(BoxingGame Game, Vector3 Center, int ParentId, int ChildId, Vector3 Heading, Weapon Weapon, int BridgeCells, int EdgeCells);

    internal static Stage Prepare(HumanoidRagdoll.RagdollJoint joint, string dir, float offset, bool bridges, float bendDegrees, string axis)
    {
        var game = new BoxingGame(SwordStance.Tuning, 1, new BoxingCorners("stand", _ => new StandGuard(Down), _ => new StandGuard(Down)));
        for (var i = 0; i < 151; i++)
        {
            game.Step(default, 1f / 60f);
        }

        game.JointBridges = bridges;
        var victim = game.Cpu;
        var (_, parentId, childId, anchor) = victim.BridgeJoints().Single(j => j.Joint == joint);
        var parentState = game.World.GetState(parentId);
        var center = parentState.Position + Vector3.Transform(anchor, parentState.Orientation);
        var frame = victim.Frame;
        if (bendDegrees != 0f)
        {
            var pivot = axis == "forward" ? frame.Forward : frame.Right;
            var q = Quaternion.CreateFromAxisAngle(pivot, bendDegrees * MathF.PI / 180f);
            var child = game.World.GetState(childId);
            game.World.SetDynamicPose(childId, center + Vector3.Transform(child.Position - center, q), Quaternion.Concatenate(child.Orientation, q));
            game.World.SetLinearVelocity(childId, Vector3.Zero);
            game.World.SetAngularVelocity(childId, Vector3.Zero);
        }

        var bridge = victim.Voxels!.Bridges.Single(b => b.Joint == joint);
        bridge.Refresh(_ => true);
        var (heading, across) = dir switch
        {
            "side" => (frame.Right, frame.Forward),
            "front" => (-frame.Forward, frame.Right),
            _ => (frame.Forward, frame.Right),
        };
        var orientation = WeaponDrills.Edge(heading, across);
        var kind = WeaponKind.Katana;
        var middle = (Weapon.TipOf(kind).X + Weapon.TwoHandGripsOf(kind).Bottom.X) * 0.5f;
        var target = center + new Vector3(0f, offset, 0f);
        var start = target - (heading * 0.423f) - Vector3.Transform(new Vector3(middle, 0f, 0f), orientation);
        var weapon = game.AddWeapon(kind, start, orientation);
        return new Stage(game, center, parentId, childId, heading, weapon, bridge.ActiveCells, bridge.EdgeCells);
    }

    /// <summary>
    /// 的の <paramref name="joint"/> の高さ +<paramref name="offset"/> を、刃が <paramref name="dir"/>（side / front / back）から速さ <paramref name="speed"/> で通る 1 振り。
    /// 子の部位を、関節のまわりに <paramref name="axis"/>（right / forward）のまわりへ <paramref name="bendDegrees"/> 回してから振る（0 なら立った姿勢のまま）。
    /// </summary>
    internal static JointResult SlashOnce(HumanoidRagdoll.RagdollJoint joint, string dir, float offset, float speed, bool bridges, float bendDegrees = 0f, string axis = "right")
    {
        var stage = Prepare(joint, dir, offset, bridges, bendDegrees, axis);
        using var game = stage.Game;
        var (center, parentId, childId, heading, weapon) = (stage.Center, stage.ParentId, stage.ChildId, stage.Heading, stage.Weapon);
        var (bridgeCells, edgeCells) = (stage.BridgeCells, stage.EdgeCells);
        var victim = game.Cpu;
        var parentGrid = victim.Voxels!.Parts.Single(p => p.Body.BodyId == parentId).Body.Grid;
        var childGrid = victim.Voxels!.Parts.Single(p => p.Body.BodyId == childId).Body.Grid;
        int parentStart = parentGrid.FilledCount, childStart = childGrid.FilledCount;
        var parentBody = victim.Voxels.Parts.Single(p => p.Body.BodyId == parentId).Body;
        var childBody = victim.Voxels.Parts.Single(p => p.Body.BodyId == childId).Body;
        var (parentPoint, childPoint) = (parentBody.WorldToGrid(center), childBody.WorldToGrid(center));
        var (parentFilled, childFilled) = (Filled(parentGrid), Filled(childGrid));
        var touched = false;
        var steps = (int)MathF.Ceiling(0.845f / speed * 240f) + 2;
        for (var i = 0; i < steps; i++)
        {
            game.World.SetLinearVelocity(weapon.Body.BodyId, heading * speed);
            game.World.SetAngularVelocity(weapon.Body.BodyId, Vector3.Zero);
            game.Step(default, 1f / 240f);
            touched |= game.RecentCuts.Any(c => c.Touched);
        }

        for (var i = 0; i < 201; i++)
        {
            game.Step(default, 1f / 240f); // 落ち着く
        }

        var near = LostNear(parentGrid, parentFilled, parentPoint) + LostNear(childGrid, childFilled, childPoint);
        return new JointResult(near, parentStart - parentGrid.FilledCount, childStart - childGrid.FilledCount, !victim.JointIntact(parentId, childId), touched, bridgeCells, edgeCells);
    }

    /// <summary>
    /// 1 振りの動画（関節へ寄る。<paramref name="slow"/> 倍のスロー、振り終わって 2.0 秒まで）。音なし。<see cref="NeckLab.SlashVideo"/> と同じ撮り方。
    /// </summary>
    public static void Video(HumanoidRagdoll.RagdollJoint joint, string dir, float offset, float speed, bool bridges, float bendDegrees, string axis, float slow, string outPath)
    {
        const int Width = 960, Height = 540, Fps = 30;
        var stage = Prepare(joint, dir, offset, bridges, bendDegrees, axis);
        using var game = stage.Game;
        var (heading, weapon, center) = (stage.Heading, stage.Weapon, stage.Center);
        var view = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, heading)) * 1.408f;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        using var renderer = new Aigis.Render.HeadlessRenderer(Width, Height);
        using var ffmpeg = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ffmpeg",
            $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {Width}x{Height} -r {Fps} -i - -c:v libx264 -pix_fmt yuv420p -crf 23 -preset fast \"{outPath}\"")
        {
            RedirectStandardInput = true,
            UseShellExecute = false,
        }) ?? throw new IOException("ffmpeg を起動できません（PATH にありますか）。");
        var steps = (int)MathF.Ceiling(0.845f / speed * 240f) + 2;
        var total = steps + (int)(2.014f * 240f);
        var next = 0f;
        var perFrame = 1f / (Fps * slow);
        for (var i = 0; i < total; i++)
        {
            if (i < steps)
            {
                game.World.SetLinearVelocity(weapon.Body.BodyId, heading * speed);
                game.World.SetAngularVelocity(weapon.Body.BodyId, Vector3.Zero);
            }

            game.Step(default, 1f / 240f);
            var t = i / 240f;
            if (t < next)
            {
                continue;
            }

            next = t + perFrame;
            var instances = new List<Aigis.Render.PrimitiveInstance>();
            var lines = new List<Aigis.Render.DebugLine>();
            BoxingRenderer.Fill(game, showTargetGhosts: false, instances, lines);
            var eye = center + view + new Vector3(0f, 0.225f, 0f);
            var (vp, displayEye) = DisplaySpace.Standard.Camera(eye, center, Vector3.UnitY, 38f * MathF.PI / 180f, (float)Width / Height, 0.02f, 30f);
            var rgba = renderer.RenderPrimitiveScene(ArenaLook.Background, vp, instances, lines, [], particles: game.BuildParticles(), lighting: game.BuildLighting(),
                shadow: game.BuildShadow(), cameraPosition: displayEye, shapes: [], postProcess: game.BuildPostProcess(), voxels: game.BuildVoxelVolumes());
            ffmpeg.StandardInput.BaseStream.Write(rgba);
        }

        ffmpeg.StandardInput.Close();
        ffmpeg.WaitForExit();
    }

    /// <summary>関節ごとの親・子の部位の格子の大きさ（半径 [mm]）とつなぎの箱の大きさ（調べもの）。</summary>
    public static string Dimensions()
    {
        using var game = new BoxingGame(SwordStance.Tuning);
        var text = new StringBuilder();
        text.AppendLine("| joint | parent half extents [mm] | child half extents [mm] | cell [mm] | bridge box [cells] |");
        text.AppendLine("|---|---|---|---:|---:|");
        foreach (var b in game.Cpu.Voxels!.Bridges)
        {
            var (p, c) = (b.Parent.Body.Grid, b.Child.Body.Grid);
            string Half(Vector3 v) => string.Create(CultureInfo.InvariantCulture, $"{v.X * 1000f:0} x {v.Y * 1000f:0} x {v.Z * 1000f:0}");
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {b.Joint} | {Half(p.HalfExtents)} | {Half(c.HalfExtents)} | {p.CellSize.X * 1000f:0.0} | {b.Body.Grid.Dimensions.X} |"));
        }

        return text.ToString();
    }

    private static bool[] Filled(Aigis.Foundation.Voxels.VoxelGrid grid) => [.. Enumerable.Range(0, grid.CellCount).Select(i => grid.MaterialAt(i) != 0)];

    /// <summary>はじめに埋まっていて、いま空の、<paramref name="point"/>（格子の座標）から <see cref="Radius"/> 以内のセルの数。</summary>
    private static int LostNear(Aigis.Foundation.Voxels.VoxelGrid grid, bool[] filled, Vector3 point)
    {
        var n = 0;
        for (var i = 0; i < grid.CellCount; i++)
        {
            if (!filled[i] || grid.MaterialAt(i) != 0)
            {
                continue;
            }

            var (x, y, z) = grid.Coordinates(i);
            n += Vector3.Distance(grid.CellCenter(x, y, z), point) <= Radius ? 1 : 0;
        }

        return n;
    }

    private static readonly GuardPosture Down = new("down", Vector3.Zero, new Vector3(0.45f, -0.75f, 0.05f));

    /// <summary>
    /// 全関節の表: 関節 × 姿勢（立ったまま・右のまわりに ±bend・前のまわりに ±bend）を、向き × 高さのずれで薙ぎ、つなぎの有無で、
    /// 関節の両側の部位から削れたセルの合計・関節で断たれた（子が落ちた）本数を比べる。
    /// </summary>
    public static string Table(IReadOnlyList<HumanoidRagdoll.RagdollJoint> joints, IReadOnlyList<float> offsets, IReadOnlyList<float> speeds, float bend, int parallel)
    {
        var poses = new List<(string Axis, float Bend)> { ("right", 0f), ("right", bend), ("right", -bend), ("forward", bend), ("forward", -bend) };
        var jobs = new List<(HumanoidRagdoll.RagdollJoint J, string Axis, float Bend, string Dir, float Offset, float Speed, bool Bridges)>();
        foreach (var j in joints)
        {
            foreach (var (axis, b) in poses)
            {
                foreach (var dir in new[] { "side", "front", "back" })
                {
                    foreach (var speed in speeds)
                    {
                        foreach (var o in offsets)
                        {
                            jobs.Add((j, axis, b, dir, o, speed, false));
                            jobs.Add((j, axis, b, dir, o, speed, true));
                        }
                    }
                }
            }
        }

        var results = new JointResult[jobs.Count];
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = parallel }, i =>
        {
            var j = jobs[i];
            results[i] = SlashOnce(j.J, j.Dir, j.Offset, j.Speed, j.Bridges, j.Bend, j.Axis);
        });
        var text = new StringBuilder();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"offsets mm {string.Join(",", offsets.Select(o => o * 1000f))}; dirs side/front/back; speeds {string.Join(",", speeds)} m/s; bend {bend} deg"));
        text.AppendLine("| joint | pose (axis, deg) | bridge cells (box-edge cells, max) | swings | cells lost near joint off | on | joint cut off | joint cut on | touched off | touched on |");
        text.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var g in jobs.Select((j, i) => (j, r: results[i])).GroupBy(x => (x.j.J, x.j.Axis, x.j.Bend)))
        {
            var off = g.Where(x => !x.j.Bridges).Select(x => x.r).ToList();
            var on = g.Where(x => x.j.Bridges).Select(x => x.r).ToList();
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {g.Key.J} | {g.Key.Axis} {g.Key.Bend:+0;-0;0} | {on.Average(r => r.BridgeCells):0} ({on.Max(r => r.EdgeCells)}) | {on.Count} | {off.Sum(r => r.NearLost)} | {on.Sum(r => r.NearLost)} | {off.Count(r => r.Severed)} | {on.Count(r => r.Severed)} | {off.Count(r => r.Touched)} | {on.Count(r => r.Touched)} |"));
        }

        text.AppendLine();
        text.AppendLine("swings where the bridge changes whether the joint is cut (off -> on):");
        text.AppendLine("| joint | pose | dir | offset mm | speed | cut off | cut on | near cells off | near cells on |");
        text.AppendLine("|---|---|---|---:|---:|---|---|---:|---:|");
        foreach (var g in jobs.Select((j, i) => (j, r: results[i])).GroupBy(x => (x.j.J, x.j.Axis, x.j.Bend, x.j.Dir, x.j.Offset, x.j.Speed)))
        {
            var off = g.Single(x => !x.j.Bridges).r;
            var on = g.Single(x => x.j.Bridges).r;
            if (off.Severed != on.Severed)
            {
                text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"| {g.Key.J} | {g.Key.Axis} {g.Key.Bend:+0;-0;0} | {g.Key.Dir} | {g.Key.Offset * 1000f:0} | {g.Key.Speed} | {off.Severed} | {on.Severed} | {off.NearLost} | {on.NearLost} |"));
            }
        }

        return text.ToString();
    }
}
