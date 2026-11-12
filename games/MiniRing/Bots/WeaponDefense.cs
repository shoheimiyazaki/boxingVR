using System.Numerics;
using MiniRing.Motion;

namespace MiniRing.Bots;

/// <summary>守りの組み立て方（ドリル <c>guardlab</c> で比べるため）。</summary>
public enum DefenseMode
{
    /// <summary>はじめの守り: 剣先だけを読み、受け（剣を剣先の動きに直角に横たえる）か外し（下がって横へ）を乱数で選ぶ。</summary>
    Legacy,

    /// <summary>受け流すだけ（足は止めたまま。<see cref="WeaponDefense.Parry"/>）。</summary>
    Parry,

    /// <summary>足と頭で外すだけ（<see cref="WeaponDefense.BestStep"/>）。</summary>
    Step,

    /// <summary>受け流しながら、足と頭でも外す。</summary>
    ParryAndStep,
}

/// <summary>
/// 相手の武器の線の今の見え方（ワールド）: 握りから先までの点と、その速度。武器は 1 本の硬い物なので、線の上の点の速度は握りと先の速度を
/// 線形に補った値でちょうど合う（剛体の速度 v + ω × r は位置について線形）。拳は長さ 0 の線（握り = 先）。
/// </summary>
/// <param name="Hilt">握り（手）の位置。</param>
/// <param name="Tip">先の位置。</param>
/// <param name="HiltVelocity">握りの速度。</param>
/// <param name="TipVelocity">先の速度。</param>
/// <param name="Shoulder">その手の肩（線の点はここから腕の長さ + 握りからの距離より先へは届かない）。</param>
/// <param name="ArmLimit">肩から握りまでが届く長さ [m]。</param>
/// <param name="BodyVelocity">相手の胴の速度（歩いているだけの動きを、振りと見分けるため）。</param>
public readonly record struct WeaponLine(Vector3 Hilt, Vector3 Tip, Vector3 HiltVelocity, Vector3 TipVelocity, Vector3 Shoulder, float ArmLimit, Vector3 BodyVelocity)
{
    /// <summary>握りから先までの長さ [m]。</summary>
    public float Length => Vector3.Distance(Hilt, Tip);

    /// <summary>線の上の点（0 = 握り、1 = 先）。</summary>
    public Vector3 At(float s) => Vector3.Lerp(Hilt, Tip, s);

    /// <summary>線の上の点の速度。</summary>
    public Vector3 VelocityAt(float s) => Vector3.Lerp(HiltVelocity, TipVelocity, s);

    /// <summary>その点が肩から届く長さ [m]（腕の長さ + 握りからの距離）。</summary>
    public float LimitAt(float s) => ArmLimit + (s * Length);

    /// <summary>振っている速さ: 胴の動きを差し引いた、いちばん速い点（先か握り）の速さ [m/s]。</summary>
    public float SwingSpeed => MathF.Max((TipVelocity - BodyVelocity).Length(), (HiltVelocity - BodyVelocity).Length());
}

/// <summary>読んだ脅威: 武器の線のどこか（<paramref name="Along"/>）が <paramref name="Time"/> 秒後に体の線へ近づく所と、その時の速度。</summary>
/// <param name="Point">近づく所（ワールド）。</param>
/// <param name="Velocity">その点の速度。</param>
/// <param name="Time">そこまでの時間 [s]。</param>
/// <param name="BodyPoint">体の線（頭から腰）のうち、いちばん近い所。</param>
/// <param name="Along">武器の線のどこか（0 = 握り、1 = 先）。</param>
/// <param name="Line">その武器の線。</param>
public readonly record struct WeaponThreat(Vector3 Point, Vector3 Velocity, float Time, Vector3 BodyPoint, float Along, WeaponLine Line);

/// <summary>
/// 武器を選ばない守り。相手の武器（剣・棍棒・バット・これから足す武器、拳も）を「握りから先までの硬い線」として読むので、
/// 武器の種類で分けずに同じ式で守れる。3 つの動作に分ける:
/// <list type="number">
/// <item><b>読み</b>（<see cref="Read"/>）: 線の上の点を、それぞれの今の速度のまま進めて、体の線（頭から腰）へ近づく初めての所と時を求める。
/// 先だけでなく線の全体を見るので、棍棒の中ほど・刀の鍔元・横に振った柄が当たる場合も読める。</item>
/// <item><b>受け流し</b>（<see cref="Parry"/>）: 来る線の通り道へ、自分の刀を横たえる。刀の面の向き（押し返す力の向き）を、来る向きの真逆から
/// 体の外側へ <see cref="SwordDefense.Deflect"/> だけ傾けるので、止めるのでなく軌道をそらす（重い武器でも、力の向きを変える分しか要らない）。</item>
/// <item><b>足と頭で外す</b>（<see cref="BestStep"/>）: 動き方の候補（8 方向の足と止まる × 頭をそのまま・沈める・引く）ごとに、体の線を動かして
/// 振られている武器の届く範囲（肩からの球）にいちばん残らない動き方を選ぶ（同じなら、当たる点の速さの 2 乗が小さく、離れる距離が大きい方）。</item>
/// </list>
/// 結果は武器の形と速さ・自分の体の位置から連続に出る（「突きなら横へ」「横振りなら屈む」のような分岐は書かない）。
/// </summary>
public static class WeaponDefense
{
    /// <summary>線の上で見る点の数（握りから先まで等分）。</summary>
    private const int LineSamples = 5;

    /// <summary>読む時間を分ける数（速い拳 3 m/s でも 1 つの刻みで 1.6 cm しか進まず、体の線の近く（数 cm の幅）を飛び越さない）。</summary>
    private const int TimeSamples = 48;

    /// <summary>頭の動きの候補（そのまま・沈める <see cref="GuardPosture.Crouched"/>・引く <see cref="GuardPosture.Leaning"/>）[腕の長さ]。</summary>
    public static IReadOnlyList<Vector3> HeadMoves { get; } = [Vector3.Zero, GuardPosture.Crouched.Head, GuardPosture.Leaning.Head];

    /// <summary>肩から拳までが届く長さ [腕の長さ]（肩の回り込みを含む。相手の届く距離の読みはみなこれを使う）。</summary>
    public const float ShoulderReach = 1.15f;

    /// <summary>相手の武器（握っていれば）と、<paramref name="watchFists"/> なら両の拳を、武器の線として並べる。</summary>
    public static IReadOnlyList<WeaponLine> Lines(in FighterSense sense, bool watchFists)
    {
        var reach = sense.ArmReach;
        var arm = ShoulderReach * reach;
        var body = sense.OpponentBody;
        var hasBody = body.Pelvis != default;
        var chest = sense.OpponentChest;
        var lines = new List<WeaponLine>(3);
        Vector3 ShoulderNear(Vector3 p) =>
            !hasBody ? chest
            : Vector3.DistanceSquared(p, body.LeftShoulder) < Vector3.DistanceSquared(p, body.RightShoulder) ? body.LeftShoulder : body.RightShoulder;

        var blade = sense.OpponentBlade;
        if (blade.Present)
        {
            // 握りの速度は、握りにいちばん近い相手の拳の速度。
            var hand = Vector3.DistanceSquared(sense.OpponentLeft.Position, blade.Hilt) < Vector3.DistanceSquared(sense.OpponentRight.Position, blade.Hilt)
                ? sense.OpponentLeft
                : sense.OpponentRight;
            lines.Add(new WeaponLine(blade.Hilt, blade.Tip, hand.Velocity, blade.TipVelocity, ShoulderNear(blade.Hilt), arm, sense.OpponentVelocity));
        }

        if (watchFists)
        {
            foreach (var fist in new[] { sense.OpponentLeft, sense.OpponentRight })
            {
                if (fist.Position != default)
                {
                    lines.Add(new WeaponLine(fist.Position, fist.Position, fist.Velocity, fist.Velocity, ShoulderNear(fist.Position), arm, sense.OpponentVelocity));
                }
            }
        }

        return lines;
    }

    /// <summary>自分の体の線（頭から腰）。</summary>
    public static (Vector3 Top, Vector3 Bottom) BodyLine(in FighterSense sense)
    {
        var reach = sense.ArmReach;
        var top = sense.SelfHead == default ? sense.Self.Origin + new Vector3(0f, 0.4f * reach, 0f) : sense.SelfHead;
        return (top, sense.Self.Origin - new Vector3(0f, 1.2f * reach, 0f));
    }

    /// <summary>
    /// 読み: 振られている線（胴の動きを差し引いて <see cref="SwordDefense.ThreatSpeed"/> より速い）の点を、今の速度のまま
    /// <see cref="SwordDefense.Horizon"/> 秒先まで進め、体の線へ <see cref="SwordDefense.Margin"/> より近づく初めての所を返す
    /// （同じ時なら、いちばん近いもの）。体の線から離れていく点・肩から届かない所は見ない。
    /// </summary>
    public static WeaponThreat? Read(in FighterSense sense, IReadOnlyList<WeaponLine> lines, SwordDefense defense)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(defense);
        var reach = sense.ArmReach;
        var (top, bottom) = BodyLine(sense);
        WeaponThreat? best = null;
        var bestGap = float.MaxValue;
        foreach (var line in lines)
        {
            if (line.SwingSpeed < defense.ThreatSpeed * reach)
            {
                continue;
            }

            var samples = line.Length < 1e-4f ? 1 : LineSamples;
            for (var i = 0; i < samples; i++)
            {
                var s = samples == 1 ? 1f : i / (float)(samples - 1);
                var p0 = line.At(s);
                var v = line.VelocityAt(s);
                if (Vector3.Dot(v, ClosestOnSegment(p0, top, bottom) - p0) <= 0f)
                {
                    continue; // 体から離れていく
                }

                for (var k = 0; k <= TimeSamples; k++)
                {
                    var tau = defense.Horizon * k / TimeSamples;
                    if (best is { } b && tau > b.Time)
                    {
                        break;
                    }

                    var p = p0 + (v * tau);
                    if (Vector3.Distance(p, line.Shoulder) > line.LimitAt(s))
                    {
                        break; // ここから先へは届かない
                    }

                    var at = ClosestOnSegment(p, top, bottom);
                    var gap = Vector3.Distance(p, at);
                    if (gap < defense.Margin * reach)
                    {
                        if (best is null || tau < best.Value.Time || gap < bestGap)
                        {
                            best = new WeaponThreat(p, v, tau, at, s, line);
                            bestGap = gap;
                        }

                        break;
                    }
                }
            }
        }

        return best;
    }

    /// <summary>
    /// 受け流しの構え（体フレームのフレーム）: 読んだ所 <paramref name="threat"/> より <see cref="SwordDefense.Lead"/> だけ手前（来る向きの逆）で、
    /// 自分の刀の <see cref="SwordDefense.Forte"/> の所（0 = 手元・1 = 先）が来る線と交わるように、刀を横たえる。
    /// 刀の面が来る線を押す向き n は、来る向きの真逆から、体の外側（来る点が体の線からずれている側）へ <see cref="SwordDefense.Deflect"/> [rad]
    /// 傾けた向き。刀の向きは来る線と n の両方に直角（2 本の線が交わる所の押し合いの向きが n になる）。来る線が n と平行（真っすぐの突き）なら、
    /// 押す向きを横（体の外側）にする。
    /// </summary>
    /// <param name="sense">自分の見え方。</param>
    /// <param name="threat">読んだ脅威。</param>
    /// <param name="defense">守り方。</param>
    /// <param name="bladeLength">刀の右手から先までの長さ [腕の長さ]。</param>
    /// <param name="spacing">両手の間隔 [腕の長さ]。</param>
    /// <param name="current">今の刀の向き（体フレーム。構えの刀の向き）。</param>
    public static MotionFrame Parry(in FighterSense sense, in WeaponThreat threat, SwordDefense defense, float bladeLength, float spacing, Vector3 current)
    {
        ArgumentNullException.ThrowIfNull(defense);
        var reach = sense.ArmReach;
        // 迎える所: 来る線のうち、体へ来る点（Along）より握りの側 Bind までの所が、その点が体へ着く時にいる所。振りは握りの側ほど遅い
        // （回る物の速さは回る中心からの距離に比例）ので、握りの側で迎えるほど止める運動量が小さい。
        var bind = MathF.Min(threat.Along, defense.Bind);
        var q = sense.Self.WorldToLocal(threat.Line.At(bind) + (threat.Line.VelocityAt(bind) * threat.Time)) / reach;
        var body = sense.Self.WorldToLocal(threat.BodyPoint) / reach;
        var v = sense.Self.InverseRotate(threat.Velocity);
        v = v.LengthSquared() > 1e-10f ? Vector3.Normalize(v) : -Vector3.UnitZ;
        // 体の外側: 来る点が体の線からずれている向き（来る向きに直角な成分）。真ん中へ来るなら、握りの側へ（武器は手の側へ戻すのがいちばん短い）。
        var side = Perp(q - body, v);
        if (side.LengthSquared() < 0.05f * 0.05f)
        {
            side = Perp((sense.Self.WorldToLocal(threat.Line.Hilt) / reach) - body, v);
        }

        side = side.LengthSquared() > 1e-8f ? Vector3.Normalize(side) : Vector3.UnitX;
        var push = Vector3.Normalize((-v * MathF.Cos(defense.Deflect)) + (side * MathF.Sin(defense.Deflect)));
        // 来る線の向き（拳は長さが無いので、来る向きと横の向きに直角な仮の向き）。
        var w = threat.Line.Length > 1e-4f ? sense.Self.InverseRotate(threat.Line.Tip - threat.Line.Hilt) : Vector3.Cross(v, side);
        w = w.LengthSquared() > 1e-10f ? Vector3.Normalize(w) : Vector3.UnitY;
        var normal = Perp(push, w);
        if (normal.LengthSquared() < 0.2f * 0.2f)
        {
            normal = Perp(side, w); // 真っすぐの突き: 横へ払う
        }

        normal = normal.LengthSquared() > 1e-8f ? Vector3.Normalize(normal) : side;
        var axis = Vector3.Cross(w, normal);
        axis = axis.LengthSquared() > 1e-8f ? Vector3.Normalize(axis) : Vector3.Normalize(Vector3.Cross(v, Vector3.UnitZ) + new Vector3(0f, 1e-3f, 0f));
        // 刀の向きの 2 つのうち、今の刀の向き（構え）からの回りが少ない方（刀を大きく回すと、回る途中で自分の体を通る）。
        axis = Vector3.Dot(axis, current) < 0f ? -axis : axis;
        // 迎える所より押す向きへ Drive だけ先を目標にする: 腕のバネが当たる時にもまだ押していて、来る線を払う（置いただけの刀は押し負ける）。
        var meet = q - (v * defense.Lead) + (normal * defense.Drive);
        var right = meet - (axis * (defense.Forte * bladeLength));
        // 自分の刀は自分の体から離しておく: 左手から先までの線が、体の線（頭から腰）へ Clearance より近ければ、その分だけ体から遠ざける
        // （押し負けた刀が自分を斬らない余地。刀の向きはそのまま）。
        var (topW, bottomW) = BodyLine(sense);
        var top = sense.Self.WorldToLocal(topW) / reach;
        var bottom = sense.Self.WorldToLocal(bottomW) / reach;
        for (var pass = 0; pass < 3; pass++)
        {
            var (gap, from, to) = Nearest(right - (axis * spacing), right + (axis * bladeLength), top, bottom);
            if (gap >= defense.Clearance)
            {
                break;
            }

            var away = to - from;
            away = away.LengthSquared() > 1e-8f ? Vector3.Normalize(away) : Vector3.UnitZ;
            right += away * (defense.Clearance - gap);
        }

        // 刃の向き: Flat なら鎬（刀の平らな面）を来る線へ向けて受ける（刃と刃で受けると、同じ鉄同士の細い所に力が集まって両方が欠ける）。
        var edge = defense.Flat ? Vector3.Normalize(Vector3.Cross(axis, normal)) : normal;
        return SwordForms.Block(right, axis, edge, spacing);
    }

    /// <summary>線分 ab（刀）と線分 cd（体の線）のいちばん近い 2 点と、その距離（ab を細かく分けて求める）。OnBody は cd の上、OnBlade は ab の上。</summary>
    private static (float Gap, Vector3 OnBody, Vector3 OnBlade) Nearest(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        var best = (Gap: float.MaxValue, OnBody: c, OnBlade: a);
        for (var i = 0; i <= 16; i++)
        {
            var p = Vector3.Lerp(a, b, i / 16f);
            var q = ClosestOnSegment(p, c, d);
            var g = Vector3.Distance(p, q);
            if (g < best.Gap)
            {
                best = (g, q, p);
            }
        }

        return best;
    }


    /// <summary>
    /// 足と頭で外す動き方: 足の向き（顔の向きから 45° ずつの 8 方向と、止まる）× 頭（<see cref="HeadMoves"/>）の候補ごとに、体の線を
    /// 歩く速さ <paramref name="walkSpeed"/> で（<see cref="SwordDefense.StepDelay"/> 秒遅れて）動かし、頭を <see cref="SwordDefense.HeadTime"/> 秒で
    /// ずらして比べる。決め手は順に: (1) リングの中に残る、(2) 振られている武器の届く範囲（相手の肩から腕の長さ + 武器の長さの球）に
    /// 体の線が残る深さの、読む間の平均が小さい（振りは弧を描くので直線の読みはずれるが、この球の外へは届かない。ドリルで直線の読みだけに
    /// 頼ると屈んで打たれた）、(3) 直線に進めた来る線の点のうち体の線へ <see cref="SwordDefense.Margin"/> より近づくものの速さの 2 乗が小さい、
    /// (4) 来る線からいちばん離れる。
    /// </summary>
    /// <returns>移動の指令（ワールド、長さ 1 = 歩く速さ）と、頭のずれ [腕の長さ]。</returns>
    public static (Vector3 Move, Vector3 Head) BestStep(in FighterSense sense, IReadOnlyList<WeaponLine> lines, SwordDefense defense, float walkSpeed, Vector3 face)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(defense);
        var reach = sense.ArmReach;
        var (top, bottom) = BodyLine(sense);
        var ring = sense.Arena;
        var side = Vector3.Cross(Vector3.UnitY, face);
        var swinging = lines.Where(l => l.SwingSpeed >= defense.ThreatSpeed * reach).ToList();
        var best = (Move: Vector3.Zero, Head: Vector3.Zero);
        var bestKey = (Inside: false, Depth: float.MaxValue, Severity: float.MaxValue, Gap: float.MinValue);
        for (var d = -1; d < 8; d++)
        {
            var move = d < 0 ? Vector3.Zero : (face * MathF.Cos(d * MathF.PI / 4f)) + (side * MathF.Sin(d * MathF.PI / 4f));
            var end = sense.Self.Origin + (move * (walkSpeed * defense.Horizon)) - ring.Center;
            var inside = MathF.Max(MathF.Abs(end.X), MathF.Abs(end.Z)) < ring.Half - (0.5f * reach);
            foreach (var head in HeadMoves)
            {
                var headWorld = sense.Self.Rotate(head * reach);
                var severity = 0f;
                var gap = float.MaxValue;
                var depth = 0f;
                for (var k = 0; k <= TimeSamples; k++)
                {
                    var tau = defense.Horizon * k / TimeSamples;
                    var shift = move * (walkSpeed * MathF.Max(0f, tau - defense.StepDelay));
                    var lean = headWorld * MathF.Min(1f, tau / defense.HeadTime);
                    var a = top + shift + lean;
                    var b = bottom + shift;
                    foreach (var line in swinging)
                    {
                        // 届く範囲（肩から腕の長さ + 武器の長さの球）にどれだけ深く残っているか。弧を描く振りは直線の読みからずれるが、
                        // この球の外へは届かないので、球から出るのがいちばん確かな外し方。
                        var shoulderGap = Vector3.Distance(line.Shoulder, ClosestOnSegment(line.Shoulder, a, b));
                        depth += MathF.Max(0f, line.LimitAt(1f) + (defense.Margin * reach) - shoulderGap);
                        var samples = line.Length < 1e-4f ? 1 : LineSamples;
                        for (var i = 0; i < samples; i++)
                        {
                            var s = samples == 1 ? 1f : i / (float)(samples - 1);
                            var v = line.VelocityAt(s);
                            var p = line.At(s) + (v * tau);
                            if (Vector3.Distance(p, line.Shoulder) > line.LimitAt(s))
                            {
                                continue;
                            }

                            var g = Vector3.Distance(p, ClosestOnSegment(p, a, b));
                            gap = MathF.Min(gap, g);
                            if (g < defense.Margin * reach)
                            {
                                severity = MathF.Max(severity, (v - line.BodyVelocity).LengthSquared());
                            }
                        }
                    }
                }

                var key = (inside, depth / (TimeSamples + 1), severity, gap);
                if (Better(key, bestKey))
                {
                    bestKey = key;
                    best = (move, head);
                }
            }
        }

        return best;

        static bool Better((bool Inside, float Depth, float Severity, float Gap) a, (bool Inside, float Depth, float Severity, float Gap) b) =>
            a.Inside != b.Inside ? a.Inside
            : MathF.Abs(a.Depth - b.Depth) > 1e-4f ? a.Depth < b.Depth
            : MathF.Abs(a.Severity - b.Severity) > 1e-6f ? a.Severity < b.Severity
            : a.Gap > b.Gap + 1e-5f;
    }


    /// <summary><paramref name="a"/> の、<paramref name="unit"/> に直角な成分。</summary>
    private static Vector3 Perp(Vector3 a, Vector3 unit) => a - (unit * Vector3.Dot(a, unit));

    /// <summary>線分 ab の上で p にいちばん近い点。</summary>
    public static Vector3 ClosestOnSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var t = ab.LengthSquared() > 1e-12f ? Math.Clamp(Vector3.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f) : 0f;
        return a + (ab * t);
    }
}
