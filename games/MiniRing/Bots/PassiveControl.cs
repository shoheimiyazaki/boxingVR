using System.Numerics;

namespace MiniRing.Bots;

/// <summary>
/// 抵抗しない入力元（ドリルの受け手、2026-11-25）: その場に立ち、相手の方を向いて、両腕を体の脇に下ろしたまま。
/// 攻めない・守らない・歩かない・握らない。体は他のファイターと同じ物理で立ち、打たれれば同じように崩れる（入力が何もしないだけ）。
/// 倒れた相手への攻め（<see cref="GroundStriker"/>）も足さない（<see cref="BoxingGame"/> が包まない）。
/// </summary>
public sealed class PassiveControl : IFighterControl
{
    /// <summary>腕を体の脇に下ろした手の目標（体フレーム = 両肩の真ん中、腕の長さ = 1。X は外へ、Y は下、Z は少し前）。
    /// 腕の長さのほぼ真下（0.92）で、肘を伸ばしきらない。肩幅の半分ほど外（0.18）で、太ももに手が乗る。</summary>
    internal static readonly Vector3 HangingHand = new(0.18f, -0.92f, 0.06f);

    public FighterCommand Read(in FighterSense sense, float dt)
    {
        var to = sense.OpponentChest - sense.Self.Origin;
        to.Y = 0f;
        var face = to.LengthSquared() > 1e-6f ? Vector3.Normalize(to) : sense.Self.Forward;
        return new FighterCommand(Vector3.Zero, face, Hang(Hand.Left, sense.ArmReach), Hang(Hand.Right, sense.ArmReach));
    }

    internal static HandCommand Hang(Hand hand, float reach)
        => new(new Vector3(hand.LocalSide() * HangingHand.X, HangingHand.Y, HangingHand.Z) * reach, false);
}
