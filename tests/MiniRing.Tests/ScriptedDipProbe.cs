using System.Globalization;
using System.Numerics;

namespace MiniRing.Tests;

/// <summary>調べ物（2026-11-27）: 頭を下げる指令（腕の長さの -0.56 倍を 0.3 秒で下げて戻す）だけを出す 1 体で、骨盤の高さ（立った高さに対する比）の沈みと戻りを、移動なし・前へ移動の 2 通りで出す。master と同じファイルで比べる。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class ScriptedDipProbe
{
    private sealed class Dip(float move) : IFighterControl
    {
        private float _t;

        public FighterCommand Read(in FighterSense sense, float dt)
        {
            _t += dt;
            var phase = _t - 1f;
            var depth = phase is > 0f and < 0.6f ? MathF.Sin(phase / 0.6f * MathF.PI) : 0f;
            var face = sense.OpponentChest - sense.Self.Origin;
            face.Y = 0f;
            var forward = face.LengthSquared() > 1e-6f ? Vector3.Normalize(face) : Vector3.UnitZ;
            var reach = sense.ArmReach;
            HandCommand Guard(Hand hand) => new(new Vector3(hand.LocalSide() * 0.15f, 0.1f, 0.5f) * reach, false);
            return new FighterCommand(phase is > -0.3f and < 1f ? forward * move : Vector3.Zero, forward, Guard(Hand.Left), Guard(Hand.Right),
                HeadOffset: new Vector3(0f, -0.56f * depth * reach, 0f));
        }
    }

    [TestMethod]
    [DataRow(0f)]
    [DataRow(1f)]
    public void Sink(float move)
    {
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("dip", _ => new StandGuard(), _ => new Dip(move)) with { Referee = false });
        float Floor(Fighter f) => MathF.Min(f.AnklePosition(Hand.Left).Y, f.AnklePosition(Hand.Right).Y);
        var f = game.Player;
        var inv = CultureInfo.InvariantCulture;
        var stand = f.PelvisPosition.Y - Floor(f);
        var low = 9f;
        var trace = new List<string>();
        for (var i = 0; i < 360; i++)
        {
            game.Step(default, 1f / 60f);
            var r = (f.PelvisPosition.Y - Floor(f)) / stand;
            if (i > 20)
            {
                low = MathF.Min(low, r);
            }

            if (i % 12 == 0 && i > 40)
            {
                trace.Add(string.Create(inv, $"{i / 60f:F1}:{r:F2}"));
            }
        }

        Console.WriteLine(string.Create(inv, $"DIP move {move}: low {low:F2} trace {string.Join(' ', trace)}"));
    }
}
