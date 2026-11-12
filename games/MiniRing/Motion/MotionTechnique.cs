using MiniRing.Weapons;

namespace MiniRing.Motion;

/// <summary>
/// 動きの技 1 つ: 両手で握る剣の動きの区間（構え → 打ち → 戻り。<see cref="SwordMotion"/>）と、それを始める間合い（胸と胸の水平の距離）。
/// 剣道のモーションキャプチャから切り出した打ち、またはロングソードの型。間合いと名前（何に当たったか）はドリル（<see cref="StrikeDrill"/>）で決める。
/// </summary>
/// <param name="Name">名前（英字。何に当たる技か + どこから切り出したか）。</param>
/// <param name="Weapon">握る剣。</param>
/// <param name="Motion">動きの区間。</param>
/// <param name="Range">始める間合い [m]。</param>
/// <param name="Target">いちばん斬った部位（ドリルで決まる。まだなら null）。</param>
/// <param name="Skill">狙った部位ごとの、その部位を打てた割合（0..1。狙いのドリル <c>aimlab</c> で、構えの違う相手に対して測る。まだなら null）。</param>
public sealed record MotionTechnique(string Name, WeaponKind Weapon, SwordMotion Motion, float Range, BodyPartKind? Target = null,
    IReadOnlyDictionary<BodyPartKind, float>? Skill = null)
{
    /// <summary>技を出す間合いの幅 [m]: 間合いが <see cref="Range"/> ± これに入ったら出す。</summary>
    public const float Window = 0.03f;

    /// <summary>間合い <paramref name="distance"/> で出せる技か。</summary>
    public bool Fits(float distance) => MathF.Abs(distance - Range) <= Window;
}
