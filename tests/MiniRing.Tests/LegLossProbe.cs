using Aigis.Foundation.Voxels;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Desktop;

namespace MiniRing.Tests;

/// <summary>調査用: 脚を断たれた体と、その相手が、その後どう打ち合うか（状態・腰の高さ・当たった数）を 1 秒ごとに書く。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class LegLossProbe
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("shin")]
    [DataRow("thighs")]
    public void Probe_BoutAfterLegLoss(string cut)
    {
        using var game = new BoxingGame(seed: 3, corners: new BoxingCorners("bout", s => BotCatalog.Create("champion", s), s => BotCatalog.Create("swarmer", s)));
        const float dt = 1f / 60f;
        for (var t = 0; t < 20 * 60; t++)
        {
            if (t == 2 * 60)
            {
                if (cut == "shin")
                {
                    Cut(game, game.Cpu, game.Cpu.Parts.LeftShin, 0.4f);
                }
                else
                {
                    Cut(game, game.Cpu, game.Cpu.Parts.LeftThigh, 0.25f);
                    Cut(game, game.Cpu, game.Cpu.Parts.RightThigh, 0.25f);
                }
            }

            game.Step(default, dt);
            if (t % 60 == 59)
            {
                TestContext.WriteLine($"{(t + 1) / 60,2}s cpu {game.Cpu.State,-10} hips {game.Cpu.HoverHeight:F3} head {game.Cpu.HeadPosition.Y:F3} | player {game.Player.State,-10} hips {game.Player.HoverHeight:F3} head {game.Player.HeadPosition.Y:F3} | cons {game.Cpu.Consciousness:F2}/{game.Player.Consciousness:F2} msg {game.Message}");
            }
        }
    }

    /// <summary>
    /// 記録したチェックサムの試合（CharacterSamenessCases）で、脚・腕を失った最初の時刻を書く。PhysicsVersion 39 で値が変わった試合が、
    /// 脚を失った（または切り株の断面の測り方が効く腕を失った）試合だけかを確かめる。
    /// </summary>
    [TestMethod]
    public void Probe_WhichRecordedMatchesLoseALimb()
    {
        foreach (var c in CharacterSamenessCases.All.Concat(CharacterSamenessCases.Weapons))
        {
            foreach (var legacy in new[] { true, false })
            {
                var selection = new MatchSelection(c.Mode, CharacterRoster.Get(c.Red), CharacterRoster.Get(c.Blue), c.Referee);
                var corners = selection.Corners;
                if (legacy)
                {
                    corners = corners with { PlayerWeaponStyle = CharacterSamenessCases.LegacyWeaponStyle(corners.PlayerWeapon), CpuWeaponStyle = CharacterSamenessCases.LegacyWeaponStyle(corners.CpuWeapon) };
                }

                using var game = new BoxingGame(corners: corners);
                var steps = (int)(c.Seconds * 60f);
                float? leg = null, arm = null;
                var half = 0UL;
                for (var i = 1; i <= steps; i++)
                {
                    game.Step(default, 1f / 60f);
                    if (i == steps / 2)
                    {
                        half = game.StateChecksum();
                    }

                    leg ??= game.Cpu.LegSevered || game.Player.LegSevered ? i / 60f : null;
                    arm ??= game.Cpu.ArmSevered(Hand.Left) || game.Cpu.ArmSevered(Hand.Right) || game.Player.ArmSevered(Hand.Left) || game.Player.ArmSevered(Hand.Right) ? i / 60f : null;
                }

                TestContext.WriteLine($"{c.Name,-30} legacy={legacy,-5} half 0x{half:X16} end 0x{game.StateChecksum():X16} halfAt {steps / 2 / 60f:F1}s leg {(leg is { } l ? $"{l:F1}" : "-")} arm {(arm is { } a ? $"{a:F1}" : "-")}");
            }
        }
    }

    /// <summary>
    /// 名簿のキャラ同士（刀 jin × ロングソード konrad、空手 akira × テコンドー taeyun）を種ごとに回し、脚が切れた・折れた最初の時刻と、
    /// その後も戦い続けたか（終わりの状態）を書く。動画に撮る種を探す。
    /// </summary>
    [TestMethod]
    [DataRow("jin", "konrad")]
    [DataRow("akira", "taeyun")]
    public void Probe_SeedsThatCutALeg(string red, string blue)
    {
        var seeds = int.TryParse(Environment.GetEnvironmentVariable("LEGLOSS_SEEDS"), out var n) ? n : 8;
        var seconds = float.TryParse(Environment.GetEnvironmentVariable("LEGLOSS_SECONDS"), System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 60f;
        var referee = Environment.GetEnvironmentVariable("LEGLOSS_REFEREE") == "on";
        var first = int.TryParse(Environment.GetEnvironmentVariable("LEGLOSS_FIRST"), out var f) ? f : 1;
        if (Environment.GetEnvironmentVariable("LEGLOSS_ONLY") is { Length: > 0 } only && only != red)
        {
            return;
        }

        for (var seed = first; seed < first + seeds; seed++)
        {
            var selection = new MatchSelection(MatchMode.Spectate, CharacterRoster.Get(red), CharacterRoster.Get(blue), referee);
            using var game = new BoxingGame(seed: seed, corners: selection.Corners);
            float? redLeg = null, blueLeg = null, over = null;
            var steps = (int)(seconds * 60f);
            for (var i = 1; i <= steps && over is null; i++)
            {
                game.Step(default, 1f / 60f);
                redLeg ??= game.Player.LegSevered ? i / 60f : null;
                blueLeg ??= game.Cpu.LegSevered ? i / 60f : null;
                over ??= game.RoundOver ? i / 60f : null;
            }

            static string T(float? t) => t is { } v ? $"{v:F1}s" : "-";
            TestContext.WriteLine($"{red} vs {blue} seed {seed,2}: red leg {T(redLeg)} blue leg {T(blueLeg)} | round over {T(over)} | red {game.Player.State} short {game.Player.LegShortfall * 100f:F0}cm dead {game.Player.IsDead} | blue {game.Cpu.State} short {game.Cpu.LegShortfall * 100f:F0}cm dead {game.Cpu.IsDead} | {game.Message}");
        }
    }

    /// <summary>脚を失った後、ダウンから立ち直れずに 10 カウントになる流れを 0.5 秒ごとに追う（状態・腰の高さの目標と実際・傾き・意識・立てるか）。</summary>
    [TestMethod]
    [DataRow("jin", "konrad", 8, false)]
    [DataRow("akira", "taeyun", 1, true)]
    public void Probe_TraceAfterLegLoss(string red, string blue, int seed, bool watchRed)
    {
        var selection = new MatchSelection(MatchMode.Spectate, CharacterRoster.Get(red), CharacterRoster.Get(blue), false);
        using var game = new BoxingGame(seed: seed, corners: selection.Corners);
        for (var i = 1; i <= 30 * 60 && !game.RoundOver; i++)
        {
            game.Step(default, 1f / 60f);
            var f = watchRed ? game.Player : game.Cpu;
            if (i % 30 == 0 && f.LegSevered)
            {
                var ground = f.PelvisPosition.Y - f.HoverHeight;
                TestContext.WriteLine($"{i / 60f,5:F1}s {f.State,-10} hoverTarget {f.HoverHeight * 100f:F0}cm pelvisY {f.PelvisPosition.Y:F3} up {f.Uprightness:F2} cons {f.Consciousness:F2} canStand {f.CanStand} rise {f.Rise:F2} legs {f.LegCondition(true):F2}/{f.LegCondition(false):F2} support {f.LegSupportLength(true) * 100f:F0}/{f.LegSupportLength(false) * 100f:F0}cm short {f.LegShortfall * 100f:F0}cm cord {f.CervicalCord:F2}/{f.ThoracicCord:F2}/{f.LumbarCord:F2} | {game.Message}");
            }
        }
    }

    /// <summary>
    /// 脚の骨が折れた・断たれた瞬間ごとに: 誰の脚か・部位・曲げで折れたか（Bent）・その歩に蹴っていたのは誰か・その骨が触れた接触の力と
    /// 相手の部位・削れたセルの数を書く。脚が折れやすすぎる原因を探す。
    /// </summary>
    [TestMethod]
    [DataRow("akira", "taeyun")]
    [DataRow("akira", "ramon")]
    public void Probe_HowLegsBreak(string red, string blue)
    {
        var seeds = int.TryParse(Environment.GetEnvironmentVariable("LEGLOSS_SEEDS"), out var n) ? n : 6;
        for (var seed = 1; seed <= seeds; seed++)
        {
            var selection = new MatchSelection(MatchMode.Spectate, CharacterRoster.Get(red), CharacterRoster.Get(blue), false);
            using var game = new BoxingGame(seed: seed, corners: selection.Corners);
            var seen = new HashSet<int>();
            for (var i = 1; i <= 40 * 60 && !game.RoundOver; i++)
            {
                game.Step(default, 1f / 60f);
                var events = game.BrokenBones.Select(b => (b.Victim, b.Body, b.Bent, Kind: "broken"))
                    .Concat(game.RecentSevers.Where(s => s.Victim is not null).Select(s => (s.Victim!, s.Body, Bent: false, Kind: "off")));
                foreach (var (victim, body, bent, kind) in events)
                {
                    var part = PartName(victim, body);
                    if (part is null || !seen.Add(body * 7 + (kind == "off" ? 1 : 0)))
                    {
                        continue;
                    }

                    var other = victim == game.Player ? game.Cpu : game.Player;
                    var who = victim == game.Player ? "red" : "blue";
                    var selfKick = victim.IsKicking(Hand.Left) || victim.IsKicking(Hand.Right);
                    var foeKick = other.IsKicking(Hand.Left) || other.IsKicking(Hand.Right);
                    var contacts = string.Join("; ", game.RecentCuts.Where(c => c.A.BodyId == body || c.B.BodyId == body).Select(c =>
                    {
                        var mine = c.A.BodyId == body;
                        var touched = mine ? c.B.BodyId : c.A.BodyId;
                        var bend = mine ? c.BentA : c.BentB;
                        return $"vs {PartName(game.Player, touched) ?? PartName(game.Cpu, touched) ?? "?"} passed {c.PassedForce:F0}N force {c.Force:F0}N approach {c.ApproachSpeed:F1}m/s cut {(mine ? c.RemovedA.Count : c.RemovedB.Count)} bendRatio {(bend is { } b ? $"{b.Ratio:F2}@slice{b.Slice}" : "-")}";
                    }));
                    TestContext.WriteLine($"{red}-{blue} s{seed} {i / 60f,5:F2}s {who} {part,-10} {kind,-6} bent={bent,-5} selfKick={selfKick,-5} foeKick={foeKick,-5} | {contacts}");
                }
            }
        }
    }

    /// <summary>無傷の腕・脚の部位を、骨の軸に沿った 5 か所で直角に押したとき、曲げで折れる力 [N]（支えの無い梁）と、骨のセルの断面の数（最小・最大）。</summary>
    [TestMethod]
    public void Probe_IntactBreakForceAlongTheLimb()
    {
        var tuning = FighterTuning.Default;
        foreach (var (kind, half) in new[]
        {
            (BodyPartKind.Forearm, tuning.HalfExtents.Forearm), (BodyPartKind.UpperArm, tuning.HalfExtents.UpperArm),
            (BodyPartKind.Shin, tuning.HalfExtents.Shin), (BodyPartKind.Thigh, tuning.HalfExtents.Thigh),
        })
        {
            var grid = MiniRing.Voxels.BoxerAnatomy.Build(kind, half, tuning.VoxelSize);
            var axis = kind is BodyPartKind.Forearm or BodyPartKind.UpperArm ? 0 : 1;
            var along = axis == 0 ? System.Numerics.Vector3.UnitX : System.Numerics.Vector3.UnitY;
            var length = axis == 0 ? half.X : half.Y;
            var forces = new List<string>();
            foreach (var at in new[] { -0.45f, -0.25f, 0f, 0.25f, 0.45f })
            {
                foreach (var across in new[] { System.Numerics.Vector3.UnitZ, axis == 0 ? System.Numerics.Vector3.UnitY : System.Numerics.Vector3.UnitX })
                {
                    var force = 1f;
                    while (force < 1e5f && Aigis.Physics.Voxels.VoxelBending.FindBreak(grid, along * (2f * at * length), -across * force, axis, loadBearingOnly: true) is null)
                    {
                        force *= 1.05f;
                    }

                    forces.Add($"{at:+0.00;-0.00}{(across == System.Numerics.Vector3.UnitZ ? "z" : "x")}:{force:0}");
                }
            }

            var (nx, ny, nz) = grid.Dimensions;
            var slices = axis == 0 ? nx : ny;
            var bonePerSlice = new int[slices];
            for (var i = 0; i < grid.CellCount; i++)
            {
                var m = grid.AtIndex(i);
                if (m != 0 && grid.Palette[m].Tissue == VoxelTissue.Bone)
                {
                    var (x, y, _) = grid.Coordinates(i);
                    bonePerSlice[axis == 0 ? x : y]++;
                }
            }

            var filled = bonePerSlice.Where(b => b > 0).ToArray();
            TestContext.WriteLine($"{kind,-9} half {half} dims {grid.Dimensions} bone/slice {filled.Min()}..{filled.Max()} [{string.Join(",", bonePerSlice)}] | {string.Join(" ", forces)}");
        }
    }

    /// <summary>試合の間、両者の腿・すねの骨のセルの残り（全体と、いちばん細い断面のセル数）と肉の残りを 0.5 秒ごとに書く（脚が折れるまで）。</summary>
    [TestMethod]
    [DataRow("akira", "taeyun", 1)]
    [DataRow("akira", "taeyun", 3)]
    public void Probe_LegBoneWear(string red, string blue, int seed)
    {
        var selection = new MatchSelection(MatchMode.Spectate, CharacterRoster.Get(red), CharacterRoster.Get(blue), false);
        using var game = new BoxingGame(seed: seed, corners: selection.Corners);
        string Wear(Fighter f, int body)
        {
            var part = f.Voxels!.PartOf(body);
            if (part is null)
            {
                return "?";
            }

            var grid = part.Body.Grid;
            var bySlice = new Dictionary<int, int>();
            var bone = 0;
            for (var i = 0; i < grid.CellCount; i++)
            {
                var m = grid.AtIndex(i);
                if (m != 0 && grid.Palette[m].Tissue == VoxelTissue.Bone)
                {
                    bone++;
                    var (_, y, _) = grid.Coordinates(i);
                    bySlice[y] = bySlice.GetValueOrDefault(y) + 1;
                }
            }

            var thinnest = bySlice.Count == 0 ? 0 : bySlice.Values.Min();
            return $"{bone}/{grid.Initial(MiniRing.Voxels.BoxerAnatomy.Bone)} min{thinnest} flesh{grid.Fraction(VoxelTissue.Flesh):F2}";
        }

        for (var i = 1; i <= 30 * 60 && !game.RoundOver; i++)
        {
            game.Step(default, 1f / 60f);
            if (i % 15 == 0)
            {
                var r = game.Player;
                var b = game.Cpu;
                TestContext.WriteLine($"{i / 60f,5:F2}s red thighs {Wear(r, r.Parts.LeftThigh)} | {Wear(r, r.Parts.RightThigh)} shins {Wear(r, r.Parts.LeftShin)} | {Wear(r, r.Parts.RightShin)} || blue thighs {Wear(b, b.Parts.LeftThigh)} | {Wear(b, b.Parts.RightThigh)} shins {Wear(b, b.Parts.LeftShin)} | {Wear(b, b.Parts.RightShin)} legs {r.LegSevered}/{b.LegSevered}");
                if (r.LegSevered && b.LegSevered)
                {
                    break;
                }
            }
        }
    }

    /// <summary>試合で使う体（名簿のキャラ）の腕・脚の格子: 骨の軸に沿った断面ごとの骨のセルの数、折れうる段（BeamSlices）、5 か所で折れる力。</summary>
    [TestMethod]
    [DataRow("akira")]
    [DataRow("taeyun")]
    [DataRow("marcus")]
    public void Probe_InGameLimbBones(string id)
    {
        var selection = new MatchSelection(MatchMode.Spectate, CharacterRoster.Get(id), CharacterRoster.Get("ramon"), false);
        using var game = new BoxingGame(seed: 1, corners: selection.Corners);
        var f = game.Player;
        foreach (var (name, body) in new[] { ("forearm", f.Parts.LeftForearm), ("upperarm", f.Parts.LeftUpperArm), ("shin", f.Parts.LeftShin), ("thigh", f.Parts.LeftThigh) })
        {
            var part = f.Voxels!.PartOf(body)!;
            var grid = part.Body.Grid;
            var axis = part.Body.BeamAxis ?? 1;
            var (nx, ny, nz) = grid.Dimensions;
            var slices = axis == 0 ? nx : axis == 1 ? ny : nz;
            var bone = new int[slices];
            var filled = new int[slices];
            var flesh = new int[slices];
            for (var i = 0; i < grid.CellCount; i++)
            {
                var m = grid.AtIndex(i);
                var (x, y, z) = grid.Coordinates(i);
                var s = axis == 0 ? x : axis == 1 ? y : z;
                filled[s] += m != 0 ? 1 : 0;
                flesh[s] += m != 0 && grid.Palette[m].Tissue == VoxelTissue.Flesh ? 1 : 0;
                if (m != 0 && grid.Palette[m].Tissue == VoxelTissue.Bone)
                {
                    bone[s]++;
                }
            }

            TestContext.WriteLine($"{id} {name,-8} filled/slice [{string.Join(",", filled)}] flesh/slice [{string.Join(",", flesh)}]");

            var along = axis == 0 ? System.Numerics.Vector3.UnitX : axis == 1 ? System.Numerics.Vector3.UnitY : System.Numerics.Vector3.UnitZ;
            var length = axis == 0 ? grid.HalfExtents.X : axis == 1 ? grid.HalfExtents.Y : grid.HalfExtents.Z;
            var across = axis == 2 ? System.Numerics.Vector3.UnitX : System.Numerics.Vector3.UnitZ;
            var forces = new List<string>();
            foreach (var at in new[] { -0.45f, -0.25f, 0f, 0.25f, 0.45f })
            {
                var force = 1f;
                Aigis.Physics.Voxels.VoxelBreak? found = null;
                while (force < 1e5f && (found = Aigis.Physics.Voxels.VoxelBending.FindBreak(grid, along * (2f * at * length), -across * force, axis, loadBearingOnly: true, part.Body.BeamSlices)) is null)
                {
                    force *= 1.05f;
                }

                forces.Add($"{at:+0.00;-0.00}:{force:0}N@{found?.Slice}");
            }

            TestContext.WriteLine($"{id} {name,-8} axis {axis} dims {grid.Dimensions} beamSlices {part.Body.BeamSlices} distalHigh {part.DistalHigh} bone/slice [{string.Join(",", bone)}] | {string.Join(" ", forces)}");
        }
    }

    private static string? PartName(Fighter f, int body)
    {
        var p = f.Parts;
        return body == p.LeftThigh ? "R-thigh" : body == p.RightThigh ? "L-thigh" : body == p.LeftShin ? "R-shin" : body == p.RightShin ? "L-shin"
            : body == p.LeftFoot || body == p.RightFoot ? "foot" : body == p.Head ? "head" : body == p.Chest ? "chest" : body == p.Pelvis ? "pelvis"
            : body == p.LeftForearm || body == p.RightForearm ? "forearm" : body == p.LeftUpperArm || body == p.RightUpperArm ? "upperarm" : null;
    }

    private static void Cut(BoxingGame game, Fighter fighter, int part, float fromNear)
    {
        var voxel = fighter.Voxels!.PartOf(part)!;
        var grid = voxel.Body.Grid;
        var axis = voxel.Body.BeamAxis ?? 1;
        var n = axis == 0 ? grid.Dimensions.X : axis == 1 ? grid.Dimensions.Y : grid.Dimensions.Z;
        var k = Math.Clamp((int)MathF.Round(fromNear * (n - 1)), 1, n - 2);
        var slice = voxel.DistalHigh ? k : n - 1 - k;
        var removed = new List<RemovedVoxel>();
        for (var i = 0; i < grid.CellCount; i++)
        {
            var (x, y, z) = grid.Coordinates(i);
            if ((axis == 0 ? x : axis == 1 ? y : z) == slice && grid.MaterialAt(i) != 0)
            {
                grid.Break(i, removed);
            }
        }

        if (fighter.Voxels.BreakIfSevered(part) is { } broken)
        {
            game.BreakBone(fighter, broken, 1f);
        }
    }
}
