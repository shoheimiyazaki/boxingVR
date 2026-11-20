using System.Globalization;
using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

/// <summary>
/// 肘の締め（<see cref="HandCommand.Tuck"/>、2026-11-17）: 拳の目標は同じまま肘の目標だけを変えると、肩・肘のサーボが追って、
/// 上腕と前腕が脇腹の前（体の中心寄り）へ寄る。胴への拳を肘と前腕で受けるための形で、姿勢を書き込まず目標として追う（§1.1）。
/// </summary>
[TestClass]
public sealed class ElbowTuckTests
{
    private const float Dt = 1f / 240f;

    private static (Vector3 Upper, Vector3 Fore) Hold(float tuck)
    {
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -1.408f, 0f), new Vector3(14.08f, 1.408f, 14.08f));
        var fighter = Fighter.Spawn(world, FighterTuning.Default, "tuck", Vector3.Zero, 0f);
        var reach = fighter.ArmReach;
        var left = AiControl.Guard(Hand.Left, reach);
        var right = AiControl.Guard(Hand.Right, reach);
        for (var step = 0; step < 806; step++)
        {
            fighter.ApplyControl(new FighterCommand(Vector3.Zero, Vector3.UnitZ, new HandCommand(left, false, Tuck: tuck), new HandCommand(right, false, Tuck: tuck)), Dt);
            world.Step(Dt);
        }

        var frame = fighter.Frame;
        return (frame.WorldToLocal(world.GetState(fighter.UpperArmId(Hand.Right)).Position) / reach,
            frame.WorldToLocal(world.GetState(fighter.ForearmId(Hand.Right)).Position) / reach);
    }

    [TestMethod]
    public void TuckedElbow_BringsTheArmInFrontOfTheRibs_WithTheSameGloveTarget()
    {
        var open = Hold(0f);
        var tucked = Hold(1f);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"right upper arm (x, y, z)/reach: open {open.Upper}, tucked {tucked.Upper}; forearm: open {open.Fore}, tucked {tucked.Fore}"));
        // 締めると上腕・前腕の中心が体の中心（X = 0）へ寄る（測定: 上腕 |x| 0.35 → 0.25、前腕 0.33 → 0.20 腕）。
        Assert.IsLessThan(MathF.Abs(open.Upper.X) - 0.05f, MathF.Abs(tucked.Upper.X), "上腕が脇へ寄る");
        Assert.IsLessThan(MathF.Abs(open.Fore.X) - 0.05f, MathF.Abs(tucked.Fore.X), "前腕が体の前へ寄る");
    }
}
