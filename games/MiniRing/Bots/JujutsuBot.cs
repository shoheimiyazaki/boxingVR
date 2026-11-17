using System.Numerics;

namespace MiniRing.Bots;

/// <summary>
/// 柔術家の入力元: 普段は打撃の教科書のボット（<see cref="Inner"/>）のまま、組める間合いに入ったら組み技の教科書（<see cref="GrappleBook"/>）の
/// 技を 1 つ選んで、その目標の経路を流す。出すのは <see cref="FighterCommand"/> だけ（§1.1）。投げが決まるか・関節が壊れるかは物理と材料が決める。
/// 技の選び方は場合分けでなく重み: 教科書の好み ×（崩す向きと、相手の支えがいちばん狭い向き・重心の動く向きとの合い具合）
/// ×（関節技なら相手の腕の伸び具合）。決定的（種付きの乱数、§1.6）。
/// 自分の肘が極められたときの逃げ（痛みの反射）も持つ: 痛む腕の拳を、引かれる向きへ預けて荷重を逃がす。
/// </summary>
public sealed class JujutsuBot : IFighterControl
{
    private readonly Random _random;
    private GrappleTechnique? _technique;
    private float _time;
    private float _cooldown;
    private float _gripWait;
    private Vector3 _leftHold;
    private Vector3 _rightHold;

    public JujutsuBot(IFighterControl inner, GrappleBook book, int seed, string? only = null)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        Book = book ?? throw new ArgumentNullException(nameof(book));
        _random = new Random(seed ^ 0x4a75);
        Only = only;
    }

    public IFighterControl Inner { get; }

    /// <summary>立ち位置の目標までのずれがこれ（腕の長さ単位）で全速（歩く速さ）になる。</summary>
    private const float StandGain = 0.3f;

    public GrappleBook Book { get; }

    /// <summary>この技だけを使う（ドリル用）。null なら教科書から選ぶ。</summary>
    public string? Only { get; }

    /// <summary>技をかけていない理由（観戦・調べもの用）。</summary>
    public string Waiting { get; private set; } = "";

    /// <summary>今かけている技（無ければ null）。</summary>
    public GrappleTechnique? Current => _technique;

    /// <summary>技をかけ始めた回数（技の名前ごと）。</summary>
    public Dictionary<string, int> Attempts { get; } = [];

    /// <summary>掴めずにあきらめた回数（技の名前ごと）。</summary>
    public Dictionary<string, int> Missed { get; } = [];

    public FighterCommand Read(in FighterSense sense, float dt)
    {
        var basis = Inner.Read(sense, dt);
        _cooldown = MathF.Max(0f, _cooldown - dt);
        if (sense.Grapple is not { } grapple)
        {
            Waiting = "no grapple sense";
            return basis;
        }

        Waiting = !sense.OpponentAlive ? "opponent down" : _cooldown > 0f ? "cooldown" : !InRange(sense) ? "far" : "rolling";

        if (_technique is null)
        {
            basis = Escape(sense, grapple, basis);
            if (!sense.OpponentAlive || _cooldown > 0f || !InRange(sense) || _random.NextSingle() >= Book.Rate * dt)
            {
                return basis;
            }

            _technique = Choose(sense, grapple);
            if (_technique is null)
            {
                return basis;
            }

            _time = 0f;
            _gripWait = 0f;
            _leftHold = AiControl.Guard(Hand.Left, sense.ArmReach);
            _rightHold = AiControl.Guard(Hand.Right, sense.ArmReach);
            Attempts[_technique.Name] = Attempts.GetValueOrDefault(_technique.Name) + 1;
        }

        return Run(sense, grapple, basis, dt);
    }

    private bool InRange(in FighterSense sense)
        => Vector3.Distance(sense.Self.Origin, sense.OpponentChest) <= Book.Engage * sense.ArmReach;

    /// <summary>今の相手の崩れ方に合った技を、重みで選ぶ（<see cref="Only"/> ならそれ）。</summary>
    private GrappleTechnique? Choose(in FighterSense sense, in GrappleSense grapple)
    {
        if (Only is { } only)
        {
            return Book.Technique(only);
        }

        var weak = WeakDirection(sense, grapple);
        var extension = MathF.Max(ArmExtension(grapple, Hand.Left, sense), ArmExtension(grapple, Hand.Right, sense));
        var total = 0f;
        Span<float> scores = stackalloc float[Book.Techniques.Count];
        for (var i = 0; i < Book.Techniques.Count; i++)
        {
            var t = Book.Techniques[i];
            var k = t.KuzushiLocal;
            var fit = k.LengthSquared() > 1e-6f ? 0.5f + (0.5f * Vector2.Dot(Vector2.Normalize(k), weak)) : 1f;
            var score = t.Weight * fit * (1f + (t.Extension * (extension - 1f))); // 腕の伸びを求める度合い（0 = 問わない、1 = 伸びきった腕だけ）
            scores[i] = MathF.Max(0f, score);
            total += scores[i];
        }

        if (total <= 0f)
        {
            return null;
        }

        var pick = _random.NextSingle() * total;
        for (var i = 0; i < scores.Length; i++)
        {
            pick -= scores[i];
            if (pick <= 0f)
            {
                return Book.Techniques[i];
            }
        }

        return Book.Techniques[^1];
    }

    /// <summary>
    /// 相手の崩れやすい向き（自分の体の座標の水平、x = 左、y = 前）: 両足首を結ぶ線に直交する向き（支えが狭い向き）を、
    /// 重心が足の真ん中からずれている・動いている側へ向けたもの。
    /// </summary>
    private static Vector2 WeakDirection(in FighterSense sense, in GrappleSense grapple)
    {
        var self = sense.Self;
        Vector2 Local(Vector3 world)
        {
            var l = self.WorldToLocal(world);
            return new Vector2(l.X, l.Z);
        }

        var a = Local(grapple.OpponentLeftAnkle);
        var b = Local(grapple.OpponentRightAnkle);
        var feet = b - a;
        var normal = feet.LengthSquared() > 1e-8f ? Vector2.Normalize(new Vector2(-feet.Y, feet.X)) : new Vector2(0f, 1f);
        var com = Local(grapple.OpponentCom);
        var velocity = sense.Self.WorldToLocal(sense.Self.Origin + grapple.OpponentComVelocity);
        var lean = (com - ((a + b) * 0.5f)) + (0.3f * new Vector2(velocity.X, velocity.Z));
        return Vector2.Dot(normal, lean) >= 0f ? normal : -normal;
    }

    /// <summary>相手の腕の伸び具合（0 = 曲げきり、1 = 伸びきり）: 肩 → 手首の距離の、肩 → 肘 → 手首の道のりに対する比を 0.7..1 で 0..1 へ。</summary>
    private static float ArmExtension(in GrappleSense grapple, Hand opponentHand, in FighterSense sense)
    {
        var shoulder = sense.OpponentBody.Shoulder(opponentHand);
        var elbow = grapple.OpponentElbow(opponentHand);
        var wrist = grapple.OpponentWrist(opponentHand);
        var path = Vector3.Distance(shoulder, elbow) + Vector3.Distance(elbow, wrist);
        var ratio = path > 1e-6f ? Vector3.Distance(shoulder, wrist) / path : 0f;
        return Math.Clamp((ratio - 0.7f) / 0.3f, 0f, 1f);
    }

    private FighterCommand Run(in FighterSense sense, in GrappleSense grapple, FighterCommand basis, float dt)
    {
        var technique = _technique!;
        _time += dt;
        var keys = technique.Keys;
        var next = 0;
        while (next < keys.Count && keys[next].T < _time)
        {
            next++;
        }

        if (next >= keys.Count)
        {
            Finish();
            return basis with { Left = basis.Left with { Grip = false }, Right = basis.Right with { Grip = false } };
        }

        var before = keys[Math.Max(0, next - 1)];
        var after = keys[next];
        var span = after.T - before.T;
        var u = span > 1e-6f ? Math.Clamp((_time - before.T) / span, 0f, 1f) : 1f;

        var reach = sense.ArmReach;
        var left = HandTarget(sense, grapple, before.Left, after.Left, u, ref _leftHold);
        var right = HandTarget(sense, grapple, before.Right, after.Right, u, ref _rightHold);
        var gripLeft = (before.Left ?? after.Left)?.Grip ?? false;
        var gripRight = (before.Right ?? after.Right)?.Grip ?? false;

        // 掴みに行ったのに掴めていなければ、少し待ってからあきらめる（掴めなかった手で投げの続きを流さない）。
        var wantsHold = (gripLeft && !grapple.HoldingLeft) || (gripRight && !grapple.HoldingRight);
        _gripWait = wantsHold ? _gripWait + dt : 0f;
        if (_gripWait > Book.GripTimeout)
        {
            Missed[technique.Name] = Missed.GetValueOrDefault(technique.Name) + 1;
            Finish();
            return basis;
        }

        var move = Lerp2(before.Move, after.Move, u);
        var moveWorld = (sense.Self.Rotate(new Vector3(move.X, 0f, move.Y)));
        if ((before.Stand ?? after.Stand) is not null)
        {
            // 立ち位置の目標へ歩く: ずれ（腕の長さ単位）に比例、歩く速さで頭打ち。
            var from = before.Stand ?? after.Stand!;
            var to = after.Stand ?? before.Stand!;
            var target = Vector3.Lerp(Point(sense, grapple, from), Point(sense, grapple, to), u);
            var gap = target - sense.Self.Origin;
            gap.Y = 0f;
            var want = gap / MathF.Max(1e-3f, StandGain * sense.ArmReach);
            moveWorld = want.LengthSquared() > 1f ? Vector3.Normalize(want) : want;
        }
        var head = Lerp3(before.Head, after.Head, u) * reach;
        LegCommand? leg = null;
        Hand legSide = Hand.Right;
        if ((before.Leg ?? after.Leg) is { } legKey)
        {
            var from = before.Leg ?? after.Leg!;
            var to = after.Leg ?? before.Leg!;
            var target = Vector3.Lerp(LegPoint(sense, grapple, from), LegPoint(sense, grapple, to), u);
            leg = new LegCommand(sense.Self.WorldToLocal(target));
            legSide = legKey.Side;
        }

        var spring = before.Spring + ((after.Spring - before.Spring) * u);
        var twist = before.Twist + ((after.Twist - before.Twist) * u);
        var face = sense.OpponentChest - sense.Self.Origin;
        face.Y = 0f;
        face = face.LengthSquared() > 1e-8f ? Vector3.Normalize(face) : sense.Self.Forward;
        var turn = before.Turn + ((after.Turn - before.Turn) * u);
        if (turn != 0f)
        {
            face = Vector3.Transform(face, Quaternion.CreateFromAxisAngle(Vector3.UnitY, turn));
        }
        return new FighterCommand(moveWorld, face,
            new HandCommand(left, gripLeft, spring),
            new HandCommand(right, gripRight, spring),
            head, twist,
            LeftLeg: legSide == Hand.Left ? leg : null,
            RightLeg: legSide == Hand.Right ? leg : null);
    }

    private void Finish()
    {
        _technique = null;
        _cooldown = Book.Cooldown;
    }

    /// <summary>手のキーの点（自分の体の座標）。キーが無い手は前の目標のまま。</summary>
    private static Vector3 HandTarget(in FighterSense sense, in GrappleSense grapple, GrappleHand? from, GrappleHand? to, float u, ref Vector3 hold)
    {
        if (from is null && to is null)
        {
            return hold;
        }

        var a = from is null ? sense.Self.LocalToWorld(hold) : Point(sense, grapple, from);
        var b = to is null ? a : Point(sense, grapple, to);
        var local = sense.Self.WorldToLocal(Vector3.Lerp(a, b, u));
        hold = local;
        return local;
    }

    private static Vector3 LegPoint(in FighterSense sense, in GrappleSense grapple, GrappleLeg leg)
        => Anchor(sense, grapple, leg.Anchor) + sense.Self.Rotate(Vec(leg.Offset) * sense.ArmReach);

    /// <summary>手の目標の点（ワールド）: 相手の点 ＋ ずれ（自分の体の座標か、相手の腕の座標）。</summary>
    private static Vector3 Point(in FighterSense sense, in GrappleSense grapple, GrappleHand hand)
    {
        var anchor = Anchor(sense, grapple, hand.Anchor);
        var offset = Vec(hand.Offset) * sense.ArmReach;
        if (!hand.Limb)
        {
            return anchor + sense.Self.Rotate(offset);
        }

        var arm = OpponentArmOnSide(sense, hand.Anchor is GrappleAnchor.ElbowL or GrappleAnchor.WristL or GrappleAnchor.ShoulderL);
        var (along, bend, side) = LimbFrame(sense, grapple, arm);
        return anchor + (along * offset.X) + (bend * offset.Y) + (side * offset.Z);
    }

    /// <summary>
    /// 相手の腕の座標: along = 肩 → 手首、bend = 肘を肩 − 手首の線の向こうへ押し出す向き（肘を反らせる向き）、side = その 2 つに直交。
    /// 腕が伸びきって肘の曲がる向きが見えないときは、下向きを肘の向きとする（構えた腕の肘は下を向く）。
    /// </summary>
    internal static (Vector3 Along, Vector3 Bend, Vector3 Side) LimbFrame(in FighterSense sense, in GrappleSense grapple, Hand opponentHand)
    {
        var shoulder = sense.OpponentBody.Shoulder(opponentHand);
        var elbow = grapple.OpponentElbow(opponentHand);
        var wrist = grapple.OpponentWrist(opponentHand);
        var along = wrist - shoulder;
        along = along.LengthSquared() > 1e-8f ? Vector3.Normalize(along) : sense.Self.Forward;
        var rel = elbow - shoulder;
        var bulge = rel - (along * Vector3.Dot(rel, along));
        var point = bulge.LengthSquared() > 1e-8f ? Vector3.Normalize(bulge) : -Vector3.UnitY;
        if (bulge.Length() < 0.01f * sense.ArmReach)
        {
            var down = -Vector3.UnitY - (along * Vector3.Dot(-Vector3.UnitY, along));
            point = down.LengthSquared() > 1e-8f ? Vector3.Normalize(down) : point;
        }

        var bend = -point;
        var side = Vector3.Normalize(Vector3.Cross(along, bend));
        return (along, bend, side);
    }

    /// <summary>自分から見て左（+X）にある相手の腕（相手の本人の左右）。</summary>
    private static Hand OpponentArmOnSide(in FighterSense sense, bool onMyLeft)
    {
        var l = sense.Self.WorldToLocal(sense.OpponentBody.LeftShoulder).X;
        var r = sense.Self.WorldToLocal(sense.OpponentBody.RightShoulder).X;
        var leftIsOnMyLeft = l >= r;
        return onMyLeft == leftIsOnMyLeft ? Hand.Left : Hand.Right;
    }

    private static Hand OpponentLegOnSide(in FighterSense sense, in GrappleSense grapple, bool onMyLeft)
    {
        var l = sense.Self.WorldToLocal(grapple.OpponentLeftAnkle).X;
        var r = sense.Self.WorldToLocal(grapple.OpponentRightAnkle).X;
        return onMyLeft == (l >= r) ? Hand.Left : Hand.Right;
    }

    private static Vector3 Anchor(in FighterSense sense, in GrappleSense grapple, GrappleAnchor anchor) => anchor switch
    {
        GrappleAnchor.Self => sense.Self.Origin,
        GrappleAnchor.Chest => sense.OpponentChest,
        GrappleAnchor.Head => sense.OpponentHead,
        GrappleAnchor.Pelvis => sense.OpponentBody.Pelvis,
        GrappleAnchor.ShoulderL => sense.OpponentBody.Shoulder(OpponentArmOnSide(sense, true)),
        GrappleAnchor.ShoulderR => sense.OpponentBody.Shoulder(OpponentArmOnSide(sense, false)),
        GrappleAnchor.ElbowL => grapple.OpponentElbow(OpponentArmOnSide(sense, true)),
        GrappleAnchor.ElbowR => grapple.OpponentElbow(OpponentArmOnSide(sense, false)),
        GrappleAnchor.WristL => grapple.OpponentWrist(OpponentArmOnSide(sense, true)),
        GrappleAnchor.WristR => grapple.OpponentWrist(OpponentArmOnSide(sense, false)),
        GrappleAnchor.AnkleL => grapple.OpponentAnkle(OpponentLegOnSide(sense, grapple, true)),
        GrappleAnchor.AnkleR => grapple.OpponentAnkle(OpponentLegOnSide(sense, grapple, false)),
        _ => sense.OpponentChest,
    };

    /// <summary>
    /// 痛みの反射: 自分の肘にかかる過伸展の力が靭帯の降伏の <see cref="GrappleBook.EscapeLoad"/> 倍を超えたら、その腕の拳を肘が曲がる向き
    /// （拳を肩へ引き寄せる）へ動かし、相手の側へ寄って荷重を逃がす。逃げきれるかは、その腕の力と相手の梃子の力の物理。
    /// </summary>
    private FighterCommand Escape(in FighterSense sense, in GrappleSense grapple, FighterCommand basis)
    {
        if (Book.EscapeLoad <= 0f)
        {
            return basis;
        }

        var command = basis;
        foreach (var hand in (ReadOnlySpan<Hand>)[Hand.Left, Hand.Right])
        {
            var load = hand == Hand.Left ? grapple.SelfElbowLoadLeft : grapple.SelfElbowLoadRight;
            if (load <= Book.EscapeLoad)
            {
                continue;
            }

            // 拳を胸の前・肩の高さへ（肘を曲げる）。体は相手へ寄る（引かれる向きへついて行く）。
            var tucked = new Vector3(hand.LocalSide() * 0.15f, 0f, 0.25f) * sense.ArmReach;
            command = hand == Hand.Left
                ? command with { Left = command.Left with { TargetLocal = tucked, Grip = false } }
                : command with { Right = command.Right with { TargetLocal = tucked, Grip = false } };
            var toward = sense.OpponentChest - sense.Self.Origin;
            toward.Y = 0f;
            command = command with { MoveWorld = toward.LengthSquared() > 1e-8f ? Vector3.Normalize(toward) * 0.6f : Vector3.Zero };
        }

        return command;
    }

    private static Vector3 Vec(float[]? v) => v is { Length: >= 3 } ? new Vector3(v[0], v[1], v[2]) : Vector3.Zero;

    private static Vector2 Lerp2(float[]? a, float[]? b, float u)
    {
        var va = a is { Length: >= 2 } ? new Vector2(a[0], a[^1]) : Vector2.Zero;
        var vb = b is { Length: >= 2 } ? new Vector2(b[0], b[^1]) : va;
        return Vector2.Lerp(va, vb, u);
    }

    private static Vector3 Lerp3(float[]? a, float[]? b, float u)
    {
        var va = Vec(a);
        var vb = b is null ? va : Vec(b);
        return Vector3.Lerp(va, vb, u);
    }

    public void OnPunchLanded(Hand hand, float damage) => Inner.OnPunchLanded(hand, damage);

    public void CarryOver(IFighterControl previous) => Inner.CarryOver(previous is JujutsuBot j ? j.Inner : previous);

    public void OnPunchTaken(in HitEvent hit) => Inner.OnPunchTaken(hit);
}
