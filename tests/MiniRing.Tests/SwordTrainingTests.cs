using System.Numerics;
using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>VR でお手本の動きを作る稽古（<see cref="SwordTraining"/>・<see cref="GuardHold"/>・<see cref="StickFlick"/>、docs/VR_MOTION.md §5）。</summary>
[TestClass]
public sealed class SwordTrainingTests
{
    [TestMethod]
    public void EveryKatanaForm_IsInTheMenu_WithAStartingGapInsideTheRing()
    {
        using var game = new BoxingGame(corners: SwordTraining.Corners(WeaponKind.Katana, 2.817f));
        var forms = SwordTraining.Forms(WeaponKind.Katana, game.PlayerSense.ArmReach);
        CollectionAssert.AreEqual(FormSwordsman.Forms(WeaponKind.Katana).Select(f => f.Name).ToList(), forms.Select(f => f.Name).ToList(), "型の全部（間の短い型も）を型の順に");
        foreach (var form in forms)
        {
            Assert.IsTrue(form.Gap >= SwordTraining.MinGap && form.Gap <= SwordTraining.Ring.Size - 1.127f, $"{form.Name} の間 {form.Gap:0.00} m");
        }

        var shomen = forms.Single(f => f.Name == "shomen");
        var tsukaAte = forms.Single(f => f.Name == "tsuka-ate");
        Assert.IsTrue(shomen.Gap > tsukaAte.Gap, $"面（{shomen.Gap:0.00} m）は柄頭打ち（{tsukaAte.Gap:0.00} m）より遠くから切り込む");
    }

    [TestMethod]
    [TestCategory("slow")]
    public void TheTrainingWorld_StartsAtTheFormsGap_AndTheSeiganTargetStaysPut()
    {
        const float gap = 2.254f;
        using var game = new BoxingGame(corners: SwordTraining.Corners(WeaponKind.Katana, gap));
        Assert.IsNotNull(game.WeaponOf(game.Player), "自分は刀を握る");
        Assert.IsNotNull(game.WeaponOf(game.Cpu), "相手も刀を握る");
        var start = game.Cpu.Frame.Origin;
        var between = Vector3.Distance(game.Player.Frame.Origin with { Y = 0f }, start with { Y = 0f });
        Assert.AreEqual(gap, between, 0.2254f, "始めは型の間で向き合う");

        // 始めの約 2 秒は構えへ沈む（肩の中心が前へ十数 cm 出る）。そこから約 2.5 秒、歩かずにその場にいる。
        // 2026-11-27（物理 106 仮）: 沈む間の最後の踏み直しが 1.7 秒より後（2.0〜2.4 秒）にずれることがあり（物理 105 でも 2.0 秒まで動いていた）、
        // 沈み終えた後は 0.3 cm 以内で止まっているので、沈み終えた 2.5 秒から見る。
        var settled = Vector3.Zero;
        for (var t = 0f; t < 5.035f; t += 1f / 240f)
        {
            game.Step(default, 1f / 240f);
            if (settled == Vector3.Zero && t >= 2.518f)
            {
                settled = game.Cpu.Frame.Origin;
            }
        }

        var moved = Vector3.Distance(game.Cpu.Frame.Origin with { Y = 0f }, settled with { Y = 0f });
        Assert.IsTrue(moved < 0.05634f, $"正眼の相手は歩かない（構えに落ち着いてから {moved * 100f:0.0} cm）");
        var sword = game.WeaponOf(game.Cpu)!;
        var tip = game.World.GetState(sword.Body.BodyId);
        var forward = Vector3.Normalize((game.Player.Frame.Origin - game.Cpu.Frame.Origin) with { Y = 0f });
        var blade = Vector3.Transform(Vector3.UnitX, tip.Orientation);
        Assert.IsTrue(Vector3.Dot(blade, forward) > 0.5f, $"切っ先は自分へ向く（中段）: 刃の向きと相手への向きの内積 {Vector3.Dot(blade, forward):0.00}");
    }

    [TestMethod]
    public void TheStick_ChangesOncePerFlick()
    {
        var flick = new StickFlick();
        var values = new[] { 0f, 0.5f, 0.8f, 0.9f, 0.6f, 0.2f, 0f, -0.75f, -0.9f, -0.1f, 0.71f };
        var steps = values.Select(flick.Step).ToArray();
        CollectionAssert.AreEqual(new[] { 0, 0, 1, 0, 0, 0, 0, -1, 0, 0, 1 }, steps, "倒した瞬間だけ、戻すまで次は無い");
    }
}
