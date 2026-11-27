using System.Globalization;
using System.Numerics;

namespace MiniRing.Tests;

/// <summary>調べ物（BASE_BODY_PLAN §10.8）: 頭を下げる指令（腕の長さの -0.7 倍）を 1 秒保ったときの胸・骨盤の傾き。master と同じファイルで比べる。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class HoldDipProbe
{
    private sealed class Hold(float depthArms, float move) : IFighterControl
    {
        private float _t;

        public FighterCommand Read(in FighterSense sense, float dt)
        {
            _t += dt;
            var phase = _t - 1f;
            var depth = phase <= 0f ? 0f : phase < 0.3f ? phase / 0.3f : phase < 1.3f ? 1f : phase < 1.6f ? 1f - ((phase - 1.3f) / 0.3f) : 0f;
            var face = sense.OpponentChest - sense.Self.Origin;
            face.Y = 0f;
            var forward = face.LengthSquared() > 1e-6f ? Vector3.Normalize(face) : Vector3.UnitZ;
            var reach = sense.ArmReach;
            HandCommand Guard(Hand hand) => new(new Vector3(hand.LocalSide() * 0.15f, 0.1f, 0.5f) * reach, false);
            return new FighterCommand(phase is > -0.5f and < 1.3f ? forward * move : Vector3.Zero, forward, Guard(Hand.Left), Guard(Hand.Right), HeadOffset: new Vector3(0f, -depthArms * depth * reach, 0.09f * depth * reach));
        }
    }

    [TestMethod]
    [DataRow(0.7f, 1f)]
    [DataRow(0.7f, 0.5f)]
    public void Chest(float arms, float move)
    {
        using var game = new BoxingGame(FighterTuning.HybridPunch, 1, corners: new BoxingCorners("dip", _ => new StandGuard(), _ => new Hold(arms, move)) with { Referee = false });
        var f = game.Player;
        var inv = CultureInfo.InvariantCulture;
        var trace = new List<string>();
        var maxChest = 0f;
        for (var i = 0; i < 240; i++)
        {
            game.Step(default, 1f / 60f);
            var cup = Vector3.Transform(Vector3.UnitY, game.World.GetState(f.Parts.Chest).Orientation);
            var pup = Vector3.Transform(Vector3.UnitY, game.World.GetState(f.Parts.Pelvis).Orientation);
            var chest = MathF.Acos(Math.Clamp(cup.Y, -1f, 1f)) * 57.3f;
            maxChest = MathF.Max(maxChest, chest);
            if (i % 12 == 0 && i > 50)
            {
                trace.Add(string.Create(inv, $"{i / 60f:F1}:{chest:F0}/{MathF.Acos(Math.Clamp(pup.Y, -1f, 1f)) * 57.3f:F0}{(f.State == FighterState.Standing ? "" : "D")}"));
            }
        }

        Console.WriteLine(string.Create(inv, $"HOLDDIP arms {arms} move {move}: max chest {maxChest:F0} trace(chest/pelvis) {string.Join(' ', trace)}"));
    }
}
