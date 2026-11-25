using System.Numerics;

namespace MiniRing;

/// <summary>
/// 支えの力を床から出す（docs/GRAPPLING.md 段 1）。体を立たせる力（腰を持ち上げる・上体を起こす・支えの点へ寄せる）は
/// 本当は足の裏が床を押す力で、その作用点（圧力中心 CoP）は床に着いている所の凸包（支持多角形）の中にしか来ない。
/// 毎歩、その 3 つの力が求めた合計の力と重心まわりのモーメントから「その力を出すのに要る CoP」を逆算し、
/// 支持多角形の外なら CoP を多角形の中の最も近い点へ寄せ、その CoP で出せるモーメントへ直す（水平の力は摩擦の範囲まで）。
/// 重心が足の外へ出ると、起こす力は出せず、むしろ倒れる向きのモーメントになる。踏み直し（足の運びの捕捉点）が
/// 間に合えば立ち直り、足を払われる・塞がれる・引かれ続けると倒れる。これが崩し。
/// </summary>
public sealed partial class Fighter
{
    private readonly SupportPolygon _support = new();
    private Vector3 _supportTorque;
    private Vector3 _supportForce;
    private static readonly Vector3[] BoxCorners =
    [
        new(-1f, -1f, -1f), new(1f, -1f, -1f), new(-1f, 1f, -1f), new(1f, 1f, -1f),
        new(-1f, -1f, 1f), new(1f, -1f, 1f), new(-1f, 1f, 1f), new(1f, 1f, 1f),
    ];

    /// <summary>
    /// 重心（水平面）が支持多角形の外へ出ている距離 [m]（中なら負: 最も近い辺までの余裕）。床に何も着いていなければ +∞。
    /// 崩れの深さ。ボットの読み・ドリルの物差しに使う（判断と測定。物理はこれで分岐しない）。
    /// </summary>
    public float SupportMargin { get; private set; } = float.NegativeInfinity;

    /// <summary>今の支持多角形（床に着いている所の凸包、XZ）。</summary>
    public IReadOnlyList<Vector2> SupportHull => _support.Hull;

    /// <summary>
    /// 背負っている相手の重さ [N]（docs/GRAPPLING.md §13）: 相手を掴み、胴どうしが触れているとき、相手が自分の脚で支えきれていない分
    /// （相手の縦の支えの抜け × 相手の上体の重さ）。脚で感じる重さで、腰を持ち上げる力に先に足す（重さを感じて脚に力を込める）。
    /// BoxingGame が毎歩入れる。
    /// </summary>
    public float CarriedLoad { get; internal set; }

    /// <summary>背負っている相手の重心（<see cref="CarriedLoad"/> が 0 より大きいとき。釣り合いを合成した重心で見る）。</summary>
    internal Vector3 CarriedCenter { get; set; }

    /// <summary>この歩で、近似の立たせる力が体へ与えた上向きの力積 [N·s]（床の衝撃を測るとき、床が出した分と分けるため）。</summary>
    internal float SupportLift => MathF.Max(0f, _supportForce.Y);

    /// <summary>この歩で、体を立たせる力（ホバー・起こすねじり・移動）が求めた力積を数え直す。</summary>
    private void BeginSupportWrench()
    {
        _supportTorque = Vector3.Zero;
        _supportForce = Vector3.Zero;
    }

    private void RecordSupportTorque(Vector3 angularImpulse) => _supportTorque += angularImpulse;

    private void RecordSupportForce(Vector3 impulse) => _supportForce += impulse;

    /// <summary>床から <see cref="FighterTuning.SupportContactGap"/> 以内にある部位の箱の角を集めて、支持多角形を作る。</summary>
    private void GatherSupport(float groundY, bool grounded)
    {
        _support.Clear();
        _limbs.Clear();
        _feet.Clear();
        _handsFeet.Clear();
        _legsTouch = false;
        _handsTouch = 0;
        if (!grounded)
        {
            return;
        }

        var touch = groundY + _tuning.SupportContactGap;
        foreach (var id in _all)
        {
            if (_detached.Contains(id))
            {
                continue;
            }

            var s = _world.GetState(id);
            // 箱の最も低い所が床から離れていれば、角を 1 つずつ見るまでもない。
            var half = HalfExtentsOf(id);
            if (s.Position.Y - half.Length() > touch)
            {
                continue;
            }

            // 足の裏は、どこか 1 か所でも床に着いていれば裏全体を支えに数える: 体重を受けているのはホバー（脚が床を押す力の近似）なので、
            // 足の箱は床に押し付けられずに傾き・浮きがちだが、本当は体重が足の裏を床に押し付けて平らに着く（CLAUDE.md §8「体を支える力」）。
            // 体重を受ける足だけ: 足の運びで振り出している足は、床をかすめても体重を受けていない（裏全体を数えると、
            // 円を歩く途中で振り出した足の裏まで支えに入り、2 秒止まった）。
            var kind = KindOf(id);
            if (Build.FourLegged && kind == BodyPartKind.Toe)
            {
                var cap = s.Position + Vector3.Transform(TarsusTip(id, half), s.Orientation);
                if (cap.Y <= touch && BearsWeight(id))
                {
                    var point = new Vector2(cap.X, cap.Z);
                    _support.Add(point);
                    _feet.Add(point);
                    _handsFeet.Add(point);
                    _limbs.Add(point);
                    _legsTouch = true;
                }
                continue;
            }

            var isFoot = !Build.FourLegged && kind is (BodyPartKind.Foot or BodyPartKind.Toe) && BearsWeight(id);
            var footDown = false;
            if (isFoot)
            {
                foreach (var corner in BoxCorners)
                {
                    if ((s.Position + Vector3.Transform(corner * half, s.Orientation)).Y <= touch)
                    {
                        footDown = true;
                        break;
                    }
                }
            }

            foreach (var corner in BoxCorners)
            {
                var p = s.Position + Vector3.Transform(corner * half, s.Orientation);
                if (p.Y <= touch || footDown)
                {
                    var flat = new Vector2(p.X, p.Z);
                    _support.Add(flat);
                    switch (kind)
                    {
                        case BodyPartKind.Foot or BodyPartKind.Toe:
                            _feet.Add(flat);
                            _handsFeet.Add(flat);
                            _limbs.Add(flat);
                            _legsTouch = true;
                            break;
                        case BodyPartKind.Coxa or BodyPartKind.Shin or BodyPartKind.Thigh:
                            _limbs.Add(flat);
                            _legsTouch = true;
                            break;
                        case BodyPartKind.Forearm or BodyPartKind.UpperArm:
                            _limbs.Add(flat);
                            _handsFeet.Add(flat);
                            _handsTouch |= id == _arms[0].Forearm ? 1 : id == _arms[1].Forearm ? 2 : 0;
                            break;
                    }
                }
            }
        }

        _support.Build();
        _limbs.Build();
        _feet.Build();
        _handsFeet.Build();
    }

    /// <summary>この足（足・つま先の部位）を足の運びが振り出していない（床に着けば体重を受ける。蹴る脚の足でも、床に着いていれば踏んでいる）。</summary>
    private (Vector2 Ball, float Yaw)? LandingFoot => _footwork.Landing ?? _rearFootwork?.Landing;

    private Vector3 TarsusTip(int part, Vector3 half)
    {
        var (center, bounds) = Voxels?.PartOf(part)?.Body.ColliderBox ?? (Vector3.Zero, half);
        return center - (bounds.Y * Vector3.UnitY);
    }

    private void AddLandingSupport(SupportPolygon polygon, (Vector2 Ball, float Yaw) landing)
    {
        if (Build.FourLegged)
        {
            polygon.Add(landing.Ball);
            return;
        }

        var heel = HalfExtents.Foot;
        var toe = FootGeometry.ToeHalf(heel);
        var (sin, cos) = MathF.SinCos(landing.Yaw);
        var forward = new Vector2(sin, cos);
        var side = new Vector2(cos, -sin);
        foreach (var along in (ReadOnlySpan<float>)[-2f * heel.Z, 2f * toe.Z])
        {
            foreach (var across in (ReadOnlySpan<float>)[-heel.X, heel.X])
            {
                polygon.Add(landing.Ball + (forward * along) + (side * across));
            }
        }
    }

    private bool BearsWeight(int footPart)
    {
        if (_rearFootwork is not null)
        {
            foreach (var (limb, pose) in StanceLegs())
            {
                if (footPart == limb.Heel || footPart == limb.Toe)
                {
                    return limb.Command is null && !(_footworkActive && pose.Swinging);
                }
            }

            return false;
        }
        for (var leg = 0; leg < 2; leg++)
        {
            if (footPart == _legParts[leg].Heel || footPart == _legParts[leg].Toe)
            {
                return !(_footworkActive && _footwork.Swinging == leg);
            }
        }

        return false;
    }

    /// <summary>床に着いている手足（前腕・上腕・すね・腿・足）の凸包: 体を床から押し上げられるのは手足だけ（胴・頭は乗っているだけ）。</summary>
    private readonly SupportPolygon _limbs = new();

    /// <summary>床に着いている手と足（膝・すねを除く）の凸包: 膝を浮かせ、手と足で体を支える（熊歩きの姿勢）。</summary>
    private readonly SupportPolygon _handsFeet = new();

    /// <summary>床に着いている足（足・つま先）の凸包。</summary>
    private readonly SupportPolygon _feet = new();

    internal int FeetHullCount => _feet.Hull.Count;

    internal float FeetDistance => _feet.IsEmpty ? float.NaN : _feet.SignedDistance(new Vector2(CenterOfMass.X, CenterOfMass.Z));

    /// <summary>膝・すね・足のどれかが床に着いているか。</summary>
    private bool _legsTouch;

    /// <summary>床に着いている前腕（ビット: 1 = Aigis の 0 番の腕、2 = 1 番の腕）。</summary>
    private int _handsTouch;

    /// <summary>
    /// 起き上がる間に腰を上げられる高さ（立つ高さに対する割合、0..1。docs/GRAPPLING.md §8）: 重心が床に着いた手足の凸包の上にあるほど
    /// 膝立ちの高さ（腿の長さ）まで、さらに足の凸包の上にあるほど立つ高さまで。手をついて体を起こし、重心を足へ移してから立つ。
    /// </summary>
    public float RiseCap { get; private set; } = 1f;

    /// <summary>起き上がる間、手足が体を押し上げられる割合（重心が床に着いた手足の凸包の上にあるほど 1）。</summary>
    public float LimbSupport { get; private set; } = 1f;

    /// <summary>
    /// 体を立たせる力が求めた合計の力積（<see cref="RecordSupportForce"/>・<see cref="RecordSupportTorque"/>）を、床が出せる力へ直す（差を骨盤・胸へ足す）。
    /// <list type="bullet">
    /// <item>床に何も着いていなければ、どれも出せない（宙の体は持ち上げられず、起こせず、運べない）。</item>
    /// <item>起こすねじり（重心まわりの水平のねじり τ）は、床反力の作用点（CoP）が重心からずれた腕 r と垂直抗力 N の積 r × N でしか出せず、
    /// CoP は支持多角形の中にしか来ない: τ から r = (τz, −τx) / N を逆算し、多角形の外なら中の最も近い点へ寄せて τ を出し直す。
    /// 重心が足の外へ出れば、起こすねじりは出せず、倒れる向きのねじりになる。</item>
    /// <item>体を運ぶ水平の力（足の運びが足を置いてから体を追わせる力）は、足が床を蹴る力の近似として摩擦（μN）の範囲で出す。
    /// CoP の制限には入れない（入れると、足を先に置いて体が後から追う今の足の運びでは、体が足に付いて行けず前のめりに倒れた）。近道（CLAUDE.md §8）。</item>
    /// </list>
    /// N はホバーの上向きの力 ＋ 脚の重さ（脚は床が直接支える）。
    /// </summary>
    private void LimitToSupport(float groundY, bool grounded, float dt)
    {
        GatherSupport(groundY, grounded);
        var c = CenterOfMass;
        if (CarriedLoad > 0f)
        {
            // 背負った相手の重さを含めた重心で釣り合いを見る（相手の重さ m = 荷重 / g）。
            var m = CarriedLoad / -_world.Gravity.Y;
            c = ((c * SupportedMass) + (CarriedCenter * m)) / (SupportedMass + m);
        }

        var c2 = new Vector2(c.X, c.Z);
        SupportMargin = _support.SignedDistance(c2);
        CopShortfall = 0f;
        if (!_tuning.SupportFromGround || dt <= 0f)
        {
            (Balance, VerticalBalance) = (1f, 1f);
            return;
        }

        if (_support.IsEmpty)
        {
            // 床に何も着いていない（どの状態でも）: 立たせる力はどれも出せない。宙の体は持ち上げられず、起こせず、運べない。
            ApplySharedImpulse(-_supportForce);
            SplitTorque(-_supportTorque);
            _supportForce = Vector3.Zero;
            (Balance, VerticalBalance) = (0f, 0f);
            CopShortfall = float.PositiveInfinity;
            return;
        }

        if (_tuning.LegLossSupportShortcut && LegShortfall > 1e-3f)
        {
            // 脚を失った体（膝・切り株・骨盤で立つ）は足の運びで踏み直せず、立つのもホバーで腰を短い脚の高さに保つ近道なので、
            // 釣り合い・起き上がりの手足の制限は掛けない（前の体と同じ。CLAUDE.md §8「脚を失った体」）。床に着いていない体に力を出さないのは同じ。
            // 掛けると、片方の腿を失った体は踏み直せずに倒れ、骨盤で座る体は「重心が足の上」にならず起き上がれなかった（master とのマージで分かった）。
            (Balance, VerticalBalance) = (1f, 1f);
            LimbSupport = 1f;
            RiseCap = 1f;
            return;
        }

        // 支えの残り（連続の釣り合いのモデル）: 捕捉点が支えの領域の外へ出た距離で 1 → 0 へ。
        // 起き上がる間（手・膝で床を押す多点の物理が無い）は、床に着いていれば前の立たせる力のまま（近道、CLAUDE.md §8）。
        (Balance, VerticalBalance) = State == FighterState.Standing ? BalanceOf(c, groundY) : (1f, 1f);
        if (State != FighterState.Standing && _tuning.SupportModel is SupportModel.Soft or SupportModel.SoftCop)
        {
            RiseOnLimbs(c, groundY, dt);
            return;
        }

        RiseCap = 1f;
        LimbSupport = 1f;
        if (_tuning.SupportModel is SupportModel.Soft or SupportModel.SoftCop && Balance < 1f)
        {
            // 起こすねじりと体を運ぶ水平の力は L で 0。脚が真下で体重を受ける縦の力（ホバー）は、その倍（一歩でも取り戻せない所）で 0:
            // 縦の支えから先に抜くと、ふらついただけの体の膝が抜けて沈み込んだ。
            var lost = 1f - Balance;
            var lostLift = 1f - VerticalBalance;
            var horizontalForce = new Vector3(_supportForce.X, 0f, _supportForce.Z);
            var lift = new Vector3(0f, _supportForce.Y, 0f);
            ApplySharedImpulse(-(horizontalForce * lost) - (lift * lostLift));
            // 減らすのは体を傾ける水平軸まわりのねじりだけ。縦軸まわり（体の向きを回す）のねじりは足の裏と床の摩擦で出るので、
            // CoP の位置には縛られない（下の CoP の制限も縦軸の分は残す）。縦軸まで減らすと、片脚で立つ間に骨盤の向きが保てず、
            // 腿のねじりの目標（股の関節の角）が骨盤ごと流れた。
            var tilt = new Vector3(_supportTorque.X, 0f, _supportTorque.Z);
            SplitTorque(-tilt * lost);
            _supportForce -= (horizontalForce * lost) + (lift * lostLift);
            _supportTorque -= tilt * lost;
        }

        // CoP の制限（起こすねじりは CoP が凸包の中で出せる分だけ）は、足で立っている間だけ。
        if (_tuning.SupportModel is SupportModel.Soft || State != FighterState.Standing)
        {
            return;
        }

        var torque = _supportTorque / dt;
        var force = _supportForce / dt;
        var normal = MathF.Max(1e-3f, MathF.Max(0f, force.Y) + (SupportedLegMass * -_world.Gravity.Y));
        var horizontal = new Vector2(force.X, force.Z);
        var friction = _tuning.SoleFriction * normal;
        if (horizontal.Length() > friction)
        {
            ApplySharedImpulse(new Vector3(horizontal.X, 0f, horizontal.Y) * ((friction / horizontal.Length()) - 1f) * dt);
        }

        var wanted = c2 + new Vector2(torque.Z / normal, -torque.X / normal);
        var cop = _support.Clamp(wanted);
        CopShortfall = Vector2.Distance(cop, wanted);
        if (CopShortfall <= 0f)
        {
            return;
        }

        var arm = cop - c2;
        var achievable = new Vector3(-(arm.Y * normal), torque.Y, arm.X * normal);
        SplitTorque((achievable - torque) * dt);
    }

    /// <summary>
    /// 起き上がる間の立たせる力（docs/GRAPPLING.md §8）: 床から体を押し上げられるのは床に着いた手足だけ。
    /// <list type="bullet">
    /// <item>手足の支え b = 重心が床に着いた手足の凸包の外へ出た距離で 1 → 0（(1 − d / L)²、L = <see cref="FighterTuning.RiseRecovery"/> × 脚の長さ）。
    /// 腰を持ち上げる力・上体を起こすねじり・体を運ぶ力に掛ける（重心が手足の上に無ければ、胴を持ち上げられない）。</item>
    /// <item>腰を上げられる高さ <see cref="RiseCap"/> = 膝立ちの高さ × b（膝・足が着いているとき）＋（立つ高さ − 膝立ちの高さ）× 足の支え
    /// （重心が足の凸包の上にあるほど 1）。手をついて体を起こし、重心を足へ移すまでは膝立ちより上がらない。</item>
    /// <item>重心を足へ移す: 床に着いた手で押して、重心を足の凸包の真ん中へ寄せる水平の力（<see cref="FighterTuning.RiseShift"/>、手足の支えの分だけ）。</item>
    /// </list>
    /// </summary>
    private void RiseOnLimbs(Vector3 com, float groundY, float dt)
    {
        var legLength = LegIk.MaxLength(_legParts[0].ThighToKnee, _legParts[0].ShinLength);
        var recovery = MathF.Max(1e-3f, _tuning.RiseRecovery * legLength);
        var c2 = new Vector2(com.X, com.Z);
        float Share(SupportPolygon hull)
        {
            if (hull.IsEmpty)
            {
                return 0f;
            }

            var share = 1f - Math.Clamp(hull.SignedDistance(c2) / recovery, 0f, 1f);
            return share * share;
        }

        var limbs = Share(_limbs);
        var feet = Share(_feet);
        LimbSupport = limbs;
        var handsFeet = _handsTouch != 0 ? Share(_handsFeet) : 0f;
        var kneel = Math.Clamp(_legParts[0].ThighToKnee.Length() / StandTargetHeight, 0f, 1f);
        var bear = MathF.Max(kneel, _tuning.RiseBearHeight);
        // 膝立ち（手足の支え）→ 膝を浮かせて手と足で支える（熊歩きの高さ）→ 足に重心を移して立つ、の 3 段を連続に足す。
        var cap = Math.Clamp((_legsTouch ? kneel * limbs : 0f) + ((bear - kneel) * MathF.Max(handsFeet, feet)) + ((1f - bear) * feet), 0f, 1f);
        // 手足が床に着く・離れるのは歩ごとにちらつく。筋が一瞬で力を抜くことは無いので、上限は時定数 RiseCapSeconds でならす。
        RiseCap += (cap - RiseCap) * (1f - MathF.Exp(-dt / MathF.Max(1e-3f, _tuning.RiseCapSeconds)));

        // 腰を持ち上げる力・体を運ぶ力は手足の支えの分だけ。上体を起こすねじりは、重心が足に乗った分だけ（それまでは手をついた
        // 前かがみのまま、上体は床に着いた手が支える。腰が床近くにあるうちに上体だけが起き上がる「吊られた」形にならない）。
        var lost = 1f - limbs;
        if (lost > 0f)
        {
            ApplySharedImpulse(-_supportForce * lost);
            _supportForce *= limbs;
        }

        SplitTorque(-_supportTorque * (1f - (limbs * MathF.Max(feet, _tuning.RiseTorsoFloor))));

        // 持ち上げる力の出どころ: 骨盤は脚が、胸は床に着いた手が押す。胸の取り分は、着いている手の割合と、重心が足に乗った割合
        // （脚から胴を通って胸まで支える）の大きい方まで。残りは骨盤へ（腰が上がらないうちに胸だけが吊り上がらない）。
        var lift = MathF.Max(0f, _supportForce.Y) * limbs;
        if (lift > 0f)
        {
            var pelvisMass = _world.GetMass(Parts.Pelvis);
            var chestMass = _world.GetMass(Parts.Chest);
            var hands = (((_handsTouch & 1) != 0 ? 0.5f : 0f) + ((_handsTouch & 2) != 0 ? 0.5f : 0f));
            var chestShare = chestMass / (pelvisMass + chestMass);
            var moved = lift * chestShare * (1f - MathF.Max(hands, feet));
            BodyImpulse(Parts.Chest, new Vector3(0f, -moved, 0f));
            BodyImpulse(Parts.Pelvis, new Vector3(0f, moved, 0f));
        }

        // 手で床を押して、重心を足の上へ移す（足が着いていて、重心がまだ足の上に無い分だけ）。
        if (!_feet.IsEmpty && feet < 1f)
        {
            var center = Vector2.Zero;
            var hull = _feet.Hull;
            for (var i = 0; i < hull.Count; i++)
            {
                center += hull[i];
            }

            center /= hull.Count;
            var v = CenterOfMassVelocity;
            var accel = ((center - c2) * _tuning.RiseShift) - (new Vector2(v.X, v.Z) * (2f * MathF.Sqrt(_tuning.RiseShift)));
            var limit = _tuning.RiseShiftMaxAccel;
            if (accel.Length() > limit)
            {
                accel *= limit / accel.Length();
            }

            // 押されて動くのは腰（お尻を足の上へ引く）。手が床を押す反作用は胸へ（腕が胴と床の間で出す力）。
            // 手で押すのは起き上がろうとする体だけ（起き上がる力 ControlScale と、意識の分）。
            var push = accel * ((1f - feet) * ControlScale * Awareness * (SupportedMass - SupportedLegMass) * dt);
            BodyImpulse(Parts.Pelvis, new Vector3(push.X, 0f, push.Y));
        }
    }

    /// <summary>
    /// 支えの残り（0..1）: 立たせる力（ホバー・起こすねじり・体を運ぶ力）のうち、今の体が出せる割合。
    /// 捕捉点 ξ = 重心 ＋ 水平の速さ / ω（ω = √(g / 重心の高さ)）が支えの領域の中なら 1、外へ d だけ出ると (1 − d / L)² で減り、
    /// L（<see cref="FighterTuning.SupportRecovery"/> × 脚の長さ: 一歩・腰の使い方で取り戻せる距離）で 0（ばらばらに倒れる）。
    /// 支えの領域は床に着いた所の凸包 ＋ 踏み出している足の着地点（<see cref="SupportRegion.Polygon"/>）か、
    /// 立っている間だけ両足を直径とする円（<see cref="SupportRegion.Disk"/>）。
    /// </summary>
    public float Balance { get; private set; } = 1f;

    /// <summary>縦の支え（腰を持ち上げる力）の残り。捕捉点が外にある間、現在の重心と支持領域の距離から求める。</summary>
    public float VerticalBalance { get; private set; } = 1f;

    private readonly SupportPolygon _region = new();

    private (float Upright, float Vertical) BalanceOf(Vector3 com, float groundY)
    {
        var h = MathF.Max(0.0563f, com.Y - groundY);
        var omega = MathF.Sqrt(-_world.Gravity.Y / h);
        var v = CenterOfMassVelocity;
        var position = new Vector2(com.X, com.Z);
        var capture = position + (new Vector2(v.X, v.Z) / omega);
        var legLength = LegIk.MaxLength(_legParts[0].ThighToKnee, _legParts[0].ShinLength);
        var recovery = MathF.Max(1e-3f, _tuning.SupportRecovery * legLength);
        float outside;
        float liftOutside;
        if (_rearFootwork is null && _tuning.SupportRegion == SupportRegion.Disk && State == FighterState.Standing && _footworkActive)
        {
            // 両足の母指球を直径の両端とする円（足の長さの半分だけ大きく）。
            var a = BallOnGround(_legParts[0]);
            var b = BallOnGround(_legParts[1]);
            var heel = HalfExtents.Foot;
            var radius = (0.5f * Vector2.Distance(a, b)) + heel.Z + FootGeometry.ToeHalf(heel).Z;
            var middle = 0.5f * (a + b);
            outside = Vector2.Distance(capture, middle) - radius;
            liftOutside = _tuning.UseCaptureForVerticalSupport ? outside : Vector2.Distance(position, middle) - radius;
        }
        else
        {
            _region.Clear();
            var hull = _support.Hull;
            for (var i = 0; i < hull.Count; i++)
            {
                _region.Add(hull[i]);
            }

            if (State == FighterState.Standing && _footworkActive && LandingFoot is { } landing)
            {
                AddLandingSupport(_region, landing);
            }

            _region.Build();
            outside = _region.SignedDistance(capture);
            liftOutside = _tuning.UseCaptureForVerticalSupport ? outside : _region.SignedDistance(position);
        }

        var share = 1f - Math.Clamp(outside / recovery, 0f, 1f);
        // 捕捉点が足で受け止められる間は縦の支持を保つ。捕捉点が外に出ても、横の速度だけでは縦の力を抜かない。
        var vertical = share >= 1f ? 1f : 1f - Math.Clamp(liftOutside / (_tuning.SupportVerticalRecovery * recovery), 0f, 1f);
        return (share * share, vertical * vertical);
    }

    /// <summary>この歩で、起こすねじりに要る CoP が支持多角形の外へ出た距離 [m]（出せなかったねじりの大きさの物差し。0 なら出せた）。</summary>
    internal float CopShortfall { get; private set; }

    /// <summary>足の運び（調べもの用）。</summary>
    internal Footwork FootworkState => _footwork;

    private readonly SupportPolygon _catchable = new();

    /// <summary>
    /// 体を運ぶ速さの目標を、捕捉点（重心 ＋ 速さ / ω）が「今床に着いている所 ＋ 踏み出している足の着地する所」の凸包に収まる分までにする。
    /// 倒立振子の重心は、捕捉点が支えの外にあると、今の足でも次の足でも止められない。体を足より先へ運ばない（足を置いてから体が付いていく）
    /// という足の運びの決まりを、重心の動きの物理（倒立振子）で言い直したもの。体の制御（どう動かしたいか）であって、出せる力の上限は
    /// <see cref="LimitToSupport"/> が別に決める。
    /// </summary>
    private Vector3 KeepCaptureOverFeet(Vector3 desired)
    {
        if (_support.IsEmpty)
        {
            return desired;
        }

        _catchable.Clear();
        var hull = _support.Hull;
        for (var i = 0; i < hull.Count; i++)
        {
            _catchable.Add(hull[i]);
        }

        if (LandingFoot is { } landing)
        {
            AddLandingSupport(_catchable, landing);
        }

        _catchable.Build();
        var c = CenterOfMass;
        var h = MathF.Max(0.141f, c.Y - (PelvisPosition.Y - _hoverHeight));
        var omega = MathF.Sqrt(-_world.Gravity.Y / h);
        var com = new Vector2(c.X, c.Z);
        var want = com + (new Vector2(desired.X, desired.Z) / omega);
        var kept = _catchable.Clamp(want);
        if (kept == want)
        {
            return desired;
        }

        var velocity = (kept - com) * omega;
        return new Vector3(velocity.X, desired.Y, velocity.Y);
    }

    /// <summary>直しのねじりの力積を、骨盤と胸へ質量の比で分ける（起こすねじりをかけていた 2 つの部位）。</summary>
    private void SplitTorque(Vector3 angularImpulse)
    {
        if (UsesHipDrive)
        {
            BodyTorque(Parts.Pelvis, angularImpulse); // 腰から打つ体: 外から起こす・回すねじりは骨盤だけにかかっている
            return;
        }

        var pelvisMass = _world.GetMass(Parts.Pelvis);
        var chestMass = _world.GetMass(Parts.Chest);
        var total = pelvisMass + chestMass;
        BodyTorque(Parts.Pelvis, angularImpulse * (pelvisMass / total));
        BodyTorque(Parts.Chest, angularImpulse * (chestMass / total));
    }
}

public sealed partial class Fighter
{
    /// <summary>
    /// 起き上がる間の脚（docs/GRAPPLING.md §8）: 足首の目標を重心の真下（左右へ股の幅、つま先へ少し）の床に置き、脚の IK で追う
    /// （足を体の下へ寄せる反射。手を床に着く <see cref="PostOnTheCanvas"/> と同じく、姿勢でなく目標を書く）。脚が届かなければ届く所まで寄せる。
    /// 骨盤は今の向きのまま解く（前かがみの間は、股を曲げて足を前へ運ぶ）。足は床から浮かせてよい（運び込む）。
    /// </summary>
    private void ApplyRiseLegs(float groundY, float dt)
    {
        var pelvis = _world.GetState(Parts.Pelvis);
        var inverse = Quaternion.Conjugate(pelvis.Orientation);
        var com = CenterOfMass;
        var forward = new BodyFrame(Vector3.Zero, _facingYaw).Forward;
        for (var i = 0; i < 2; i++)
        {
            var leg = _legParts[i];
            if (!_world.HasConstraint(leg.HipServo))
            {
                continue;
            }

            var pivot = LegPivot(leg, pelvis);
            var side = new Vector2(pivot.X - pelvis.Position.X, pivot.Z - pelvis.Position.Z);
            var ball = new Vector2(com.X, com.Z) + side + (new Vector2(forward.X, forward.Z) * (HalfExtents.Foot.Z * _tuning.RiseFootLead));
            var pose = new FootPose(new Vector3(ball.X, 0f, ball.Y), _facingYaw, 0f, true);
            var (ankle, foot) = AnkleTarget(leg, pose, groundY);
            var toes = forward;
            if (leg.Command is { } requested)
            {
                // 入力元の足の目標（捨て身技で相手の腰に足を当てる）: 足を寄せる反射の代わりにそれを追う。
                ankle = Frame.LocalToWorld(requested.TargetLocal);
                foot = Quaternion.Concatenate(requested.Orientation ?? Quaternion.Identity, Quaternion.CreateFromAxisAngle(Vector3.UnitY, _facingYaw));
                toes = requested.KneeTargetLocal is { } kneeGoal ? Frame.LocalToWorld(kneeGoal) - pivot : forward;
            }

            var (thigh, knee, reach) = LegIk.Solve(Vector3.Transform(ankle - pivot, inverse), leg.ThighToKnee, leg.ShinLength, Vector3.Transform(toes, inverse));
            leg.ReachRatio = reach;
            SetLegHipTarget(leg, pelvis, thigh);
            SetTwistTarget(leg.KneeServo, knee);
            var shin = Quaternion.Concatenate(Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitX, knee), thigh), pelvis.Orientation);
            leg.AnkleTarget = LegIk.AnklePitch(shin, foot);
            SetTwistTarget(leg.AnkleServo, leg.AnkleTarget);
            SetTwistTarget(leg.ToeServo, 0f);
            // 足を運ぶ力は、倒れていく間は抜けていて、起き上がる力（ControlScale: 張り × 起き上がる試み）の分だけ戻る。
            // 気を失っている体は足を寄せない（Awareness 0 で 0）。
            DriveFoot(leg, ankle, swinging: true, pelvisId: Parts.Pelvis, dt, strength: leg.Command is null ? ControlScale * ControlScale * Awareness : ControlScale);
        }
    }
}
