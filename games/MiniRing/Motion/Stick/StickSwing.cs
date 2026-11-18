using System.Globalization;
using System.Numerics;
using MiniRing.Characters;
using MiniRing.Weapons;

namespace MiniRing.Motion.Stick;

/// <summary>
/// 振りの断面（docs/STICK_MOTION.md §11）: 肩を通る平面と、斬る向き。座標は棒人形の世界（+Z 前・+Y 上・+X はファイターの左）。
/// 右手で持つので、右 = −X。<see cref="Direction"/> は断面の中で、剣先が体の前を通る所の進む向き。
/// </summary>
/// <param name="Name">英語の小文字の名前（コマンドの引数・ファイル名）。</param>
/// <param name="Label">日本語の名前。</param>
/// <param name="Normal">断面の法線（単位ベクトル）。</param>
/// <param name="Direction">斬る向き（断面の中の単位ベクトル）。</param>
public sealed record SwingPlane(string Name, string Label, Vector3 Normal, Vector3 Direction)
{
    private static Vector3 Unit(float x, float y, float z) => Vector3.Normalize(new Vector3(x, y, z));

    /// <summary>
    /// 6 つの断面。縦の面は前後と上下（法線 X）、水平は法線が上、斜めの面は前と斜め（右上 ⇔ 左下など）を含む面。
    /// 斜めの 4 本は 2 つの面（袈裟と左の斬り上げ、逆袈裟と右の斬り上げ）を、向きを逆にして使う。
    /// </summary>
    public static IReadOnlyList<SwingPlane> All { get; } =
    [
        new("vertical", "真っ向", Unit(1, 0, 0), Unit(0, -1, 0)),
        new("horizontal", "水平", Unit(0, 1, 0), Unit(1, 0, 0)),
        new("kesa", "袈裟（右上→左下）", Unit(1, 1, 0), Unit(1, -1, 0)),
        new("gyaku-kesa", "逆袈裟（左上→右下）", Unit(1, -1, 0), Unit(-1, -1, 0)),
        new("kiriage", "右斬り上げ（右下→左上）", Unit(1, -1, 0), Unit(1, 1, 0)),
        new("gyaku-kiriage", "左斬り上げ（左下→右上）", Unit(1, 1, 0), Unit(-1, 1, 0)),
    ];

    public static SwingPlane Get(string name)
        => All.FirstOrDefault(p => p.Name == name) ?? throw new ArgumentException($"知らない断面 {name}（{string.Join(", ", All.Select(p => p.Name))}）。", nameof(name));
}

/// <summary>点の重み（探索の点 = 速さ × w − 滑らかさ × w − 断面 × w − 刃筋 × w − 向き × w）。各項は「悪い」で 1 になるよう割ってある。<c>Whole</c> = 断面の外れに数える、構えから振り抜くまで全体（振りかぶりを含む）の割合（0 = 主な 1 本の動きだけ。0.5 = 両者の平均。振りかぶりが断面から大きく外れた振りを選ばないため、2026-11-18 の 1 回目の結果を見て足した）。</summary>
public sealed record SwingWeights(float Speed = 3f, float Smooth = 1f, float Plane = 1f, float Edge = 1f, float Direction = 1f, float Whole = 0.5f)
{
    /// <summary>"speed:1,smooth:2,..." を読む（書かなかった項は既定）。</summary>
    public static SwingWeights Parse(IEnumerable<string> items)
    {
        var w = new SwingWeights();
        foreach (var item in items)
        {
            var parts = item.Split(':');
            if (parts.Length != 2 || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < 0f)
            {
                throw new ArgumentException($"重み '{item}' は name:value（0 以上）で書いてください。", nameof(items));
            }

            w = parts[0] switch
            {
                "speed" => w with { Speed = value },
                "smooth" => w with { Smooth = value },
                "plane" => w with { Plane = value },
                "edge" => w with { Edge = value },
                "dir" or "direction" => w with { Direction = value },
                "whole" => w with { Whole = value },
                _ => throw new ArgumentException($"知らない重み {parts[0]}（speed, smooth, plane, edge, dir, whole）。", nameof(items)),
            };
        }

        return w;
    }
}

/// <summary>
/// 剣をがっちり握った腕の関節の範囲 [rad]（2026-11-18、ユーザーの指定: 手首・肘のねじれ・肩を狭める）。キーはこの中へ収め、キーの間の補間がはみ出した分は失格の罰（<see cref="SwingEval.RangeExcess"/>）。
/// 構えそのものがはみ出している量までは許す（構えは振りの始まりなので）。値は人の可動域の文献値（AAOS の平均）を、握りで狭まる分だけ縮めた目安。
/// </summary>
/// <param name="WristFlex">手首の屈曲（正 = 掌屈）。素手は掌屈 80°・背屈 70°。柄を強く握ると、握力が保てるのは中間 ±30° ほど（握力は背屈 30° 付近で最大、掌屈で急に落ちる）。</param>
/// <param name="WristDeviation">手首の橈尺屈（正 = 橈屈）。素手は橈屈 20°・尺屈 30°。握ると尺屈 25°・橈屈 10° ほど（剣先を前へ倒すのは尺屈の側）。</param>
/// <param name="ForearmRoll">前腕の回内・回外（肘より先のねじれ。剣の軸まわり）。素手は各 80°。握った剣の刃の向きを保つので ±45°。</param>
/// <param name="Elbow">肘の曲げ（0 = 伸ばし切り。過伸展なし）。素手は 0〜145°。</param>
/// <param name="ShoulderTwist">肩のねじれ（上腕の内旋・外旋。T ポーズの腕の軸まわり）。素手は内旋 70°・外旋 90° ほど。±75°。</param>
/// <param name="ShoulderBack">上腕の向きの、後ろ向きの成分の上限（sin）。伸展・水平外転は後ろへ 45° まで（sin 45° ≈ 0.7）。</param>
/// <param name="ShoulderAcross">上腕の向きの、反対の脇への成分の上限（sin）。水平内転は正面から 40° 先まで（sin 40° ≈ 0.64）。</param>
public sealed record SwingLimits(
    float WristFlexLow = -30f * SwingLimits.Deg, float WristFlexHigh = 30f * SwingLimits.Deg,
    float WristDeviationLow = -25f * SwingLimits.Deg, float WristDeviationHigh = 10f * SwingLimits.Deg,
    float ForearmRollLow = -45f * SwingLimits.Deg, float ForearmRollHigh = 45f * SwingLimits.Deg,
    float ElbowLow = 0f, float ElbowHigh = 140f * SwingLimits.Deg,
    float ShoulderTwistLow = -75f * SwingLimits.Deg, float ShoulderTwistHigh = 75f * SwingLimits.Deg,
    float ShoulderBack = 0.7f, float ShoulderAcross = 0.64f)
{
    internal const float Deg = MathF.PI / 180f;

    /// <summary>今までの広い範囲（手首は <c>HandSkeleton.Range</c>・ねじり ±80°、肘・肩はラグドールの範囲）。比べるため。</summary>
    public static SwingLimits Loose { get; } = new(-70f * Deg, 80f * Deg, -30f * Deg, 20f * Deg, -80f * Deg, 80f * Deg, -10f * Deg, 145f * Deg,
        -HumanoidRagdollLimits.ShoulderTwist, HumanoidRagdollLimits.ShoulderTwist, 1f, 1f);
}

/// <summary>1 本の振りの評価（物差しと点の内訳）。</summary>
public sealed record SwingEval
{
    /// <summary>剣先の最大の速さ [m/s]。</summary>
    public float PeakSpeed { get; init; }

    /// <summary>剣先の主な 1 本の動き（最大の速さの山、谷から谷）の LDLJ（NaN = 動きが小さくて測れない）。</summary>
    public float Ldlj { get; init; }

    /// <summary>同じ区間の SPARC。</summary>
    public float Sparc { get; init; }

    /// <summary>主な山以外の、速さの山の数（主な山から後ろ。振り抜いた後の打ち直し・揺れ返し）。</summary>
    public int ExtraPeaks { get; init; }

    /// <summary>同じ区間の剣先の軌跡の、曲率の変化（<see cref="StickBeauty.CurvatureVariation"/>）。</summary>
    public float CurvatureVariation { get; init; }

    /// <summary>肘・手首・剣先の軌跡（主な 1 本の動きの始めから終わりまで）の、断面からの外れ（距離の RMS ÷ 広がり）。</summary>
    public float PlaneMisfit { get; init; }

    /// <summary>構えから振り抜くまで全体（振りかぶりを含む）の、同じ外れ。</summary>
    public float PlaneMisfitAll { get; init; }

    /// <summary>刃筋の角 [rad]（速さで重みを付けた平均）。</summary>
    public float EdgeAngle { get; init; }

    /// <summary>剣先が最大の速さの瞬間の、進む向きと断面の斬る向きとの角 [rad]。</summary>
    public float DirectionAngle { get; init; }

    /// <summary>その瞬間の、肩から剣先までの距離 ÷ 腕と剣の長さ（伸びている度合い）。</summary>
    public float ReachFraction { get; init; }

    /// <summary>その瞬間の、肩から剣先への向きの前（+Z）成分（体の前で振っているか）。</summary>
    public float Forward { get; init; }

    /// <summary>肩・肘・手首の負担（トルク ÷ 上限）の最大。1 を超えたら失格。</summary>
    public float MaxLoad { get; init; }

    /// <summary>負担が最大の関節。</summary>
    public string MaxLoadJoint { get; init; } = "";

    /// <summary>刃が自分の体へ入り込んだ深さの最大 [m]（負なら隙間）。</summary>
    public float SelfCut { get; init; }

    /// <summary>腕が胴・頭へ入り込んだ深さの最大 [m]。</summary>
    public float ArmOverlap { get; init; }

    /// <summary>キーの間の補間が関節の範囲（<see cref="SwingLimits"/>）をはみ出した最大の量（角 [rad]・肩の向きは sin の差）。0 なら範囲の中。</summary>
    public float RangeExcess { get; init; }

    /// <summary>構えから振り抜くまでの時間 [s]。</summary>
    public float Duration { get; init; }

    /// <summary>主な 1 本の動きの歩の範囲。</summary>
    public int StrokeFrom { get; init; }

    public int StrokeTo { get; init; }

    /// <summary>点の項（重みを掛ける前、悪いほど大きい）。</summary>
    public float SmoothTerm { get; init; }

    public float PlaneTerm { get; init; }

    public float EdgeTerm { get; init; }

    public float DirectionTerm { get; init; }

    /// <summary>失格の罰（負担 &gt; 1・刃や腕が体を通る）。</summary>
    public float Disqualification { get; init; }

    /// <summary>点（大きいほど良い）。</summary>
    public float Score { get; init; }

    /// <summary>失格でないか。</summary>
    public bool Valid => Disqualification <= 0f;

    public string Breakdown(SwingWeights w)
        => $"点 {Score:0.00} = 速さ {w.Speed * (PeakSpeed / SwingRig.SpeedReference):0.00} − 滑らか {w.Smooth * SmoothTerm:0.00} − 断面 {w.Plane * PlaneTerm:0.00} − 刃筋 {w.Edge * EdgeTerm:0.00} − 向き {w.Direction * DirectionTerm:0.00} − 失格 {Disqualification:0.00}";
}

/// <summary>1 本の振りを走らせた結果（姿勢の列と力学）。</summary>
public sealed record SwingTrace(IReadOnlyList<StickFrame> Frames, IReadOnlyList<StickLoad> Loads, float Dt)
{
    /// <summary>補間が関節の範囲をはみ出した最大の量（<see cref="SwingEval.RangeExcess"/>）。</summary>
    public float RangeExcess { get; init; }
}

/// <summary>
/// 片手剣を握った棒人形で、肩から先（肩・肘・手首）だけを動かす振り（docs/STICK_MOTION.md §11）。骨盤・胸・頭・脚・左腕は構えのまま。
/// 振りの表し方: 構え（K0）→ 振りかぶり（K1）→ 打ち抜き（K2）→ 振り抜き（K3）の 3 つのキーの関節角と、間の時間 3 つ（遺伝子 24 個）。
/// キーの間は躍度最小の経由点つき補間（Flash &amp; Hogan 1985。5 次の多項式をつなぎ、経由点で速さ・加速度・躍度・スナップを連続にする）。
/// 肩は回転ベクトル（玉）、肘は角、手首は 屈曲・橈尺屈・ねじり（人の可動域 <c>HandSkeleton.Range</c> の中）。
/// </summary>
public sealed class SwingRig
{
    /// <summary>歩の間隔 [s]。</summary>
    public const float Dt = 1f / 120f;

    /// <summary>キーの数（構えを除く）。</summary>
    public const int KeyCount = 3;

    /// <summary>キー 1 つの遺伝子の数: 肩の回転ベクトル 3・肘 1・手首 3。</summary>
    public const int PerKey = 7;

    /// <summary>遺伝子の数（キー × 7 ＋ 時間 3）。</summary>
    public const int GeneCount = (KeyCount * PerKey) + KeyCount;

    /// <summary>剣先の速さを 1 とみなす値 [m/s]（点の速さの項の割り算。重みを比べやすくする単位で、物理の値ではない）。</summary>
    public const float SpeedReference = 10f;

    /// <summary>キーの間の時間の下限・上限 [s]。</summary>
    public const float MinSegment = 0.06f;

    public const float MaxSegment = 0.5f;

    private const float Deg = MathF.PI / 180f;

    private readonly int _shoulder = StickBody.Ids.UpperArmR;
    private readonly int _elbow = StickBody.Ids.ForearmR;
    private readonly int _weapon = StickBody.Ids.Weapon;
    private readonly float[] _wristLow = new float[3];
    private readonly float[] _wristHigh = new float[3];
    private readonly float[] _guardKey;

    private SwingRig(StickBody body, SwingLimits limits)
    {
        Body = body;
        Limits = limits;
        GuardFrame = new StickFrame(body, body.Guard);
        Shoulder = GuardFrame.Joints[_shoulder];
        ArmLength = Vector3.Distance(GuardFrame.Joints[_shoulder], GuardFrame.Joints[_elbow]) + Vector3.Distance(GuardFrame.Joints[_elbow], GuardFrame.Joints[_weapon]);
        BladeLength = Vector3.Distance(body.Segments[_weapon].Joint, body.Segments[_weapon].Tip);
        (_wristLow[0], _wristHigh[0]) = (limits.WristFlexLow, limits.WristFlexHigh);
        (_wristLow[1], _wristHigh[1]) = (limits.WristDeviationLow, limits.WristDeviationHigh);
        (_wristLow[2], _wristHigh[2]) = (limits.ForearmRollLow, limits.ForearmRollHigh);
        // 体の関節の範囲（ラグドール）より狭い側を採る。
        var elbow = body.Segments[_elbow];
        ElbowLow = MathF.Max(elbow.TwistMin, limits.ElbowLow);
        ElbowHigh = MathF.Min(elbow.TwistMax, limits.ElbowHigh);
        var shoulder = body.Segments[_shoulder];
        ShoulderTwistLow = MathF.Max(shoulder.TwistMin, limits.ShoulderTwistLow);
        ShoulderTwistHigh = MathF.Min(shoulder.TwistMax, limits.ShoulderTwistHigh);
        _guardKey = new float[PerKey];
        var at = body.Offsets[_shoulder];
        for (var i = 0; i < 3; i++)
        {
            _guardKey[i] = body.Guard.Values[at + i];
        }

        _guardKey[3] = body.Guard.Values[body.Offsets[_elbow]];
        _guardExcess = Excesses(_guardKey);
    }

    /// <summary>関節の範囲（剣を握った腕）。</summary>
    public SwingLimits Limits { get; }

    /// <summary>肩のねじれの範囲 [rad]（体の範囲と <see cref="SwingLimits"/> の狭い方）。</summary>
    public float ShoulderTwistLow { get; }

    public float ShoulderTwistHigh { get; }

    private readonly float[] _guardExcess;

    /// <summary>
    /// 関節の値（肩の回転ベクトル 3・肘・手首 3）が範囲をはみ出した量: [0] 肩のねじれ、[1] 上腕が後ろへ、[2] 上腕が反対の脇へ、[3] 肘、[4..6] 手首。
    /// 上腕の向きは胸の休みの座標（前 = +Z、上 = +Y、外 = 腕の軸）で測る（胸は構えのまま動かない）。
    /// </summary>
    private float[] Excesses(IReadOnlyList<float> v)
    {
        var s = Body.Segments[_shoulder];
        var q = StickMath.Exp(new Vector3(v[0], v[1], v[2]));
        var (_, twist) = StickMath.SwingTwist(q, s.Axis);
        var twistAngle = StickMath.SignedAngle(twist, s.Axis);
        var direction = Vector3.Transform(s.Axis, q);
        var e = new float[PerKey];
        e[0] = Outside(twistAngle, ShoulderTwistLow, ShoulderTwistHigh);
        e[1] = MathF.Max(0f, -direction.Z - Limits.ShoulderBack);
        e[2] = MathF.Max(0f, -Vector3.Dot(direction, s.Axis) - Limits.ShoulderAcross);
        e[3] = Outside(v[3], ElbowLow, ElbowHigh);
        for (var w = 0; w < 3; w++)
        {
            e[4 + w] = Outside(v[4 + w], _wristLow[w], _wristHigh[w]);
        }

        return e;
    }

    private static float Outside(float value, float low, float high) => MathF.Max(0f, MathF.Max(low - value, value - high));

    /// <summary>構えがはみ出している分を差し引いた、はみ出しの最大。</summary>
    private float ExcessBeyondGuard(IReadOnlyList<float> v)
    {
        var e = Excesses(v);
        var worst = 0f;
        for (var i = 0; i < e.Length; i++)
        {
            worst = MathF.Max(worst, e[i] - _guardExcess[i]);
        }

        return worst;
    }

    public StickBody Body { get; }

    /// <summary>構えの姿勢のフレーム（肩・胸・頭・脚・左腕は振りの間じっとしている）。</summary>
    public StickFrame GuardFrame { get; }

    /// <summary>右肩の関節の点（世界）。断面はここを通る。</summary>
    public Vector3 Shoulder { get; }

    /// <summary>肩から手首までの長さ [m]。</summary>
    public float ArmLength { get; }

    /// <summary>握りから剣先までの長さ [m]。</summary>
    public float BladeLength { get; }

    /// <summary>肩から剣先までの最大の距離（腕と剣をまっすぐ伸ばしたとき）[m]。</summary>
    public float Reach => ArmLength + BladeLength;

    public float ElbowLow { get; }

    public float ElbowHigh { get; }

    /// <summary>手首の角の範囲（0 = 屈曲（正 = 掌屈）、1 = 橈尺屈（正 = 橈屈）、2 = ねじり）。</summary>
    public (float Low, float High) WristRange(int index) => (_wristLow[index], _wristHigh[index]);

    /// <summary>名簿のキャラの体に鉄の剣（片手）を右手で握らせた棒人形。</summary>
    public static SwingRig Create(string characterId = "jin", SwingLimits? limits = null)
    {
        var c = CharacterRoster.Get(characterId);
        var tuning = FighterTuning.Default with { Build = c.Build ?? BodyBuild.Standard };
        var corners = new BoxingCorners("stick", _ => new StandGuard(), _ => new StandGuard(), CpuWeapon: WeaponKind.IronSword, CpuTwoHanded: false, CpuBody: c.Build);
        using var game = new BoxingGame(tuning, seed: 1, corners: corners);
        var fighter = game.Cpu;
        var body = StickBody.FromFighter(game, fighter, tuning);
        var kind = WeaponKind.IronSword;
        var weapon = game.WeaponOf(fighter) ?? throw new InvalidOperationException("鉄の剣を握らせられません。");
        var length = Weapon.TipOf(kind).X - Weapon.GripOf(kind).X;
        var mass = game.World.GetMass(weapon.Body.BodyId);
        // 片手: 手首のサーボの上限。手首の振れの上限は、掌屈 80°・橈屈 20° を合わせた範囲より少し広い 95°（異方的な範囲は、振りの側で人の可動域に収める）。
        var armed = body.WithWeapon(BoxingGame.WeaponHand, length, mass, -Weapon.GripOf(kind).X, tuning.WristMaxTorque * tuning.TorqueScale, swingLimit: 95f * Deg);
        return new SwingRig(armed, limits ?? new SwingLimits());
    }

    // ---- 遺伝子 ----

    /// <summary>キー <paramref name="key"/>（1〜3）の遺伝子の始まり。</summary>
    private static int KeyAt(int key) => (key - 1) * PerKey;

    /// <summary>遺伝子を範囲の中へ収める（肩は玉の範囲、肘・手首は関節の範囲、時間は上下限）。新しい配列を返す。</summary>
    public float[] Clamp(float[] genes)
    {
        ArgumentNullException.ThrowIfNull(genes);
        var g = (float[])genes.Clone();
        for (var k = 1; k <= KeyCount; k++)
        {
            var at = KeyAt(k);
            var shoulder = ClampShoulder(new Vector3(g[at], g[at + 1], g[at + 2]));
            (g[at], g[at + 1], g[at + 2]) = (shoulder.X, shoulder.Y, shoulder.Z);
            g[at + 3] = Math.Clamp(g[at + 3], ElbowLow, ElbowHigh);
            for (var w = 0; w < 3; w++)
            {
                g[at + 4 + w] = Math.Clamp(g[at + 4 + w], _wristLow[w], _wristHigh[w]);
            }
        }

        for (var s = 0; s < KeyCount; s++)
        {
            g[(KeyCount * PerKey) + s] = Math.Clamp(g[(KeyCount * PerKey) + s], MinSegment, MaxSegment);
        }

        return g;
    }

    private Vector3 ClampShoulder(Vector3 rotation)
    {
        var scratch = new StickPose(new float[Body.Dof]);
        var at = Body.Offsets[_shoulder];
        (scratch.Values[at], scratch.Values[at + 1], scratch.Values[at + 2]) = (rotation.X, rotation.Y, rotation.Z);
        scratch.Project(Body);
        var projected = StickMath.Exp(new Vector3(scratch.Values[at], scratch.Values[at + 1], scratch.Values[at + 2]));
        var axis = Body.Segments[_shoulder].Axis;
        var (swing, twist) = StickMath.SwingTwist(projected, axis);
        var twistAngle = StickMath.SignedAngle(twist, axis);
        if (twistAngle >= ShoulderTwistLow && twistAngle <= ShoulderTwistHigh)
        {
            return new Vector3(scratch.Values[at], scratch.Values[at + 1], scratch.Values[at + 2]); // 範囲の中は触らない（収め直しても値が変わらないように）
        }

        return StickMath.Log(swing * Quaternion.CreateFromAxisAngle(axis, Math.Clamp(twistAngle, ShoulderTwistLow, ShoulderTwistHigh)));
    }

    /// <summary>範囲の中から一様に引いた遺伝子（肩は振れの面積が一様になるよう、振れの方向・角・ねじれを引く）。</summary>
    public float[] RandomGenes(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var g = new float[GeneCount];
        var s = Body.Segments[_shoulder];
        var perpendicular = Vector3.Normalize(Vector3.Cross(s.Axis, Math.Abs(s.Axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitZ));
        var other = Vector3.Cross(s.Axis, perpendicular);
        for (var k = 1; k <= KeyCount; k++)
        {
            var at = KeyAt(k);
            var azimuth = random.NextSingle() * MathF.Tau;
            var swing = s.SwingLimit * MathF.Sqrt(random.NextSingle());
            var twist = ShoulderTwistLow + (random.NextSingle() * (ShoulderTwistHigh - ShoulderTwistLow));
            var axis = (MathF.Cos(azimuth) * perpendicular) + (MathF.Sin(azimuth) * other);
            var q = Quaternion.CreateFromAxisAngle(axis, swing) * Quaternion.CreateFromAxisAngle(s.Axis, twist);
            var rotation = StickMath.Log(q);
            (g[at], g[at + 1], g[at + 2]) = (rotation.X, rotation.Y, rotation.Z);
            g[at + 3] = ElbowLow + (random.NextSingle() * (ElbowHigh - ElbowLow));
            for (var w = 0; w < 3; w++)
            {
                g[at + 4 + w] = _wristLow[w] + (random.NextSingle() * (_wristHigh[w] - _wristLow[w]));
            }
        }

        for (var i = 0; i < KeyCount; i++)
        {
            g[(KeyCount * PerKey) + i] = MinSegment + (random.NextSingle() * (MaxSegment - MinSegment));
        }

        return Clamp(g);
    }

    /// <summary>構え → キー 1・2・3 の、関節ごと（PerKey 個）の値。</summary>
    private double[][] KeysOf(float[] g)
    {
        var keys = new double[PerKey][];
        for (var d = 0; d < PerKey; d++)
        {
            keys[d] = new double[KeyCount + 1];
            keys[d][0] = _guardKey[d];
            for (var k = 1; k <= KeyCount; k++)
            {
                keys[d][k] = g[KeyAt(k) + d];
            }
        }

        return keys;
    }

    // ---- 走らせる ----

    /// <summary>遺伝子の振りを走らせる: 歩ごとの姿勢（肩から先だけ変え、ほかは構え）と、力学（肩・肘・手首の負担）。</summary>
    public SwingTrace Simulate(float[] genes)
    {
        ArgumentNullException.ThrowIfNull(genes);
        var g = Clamp(genes);
        var durations = new double[KeyCount];
        var total = 0.0;
        for (var i = 0; i < KeyCount; i++)
        {
            durations[i] = g[(KeyCount * PerKey) + i];
            total += durations[i];
        }

        var keys = KeysOf(g);
        var segments = new double[PerKey][][];
        for (var d = 0; d < PerKey; d++)
        {
            segments[d] = SwingInterpolation.ViaPoints(keys[d], durations);
        }

        var n = (int)Math.Ceiling(total / Dt) + 1;
        var frames = new StickFrame[n];
        var axis = Body.Segments[_weapon].Axis;
        var shoulderAt = Body.Offsets[_shoulder];
        var elbowAt = Body.Offsets[_elbow];
        var weaponAt = Body.Offsets[_weapon];
        var values = new double[PerKey];
        var raw = new float[PerKey];
        var excess = 0f;
        for (var i = 0; i < n; i++)
        {
            var t = Math.Min(i * (double)Dt, total);
            var segment = 0;
            var start = 0.0;
            while (segment < KeyCount - 1 && t > start + durations[segment])
            {
                start += durations[segment];
                segment++;
            }

            for (var d = 0; d < PerKey; d++)
            {
                values[d] = SwingInterpolation.Evaluate(segments[d][segment], t - start);
                raw[d] = (float)values[d];
            }

            excess = MathF.Max(excess, ExcessBeyondGuard(raw));

            var pose = Body.Guard.Copy();
            (pose.Values[shoulderAt], pose.Values[shoulderAt + 1], pose.Values[shoulderAt + 2]) = ((float)values[0], (float)values[1], (float)values[2]);
            pose.Values[elbowAt] = Math.Clamp((float)values[3], ElbowLow, ElbowHigh);
            var flex = Math.Clamp((float)values[4], _wristLow[0], _wristHigh[0]);
            var deviation = Math.Clamp((float)values[5], _wristLow[1], _wristHigh[1]);
            var roll = Math.Clamp((float)values[6], _wristLow[2], _wristHigh[2]);
            var wrist = StickMath.Log(WristRotation(flex, deviation, roll, axis));
            (pose.Values[weaponAt], pose.Values[weaponAt + 1], pose.Values[weaponAt + 2]) = (wrist.X, wrist.Y, wrist.Z);
            pose.Project(Body);
            frames[i] = new StickFrame(Body, pose);
        }

        var loads = new StickLoad[n];
        for (var i = 0; i < n; i++)
        {
            loads[i] = StickDynamics.Solve(frames[Math.Max(0, i - 1)], frames[i], frames[Math.Min(n - 1, i + 1)], Dt, (true, true));
        }

        return new SwingTrace(frames, loads, Dt) { RangeExcess = excess };
    }

    /// <summary>
    /// 手首の回転（前腕の休みの座標）: 屈曲は +Z まわり（正 = 掌屈。T ポーズの右腕は −X へ伸び、手のひらが下を向くので、剣は下へ曲がる）、
    /// 橈尺屈は +Y まわり（正 = 橈屈。親指側 = 前へ）、ねじりは剣の軸まわり。ねじりが最も内側（先に掛かる）。
    /// </summary>
    public static Quaternion WristRotation(float flex, float deviation, float roll, Vector3 axis)
        => Quaternion.CreateFromAxisAngle(Vector3.UnitZ, flex) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, deviation) * Quaternion.CreateFromAxisAngle(axis, roll);

    // ---- 評価 ----

    /// <summary>遺伝子の振りを走らせて物差しと点を出す。</summary>
    public SwingEval Evaluate(float[] genes, SwingPlane plane, SwingWeights weights)
    {
        var trace = Simulate(genes);
        return Evaluate(trace, plane, weights);
    }

    /// <summary>走らせた振りの物差しと点。</summary>
    public SwingEval Evaluate(SwingTrace trace, SwingPlane plane, SwingWeights weights)
    {
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(plane);
        ArgumentNullException.ThrowIfNull(weights);
        var frames = trace.Frames;
        var dt = trace.Dt;
        var n = frames.Count;
        var tip = new Vector3[n];
        var elbow = new Vector3[n];
        var wrist = new Vector3[n];
        for (var i = 0; i < n; i++)
        {
            tip[i] = frames[i].Tip(_weapon);
            elbow[i] = frames[i].Joints[_elbow];
            wrist[i] = frames[i].Joints[_weapon];
        }

        var speeds = StickBeauty.Speeds(tip, dt);
        var peak = 0;
        for (var i = 1; i < n; i++)
        {
            if (speeds[i] > speeds[peak])
            {
                peak = i;
            }
        }

        var (from, to) = StickBeauty.MainSubmovement(speeds);
        var span = to - from + 1;
        var ldlj = span >= 6 ? StickBeauty.Ldlj(tip.AsSpan(from, span).ToArray(), dt) : null;
        var sparc = span >= 8 ? StickBeauty.Sparc(speeds.AsSpan(from, span).ToArray(), dt) : null;
        var extraPeaks = Math.Max(0, StickBeauty.SpeedPeaks(speeds.AsSpan(from, n - from).ToArray()) - 1);
        var curvature = span >= 4 ? StickBeauty.CurvatureVariation(tip.AsSpan(from, span).ToArray()) : 0f;

        // 断面: 肘・手首・剣先の軌跡（肩はこの面の上に固定）。主な 1 本の動きの始めから振り抜いた所まで。
        var points = new List<Vector3>(3 * (n - from));
        for (var i = from; i < n; i++)
        {
            points.Add(elbow[i]);
            points.Add(wrist[i]);
            points.Add(tip[i]);
        }

        var misfit = StickBeauty.PlaneMisfit(points, Shoulder, plane.Normal);
        var whole = new List<Vector3>(3 * n);
        for (var i = 0; i < n; i++)
        {
            whole.Add(elbow[i]);
            whole.Add(wrist[i]);
            whole.Add(tip[i]);
        }

        var misfitAll = StickBeauty.PlaneMisfit(whole, Shoulder, plane.Normal);

        var axes = new Vector3[n];
        var edges = new Vector3[n];
        var restEdge = Vector3.UnitZ; // T ポーズ・手のひら下の右手で、剣の腹は上下、刃は前後を向く。
        for (var i = 0; i < n; i++)
        {
            var r = frames[i].Rotations[_weapon];
            axes[i] = Vector3.Transform(Body.Segments[_weapon].Axis, r);
            edges[i] = Vector3.Transform(restEdge, r);
        }

        var edge = StickBeauty.EdgeAngle(tip, axes, edges, dt, from, to);

        var vPeak = (tip[Math.Min(n - 1, peak + 1)] - tip[Math.Max(0, peak - 1)]) / (2f * dt);
        var direction = vPeak.LengthSquared() > 1e-8f ? MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(vPeak), plane.Direction), -1f, 1f)) : MathF.PI;
        var offset = tip[peak] - Shoulder;
        var reach = offset.Length() / Reach;
        var forward = offset.LengthSquared() > 1e-8f ? Vector3.Normalize(offset).Z : 0f;

        var maxLoad = 0f;
        var maxLoadJoint = "";
        foreach (var load in trace.Loads)
        {
            foreach (var j in new[] { _shoulder, _elbow, _weapon })
            {
                var ratio = load.Ratio(Body, j);
                if (ratio > maxLoad)
                {
                    (maxLoad, maxLoadJoint) = (ratio, Body.Segments[j].Name);
                }
            }
        }

        var selfCut = float.NegativeInfinity;
        var overlap = float.NegativeInfinity;
        foreach (var frame in frames)
        {
            selfCut = MathF.Max(selfCut, Body.SelfCut(frame).Depth);
            foreach (var arm in new[] { _shoulder, _elbow })
            {
                foreach (var trunk in new[] { StickBody.Ids.Chest, StickBody.Ids.Head, StickBody.Ids.Pelvis })
                {
                    if (arm == _shoulder && trunk == StickBody.Ids.Chest)
                    {
                        continue; // 上腕は胸に付いている
                    }

                    overlap = MathF.Max(overlap, StickReview.Overlap(Body, frame, arm, trunk));
                }
            }
        }

        // 点の項。それぞれ「悪い」で 1 になる割り算（LDLJ −8・SPARC −2.6・曲率の変化 e⁴−1・断面の外れ 0.1・30°・30°・伸び 0.8）。
        var ldljValue = ldlj ?? -12f;
        var sparcValue = sparc ?? -5f;
        var smooth = (MathF.Max(0f, -ldljValue - 5f) / 3f / 3f) + (MathF.Max(0f, -sparcValue - 1.6f) / 1f / 3f) + (MathF.Log(1f + curvature) / 4f / 3f) + extraPeaks;
        var planeTerm = (((1f - weights.Whole) * misfit) + (weights.Whole * misfitAll)) / 0.1f;
        var edgeTerm = edge / (30f * Deg);
        var directionTerm = (direction / (30f * Deg)) + (MathF.Max(0f, 0.8f - reach) / 0.2f) + (MathF.Max(0f, 0.3f - forward) / 0.3f);
        var disqualification = 0f;
        if (maxLoad > 1f)
        {
            disqualification += 5f + (20f * (maxLoad - 1f));
        }

        if (selfCut > PenetrationLimit)
        {
            disqualification += 5f + (1000f * selfCut);
        }

        if (overlap > PenetrationLimit)
        {
            disqualification += 5f + (1000f * overlap);
        }

        // 関節の範囲: 1° ほどの補間のはみ出しは許し、それを超えたら失格（はみ出し 0.1 rad ≈ 6° で罰 +2）。
        if (trace.RangeExcess > RangeTolerance)
        {
            disqualification += 5f + (20f * trace.RangeExcess);
        }

        var peakSpeed = speeds[peak];
        var score = (weights.Speed * (peakSpeed / SpeedReference)) - (weights.Smooth * smooth) - (weights.Plane * planeTerm) - (weights.Edge * edgeTerm)
            - (weights.Direction * directionTerm) - disqualification;
        return new SwingEval
        {
            PeakSpeed = peakSpeed,
            Ldlj = ldlj ?? float.NaN,
            Sparc = sparc ?? float.NaN,
            ExtraPeaks = extraPeaks,
            CurvatureVariation = curvature,
            PlaneMisfit = misfit,
            PlaneMisfitAll = misfitAll,
            EdgeAngle = edge,
            DirectionAngle = direction,
            ReachFraction = reach,
            Forward = forward,
            MaxLoad = maxLoad,
            MaxLoadJoint = maxLoadJoint,
            SelfCut = selfCut,
            ArmOverlap = overlap,
            RangeExcess = trace.RangeExcess,
            Duration = (n - 1) * dt,
            StrokeFrom = from,
            StrokeTo = to,
            SmoothTerm = smooth,
            PlaneTerm = planeTerm,
            EdgeTerm = edgeTerm,
            DirectionTerm = directionTerm,
            Disqualification = disqualification,
            Score = score,
        };
    }

    /// <summary>体の通り抜けの閾値 [m]（<see cref="StickReviewSettings.Penetration"/> と同じ 5 mm）。</summary>
    private const float PenetrationLimit = 0.005f;

    /// <summary>補間が関節の範囲をはみ出してよい量 [rad]（1°。キーの角が範囲の端にあると、躍度最小の補間はわずかに行き過ぎる）。</summary>
    private const float RangeTolerance = 1f * Deg;

    /// <summary>この振りを、棒人形の標準の見立て（<see cref="StickReview"/>）へ通す。バランス（捕捉点・床）は体を構えのまま固定しているので数えない。</summary>
    public StickReview Review(SwingTrace trace, FighterTuning? tuning = null)
    {
        ArgumentNullException.ThrowIfNull(trace);
        var steps = trace.Frames.Select(_ => new StickStep([], (true, true))).ToArray();
        var result = new StickResult(Body, trace.Dt, trace.Frames, trace.Loads, steps, new float[trace.Frames.Count]);
        return StickReview.Of(result, StickReviewSettings.From(tuning ?? FighterTuning.Default));
    }
}

/// <summary>躍度最小の経由点つき補間（Flash &amp; Hogan 1985）: 5 次の多項式を区間ごとにつなぎ、経由点で位置・速さ・加速度・躍度・スナップを連続にする。両端は速さ・加速度 0。</summary>
public static class SwingInterpolation
{
    /// <summary>5 次の多項式の係数 c0..c5（x(t) = Σ c_i t^i）: 始め (x0, v0, a0) → 終わり (x1, v1, a1)、時間 T。</summary>
    public static double[] Quintic(double x0, double v0, double a0, double x1, double v1, double a1, double t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        var t4 = t3 * t;
        var t5 = t4 * t;
        return
        [
            x0,
            v0,
            a0 / 2,
            ((20 * (x1 - x0)) - (((8 * v1) + (12 * v0)) * t) - (((3 * a0) - a1) * t2)) / (2 * t3),
            ((30 * (x0 - x1)) + (((14 * v1) + (16 * v0)) * t) + (((3 * a0) - (2 * a1)) * t2)) / (2 * t4),
            ((12 * (x1 - x0)) - (6 * (v1 + v0) * t) - ((a0 - a1) * t2)) / (2 * t5),
        ];
    }

    /// <summary>多項式の値。</summary>
    public static double Evaluate(double[] c, double t) => c[0] + (t * (c[1] + (t * (c[2] + (t * (c[3] + (t * (c[4] + (t * c[5])))))))));

    /// <summary>微分: 1 = 速さ、2 = 加速度、3 = 躍度。</summary>
    public static double Derivative(double[] c, double t, int order) => order switch
    {
        1 => c[1] + (t * ((2 * c[2]) + (t * ((3 * c[3]) + (t * ((4 * c[4]) + (t * 5 * c[5]))))))),
        2 => (2 * c[2]) + (t * ((6 * c[3]) + (t * ((12 * c[4]) + (t * 20 * c[5]))))),
        3 => (6 * c[3]) + (t * ((24 * c[4]) + (t * 60 * c[5]))),
        _ => throw new ArgumentOutOfRangeException(nameof(order)),
    };

    /// <summary>
    /// 位置 <paramref name="x"/>（始め → 経由点 … → 終わり）を、区間の時間 <paramref name="durations"/> で通る躍度最小の動きの、区間ごとの係数。
    /// 経由点の速さ・加速度を未知数に、躍度とスナップの連続（2 つの式 × 経由点）の線形の式を解く。
    /// </summary>
    public static double[][] ViaPoints(double[] x, double[] durations)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(durations);
        var segments = durations.Length;
        if (x.Length != segments + 1)
        {
            throw new ArgumentException("位置の数は区間の数 + 1 です。", nameof(x));
        }

        var unknown = 2 * (segments - 1);
        double[] u = new double[unknown];
        double[][] Build(double[] unknowns)
        {
            var result = new double[segments][];
            for (var s = 0; s < segments; s++)
            {
                var v0 = s == 0 ? 0 : unknowns[2 * (s - 1)];
                var a0 = s == 0 ? 0 : unknowns[(2 * (s - 1)) + 1];
                var v1 = s == segments - 1 ? 0 : unknowns[2 * s];
                var a1 = s == segments - 1 ? 0 : unknowns[(2 * s) + 1];
                result[s] = Quintic(x[s], v0, a0, x[s + 1], v1, a1, durations[s]);
            }

            return result;
        }

        double[] Residual(double[] unknowns)
        {
            var polys = Build(unknowns);
            var r = new double[unknown];
            for (var s = 0; s < segments - 1; s++)
            {
                r[2 * s] = Derivative(polys[s], durations[s], 3) - Derivative(polys[s + 1], 0, 3);
                r[(2 * s) + 1] = (24 * polys[s][4]) + (120 * polys[s][5] * durations[s]) - (24 * polys[s + 1][4]);
            }

            return r;
        }

        if (unknown > 0)
        {
            var baseline = Residual(u);
            var a = Enumerable.Range(0, unknown).Select(_ => new double[unknown]).ToArray();
            for (var j = 0; j < unknown; j++)
            {
                var probe = new double[unknown];
                probe[j] = 1;
                var r = Residual(probe);
                for (var i = 0; i < unknown; i++)
                {
                    a[i][j] = r[i] - baseline[i];
                }
            }

            u = Solve(a, baseline.Select(v => -v).ToArray());
        }

        return Build(u);
    }

    private static double[] Solve(double[][] a, double[] b)
    {
        var n = b.Length;
        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            for (var r = col + 1; r < n; r++)
            {
                if (Math.Abs(a[r][col]) > Math.Abs(a[pivot][col]))
                {
                    pivot = r;
                }
            }

            if (pivot != col)
            {
                for (var c = 0; c < n; c++)
                {
                    (a[col][c], a[pivot][c]) = (a[pivot][c], a[col][c]);
                }

                (b[col], b[pivot]) = (b[pivot], b[col]);
            }

            for (var r = col + 1; r < n; r++)
            {
                var f = a[r][col] / a[col][col];
                for (var c = col; c < n; c++)
                {
                    a[r][c] -= f * a[col][c];
                }

                b[r] -= f * b[col];
            }
        }

        var x = new double[n];
        for (var r = n - 1; r >= 0; r--)
        {
            var sum = b[r];
            for (var c = r + 1; c < n; c++)
            {
                sum -= a[r][c] * x[c];
            }

            x[r] = sum / a[r][r];
        }

        return x;
    }
}
