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

    /// <summary>弧で読むときの、肩まわりの回りの上限 [rad]。振りは肩の可動域で止まるので、直線の読みのように行き過ぎない（約 90°）。</summary>
    public const float MaxArcAngle = 1.6f;

    /// <summary>
    /// 線の上の点（<paramref name="s"/>）の <paramref name="tau"/> 秒後の位置。<paramref name="arc"/> が false なら今の速度のまま進める（直線）。
    /// true なら、相手の振りは肩を中心にした弧なので、胴の動きを引いた速度から肩まわりの角速度 ω = r × v / |r|² を求め、肩からの距離を保ったまま
    /// 回し（<see cref="MaxArcAngle"/> まで）、胴の動きの分だけ運ぶ。肩へ向かう・離れる動き（突き）は ω が 0 で直線のまま。
    /// </summary>
    public Vector3 Predict(float s, float tau, bool arc)
    {
        var p0 = At(s);
        var v = VelocityAt(s);
        if (!arc || !TryArc(p0, v, out var axis, out var omega))
        {
            return p0 + (v * tau);
        }

        var theta = MathF.Min(omega * tau, MaxArcAngle);
        return Shoulder + (BodyVelocity * tau) + Rotate(p0 - Shoulder, axis, theta);
    }

    /// <summary><see cref="Predict"/> の <paramref name="tau"/> 秒後の速度（弧なら回った後の向き。回りの上限に達したら胴の速度だけ）。</summary>
    public Vector3 PredictVelocity(float s, float tau, bool arc)
    {
        var p0 = At(s);
        var v = VelocityAt(s);
        if (!arc || !TryArc(p0, v, out var axis, out var omega))
        {
            return v;
        }

        return omega * tau >= MaxArcAngle ? BodyVelocity : BodyVelocity + (Vector3.Cross(axis, Rotate(p0 - Shoulder, axis, omega * tau)) * omega);
    }

    private static Vector3 Rotate(Vector3 r, Vector3 axis, float theta) =>
        (r * MathF.Cos(theta)) + (Vector3.Cross(axis, r) * MathF.Sin(theta)) + (axis * Vector3.Dot(axis, r) * (1f - MathF.Cos(theta)));

    private bool TryArc(Vector3 p0, Vector3 v, out Vector3 axis, out float omega)
    {
        var r = p0 - Shoulder;
        var r2 = r.LengthSquared();
        var w = r2 > 1e-8f ? Vector3.Cross(r, v - BodyVelocity) / r2 : default;
        omega = w.Length();
        axis = omega > 1e-4f ? w / omega : default;
        return omega > 1e-4f;
    }

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
/// <param name="Arc">肩まわりの弧で読んだ脅威か（<see cref="WeaponLine.Predict"/>）。迎える所の見積もりも同じ読み方にそろえる。</param>
public readonly record struct WeaponThreat(Vector3 Point, Vector3 Velocity, float Time, Vector3 BodyPoint, float Along, WeaponLine Line, bool Arc = false);

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

                    var p = line.Predict(s, tau, defense.Arc);
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
                            best = new WeaponThreat(p, defense.Arc ? line.PredictVelocity(s, tau, true) : v, tau, at, s, line, defense.Arc);
                            bestGap = gap;
                        }

                        break;
                    }
                }
            }
        }

        return best;
    }

    /// <summary>受けの連続量。傾き [rad]、押し込み・引き [腕の長さ]、流す割合・足へ寄せる割合 [0..1]。</summary>
    public readonly record struct ParryResponse(float Angle, float Drive, float Yield, float YieldShare, float StepShare);

    /// <summary>
    /// 速さの 2 乗を負荷の代理とし、残り時間が短いほど流す。r = (v / LoadSpeed)² × (Horizon + CounterTime) / (Time + CounterTime)、
    /// 流す割合 = r / (1 + r)。係数は教育の値であり、腕の力・武器の材質には触れない。Adaptive が false なら従来の固定値。
    /// </summary>
    public static ParryResponse Response(in FighterSense sense, in WeaponThreat threat, SwordDefense defense)
    {
        ArgumentNullException.ThrowIfNull(defense);
        if (!defense.Adaptive)
        {
            return new(defense.Deflect, defense.Drive, 0f, 0f, 1f);
        }

        var speed = threat.Velocity.Length() / sense.ArmReach;
        var ratio = speed / defense.LoadSpeed;
        var load = ratio * ratio * (defense.Horizon + defense.CounterTime) / (MathF.Max(0f, threat.Time) + defense.CounterTime);
        var yield = load / (1f + load);
        var (top, bottom) = BodyLine(sense);
        var shoulderGap = Vector3.Distance(threat.Line.Shoulder, ClosestOnSegment(threat.Line.Shoulder, top, bottom));
        var depth = MathF.Max(0f, threat.Line.LimitAt(threat.Along) + (defense.Margin * sense.ArmReach) - shoulderGap) / sense.ArmReach;
        var time = MathF.Max(0f, threat.Time);
        var step = 1f - ((1f - yield) * time / (time + defense.CounterTime) / (1f + depth));
        return new(defense.Deflect + ((defense.YieldAngle - defense.Deflect) * yield), defense.Drive * (1f - yield), defense.YieldDistance * yield, yield, step);
    }

    /// <summary>
    /// 返しの隙: 剣先の向きが体の外を向き、相手の胴の移動を含む先の速度も体から離れ、刃の線全体が CounterTime 秒先まで体を通らない。
    /// 武器を落とした相手にも返せる。攻撃の名前・種類は見ない。
    /// </summary>
    public static bool CounterOpening(in FighterSense sense, SwordDefense defense)
    {
        ArgumentNullException.ThrowIfNull(defense);
        if (!sense.OpponentBlade.Present)
        {
            return true;
        }

        var (top, bottom) = BodyLine(sense);
        var blade = sense.OpponentBlade;
        var away = blade.Tip - ClosestOnSegment(blade.Tip, top, bottom);
        if (Vector3.Dot(blade.Tip - blade.Hilt, away) <= 0f || Vector3.Dot(blade.TipVelocity, away) <= 0f)
        {
            return false;
        }

        foreach (var line in Lines(sense, watchFists: false))
        {
            // 現在から予測位置への線分も見るので、その間に体を横切る刃を隙と誤認しない。
            for (var i = 0; i < LineSamples; i++)
            {
                var along = i / (float)(LineSamples - 1);
                var start = line.At(along);
                var end = start + (line.VelocityAt(along) * defense.CounterTime);
                if (Nearest(start, end, top, bottom).Gap < defense.CounterClearance * sense.ArmReach)
                {
                    return false;
                }
            }
        }

        return true;
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
    /// <param name="rearLength">右手から後ろの端（棒の −X の端）までの長さ [腕の長さ]。無ければ（NaN）左手まで（刀・ロングソード）。棒は左手の後ろにも 1.4 腕の長さ伸びるので、
    /// 体から離す線（Clearance）は後ろの端まで見る。</param>
    /// <param name="response">迎える線を保ったまま毎歩追う負荷の量。null なら、その脅威から計算する。</param>
    public static MotionFrame Parry(in FighterSense sense, in WeaponThreat threat, SwordDefense defense, float bladeLength, float spacing, Vector3 current, float rearLength = float.NaN, ParryResponse? response = null)
    {
        ArgumentNullException.ThrowIfNull(defense);
        var reach = sense.ArmReach;
        var action = response ?? Response(sense, threat, defense);
        // 迎える所: 来る線のうち、体へ来る点（Along）より握りの側 Bind までの所が、その点が体へ着く時にいる所。振りは握りの側ほど遅い
        // （回る物の速さは回る中心からの距離に比例）ので、握りの側で迎えるほど止める運動量が小さい。
        var bind = MathF.Min(threat.Along, defense.Bind);
        var q = sense.Self.WorldToLocal(threat.Line.Predict(bind, threat.Time, threat.Arc)) / reach;
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
        var push = Vector3.Normalize((-v * MathF.Cos(action.Angle)) + (side * MathF.Sin(action.Angle)));
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
        // 負荷が大きいほど押し込みを弱め、来る向きへ手を逃がして刃の上を滑らせる。
        var meet = q - (v * defense.Lead) + (normal * action.Drive) + (v * action.Yield);
        var right = meet - (axis * (defense.Forte * bladeLength));
        right = KeepClear(sense, right, axis, bladeLength, spacing, rearLength, defense.Clearance);
        // 刃の向き: Flat なら鎬（刀の平らな面）を来る線へ向けて受ける（刃と刃で受けると、同じ鉄同士の細い所に力が集まって両方が欠ける）。
        var edge = defense.Flat ? Vector3.Normalize(Vector3.Cross(axis, normal)) : normal;
        return SwordForms.Block(right, axis, edge, spacing);
    }

    /// <summary>
    /// 自分の刀は自分の体から離しておく: 左手（または後ろの端）から先までの線が、体の線（頭から腰）へ <paramref name="clearance"/> より近ければ、その分だけ
    /// 体から遠ざける（押し負けた刀が自分を斬らない余地。刀の向きはそのまま）。右手の位置（体フレーム、腕の長さ = 1）を返す。
    /// </summary>
    private static Vector3 KeepClear(in FighterSense sense, Vector3 right, Vector3 axis, float bladeLength, float spacing, float rearLength, float clearance)
    {
        var reach = sense.ArmReach;
        var (topW, bottomW) = BodyLine(sense);
        var top = sense.Self.WorldToLocal(topW) / reach;
        var bottom = sense.Self.WorldToLocal(bottomW) / reach;
        for (var pass = 0; pass < 3; pass++)
        {
            var (gap, from, to) = Nearest(right - (axis * (float.IsNaN(rearLength) ? spacing : rearLength)), right + (axis * bladeLength), top, bottom);
            if (gap >= clearance)
            {
                break;
            }

            var away = to - from;
            away = away.LengthSquared() > 1e-8f ? Vector3.Normalize(away) : Vector3.UnitZ;
            right += away * (clearance - gap);
        }

        return right;
    }

    /// <summary>
    /// 間に合う受け: <see cref="Parry"/> の理想の受け（<paramref name="ideal"/>）を、残り時間のうちに手と刀の向きが動ける範囲（<see cref="SwordDefense.HandSpeed"/>・
    /// <see cref="SwordDefense.TurnSpeed"/> × 反応の遅れ <see cref="SwordDefense.ReactDelay"/> を引いた時間）へ置き直す。理想の所へ届くならそのまま、届かないなら届く範囲の
    /// 候補（理想へ向かう最も遠い点・今の手のまわりの点・構え）のうち、来る線と自分の刀の線の距離が最小（に、構えからの動きの大きさ × <see cref="SwordDefense.Economy"/> を足した値が最小）
    /// の所へ。どこも <see cref="SwordDefense.InterceptGap"/> 以内で遮れなければ受けない（null。足と頭だけで外す）。種類の分岐は無く、届く範囲と遮る量の連続の比べ。
    /// 来る線は、読んだ時から着くまでの後ろ 4 割の間の線（刃が通り過ぎる幅）で見る。
    /// </summary>
    /// <param name="ideal">理想の受け（<see cref="Parry"/> の結果）。</param>
    /// <param name="handFrom">今の右手の位置（体フレーム、腕の長さ = 1）。</param>
    /// <param name="currentBlade">今の刀の向き（体フレーム）。</param>
    /// <param name="rest">構えのフレーム（動きの小ささを測る基準）。</param>
    /// <param name="elapsed">脅威を読んでからの経過 [s]（同じ打ちを読み直す間は残り時間が減る）。</param>
    public static MotionFrame? ReachableParry(in FighterSense sense, in WeaponThreat threat, SwordDefense defense, MotionFrame ideal, Vector3 handFrom, Vector3 currentBlade,
        MotionFrame rest, float bladeLength, float spacing, float rearLength = float.NaN, float elapsed = 0f)
    {
        ArgumentNullException.ThrowIfNull(defense);
        var reach = sense.ArmReach;
        var time = MathF.Max(0f, threat.Time);
        var usable = MathF.Max(0f, time - elapsed - defense.ReactDelay);
        var budget = defense.HandSpeed * usable;
        var axis = TurnToward(currentBlade, ideal.Blade, defense.TurnSpeed * usable);
        var edge = SwordMotion.Perpendicular(ideal.Edge, axis);
        var rear = float.IsNaN(rearLength) ? spacing : rearLength;

        // 来る線: 着くまでの後ろ 4 割の間（刃が通り過ぎる幅）の、握りから先までの線。
        var incoming = new (Vector3 A, Vector3 B)[3];
        for (var j = 0; j < incoming.Length; j++)
        {
            var tau = time * (0.6f + (0.2f * j));
            incoming[j] = (sense.Self.WorldToLocal(threat.Line.Predict(0f, tau, threat.Arc)) / reach, sense.Self.WorldToLocal(threat.Line.Predict(1f, tau, threat.Arc)) / reach);
        }

        float Gap(Vector3 right)
        {
            var best = float.MaxValue;
            for (var i = 0; i <= 24; i++)
            {
                var p = (right - (axis * rear)) + (axis * ((rear + bladeLength) * (i / 24f)));
                foreach (var (a, b) in incoming)
                {
                    best = MathF.Min(best, Vector3.Distance(p, ClosestOnSegment(p, a, b)));
                }
            }

            return best;
        }

        float Score(Vector3 right) => Gap(right) + (defense.Economy * (Vector3.Distance(right, rest.Right) + (0.3f * AngleBetween(axis, rest.Blade))));

        var candidates = new List<Vector3> { ideal.Right, rest.Right };
        var toIdeal = ideal.Right - handFrom;
        if (toIdeal.Length() > 1e-6f)
        {
            candidates.Add(handFrom + (Vector3.Normalize(toIdeal) * MathF.Min(budget, toIdeal.Length())));
        }

        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                for (var z = -1; z <= 1; z++)
                {
                    if (x == 0 && y == 0 && z == 0)
                    {
                        continue;
                    }

                    var dir = Vector3.Normalize(new Vector3(x, y, z));
                    candidates.Add(handFrom + (dir * budget));
                    candidates.Add(handFrom + (dir * (0.5f * budget)));
                }
            }
        }

        candidates.Add(handFrom);
        var bestRight = Vector3.Zero;
        var bestScore = float.MaxValue;
        foreach (var raw in candidates)
        {
            // 届く範囲（今の手から budget）の内だけ。体から離す余地（KeepClear）を足してから、もう一度届くか見る。
            var right = KeepClear(sense, raw, axis, bladeLength, spacing, rearLength, defense.Clearance);
            if (Vector3.Distance(right, handFrom) > budget + 1e-4f && Vector3.Distance(right, handFrom) > 1e-4f)
            {
                right = handFrom + (Vector3.Normalize(right - handFrom) * budget);
            }

            var score = Score(right);
            if (score < bestScore)
            {
                bestScore = score;
                bestRight = right;
            }
        }

        return Gap(bestRight) > defense.InterceptGap ? null : SwordForms.Block(bestRight, axis, edge, spacing);
    }

    /// <summary>向き <paramref name="from"/> から <paramref name="to"/> へ、最大 <paramref name="limit"/> [rad] だけ回した単位ベクトル。</summary>
    private static Vector3 TurnToward(Vector3 from, Vector3 to, float limit)
    {
        var a = from.LengthSquared() > 1e-8f ? Vector3.Normalize(from) : to;
        var b = to.LengthSquared() > 1e-8f ? Vector3.Normalize(to) : a;
        var angle = AngleBetween(a, b);
        if (angle <= limit || angle < 1e-5f)
        {
            return b;
        }

        var t = limit / angle;
        var mixed = Vector3.Lerp(a, b, t);
        return mixed.LengthSquared() > 1e-8f ? Vector3.Normalize(mixed) : a;
    }

    private static float AngleBetween(Vector3 a, Vector3 b) => MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1f, 1f));

    /// <summary>左手の物が面を広げる所から肩まで、腕がいっぱいに届く長さ [腕の長さ]。これより先へは目標を置かない。</summary>
    private const float CoverReach = 0.95f;

    /// <summary>左の肩の位置（体フレーム、腕の長さ = 1。肩の幅 0.57 の半分）。</summary>
    private static readonly Vector3 LeftShoulder = new(0.285f, 0f, 0f);

    /// <summary>
    /// 空いた手の物（盾・グローブ）で覆う: 来る線（<paramref name="threat"/> を、体の線へ近づく向きに逆にたどった道）が、手の前に広げた面（今の手の位置
    /// <paramref name="rest"/> を通り、物の表が <paramref name="facing"/> を向く面）を通る点へ、物の面の中心を寄せる。
    /// 動かす量は、通る点と面の中心との離れ（<paramref name="radius"/> = 物が面を広げる半径、<see cref="SwordsmanBot.OffhandRadius"/>）のうち、面の縁から 0.4 倍の内側まで
    /// 届かない分だけ: 大きい盾は少し寄せるだけで覆え、小さい物（グローブ）はほぼそのまま通る点へ動く。盾かグローブかで式は分けず、見えている半径だけが違う。
    /// 返すのは左手の目標（体フレーム、腕の長さ = 1）。肩から届く長さの外へは置かない。
    /// </summary>
    public static Vector3 Cover(in FighterSense sense, in WeaponThreat threat, Vector3 rest, Vector3 facing, float radius)
    {
        var reach = sense.ArmReach;
        var q = sense.Self.WorldToLocal(threat.Point) / reach;
        var v = sense.Self.InverseRotate(threat.Velocity);
        v = v.LengthSquared() > 1e-10f ? Vector3.Normalize(v) : -Vector3.UnitZ;
        var n = facing.LengthSquared() > 1e-8f ? Vector3.Normalize(facing) : Vector3.UnitZ;
        var along = Vector3.Dot(v, n);
        // 来る線が面を通る点。線が面とほぼ平行なら、読んだ所を面へ落とした点。
        // 面を通るのは読んだ所より手前（来る向きの逆）。もう面を過ぎた線は、読んだ所を面へ落とした点。
        var ahead = MathF.Abs(along) > 0.2f ? Vector3.Dot(rest - q, n) / along : 1f;
        var through = ahead < 0f
            ? q + (v * ahead)
            : q - (n * Vector3.Dot(q - rest, n));
        var off = through - rest;
        off -= n * Vector3.Dot(off, n); // 面の中で動かす
        var gap = off.Length();
        var move = MathF.Max(0f, gap - (0.6f * radius));
        var target = gap > 1e-6f ? rest + (off / gap * move) : rest;
        var reachable = target - LeftShoulder;
        return reachable.Length() > CoverReach ? LeftShoulder + (Vector3.Normalize(reachable) * CoverReach) : target;
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
