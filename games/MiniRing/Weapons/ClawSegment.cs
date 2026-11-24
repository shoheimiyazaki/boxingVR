using System.Numerics;

namespace MiniRing.Weapons;

/// <summary>刃元と切っ先の目標（肩中心の体フレーム）。面の法線で刃面のロールを決める。</summary>
public readonly record struct ClawSegment(Vector3 Root, Vector3 Tip, Vector3 Normal)
{
    public static float Length => SickleClawShape.Edge(Hand.Right).Length();

    public static ClawSegment FromWrist(Hand hand, Vector3 wrist, Quaternion rotation)
        => new(wrist, wrist + Vector3.Transform(SickleClawShape.Edge(hand), rotation), Vector3.Transform(Vector3.UnitY, rotation));

    // 固定長の線分を既存の手首目標に変換する。リプレイは従来通りこの変換後の指令を保存する。
    public HandCommand ToCommand(Hand hand)
    {
        var travel = Tip - Root;
        if (!Finite(Root) || !Finite(travel) || !Finite(Normal) || travel.LengthSquared() is < 1e-8f or > 100f)
        {
            throw new ArgumentException("刃の目標には有限の刃元、切っ先、面の法線が必要です。");
        }

        var axis = Vector3.Normalize(travel);
        var across = Vector3.Cross(axis, Normal);
        if (across.LengthSquared() < 1e-8f)
        {
            across = Vector3.Cross(axis, MathF.Abs(axis.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY);
        }
        across = Vector3.Normalize(across);
        var up = Vector3.Cross(across, axis);
        var basis = new Matrix4x4(axis.X, axis.Y, axis.Z, 0f, up.X, up.Y, up.Z, 0f,
            across.X, across.Y, across.Z, 0f, 0f, 0f, 0f, 1f);
        var local = SickleClawShape.Edge(hand);
        var align = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Atan2(local.Z, local.X));
        return new HandCommand(Root, false, Orientation: Quaternion.Normalize(Quaternion.Concatenate(align, Quaternion.CreateFromRotationMatrix(basis))));
    }

    private static bool Finite(Vector3 value) => float.IsFinite(value.LengthSquared());
}
