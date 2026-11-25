using System.Numerics;

namespace MiniRing.Arena;

internal readonly record struct GalvaHandPose(Vector3 Wrist, Vector3 Blade, Vector3 Bend, Vector3 Elbow)
{
    public GalvaHandPose Offset(GalvaHandPose from, GalvaHandPose to, float weight) => new(
        Wrist + (to.Wrist - from.Wrist) * weight, Blade + (to.Blade - from.Blade) * weight,
        Bend + (to.Bend - from.Bend) * weight, Elbow + (to.Elbow - from.Elbow) * weight);
}

internal readonly record struct GalvaMotionPose(
    Vector3 Root, Vector3 Thorax, GalvaHandPose Left, GalvaHandPose Right,
    Vector3 Foreleg, Vector3 ForelegAxis)
{
    public static GalvaMotionPose Guard => new(Vector3.Zero, Vector3.Zero,
        new(new(0.45f, 1.04f, 0.53f), new(0.06f, -0.64f, -0.77f),
            new(-0.65f, 0.50f, -0.55f), new(1f, -0.39f, -0.35f)),
        new(new(-0.45f, 1.04f, 0.53f), new(-0.06f, -0.64f, -0.77f),
            new(0.65f, 0.50f, -0.55f), new(-1f, -0.39f, -0.35f)),
        Vector3.Zero, -Vector3.UnitY);

    public GalvaMotionPose Offset(GalvaMotionPose from, GalvaMotionPose to, float weight) => new(
        Root + (to.Root - from.Root) * weight, Thorax + (to.Thorax - from.Thorax) * weight,
        Left.Offset(from.Left, to.Left, weight), Right.Offset(from.Right, to.Right, weight),
        Foreleg + (to.Foreleg - from.Foreleg) * weight, ForelegAxis + (to.ForelegAxis - from.ForelegAxis) * weight);
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
                var span = b.Time - a.Time;
                var t = (phase - a.Time) / span;
                var result = a.Pose.Offset(a.Pose, b.Pose, Smooth(t));
                // Time-scaled Hermite tangents are shared by adjacent spans. Only the clip endpoints come to rest.
                if (i > 1)
                {
                    var before = Keys[i - 2];
                    result = result.Offset(before.Pose, b.Pose, t * (1f - t) * (1f - t) * span / (b.Time - before.Time));
                }
                if (i + 1 < Keys.Count)
                {
                    var after = Keys[i + 1];
                    result = result.Offset(a.Pose, after.Pose, t * t * (t - 1f) * span / (after.Time - a.Time));
                }
                return result;
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
    private static readonly GalvaHandPose KesaRight = Guard.Right with { Elbow = new(-1f, -1.5f, 0.5f) };

    public static IReadOnlyList<GalvaClip> All { get; } =
    [
        new("stalk", "Stalk  /  four-beat walk", 3.5f, 0.35f, 0.76f, 0.105f, [0f, 0.5f, 0.75f, 0.25f], []),
        new("trot", "Trot  /  diagonal pairs", 2.45f, 0.43f, 0.57f, 0.16f, [0f, 0.5f, 0.5f, 0f], []),
        Attack("sweep", "Claw  /  whip sweep", 2.1f,
            (0.25f, Guard with { Root = new(-0.09f, -0.07f, -0.08f), Thorax = new(0.46f, 0.03f, 0.08f),
                Right = new(new(-0.61f, 1.03f, 0.25f), new(0.08f, -0.46f, -0.89f), new(0.64f, 0.65f, -0.18f), new(-0.75f, -0.40f, -0.72f)) }),
            (0.36f, Guard with { Root = new(-0.07f, -0.07f, 0f), Thorax = new(0.16f, 0.06f, 0.03f),
                Right = new(new(-0.74f, 1.12f, 0.36f), new(-0.83f, -0.28f, -0.45f), new(0.20f, 0.93f, 0.30f), new(-0.78f, -0.56f, 0.55f)) }),
            (0.45f, Guard with { Root = new(0f, -0.06f, 0.11f), Thorax = new(-0.38f, 0.10f, -0.06f),
                Right = new(new(-0.63f, 1.14f, 0.71f), new(-0.87f, 0.04f, 0.48f), new(0.48f, 0.17f, 0.87f), new(-0.62f, -0.76f, 0.52f)) }),
            (0.54f, Guard with { Root = new(0.07f, -0.06f, 0.17f), Thorax = new(-0.54f, 0.13f, -0.10f),
                Right = new(new(-0.12f, 1.13f, 0.82f), new(-0.15f, -0.04f, 0.99f), new(0.99f, 0.02f, 0.15f), new(-0.34f, -0.90f, 0.34f)) }),
            (0.64f, Guard with { Root = new(0.11f, -0.07f, 0.17f), Thorax = new(-0.43f, 0.16f, -0.13f),
                Right = new(new(0.29f, 1.04f, 0.53f), new(0.87f, -0.14f, 0.47f), new(0.48f, -0.14f, -0.86f), new(-0.21f, -0.90f, -0.49f)) }),
            (0.78f, Guard with { Root = new(0.05f, -0.04f, 0.09f), Thorax = new(-0.22f, 0.08f, -0.07f),
                Right = new(new(0.07f, 1.13f, 0.58f), new(0.35f, -0.89f, -0.28f), new(0.83f, 0.26f, -0.35f), new(-0.62f, -0.91f, -0.30f)) })),
        Attack("kesa", "Claw  /  diagonal kesa cut", 2.20f,
            (0.30f, Guard with { Root = new(-0.10f, 0.04f, -0.08f), Thorax = new(0.32f, -0.11f, 0.16f),
                Right = Strike(KesaRight, new(-0.60f, 1.54f, 0.31f), new(-0.55f, 0.53f, -0.65f), new(0.80f, 0.33f, -0.30f)) }),
            (0.42f, Guard with { Root = new(-0.06f, 0.03f, 0.02f), Thorax = new(0.09f, 0.01f, 0.07f),
                Right = Strike(KesaRight, new(-0.59f, 1.49f, 0.64f), new(-0.85f, 0.33f, 0.42f), new(0.49f, -0.30f, 0.82f)) }),
            (0.51f, Guard with { Root = new(0.04f, -0.05f, 0.14f), Thorax = new(-0.25f, 0.16f, -0.10f),
                Right = Strike(KesaRight, new(-0.25f, 1.22f, 0.80f), new(-0.33f, -0.26f, 0.91f), new(0.60f, -0.78f, 0.02f)) }),
            (0.62f, Guard with { Root = new(0.11f, -0.11f, 0.18f), Thorax = new(-0.44f, 0.22f, -0.20f),
                Right = Strike(KesaRight, new(0.24f, 0.86f, 0.58f), new(0.61f, -0.51f, 0.61f), new(0.61f, -0.10f, -0.78f)) }),
            (0.76f, Guard with { Root = new(0.06f, -0.07f, 0.08f), Thorax = new(-0.24f, 0.12f, -0.10f),
                Right = Strike(KesaRight, new(0.14f, 1.04f, 0.54f), new(0.33f, -0.84f, -0.43f), new(0.67f, 0.47f, -0.19f)) })),
        Attack("cleave", "Both claws  /  overhead cleave", 2.25f,
            (0.17f, Guard with { Root = new(0f, 0.015f, -0.06f), Thorax = new(0f, -0.08f, 0f),
                Left = Strike(Guard.Left, new(0.42f, 1.38f, 0.44f), new(0.24f, -0.88f, 0.40f), new(-0.90f, 0.02f, 0.37f)),
                Right = Strike(Guard.Right, new(-0.42f, 1.38f, 0.44f), new(-0.24f, -0.88f, 0.40f), new(0.90f, 0.02f, 0.37f)) }),
            (0.33f, Guard with { Root = new(0f, 0.045f, -0.10f), Thorax = new(0f, -0.14f, 0f),
                Left = Strike(Guard.Left, new(0.36f, 1.70f, 0.34f), new(0.18f, 0.66f, 0.72f), new(-0.55f, -0.56f, 0.61f)),
                Right = Strike(Guard.Right, new(-0.36f, 1.70f, 0.34f), new(-0.18f, 0.66f, 0.72f), new(0.55f, -0.56f, 0.61f)) }),
            (0.49f, Guard with { Root = new(0f, -0.04f, 0.13f), Thorax = new(0f, 0.18f, 0f),
                Left = Strike(Guard.Left, new(0.32f, 1.30f, 0.64f), new(0.07f, -0.24f, 0.96f), new(-0.32f, -0.88f, -0.20f)),
                Right = Strike(Guard.Right, new(-0.32f, 1.30f, 0.64f), new(-0.07f, -0.24f, 0.96f), new(0.32f, -0.88f, -0.20f)) }),
            (0.62f, Guard with { Root = new(0f, -0.14f, 0.18f), Thorax = new(0f, 0.28f, 0f),
                Left = Strike(Guard.Left, new(0.33f, 0.97f, 0.65f), new(0.06f, -0.75f, 0.65f), new(-0.38f, -0.60f, -0.65f)),
                Right = Strike(Guard.Right, new(-0.33f, 0.97f, 0.65f), new(-0.06f, -0.75f, 0.65f), new(0.38f, -0.60f, -0.65f)) })),
        Attack("claw-thrust", "Claw  /  lunge and thrust", 1.95f,
            (0.32f, Guard with { Root = new(0f, -0.08f, -0.11f), Thorax = new(0.12f, -0.04f, 0f),
                Left = Strike(Guard.Left, new(0.36f, 1.03f, 0.18f), new(0.15f, -0.68f, 0.72f), new(-0.89f, 0.38f, 0.24f)) }),
            (0.50f, Guard with { Root = new(0.03f, -0.09f, 0.34f), Thorax = new(-0.22f, 0.22f, -0.06f),
                Right = Guard.Right with { Wrist = new(-0.51f, 1.10f, 0.78f) },
                Left = Strike(Guard.Left, new(0.23f, 1.01f, 0.93f), new(-0.06f, 0.04f, 1f), new(-0.92f, 0.39f, -0.07f)) }),
            (0.64f, Guard with { Root = new(0.02f, -0.08f, 0.29f), Thorax = new(-0.13f, 0.17f, -0.04f),
                Right = Guard.Right with { Wrist = new(-0.50f, 1.08f, 0.70f) },
                Left = Strike(Guard.Left, new(0.22f, 1.04f, 0.81f), new(-0.06f, -0.12f, 1f), new(-0.72f, 0.68f, 0.04f)) })),
        Attack("foreleg-thrust", "Foreleg  /  tarsus thrust", 2.25f,
            (0.32f, Guard with { Root = new(0.13f, -0.08f, -0.02f), Thorax = new(0.13f, -0.08f, -0.14f),
                Foreleg = new(-0.10f, 0.34f, -0.14f), ForelegAxis = new(0f, -0.70f, 0.71f) }),
            (0.50f, Guard with { Root = new(0.08f, -0.08f, 0.23f), Thorax = new(-0.16f, 0.11f, -0.07f),
                Foreleg = new(0.03f, 0.60f, 0.85f), ForelegAxis = new(0f, -0.04f, 1f) }),
            (0.64f, Guard with { Root = new(0.11f, -0.07f, 0.17f), Thorax = new(-0.10f, 0.09f, -0.09f),
                Foreleg = new(-0.08f, 0.40f, 0.55f), ForelegAxis = new(0f, -0.24f, 0.97f) })),
    ];

    private static GalvaHandPose Strike(GalvaHandPose hand, Vector3 wrist, Vector3 blade, Vector3 bend) =>
        hand with { Wrist = wrist, Blade = blade, Bend = bend };

    private static GalvaClip Attack(string id, string title, float seconds, params (float Time, GalvaMotionPose Pose)[] keys) =>
        new(id, title, seconds, 0f, 0f, 0f, [], [(0f, Guard), .. keys, (1f, Guard)]);
}
