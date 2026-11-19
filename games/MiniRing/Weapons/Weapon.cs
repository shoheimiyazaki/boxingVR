using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics;
using Aigis.Physics.Voxels;
using Aigis.Render.Voxels;
using MiniRing.Voxels;

namespace MiniRing.Weapons;

/// <summary>武器の種類。</summary>
public enum WeaponKind
{
    /// <summary>鉄の剣: 菱形の断面の刃（中央の鎬が厚く、両側の刃先は 1 セルの薄さ）と尖った切っ先、鍔、木の握り、柄頭。</summary>
    IronSword,

    /// <summary>木の棍棒: 握りから先へ太くなる丸い棒。刃はない。</summary>
    WoodenClub,

    /// <summary>
    /// 日本刀（両手）: 反りのある片刃の刀身（刃は −Y、峰は +Y。断面は峰が厚く刃先へ薄くなる楔）、切っ先、鍔、長い柄。
    /// 右手は鍔のすぐ下、左手は柄頭を握る。
    /// </summary>
    Katana,

    /// <summary>
    /// ロングソード（両手）: まっすぐな両刃の刀身（菱形の断面）、十字の鍔、長い握り、柄頭。右手は鍔のすぐ下、左手は柄頭の手前を握る。
    /// </summary>
    Longsword,

    /// <summary>
    /// ボクシンググローブ: 拳を包む球。表は革（コーナーの色）、中は詰め物（発泡材）、芯は拳（骨）。拳（前腕の先）に付けて打つ。
    /// 当たりは武器と同じ圧力のモデルで、詰め物が広い面でたわんで衝撃を和らげる。
    /// </summary>
    Glove,

    /// <summary>
    /// 素手の拳（<see cref="HandWear.None"/>）: 薄い皮膚の下に骨の指を握り込み、人さし指・中指の拳頭を前へ出す。
    /// 皮膚同士は強さが同じなので、打った拳の皮膚も削れる。
    /// </summary>
    Fist,

    /// <summary>
    /// メリケンサック（<see cref="HandWear.KnuckleDuster"/>）: 素手の拳の前に、拳頭の 4 つの突起を持つ鉄の棒。両端の板で掌の側の握りの棒へつながる。
    /// 硬く狭い面（突起）に力が集まるので、同じ打撃でも皮膚と肉を押し潰す。
    /// </summary>
    KnuckleDuster,

    /// <summary>
    /// カタール（<see cref="HandWear.Katar"/>）: 拳で握る H 字の鉄の枠（拳の両脇の棒と、拳の前の横木）から、拳の向きへまっすぐ伸びる菱形の断面の鉄の刃。
    /// 拳を突き出すとそのまま刺さる。
    /// </summary>
    Katar,

    /// <summary>
    /// 籠手（<see cref="HandWear.Gauntlet"/>）: 拳の前半分（拳頭と指）を覆う鉄の板、その下に詰め物、掌の側は革（コーナーの色）、手首に鉄の輪。
    /// 刃は無く、グローブより小さく硬い面で打つ。
    /// </summary>
    Gauntlet,

    /// <summary>
    /// 木の棒（六尺棒、両手）: 丸い断面がほぼ一定の太さで、両端が打つ所（どちらの端も同じ形）。両手を肩幅ほど離して中ほどを握る。
    /// 刃はない。長さは基準体格の身長ほど。
    /// </summary>
    Staff,

    /// <summary>
    /// トンファー（<see cref="HandWear.Tonfa"/>、両手に 1 本ずつ）: 拳で握る横の握り（短い柄）と、その端から直角に前腕の外側に沿って後ろへ伸びる
    /// 長い木の棒。棒の前の端は拳の少し前へ出る。素手の拳が握りを握る。材質は棍棒・棒と同じ木。
    /// </summary>
    Tonfa,

    /// <summary>
    /// 盾（<see cref="HandWear.Shield"/>、空いた手にはめる）: 拳が握る横の握り棒と、拳の前半分を覆う鉄の椀（ボス）、拳の中心の面にある丸い木の板、
    /// 板の縁を巻く鉄の輪。素手の拳が握りを握る。材質は木と鉄（今の武器と同じ値）。
    /// </summary>
    Shield,

    /// <summary>
    /// 杖（じょう、両手。合気道・杖道で使う 4 尺 2 寸 ≒ 128〜130 cm の木の丸棒）: 六尺棒（<see cref="Staff"/>）と同じ作りで、短く細い。
    /// 両端が打つ所。握りを手の中で滑らせて持ち替える（<see cref="HandCommand.Slide"/>）。
    /// </summary>
    Jo,

    /// <summary>
    /// バット（両手）: 木の棍棒（<see cref="WoodenClub"/>）と同じ形・材質の物を、両手で握って型（<see cref="MiniRing.Motion.BatForms"/>）で振る種類。
    /// 握りの端（柄頭の側）も打つ所（グリップエンド）。片手で振る棍棒（こんぼう）とは別の種類にして、こんぼうの試合・記録を変えない。
    /// </summary>
    Bat,
}

/// <summary>
/// ボクセルでできた武器 1 本（または折れた破片）。長さはローカル X（+X が切っ先・打つ側）、剣の刃の幅は Y、厚さは Z。
/// 物理の箱の当たり判定はリング（床）とだけぶつかり、体や他の武器とはボクセル同士で当たる（<see cref="VoxelContact"/>）。
/// </summary>
public sealed class Weapon
{
    private Weapon(WeaponKind kind, VoxelBody body, bool initializeHand = true)
    {
        Kind = kind;
        Body = body;
        _grids = [body];
        Volume = VoxelVolume.For(body.Grid);
        if (kind == WeaponKind.Fist && initializeHand)
        {
            _motion = new HandMotion(HandSkeleton.PoseOf(HandShape.Seiken));
            HandState = new BareHandState().Placed(_motion.Pose, out var grid);
            Body.ReplaceGrid(grid);
            Volume = VoxelVolume.For(Body.Grid);
        }
    }

    public WeaponKind Kind { get; }

    /// <summary>
    /// 物の格子（握りの位置などの武器のローカル座標はこの格子の中心からの値）。素手（<see cref="WeaponKind.Fist"/>）では、Body が手の格子そのもの
    /// （<see cref="HandState"/>）で、<see cref="Hands"/> は空: 手だけで物が無いので、手を別の格子に分けず、今の形のまま 1 つのモデルにまとめる。
    /// </summary>
    public VoxelBody Body { get; }

    private readonly List<WeaponHand> _hands = [];
    private VoxelBody[] _grids;

    /// <summary>物の格子に重ねた手の格子（はめる物・握る物の中の手。追加した順で固定）。素手は空（<see cref="Body"/> を参照）。</summary>
    public IReadOnlyList<WeaponHand> Hands => _hands;

    /// <summary>
    /// 同じ剛体に乗った全ての格子: 物の格子（<see cref="Body"/>）が先頭、続いて <see cref="Hands"/> の順。当たり・削れ・描画・チェックサムは
    /// この順（固定）で全ての格子について行う。手が無ければ <see cref="Body"/> 1 つ。
    /// </summary>
    public IReadOnlyList<VoxelBody> Grids => _grids;

    /// <summary>
    /// 手の格子 <paramref name="grid"/>（中心が武器のローカルの <paramref name="gridCenterLocal"/>）を、この武器の剛体に重ねる。
    /// 剛体の質量と当たりの箱は物と手の和になる（<see cref="VoxelBody.AddCompanion"/>）。<paramref name="massPerCell"/> は手の 1 セルの質量 [kg]
    /// （null なら物の格子と同じ）。
    /// </summary>
    internal WeaponHand AddHand(VoxelGrid grid, BareHandState state, Vector3 gridCenterLocal, float? massPerCell = null)
    {
        var hand = new WeaponHand(Body.AddCompanion(grid, gridCenterLocal, massPerCell), state);
        _hands.Add(hand);
        _grids = [Body, .. _hands.Select(h => h.Body)];
        return hand;
    }

    /// <summary>描画用の 3D テクスチャ。</summary>
    public VoxelVolume Volume { get; private set; }

    public BareHandState? HandState { get; private set; }

    private readonly HandMotion? _motion;
    private float[] _strength = [];
    private (VoxelGrid Grid, int Version) _strengthOf;

    /// <summary>
    /// 手形を指令する（<see cref="BoxingGame"/> が毎歩呼ぶ）。手の関節は目標の曲げ角（<see cref="HandSkeleton.PoseOf"/>）を、バネ・ダンパ（<see cref="HandMotion"/>）で
    /// <paramref name="dt"/> 秒ぶん追う: 手形を変えるには時間がかかり、骨の欠けた指は力が入らない。関節の角度がセル 1 つぶん動くたびに、
    /// 親セルを節の向きに沿って置き直す（損傷は台帳のまま）。<paramref name="dt"/> が 0 以下なら、一気に目標の姿勢へ（損傷に依らない）。
    /// </summary>
    public void SetHandShape(HandShape shape, float dt = 0f)
    {
        if (!Enum.IsDefined(shape))
        {
            throw new ArgumentOutOfRangeException(nameof(shape));
        }

        if (HandState is null || _motion is null)
        {
            return;
        }

        var target = HandSkeleton.PoseOf(shape);
        if (dt <= 0f)
        {
            _motion.Snap(target);
        }
        else
        {
            var settled = _motion.Step(target, dt, BoneStrength());
            var drift = _motion.Pose.MaxDifference(HandState.Pose);
            if (drift < ReplaceAngle && !(settled && drift > 1e-6f))
            {
                if (HandState.Shape != shape)
                {
                    HandState = HandState.WithShape(shape);
                }

                return;
            }
        }

        if (HandState.Shape == shape && _motion.Pose.MaxDifference(HandState.Pose) < 1e-6f)
        {
            return;
        }

        HandState = HandState.Capture(Body.Grid).WithShape(shape).Placed(_motion.Pose, out var grid);
        Body.ReplaceGrid(grid);
        Volume = VoxelVolume.For(Body.Grid);
    }

    /// <summary>
    /// 打つ面の点: 手の骨格のいまの置き場（残っているセル）のうち、向き <paramref name="direction"/>（手の格子のローカル）に最も前にあるセル
    /// （前から半セル以内）の重心から、握りの点（<see cref="HandSkeleton.Wrist"/>）までのずれ。手形ごとに分けず、関節角が決めた形から 1 つの式で出す
    /// （貫手は指先、正拳は拳頭、掌底は掌の根元、裏拳は手の甲）。手の無い物（グローブなど）は null。
    /// </summary>
    public Vector3? StrikeOffset(Vector3 direction)
    {
        if (HandState is null || direction.LengthSquared() < 1e-12f)
        {
            return null;
        }

        var grid = Body.Grid;
        var place = HandState.Placement;
        var unit = Vector3.Normalize(direction);
        var front = float.NegativeInfinity;
        for (var i = 0; i < place.Count; i++)
        {
            if (grid.MaterialAt(place[i]) != 0)
            {
                var (x, y, z) = grid.Coordinates(place[i]);
                front = MathF.Max(front, Vector3.Dot(grid.CellCenter(x, y, z), unit));
            }
        }

        var sum = Vector3.Zero;
        var count = 0;
        for (var i = 0; i < place.Count; i++)
        {
            if (grid.MaterialAt(place[i]) == 0)
            {
                continue;
            }

            var (x, y, z) = grid.Coordinates(place[i]);
            var c = grid.CellCenter(x, y, z);
            if (Vector3.Dot(c, unit) >= front - (0.5f * HandSkeleton.Cell))
            {
                sum += c;
                count++;
            }
        }

        return count == 0 ? null : (sum / count) - HandSkeleton.Wrist;
    }

    /// <summary>
    /// 親セルを置き直す関節角のずれ [rad]: 指先がセル 1 つぶん動く角度（セル ÷ 中指の長さ）。これより細かいずれは、
    /// セルの置き場が変わらない。
    /// </summary>
    private static readonly float ReplaceAngle = HandSkeleton.Cell / HandSkeleton.MiddleFingerLength;

    /// <summary>関節ごとの骨の強さ（<see cref="BareHandState.Part"/> の最も傷んだ骨のセルの残り。格子が変わったときだけ数え直す）。</summary>
    private float[] BoneStrength()
    {
        if (_strengthOf.Grid != Body.Grid || _strengthOf.Version != Body.Grid.Version)
        {
            var state = HandState!.Capture(Body.Grid);
            var palm = state.Part(BareHandState.FingerCount, FingerSegment.Palm).BoneWeakest;
            var strength = new float[HandPose.JointCount];
            strength[0] = palm;
            strength[1] = palm;
            for (var f = 0; f < BareHandState.FingerCount; f++)
            {
                for (var j = 0; j < 3; j++)
                {
                    strength[2 + (f * 3) + j] = state.Part(f, (FingerSegment)j).BoneWeakest;
                }
            }

            _strength = strength;
            _strengthOf = (Body.Grid, Body.Grid.Version);
        }

        return _strength;
    }

    /// <summary>この武器を描く指示（格子の変化をテクスチャへ写す）。</summary>
    public VoxelVolumeDraw Draw()
    {
        Volume.Sync(Body.Grid);
        return new VoxelVolumeDraw(Volume, VoxelVolume.CellToWorld(Body.Grid, Body.GridToWorld()));
    }

    /// <summary><paramref name="grid"/> がこの武器の剛体に乗った格子（物か手）か。割り当てなし。</summary>
    public bool Carries(VoxelBody grid)
    {
        for (var i = 0; i < _grids.Length; i++)
        {
            if (_grids[i] == grid)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>この武器の全ての格子（物 → 手の順）を描く指示を <paramref name="draws"/> へ足す。</summary>
    public void AddDraws(List<VoxelVolumeDraw> draws)
    {
        ArgumentNullException.ThrowIfNull(draws);
        draws.Add(Draw());
        for (var i = 0; i < _hands.Count; i++)
        {
            draws.Add(_hands[i].Draw());
        }
    }

    /// <summary>折れて飛んだ破片を、同じ種類の武器として包む。</summary>
    internal static Weapon PieceOf(WeaponKind kind, VoxelBody piece) => new(kind, piece, initializeHand: false);

    /// <summary>
    /// 武器を <paramref name="world"/> に置く。質量はボクセルの材質の密度 × 体積の合計（剛体の箱は格子の大きさ）。
    /// </summary>
    public static Weapon Create(PhysicsWorld world, WeaponKind kind, Vector3 position, Quaternion orientation) =>
        Create(world, kind, Build(kind), position, orientation);

    /// <summary>
    /// 手にはめる物（<see cref="IsWorn"/>）を置く。<paramref name="leather"/> はグローブと籠手の革の材質（コーナーの色:
    /// <see cref="BoxerAnatomy.RedGlove"/> か <see cref="BoxerAnatomy.BlueGlove"/>。革を使わない物では使わない）。
    /// </summary>
    public static Weapon CreateWorn(PhysicsWorld world, WeaponKind kind, byte leather, Vector3 position, Quaternion orientation, bool mirrored = false)
    {
        if (!IsWorn(kind))
        {
            throw new ArgumentException($"{kind} は手にはめる物ではありません。", nameof(kind));
        }

        return Create(world, kind, Build(kind, leather, WeaponVoxelOverrides.Shipped, mirrored), position, orientation, mirrored);
    }

    /// <summary>
    /// 左右が非対称な形（トンファーの棒は拳の片側にある）を、左右の手で鏡にするか。左の手の物は拳の幅の向き（Z）を裏返して作る
    /// （手の座標は左右とも同じ向きの回転で付くので、そのままだと棒が一方では外側・他方では内側になる）。左右が対称な物は裏返さない。
    /// </summary>
    public static bool MirroredOn(WeaponKind kind, Hand hand) => kind == WeaponKind.Tonfa && hand == Hand.Left;

    /// <summary>
    /// 手にはめる物か（グローブ・素手の拳・メリケンサック・カタール・籠手・トンファー）。はめた物は拳から離れず、拳の打撃（パンチ）として数え、
    /// 握った本人の体・同じ人のもう一方の手にはめた物とは当たらない。
    /// </summary>
    public static bool IsWorn(WeaponKind kind) => kind is WeaponKind.Glove or WeaponKind.Fist or WeaponKind.KnuckleDuster or WeaponKind.Katar or WeaponKind.Gauntlet or WeaponKind.Tonfa or WeaponKind.Shield;

    private static Weapon Create(PhysicsWorld world, WeaponKind kind, VoxelGrid grid, Vector3 position, Quaternion orientation, bool mirrored = false)
    {
        ArgumentNullException.ThrowIfNull(world);
        var id = world.AddDynamicBox(position, orientation, grid.HalfExtents, MassOf(grid));
        if (WornHandOf(kind) is not { } spec)
        {
            return new Weapon(kind, new VoxelBody(world, id, grid));
        }

        // 手にはめる物: 物の格子（2 mm）に、中の手の格子（5 mm、素手と同じ手のモデル）を同じ剛体に重ねる。質量は材質の密度 × 体積の和
        // （materialMass: 手の組織の材質の密度も数える。セル数に比例させると、細かい物のセルと粗い手のセルが同じ重さになってしまう）。
        var weapon = new Weapon(kind, new VoxelBody(world, id, grid, materialMass: true));
        var state = new BareHandState().Placed(spec.Pose, out var handGrid, spec.Held);
        weapon.AddHand(handGrid, state, HandCenterLocal(kind, mirrored));
        return weapon;
    }

    /// <summary>武器 <paramref name="kind"/> を作ったときの質量 [kg]（<see cref="Create(PhysicsWorld, WeaponKind, Vector3, Quaternion)"/> と同じ格子・同じ式）。</summary>
    public static float MassOf(WeaponKind kind) => MassOf(Build(kind));

    /// <summary>格子の質量 [kg]: ボクセルの材質の密度 × 体積の合計。</summary>
    private static float MassOf(VoxelGrid grid)
    {
        var mass = 0f;
        for (var m = 1; m < grid.Palette.Count; m++)
        {
            mass += grid.Remaining((byte)m) * grid.CellVolume * grid.Palette[(byte)m].MechanicsOrDefault.Density;
        }

        return mass;
    }

    /// <summary>
    /// 片手で握る所（武器のローカル）: 剣は握りの真ん中、棍棒は握りの端から 1.5cm、両手持ちの刀剣は上の手（鍔の下）の所。
    /// </summary>
    public static Vector3 GripOf(WeaponKind kind, bool mirrored = false) => kind switch
    {
        WeaponKind.IronSword => new Vector3((GuardFrom + PommelAt) * 0.5f, 0f, 0f),
        WeaponKind.WoodenClub => new Vector3(-ClubHalfLength + 0.015f, 0f, 0f),
        WeaponKind.Katana or WeaponKind.Longsword or WeaponKind.Staff or WeaponKind.Jo or WeaponKind.Bat => TwoHandGripsOf(kind).Top,
        WeaponKind.Glove => Vector3.Zero, // 拳はグローブの真ん中
        WeaponKind.Fist => HandSkeleton.Wrist, // 腕の先が手を握る所（手首）
        WeaponKind.KnuckleDuster or WeaponKind.Katar or WeaponKind.Gauntlet or WeaponKind.Tonfa or WeaponKind.Shield => HandSkeleton.Wrist - WornBoundsOf(kind, mirrored).Center, // 中の手の手首（腕の先が手を握る点）
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "知らない武器です。"),
    };

    /// <summary>両手で握れる武器か（棍棒・日本刀・ロングソード・棒。片手剣の握りは片手ぶんの長さ）。</summary>
    public static bool CanTwoHand(WeaponKind kind) => kind is WeaponKind.WoodenClub or WeaponKind.Bat or WeaponKind.Katana or WeaponKind.Longsword or WeaponKind.Staff or WeaponKind.Jo;

    /// <summary>
    /// 丸棒（六尺棒・杖）か: 太さが一定の木の丸い軸で、両端が打つ所・握りを軸に沿って滑らせられる。形の種類（長さ・太さ）だけが違い、
    /// 握り・型・受けの扱いは同じ（CLAUDE.md §8 の種類の一覧の近道）。
    /// </summary>
    public static bool IsRod(WeaponKind kind) => kind is WeaponKind.Staff or WeaponKind.Jo;

    /// <summary>丸棒の半分の長さと半径 [m]（<see cref="IsRod"/> の物だけ）。</summary>
    private static (float HalfLength, float Radius) RodOf(WeaponKind kind) => kind switch
    {
        WeaponKind.Staff => (StaffHalfLength, StaffRadius),
        WeaponKind.Jo => (JoHalfLength, JoRadius),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "丸棒（六尺棒・杖）だけ。"),
    };

    /// <summary>
    /// 手の中で滑らせて握り直せる範囲（武器のローカル X の最小・最大 [m]。滑らせられない武器は null）。棒は、両端の半球と打つ所の手前
    /// （<see cref="StaffTipInset"/>）を除いた、軸が一定の太さのほぼ全体。刀・ロングソード・こんぼうは柄が短く握る所が決まっているので
    /// null（<see cref="HandCommand.Slide"/> は無視）。種類の一覧に足す形は <see cref="CanTwoHand"/> と同じ近道（CLAUDE.md §8）。
    /// </summary>
    public static (float Min, float Max)? SlideSpanOf(WeaponKind kind) => kind switch
    {
        WeaponKind.Staff or WeaponKind.Jo => (-(RodOf(kind).HalfLength - RodOf(kind).Radius - StaffTipInset), RodOf(kind).HalfLength - RodOf(kind).Radius - StaffTipInset),
        _ => null,
    };

    /// <summary>刃の無い（打って潰す）武器か。刃のある武器は、刃の形と材質だけで斬る（<see cref="VoxelContact"/>）。</summary>
    public static bool IsBlunt(WeaponKind kind) => kind is WeaponKind.WoodenClub or WeaponKind.Bat or WeaponKind.Staff or WeaponKind.Jo or WeaponKind.Glove or WeaponKind.Fist or WeaponKind.KnuckleDuster or WeaponKind.Gauntlet or WeaponKind.Tonfa or WeaponKind.Shield;

    private static readonly Lazy<bool>[] RigidFaces =
        Enum.GetValues<WeaponKind>().Select(k => new Lazy<bool>(() => StrikesWithRigidFace(k))).ToArray();

    /// <summary>
    /// 打つ所（<see cref="TipOf"/>）の表の材質が、曲がらない硬い物（ヤング率を持つ鉄・木）か。硬い面は、握った腕が押し込む力をそのまま
    /// 当たった所へ伝える（<see cref="VoxelContact.Resolve"/> の drive）。柔らかい面（グローブの革・素手の皮膚）は、その下の詰め物や肉が
    /// 腕の押す力を広い面で受けて散らす: 圧力のモデルは表の面だけを見るので、柔らかい面の下の腕の力を数えると、押し当てているだけで
    /// 肌が削れてしまう。種類ではなく、作った形の打つ所の材質から決まる。
    /// </summary>
    public static bool HasRigidFace(WeaponKind kind) => RigidFaces[(int)kind].Value;

    private static bool StrikesWithRigidFace(WeaponKind kind)
    {
        var grid = Build(kind);
        var tip = TipOf(kind);
        var step = grid.CellSize.X * 0.5f;
        // 打つ所から内へ、最初に埋まったセルの材質（尖った切っ先は先の数 mm がセルにならないので、2 cm 内まで見る）。
        for (var i = 0; step * i <= 0.02f; i++)
        {
            var at = grid.FilledCellAt(tip - new Vector3(step * i, 0f, 0f));
            if (at >= 0)
            {
                return grid.Palette[grid.MaterialAt(at)].MechanicsOrDefault.Stiffness is > 0f;
            }
        }

        return false;
    }

    /// <summary>両手で握るときの、2 つの拳の間隔 [m]（素手の拳の幅ほど）。</summary>
    public const float HandSpacing = 0.028f;

    /// <summary>
    /// 両手で握る所（武器のローカル）: 下の手（添える手）は 1 本で握るときの所、上の手（利き手）はその 1 拳ぶん先。
    /// 野球のバットと同じく、右利きなら左手が下、右手が上。
    /// </summary>
    public static (Vector3 Top, Vector3 Bottom) TwoHandGripsOf(WeaponKind kind)
    {
        switch (kind)
        {
            case WeaponKind.Katana:
                // 右手は鍔のすぐ下、左手は柄頭（剣道の握り: 両手は柄の両端）。
                return (new Vector3(KatanaGuardAt - 0.012f, 0f, 0f), new Vector3(-KatanaHalfLength + 0.011f, 0f, 0f));
            case WeaponKind.Longsword:
                return (new Vector3(LongGuardAt - 0.011f, 0f, 0f), new Vector3(LongPommelAt + LongPommelRadius + 0.008f, 0f, 0f));
            case WeaponKind.WoodenClub or WeaponKind.Bat:
                var bottom = new Vector3(-ClubHalfLength + 0.015f, 0f, 0f); // 片手で握る所（棍棒の握りの端から 1.5cm）
                return (bottom + new Vector3(HandSpacing, 0f, 0f), bottom);
            case WeaponKind.Staff or WeaponKind.Jo:
                // 棒の真ん中をはさんで、両手を肩幅ほど離す（右手が +X 側、左手が −X 側。棒は対称で、どちらの端も打つ）。
                return (new Vector3(0.5f * StaffHandSpacing, 0f, 0f), new Vector3(-0.5f * StaffHandSpacing, 0f, 0f));
            default:
                throw new ArgumentException($"{kind} は両手で握れません。", nameof(kind));
        }
    }

    /// <summary>打つ側の先（武器のローカル）: 剣の切っ先、棍棒の頭の端の少し手前。振りの速さはここで測る。</summary>
    public static Vector3 TipOf(WeaponKind kind) => kind switch
    {
        WeaponKind.IronSword => new Vector3(SwordHalfLength, 0f, 0f),
        WeaponKind.WoodenClub or WeaponKind.Bat => new Vector3(ClubHalfLength - 0.01f, 0f, 0f),
        WeaponKind.Katana => new Vector3(KatanaHalfLength, KatanaSori, 0f),
        WeaponKind.Longsword => new Vector3(LongHalfLength, 0f, 0f),
        WeaponKind.Staff or WeaponKind.Jo => new Vector3(RodOf(kind).HalfLength - StaffTipInset, 0f, 0f),
        WeaponKind.Glove => new Vector3(GloveRadius, 0f, 0f),
        WeaponKind.Fist => HandSkeleton.SeikenTip,
        WeaponKind.KnuckleDuster or WeaponKind.Katar or WeaponKind.Gauntlet or WeaponKind.Tonfa or WeaponKind.Shield => WornTipInHand(kind) - WornBoundsOf(kind).Center, // 手の座標の打つ所を、物の格子の座標へ
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "知らない武器です。"),
    };

    /// <summary>
    /// 打つ端（武器のローカルの X。<see cref="TipOf"/> と同じ測り方）。棒は両端、刀・ロングソードは切っ先と柄頭。
    /// 接触の形と材質は変えず、入力元の間合いと見える速さを測る点だけを列挙する。
    /// </summary>
    public static IReadOnlyList<float> StrikeEndsX(WeaponKind kind) => kind switch
    {
        WeaponKind.Katana => [TipOf(kind).X, -KatanaHalfLength],
        WeaponKind.Longsword => [TipOf(kind).X, LongPommelAt - LongPommelRadius],
        WeaponKind.Bat => [TipOf(kind).X, -ClubHalfLength + 0.01f], // 頭の端と、握りの端（グリップエンド。先と同じ 1cm 手前）
        _ => IsRod(kind) ? [TipOf(kind).X, -TipOf(kind).X] : [TipOf(kind).X],
    };

    /// <summary>武器の格子（材質は <see cref="BoxerAnatomy.Palette"/> の鉄と木。手にはめる物の革は赤コーナーの色）。</summary>
    public static VoxelGrid Build(WeaponKind kind) => Build(kind, BoxerAnatomy.RedGlove);

    /// <summary>
    /// 武器の格子。<paramref name="leather"/> はグローブと籠手の革の材質（他の物では使わない）。ボクセル編集ソフトで直した中身
    /// （<see cref="WeaponVoxelOverrides.Shipped"/>、games/MiniRing/Voxels/weapons.vox）があればそれを使い、無ければ生成のまま。
    /// </summary>
    public static VoxelGrid Build(WeaponKind kind, byte leather) => Build(kind, leather, WeaponVoxelOverrides.Shipped);

    /// <summary>
    /// 武器の格子。<paramref name="overrides"/> にその種類の直しがあり、セルの数が生成した格子と同じならそれを使う（格子の大きさ・セルの大きさは生成と同じ。
    /// 革は直しの中の赤い革を <paramref name="leather"/> に置き換える）。null・無い・大きさが違えば生成のまま。
    /// 生成に付く後処理は、直しの対象の種類には無い（<see cref="WeaponKind.Fist"/> の外側を皮膚にする後処理だけで、拳は直しの対象外）。
    /// </summary>
    public static VoxelGrid Build(WeaponKind kind, byte leather, WeaponVoxelOverrides? overrides) => Build(kind, leather, overrides, mirrored: false);

    /// <summary>左右が非対称な形を鏡にして作る（<see cref="MirroredOn"/>）。それ以外は上と同じ。</summary>
    public static VoxelGrid Build(WeaponKind kind, byte leather, WeaponVoxelOverrides? overrides, bool mirrored)
    {
        var generated = Generate(kind, leather, mirrored);
        // バットは棍棒と同じ形（.vox で直した中身も同じ物を使う）。
        if (overrides?.For(kind == WeaponKind.Bat ? WeaponKind.WoodenClub : kind, generated.Dimensions) is not { } edited)
        {
            return generated;
        }

        var half = generated.HalfExtents;
        var dims = generated.Dimensions;
        var cell = 2f * half / new Vector3(dims.X, dims.Y, dims.Z);
        return new VoxelGrid(BoxerAnatomy.Palette, half, dims, (p, _) =>
        {
            var c = (p + half) / cell;
            var z = mirrored ? dims.Z - 1 - (int)c.Z : (int)c.Z;
            var m = edited[(int)c.X + (dims.X * ((int)c.Y + (dims.Y * z)))];
            return m == BoxerAnatomy.RedGlove ? leather : m;
        });
    }

    private static VoxelGrid Generate(WeaponKind kind, byte leather, bool mirrored = false) => kind switch
    {
        WeaponKind.IronSword => Sword(),
        WeaponKind.WoodenClub or WeaponKind.Bat => Club(),
        WeaponKind.Katana => Katana(),
        WeaponKind.Longsword => Longsword(),
        WeaponKind.Staff or WeaponKind.Jo => Rod(kind),
        WeaponKind.Glove => Glove(leather),
        WeaponKind.Fist => BareHandState.InitialGrid(),
        WeaponKind.KnuckleDuster => Worn(kind, WornCell, KnuckleDusterAt),
        WeaponKind.Katar => Worn(kind, KatarCell, KatarAt),
        WeaponKind.Gauntlet => Worn(kind, WornCell, p => GauntletAt(p, leather)),
        WeaponKind.Tonfa => Worn(kind, WornCell, TonfaAt, mirrored),
        WeaponKind.Shield => Worn(kind, WornCell, ShieldAt, mirrored),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "知らない武器です。"),
    };

    // ── 剣（人形の縮尺: 実物の片手剣のおよそ 0.36 倍）。刃の薄さを形で表すため、体より細かい 1.25mm のセル。
    private const float SwordCell = 0.00125f;
    private const float SwordHalfLength = 0.165f;
    private const float BladeFrom = -0.1f;
    private const float BladeHalfWidth = 0.009f;
    private const float BladeSpine = 0.00375f;
    private const float PointLength = 0.03f;
    private const float GuardFrom = -0.106f;
    private const float GuardHalfWidth = 0.02f;
    private const float GuardHalfThickness = 0.0025f;
    private const float GripRadius = 0.004f;
    private const float PommelAt = -0.159f;
    private const float PommelRadius = 0.006f;

    /// <summary>
    /// 刃（x が <see cref="BladeFrom"/> から先）: 幅は切っ先の手前 <see cref="PointLength"/> から 0 へ細り、断面は菱形
    /// （中央の鎬で <see cref="BladeSpine"/>、刃先へ向かって薄くなる。刃先の 1/3 は 1 セルの薄さ）。
    /// 鍔は鉄の板、握りは木の丸棒、柄頭は鉄の玉。
    /// </summary>
    private static VoxelGrid Sword()
    {
        var half = new Vector3(SwordHalfLength, GuardHalfWidth, 0.0045f);
        return new VoxelGrid(BoxerAnatomy.Palette, half, SwordCell, (p, _) =>
        {
            if (p.X >= BladeFrom)
            {
                var toPoint = SwordHalfLength - p.X;
                var width = BladeHalfWidth * MathF.Min(1f, toPoint / PointLength);
                var across = MathF.Abs(p.Y);
                if (across > width)
                {
                    return 0;
                }

                // 菱形: 鎬から刃先へ厚さが直線で減る。最も薄い所でも 1 セル（中央の列）は残る。
                var thickness = BladeSpine * (1f - (across / MathF.Max(width, 1e-6f)));
                return MathF.Abs(p.Z) <= MathF.Max(0.5f * thickness, 0.5f * SwordCell) ? BoxerAnatomy.Iron : (byte)0;
            }

            if (p.X >= GuardFrom)
            {
                return MathF.Abs(p.Y) <= GuardHalfWidth && MathF.Abs(p.Z) <= GuardHalfThickness ? BoxerAnatomy.Iron : (byte)0;
            }

            if (Vector3.Distance(p, new Vector3(PommelAt, 0f, 0f)) <= PommelRadius)
            {
                return BoxerAnatomy.Iron;
            }

            return RoundTang(p, GripRadius) ? BoxerAnatomy.Iron : (p.Y * p.Y) + (p.Z * p.Z) <= GripRadius * GripRadius ? BoxerAnatomy.Wood : (byte)0;
        });
    }

    /// <summary>
    /// 柄の中を通る鉄の芯（茎・タング）の断面の割合: 握りの半径に対する芯の半径。実物の片手剣・長剣のタングは握りの木の中を柄頭まで通り、
    /// 柄頭で留める（芯の太さは握りの太さの 4〜5 割）。握りの曲げの強さは断面二次モーメント（半径の 4 乗）で効くので、
    /// 木だけの断面より鉄の芯（木よりずっと引張が強い）が曲げを受け持つ。指示書 docs/plans/CODEX_BRIEF_WEAPON_TANG_AND_SPLIT.md、docs/VOXEL_WEAPONS.md「柄の芯」。
    /// </summary>
    private const float TangRadiusShare = 0.45f;

    /// <summary>握りの丸い芯: 軸（X）から半径 <paramref name="gripRadius"/> × <see cref="TangRadiusShare"/> 以内の点か。</summary>
    private static bool RoundTang(Vector3 p, float gripRadius)
    {
        var r = gripRadius * TangRadiusShare;
        return (p.Y * p.Y) + (p.Z * p.Z) <= r * r;
    }

    // ── 棍棒（人形の縮尺: 長さ 21cm、握りの太さ 1.4cm から頭の太さ 2.4cm）。刃がないので 2.5mm のセル。
    private const float ClubCell = 0.0025f;
    private const float ClubHalfLength = 0.105f;
    private const float ClubGripRadius = 0.007f;
    private const float ClubHeadRadius = 0.012f;

    /// <summary>握り（−X）から頭（+X）へ太くなる丸い木の棒。頭の端は丸い。</summary>
    private static VoxelGrid Club()
    {
        var half = new Vector3(ClubHalfLength, ClubHeadRadius, ClubHeadRadius);
        return new VoxelGrid(BoxerAnatomy.Palette, half, ClubCell, (p, _) =>
        {
            var along = (p.X + ClubHalfLength) / (2f * ClubHalfLength);
            var radius = ClubGripRadius + ((ClubHeadRadius - ClubGripRadius) * along * along);
            // 頭の端を丸める（端から頭の半径ぶんは半球）。
            var fromEnd = ClubHalfLength - p.X;
            if (fromEnd < ClubHeadRadius)
            {
                radius = MathF.Min(radius, MathF.Sqrt(MathF.Max(0f, (ClubHeadRadius * ClubHeadRadius) - ((ClubHeadRadius - fromEnd) * (ClubHeadRadius - fromEnd)))));
            }

            return (p.Y * p.Y) + (p.Z * p.Z) <= radius * radius ? BoxerAnatomy.Wood : (byte)0;
        });
    }

    // ── 棒（六尺棒。人形の縮尺 0.355: 実物の 180 cm → 64 cm、太さ 3 cm → 1.1 cm）。刃がないので棍棒と同じ 2.5 mm のセル。
    // 棒の寸法の根拠: 長さは基準体格の身長（0.355 × 1.75 m = 62 cm）ほど（六尺棒は背丈ほど）。太さは実物の縮尺だと半径 5 mm で、
    // 2.5 mm のセルで丸く見えるよう半径 6 mm（セル 5 個ぶん）にした。
    private const float StaffCell = 0.0025f;
    private const float StaffHalfLength = 0.31f;
    private const float StaffRadius = 0.006f;

    /// <summary>打つ所（<see cref="TipOf"/>）を、端の丸みの少し手前に置く距離 [m]（棍棒の頭と同じ 1 cm）。</summary>
    private const float StaffTipInset = 0.01f;

    /// <summary>
    /// 両手の間隔 [m]: 肩幅ほど（人形の肩の幅 10.7 cm（測った）。<c>docs/BOJUTSU.md</c>）。棒は両手のてこで回すので、間隔が広いほど向きの指令が楽。
    /// </summary>
    public const float StaffHandSpacing = 0.11f;

    // ── 杖（4 尺 2 寸 ≒ 128 cm、ユーザーの指定で 130 cm。人形の縮尺 0.355: 130 cm → 46 cm、太さ 2.4 cm → 0.85 cm）。
    // 実物の縮尺の半径 4.3 mm を、2.5 mm のセルで丸く見えるよう 5 mm（セル 4 個ぶん）にした（六尺棒と同じ丸め方）。
    private const float JoHalfLength = 0.23f;
    private const float JoRadius = 0.005f;

    /// <summary>丸い木の棒（六尺棒・杖）。両端は半球で丸める。太さは一定。</summary>
    private static VoxelGrid Rod(WeaponKind kind)
    {
        var (length, rodRadius) = RodOf(kind);
        var half = new Vector3(length, rodRadius, rodRadius);
        return new VoxelGrid(BoxerAnatomy.Palette, half, StaffCell, (p, _) =>
        {
            var radius = rodRadius;
            var fromEnd = length - MathF.Abs(p.X);
            if (fromEnd < rodRadius)
            {
                var back = rodRadius - fromEnd;
                radius = MathF.Sqrt(MathF.Max(0f, (rodRadius * rodRadius) - (back * back)));
            }

            return (p.Y * p.Y) + (p.Z * p.Z) <= radius * radius ? BoxerAnatomy.Wood : (byte)0;
        });
    }

    // ── 日本刀（人形の縮尺: 実物のおよそ 0.32 倍。刃長 72 cm → 23 cm、柄 27 cm → 8.5 cm、反り 1.8 cm → 0.6 cm）。1.25mm のセル。
    private const float KatanaCell = 0.00125f;
    private const float KatanaHalfLength = 0.159f;
    private const float KatanaGuardAt = -KatanaHalfLength + 0.085f;
    private const float KatanaBladeWidth = 0.0095f;
    private const float KatanaBack = 0.0025f;
    private const float KatanaSori = 0.006f;
    private const float KatanaPoint = 0.02f;
    private const float KatanaTsubaRadius = 0.013f;
    private const float KatanaTsubaHalf = 0.00125f;
    private const float KatanaGripHalfWidth = 0.0045f;
    private const float KatanaGripHalfThick = 0.0035f;

    // 茎の寸法の根拠（人形の縮尺 0.32 倍、柄の幅 9 mm・厚さ 7 mm に対して）: 実物の刀の茎は刀身の幅を保って柄頭の近くまで通り、
    // 柄木・柄巻きがまわりを覆う。根元は柄の幅の 5〜6 割（0.55）、端は少し細る（0.4）。厚さは柄の厚さ 7 mm の半分ほどの 3.7 mm（セル 3 個。刀身の峰 2.5 mm より少し厚い）。
    private const float KatanaTangRootShare = 0.55f;
    private const float KatanaTangEndShare = 0.4f;
    private const float KatanaTangHalfThick = 0.0014f;

    /// <summary>
    /// 刀身（鍔から先）: 中心線は鍔から切っ先へ放物線で +Y（峰の側）へ反る。幅（Y）は刃（−Y の縁）から峰まで、厚さ（Z）は峰で
    /// <see cref="KatanaBack"/>、刃先へ向かって 1 セルまで薄くなる楔。切っ先の手前 <see cref="KatanaPoint"/> から刃の側が峰へ向かって
    /// 丸く削られて尖る。鍔は鉄の円盤、柄は木（柄巻き）の楕円の棒。
    /// </summary>
    private static VoxelGrid Katana()
    {
        var half = new Vector3(KatanaHalfLength, KatanaTsubaRadius + KatanaSori, KatanaTsubaRadius);
        return new VoxelGrid(BoxerAnatomy.Palette, half, KatanaCell, (p, _) =>
        {
            if (p.X > KatanaGuardAt + KatanaTsubaHalf)
            {
                var t = (p.X - KatanaGuardAt) / (KatanaHalfLength - KatanaGuardAt);
                var center = KatanaSori * t * t;
                var back = center + (0.5f * KatanaBladeWidth);
                var edge = center - (0.5f * KatanaBladeWidth);
                // 切っ先: 最後の KatanaPoint で、刃の縁が峰へ向かって弧を描いて上がる。
                var toTip = KatanaHalfLength - p.X;
                if (toTip < KatanaPoint)
                {
                    var u = 1f - (toTip / KatanaPoint);
                    edge += KatanaBladeWidth * (1f - MathF.Sqrt(MathF.Max(0f, 1f - (u * u))));
                }

                if (p.Y < edge || p.Y > back)
                {
                    return 0;
                }

                var fromEdge = (p.Y - edge) / MathF.Max(1e-6f, back - edge);
                var thickness = KatanaBack * MathF.Min(1f, fromEdge * 1.6f);
                return MathF.Abs(p.Z) <= MathF.Max(0.5f * thickness, 0.5f * KatanaCell) ? BoxerAnatomy.Iron : (byte)0;
            }

            if (p.X >= KatanaGuardAt - KatanaTsubaHalf)
            {
                return (p.Y * p.Y) + (p.Z * p.Z) <= KatanaTsubaRadius * KatanaTsubaRadius ? BoxerAnatomy.Iron : (byte)0;
            }

            // 茎（なかご）: 鍔から柄頭の端まで通る鉄の平たい板。根元（鍔側）は柄の幅の KatanaTangRootShare、柄頭の端へ向かって
            // KatanaTangEndShare まで細る。厚さは 2 セル以上（KatanaTangHalfThick）。
            var along = (p.X - (-KatanaHalfLength)) / (KatanaGuardAt - KatanaTsubaHalf + KatanaHalfLength);
            var tangHalfWidth = KatanaGripHalfWidth * (KatanaTangEndShare + ((KatanaTangRootShare - KatanaTangEndShare) * MathF.Min(1f, along)));
            if (MathF.Abs(p.Y) <= tangHalfWidth && MathF.Abs(p.Z) <= KatanaTangHalfThick)
            {
                return BoxerAnatomy.Iron;
            }

            var ey = p.Y / KatanaGripHalfWidth;
            var ez = p.Z / KatanaGripHalfThick;
            return (ey * ey) + (ez * ez) <= 1f ? BoxerAnatomy.Wood : (byte)0;
        });
    }

    // ── ロングソード（人形の縮尺 0.32 倍: 刃長 90 cm → 29 cm、鍔 22 cm → 7 cm、握り 20 cm → 6.5 cm）。
    private const float LongCell = 0.00125f;
    private const float LongHalfLength = 0.19f;
    private const float LongPommelRadius = 0.0075f;
    private const float LongPommelAt = -LongHalfLength + LongPommelRadius;
    private const float LongGuardAt = LongPommelAt + LongPommelRadius + 0.065f;
    private const float LongGuardHalfWidth = 0.035f;
    private const float LongGuardHalfThick = 0.0016f;
    private const float LongBladeBase = 0.008f;
    private const float LongBladeTip = 0.004f;
    private const float LongSpine = 0.002f;
    private const float LongPoint = 0.03f;
    private const float LongGripRadius = 0.0045f;

    /// <summary>
    /// 刀身（鍔から先）: 幅は根元の <see cref="LongBladeBase"/> から先の <see cref="LongBladeTip"/> へ細り、切っ先の手前 <see cref="LongPoint"/>
    /// から 0 へ尖る。断面は菱形（中央の鎬で <see cref="LongSpine"/>、両刃の縁は 1 セル）。鍔は鉄の棒、握りは木、柄頭は鉄の玉。
    /// </summary>
    private static VoxelGrid Longsword()
    {
        var half = new Vector3(LongHalfLength, LongGuardHalfWidth, 0.0045f);
        return new VoxelGrid(BoxerAnatomy.Palette, half, LongCell, (p, _) =>
        {
            if (p.X > LongGuardAt + LongGuardHalfThick)
            {
                var t = (p.X - LongGuardAt) / (LongHalfLength - LongGuardAt);
                var width = LongBladeBase + ((LongBladeTip - LongBladeBase) * t);
                width *= MathF.Min(1f, (LongHalfLength - p.X) / LongPoint);
                var across = MathF.Abs(p.Y);
                if (across > width)
                {
                    return 0;
                }

                var thickness = LongSpine * (1f - (across / MathF.Max(width, 1e-6f)));
                return MathF.Abs(p.Z) <= MathF.Max(0.5f * thickness, 0.5f * LongCell) ? BoxerAnatomy.Iron : (byte)0;
            }

            if (p.X >= LongGuardAt - LongGuardHalfThick)
            {
                return MathF.Abs(p.Y) <= LongGuardHalfWidth && MathF.Abs(p.Z) <= 0.0016f ? BoxerAnatomy.Iron : (byte)0;
            }

            if (Vector3.Distance(p, new Vector3(LongPommelAt, 0f, 0f)) <= LongPommelRadius)
            {
                return BoxerAnatomy.Iron;
            }

            return RoundTang(p, LongGripRadius) ? BoxerAnatomy.Iron : (p.Y * p.Y) + (p.Z * p.Z) <= LongGripRadius * LongGripRadius ? BoxerAnatomy.Wood : (byte)0;
        });
    }

    // ── ボクシンググローブ（人形の縮尺: 半径 2.4 cm = 試合のグローブの球の大きさ）。4 mm のセル（刃の無い広い面なので粗くてよい。
    // 2 mm では、構えて触れ合っている間の接触の計算が重すぎた）。
    private const float GloveRadius = 0.024f;
    private const float GloveCell = 0.004f;
    private const float GloveLeather = 0.004f;
    private const float GloveFist = 0.012f;

    /// <summary>グローブ: 半径 <see cref="GloveRadius"/> の球。外側 <see cref="GloveLeather"/> は革、その内は詰め物、芯（半径 <see cref="GloveFist"/>）は拳（骨）。</summary>
    public static VoxelGrid Glove(byte leather)
    {
        var half = new Vector3(GloveRadius);
        return new VoxelGrid(BoxerAnatomy.Palette, half, GloveCell, (p, _) =>
        {
            var r = p.Length();
            return r > GloveRadius ? (byte)0 : r > GloveRadius - GloveLeather ? leather : r > GloveFist ? BoxerAnatomy.Padding : BoxerAnatomy.Bone;
        });
    }

    // ── 手にはめる物（グローブ以外）。形は「手の座標」で書く: 原点は手の格子の中心（掌の真ん中。HandSkeleton と同じ座標）、+X は拳を突き出す向き、
    // Z は拳頭の並ぶ向き（親指の側から小指の側）、Y は手の甲と掌を結ぶ向き。中の手は素手と同じ手のモデル（5 mm の格子: HandSkeleton・BareHandState）で、
    // 物の格子（2 mm、刃の薄いカタールは 1.25 mm）とは別の格子として、同じ剛体に重ねる（AddHand）。物のセルは手のセルを避けて置く（二重に固くならない）。
    // 物の寸法は、中の手の寸法（手の格子から測る）を基準にして書く: 手が大きくなれば物もそれに合わせて大きくなる。
    // 人形の縮尺はおよそ 0.5 倍（人の拳の幅 9 cm → 拳の幅 4〜5 cm）。物の太さ・長さは、人の物をその縮尺にし、手の寸法に対する比で決めた。

    /// <summary>素手の拳の半径 [m]（グローブの半径 2.4 cm × 0.55。<see cref="CoverRadiusOf"/> の素手の拳の広がりの目安。形は <see cref="HandSkeleton"/> が決める）。</summary>
    private const float FistRadius = 0.013f;

    /// <summary>メリケンサック・籠手・トンファー・盾のセル [m]（2 mm: メリケンサックの突起が 1〜2 セルで形になる大きさ）。</summary>
    private const float WornCell = 0.002f;

    /// <summary>カタールのセル [m]（刃の薄さを形で表すため、剣と同じ 1.25 mm）。</summary>
    private const float KatarCell = 0.00125f;

    /// <summary>指のピッチ [m]（指 1 本の幅 = 2 セル。<see cref="HandSkeleton"/> の指は横に 2 セルおき）。</summary>
    private const float FingerPitch = 0.01f;

    // メリケンサック（人の: 幅 9 cm、前の棒の高さ 1.3 cm・厚さ 1.2 cm → 手の寸法に対する比）。素手の正拳のまま、拳頭の前の面に棒が接する。
    private const float DusterBar = 0.004f;         // 前の棒の厚さ（X）
    private const float DusterBarMargin = 0.002f;   // 棒が拳頭の列の高さの外へ広がる分
    private const float DusterBoss = 0.004f;        // 拳頭ごとの突起の半径（前の棒から前へ出る）
    private const float DusterEndPlate = 0.003f;    // 両端の板の厚さ（Z）
    private const float DusterEndGap = 0.001f;      // 両端の板と拳の幅の間
    private const float DusterEndBack = 0.005f;     // 両端の板の後ろの端（X。指の付け根の少し手前）

    // カタール（人の: 刃 25 cm・根元の幅 5 cm、枠 15 cm → 刃の長さは今まで通り 7 cm、根元の幅は手の幅に対する人の比 0.58 × 手の半分）。
    // 手は横木（握り棒）を握り（GripPose）、枠の面は握り棒の高さ（HandSkeleton.GripCenter.Y）。
    private const float KatarGripRadius = 0.004f;      // 握る横木の半径（直径 8 mm）。手のセル（5 mm）の対角の半分 3.54 mm より太くないと、棒が手のセルを空けられない（4 つのセルの角に棒の軸が来る）
    private const float KatarBladeGap = 0.006f;        // 拳頭の前の面から刃の根元まで
    private const float KatarBladeLength = 0.07f;      // 刃の根元から切っ先まで
    private const float KatarBladeHalfWidth = 0.0145f; // 刃の根元の幅の半分（Z）
    private const float KatarPoint = 0.02f;            // 切っ先: 最後の 2 cm で 0 へ尖る
    private const float KatarSpine = 0.003f;           // 鎬の厚さ（Y）
    private const float KatarBarGap = 0.002f;          // 手の幅の外から、両脇の棒の内の面まで
    private const float KatarBarThick = 0.0025f;       // 枠の棒の太さ
    private const float KatarBarBack = 0.024f;         // 両脇の棒の後ろの端（腕の先の握る所から後ろ。前腕に沿う）

    // 籠手（人の: 板 2 mm 前後の鋼を拳の上に、その下に詰め物 → 手の表面から 2 mm の詰め物、その外 2.5 mm の鉄・革の殻）。
    // 殻は手の形に沿う（手のセルからの距離で決める）。
    private const float GauntletPadding = 0.002f;     // 詰め物の厚さ（手の外の面から）
    private const float GauntletShell = 0.0025f;      // 鉄の板・革の厚さ
    private const float GauntletPlateFrom = 0f;       // 鉄の板が覆う所（手の座標の X がこれより前: 拳頭・指・甲の前半。それより後ろは革）
    private const float GauntletCuffLength = 0.017f;  // 手首の鉄の輪の長さ
    private const float GauntletCuffBack = 0.018f;    // 輪の後ろの端（腕の先の握る所から後ろ）
    private const float GauntletCuffInner = 0.017f;   // 輪の内の半径（前腕の箱の太さ 32 mm の外）
    private const float GauntletCuffOuter = 0.0195f;  // 輪の外の半径

    // トンファー（人の: 全長 50 cm 前後・握りの柄 12 cm 前後・棒の太さ 3 cm 前後）。棒は手の外側（Z の正の側）に前腕と平行に置き、
    // 後ろの端は肘を少し越える。前の端は拳の前へ。握りの柄は手が握る（GripPose）。
    private const float TonfaStickRadius = 0.008f;    // 棒の半径（直径 16 mm = 手の幅 5 cm に対する人の比 0.33）
    private const float TonfaStickGap = 0.001f;       // 手の幅の外から棒の内の面まで
    private const float TonfaStickLift = 0.006f;      // 棒の中心の高さ: 握る柄の軸より甲の側へ（柄と棒が重なって一つにつながる）
    private const float TonfaHandleRadius = 0.004f;   // 握りの柄の半径（直径 8 mm。手のセルの対角の半分より太く）
    private const float TonfaBack = 0.11f;            // 棒の後ろの端（腕の先の握る所から後ろ。肘を少し越える）
    private const float TonfaFrontBeyond = 0.017f;    // 棒の前の端（拳の前の面から前へ）

    // 盾（人の: 丸盾の直径 60 cm・板の厚さ 1.5 cm 前後 → 人形の縮尺で直径 16 cm・板の厚さ 4 mm）。椀（ボス）は手の形に沿う殻（手のセルからの
    // 距離）で前半分を覆い、板は手の中心の面（X が ±板の厚さ）に広がる。手は鉄の握り棒を握る（GripPose）。
    private const float ShieldRadius = 0.08f;         // 板の半径（直径 16 cm）
    private const float ShieldPlateHalf = 0.002f;     // 板の厚さの半分（厚さ 4 mm）
    private const float ShieldRim = 0.002f;           // 縁を巻く鉄の輪の幅（1 セル）
    private const float ShieldBossClear = 0.004f;     // 椀の内側と手の間の隙間
    private const float ShieldBossShell = 0.003f;     // 椀の殻の厚さ（1.5 セル: 1 セルだと曲面で穴が開く）
    private const float ShieldGripRadius = 0.004f;    // 握り棒の半径（直径 8 mm。手のセルの対角の半分より太く）

    /// <summary>手にはめる物の中の手: 手の姿勢と、握る棒（素手の正拳なら null）。</summary>
    private readonly record struct WornHandSpec(HandPose Pose, Func<Vector3, bool>? Held);

    private static WornHandSpec? WornHandOf(WeaponKind kind) => kind switch
    {
        WeaponKind.KnuckleDuster or WeaponKind.Gauntlet => new WornHandSpec(HandSkeleton.PoseOf(HandShape.Seiken), null),
        WeaponKind.Katar => new WornHandSpec(HandSkeleton.GripPose, HandSkeleton.HeldBar(KatarGripRadius)),
        WeaponKind.Tonfa => new WornHandSpec(HandSkeleton.GripPose, HandSkeleton.HeldBar(TonfaHandleRadius)),
        WeaponKind.Shield => new WornHandSpec(HandSkeleton.GripPose, HandSkeleton.HeldBar(ShieldGripRadius)),
        _ => null,
    };

    /// <summary>
    /// 手にはめる物の中の手を、手の座標のまま測る鋳型（傷の無い手の格子。物の形を手の寸法・手の形に合わせて書くための物差し）。
    /// </summary>
    private sealed class HandTemplate
    {
        private readonly VoxelGrid _grid;

        public HandTemplate(WornHandSpec spec)
        {
            Held = spec.Held;
            _ = new BareHandState().Placed(spec.Pose, out _grid, spec.Held);
            var bounds = _grid.OccupiedBounds()!.Value;
            Min = bounds.Center - bounds.HalfExtents;
            Max = bounds.Center + bounds.HalfExtents;
            // 最も前の層（拳頭の前の面）の高さの範囲。
            var front = float.MinValue;
            for (var i = 0; i < _grid.CellCount; i++)
            {
                if (_grid.MaterialAt(i) != 0)
                {
                    var (x, y, z) = _grid.Coordinates(i);
                    front = MathF.Max(front, _grid.CellCenter(x, y, z).X);
                }
            }

            float low = float.MaxValue, high = float.MinValue;
            for (var i = 0; i < _grid.CellCount; i++)
            {
                if (_grid.MaterialAt(i) != 0)
                {
                    var (x, y, z) = _grid.Coordinates(i);
                    var c = _grid.CellCenter(x, y, z);
                    if (c.X >= front - (0.5f * HandSkeleton.Cell))
                    {
                        low = MathF.Min(low, c.Y - (0.5f * HandSkeleton.Cell));
                        high = MathF.Max(high, c.Y + (0.5f * HandSkeleton.Cell));
                    }
                }
            }

            FrontLow = low;
            FrontHigh = high;
        }

        /// <summary>握る棒（素手の正拳なら null）。</summary>
        public Func<Vector3, bool>? Held { get; }

        /// <summary>手のセルを囲む箱（手の座標の最小・最大の面）。</summary>
        public Vector3 Min { get; }

        public Vector3 Max { get; }

        /// <summary>最も前の層（拳頭の前の面）の、高さ（Y）の下端と上端。</summary>
        public float FrontLow { get; }

        public float FrontHigh { get; }

        public float FrontMiddle => 0.5f * (FrontLow + FrontHigh);

        /// <summary>手の幅の半分（Z の大きい方の端）。</summary>
        public float HalfZ => MathF.Max(-Min.Z, Max.Z);

        public bool Occupies(Vector3 p) => _grid.FilledCellAt(p) >= 0;

        /// <summary>点 <paramref name="p"/> から、手のセルの箱までの最短距離 [m]（手の中は 0。2 セルより遠い所は 1 m）。</summary>
        public float Distance(Vector3 p)
        {
            const float Reach = 0.011f;
            if (p.X < Min.X - Reach || p.Y < Min.Y - Reach || p.Z < Min.Z - Reach || p.X > Max.X + Reach || p.Y > Max.Y + Reach || p.Z > Max.Z + Reach)
            {
                return 1f;
            }

            var f = (p + _grid.HalfExtents) / _grid.CellSize;
            var (cx, cy, cz) = ((int)MathF.Floor(f.X), (int)MathF.Floor(f.Y), (int)MathF.Floor(f.Z));
            var best = 1f;
            var half = _grid.CellSize * 0.5f;
            for (var dz = -2; dz <= 2; dz++)
            {
                for (var dy = -2; dy <= 2; dy++)
                {
                    for (var dx = -2; dx <= 2; dx++)
                    {
                        var (x, y, z) = (cx + dx, cy + dy, cz + dz);
                        if (x < 0 || y < 0 || z < 0 || x >= _grid.Dimensions.X || y >= _grid.Dimensions.Y || z >= _grid.Dimensions.Z || _grid.MaterialAt(_grid.Index(x, y, z)) == 0)
                        {
                            continue;
                        }

                        var gap = Vector3.Max(Vector3.Abs(p - _grid.CellCenter(x, y, z)) - half, Vector3.Zero);
                        best = MathF.Min(best, gap.Length());
                    }
                }
            }

            return best;
        }
    }

    private static readonly Lazy<HandTemplate?>[] HandTemplates =
        Enum.GetValues<WeaponKind>().Select(k => new Lazy<HandTemplate?>(() => WornHandOf(k) is { } spec ? new HandTemplate(spec) : null)).ToArray();

    private static HandTemplate TemplateOf(WeaponKind kind)
        => HandTemplates[(int)kind].Value ?? throw new ArgumentException($"{kind} は手を中に持つ手にはめる物ではありません。", nameof(kind));

    /// <summary>
    /// その物が手の前に広げる面の半径 [m]（腕の軸に直角な面の、手の中心から見える最大の広がり）。守りの目安: 盾の大きさ。
    /// 手にはめる物は形の箱の Y・Z の広がり、グローブはその球の半径。武器は握りから離れた面を持たないので 0。
    /// </summary>
    public static float CoverRadiusOf(WeaponKind kind) => kind switch
    {
        WeaponKind.Glove => GloveRadius,
        WeaponKind.Fist => FistRadius,
        WeaponKind.KnuckleDuster or WeaponKind.Katar or WeaponKind.Gauntlet or WeaponKind.Tonfa or WeaponKind.Shield
            => MathF.Max(WornBoundsOf(kind).Half.Y, WornBoundsOf(kind).Half.Z),
        _ => 0f,
    };

    /// <summary>手にはめる物の形（物の格子）を囲む箱（手の座標）の中心と半分の大きさ。</summary>
    private static (Vector3 Center, Vector3 Half) WornBoundsOf(WeaponKind kind, bool mirrored = false)
    {
        var t = TemplateOf(kind);
        var (from, to) = kind switch
        {
            WeaponKind.KnuckleDuster => (
                new Vector3(DusterEndBack, t.FrontLow - DusterBarMargin, -(t.HalfZ + DusterEndGap + DusterEndPlate)),
                new Vector3(t.Max.X + DusterBar + DusterBoss, t.FrontHigh + DusterBarMargin, t.HalfZ + DusterEndGap + DusterEndPlate)),
            WeaponKind.Katar => (
                new Vector3(HandSkeleton.Wrist.X - KatarBarBack, HandSkeleton.GripCenter.Y - KatarGripRadius, -(t.HalfZ + KatarBarGap + KatarBarThick)),
                new Vector3(t.Max.X + KatarBladeGap + KatarBladeLength, HandSkeleton.GripCenter.Y + KatarGripRadius, t.HalfZ + KatarBarGap + KatarBarThick)),
            WeaponKind.Gauntlet => (
                new Vector3(HandSkeleton.Wrist.X - GauntletCuffBack, MathF.Min(t.Min.Y - GauntletPadding - GauntletShell, HandSkeleton.Wrist.Y - GauntletCuffOuter), t.Min.Z - GauntletPadding - GauntletShell),
                new Vector3(t.Max.X + GauntletPadding + GauntletShell, MathF.Max(t.Max.Y + GauntletPadding + GauntletShell, HandSkeleton.Wrist.Y + GauntletCuffOuter), t.Max.Z + GauntletPadding + GauntletShell)),
            WeaponKind.Tonfa => (
                new Vector3(HandSkeleton.Wrist.X - TonfaBack, MathF.Min(StickY - TonfaStickRadius, HandSkeleton.GripCenter.Y - TonfaHandleRadius), t.Min.Z - TonfaStickGap),
                new Vector3(t.Max.X + TonfaFrontBeyond, MathF.Max(StickY + TonfaStickRadius, HandSkeleton.GripCenter.Y + TonfaHandleRadius), StickZ(t) + TonfaStickRadius)),
            WeaponKind.Shield => (
                new Vector3(-ShieldPlateHalf, PlateY(t) - ShieldRadius, -ShieldRadius),
                new Vector3(t.Max.X + ShieldBossClear + ShieldBossShell, PlateY(t) + ShieldRadius, ShieldRadius)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "手の座標で書く手にはめる物ではありません。"),
        };
        // 端のセルの中心が形の端に乗るよう、半セル広げる。
        var pad = new Vector3(0.5f * (kind == WeaponKind.Katar ? KatarCell : WornCell));
        var center = (from + to) * 0.5f;
        return (mirrored ? center with { Z = -center.Z } : center, ((to - from) * 0.5f) + pad);
    }

    private static float StickY => HandSkeleton.GripCenter.Y + TonfaStickLift;

    private static float StickZ(HandTemplate t) => t.Max.Z + TonfaStickGap + TonfaStickRadius;

    /// <summary>盾の板の中心の高さ（手の高さの真ん中）。</summary>
    private static float PlateY(HandTemplate t) => 0.5f * (t.Min.Y + t.Max.Y);

    /// <summary>手にはめる物の打つ所（手の座標）。<see cref="TipOf"/> はこれを物の格子の座標へ直す。</summary>
    private static Vector3 WornTipInHand(WeaponKind kind)
    {
        var t = TemplateOf(kind);
        return kind switch
        {
            WeaponKind.KnuckleDuster => new Vector3(t.Max.X + DusterBar + DusterBoss, t.FrontMiddle, -0.5f * FingerPitch), // 中指の拳頭の突起の先
            WeaponKind.Katar => new Vector3(t.Max.X + KatarBladeGap + KatarBladeLength, HandSkeleton.GripCenter.Y, 0f),
            WeaponKind.Gauntlet => new Vector3(t.Max.X + GauntletPadding + GauntletShell, t.FrontMiddle, 0f),
            WeaponKind.Tonfa => new Vector3(t.Max.X + TonfaFrontBeyond, StickY, StickZ(t)), // 棒の前の端の先
            WeaponKind.Shield => new Vector3(t.Max.X + ShieldBossClear + ShieldBossShell, t.FrontMiddle, 0f), // 椀（ボス）の頂き（盾で押す・打つ所）
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "手の座標で書く手にはめる物ではありません。"),
        };
    }

    /// <summary>物の格子の中での、中の手の格子の中心（手の座標の原点が、物の格子のローカルのどこか）。</summary>
    private static Vector3 HandCenterLocal(WeaponKind kind, bool mirrored) => -WornBoundsOf(kind, mirrored).Center;

    /// <summary>手にはめる物の、手の座標の原点（中の手の格子の中心）の、武器のローカルでの位置。物のセルを手の座標で読むときの基準。</summary>
    internal static Vector3 HandOriginOf(WeaponKind kind, bool mirrored = false) => HandCenterLocal(kind, mirrored);

    private static VoxelGrid Worn(WeaponKind kind, float cell, Func<Vector3, byte> fill, bool mirrored = false)
    {
        var (center, half) = WornBoundsOf(kind, mirrored);
        var hand = TemplateOf(kind);
        return new VoxelGrid(BoxerAnatomy.Palette, half, cell, (p, _) =>
        {
            var f = p + center;
            // 手のセルの所は手の格子が持つ（物のセルを重ねない）。
            return hand.Occupies(f) ? (byte)0 : fill(mirrored ? f with { Z = -f.Z } : f);
        });
    }

    /// <summary>
    /// メリケンサック: 素手の正拳の拳頭の前の面に接する鉄の棒（幅は拳の幅 + 両端の板）と、拳頭ごとの 4 つの丸い突起。棒の両端の板が拳の脇を
    /// 後ろへ回り込む。
    /// </summary>
    private static byte KnuckleDusterAt(Vector3 p)
    {
        var t = TemplateOf(WeaponKind.KnuckleDuster);
        var across = MathF.Abs(p.Z);
        var width = t.HalfZ + DusterEndGap + DusterEndPlate;
        var front = t.Max.X;
        var inBar = p.Y >= t.FrontLow - DusterBarMargin && p.Y <= t.FrontHigh + DusterBarMargin;
        // 前の棒。
        if (inBar && p.X >= front && p.X <= front + DusterBar && across <= width)
        {
            return BoxerAnatomy.Iron;
        }

        // 拳頭の前の 4 つの突起（拳頭の間隔は指のピッチ）。
        for (var k = 0; k < 4; k++)
        {
            var z = (k - 1.5f) * FingerPitch;
            if (p.X >= front && Vector3.Distance(p, new Vector3(front + DusterBar, t.FrontMiddle, z)) <= DusterBoss)
            {
                return BoxerAnatomy.Iron;
            }
        }

        // 両端の板（前の棒から後ろへ、拳の脇を回る）。
        return inBar && across >= width - DusterEndPlate && across <= width && p.X >= DusterEndBack && p.X <= front + DusterBar ? BoxerAnatomy.Iron : (byte)0;
    }

    /// <summary>
    /// カタール: 手が握る横木（Z に沿う鉄の棒）、手の両脇から前腕へ沿う 2 本の棒、手の前の横木（刃の根元）、そこから +X へまっすぐ
    /// 伸びる刃。枠・刃は握り棒の高さの面にある。刃の幅（Z）は根元の <see cref="KatarBladeHalfWidth"/> から、切っ先の手前 <see cref="KatarPoint"/> までに半分へ細り、そこから 0 へ
    /// 尖る。断面は剣と同じ菱形（中央の鎬で <see cref="KatarSpine"/>、刃先へ薄く、最も薄い所で 1 セル）。
    /// </summary>
    private static byte KatarAt(Vector3 p)
    {
        var t = TemplateOf(WeaponKind.Katar);
        var bladeFrom = t.Max.X + KatarBladeGap;
        var tip = bladeFrom + KatarBladeLength;
        var barZ = t.HalfZ + KatarBarGap;
        var level = p.Y - HandSkeleton.GripCenter.Y;
        var across = MathF.Abs(p.Z);
        if (p.X > bladeFrom)
        {
            var toTip = tip - p.X;
            var along = (p.X - bladeFrom) / (KatarBladeLength - KatarPoint);
            var width = toTip >= KatarPoint
                ? KatarBladeHalfWidth * (1f - (0.5f * along))
                : 0.5f * KatarBladeHalfWidth * MathF.Max(0f, toTip / KatarPoint);
            if (across > width)
            {
                return 0;
            }

            var thickness = KatarSpine * (1f - (across / MathF.Max(width, 1e-6f)));
            return MathF.Abs(level) <= MathF.Max(0.5f * thickness, 0.5f * KatarCell) ? BoxerAnatomy.Iron : (byte)0;
        }

        var inFrame = MathF.Abs(level) <= KatarBarThick * 0.6f;
        // 手の前の横木（刃の根元）。
        if (inFrame && p.X >= bladeFrom - (2f * KatarBarThick) && across <= barZ + KatarBarThick)
        {
            return BoxerAnatomy.Iron;
        }

        // 手の両脇の棒。
        if (inFrame && across >= barZ && across <= barZ + KatarBarThick && p.X >= HandSkeleton.Wrist.X - KatarBarBack)
        {
            return BoxerAnatomy.Iron;
        }

        // 手が握る横木（手の中を Z に沿って通り、両脇の棒へつながる）。
        return t.Held!(p) && across <= barZ + KatarBarThick ? BoxerAnatomy.Iron : (byte)0;
    }

    /// <summary>
    /// トンファー: 手が握る横の柄（Z に沿い、手の外側の棒へつながる）と、その端から前腕に沿って X に伸びる木の丸い棒
    /// （後ろは肘を少し越え、前は拳の少し前へ出る。両端は丸める）。木の材質は棍棒・棒と同じ。
    /// </summary>
    private static byte TonfaAt(Vector3 p)
    {
        var t = TemplateOf(WeaponKind.Tonfa);
        var stickZ = StickZ(t);
        var back = HandSkeleton.Wrist.X - TonfaBack;
        var front = t.Max.X + TonfaFrontBeyond;
        var dz = p.Z - stickZ;
        var radial = MathF.Sqrt(((p.Y - StickY) * (p.Y - StickY)) + (dz * dz));
        if (p.X >= back && p.X <= front && radial <= TonfaStickRadius)
        {
            // 両端は半球に丸める（角が立つと、受けのたびに端のセルだけが削れる）。
            var overFront = p.X - (front - TonfaStickRadius);
            var overBack = (back + TonfaStickRadius) - p.X;
            var over = MathF.Max(overFront, overBack);
            if (over <= 0f || (radial * radial) + (over * over) <= TonfaStickRadius * TonfaStickRadius)
            {
                return BoxerAnatomy.Wood;
            }
        }

        return p.Z >= t.Min.Z - TonfaStickGap && p.Z <= stickZ && t.Held!(p) ? BoxerAnatomy.Wood : (byte)0;
    }

    /// <summary>
    /// 盾: 手が握る鉄の握り棒（Z に沿う）、手の前半分を覆う鉄の椀（ボス: 手のセルから <see cref="ShieldBossClear"/> 離れた、厚さ
    /// <see cref="ShieldBossShell"/> の殻。手の形に沿う）、手の中心の面（X が ±<see cref="ShieldPlateHalf"/>）に広がる丸い木の板（椀の外から <see cref="ShieldRadius"/> まで）、
    /// 板の縁を巻く鉄の輪。板は椀の中に穴を開けてあり、腕は後ろから椀の中へ入って握りを握る。
    /// </summary>
    private static byte ShieldAt(Vector3 p)
    {
        var t = TemplateOf(WeaponKind.Shield);
        var radial = MathF.Sqrt(((p.Y - PlateY(t)) * (p.Y - PlateY(t))) + (p.Z * p.Z));
        // 握り棒（手が握る。椀の殻の両端へつながる）。
        if (MathF.Abs(p.Z) <= t.HalfZ + ShieldBossClear + (0.5f * ShieldBossShell) && t.Held!(p))
        {
            return BoxerAnatomy.Iron;
        }

        var inPlate = MathF.Abs(p.X) <= ShieldPlateHalf && radial <= ShieldRadius;
        if (!inPlate && p.X < -ShieldPlateHalf)
        {
            return 0;
        }

        var d = t.Distance(p);
        // 椀（前の半球の殻）。
        if (p.X >= -ShieldPlateHalf && d > ShieldBossClear && d <= ShieldBossClear + ShieldBossShell)
        {
            return BoxerAnatomy.Iron;
        }

        // 板と縁の輪（手の中心の面の薄い層。椀の内側は穴）。
        return inPlate && d > ShieldBossClear ? radial >= ShieldRadius - ShieldRim ? BoxerAnatomy.Iron : BoxerAnatomy.Wood : (byte)0;
    }

    /// <summary>
    /// 籠手: 素手の正拳の上に詰め物（手の外の面から <see cref="GauntletPadding"/>）、その上の殻（厚さ <see cref="GauntletShell"/>）は、手の前の側
    /// （X が <see cref="GauntletPlateFrom"/> より前: 拳頭・指・甲の前半）が鉄の板、後ろ（掌の付け根・手首の側）が革。手首に鉄の輪。殻は手の形に沿う。
    /// </summary>
    private static byte GauntletAt(Vector3 p, byte leather)
    {
        var ring = MathF.Sqrt(((p.Y - HandSkeleton.Wrist.Y) * (p.Y - HandSkeleton.Wrist.Y)) + (p.Z * p.Z));
        var cuffBack = HandSkeleton.Wrist.X - GauntletCuffBack;
        if (p.X >= cuffBack && p.X <= cuffBack + GauntletCuffLength && ring >= GauntletCuffInner && ring <= GauntletCuffOuter)
        {
            return BoxerAnatomy.Iron;
        }

        var d = TemplateOf(WeaponKind.Gauntlet).Distance(p);
        if (d <= GauntletPadding)
        {
            return BoxerAnatomy.Padding;
        }

        return d <= GauntletPadding + GauntletShell ? p.X >= GauntletPlateFrom ? BoxerAnatomy.Iron : leather : (byte)0;
    }
}
