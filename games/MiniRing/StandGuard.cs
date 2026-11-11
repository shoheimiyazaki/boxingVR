using System.Numerics;

namespace MiniRing;

/// <summary>
/// その場で構えて立っているだけの操作元: 相手を向き、両手を顔の前に上げ、動かない。武器の実験場やテストの「的」に使う。
/// </summary>
public sealed class StandGuard : IFighterControl
{
    public FighterCommand Read(in FighterSense sense, float dt)
    {
        var face = sense.OpponentChest - sense.Self.Origin;
        face.Y = 0f;
        var reach = sense.ArmReach;
        HandCommand Guard(Hand hand) => new(new Vector3(hand.LocalSide() * 0.15f * reach, 0.1f * reach, 0.45f * reach), false);
        return new FighterCommand(Vector3.Zero, face.LengthSquared() > 1e-6f ? Vector3.Normalize(face) : Vector3.UnitZ, Guard(Hand.Left), Guard(Hand.Right));
    }
}
