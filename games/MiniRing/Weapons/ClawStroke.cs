using System.Numerics;

namespace MiniRing.Weapons;

/// <summary>構えから助走を始め、切断面の中を一定方向へ振り抜く目標経路。</summary>
public readonly record struct ClawStroke(Hand Hand, float Reach, Vector3 Aim, bool Sweep, float Windup, float DriveTime, float FollowThrough)
{
    private const float LoadTime = 0.27f;
    public float CutStart => LoadTime + DriveTime * 0.30f;
    public float CutEnd => LoadTime + DriveTime * 0.82f;
    public float Duration => LoadTime + DriveTime + FollowThrough;
    private static Vector3 Forward => Vector3.UnitZ;
    private Vector3 Across
    {
        get
        {
            var slope = Math.Clamp(0.12f - (0.25f * Aim.Y / MathF.Max(Reach, 0.01f)), 0.03f, 0.28f);
            return Vector3.Normalize(new Vector3(-Hand.LocalSide(), -slope, 0f));
        }
    }
    public Vector3 Normal => Sweep ? Vector3.Cross(Forward, Across) : Vector3.UnitY;

    public static ClawStroke Create(Hand hand, float reach, Vector3 aim, bool sweep, float drive = 0.31f, float windup = 1f)
        => new(hand, reach, aim, sweep, windup, drive, 0.32f);

    public static ClawSegment Guard(Hand hand, float reach)
    {
        var root = new Vector3(0.57f * hand.LocalSide(), 0.10f, 0.34f) * reach;
        var direction = Vector3.Normalize(new Vector3(0.24f * hand.LocalSide(), -0.84f, 0.62f));
        return new ClawSegment(root, root + direction * ClawSegment.Length, new Vector3(-0.45f * hand.LocalSide(), 0.20f, 0.76f));
    }

    public ClawSegment Sample(float time, ClawSegment? recoveryGuard = null)
    {
        var guard = Guard(Hand, Reach);
        if (time < LoadTime)
        {
            return Blend(guard, Drive(0f), Smooth(time / LoadTime));
        }

        if (time < LoadTime + DriveTime)
        {
            return Drive(Smooth((time - LoadTime) / DriveTime));
        }

        return Blend(Drive(1f), recoveryGuard ?? guard, Smooth((time - LoadTime - DriveTime) / FollowThrough));
    }

    private ClawSegment Drive(float phase)
    {
        Vector3 root;
        Vector3 axis;
        var z = Math.Clamp(Aim.Z - ClawSegment.Length * 0.56f, -Reach * 0.12f, Reach * 0.86f);
        var center = new Vector3(Aim.X * 0.80f, Math.Clamp(Aim.Y, -Reach * 0.66f, Reach * 0.48f), z);
        if (Sweep)
        {
            var across = Across;
            var angle = (-0.98f * Windup) + (phase * (0.98f * Windup + 1.17f));
            root = center + (across * ((phase - 0.54f) * Reach * (0.90f + 0.30f * Windup))) + (Forward * ((MathF.Cos(angle) - 1f) * Reach * 0.30f));
            axis = Forward * MathF.Cos(angle) + across * MathF.Sin(angle);
        }
        else
        {
            var atContact = Math.Clamp(Aim.Z - ClawSegment.Length, -Reach * 0.42f, Reach * 0.86f);
            root = center with { Z = atContact + ((phase - 0.40f) * Reach * 0.92f * Windup) };
            axis = Forward;
        }

        return new ClawSegment(root, root + axis * ClawSegment.Length, Normal);
    }

    private static ClawSegment Blend(ClawSegment from, ClawSegment to, float blend)
    {
        var root = Vector3.Lerp(from.Root, to.Root, blend);
        var axis = Vector3.Normalize(Vector3.Lerp(from.Tip - from.Root, to.Tip - to.Root, blend));
        return new ClawSegment(root, root + axis * ClawSegment.Length, Vector3.Lerp(from.Normal, to.Normal, blend));
    }

    private static float Smooth(float value)
    {
        var t = Math.Clamp(value, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
