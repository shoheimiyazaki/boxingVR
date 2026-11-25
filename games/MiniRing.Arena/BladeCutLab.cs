using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using Aigis.Foundation.Voxels;
using Aigis.Physics;
using Aigis.Physics.Voxels;
using Aigis.Render;
using MiniRing.Characters;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// 刃の切れ味の計測台（docs/BLADE_CUT_STUDY.md）: ラグドールで振らず、刃物を 1 つの関節（肩の位置の固定した軸）のまわりの円弧で、
/// 構えて立つ相手の部位へ当てる。刃が相手に触れるまでは円弧の角速度を保ち（腕が軌道を決める）、触れた後は刃そのもの
/// （<see cref="Mode.Free"/>: 試合の剣と同じく、接触は刃と打たれた部位の 2 体の運動エネルギーで斬る）か、角速度を保ったまま
/// （<see cref="Mode.Imposed"/>: 腕が無限に強い上限）で斬らせる。押し込む手の力（<see cref="Case.Drive"/>）は試合の
/// <c>BoxingGame.Drive</c> と同じ入口（<see cref="BoxingGame.FreeDrive"/>）から渡す。物理と判断の振る舞いは変えない。
/// </summary>
internal static class BladeCutLab
{
    /// <summary>1 歩 [s]: 物理の刻み（既定 240 Hz。簡素化の測定では <see cref="BoxingGame.DefaultSubstepRate"/> に合わせて粗くする）。</summary>
    private static float Dt => 1f / BoxingGame.DefaultSubstepRate;

    /// <summary>肩（円弧の中心）から握りまでの長さ [m]: 伸ばした腕（肩から握った拳の中心）。成人男性の肩峰から握りの中心でおよそ 0.6〜0.65 m。</summary>
    private const float ArmLength = 0.62f;

    /// <summary>当たる前に円弧を進める長さ [m]（部位の表面の手前、打つ点の道のりで）。</summary>
    private const float RunUp = 0.10f;

    /// <summary>当たってから測り続ける長さ [s]。</summary>
    private const float AfterContact = 0.25f;

    /// <summary>当てる部位。</summary>
    public enum Target
    {
        Forearm,
        UpperArm,
        Neck,
        Torso,
        Thigh,
    }

    /// <summary>当たった後の刃の動き。</summary>
    public enum Mode
    {
        /// <summary>当たった後は刃だけ（試合の握った剣と同じ: 接触は腕の質量を見ない）。</summary>
        Free,

        /// <summary>当たった後も角速度を保つ（腕が無限に強い上限。jointlab・necklab と同じ）。</summary>
        Imposed,
    }

    /// <summary>1 回の振り。</summary>
    /// <param name="Kind">刃物。</param>
    /// <param name="Target">当てる部位（相手の Aigis の Left = 本人の右の手足）。</param>
    /// <param name="Speed">打つ点（刃の物打ち）の円弧の速さ [m/s]。</param>
    /// <param name="Drive">刃を打つ向きへ押し込む手の力 [N]（試合の <c>HandPush</c> にあたる）。</param>
    /// <param name="MassScale">刃の剛体の質量と慣性の倍率（形は同じ。運動量とエネルギーを分ける実験）。</param>
    /// <param name="FromTip">打つ点の、切っ先からの距離 [m]。</param>
    /// <param name="Mode">当たった後の刃の動き。</param>
    /// <param name="Tilt">刃筋の傾き [度]: 刃の面と打つ向きのなす角（刀身の軸のまわりに刃を回す）。0 なら刃筋が通る（刃の面が打つ向きに沿う）。</param>
    public sealed record Case(WeaponKind Kind, Target Target, float Speed, float Drive = 0f, float MassScale = 1f, float FromTip = 0.12f, Mode Mode = Mode.Free, float Tilt = 0f)
    {
        public string Label => string.Create(CultureInfo.InvariantCulture,
            $"{WeaponName(Kind)} {TargetName(Target)} {Speed:0.#} m/s{(Drive > 0f ? $" +{Drive:0}N" : "")}{(MassScale != 1f ? $" 質量×{MassScale:0.##}" : "")}{(FromTip != 0.12f ? $" 先から{FromTip * 100f:0}cm" : "")}{(Mode == Mode.Imposed ? " 角速度保持" : "")}{(Tilt != 0f ? $" 刃筋{Tilt:0}°" : "")}");
    }

    /// <summary>1 回の結果。</summary>
    public sealed record Row(
        Case Case, bool Touched, float SwordMass, float MEffSword, float MEffPart, float HitSpeed, float Energy, float SwordEnergy, float Momentum,
        int Skin, int Muscle, int Bone, int Organ, int Other, float Depth, float Thickness, bool BoneCut, bool Bent, bool Fell, bool Died, bool Stopped,
        float ExitRatio, float Spent, float PeakPressure, int WeaponLost, string FirstPart, string OtherParts = "", int SlabBone = 0, float Cell = 0f)
    {
        /// <summary>2 体の換算質量 [kg]（接触の式が使う質量: 刃と部位の、打つ向きの有効質量の直列）。</summary>
        public float MEffPair => MEffSword > 0f && MEffPart > 0f ? 1f / ((1f / MEffSword) + (1f / MEffPart)) : 0f;

        /// <summary>深さの割合（打つ向きの厚さに対して）。</summary>
        public float DepthShare => Thickness > 0f ? Math.Clamp(Depth / Thickness, 0f, 1f) : 0f;

        public int Level => !Touched ? 0 : Fell || Died ? 6 : BoneCut ? 5 : Bone > 0 ? 4 : Muscle > 0 || Organ > 0 ? 3 : Skin > 0 ? 2 : 1;

        public string Outcome => Level switch
        {
            0 => "当たらず",
            1 => "無傷",
            2 => "皮まで",
            3 => "肉まで",
            4 => "骨に食い込む",
            5 => Bent ? "骨が折れる（曲げ、肉でぶら下がる）" : "骨を断つ（肉でぶら下がる）",
            _ => Died ? "落ちる（死）" : "落ちる",
        };
    }

    public static string WeaponName(WeaponKind kind) => kind switch
    {
        WeaponKind.IronSword => "片手剣",
        WeaponKind.Katana => "日本刀",
        WeaponKind.Longsword => "ロングソード",
        _ => kind.ToString(),
    };

    public static string TargetName(Target target) => target switch
    {
        Target.Forearm => "前腕",
        Target.UpperArm => "上腕",
        Target.Neck => "首",
        Target.Torso => "胴",
        _ => "腿",
    };

    /// <summary>的と刃の置き方（ワールド）。</summary>
    internal sealed record Aim(int PartBody, VoxelBody PartVoxels, Vector3 Point, Vector3 Heading, Vector3 Across, Vector3 LocalPoint, Vector3 LocalHeading, float Entry, float Thickness, int SlabBone, float Cell);

    /// <summary>
    /// 的の姿勢: 両腕を体の横へ広げて少し下げる（手は肩から横へ腕の 0.9、下へ 0.25）。構えた腕は前腕・上腕・顔が近く、刃が同時に
    /// いくつもの部位に触れて測りが混ざるため、腕を胴から離す。
    /// </summary>
    private static readonly GuardPosture Spread = new("腕を広げる", Vector3.Zero, new Vector3(0.9f, -0.25f, 0.1f));

    /// <summary>胴を斬るときの的の姿勢: 両手を頭の横の上へ上げる（胴の高さを横に払う刃が腕に触れない）。</summary>
    private static readonly GuardPosture Raised = new("腕を上げる", Vector3.Zero, new Vector3(0.45f, 0.75f, 0.05f));

    /// <summary>的（<paramref name="target"/> に合わせた姿勢で立つ）と、構えて立つ相手（<see cref="WeaponDrills.StillMatch"/> と同じく 1.68 秒落ち着かせる）。</summary>
    private static BoxingGame Still(Target target)
    {
        var posture = target == Target.Torso ? Raised : Spread;
        var game = new BoxingGame(seed: 1, corners: new BoxingCorners("stand", _ => new StandGuard(posture), _ => new StandGuard()));
        for (var f = 0; f < 101; f++)
        {
            game.Step(default, 1f / 60f);
        }

        return game;
    }

    private static int PartId(Fighter f, Target t) => t switch
    {
        Target.Forearm => f.Parts.LeftForearm,
        Target.UpperArm => f.Parts.LeftUpperArm,
        Target.Neck => f.Parts.Head,
        Target.Torso => f.Parts.Abdomen ?? f.Parts.Pelvis,
        _ => f.Parts.LeftThigh,
    };

    private static Aim Place(BoxingGame game, Target target)
    {
        var victim = game.Cpu;
        var id = PartId(victim, target);
        var part = victim.Voxels!.PartOf(id) ?? throw new InvalidOperationException($"部位 {target} のボクセルがありません。");
        var grid = part.Body.Grid;
        var axisIndex = target is Target.Neck or Target.Torso ? 1 : part.SeverAxis ?? 1;
        var axisLocal = axisIndex switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ };
        int Coord((int X, int Y, int Z) c) => axisIndex switch { 0 => c.X, 1 => c.Y, _ => c.Z };
        var filled = Enumerable.Range(0, grid.CellCount).Where(i => grid.MaterialAt(i) != 0).ToList();
        List<int> slab;
        List<int> core;
        if (target == Target.Neck)
        {
            // 首: 頭の部位の底にある頸椎の段（脊髄の通る段）。狙いは脊髄の中心。
            core = filled.Where(i => grid.MaterialAt(i) == BoxerAnatomy.Cord).ToList();
            var (lo, hi) = (core.Min(i => grid.Coordinates(i).Y), core.Max(i => grid.Coordinates(i).Y));
            var mid = (lo + hi) / 2;
            slab = filled.Where(i => grid.Coordinates(i).Y == mid).ToList();
            core = core.Where(i => grid.Coordinates(i).Y == mid).ToList();
        }
        else
        {
            var occupied = filled.Select(i => Coord(grid.Coordinates(i))).ToList();
            var mid = (occupied.Min() + occupied.Max()) / 2;
            slab = filled.Where(i => Coord(grid.Coordinates(i)) == mid).ToList();
            core = target == Target.Torso ? slab : slab.Where(i => grid.MaterialAt(i) == BoxerAnatomy.Bone).ToList();
            core = core.Count > 0 ? core : slab;
        }

        Vector3 Center(int i)
        {
            var (x, y, z) = grid.Coordinates(i);
            return grid.CellCenter(x, y, z);
        }

        var localPoint = core.Aggregate(Vector3.Zero, (s, i) => s + Center(i)) / core.Count;
        var toWorld = part.Body.GridToWorld();
        var point = Vector3.Transform(localPoint, toWorld);
        var axis = Vector3.Normalize(Vector3.TransformNormal(axisLocal, toWorld));
        var chest = victim.ChestPosition;
        var side = game.World.GetState(victim.Parts.LeftUpperArm).Position - chest;
        side.Y = 0f;
        side = Vector3.Normalize(side);
        var forward = victim.Frame.Forward;
        var prefer = target switch
        {
            Target.Forearm or Target.UpperArm => -Vector3.UnitY, // 広げた腕へ振り下ろす
            Target.Torso => -forward, // 胴を前から横一文字
            _ => -side, // 首・腿は体の外から内へ
        };
        var heading = Vector3.Normalize(prefer - (axis * Vector3.Dot(prefer, axis)));
        var across = Vector3.Normalize(Vector3.Cross(heading, axis));
        // 円弧の中心（肩）は打つ点から −across の向き。振る人の立つ側へ置く（胴は体の横、ほかは相手の前）。
        var pivotSide = target is Target.Torso ? side : forward;
        if (Vector3.Dot(-across, pivotSide) < 0f)
        {
            across = -across;
        }

        Matrix4x4.Invert(toWorld, out var toLocal);
        var localHeading = Vector3.Normalize(Vector3.TransformNormal(heading, toLocal));
        var projections = slab.Select(i => Vector3.Dot(Center(i) - localPoint, localHeading)).ToList();
        var cell = grid.CellSize.X;
        var entry = -projections.Min() + (0.5f * cell);
        var thickness = projections.Max() - projections.Min() + cell;
        return new Aim(id, part.Body, point, heading, across, localPoint, localHeading, entry, thickness, slab.Count(i => grid.MaterialAt(i) == BoxerAnatomy.Bone), cell);
    }

    /// <summary>刃の打つ点（武器のローカル）: 切っ先から <paramref name="fromTip"/> 手前の、刃（−Y）の縁。</summary>
    private static Vector3 StrikeLocal(WeaponKind kind, float fromTip)
    {
        var grid = Weapon.Build(kind);
        var x = Weapon.TipOf(kind).X - fromTip;
        var edge = float.MaxValue;
        for (var i = 0; i < grid.CellCount; i++)
        {
            if (grid.MaterialAt(i) == 0)
            {
                continue;
            }

            var (cx, cy, cz) = grid.Coordinates(i);
            var c = grid.CellCenter(cx, cy, cz);
            if (MathF.Abs(c.X - x) <= grid.CellSize.X * 0.5f)
            {
                edge = MathF.Min(edge, c.Y - (grid.CellSize.Y * 0.5f));
            }
        }

        return new Vector3(x, edge, 0f);
    }

    /// <summary>
    /// 剛体 <paramref name="id"/> の、<paramref name="point"/> で <paramref name="direction"/> へ押したときの有効質量 [kg]（<c>VoxelContact.InverseMass</c> と同じ式）。
    /// </summary>
    private static float EffectiveMass(PhysicsWorld world, int id, BodyState s, Vector3 point, Vector3 direction)
    {
        var r = point - s.Position;
        var local = Vector3.Transform(Vector3.Cross(r, direction), Quaternion.Conjugate(s.Orientation));
        var inertia = world.GetLocalInertiaDiagonal(id);
        var spin = Vector3.Transform(local / Vector3.Max(inertia, new Vector3(1e-12f)), s.Orientation);
        var inverse = (1f / world.GetMass(id)) + Vector3.Dot(Vector3.Cross(spin, r), direction);
        return inverse > 1e-12f ? 1f / inverse : 0f;
    }

    private static float KineticEnergy(PhysicsWorld world, int id, BodyState s)
    {
        var w = Vector3.Transform(s.AngularVelocity, Quaternion.Conjugate(s.Orientation));
        var inertia = world.GetLocalInertiaDiagonal(id);
        return (0.5f * world.GetMass(id) * s.LinearVelocity.LengthSquared()) + (0.5f * Vector3.Dot(w * w, inertia));
    }

    /// <summary>1 振りの場面（置いた刃・的・円弧）。</summary>
    internal sealed class Stage
    {
        public required BoxingGame Game;
        public required Weapon Weapon;
        public required Aim Aim;
        public required Vector3 Pivot;
        public required Vector3 Omega;
        public required Vector3 StrikeLocal;
        public required float Radius;
        public required float RunUpAngle;
    }

    private static Stage Prepare(Case c)
    {
        var game = Still(c.Target);
        var aim = Place(game, c.Target);
        var strike = StrikeLocal(c.Kind, c.FromTip);
        var grip = Weapon.GripOf(c.Kind).X;
        var radius = ArmLength + (strike.X - grip);
        var pivot = aim.Point - (aim.Across * radius);
        var n = Vector3.Normalize(Vector3.Cross(aim.Across, aim.Heading));
        // 刃筋の傾き: 刀身の軸（across）のまわりに刃を回す（打つ点は刃の縁なので、回しても当たる所はほぼ同じ）。
        var orientation = Quaternion.Concatenate(WeaponDrills.Edge(aim.Heading, aim.Across), Quaternion.CreateFromAxisAngle(aim.Across, c.Tilt * MathF.PI / 180f));
        var runUp = (aim.Entry + RunUp) / radius;
        var back = Quaternion.CreateFromAxisAngle(n, -runUp);
        var start = pivot + Vector3.Transform(aim.Across * radius, back);
        var startOrientation = Quaternion.Concatenate(orientation, back);
        var weapon = game.AddWeapon(c.Kind, start - Vector3.Transform(strike, startOrientation), startOrientation);
        var id = weapon.Body.BodyId;
        if (c.MassScale != 1f)
        {
            var bounds = weapon.Body.Grid.OccupiedBounds() ?? throw new InvalidOperationException("空の刃です。");
            game.World.ReshapeDynamicBox(id, weapon.Body.GridCenterLocal + bounds.Center, bounds.HalfExtents, game.World.GetMass(id) * c.MassScale);
        }

        var omega = n * (c.Speed / radius);
        var stage = new Stage { Game = game, Weapon = weapon, Aim = aim, Pivot = pivot, Omega = omega, StrikeLocal = strike, Radius = radius, RunUpAngle = runUp };
        if (c.Drive > 0f)
        {
            game.FreeDrive = w => w == weapon ? Tangent(stage) * c.Drive : Vector3.Zero;
        }

        Impose(stage);
        return stage;
    }

    /// <summary>今の打つ点の円弧の接線（打つ向き）。</summary>
    private static Vector3 Tangent(Stage s)
    {
        var state = s.Game.World.GetState(s.Weapon.Body.BodyId);
        var at = state.Position + Vector3.Transform(s.StrikeLocal, state.Orientation);
        var t = Vector3.Cross(s.Omega, at - s.Pivot);
        return t.LengthSquared() > 1e-12f ? Vector3.Normalize(t) : s.Aim.Heading;
    }

    /// <summary>刃を円弧の角速度に合わせる（肩のまわりの回転: 剛体の原点の速さ = ω ×（原点 − 肩））。</summary>
    private static void Impose(Stage s)
    {
        var id = s.Weapon.Body.BodyId;
        var state = s.Game.World.GetState(id);
        s.Game.World.SetAngularVelocity(id, s.Omega);
        s.Game.World.SetLinearVelocity(id, Vector3.Cross(s.Omega, state.Position - s.Pivot));
    }

    /// <summary>
    /// 1 振りを走らせる。<paramref name="afterStep"/> は物理の 1 歩ごとに、当たってからの時間（当たる前は負）を添えて呼ぶ（動画を撮るため）。
    /// </summary>
    public static Row Run(Case c, Action<BoxingGame, Stage, float>? afterStep = null)
    {
        var stage = Prepare(c);
        using var game = stage.Game;
        var world = game.World;
        var victim = game.Cpu;
        var aim = stage.Aim;
        var weapon = stage.Weapon;
        var id = weapon.Body.BodyId;
        var grids = weapon.Grids.ToHashSet();
        var fragmentsBefore = victim.Voxels!.Fragments.Select(f => f.Body).ToHashSet();
        // 的の部位: 同じ剛体に乗った格子（顔の層）と、斬って分かれた破片も含める。
        bool IsTarget(VoxelBody b) => b.BodyId == aim.PartBody || (!fragmentsBefore.Contains(b) && FromTarget(victim, b, aim.PartBody));
        bool OnVictim(VoxelBody b) => victim.Voxels.PartOf(b.BodyId) is not null || victim.Voxels.Fragments.Any(f => f.Body == b);
        int skin = 0, muscle = 0, bone = 0, organ = 0, other = 0, weaponLost = 0;
        float depthProjection = float.NegativeInfinity, spent = 0f, peak = 0f;
        bool touched = false, boneCut = false, bent = false, fell = false, stopped = false;
        float hitSpeed = 0f, mEffSword = 0f, mEffPart = 0f, swordEnergy = 0f;
        string firstPart = "";
        var inTarget = false;
        float? exitVelocity = null;
        var otherParts = new SortedDictionary<string, int>();
        float? contactAt = null;
        var mass = world.GetMass(id);
        var maxSeconds = ((stage.RunUpAngle * stage.Radius) + aim.Thickness + 0.4f) / MathF.Max(c.Speed, 0.5f);
        var t = 0f;
        while (t < maxSeconds + AfterContact && (contactAt is null || t < contactAt + AfterContact))
        {
            if (contactAt is null || c.Mode == Mode.Imposed)
            {
                Impose(stage);
            }

            var before = world.GetState(id);
            var partBefore = world.GetState(aim.PartBody);
            game.Step(default, Dt);
            t += Dt;
            foreach (var hit in game.RecentCuts)
            {
                if (!hit.Touched || !(grids.Contains(hit.A) || grids.Contains(hit.B)))
                {
                    continue;
                }

                var struck = grids.Contains(hit.A) ? hit.B : hit.A;
                weaponLost += hit.RemovedFor(grids.Contains(hit.A) ? hit.A : hit.B).Count;
                if (!OnVictim(struck))
                {
                    continue;
                }

                if (contactAt is null)
                {
                    contactAt = t;
                    firstPart = victim.Voxels.PartOf(struck.BodyId)?.Kind.ToString() ?? "fragment";
                    var strikeWorld = before.Position + Vector3.Transform(stage.StrikeLocal, before.Orientation);
                    var strikeVelocity = before.LinearVelocity + Vector3.Cross(before.AngularVelocity, strikeWorld - before.Position);
                    var partVelocity = partBefore.LinearVelocity + Vector3.Cross(partBefore.AngularVelocity, strikeWorld - partBefore.Position);
                    var relative = strikeVelocity - partVelocity;
                    hitSpeed = relative.Length();
                    var direction = hitSpeed > 1e-5f ? relative / hitSpeed : aim.Heading;
                    mEffSword = EffectiveMass(world, id, before, strikeWorld, direction);
                    mEffPart = EffectiveMass(world, aim.PartBody, partBefore, strikeWorld, direction);
                    swordEnergy = KineticEnergy(world, id, before);
                }

                spent += hit.EnergySpent;
                peak = MathF.Max(peak, hit.Pressure);
                var target = IsTarget(struck);
                if (target)
                {
                    stopped = hit.Stopped;
                }

                foreach (var v in hit.RemovedFor(struck))
                {
                    if (!target)
                    {
                        other++;
                        var kind = victim.Voxels.PartOf(struck.BodyId)?.Kind.ToString() ?? "fragment";
                        otherParts[kind] = otherParts.GetValueOrDefault(kind) + 1;
                        continue;
                    }

                    var material = BoxerAnatomy.Palette[v.Material];
                    if (material.Tissue == VoxelTissue.Bone)
                    {
                        bone++;
                    }
                    else if (material.Tissue == VoxelTissue.Organ || v.Material == BoxerAnatomy.Cord)
                    {
                        organ++;
                    }
                    else if (v.Material is BoxerAnatomy.Muscle or BoxerAnatomy.Artery)
                    {
                        muscle++;
                    }
                    else
                    {
                        skin++;
                    }

                    if (struck == aim.PartVoxels)
                    {
                        depthProjection = MathF.Max(depthProjection, Vector3.Dot(v.LocalCenter - aim.LocalPoint, aim.LocalHeading));
                    }
                }
            }

            touched |= contactAt is not null;
            var targetNow = game.RecentCuts.Any(h => h.Touched && (grids.Contains(h.A) || grids.Contains(h.B)) && IsTarget(grids.Contains(h.A) ? h.B : h.A));
            if (targetNow)
            {
                inTarget = true;
            }
            else if (inTarget)
            {
                inTarget = false; // 刃が的から抜けた直後（手を離れた刃が回り出す前）の、打つ点の打つ向きの速さ
                var s = world.GetState(id);
                var tip = s.Position + Vector3.Transform(stage.StrikeLocal, s.Orientation);
                exitVelocity = Vector3.Dot(s.LinearVelocity + Vector3.Cross(s.AngularVelocity, tip - s.Position), aim.Heading);
            }

            boneCut |= game.BrokenBones.Any(b => b.Victim == victim && b.Body == aim.PartBody);
            bent |= game.BrokenBones.Any(b => b.Victim == victim && b.Body == aim.PartBody && b.Bent);
            fell |= game.RecentSevers.Any(s => s.Victim == victim && s.Body == aim.PartBody);
            afterStep?.Invoke(game, stage, contactAt is { } at ? t - at : -1f);
        }

        var exit = c.Speed > 0f ? (exitVelocity ?? 0f) / c.Speed : 0f;
        // 骨が断たれて肉でぶら下がった部位が、その後に刃でつなぎの肉まで断たれた（つなぎが無くなった）なら、落ちたとして数える。
        var newFragments = victim.Voxels.Fragments.Where(f => !fragmentsBefore.Contains(f.Body) && f.FragmentOf == aim.PartBody).ToList();
        fell |= boneCut && newFragments.Count > 0 && !newFragments.Exists(f => victim.HangsByFlesh(f.Body.BodyId));
        var cellSize = aim.PartVoxels.Grid.CellSize.X;
        var depth = float.IsNegativeInfinity(depthProjection) ? 0f : depthProjection + aim.Entry + (0.5f * cellSize);
        if (boneCut || fell)
        {
            depth = MathF.Max(depth, aim.Thickness);
        }

        var pair = mEffSword > 0f && mEffPart > 0f ? 1f / ((1f / mEffSword) + (1f / mEffPart)) : 0f;
        return new Row(c, touched, mass, mEffSword, mEffPart, hitSpeed, 0.5f * pair * hitSpeed * hitSpeed, swordEnergy, pair * hitSpeed,
            skin, muscle, bone, organ, other, depth, aim.Thickness, boneCut, bent, fell, victim.IsDead, stopped, exit, spent, peak, weaponLost, firstPart, string.Join(" ", otherParts.Select(kv => $"{kv.Key}:{kv.Value}")), aim.SlabBone, aim.Cell);
    }

    /// <summary>破片 <paramref name="fragment"/> が、的の部位（剛体 <paramref name="partBody"/>）から分かれた物か（反対の手足の破片は数えない）。</summary>
    private static bool FromTarget(Fighter victim, VoxelBody fragment, int partBody)
        => victim.Voxels!.Fragments.Any(f => f.Body == fragment && f.FragmentOf == partBody);

    /// <summary>全部の組を並べて走らせる（<paramref name="parallel"/> 本ずつ）。順は入力の順で返す。</summary>
    public static IReadOnlyList<Row> RunAll(IReadOnlyList<Case> cases, int parallel, TextWriter log)
    {
        var rows = new Row[cases.Count];
        var watch = Stopwatch.StartNew();
        var done = 0;
        Parallel.For(0, cases.Count, new ParallelOptions { MaxDegreeOfParallelism = parallel }, i =>
        {
            rows[i] = Run(cases[i]);
            var n = Interlocked.Increment(ref done);
            lock (log)
            {
                var r = rows[i];
                log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"[{n}/{cases.Count} {watch.Elapsed.TotalSeconds:0}s] {r.Case.Label}: {r.Outcome} 皮{r.Skin} 肉{r.Muscle} 骨{r.Bone} 臓{r.Organ} 他{r.Other} 深さ{r.Depth * 100f:0.0}/{r.Thickness * 100f:0.0}cm E={r.Energy:0.0}J v={r.HitSpeed:0.0}"));
            }
        });
        return rows;
    }

    /// <summary>既定の速さの梯子 [m/s]（打つ点の速さ）。</summary>
    public static readonly float[] Speeds = [2f, 3f, 4f, 5f, 6f, 8f, 10f, 12f, 15f, 18f, 22f, 26f, 30f, 35f, 40f, 50f];

    public static readonly WeaponKind[] Blades = [WeaponKind.IronSword, WeaponKind.Katana, WeaponKind.Longsword];

    private static readonly Target[] PairTargets = [Target.Forearm, Target.Neck];
    private static readonly float[] PairSpeeds = [6f, 10f, 15f, 22f, 30f];
    private static readonly float[] MassScales = [2f, 4f];
    private static readonly Target[] DriveTargets = [Target.Forearm, Target.Neck, Target.Thigh];
    private static readonly float[] Drives = [100f, 300f, 1000f];
    private static readonly float[] DriveSpeeds = [4f, 8f, 15f];
    private static readonly Target[] ImposedTargets = [Target.Forearm, Target.Neck, Target.Thigh, Target.Torso];
    private static readonly float[] ImposedSpeeds = [2f, 4f, 8f];

    /// <summary>
    /// 計測の全部（梯子と、<paramref name="extras"/> なら質量・打つ点・押す手・角速度保持の比べ）を、表（Markdown）と生の CSV にする。
    /// </summary>
    public static (string Markdown, string Csv) Study(int parallel, TextWriter log, IReadOnlyList<float>? speeds = null, IReadOnlyList<Target>? targets = null, IReadOnlyList<WeaponKind>? blades = null, bool extras = true)
    {
        speeds ??= Speeds;
        targets ??= Enum.GetValues<Target>();
        blades ??= Blades;
        var cases = new List<Case>();
        foreach (var kind in blades)
        {
            foreach (var target in targets)
            {
                cases.AddRange(speeds.Select(v => new Case(kind, target, v)));
            }
        }

        var ladderCount = cases.Count;
        if (extras)
        {
            // 運動量かエネルギーか: 日本刀の前腕・首で、質量を 2 倍・4 倍にし、同じ速さ・同じ運動量（速さ ÷ 倍率）・同じエネルギー（速さ ÷ √倍率）で比べる。
            foreach (var target in PairTargets)
            {
                foreach (var v in PairSpeeds)
                {
                    foreach (var k in MassScales)
                    {
                        cases.Add(new Case(WeaponKind.Katana, target, v, MassScale: k));
                        cases.Add(new Case(WeaponKind.Katana, target, v / k, MassScale: k));
                        cases.Add(new Case(WeaponKind.Katana, target, v / MathF.Sqrt(k), MassScale: k));
                    }
                }

                // 打つ点: 切っ先から 30 cm（鍔寄り。刃の有効質量が大きい）。12 cm は梯子にある。
                cases.AddRange(PairSpeeds.Select(v => new Case(WeaponKind.Katana, target, v, FromTip: 0.30f)));
            }

            // 押し込む手: 試合の HandPush の大きさ（--mode ingame で測る）の範囲で。
            foreach (var target in DriveTargets)
            {
                foreach (var f in Drives)
                {
                    cases.AddRange(DriveSpeeds.Select(v => new Case(WeaponKind.Katana, target, v, Drive: f)));
                }
            }

            // 腕が無限に強い上限（角速度を保ち続ける）。
            foreach (var target in ImposedTargets)
            {
                cases.AddRange(ImposedSpeeds.Select(v => new Case(WeaponKind.Katana, target, v, Mode: Mode.Imposed)));
            }
        }

        var rows = RunAll(cases, parallel, log);
        return (Markdown(rows.Take(ladderCount).ToList(), rows.Skip(ladderCount).ToList()), Csv(rows));
    }

    private static readonly Target[] EdgeTargets = [Target.Forearm, Target.UpperArm, Target.Neck, Target.Thigh];
    private static readonly float[] EdgeSpeeds = [8f, 12f, 18f, 25f, 35f];
    private static readonly float[] Tilts = [0f, 5f, 10f, 20f, 35f];
    private static readonly float[] PushSpeeds = [1f, 2f, 4f];
    private static readonly float[] PushDrives = [0f, 150f, 400f, 800f];

    /// <summary>
    /// 太刀筋の計測: 日本刀で (1) 刃筋の傾き × 速さ、(2) 押し切り（遅い振り × 押す手の力）を、前腕・上腕・首・腿に。表（Markdown）と CSV。
    /// </summary>
    public static (string Markdown, string Csv) EdgeStudy(int parallel, TextWriter log)
    {
        var tilt = new List<Case>();
        foreach (var target in EdgeTargets)
        {
            foreach (var v in EdgeSpeeds)
            {
                tilt.AddRange(Tilts.Select(a => new Case(WeaponKind.Katana, target, v, Tilt: a)));
            }
        }

        var push = new List<Case>();
        foreach (var target in EdgeTargets)
        {
            foreach (var v in PushSpeeds)
            {
                push.AddRange(PushDrives.Select(f => new Case(WeaponKind.Katana, target, v, Drive: f)));
            }
        }

        var rows = RunAll([.. tilt, .. push], parallel, log);
        var text = new StringBuilder();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"# 太刀筋の計測（bladecut --mode edge、PhysicsVersion {Fighter.PhysicsVersion}、日本刀）"));
        text.AppendLine();
        text.AppendLine("## 刃筋の傾き（行 = 部位・速さ、列 = 傾き。中身 = 結果 / 深さの割合 / 壊したセル）");
        text.AppendLine();
        text.AppendLine("| 部位 | 速さ [m/s] | " + string.Join(" | ", Tilts.Select(a => string.Create(CultureInfo.InvariantCulture, $"{a:0}°"))) + " |");
        text.AppendLine("|---|---:|" + string.Concat(Tilts.Select(_ => "---|")));
        foreach (var g in rows.Take(tilt.Count).GroupBy(r => (r.Case.Target, r.Case.Speed)))
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {TargetName(g.Key.Target)} | {g.Key.Speed:0} | ") + string.Join(" | ", g.Select(Cell)) + " |");
        }

        text.AppendLine();
        text.AppendLine("## 押し切り（行 = 部位・速さ、列 = 押す手の力）");
        text.AppendLine();
        text.AppendLine("| 部位 | 速さ [m/s] | " + string.Join(" | ", PushDrives.Select(f => string.Create(CultureInfo.InvariantCulture, $"{f:0} N"))) + " |");
        text.AppendLine("|---|---:|" + string.Concat(PushDrives.Select(_ => "---|")));
        foreach (var g in rows.Skip(tilt.Count).GroupBy(r => (r.Case.Target, r.Case.Speed)))
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {TargetName(g.Key.Target)} | {g.Key.Speed:0} | ") + string.Join(" | ", g.Select(Cell)) + " |");
        }

        return (text.ToString(), Csv(rows));
    }

    private static string Cell(Row r) => string.Create(CultureInfo.InvariantCulture, $"{Short(r)} {r.DepthShare * 100f:0}% ({r.Skin + r.Muscle + r.Bone + r.Organ})");

    private static string Short(Row r) => r.Level switch
    {
        0 => "—",
        1 => "無傷",
        2 => "皮",
        3 => "肉",
        4 => "骨傷",
        5 => r.Bent ? "骨折" : "骨断",
        _ => "落ちる",
    };

    public static string Csv(IEnumerable<Row> rows)
    {
        var text = new StringBuilder();
        text.AppendLine("weapon,target,speed_set,drive_N,mass_scale,from_tip_m,mode,tilt_deg,touched,first_part,sword_mass_kg,meff_sword_kg,meff_part_kg,meff_pair_kg,hit_speed,energy_J,sword_energy_J,momentum_Ns,skin,muscle,bone,organ,other,depth_m,thickness_m,bone_cut,bent,fell,died,stopped,exit_ratio,spent_J,peak_MPa,weapon_lost,other_parts,slab_bone_cells,cell_m,outcome");
        foreach (var r in rows)
        {
            var c = r.Case;
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{c.Kind},{c.Target},{c.Speed:0.###},{c.Drive:0},{c.MassScale:0.###},{c.FromTip:0.###},{c.Mode},{c.Tilt:0.#},{r.Touched},{r.FirstPart},{r.SwordMass:0.0000},{r.MEffSword:0.0000},{r.MEffPart:0.0000},{r.MEffPair:0.0000},{r.HitSpeed:0.000},{r.Energy:0.000},{r.SwordEnergy:0.000},{r.Momentum:0.000},{r.Skin},{r.Muscle},{r.Bone},{r.Organ},{r.Other},{r.Depth:0.0000},{r.Thickness:0.0000},{r.BoneCut},{r.Bent},{r.Fell},{r.Died},{r.Stopped},{r.ExitRatio:0.000},{r.Spent:0.000},{r.PeakPressure / 1e6f:0.000},{r.WeaponLost},{r.OtherParts},{r.SlabBone},{r.Cell:0.0000},{r.Outcome}"));
        }

        return text.ToString();
    }

    private static string Markdown(IReadOnlyList<Row> ladder, IReadOnlyList<Row> extra)
    {
        var text = new StringBuilder();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"# 刃の切れ味の計測（bladecut、PhysicsVersion {Fighter.PhysicsVersion}）"));
        text.AppendLine();
        text.AppendLine("打つ点 = 切っ先から 12 cm の刃。円弧の中心 = 握りから 0.62 m（肩）。当たるまで円弧の角速度を保ち、当たった後は刃だけ（試合と同じ接触の式）。相手は構えて立つ CPU。");
        text.AppendLine();
        text.AppendLine("## 境目（最初にその段に届いた速さ [m/s]、括弧は接触のエネルギー [J]）");
        text.AppendLine();
        text.AppendLine("| 武器 | 部位 | 皮を破る | 肉を斬る | 骨に食い込む | 骨を断つ | 落ちる |");
        text.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var group in ladder.GroupBy(r => (r.Case.Kind, r.Case.Target)))
        {
            var rows = group.OrderBy(r => r.Case.Speed).ToList();
            string First(int level)
            {
                var r = rows.FirstOrDefault(x => x.Level >= level);
                return r is null ? "—" : string.Create(CultureInfo.InvariantCulture, $"{r.Case.Speed:0.#} ({r.Energy:0.#})");
            }

            text.AppendLine(CultureInfo.InvariantCulture, $"| {WeaponName(group.Key.Kind)} | {TargetName(group.Key.Target)} | {First(2)} | {First(3)} | {First(4)} | {First(5)} | {First(6)} |");
        }

        text.AppendLine();
        text.AppendLine("## 全部の振り");
        text.AppendLine();
        AppendRows(text, ladder);
        text.AppendLine();
        text.AppendLine("## 比べの実験（質量・打つ点・押す手・角速度保持）");
        text.AppendLine();
        AppendRows(text, extra);
        return text.ToString();
    }

    private static void AppendRows(StringBuilder text, IEnumerable<Row> rows)
    {
        text.AppendLine("| 振り | 当たった速さ [m/s] | 刃の質量 [kg] | 刃の有効質量 [kg] | 部位の有効質量 [kg] | 換算質量 [kg] | 接触のエネルギー [J] | 刃の運動エネルギー [J] | 運動量 [N·s] | 皮 | 肉 | 骨 | 臓器・脊髄 | 他の部位 | 深さ / 厚さ [cm] | 結果 | 止まった | 抜けた直後の速さの比 | 壊すのに使った [J] | 刃の欠け |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|---|---:|---:|---:|");
        foreach (var r in rows)
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {r.Case.Label} | {r.HitSpeed:0.0} | {r.SwordMass:0.000} | {r.MEffSword:0.000} | {r.MEffPart:0.00} | {r.MEffPair:0.000} | {r.Energy:0.0} | {r.SwordEnergy:0.0} | {r.Momentum:0.00} | {r.Skin} | {r.Muscle} | {r.Bone} | {r.Organ} | {r.Other} | {r.Depth * 100f:0.0} / {r.Thickness * 100f:0.0} | {r.Outcome} | {(r.Stopped ? "○" : "—")} | {r.ExitRatio:0.00} | {r.Spent:0.0} | {r.WeaponLost} |"));
        }
    }

    /// <summary>
    /// 試合の中の値: 名簿の刃物の使い手どうしを戦わせ、刃が相手に当てた 1 打ごとの速さ・壊したセル・その歩の握る手の押す力・刃の運動エネルギーを集める。
    /// </summary>
    public static string InGame(IReadOnlyList<(string Red, string Blue)> pairs, int seeds, float seconds, TextWriter log, int parallel = 3)
    {
        var rows = new ConcurrentBag<(string Pair, string Weapon, string Part, float Speed, int Cells, float Drive, float Energy)>();
        var jobs = pairs.SelectMany(p => Enumerable.Range(1, seeds).Select(s => (p.Red, p.Blue, Seed: s))).ToList();
        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = parallel }, job =>
        {
            var red = CharacterRoster.Get(job.Red);
            var blue = CharacterRoster.Get(job.Blue);
            var redContender = RosterLeague.ContenderOf(red, blue.Id, null);
            var blueContender = RosterLeague.ContenderOf(blue, red.Id, null);
            var corners = VideoMaker.Corners(redContender, blueContender, job.Seed, red.Wear, blue.Wear, red.Weapon, blue.Weapon,
                s => blueContender.Create(s), s => redContender.Create(s), referee: true);
            corners = VideoMaker.WithCharacters(corners, red, blue);
            using var game = new BoxingGame(seed: job.Seed, corners: corners);
            for (var t = 0f; t < seconds; t += 1f / 60f)
            {
                game.Step(default, 1f / 60f);
                foreach (var hit in game.WeaponHits)
                {
                    if (game.WeaponOf(hit.Attacker) is not { } w || Weapon.IsBlunt(w.Kind) || Weapon.IsWorn(w.Kind))
                    {
                        continue;
                    }

                    var drive = Vector3.Zero;
                    foreach (var hand in new[] { Hand.Left, Hand.Right })
                    {
                        drive += hit.Attacker.Wields(hand) ? hit.Attacker.HandPush(hand) : Vector3.Zero;
                    }

                    var s = game.World.GetState(w.Body.BodyId);
                    rows.Add(($"{job.Red}-{job.Blue}#{job.Seed}", WeaponName(w.Kind), hit.Part.ToString(), hit.Speed, hit.Cells, drive.Length(), KineticEnergy(game.World, w.Body.BodyId, s)));
                }
            }

            lock (log)
            {
                log.WriteLine($"ingame {job.Red}-{job.Blue} seed {job.Seed} done");
            }
        });

        var text = new StringBuilder();
        text.AppendLine("## 試合の中の刃の当たり（名簿の刃物の使い手どうし）");
        text.AppendLine();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"組: {string.Join("・", pairs.Select(p => $"{p.Red}-{p.Blue}"))}、各 {seeds} 試合 × {seconds:0} 秒。1 打 = `WeaponHits` の 1 件（押し当てているだけの接触を除く）。"));
        text.AppendLine();
        text.AppendLine("| 武器 | 打数 | 速さ p50 / p90 / 最大 [m/s] | 壊したセル p50 / p90 / 最大 | 押す手の力 p50 / p90 / 最大 [N] | 刃の運動エネルギー p50 / p90 / 最大 [J] |");
        text.AppendLine("|---|---:|---|---|---|---|");
        foreach (var g in rows.GroupBy(r => r.Weapon).OrderBy(g => g.Key))
        {
            string Q(IEnumerable<float> xs)
            {
                var s = xs.OrderBy(x => x).ToList();
                float P(double q) => s[(int)Math.Min(s.Count - 1, Math.Floor(q * s.Count))];
                return string.Create(CultureInfo.InvariantCulture, $"{P(0.5):0.0} / {P(0.9):0.0} / {s[^1]:0.0}");
            }

            text.AppendLine(CultureInfo.InvariantCulture, $"| {g.Key} | {g.Count()} | {Q(g.Select(r => r.Speed))} | {Q(g.Select(r => (float)r.Cells))} | {Q(g.Select(r => r.Drive))} | {Q(g.Select(r => r.Energy))} |");
        }

        text.AppendLine();
        text.AppendLine("部位ごとの打数（速さ p90 [m/s]）:");
        text.AppendLine();
        foreach (var g in rows.GroupBy(r => (r.Weapon, r.Part)).OrderBy(g => g.Key.Weapon).ThenByDescending(g => g.Count()))
        {
            var s = g.Select(r => r.Speed).OrderBy(x => x).ToList();
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"- {g.Key.Weapon} × {g.Key.Part}: {g.Count()} 打（p90 {s[(int)Math.Min(s.Count - 1, Math.Floor(0.9 * s.Count))]:0.0} m/s、セル合計 {g.Sum(r => r.Cells)}）"));
        }

        return text.ToString();
    }

    /// <summary>1 振りを、当たる所の横から <paramref name="slow"/> 倍のスローで撮る（音なし）。</summary>
    public static Row Film(Case c, string outPath, float slow, int width = 960, int height = 540, int fps = 30)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        using var renderer = new HeadlessRenderer((uint)width, (uint)height);
        using var ffmpeg = Process.Start(new ProcessStartInfo("ffmpeg",
            $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {width}x{height} -r {fps} -i - -c:v libx264 -pix_fmt yuv420p -crf 21 -preset fast \"{outPath}\"")
        {
            RedirectStandardInput = true,
            UseShellExecute = false,
        }) ?? throw new IOException("ffmpeg を起動できません（PATH にありますか）。");
        var instances = new List<PrimitiveInstance>();
        var lines = new List<DebugLine>();
        var perFrame = 1f / (fps * slow);
        var next = 0f;
        var clock = 0f;
        var row = Run(c, (game, stage, since) =>
        {
            clock += Dt;
            if (since < -0.12f || clock < next)
            {
                return; // 当たる 0.12 秒前から撮る
            }

            next = clock + perFrame;
            var aim = stage.Aim;
            var n = Vector3.Normalize(stage.Omega);
            var side = MathF.Abs(n.Y) < 0.7f ? n : Vector3.Normalize(-aim.Heading - (0.6f * aim.Across));
            var focus = aim.Point;
            var eye = focus + (side * 1.4f) + new Vector3(0f, 0.35f, 0f);
            BoxingRenderer.Fill(game, showTargetGhosts: false, instances, lines);
            var (vp, displayEye) = DisplaySpace.Standard.Camera(eye, focus, Vector3.UnitY, 40f * MathF.PI / 180f, (float)width / height, 0.02f, 30f);
            var caption = new ScreenTextDraw[]
            {
                new(c.Label, new Vector2(30f, 36f), 30f, new Vector4(1f, 1f, 1f, 1f), TextAnchor.Left),
                new(string.Create(CultureInfo.InvariantCulture, $"{(since < 0f ? "当たる前" : $"当たって {since * 1000f:0} ms")}（{slow:0} 倍スロー）"), new Vector2(width - 30f, height - 30f), 24f, new Vector4(0.8f, 0.82f, 0.9f, 1f), TextAnchor.Right),
            };
            var rgba = renderer.RenderPrimitiveScene(ArenaLook.Background, vp, instances, lines, caption, particles: game.BuildParticles(), lighting: game.BuildLighting(),
                shadow: game.BuildShadow(), cameraPosition: displayEye, shapes: [], postProcess: game.BuildPostProcess(), voxels: game.BuildVoxelVolumes());
            ffmpeg.StandardInput.BaseStream.Write(rgba);
        });
        ffmpeg.StandardInput.Close();
        ffmpeg.WaitForExit();
        if (ffmpeg.ExitCode != 0)
        {
            throw new IOException($"ffmpeg failed with exit code {ffmpeg.ExitCode}.");
        }

        return row;
    }

    public static Target ParseTarget(string name) => name.ToLowerInvariant() switch
    {
        "forearm" => Target.Forearm,
        "upperarm" => Target.UpperArm,
        "neck" => Target.Neck,
        "torso" => Target.Torso,
        "thigh" => Target.Thigh,
        _ => throw new ArgumentException($"知らない部位: {name}（forearm, upperarm, neck, torso, thigh）"),
    };

    public static WeaponKind ParseBlade(string name) => name.ToLowerInvariant() switch
    {
        "sword" or "ironsword" => WeaponKind.IronSword,
        "katana" => WeaponKind.Katana,
        "longsword" => WeaponKind.Longsword,
        _ => throw new ArgumentException($"知らない刃物: {name}（sword, katana, longsword）"),
    };
}
