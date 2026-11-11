using System.Numerics;
using System.Text.Json.Serialization;

namespace MiniRing.Weapons;

/// <summary>振りの型。</summary>
public enum SwingFamily
{
    /// <summary>縦振り: 頭の上へ振りかぶり、真上から打ち下ろす。</summary>
    Chop,

    /// <summary>横振り: 利き手の側の後ろへ引き、横から頭へ振り抜く。</summary>
    Side,

    /// <summary>突き: 腰の横へ引き、頭の先端で前へ突く。</summary>
    Thrust,

    /// <summary>
    /// バットの振り（両手持ち）: 右肩の上で棍棒を立てて構え、後ろへ寝かせて溜め、胴を回して横から頭へ振り抜く。左手は右手の下に添える
    /// （<see cref="BoxingCorners.PlayerTwoHanded"/> / <see cref="BoxingCorners.CpuTwoHanded"/>）。
    /// </summary>
    Bat,

    /// <summary>
    /// 剣の技（片手）: 剣先を相手の顔へ向けた中段に構え（<see cref="WeaponSwing.SwordGuard"/>）、技ごとの形で斬る・突く。刃は両刃で、
    /// 刃筋（刃の向き）は <see cref="WeaponSwing.Roll"/> で決める。技の形と狙う部位は <see cref="SwordTechnique"/>。
    /// </summary>
    Sword,
}

/// <summary>
/// 握った武器（棍棒・剣）の 1 振り。拳の位置（体フレームのローカル、腕の長さ <c>reach</c> を 1 とした値）と棍棒の向き（体フレームでの方位角と仰角 [rad]、
/// 0/0 が真正面）を、構え → 振りかぶり → 打ち → 構えへ戻す、の目標の列にする。拳はこの目標へバネで引かれ、手首は棍棒をこの向きへ
/// 回す（<see cref="Fighter"/> の力の上限のまま）ので、棍棒の重さに負けて遅れることもある。
/// <list type="bullet">
/// <item><see cref="WristDelay"/>: 打ちの間、棍棒の向きを振りかぶりのまま残しておく割合（0 = 拳と一緒に回す、0.6 = 最後の 4 割で手首を返す）。
/// 溜めた棍棒を最後に返すと、頭の速さが拳の速さに上乗せされる（ゴルフの「タメ」）。</item>
/// <item><see cref="Twist"/>: 打ちの間の胴のひねりの上乗せ [rad]（正で右肩が前）。</item>
/// <item><see cref="Step"/>: 打ちの間に踏み込む強さ（移動の指令の大きさ、−1..1。負は下がりながら打つ: 剣道の引き技）。</item>
/// <item><see cref="Ease"/>: 打ちの拳の目標の進み方の指数（1 = 等速、2 以上 = 初めはゆっくり、最後に速く）。</item>
/// <item><see cref="Roll"/>: 刃筋 [rad]。武器を自分の軸のまわりに回す角度（0 = 刃が向きの面の上下を向く: 縦に振れば刃で当たる。
/// π/2 = 刃が横を向く: 横に振れば刃で当たる）。振りかぶりの間に構えの 0 からこの角度へ回し、戻りで 0 へ戻す。丸い棍棒には効かない。</item>
/// </list>
/// </summary>
public sealed record WeaponSwing(
    SwingFamily Family,
    Vector3 WindupFist, float WindupAzimuth, float WindupElevation,
    Vector3 StrikeFist, float StrikeAzimuth, float StrikeElevation,
    float WindupTime, float StrikeTime, float RecoverTime,
    float WristDelay, float Twist, float Step, float Ease, float Roll = 0f)
{
    /// <summary>構え（打っていない間）の拳と棍棒の向き: 体の前、顔の高さの少し下で、棍棒を斜め上へ立てる。</summary>
    public static readonly (Vector3 Fist, float Azimuth, float Elevation) Guard = (new Vector3(-0.15f, 0.05f, 0.4f), 0.2f, 1.0f);

    /// <summary>バットの構え: 右肩の前で両手で握り、棍棒を後ろへ傾けて立てる。</summary>
    public static readonly (Vector3 Fist, float Azimuth, float Elevation) BatGuard = (new Vector3(-0.3f, 0.1f, 0.25f), -2.8f, 1.1f);

    /// <summary>剣の中段の構え: 拳を体の前、胸の高さに置き、剣先を相手の顔へ向けて斜め上へ。</summary>
    public static readonly (Vector3 Fist, float Azimuth, float Elevation) SwordGuard = (new Vector3(-0.1f, 0.0f, 0.45f), 0.1f, 0.4f);

    /// <summary>両手で握る振りか。</summary>
    [JsonIgnore]
    public bool TwoHanded => Family == SwingFamily.Bat;

    /// <summary>型の構え。</summary>
    public static (Vector3 Fist, float Azimuth, float Elevation) GuardOf(SwingFamily family) => family switch
    {
        SwingFamily.Bat => BatGuard,
        SwingFamily.Sword => SwordGuard,
        _ => Guard,
    };

    /// <summary>振りの長さ [s]。</summary>
    [JsonIgnore]
    public float Duration => WindupTime + StrikeTime + RecoverTime;

    /// <summary>打ちが始まる時刻 [s]。</summary>
    [JsonIgnore]
    public float StrikeStart => WindupTime;

    /// <summary>打ちが終わる時刻 [s]。</summary>
    [JsonIgnore]
    public float StrikeEnd => WindupTime + StrikeTime;

    /// <summary>
    /// 振り始めから <paramref name="time"/> 秒の、棍棒を持つ手（右手）の指令と、胴のひねり・踏み込みの強さ。
    /// </summary>
    public (HandCommand Hand, float Twist, float Step) At(float time, float reach)
    {
        var guard = GuardOf(Family);
        Vector3 fist;
        float az, el, twist = 0f, step = 0f, roll = Roll;
        if (time < WindupTime)
        {
            var u = Smooth(time / WindupTime);
            fist = Vector3.Lerp(guard.Fist, WindupFist, u);
            az = Lerp(guard.Azimuth, WindupAzimuth, u);
            el = Lerp(guard.Elevation, WindupElevation, u);
            roll = Roll * u;
        }
        else if (time < StrikeEnd)
        {
            var u = (time - WindupTime) / StrikeTime;
            fist = Vector3.Lerp(WindupFist, StrikeFist, MathF.Pow(u, Ease));
            // 手首のタメ: 遅らせた割合が過ぎてから、残りの時間で一気に返す。
            var w = Math.Clamp((u - WristDelay) / MathF.Max(1e-3f, 1f - WristDelay), 0f, 1f);
            az = Lerp(WindupAzimuth, StrikeAzimuth, w);
            el = Lerp(WindupElevation, StrikeElevation, w);
            twist = Twist;
            step = Step;
        }
        else
        {
            var u = Smooth(Math.Clamp((time - StrikeEnd) / RecoverTime, 0f, 1f));
            fist = Vector3.Lerp(StrikeFist, guard.Fist, u);
            az = Lerp(StrikeAzimuth, guard.Azimuth, u);
            el = Lerp(StrikeElevation, guard.Elevation, u);
            twist = Twist * (1f - u);
            roll = Roll * (1f - u);
        }

        return (new HandCommand(fist * reach, false, Orientation: Orientation(az, el, roll)), twist, step);
    }

    /// <summary>構えの指令（<paramref name="family"/> の型の構え。既定は片手の構え）。</summary>
    public static HandCommand GuardCommand(float reach, SwingFamily family = SwingFamily.Side)
    {
        var guard = GuardOf(family);
        return new(guard.Fist * reach, false, Orientation: Orientation(guard.Azimuth, guard.Elevation));
    }

    /// <summary>体フレームでの向き（方位角: 0 = 前、正 = 左 / 仰角: 正 = 上）。</summary>
    public static Vector3 Direction(float azimuth, float elevation) =>
        new(MathF.Sin(azimuth) * MathF.Cos(elevation), MathF.Sin(elevation), MathF.Cos(azimuth) * MathF.Cos(elevation));

    /// <summary>
    /// 武器のローカル X を <see cref="Direction"/> へ向け、自分の軸のまわりに <paramref name="roll"/> 回す回転（武器のローカル → 体フレーム）。
    /// roll 0 では武器のローカル Y（剣の刃の幅の向き）が、X と鉛直を含む面の中にある。
    /// </summary>
    public static Quaternion Orientation(float azimuth, float elevation, float roll = 0f)
    {
        var x = Direction(azimuth, elevation);
        // Y は仰角を上げる向き（Direction の仰角での微分）: 真上を越えて振りかぶっても向きが跳ばない（刃筋が 180° 裏返らない）。
        var y = new Vector3(-MathF.Sin(azimuth) * MathF.Sin(elevation), MathF.Cos(elevation), -MathF.Cos(azimuth) * MathF.Sin(elevation));
        var z = Vector3.Cross(x, y);
        var aim = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0f, y.X, y.Y, y.Z, 0f, z.X, z.Y, z.Z, 0f, 0f, 0f, 0f, 1f)));
        return roll == 0f ? aim : Quaternion.Normalize(Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitX, roll), aim));
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private static float Smooth(float t) => t * t * (3f - (2f * t));

    /// <summary>型ごとの、研究の出発点の振り（手で決めた素直な形）。</summary>
    public static WeaponSwing Default(SwingFamily family) => family switch
    {
        SwingFamily.Chop => new(family,
            new Vector3(-0.05f, 0.55f, 0.05f), 0f, 2.3f,
            new Vector3(-0.05f, 0.0f, 0.75f), 0f, -0.3f,
            0.35f, 0.2f, 0.35f, WristDelay: 0f, Twist: 0f, Step: 0f, Ease: 1f),
        SwingFamily.Side => new(family,
            new Vector3(-0.5f, 0.2f, 0.05f), -2.0f, 0.2f,
            new Vector3(0.1f, 0.15f, 0.65f), 1.0f, 0.1f,
            0.35f, 0.2f, 0.35f, WristDelay: 0f, Twist: 0f, Step: 0f, Ease: 1f),
        SwingFamily.Thrust => new(family,
            new Vector3(-0.2f, 0.0f, 0.1f), 0f, 0.1f,
            new Vector3(-0.05f, 0.15f, 1.0f), 0f, 0.1f,
            0.3f, 0.18f, 0.35f, WristDelay: 0f, Twist: 0f, Step: 0f, Ease: 1f),
        SwingFamily.Bat => new(family,
            new Vector3(-0.45f, 0.1f, 0.05f), -2.3f, 0.5f,
            new Vector3(0.05f, 0.1f, 0.55f), 1.0f, 0.1f,
            0.35f, 0.2f, 0.4f, WristDelay: 0f, Twist: 0f, Step: 0f, Ease: 1f),
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "知らない振りの型です。"),
    };
}
