using System.Globalization;
using System.Numerics;
using Aigis.Physics;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// つながりで分かれる体（docs/VOXEL_DAMAGE.md「つながりで分かれる体」）を見るドリル: 打ち合っているボット（青）の首・胸・腰のどこかを、
/// 横から飛んできた日本刀が水平に通り抜ける。刃が物質のつながりを断てば体はそこで分かれ、頭とつながった側だけがボットの入力で動き続ける。
/// 文字では、分かれた破片・体から外れた部位・死・状態を時刻ごとに出す。<c>--video</c> で撮る（音なし）。
/// </summary>
internal static class SplitLab
{
    internal sealed record Stage(BoxingGame Game, Weapon Weapon, Vector3 Heading, Vector3 Target, Vector3 Camera);

    /// <summary>刃が通る高さの所（ワールド）: neck = 首の関節、chest = 胸の中心、waist = 腹と骨盤の間の関節、hip = 骨盤の中心。</summary>
    private static Vector3 TargetOf(BoxingGame game, string at)
    {
        var victim = game.Cpu;
        var p = victim.Parts;
        Vector3 JointAt(HumanoidRagdoll.RagdollJoint joint)
        {
            var (_, parentId, _, anchor) = victim.BridgeJoints().Single(j => j.Joint == joint);
            var s = game.World.GetState(parentId);
            return s.Position + Vector3.Transform(anchor, s.Orientation);
        }

        return at switch
        {
            "neck" => JointAt(HumanoidRagdoll.RagdollJoint.Neck),
            "chest" => game.World.GetState(p.Chest).Position,
            "waist" => JointAt(HumanoidRagdoll.RagdollJoint.Spine),
            "hip" => game.World.GetState(p.Pelvis).Position,
            _ => throw new ArgumentException("--at は neck / chest / waist / hip のどれかです。"),
        };
    }

    /// <summary>試合を <paramref name="lead"/> 秒回し、青の <paramref name="at"/> の高さ（+<paramref name="offset"/>）へ横から刀を置く。</summary>
    internal static Stage Prepare(string at, string dir, float offset, float lead, int seed)
    {
        // 青はその場で弱・強を打ち続ける（RushControl）、赤は離れて構えて立つだけ。
        var corners = new BoxingCorners("rush", _ => new RushControl(), _ => new StandGuard(), Ring: RingLayout.DrillWeapons, Referee: false, BreakActions: false); // 間を空けて、青の拳が赤に届かない
        var game = new BoxingGame(seed: seed, corners: corners);
        for (var t = 0f; t < lead; t += 1f / 240f)
        {
            game.Step(default, 1f / 240f);
        }

        // 刃は、赤から青への向き（away）に直角に横から（side）か、青の前から後ろへ（back。赤から離れる向きなので赤には当たらない）。
        // カメラは刃の向きに直角に、side なら青の後ろ（赤の反対の側）、back ならリングの中心の側から（刃は画面を横切る）。
        var line = (game.World.GetState(game.Cpu.Parts.Pelvis).Position - game.World.GetState(game.Player.Parts.Pelvis).Position) with { Y = 0f };
        var away = line.LengthSquared() > 1e-4f ? Vector3.Normalize(line) : -game.Cpu.Frame.Forward;
        var across = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, away));
        var heading = dir switch
        {
            "side" => across,
            "back" => away,
            _ => throw new ArgumentException("--dir は side か back です。"),
        };
        var view = dir == "side" ? away
            : Vector3.Dot(across, (game.Layout.Center - game.World.GetState(game.Cpu.Parts.Pelvis).Position) with { Y = 0f }) >= 0f ? across : -across;
        var orientation = WeaponDrills.Edge(heading, dir == "side" ? away : across);
        var kind = WeaponKind.Katana;
        var middle = (Weapon.TipOf(kind).X + Weapon.TwoHandGripsOf(kind).Bottom.X) * 0.5f;
        var target = TargetOf(game, at) + new Vector3(0f, offset, 0f);
        var start = target - (heading * 0.6f) - Vector3.Transform(new Vector3(middle, 0f, 0f), orientation);
        var weapon = game.AddWeapon(kind, start, orientation);
        return new Stage(game, weapon, heading, target, view);
    }

    /// <summary>刃を <paramref name="speed"/> で通し、その後 <paramref name="after"/> 秒回す。<paramref name="frame"/> は 1/240 s ごとに呼ぶ（撮影用）。</summary>
    internal static void Run(Stage stage, float speed, float after, Action<float>? frame, TextWriter log)
    {
        var game = stage.Game;
        var victim = game.Cpu;
        var pass = (int)MathF.Ceiling(1.2f / speed * 240f) + 2;
        var total = pass + (int)(after * 240f);
        var reported = -1f;
        for (var i = 0; i < total; i++)
        {
            if (i < pass)
            {
                game.World.SetLinearVelocity(stage.Weapon.Body.BodyId, stage.Heading * speed);
                game.World.SetAngularVelocity(stage.Weapon.Body.BodyId, Vector3.Zero);
            }

            game.Step(default, 1f / 240f);
            var t = i / 240f;
            frame?.Invoke(t);
            if (t - reported >= (t < 1f ? 0.1f : 0.5f))
            {
                reported = t;
                var p = victim.Parts;
                var detached = victim.AllParts.Where(id => !victim.Owns(id)).Select(id => victim.KindOf(id).ToString()).Distinct();
                var pieces = victim.Voxels!.Fragments.GroupBy(f => f.Kind).Select(g => $"{g.Key}×{g.Count()}");
                log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"t {t:0.0}: state {victim.State} dead {victim.DeathCause ?? "-"} head y {victim.HeadPosition.Y:0.00} chest y {game.World.GetState(p.Chest).Position.Y:0.00} pelvis y {victim.PelvisPosition.Y:0.00} | pieces [{string.Join(", ", pieces)}] | off [{string.Join(", ", detached)}] | arm str {victim.Strength(p.LeftUpperArm):0.00} chest v {game.World.GetState(p.Chest).LinearVelocity.Length():0.00} legs v {game.World.GetState(p.LeftThigh).LinearVelocity.Length():0.00}"));
            }
        }
    }

    /// <summary>撮る: リングの中心の側から体全体を据えたカメラで（刃が通る始めの 0.5 秒は <paramref name="slow"/> 倍のスロー）。音なし。</summary>
    internal static void Video(string at, string dir, float offset, float speed, float after, float slow, int seed, float lead, string outPath, TextWriter log)
    {
        const int Width = 960, Height = 540, Fps = 30;
        var stage = Prepare(at, dir, offset, lead: lead, seed);
        using var game = stage.Game;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        using var renderer = new Aigis.Render.HeadlessRenderer(Width, Height);
        using var ffmpeg = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ffmpeg",
            $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {Width}x{Height} -r {Fps} -i - -c:v libx264 -pix_fmt yuv420p -crf 23 -preset fast \"{outPath}\"")
        {
            RedirectStandardInput = true,
            UseShellExecute = false,
        }) ?? throw new IOException("ffmpeg を起動できません（PATH にありますか）。");
        var next = 0f;
        // 刃に直角な高い所から、頭とつながった胸を追って見下ろす（ロープが手前を隠さない）。
        var view = stage.Camera;
        var follow = game.World.GetState(game.Cpu.Parts.Chest).Position;
        void Shoot(float t)
        {
            if (t < next)
            {
                return;
            }

            next = t + (1f / (Fps * (t < 0.5f ? slow : 1f))); // 刃が通る始めの 0.5 秒だけスロー
            follow = Vector3.Lerp(follow, game.World.GetState(game.Cpu.Parts.Chest).Position, 0.15f);
            var center = follow with { Y = stage.Target.Y - 0.3f };
            var eye = center + (view * 2.2f) + new Vector3(0f, 1.5f, 0f);
            var instances = new List<Aigis.Render.PrimitiveInstance>();
            var lines = new List<Aigis.Render.DebugLine>();
            BoxingRenderer.Fill(game, showTargetGhosts: false, instances, lines);
            var (vp, displayEye) = DisplaySpace.Standard.Camera(eye, center, Vector3.UnitY, 45f * MathF.PI / 180f, (float)Width / Height, 0.02f, 30f);
            var rgba = renderer.RenderPrimitiveScene(ArenaLook.Background, vp, instances, lines, [], particles: game.BuildParticles(), lighting: game.BuildLighting(),
                shadow: game.BuildShadow(), cameraPosition: displayEye, shapes: [], postProcess: game.BuildPostProcess(), voxels: game.BuildVoxelVolumes());
            ffmpeg.StandardInput.BaseStream.Write(rgba);
        }

        Run(stage, speed, after, Shoot, log);
        ffmpeg.StandardInput.Close();
        ffmpeg.WaitForExit();
    }

    /// <summary>
    /// 名簿の 2 人（剣士どうしなど）の試合を描かずに回し、胴・首・頭がつながりで分かれた・関節の面で外れた時刻と、その後の状態を出す
    /// （分かれた体でボット・中継の判断が例外を出さないことも見る）。
    /// </summary>
    internal static void Scan(string redId, string blueId, int seeds, float seconds, TextWriter log)
    {
        var red = Characters.CharacterRoster.Get(redId);
        var blue = Characters.CharacterRoster.Get(blueId);
        for (var seed = 1; seed <= seeds; seed++)
        {
            var redContender = RosterLeague.ContenderOf(red, blue.Id, null);
            var blueContender = RosterLeague.ContenderOf(blue, red.Id, null);
            var corners = VideoMaker.Corners(redContender, blueContender, seed, red.Wear, blue.Wear, red.Weapon, blue.Weapon,
                s => blueContender.Create(s), s => redContender.Create(s), true);
            corners = VideoMaker.WithCharacters(corners, red, blue);
            using var game = new BoxingGame(seed: seed, corners: corners);
            var seen = new HashSet<string>();
            for (var t = 0f; t < seconds; t += 1f / 60f)
            {
                game.Step(default, 1f / 60f);
                foreach (var (name, fighter) in new[] { ("red", game.Player), ("blue", game.Cpu) })
                {
                    var torso = fighter.Voxels!.Fragments.Count(f => f.Kind is BodyPartKind.Chest or BodyPartKind.Abdomen or BodyPartKind.Pelvis or BodyPartKind.Head);
                    var off = string.Join(",", fighter.AllParts.Where(id => !fighter.Owns(id)).Select(id => fighter.KindOf(id).ToString()).Distinct());
                    var key = $"{name}:{torso}:{off}:{fighter.DeathCause}";
                    var tick = (int)MathF.Floor(t * 2f);
                    if (off.Contains("Pelvis", StringComparison.Ordinal) && seen.Add($"{name}@{tick}"))
                    {
                        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                            $"   {name} t {t:0.0}: state {fighter.State} head y {fighter.HeadPosition.Y:0.00} chest y {game.World.GetState(fighter.Parts.Chest).Position.Y:0.00} pelvis y {fighter.PelvisPosition.Y:0.00} knocked {fighter.IsKnockedDown}"));
                    }

                    if ((torso > 0 || off.Length > 0) && seen.Add(key))
                    {
                        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                            $"seed {seed} t {t:0.00} {name}: torso/head pieces {torso} off [{off}] state {fighter.State} dead {fighter.DeathCause ?? "-"}"));
                    }
                }
            }

            log.WriteLine($"seed {seed}: done ({game.Message})");
        }
    }

    /// <summary>
    /// 関節を「のこぎり」で少しずつ断つ寄りの動画（関節のつなぎの見え方を見る。音なし）: 打ち続ける青の関節 <paramref name="joint"/> の面（関節の中心を通り、
    /// 親から子へ向く面）から <paramref name="depth"/> 以内の親・子の格子のセル（顔の層も）を、画面の手前から奥へ <paramref name="seconds"/> 秒かけて壊していく
    /// （刃の代わりに、決まった量ずつ確実に断つ）。途中ではつなぎが細って残り、断ち切ると関節が外れる。カメラは関節に寄って追う。
    /// </summary>
    internal static void Saw(HumanoidRagdoll.RagdollJoint joint, float depth, float seconds, float after, int seed, string outPath, TextWriter log)
    {
        const int Width = 960, Height = 540, Fps = 30;
        var corners = new BoxingCorners("rush", _ => new StandGuard(), _ => new StandGuard(), Ring: RingLayout.DrillWeapons, Referee: false, BreakActions: false);
        using var game = new BoxingGame(seed: seed, corners: corners);
        for (var t = 0f; t < 1f; t += 1f / 240f)
        {
            game.Step(default, 1f / 240f);
        }

        var victim = game.Cpu;
        var (_, parentId, childId, anchorOnParent) = victim.BridgeJoints().Single(j => j.Joint == joint);
        var layers = victim.Voxels!.Layers.Where(l => l.Body.BodyId == parentId || l.Body.BodyId == childId).ToList();
        Vector3 Anchor()
        {
            var s = game.World.GetState(parentId);
            return s.Position + Vector3.Transform(anchorOnParent, s.Orientation);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        using var renderer = new Aigis.Render.HeadlessRenderer(Width, Height);
        using var ffmpeg = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ffmpeg",
            $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {Width}x{Height} -r {Fps} -i - -c:v libx264 -pix_fmt yuv420p -crf 20 -preset fast \"{outPath}\"")
        {
            RedirectStandardInput = true,
            UseShellExecute = false,
        }) ?? throw new IOException("ffmpeg を起動できません（PATH にありますか）。");
        var away = (game.World.GetState(victim.Parts.Pelvis).Position - game.World.GetState(game.Player.Parts.Pelvis).Position) with { Y = 0f };
        var view = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, Vector3.Normalize(away))); // 横から
        var removed = new List<Aigis.Foundation.Voxels.RemovedVoxel>();
        var total = (int)((seconds + after) * 240f);
        var next = 0f;
        var reported = -1f;
        for (var i = 0; i < total; i++)
        {
            var time = i / 240f;
            var progress = Math.Clamp(time / seconds, 0f, 1f);
            var anchor = Anchor();
            var normal = Vector3.Normalize(game.World.GetState(childId).Position - game.World.GetState(parentId).Position);
            var sweep = Vector3.Normalize(Vector3.Cross(normal, Vector3.UnitY).LengthSquared() > 1e-4f ? Vector3.Cross(Vector3.UnitY, normal) : view);
            sweep = Vector3.Dot(sweep, view) > 0f ? -sweep : sweep; // 手前（カメラの側）から奥へ
            if (time < seconds)
            {
                foreach (var layer in layers)
                {
                    var grid = layer.Body.Grid;
                    var toWorld = layer.Body.GridToWorld();
                    for (var c = 0; c < grid.CellCount; c++)
                    {
                        if (grid.MaterialAt(c) == 0)
                        {
                            continue;
                        }

                        var (x, y, z) = grid.Coordinates(c);
                        var at = Vector3.Transform(grid.CellCenter(x, y, z), toWorld) - anchor;
                        // 面からの距離が depth 以内で、手前から進んだ「のこぎり」の先（-0.2 m から +0.2 m を progress で進む）より手前のセル。
                        if (MathF.Abs(Vector3.Dot(at, normal)) <= depth && Vector3.Dot(at, sweep) <= -0.2f + (0.4f * progress))
                        {
                            grid.Break(c, removed);
                        }
                    }

                    layer.Body.UpdateMass();
                }
            }

            game.Step(default, 1f / 240f);
            if (time - reported >= 0.25f)
            {
                reported = time;
                var (pairs, held) = victim.Voxels.JointContactCount(parentId, childId);
                log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"t {time:0.00}: saw {progress * 100f:0}% contact {held}/{pairs} intact {victim.JointIntact(parentId, childId)} bridge cells {victim.Voxels.Bridges.Single(b => b.Joint == joint).View().Body.Grid.FilledCount} dead {victim.DeathCause ?? "-"}"));
            }

            if (time < next)
            {
                continue;
            }

            next = time + (1f / Fps);
            var center = anchor;
            var eye = center + (view * 0.75f) + new Vector3(0f, 0.12f, 0f);
            var instances = new List<Aigis.Render.PrimitiveInstance>();
            var lines = new List<Aigis.Render.DebugLine>();
            BoxingRenderer.Fill(game, showTargetGhosts: false, instances, lines);
            var (vp, displayEye) = DisplaySpace.Standard.Camera(eye, center, Vector3.UnitY, 40f * MathF.PI / 180f, (float)Width / Height, 0.02f, 30f);
            var rgba = renderer.RenderPrimitiveScene(ArenaLook.Background, vp, instances, lines, [], particles: game.BuildParticles(), lighting: game.BuildLighting(),
                shadow: game.BuildShadow(), cameraPosition: displayEye, shapes: [], postProcess: game.BuildPostProcess(), voxels: game.BuildVoxelVolumes());
            ffmpeg.StandardInput.BaseStream.Write(rgba);
        }

        ffmpeg.StandardInput.Close();
        ffmpeg.WaitForExit();
    }

    internal static int Command(Options o)
    {
        if (o.Get("saw") is { } sawJoint)
        {
            Saw(JointLab.Parse(sawJoint), o.Float("depth", 0.012f, min: 0.001f), o.Float("seconds", 3f, min: 0.1f), o.Float("after", 1.5f, min: 0f), o.Int("seed", 1),
                o.Get("video") ?? $"docs/video/saw_{sawJoint}.mp4", Console.Out);
            return 0;
        }
        if (o.Get("scan") is { } pair)
        {
            var ids = pair.Split(',');
            Scan(ids[0], ids[1], o.Int("seeds", 4, min: 1), o.Float("seconds", 60f, min: 1f), Console.Out);
            return 0;
        }

        var at = o.Get("at") ?? "waist";
        var dir = o.Get("dir") ?? "side";
        var offset = o.Float("offset", 0f);
        var speed = o.Float("speed", 15f, min: 0.1f);
        var after = o.Float("seconds", 4f, min: 0.1f);
        var seed = o.Int("seed", 1);
        var lead = o.Float("lead", 1f, min: 0.1f);
        if (o.Get("video") is { } video)
        {
            Video(at, dir, offset, speed, after, o.Float("slow", 1f, min: 0.25f), seed, lead, video, Console.Out);
            return 0;
        }

        var stage = Prepare(at, dir, offset, lead: lead, seed);
        using var game = stage.Game;
        Run(stage, speed, after, null, Console.Out);
        var victim = game.Cpu;
        foreach (var (joint, parent, child, _) in victim.BridgeJoints())
        {
            var (total, held) = victim.Voxels!.JointContactCount(parent, child);
            Console.WriteLine($"  {joint}: contact {held}/{total} intact {victim.JointIntact(parent, child)}");
        }

        foreach (var part in victim.Voxels!.Parts.Concat(victim.Voxels.Fragments))
        {
            var n = part.Body.Grid.ConnectedComponents().Count;
            if (n > 1)
            {
                Console.WriteLine($"  NOT CONNECTED: {part.Kind} ({part.Body.BodyId}) has {n} pieces");
            }
        }

        return 0;
    }
}
