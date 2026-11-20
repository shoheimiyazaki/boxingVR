using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

/// <summary>
/// 引っかかりの物差しとドリル（1 体がリングの上を歩く）。物差しは 2 つ。
/// (1) 足の引っかかり: 踏み出している足（Footwork が宙へ運ぼうとしている足）の母指球の目標が実際のつま先から遠く離れているのに、
///     その足の水平の速さがほぼ 0 で、足が床に触れている（箱の底がキャンバスから数 mm 以内）歩。
/// (2) 体の停滞: 移動の指令（長さ 1）に対し、直近 <see cref="Window"/> 秒に骨盤が指令の向きへ進んだ平均の速さが、歩く速さの
///     <see cref="StalledRatio"/> 未満の歩（目標の点まで 4 cm 以内に着いたら次の点へ移るので、着いて止まった分は数えない）。
/// 回数は連続の数、時間はその歩数 × dt。
/// </summary>
internal static class BootSnagDrill
{
    public const float Dt = 1f / 240f;
    public const float FarFromTarget = 0.04f;
    public const float StalledSpeed = 0.05f;
    public const float OnFloor = 0.004f;
    public const float Window = 0.5f;
    public const float StalledRatio = 0.25f;

    public readonly record struct Result(string Name, int FootSnags, float FootSnagSeconds, int Stalls, float StallSeconds, float LongestStall, float Seconds, float SpeedRatio, int FootSteps, bool Finished);

    public static (PhysicsWorld World, Fighter Fighter, Ring Ring, RingLayout Layout) Build(Vector3? offset = null, float yaw = 0f, FighterTuning? tuning = null)
    {
        var layout = RingLayout.Boxing;
        var world = new PhysicsWorld();
        LivingRoom.AddColliders(world, layout);
        var ring = new Ring(world, layout.Center, layout.Size, BoxingGame.RingSurfaceHeight, LivingRoom.PhysicsStageBase);
        var fighter = Fighter.Spawn(world, tuning ?? FighterTuning.Default, "walker", ring.SurfaceCenter + (offset ?? Vector3.Zero), yaw);
        return (world, fighter, ring, layout);
    }

    public static FighterCommand Command(Fighter f, Vector3 move, Vector3 face) => new(move, face,
        new HandCommand(AiControl.Guard(Hand.Left, f.ArmReach), false),
        new HandCommand(AiControl.Guard(Hand.Right, f.ArmReach), false));

    /// <summary>歩く道（リングの中心からの水平オフセットの列）。縁から 0.2 m 内に収める。</summary>
    public static IEnumerable<(string Name, Vector2[] Waypoints)> Paths(int seed)
    {
        const float r = 0.38f;
        yield return ("forward-back", [new(0, 0), new(0, r), new(0, -r), new(0, r), new(0, -r)]);
        yield return ("side-side", [new(0, 0), new(r, 0), new(-r, 0), new(r, 0), new(-r, 0)]);
        yield return ("diagonals", [new(0, 0), new(r, r), new(-r, -r), new(r, -r), new(-r, r), new(0, 0)]);
        yield return ("square", [new(-r, -r), new(r, -r), new(r, r), new(-r, r), new(-r, -r), new(r, -r)]);
        var circle = new List<Vector2>();
        for (var i = 0; i <= 24; i++)
        {
            var a = i * MathF.PI / 6f;
            circle.Add(new Vector2(MathF.Cos(a), MathF.Sin(a)) * r);
        }

        yield return ("circle", [.. circle]);
        var rng = new Random(seed);
        var random = Enumerable.Range(0, 10).Select(_ => new Vector2(((float)rng.NextDouble() * 2f) - 1f, ((float)rng.NextDouble() * 2f) - 1f) * r).ToArray();
        yield return ($"random-{seed}", random);
        yield return ("quick-reversals", [new(0, 0), new(0.2f, 0), new(-0.2f, 0), new(0.2f, 0.15f), new(-0.2f, -0.15f), new(0.2f, 0), new(-0.2f, 0)]);
    }

    /// <summary>1 つの道を歩く。顔は +Z（リングの奥）へ向けたまま、横も後ろも歩く。<paramref name="maxSeconds"/> までに回りきらなければ Finished が偽。</summary>
    public static Result Walk(string name, Vector2[] waypoints, float maxSeconds = 25f, Action<string>? trace = null, Action<string>? contacts = null,
        FighterTuning? tuning = null, Action<PhysicsWorld, Fighter>? setup = null)
    {
        var (world, f, ring, _) = Build(new Vector3(waypoints[0].X, 0f, waypoints[0].Y), 0f, tuning);
        using (world)
        {
            setup?.Invoke(world, f);
            var face = Vector3.UnitZ;
            for (var i = 0; i < (int)(1f / Dt); i++)
            {
                f.ApplyControl(Command(f, Vector3.Zero, face), Dt);
                world.Step(Dt);
            }

            var center = ring.SurfaceCenter;
            var ground = ring.SurfaceHeight;
            var target = 1;
            var footEvents = 0;
            var footSteps2 = 0;
            var footRun = 0;
            var stalls = 0;
            var stallSteps = 0;
            var stallRun = 0;
            var longest = 0;
            var along = 0f;
            var commanded = 0f;
            var firstStep = f.Footwork.Steps;
            var steps = 0;
            var maxSteps = (int)(maxSeconds / Dt);
            var windowLength = (int)(Window / Dt);
            var history = new Queue<float>();
            var historySum = 0f;
            var walk = (tuning ?? FighterTuning.Default).WalkSpeed;
            while (target < waypoints.Length && steps < maxSteps)
            {
                var to = center + new Vector3(waypoints[target].X, 0f, waypoints[target].Y) - f.PelvisPosition;
                to.Y = 0f;
                if (to.Length() < 0.04f)
                {
                    target++;
                    history.Clear();
                    historySum = 0f;
                    stallRun = 0;
                    continue;
                }

                var dir = Vector3.Normalize(to);
                f.ApplyControl(Command(f, dir, face), Dt);
                world.Step(Dt);
                steps++;

                var v = f.PelvisVelocity;
                var speedAlong = Vector3.Dot(new Vector3(v.X, 0f, v.Z), dir);
                along += speedAlong;
                commanded += walk;
                history.Enqueue(speedAlong);
                historySum += speedAlong;
                if (history.Count > windowLength)
                {
                    historySum -= history.Dequeue();
                }

                if (history.Count == windowLength && historySum / windowLength < StalledRatio * walk)
                {
                    stallSteps++;
                    stallRun++;
                    longest = Math.Max(longest, stallRun);
                    if (stallRun == 1)
                    {
                        stalls++;
                    }
                }
                else
                {
                    stallRun = 0;
                }

                var snag = false;
                foreach (var foot in new[] { Footwork.Lead, Footwork.Rear })
                {
                    var pose = f.Footwork.Pose(foot);
                    if (!pose.Swinging)
                    {
                        continue;
                    }

                    var toe = world.GetState(f.ToeId(foot));
                    var heel = world.GetState(f.HeelId(foot));
                    var gap = new Vector2(pose.Ball.X - toe.Position.X, pose.Ball.Z - toe.Position.Z).Length();
                    var speed = new Vector2(heel.LinearVelocity.X, heel.LinearVelocity.Z).Length();
                    var low = Math.Min(LowestY(world, f, f.ToeId(foot)), LowestY(world, f, f.HeelId(foot))) - ground;
                    snag |= gap > FarFromTarget && speed < StalledSpeed && low < OnFloor;
                }

                if (snag)
                {
                    footSteps2++;
                    footRun++;
                    if (footRun == 1)
                    {
                        footEvents++;
                    }
                }
                else
                {
                    footRun = 0;
                }

                if (trace is not null && steps % 48 == 0)
                {
                    var pp = f.PelvisPosition - center;
                    var lt = world.GetState(f.ToeId(0)).Position - center;
                    var rt = world.GetState(f.ToeId(1)).Position - center;
                    var pl = f.Footwork.Pose(0).Ball - new Vector3(center.X, 0, center.Z);
                    var pr = f.Footwork.Pose(1).Ball - new Vector3(center.X, 0, center.Z);
                    trace($"t={steps * Dt:0.0} wp{target} pelvis ({pp.X:0.000},{pp.Z:0.000}) y {pp.Y:0.000} sw {f.Footwork.Swinging} toeL ({lt.X:0.000},{lt.Y:0.000},{lt.Z:0.000}) tgtL ({pl.X:0.000},{pl.Z:0.000}) toeR ({rt.X:0.000},{rt.Y:0.000},{rt.Z:0.000}) tgtR ({pr.X:0.000},{pr.Z:0.000}) stall {stallRun} {FootDebug(f.Footwork, center)}");
                }

                if (contacts is not null && steps % 48 == 0 && stallRun > 0)
                {
                    foreach (var c in world.Contacts())
                    {
                        contacts($"  contact {ContactName(f, c.A, c.StaticA)} - {ContactName(f, c.B, c.StaticB)} n=({c.Normal.X:0.00},{c.Normal.Y:0.00},{c.Normal.Z:0.00}) depth {c.Depth * 1000f:0.0}mm pts {c.PointCount}");
                    }
                }
            }

            return new Result(name, footEvents, footSteps2 * Dt, stalls, stallSteps * Dt, longest * Dt, steps * Dt, commanded > 0 ? along / commanded : 0f, f.Footwork.Steps - firstStep, target >= waypoints.Length);
        }
    }

    private static string FootDebug(Footwork w, Vector3 center)
    {
        T Field<T>(string n) => (T)typeof(Footwork).GetField(n, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(w)!;
        var from = Field<Vector2>("_from") - new Vector2(center.X, center.Z);
        var to = Field<Vector2>("_to") - new Vector2(center.X, center.Z);
        var dur = Field<float>("_swingDuration");
        return $"swing from ({from.X:0.000},{from.Y:0.000}) to ({to.X:0.000},{to.Y:0.000}) dur {dur:0.00} yawL {Field<float[]>("_yaw")[0] * 57.3f:0} yawR {Field<float[]>("_yaw")[1] * 57.3f:0}";
    }

    private static string ContactName(Fighter f, int id, bool isStatic) => isStatic ? "STATIC" : f.AllParts.Contains(id) ? $"{f.KindOf(id)}{id}" : $"other{id}";

    private static float LowestY(PhysicsWorld world, Fighter f, int id)
    {
        var s = world.GetState(id);
        var half = f.HalfExtentsOf(id);
        var r = Matrix4x4.CreateFromQuaternion(s.Orientation);
        return s.Position.Y - ((MathF.Abs(r.M12) * half.X) + (MathF.Abs(r.M22) * half.Y) + (MathF.Abs(r.M32) * half.Z));
    }
}
