using System.Numerics;

namespace MiniRing;

/// <summary>
/// プレイヤーの体（HMD + コントローラ）→ ファイターの体フレームへの写像（純関数）。
/// プレイヤーの「肩中心・顔の向き」フレームでの手の相対位置を、身長比で縮めてファイターの体フレームへ渡す。
/// プレイヤーが前へ突き出せば、ファイターは（相手へロックオンした）前方へ突き出す。
/// </summary>
public static class PuppetMapping
{
    /// <summary>目の高さ ≒ 身長 × この比。</summary>
    public const float EyeHeightRatio = 0.93f;

    /// <summary>目 → 肩中心の下方向距離 ≒ 身長 × この比。</summary>
    public const float EyeToShoulderDropRatio = 0.095f;

    /// <summary>目 → 肩中心の後方距離 [m]（頭は肩より前にある）。</summary>
    public const float EyeToShoulderBack = 0.07f;

    /// <summary>目の高さから身長を推定する（未トラッキングで床付近なら 1m を下限に）。</summary>
    public static float PlayerHeightFromEye(float eyeHeight) => MathF.Max(1.0f, eyeHeight / EyeHeightRatio);

    public static float Scale(float puppetArmReach, float playerHeight) => puppetArmReach / MathF.Max(0.5f, ArmReachFromHeight(playerHeight));

    /// <summary>肩から拳までの長さ ≒ 身長 × 0.36（立位の人体計測の平均）。</summary>
    public static float ArmReachFromHeight(float playerHeight) => playerHeight * 0.36f;

    /// <summary>HMD の位置とヨーから、プレイヤーの肩中心フレームを推定する。</summary>
    public static BodyFrame PlayerShoulderFrame(Vector3 headPosition, float headYawRadians, float playerHeight)
    {
        var eye = new BodyFrame(headPosition, headYawRadians);
        var origin = headPosition - new Vector3(0f, playerHeight * EyeToShoulderDropRatio, 0f) - (eye.Forward * EyeToShoulderBack);
        return new BodyFrame(origin, headYawRadians);
    }

    /// <summary>コントローラのワールド位置 → ファイター体フレームのローカル目標。</summary>
    public static Vector3 HandTargetLocal(Vector3 controllerPosition, in BodyFrame playerShoulders, float scale)
        => playerShoulders.WorldToLocal(controllerPosition) * scale;

    /// <summary>
    /// <see cref="HandTargetLocal(Vector3, in BodyFrame, float)"/> の増幅版: 構えの位置（<see cref="GuardLocal"/>）からの
    /// ずれを <paramref name="gain"/> 倍する。肩からの距離ごと倍にすると構えただけで腕が伸び切るので、
    /// 構えを中心に増幅する（小さく打っても大きく届く。届かない分は腕の IK が伸び切りで止める）。
    /// </summary>
    public static Vector3 HandTargetLocal(Vector3 controllerPosition, in BodyFrame playerShoulders, float scale, Hand hand, float playerHeight, float gain)
    {
        var guard = GuardLocal(hand, playerHeight);
        return (guard + ((playerShoulders.WorldToLocal(controllerPosition) - guard) * gain)) * scale;
    }

    /// <summary>プレイヤーの構えの拳の位置（肩中心フレーム）: あごの横・肩より少し上・前へ約 25cm（身長 1.7m）。</summary>
    public static Vector3 GuardLocal(Hand hand, float playerHeight)
        => new Vector3(hand.LocalSide() * 0.08f, 0.05f, 0.15f) * playerHeight;

    /// <summary>
    /// 頭の、構えの位置（ゆっくり追従する平均位置）からのずれを人形の体フレームへ縮小して渡す。
    /// スリップ・ダッキング・前傾は、人形の体の軸の傾き・沈み込みになる。
    /// </summary>
    public static Vector3 HeadOffsetLocal(Vector3 head, Vector3 stanceHead, float bodyYawRadians, float scale)
        => new BodyFrame(Vector3.Zero, bodyYawRadians).InverseRotate(head - stanceHead) * scale;

    /// <summary>スティック入力を視線ヨー基準のワールド水平方向へ（デッドゾーン付き・長さ 0..1）。</summary>
    public static Vector3 StickToWorld(Vector2 stick, float viewYawRadians, float deadZone = 0.15f)
    {
        var magnitude = stick.Length();
        if (magnitude < deadZone)
        {
            return Vector3.Zero;
        }

        var rescaled = Math.Clamp((magnitude - deadZone) / (1f - deadZone), 0f, 1f);
        // スティック右 = 体の右 = ローカル −X（右手系）。
        var local = new Vector3(-stick.X, 0f, stick.Y) / magnitude * rescaled;
        return new BodyFrame(Vector3.Zero, viewYawRadians).Rotate(local);
    }

    /// <summary>角度の指数平滑（フレームレート非依存: 1 − e^(−λ·dt)）。最短経路で追う。</summary>
    public static float SmoothYaw(float current, float target, float lambda, float dt)
        => current + (BodyFrame.DeltaAngle(current, target) * (1f - MathF.Exp(-lambda * dt)));
}
