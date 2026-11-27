using System.Numerics;

namespace MiniRing;

/// <summary>
/// 釣り合いの傾き（2026-11-27、ユーザーの指定）: 体を、重力と体の加速度を合わせた向き（g − a）に逆らわないよう傾ける目標。
/// 床が体へ水平の加速度 a を与えるとき、床反力は足の作用点から重心を通る（重心まわりに回さない）ので、作用点から重心への線は (a − g) に沿う:
/// 加速する向きへ体を傾けて（tan θ = |a| / g）、はじめて足で押した力が体を回さずに運ぶ。上体の姿勢の目標（胸・骨盤）に足し、既存の PD・関節の筋で追う。
/// <list type="bullet">
/// <item>予測（フィードフォワード）: 移動の指令の速さ（とステップの滑りの速さ・減速）を予定として、それを追う床反力の作用点を捕捉点の制御で出す。
/// 指令が出た歩から作用点が動くので、体が動き出す前に上体が先に傾く。止まる指令でも同じ式（速さが残っていれば作用点が前へ出て、外なら傾けて止まる）。</item>
/// <item>外乱（打たれた・押された）: 重心の捕捉点 ξ = 重心 + 速さ / ω が支えの多角形（足の凸包）の外へ出た分 d を、足では作用点を置けない分として
/// 倒立振子の加速度 ω² d で取り戻す向きへ傾ける（足首で出せる範囲は足の中で受け、出た分だけ腰・上体で受ける）。打撃の種類では分けない。</item>
/// <item>どちらも物理の量だけ（g・ω = √(g / 重心の高さ)・体が付いていく時間 <see cref="FootworkTuning.BodyLag"/>）で、傾きの上限は <see cref="FighterTuning.MaxLeanRadians"/>。
/// 足を出して支えを作り直すのは足の運び（捕捉点が両足の線から外れたら、その向きへ構えを寄せて踏み出す: <see cref="FootworkTuning.CapturePlacementShare"/>）。</item>
/// </list>
/// </summary>
public sealed partial class Fighter
{
    /// <summary>最後に出した釣り合いの加速度の目標 [m/s²、水平、ワールド]（調べもの用）。</summary>
    internal Vector2 BalanceAccel { get; private set; }

    /// <summary>支えの多角形（<see cref="_region"/>）を前の歩で作ったか。</summary>
    private bool _regionFresh;

    /// <summary>前の歩の腰の体重移動（<see cref="HipShift"/>）。</summary>
    private Vector3 _lastHipShift;

    /// <summary>
    /// 釣り合いの傾きの目標（向きの座標系の前傾 <c>Pitch</c>・横の傾き <c>Roll</c> [rad]、<see cref="ApplyFacingAndUpright"/> の傾きと同じ向き）。
    /// 立っていて足の運びで立っている間だけ（倒れて起き上がる間・足の運びの無い体は 0）。
    /// </summary>
    private (float Pitch, float Roll) BalanceLean(in FighterCommand command, float groundY, float dt)
    {
        // 支えの多角形は前の歩に作った物だけを使う（倒れていた間・作らなかった歩の古い多角形を読まない）。
        var regionFresh = _regionFresh;
        _regionFresh = false;
        // 拳を打ち込む体重移動（腰の行き先を前へ運ぶ HipShift）も自分で出した動き: その速さは予定に入れる（入れないと、打ち込む体を後ろへ傾けて体重を抜いた）。
        var shift = HipShift;
        var shiftVelocity = (shift - _lastHipShift) / MathF.Max(1e-4f, dt);
        _lastHipShift = shift;
        // 四足の体は胴が水平で、胴を前後に傾けても重心がほとんど動かないので、この傾きでは受けない（DEBT「腰から打つ体」）。
        if (State != FighterState.Standing || !_footworkActive || Build.FourLegged)
        {
            BalanceAccel = Vector2.Zero;
            return (0f, 0f);
        }

        var g = -_world.Gravity.Y;
        var com = CenterOfMass;
        var velocity = CenterOfMassVelocity;
        var omega = MathF.Max(1e-3f, MathF.Sqrt(MathF.Max(0f, g) / MathF.Max(0.0563f, com.Y - groundY)));
        var intent = command.ClampedMove * (_tuning.WalkSpeed * LegsVigor);
        var planned = new Vector2(intent.X + shiftVelocity.X, intent.Z + shiftVelocity.Z);
        var plannedAccel = Vector2.Zero;
        if (_glide is { } glide)
        {
            // ステップの後の滑り（Fighter.Step.cs）: 蹴り出した勢いは自分で出した動きなので、その向きの速さは予定どおり。体を運ぶ力は StepBrakeAccel で減速させる。
            var direction = new Vector2(glide.X, glide.Z);
            var excess = MathF.Max(0f, Vector2.Dot(new Vector2(velocity.X, velocity.Z) - planned, direction));
            planned += direction * excess;
            plannedAccel = excess > 0f ? -direction * _tuning.Footwork.StepBrakeAccel : Vector2.Zero;
        }

        var accel = Vector2.Zero;
        if (regionFresh && !_region.IsEmpty)
        {
            // 倒立振子: 重心の加速度 = ω² (重心 − p)（p は床反力の作用点、ω = √(g / 重心の高さ)）。予定の速さ v* と予定の加速度 a* を追う作用点は、
            // 捕捉点の制御（収束の速さ ω、調整値なし）で p = 重心 + (v − v*) / ω − a* / ω²。予定は移動の指令（指令が出た歩から変わるので、体が動き出す前に
            // p が後ろへ回る = 予測）と、ステップの滑りとその減速。打たれて体が流れれば v − v* が残り、p は捕捉点 ξ = 重心 + v / ω へ（外乱、同じ式）。
            // p が足の凸包（前の歩の支えの多角形、LimitToSupport が作る）の中なら足首（作用点を足の中で動かす）で足りて傾けない。
            // 外へ出た分 e = p − (凸包の最も近い点) は足では出せないので、体を傾けて出す: 加速度 −ω² e（傾き = atan(|e| / 重心の高さ)）。
            var needed = new Vector2(com.X, com.Z) + ((new Vector2(velocity.X, velocity.Z) - planned) / omega) - (plannedAccel / (omega * omega));
            if (_region.SignedDistance(needed) > 0f)
            {
                accel = omega * omega * (_region.Clamp(needed) - needed);
            }
        }

        BalanceAccel = accel;
        var local = Vector3.Transform(new Vector3(accel.X, 0f, accel.Y), Quaternion.CreateFromAxisAngle(Vector3.UnitY, -_facingYaw));
        return (MathF.Atan2(local.Z, g), MathF.Atan2(-local.X, g)); // 前へ加速 → 前傾。+X（左）へ加速 → 左へ（roll は −で +X 側へ傾く）
    }
}
