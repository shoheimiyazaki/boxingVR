using System.Numerics;

namespace MiniRing.Arena;

internal readonly record struct GalvaHandPose(Vector3 Wrist, Vector3 Blade);

internal readonly record struct GalvaMotionPose(
    Vector3 Root, Vector3 Thorax, GalvaHandPose Left, GalvaHandPose Right,
    Vector3 Foreleg, Vector3 ForelegAxis)
{
    public static GalvaMotionPose Guard => new(Vector3.Zero, Vector3.Zero,
        new(new(0.45f, 1.04f, 0.53f), new(0.24f, -0.58f, 0.77f)),
        new(new(-0.45f, 1.04f, 0.53f), new(-0.24f, -0.58f, 0.77f)),
        Vector3.Zero, -Vector3.UnitY);

    public static GalvaMotionPose Blend(GalvaMotionPose a, GalvaMotionPose b, float t) => new(
        Vector3.Lerp(a.Root, b.Root, t), Vector3.Lerp(a.Thorax, b.Thorax, t),
        new(Vector3.Lerp(a.Left.Wrist, b.Left.Wrist, t), Vector3.Lerp(a.Left.Blade, b.Left.Blade, t)),
        new(Vector3.Lerp(a.Right.Wrist, b.Right.Wrist, t), Vector3.Lerp(a.Right.Blade, b.Right.Blade, t)),
        Vector3.Lerp(a.Foreleg, b.Foreleg, t), Vector3.Lerp(a.ForelegAxis, b.ForelegAxis, t));
}

internal sealed record GalvaClip(string Id, string Title, float Seconds, float Stride, float Duty, float Lift,
    IReadOnlyList<float> FootPhases, IReadOnlyList<(float Time, GalvaMotionPose Pose)> Keys)
{
    public bool Walk => FootPhases.Count != 0;
    public float Travel => Walk ? 2f * Stride / Duty : 0f;

    public GalvaMotionPose At(float phase)
    {
        var guard = GalvaMotionPose.Guard;
        if (Walk)
        {
            var cycle = phase * 4f * MathF.PI;
            var flick = MathF.Sin(cycle);
            return guard with
            {
                Root = new(0f, -Lift * 0.19f * (1f - MathF.Cos(cycle * 2f)), Travel * phase),
                Thorax = new(0.025f * flick, 0.08f, 0.042f * flick),
                Left = guard.Left with { Wrist = guard.Left.Wrist + Vector3.UnitY * 0.065f * flick },
                Right = guard.Right with { Wrist = guard.Right.Wrist - Vector3.UnitY * 0.065f * flick },
            };
        }

        for (var i = 1; i < Keys.Count; i++)
        {
            if (phase <= Keys[i].Time)
            {
                var a = Keys[i - 1];
                var b = Keys[i];
                var t = (phase - a.Time) / (b.Time - a.Time);
                return GalvaMotionPose.Blend(a.Pose, b.Pose, Smooth(t));
            }
        }
        return Keys[^1].Pose;
    }

    public Vector3 Foot(int index, float phase)
    {
        var p = (2f * phase) + FootPhases[index];
        p -= MathF.Floor(p);
        if (p < Duty)
        {
            return new Vector3(0f, 0f, Stride * (0.5f - p / Duty));
        }

        var swing = (p - Duty) / (1f - Duty);
        return new Vector3(0f, Lift * MathF.Pow(MathF.Sin(MathF.PI * swing), 2f), Stride * (Smooth(swing) - 0.5f));
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);
}

internal static class GalvaClips
{
    private static readonly GalvaMotionPose Guard = GalvaMotionPose.Guard;

    public static IReadOnlyList<GalvaClip> All { get; } =
    [
        new("stalk", "Stalk  /  four-beat walk", 3.5f, 0.35f, 0.76f, 0.105f, [0f, 0.5f, 0.75f, 0.25f], []),
        new("trot", "Trot  /  diagonal pairs", 2.45f, 0.43f, 0.57f, 0.16f, [0f, 0.5f, 0.5f, 0f], []),
        Attack("sweep", "Claw  /  horizontal sweep", 2.1f,
            (0.27f, Guard with { Root = new(-0.065f, -0.08f, -0.13f), Thorax = new(-0.34f, 0.06f, 0.06f),
                Right = new(new(-0.67f, 1.09f, 0.31f), new(-0.92f, 0.02f, 0.37f)) }),
            (0.49f, Guard with { Root = new(0.06f, -0.03f, 0.14f), Thorax = new(0.23f, 0.15f, -0.10f),
                Right = new(new(-0.10f, 1.11f, 0.70f), new(0.04f, -0.04f, 1f)) }),
            (0.63f, Guard with { Root = new(0.10f, -0.07f, 0.20f), Thorax = new(0.46f, 0.17f, -0.13f),
                Right = new(new(0.24f, 0.98f, 0.44f), new(0.88f, -0.18f, 0.28f)) })),
        Attack("cleave", "Both claws  /  overhead cleave", 2.25f,
            (0.33f, Guard with { Root = new(0f, 0.045f, -0.10f), Thorax = new(0f, -0.14f, 0f),
                Left = new(new(0.36f, 1.70f, 0.34f), new(0.18f, 0.66f, 0.72f)),
                Right = new(new(-0.36f, 1.70f, 0.34f), new(-0.18f, 0.66f, 0.72f)) }),
            (0.49f, Guard with { Root = new(0f, -0.04f, 0.13f), Thorax = new(0f, 0.18f, 0f),
                Left = new(new(0.32f, 1.30f, 0.64f), new(0.07f, -0.24f, 0.96f)),
                Right = new(new(-0.32f, 1.30f, 0.64f), new(-0.07f, -0.24f, 0.96f)) }),
            (0.62f, Guard with { Root = new(0f, -0.14f, 0.18f), Thorax = new(0f, 0.28f, 0f),
                Left = new(new(0.27f, 0.88f, 0.65f), new(0.06f, -0.80f, 0.60f)),
                Right = new(new(-0.27f, 0.88f, 0.65f), new(-0.06f, -0.80f, 0.60f)) })),
        Attack("claw-thrust", "Claw  /  lunge and thrust", 1.95f,
            (0.32f, Guard with { Root = new(0f, -0.08f, -0.11f), Thorax = new(0.12f, -0.04f, 0f),
                Left = new(new(0.36f, 1.03f, 0.18f), new(0.07f, -0.06f, 1f)) }),
            (0.50f, Guard with { Root = new(0.03f, -0.09f, 0.34f), Thorax = new(-0.22f, 0.22f, -0.06f),
                Left = new(new(0.23f, 1.01f, 0.93f), new(-0.06f, 0.04f, 1f)) }),
            (0.64f, Guard with { Root = new(0.02f, -0.08f, 0.29f), Thorax = new(-0.13f, 0.17f, -0.04f),
                Left = new(new(0.22f, 1.04f, 0.81f), new(-0.06f, -0.12f, 1f)) })),
        Attack("foreleg-thrust", "Foreleg  /  tarsus thrust", 2.25f,
            (0.32f, Guard with { Root = new(0.13f, -0.08f, -0.02f), Thorax = new(0.13f, -0.08f, -0.14f),
                Foreleg = new(-0.10f, 0.34f, -0.14f), ForelegAxis = new(0f, -0.70f, 0.71f) }),
            (0.50f, Guard with { Root = new(0.08f, -0.08f, 0.23f), Thorax = new(-0.16f, 0.11f, -0.07f),
                Foreleg = new(0.03f, 0.60f, 0.85f), ForelegAxis = new(0f, -0.04f, 1f) }),
            (0.64f, Guard with { Root = new(0.11f, -0.07f, 0.17f), Thorax = new(-0.10f, 0.09f, -0.09f),
                Foreleg = new(-0.08f, 0.40f, 0.55f), ForelegAxis = new(0f, -0.24f, 0.97f) })),
    ];

    private static GalvaClip Attack(string id, string title, float seconds, params (float Time, GalvaMotionPose Pose)[] keys) =>
        new(id, title, seconds, 0f, 0f, 0f, [], [(0f, Guard), .. keys, (1f, Guard)]);
}
