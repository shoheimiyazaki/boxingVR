using System.Numerics;

namespace MiniRing.Motion;

/// <summary>
/// VR のコントローラ・HMD の 1 フレーム分の生の入力（ワールド座標、人の体の寸法のまま [m]）。OpenXR の型に依らない形で、
/// 写像（<see cref="VrPlayerMapper"/>）・記録（<see cref="VrMotionClip"/>）・合成（テスト・Arena）が同じものを通る。
/// 向きは grip 姿勢（ローカル −Z がコントローラの前、+Y が上、+X が右）をワールドへ写した回転。ゼロの回転は「向き不明」で、単位回転として扱う。
/// </summary>
public readonly record struct VrRawFrame(
    Vector3 HeadPosition, Quaternion HeadOrientation,
    Vector3 LeftHand, Vector3 RightHand, bool LeftTracked, bool RightTracked,
    Vector2 LeftStick, Vector2 RightStick, float LeftGrip, float RightGrip,
    bool CalibratePressed, bool Focused,
    Quaternion LeftHandOrientation = default, Quaternion RightHandOrientation = default,
    float LeftTrigger = 0f, float RightTrigger = 0f)
{
    /// <summary>向きが零（不明）なら単位回転、それ以外は正規化。</summary>
    public static Quaternion OrIdentity(Quaternion q)
    {
        var l = q.LengthSquared();
        return l < 1e-8f || !float.IsFinite(l) ? Quaternion.Identity : Quaternion.Normalize(q);
    }

    public Quaternion HeadRotation => OrIdentity(HeadOrientation);

    public Quaternion HandRotation(Hand hand) => OrIdentity(hand == Hand.Left ? LeftHandOrientation : RightHandOrientation);

    public Vector3 HandPosition(Hand hand) => hand == Hand.Left ? LeftHand : RightHand;

    public bool Tracked(Hand hand) => hand == Hand.Left ? LeftTracked : RightTracked;

    public float Trigger(Hand hand) => hand == Hand.Left ? LeftTrigger : RightTrigger;

    public float Grip(Hand hand) => hand == Hand.Left ? LeftGrip : RightGrip;
}
