using System.Numerics;
using Aigis.Foundation.Voxels;
using MiniRing.Voxels;

namespace MiniRing.Weapons;

/// <summary>
/// 武器の実験場の 1 本: 構えて立つ 2 人の試合に武器を置き、狙った所へ飛ばす。<see cref="Focus"/> はカメラが見る所。
/// </summary>
/// <param name="Weapon">打つ武器（飛んでいく方）。</param>
/// <param name="Target">打たれる武器（武器同士の打ち合いなら）。体を打つなら null。</param>
/// <param name="Focus">当たる所（ワールド）。</param>
/// <param name="Heading">武器の飛ぶ向き（ワールド、単位）。</param>
public sealed record WeaponShot(Weapon Weapon, Weapon? Target, Vector3 Focus, Vector3 Heading);

/// <summary>実験場の 1 本の設定: 名前・説明と、試合へ武器を置く手順。</summary>
public sealed record WeaponTrial(string Name, string Title, string Detail, Func<BoxingGame, WeaponShot> Setup);

/// <summary>
/// 1 本の結果: 体と武器が失ったセル、腕が落ちたか、武器が折れたか。<paramref name="FirstContactLost"/> は打った武器の最初の接触だけで
/// 相手が失ったセル（その打ち方そのものの結果）、<paramref name="MaxSpin"/> は打った武器のいちばん速い回転 [rad/s]
/// （手に持っていない武器は、腹を中心から外れて打つと長さの軸まわりに速く回り、回ってきた刃が後から当たる）。
/// </summary>
public sealed record WeaponOutcome(
    WeaponTrial Trial, IReadOnlyDictionary<byte, int> BodyLost, int WeaponLost, int TargetLost, bool Severed, int Snapped, float PeakPressure, float EnergySpent,
    int FirstContactLost, float MaxSpin)
{
    /// <summary>体の失ったセルのうち、ある組織のもの。</summary>
    public int Lost(VoxelTissue tissue) => BodyLost.Where(kv => BoxerAnatomy.Palette[kv.Key].Tissue == tissue).Sum(kv => kv.Value);
}

/// <summary>
/// 武器の実験場: 鉄の剣と木の棍棒で、刃で斬る・腹で打つ・突く・強く斬る・棍棒で打つ、武器同士を打ち合う。的は構えて立っているだけの
/// CPU（<see cref="StandGuard"/>）。どの結果も、触れた形と材質だけから出てくる（<see cref="Aigis.Physics.Voxels.VoxelContact"/>）。
/// テストと対戦場（<c>weaponlab</c>、動画と表）が同じ設定を使う。
/// </summary>
public static class WeaponDrills
{
    /// <summary>
    /// 速さの相似則: 材質の強さを人形の大きさに比例して縮めた（<see cref="BoxerAnatomy.Doll"/>）ので、振りの速さは √大きさ 倍にする
    /// （運動エネルギー ∝ 速さ² と、壊すのに要る仕事 ∝ 強さ の比が、人の大きさのときと同じになる）。
    /// </summary>
    private static readonly float DollSpeed = MathF.Sqrt(BoxerAnatomy.Similitude);

    /// <summary>ふつうの振りの速さ [m/s]（人形の縮尺の剣の、当たる所の速さ。人の大きさで 6 m/s）。</summary>
    public static readonly float OrdinarySpeed = 6f * DollSpeed;

    /// <summary>強い振りの速さ [m/s]（人の大きさで 16 m/s）。</summary>
    public static readonly float HardSpeed = 16f * DollSpeed;

    /// <summary>刃の真ん中あたり（鍔から 13cm 先）の、刃先（ローカル −Y の縁）。</summary>
    public static Vector3 EdgeMiddle { get; } = new(0.03f, -0.009f, 0f);

    /// <summary>剣の腹の同じ所（ローカル −Z の面）。</summary>
    public static Vector3 FlatMiddle { get; } = new(0.03f, 0f, -0.002f);

    /// <summary>剣の切っ先。</summary>
    public static Vector3 SwordPoint { get; } = new(0.165f, 0f, 0f);

    /// <summary>棍棒の頭の側面（ローカル −Y の面）。</summary>
    public static Vector3 ClubHeadSide { get; } = new(0.08f, -0.011f, 0f);

    /// <summary>棍棒の頭の端。</summary>
    public static Vector3 ClubEnd { get; } = new(0.105f, 0f, 0f);

    /// <summary>どちらも構えて立っているだけの試合。構えに落ち着くまで 1 秒進めてから返す。</summary>
    public static BoxingGame StillMatch()
    {
        var game = new BoxingGame(seed: 1, corners: new BoxingCorners("stand", _ => new StandGuard(), _ => new StandGuard()));
        for (var f = 0; f < 60; f++)
        {
            game.Step(default, 1f / 60f);
        }

        return game;
    }

    /// <summary>行 = 武器のローカル X, Y, Z の向かう先（ワールド）の回転。</summary>
    public static Quaternion Axes(Vector3 x, Vector3 y, Vector3 z) =>
        Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0f, y.X, y.Y, y.Z, 0f, z.X, z.Y, z.Z, 0f, 0f, 0f, 0f, 1f));

    /// <summary>刃（ローカル −Y の縁）が <paramref name="heading"/> へ向かい、刀身（ローカル X）は <paramref name="across"/> に沿う。</summary>
    public static Quaternion Edge(Vector3 heading, Vector3 across) => Axes(across, -heading, Vector3.Cross(across, -heading));

    /// <summary>腹（ローカル −Z の面）が <paramref name="heading"/> へ向かい、刀身は <paramref name="across"/> に沿う。</summary>
    public static Quaternion Flat(Vector3 heading, Vector3 across) => Axes(across, Vector3.Cross(-heading, across), -heading);

    /// <summary>切っ先（ローカル +X）が <paramref name="heading"/> へ向かい、刃は上下を向く。</summary>
    public static Quaternion Point(Vector3 heading) => Axes(heading, Vector3.UnitY, Vector3.Cross(heading, Vector3.UnitY));

    /// <summary>
    /// 前腕 <paramref name="arm"/> を横から斬る向き: 体の外から体の中心線へ向かう水平な向き（腕の軸にほぼ直角）と、腕の軸と打つ向きの
    /// 両方に直角な刀身の向き（刃の面が腕を輪切りにする）。
    /// </summary>
    public static (Vector3 Heading, Vector3 Across) AcrossArm(BoxingGame game, Fighter victim, int arm)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(victim);
        var s = game.World.GetState(arm);
        var axis = Vector3.Transform(Vector3.UnitX, s.Orientation);
        var inward = victim.ChestPosition - s.Position;
        inward -= axis * Vector3.Dot(inward, axis);
        inward.Y = 0f;
        var heading = Vector3.Normalize(inward);
        return (heading, Vector3.Normalize(Vector3.Cross(heading, axis)));
    }

    /// <summary>
    /// 武器の <paramref name="striking"/>（武器のローカルの当てる点）が、<paramref name="target"/> の 5cm 手前から
    /// <paramref name="heading"/> へ <paramref name="speed"/> で飛んでいくように置く。
    /// </summary>
    public static Weapon Launch(BoxingGame game, WeaponKind kind, Quaternion orientation, Vector3 striking, Vector3 target, Vector3 heading, float speed)
    {
        ArgumentNullException.ThrowIfNull(game);
        var weapon = game.AddWeapon(kind, target - (heading * 0.05f) - Vector3.Transform(striking, orientation), orientation);
        game.World.SetLinearVelocity(weapon.Body.BodyId, heading * speed);
        return weapon;
    }

    /// <summary>前腕 <paramref name="forearm"/> がどちらの手のものか（Aigis の Left 腕は本人の右手）。</summary>
    public static Hand HandOf(Fighter fighter, int forearm)
    {
        ArgumentNullException.ThrowIfNull(fighter);
        return fighter.ForearmId(Hand.Left) == forearm ? Hand.Left : Hand.Right;
    }

    /// <summary>CPU の前腕を横から: 剣を <paramref name="pose"/> の向きで <paramref name="striking"/> の所から当てる。</summary>
    private static WeaponShot AtForearm(BoxingGame game, WeaponKind kind, Func<Vector3, Vector3, Quaternion> pose, Vector3 striking, float speed)
    {
        var arm = game.Cpu.Parts.LeftForearm;
        var (heading, across) = AcrossArm(game, game.Cpu, arm);
        var at = game.World.GetState(arm).Position;
        return new WeaponShot(Launch(game, kind, pose(heading, across), striking, at, heading, speed), null, at, heading);
    }

    /// <summary>CPU の腹を前から: 武器の <paramref name="striking"/> を先にして突く。</summary>
    private static WeaponShot AtBelly(BoxingGame game, WeaponKind kind, Vector3 striking, float speed)
    {
        var belly = game.World.GetState(game.Cpu.Parts.Pelvis).Position + new Vector3(0f, 0.01f, 0f);
        var into = -game.Cpu.Frame.Forward;
        return new WeaponShot(Launch(game, kind, Point(into), striking, belly, into, speed), null, belly, into);
    }

    /// <summary>リングの上 35cm の空中（2 人から離れた所）。</summary>
    private static Vector3 MidAir => BoxingGame.RingCenter + new Vector3(0.3f, BoxingGame.RingSurfaceHeight + 0.35f, 0f);

    /// <summary>実験場の全項目（並びは動画の順）。</summary>
    public static IReadOnlyList<WeaponTrial> Trials { get; } =
    [
        new("sword-edge", "鉄の剣 — 刃で斬る", $"前腕を横から、刃で {OrdinarySpeed:0.0} m/s",
            g => AtForearm(g, WeaponKind.IronSword, Edge, EdgeMiddle, OrdinarySpeed)),
        new("sword-flat", "鉄の剣 — 腹で打つ", $"同じ所を、刃の腹で {OrdinarySpeed:0.0} m/s",
            g => AtForearm(g, WeaponKind.IronSword, Flat, FlatMiddle, OrdinarySpeed)),
        new("sword-hard", "鉄の剣 — 強く斬る", $"同じ所を、刃で {HardSpeed:0.0} m/s",
            g => AtForearm(g, WeaponKind.IronSword, Edge, EdgeMiddle, HardSpeed)),
        new("sword-thrust", "鉄の剣 — 突く", $"腹を前から、切っ先で {OrdinarySpeed:0.0} m/s",
            g => AtBelly(g, WeaponKind.IronSword, SwordPoint, OrdinarySpeed)),
        new("club-swing", "木の棍棒 — 打つ", $"前腕を横から、頭の側面で {OrdinarySpeed:0.0} m/s",
            g => AtForearm(g, WeaponKind.WoodenClub, Edge, ClubHeadSide, OrdinarySpeed)),
        new("club-thrust", "木の棍棒 — 突く", $"腹を前から、頭の端で {OrdinarySpeed:0.0} m/s",
            g => AtBelly(g, WeaponKind.WoodenClub, ClubEnd, OrdinarySpeed)),
        new("edge-on-club", "鉄の刃 × 木の棍棒", $"空中に寝かせた棍棒へ、上から刃で {OrdinarySpeed:0.0} m/s",
            g =>
            {
                var club = g.AddWeapon(WeaponKind.WoodenClub, MidAir, Quaternion.Identity);
                var down = -Vector3.UnitY;
                var sword = Launch(g, WeaponKind.IronSword, Edge(down, Vector3.UnitZ), EdgeMiddle, MidAir + new Vector3(0.05f, 0.012f, 0f), down, OrdinarySpeed);
                return new WeaponShot(sword, club, MidAir, down);
            }),
        new("club-on-flat", "木の棍棒 × 鉄の剣の腹", $"空中に寝かせた剣の腹へ、上から棍棒で {8f * DollSpeed:0.0} m/s",
            g =>
            {
                var sword = g.AddWeapon(WeaponKind.IronSword, MidAir, Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2f));
                var down = -Vector3.UnitY;
                var club = Launch(g, WeaponKind.WoodenClub, Edge(down, Vector3.UnitZ), ClubHeadSide, MidAir + new Vector3(0.03f, 0.003f, 0f), down, 8f * DollSpeed);
                return new WeaponShot(club, sword, MidAir, down);
            }),
    ];

    /// <summary>
    /// 1 本を、<paramref name="frames"/> 回 × <paramref name="dt"/> 秒だけ進め、結果を数える。<paramref name="afterStep"/> は毎回の後に
    /// 呼ぶ（動画を撮るため）。
    /// </summary>
    public static WeaponOutcome Run(WeaponTrial trial, BoxingGame game, WeaponShot shot, int frames, float dt, Action<int>? afterStep = null)
    {
        ArgumentNullException.ThrowIfNull(trial);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(shot);
        var bodyLost = new Dictionary<byte, int>();
        int weaponLost = 0, targetLost = 0;
        int? firstContact = null;
        float peak = 0f, spent = 0f, spin = 0f;
        var weaponsBefore = game.Weapons.Count;
        var victim = game.Cpu;
        bool OnVictim(Aigis.Physics.Voxels.VoxelBody body) =>
            victim.Voxels is { } v && (v.Parts.Any(p => p.Body == body) || v.Fragments.Any(p => p.Body == body));
        for (var f = 0; f < frames; f++)
        {
            game.Step(default, dt);
            foreach (var hit in game.RecentCuts)
            {
                if (firstContact is null && (hit.A == shot.Weapon.Body || hit.B == shot.Weapon.Body))
                {
                    var other = hit.A == shot.Weapon.Body ? hit.B : hit.A;
                    firstContact = hit.RemovedFor(other).Count;
                }

                weaponLost += hit.RemovedFor(shot.Weapon.Body).Count;
                targetLost += shot.Target is { } t ? hit.RemovedFor(t.Body).Count : 0;
                peak = MathF.Max(peak, hit.Pressure);
                spent += hit.EnergySpent;
                foreach (var body in new[] { hit.A, hit.B }.Where(OnVictim))
                {
                    foreach (var v in hit.RemovedFor(body))
                    {
                        bodyLost[v.Material] = bodyLost.GetValueOrDefault(v.Material) + 1;
                    }
                }
            }

            spin = MathF.Max(spin, game.World.GetState(shot.Weapon.Body.BodyId).AngularVelocity.Length());
            afterStep?.Invoke(f);
        }

        var severed = victim.ArmSevered(Hand.Left) || victim.ArmSevered(Hand.Right);
        return new WeaponOutcome(trial, bodyLost, weaponLost, targetLost, severed, game.Weapons.Count - weaponsBefore, peak, spent, firstContact ?? 0, spin);
    }
}
