using System.Numerics;

namespace MiniRing.Bots;

/// <summary>
/// 構えたまま相手へ詰めるだけの入力元（打たない）。組み技のドリル（throwlab）で取りの「いつもの」入力元にし、技は外側の
/// <see cref="JujutsuBot"/> がかける。胸と胸の距離が <see cref="Stop"/>（腕の長さ = 1）より遠ければ歩いて寄る。
/// </summary>
public sealed class Closer(float stop = 0.75f, float speed = 0.6f) : IFighterControl
{
    public float Stop { get; } = stop;

    public FighterCommand Read(in FighterSense sense, float dt)
    {
        var to = sense.OpponentChest - sense.Self.Origin;
        to.Y = 0f;
        var distance = to.Length();
        var face = distance > 1e-4f ? to / distance : sense.Self.Forward;
        var move = distance > Stop * sense.ArmReach ? face * speed : Vector3.Zero;
        return new FighterCommand(move, face,
            new HandCommand(AiControl.Guard(Hand.Left, sense.ArmReach), false),
            new HandCommand(AiControl.Guard(Hand.Right, sense.ArmReach), false));
    }
}
