using System.Numerics;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 一人称（VR の <c>--first-person</c>）の手の目標が、目に見えているコントローラの点と一致すること（2026-11-24、ユーザーの要望
/// 「コントローラの位置と目標を見た目同じにしてください」）。目の描画と同じ変換（<see cref="VrWorldPlacement"/>）でコントローラをワールドへ写し、
/// 体フレームへ戻すので、増幅（<see cref="VrPlayerMapper.MotionGain"/>）は掛からない。体が目標を追う（届かない・遅れる）のは別の話（§1.1）。
/// </summary>
[TestClass]
public sealed class VrFirstPersonHandTests
{
    private const float Dt = 1f / 240f;

    private static readonly Vector3 Head = new(0f, 1.62f, 0f);

    private static BoxingGame NewGame() => new(FighterTuning.Default with { PlayerFatigue = false },
        corners: SwordTraining.Corners(WeaponKind.Katana, 2.535f)) { HidePlayerHead = true };

    private static VrRawFrame Raw(Vector3 head, Vector3 left, Vector3 right, Quaternion leftRotation, Quaternion rightRotation) => new(
        head, Quaternion.Identity, left, right, true, true,
        Vector2.Zero, Vector2.Zero, 0f, 0f, false, true, leftRotation, rightRotation);

    private static VrPlayerMapper Mapper(bool weapon) => weapon
        ? new VrPlayerMapper(weaponMode: true) { GripHolds = true, FirstPerson = true, HeldWeapon = WeaponKind.Katana }
        : new VrPlayerMapper { FirstPerson = true };

    private static float Gap(in FighterSense sense, in HandCommand hand, Vector3 controllerWorld)
        => Vector3.Distance(sense.Self.LocalToWorld(hand.TargetLocal), controllerWorld);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TheHandTargetIsTheControllerSeenThroughTheEyeTransform(bool weapon)
    {
        using var game = NewGame();
        var mapper = Mapper(weapon);
        var guardLeft = new Vector3(-0.15f, 1.35f, -0.25f);
        var guardRight = new Vector3(0.15f, 1.35f, -0.25f);
        var jab = guardLeft + new Vector3(0.05f, 0.05f, -0.45f); // 構えから 45 cm 前へ突き出す
        var hook = guardRight + new Vector3(-0.3f, 0.1f, -0.2f);
        foreach (var (left, right) in new[] { (guardLeft, guardRight), (jab, guardRight), (guardLeft, hook), (jab, hook) })
        {
            var sense = game.PlayerSense;
            var command = mapper.Read(Raw(Head, left, right, Quaternion.Identity, Quaternion.Identity), sense, Dt).Player;
            var placement = mapper.Placement ?? throw new AssertFailedException("身長の合わせで一人称の変換が決まっていない");
            var leftGap = Gap(sense, command.Left, placement.Point(left));
            var rightGap = Gap(sense, command.Right, placement.Point(right));
            Console.WriteLine($"weapon {weapon}: left gap {leftGap * 1000f:0.000} mm, right gap {rightGap * 1000f:0.000} mm (scale {placement.Scale:0.000})");
            Assert.AreEqual(0f, leftGap, 1e-5f, "左手の目標が見えている左のコントローラとずれる");
            Assert.AreEqual(0f, rightGap, 1e-5f, "右手の目標が見えている右のコントローラとずれる");

            // 描画（表示の空間）でも同じ点: 目と同じ変換で表示したコントローラ = 表示した手の目標。
            var shown = placement.Displayed(game.Display);
            Assert.AreEqual(0f, Vector3.Distance(shown.Point(right), game.Display.Point(sense.Self.LocalToWorld(command.Right.TargetLocal))), 1e-5f);
            game.Step(new BoxingInput(command), Dt); // 体が動いて体フレームが変わっても、次の歩でまた一致する
        }
    }

    [TestMethod]
    public void MovingTheControllerMovesTheTargetByTheSameWorldDistance()
    {
        using var game = NewGame();
        var mapper = Mapper(weapon: false);
        var left = new Vector3(-0.15f, 1.35f, -0.25f);
        var right = new Vector3(0.15f, 1.35f, -0.25f);
        var sense = game.PlayerSense;
        var before = mapper.Read(Raw(Head, left, right, Quaternion.Identity, Quaternion.Identity), sense, Dt).Player;
        var reach = new Vector3(0f, 0f, -0.4f);
        var after = mapper.Read(Raw(Head, left, right + reach, Quaternion.Identity, Quaternion.Identity), sense, Dt).Player;
        var scale = mapper.Placement!.Value.Scale;
        var moved = (after.Right.TargetLocal - before.Right.TargetLocal).Length();
        Console.WriteLine($"controller 40 cm → target {moved * 100f:0.00} cm (scale {scale:0.000}, gain {mapper.HandGain})");
        Assert.AreEqual(1f, mapper.HandGain);
        Assert.AreEqual(reach.Length() * scale, moved, 1e-5f, "一人称では増幅しない（倍率 = 変換の縮尺だけ）");
    }

    [TestMethod]
    public void TheWeaponPointsWhereTheControllerPoints()
    {
        using var game = NewGame();
        var mapper = Mapper(weapon: true);
        var sense = game.PlayerSense;
        var rotation = Quaternion.CreateFromYawPitchRoll(0.5f, 0.6f, 0.2f); // 右へ振り、上へ立てた刀
        var command = mapper.Read(Raw(Head, new Vector3(-0.15f, 1.35f, -0.25f), new Vector3(0.15f, 1.35f, -0.25f), rotation, rotation), sense, Dt).Player;
        var placement = mapper.Placement!.Value;
        var seen = Vector3.Transform(-Vector3.UnitZ, placement.Rotation(rotation)); // 目に見えているコントローラの前
        var blade = sense.Self.Rotate(Vector3.Transform(Vector3.UnitX, command.Right.Orientation!.Value)); // 武器のローカル X = 打つ側の先
        Console.WriteLine($"controller forward {seen}, weapon axis {blade}");
        Assert.AreEqual(1f, Vector3.Dot(Vector3.Normalize(seen), Vector3.Normalize(blade)), 1e-4f, "武器の向きが見えているコントローラの向きとずれる");
    }

    [TestMethod]
    public void OnlyThePlayersAttachedBodyIsTranslucentAndTheVrSnapshotKeepsIt()
    {
        // 一人称の描画: 自分の体の部位だけ半透明。相手・武器（握った刀）は不透明。VR の描画スレッドへの写しでも不透明度が残る。
        using var game = NewGame();
        game.PlayerBodyOpacity = BoxingRenderer.FirstPersonBodyOpacity;
        var draws = game.BuildVoxelVolumes();
        // 頭（と顔の層、首のつなぎ）は描かない（HidePlayerHead）。ほかの関節のつなぎ（JointBridge.View）は体の一部として描く。
        var playerLayers = game.Player.Voxels!.Layers.Count(l => l.Kind != BodyPartKind.Head) + game.Player.Voxels.Bridges.Count(b => b.Child.Kind != BodyPartKind.Head);
        Assert.AreEqual(playerLayers, draws.Count(d => d.IsTranslucent), "半透明は自分の体の部位（頭を除く）だけ");
        Assert.IsTrue(draws.Where(d => d.IsTranslucent).All(d => d.Opacity == BoxingRenderer.FirstPersonBodyOpacity));
        Assert.IsTrue(draws.Count(d => !d.IsTranslucent) >= game.Cpu.Voxels!.Layers.Count, "相手の体は不透明のまま");

        var snapshot = new Aigis.Render.Voxels.VoxelSnapshotProducer().Capture(draws);
        var resolved = new Aigis.Render.Voxels.VoxelSnapshotConsumer().Resolve(snapshot);
        CollectionAssert.AreEqual(draws.Select(d => d.Opacity).ToArray(), resolved.Select(d => d.Opacity).ToArray(), "写しで不透明度が落ちる");

        game.PlayerBodyOpacity = 1f;
        Assert.IsFalse(game.BuildVoxelVolumes().Any(d => d.IsTranslucent), "既定（三人称・観戦）はすべて不透明");
    }

    [TestMethod]
    public void TheTableTopPuppetKeepsItsAmplifiedHands()
    {
        var mapper = new VrPlayerMapper();
        Assert.AreEqual(VrPlayerMapper.MotionGain, mapper.HandGain, "一人称でない（卓上の人形を外から操る）ときは今までどおり増幅する");
    }
}
