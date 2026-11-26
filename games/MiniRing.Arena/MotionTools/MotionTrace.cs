using System.Globalization;
using System.Numerics;
using System.Text.Json;
using MiniRing.Weapons;

namespace MiniRing.Arena.MotionTools;

/// <summary>1 時刻の体の様子（観測だけ。試合の計算には入らない）。</summary>
internal sealed record MotionSample(
    float Time,
    Vector3[] Points,
    Vector3 LeftTarget,
    Vector3 RightTarget,
    bool[] Contacts,
    float MaxGap,
    string GapJoint,
    float EdgeDegrees,
    float Stamina,
    bool Down,
    string? Hit,
    int SelfCells,
    Vector3 Forward,
    Vector3 OpponentHead,
    float Concussion);

/// <summary>
/// 1 人のファイターの動きの記録（docs/plans/MOTION_TOOLS_PLAN.md）。<see cref="Record"/> を物理の 1 歩ごとに呼ぶと、<see cref="Every"/> 歩に 1 回、
/// 頭・腰・両手・両足（つま先・かかと）・刃先などの点と、足の接地・関節の隙間・刃筋・目標の手の位置を覚える。<see cref="Save"/> で JSON にして、
/// tools/motioncard.py が絵にし、<see cref="MotionAudit"/> が不自然な瞬間を探す。何が撮られたか（場面・版・種）も見出しに残す。
/// </summary>
internal sealed class MotionTrace
{
    /// <summary>点の名前（<see cref="MotionSample.Points"/> の順）。</summary>
    public static readonly string[] PointNames =
    [
        "head", "chest", "pelvis", "com",
        "shoulderL", "elbowL", "handL", "shoulderR", "elbowR", "handR",
        "hipL", "kneeL", "ankleL", "heelL", "toeL",
        "hipR", "kneeR", "ankleR", "heelR", "toeR",
        "tip",
    ];

    /// <summary>接地の名前（<see cref="MotionSample.Contacts"/> の順）。</summary>
    public static readonly string[] ContactNames = ["heelL", "toeL", "heelR", "toeR"];

    private const float StepSeconds = 1f / 240f;

    private int _steps;

    public MotionTrace(string scene, string subject, int seed, float legLength, float armReach, int every = 2)
    {
        Scene = scene;
        Subject = subject;
        Seed = seed;
        LegLength = legLength;
        ArmReach = armReach;
        Every = Math.Max(1, every);
    }

    public string Scene { get; }

    public string Subject { get; }

    public int Seed { get; }

    public float LegLength { get; }

    public float ArmReach { get; }

    public int Every { get; }

    public float Dt => Every * StepSeconds;

    public List<MotionSample> Frames { get; } = [];

    /// <summary>添字 <paramref name="index"/>（<see cref="PointNames"/>）の点の時系列。</summary>
    public Vector3[] Series(int index) => [.. Frames.Select(f => f.Points[index])];

    public Vector3[] Series(string name) => Series(Array.IndexOf(PointNames, name));

    /// <summary>物理の 1 歩の後に呼ぶ。</summary>
    public void Record(BoxingGame game, Fighter me, float time)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(me);
        var step = _steps++;
        var hits = game.RecentHits.Where(h => h.Attacker == me).Select(h => $"{(h.Kick ? "Kick" : h.Hand.ToString())}:{h.Zone}").ToList();
        var selfCells = SelfCells(game, me);
        if (step % Every != 0)
        {
            // 間引いた歩の当たり・自傷も落とさない（次に覚える時刻に足す）。
            _pendingHits.AddRange(hits);
            _pendingSelf += selfCells;
            return;
        }

        hits.AddRange(_pendingHits);
        selfCells += _pendingSelf;
        _pendingHits.Clear();
        _pendingSelf = 0;
        var opponent = game.Player == me ? game.Cpu : game.Player;
        var world = game.World;
        var weapon = game.WeaponOf(me);
        Vector3 tip;
        var edge = float.NaN;
        if (weapon is not null)
        {
            var s = world.GetState(weapon.Body.BodyId);
            var offset = Vector3.Transform(Weapon.TipOf(weapon.Kind), s.Orientation);
            tip = s.Position + offset;
            var tipVelocity = s.LinearVelocity + Vector3.Cross(s.AngularVelocity, offset);
            edge = tipVelocity.Length() > 1f ? MotionMetrics.EdgeOffPlaneDegrees(tipVelocity, s.Orientation) : float.NaN;
        }
        else
        {
            tip = me.BladeTipWorld(Hand.Right) ?? me.BladeTipWorld(Hand.Left) ?? me.GloveCenter(Hand.Right);
        }

        var contacts = new bool[4];
        int[] ids = [me.HeelId(0), me.ToeId(0), me.HeelId(1), me.ToeId(1)];
        foreach (var c in world.Contacts())
        {
            for (var k = 0; k < ids.Length; k++)
            {
                contacts[k] |= c.Depth >= 0f && ((c.A == ids[k] && c.StaticB) || (c.B == ids[k] && c.StaticA));
            }
        }

        var gap = 0f;
        var gapJoint = "";
        foreach (var (joint, g) in me.JointGaps())
        {
            if (g > gap)
            {
                gap = g;
                gapJoint = joint;
            }
        }

        var forward = opponent.ChestPosition - me.ChestPosition;
        forward.Y = 0f;
        forward = forward.LengthSquared() < 1e-8f ? Vector3.UnitZ : Vector3.Normalize(forward);
        Vector3[] points =
        [
            me.HeadPosition, me.ChestPosition, me.PelvisPosition, me.CenterOfMass,
            me.ShoulderPosition(Hand.Left), me.ElbowPosition(Hand.Left), me.GloveCenter(Hand.Left),
            me.ShoulderPosition(Hand.Right), me.ElbowPosition(Hand.Right), me.GloveCenter(Hand.Right),
            Hip(world, me, 1f), me.KneePosition(Hand.Left), me.AnklePosition(Hand.Left), me.HeelPosition(Hand.Left), world.GetState(me.ToeId(0)).Position,
            Hip(world, me, -1f), me.KneePosition(Hand.Right), me.AnklePosition(Hand.Right), me.HeelPosition(Hand.Right), world.GetState(me.ToeId(1)).Position,
            tip,
        ];
        Frames.Add(new MotionSample(time, points, me.HandTargetWorld(Hand.Left), me.HandTargetWorld(Hand.Right), contacts, gap, gapJoint, edge, me.Stamina,
            me.State != FighterState.Standing, hits.Count == 0 ? null : string.Join("+", hits), selfCells, forward, opponent.HeadPosition, me.Concussion));
    }

    /// <summary>股関節の中心どうしの距離の半分 [m]（大人の左右の股関節の中心は 17〜18 cm 離れる）。絵と腰の線のためだけの値。</summary>
    public const float HipHalfWidth = 0.09f;

    /// <summary>
    /// 股関節の点: 腰の剛体の左右（ローカル +X が左: <c>HandExtensions.LocalSide</c>）へ <see cref="HipHalfWidth"/>。この体の両腿は腰の真ん中の 1 点に
    /// つながっていて（股関節の拘束の点が左右で同じ）、関節の点からは腰の線（胴のひねり・左右の幅）が測れないので、腰の向きから置く。
    /// </summary>
    private static Vector3 Hip(Aigis.Physics.PhysicsWorld world, Fighter me, float side)
    {
        var pelvis = world.GetState(me.Parts.Pelvis);
        return pelvis.Position + Vector3.Transform(new Vector3(side * HipHalfWidth, 0f, 0f), pelvis.Orientation);
    }

    private readonly List<string> _pendingHits = [];
    private int _pendingSelf;

    private static int SelfCells(BoxingGame game, Fighter me)
    {
        var tools = new HashSet<object>();
        if (game.WeaponOf(me) is { } w)
        {
            tools.Add(w.Body);
        }

        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            if (game.GloveOf(me, hand) is { } g)
            {
                tools.Add(g.Body);
            }
        }

        if (tools.Count == 0 || me.Voxels is null)
        {
            return 0;
        }

        var own = me.Voxels.Parts.Select(p => (object)p.Body).ToHashSet();
        var cells = 0;
        foreach (var cut in game.RecentCuts)
        {
            cells += tools.Contains(cut.A) && own.Contains(cut.B) ? cut.RemovedB.Count : 0;
        }

        return cells;
    }

    /// <summary>JSON に書く（tools/motioncard.py が読む）。<paramref name="findings"/> と <paramref name="captions"/> も一緒に置く。</summary>
    public void Save(string path, IReadOnlyList<MotionFinding> findings, IReadOnlyList<MotionCaption> captions, IReadOnlyDictionary<string, string>? extra = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        using var json = new Utf8JsonWriter(stream);
        json.WriteStartObject();
        json.WriteString("scene", Scene);
        json.WriteString("subject", Subject);
        json.WriteNumber("seed", Seed);
        json.WriteNumber("physicsVersion", Fighter.PhysicsVersion);
        json.WriteNumber("logicVersion", Bots.BoxerBot.LogicVersion);
        json.WriteNumber("dt", Dt);
        json.WriteNumber("legLength", LegLength);
        json.WriteNumber("armReach", ArmReach);
        foreach (var (k, v) in extra ?? new Dictionary<string, string>())
        {
            json.WriteString(k, v);
        }

        json.WriteStartArray("points");
        foreach (var n in PointNames)
        {
            json.WriteStringValue(n);
        }

        json.WriteEndArray();
        json.WriteStartArray("contactNames");
        foreach (var n in ContactNames)
        {
            json.WriteStringValue(n);
        }

        json.WriteEndArray();
        json.WriteStartArray("frames");
        foreach (var f in Frames)
        {
            json.WriteStartObject();
            json.WriteNumber("t", Round(f.Time, 4));
            json.WriteStartArray("p");
            foreach (var p in f.Points)
            {
                WriteVector(json, p);
            }

            json.WriteEndArray();
            json.WriteStartArray("target");
            WriteVector(json, f.LeftTarget);
            WriteVector(json, f.RightTarget);
            json.WriteEndArray();
            json.WriteStartArray("c");
            foreach (var c in f.Contacts)
            {
                json.WriteNumberValue(c ? 1 : 0);
            }

            json.WriteEndArray();
            json.WriteNumber("gap", Round(f.MaxGap, 4));
            json.WriteString("gapJoint", f.GapJoint);
            if (!float.IsNaN(f.EdgeDegrees))
            {
                json.WriteNumber("edge", Round(f.EdgeDegrees, 1));
            }

            json.WriteNumber("stamina", Round(f.Stamina, 4));
            json.WriteBoolean("down", f.Down);
            if (f.Hit is not null)
            {
                json.WriteString("hit", f.Hit);
            }

            if (f.SelfCells > 0)
            {
                json.WriteNumber("self", f.SelfCells);
            }

            json.WriteStartArray("fwd");
            WriteVector(json, f.Forward);
            json.WriteEndArray();
            json.WriteStartArray("opp");
            WriteVector(json, f.OpponentHead);
            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("findings");
        foreach (var x in findings)
        {
            json.WriteStartObject();
            json.WriteString("kind", x.Kind);
            json.WriteString("where", x.Where);
            json.WriteNumber("from", Round(x.From, 3));
            json.WriteNumber("to", Round(x.To, 3));
            json.WriteNumber("value", Round(x.Value, 3));
            json.WriteString("text", x.Text);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("captions");
        foreach (var c in captions)
        {
            json.WriteStartObject();
            json.WriteNumber("t", Round(c.Time, 3));
            json.WriteString("text", c.Text);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static double Round(float v, int digits) => Math.Round(v, digits);

    private static void WriteVector(Utf8JsonWriter json, Vector3 v)
    {
        json.WriteNumberValue(Round(v.X, 4));
        json.WriteNumberValue(Round(v.Y, 4));
        json.WriteNumberValue(Round(v.Z, 4));
    }

    /// <summary>1 行の要約（ログ用）。</summary>
    public string Summary() => string.Create(CultureInfo.InvariantCulture, $"{Scene} [{Subject}] {Frames.Count} frames, {(Frames.Count == 0 ? 0 : Frames[^1].Time - Frames[0].Time):0.00} s");

    internal static string F(float v, string format = "0.0") => v.ToString(format, CultureInfo.InvariantCulture);
}
