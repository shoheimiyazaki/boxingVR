using System.Numerics;
using Aigis.Physics;
using Aigis.Render;
using MiniRing.Visual;

namespace MiniRing;

/// <summary>
/// 試合の描画データ（Aigis の PrimitiveInstance / DebugLine）。体力バーもワールド空間の箱で作るので、
/// スクリーン HUD が使えない VR でもそのまま見える。
/// </summary>
public static class BoxingRenderer
{
    // 体は単色（2 人の見分けは色味をわずかに変えるだけ）、グローブだけ色付き。
    private static readonly Vector4 PlayerColor = new(0.8f, 0.75f, 0.69f, 1f);
    private static readonly Vector4 PlayerGlove = new(0.85f, 0.1f, 0.1f, 1f);
    private static readonly Vector4 CpuColor = new(0.66f, 0.69f, 0.75f, 1f);
    private static readonly Vector4 CpuGlove = new(0.1f, 0.25f, 0.85f, 1f);
    private static readonly Vector4 Bruise = new(0.5f, 0.12f, 0.14f, 1f);

    /// <summary>手の目標の玉の半径 [m]（60cm の人形で見て分かり、拳より小さい）。</summary>
    private const float TargetMarkerRadius = 0.011f;

    public static (IReadOnlyList<PrimitiveInstance> Instances, IReadOnlyList<DebugLine> Lines) Build(BoxingGame game, bool showTargetGhosts)
    {
        ArgumentNullException.ThrowIfNull(game);
        var instances = new List<PrimitiveInstance>(game.Ring.Visuals);
        var lines = new List<DebugLine>();
        AddFighter(instances, game.World, game.Player, PlayerColor, PlayerGlove);
        AddFighter(instances, game.World, game.Cpu, CpuColor, CpuGlove);

        if (showTargetGhosts)
        {
            // 手の目標（バネの引っ張り先）を常に見せる: 「自分の手がここへ引っ張っている」ことが分かる。
            // 線も玉も深度テストなし（XRayFill）で、グローブや体の中に入っても隠れない。
            foreach (var hand in new[] { Hand.Left, Hand.Right })
            {
                var color = game.Player.IsHolding(hand) ? new Vector4(1f, 0.55f, 0.15f, 1f)
                    : game.Player.LastCommand.For(hand).Grip ? new Vector4(1f, 0.85f, 0.2f, 1f)
                    : new Vector4(0.4f, 0.9f, 1f, 1f);
                lines.Add(new DebugLine(game.Player.GloveCenter(hand), game.Player.HandTargetWorld(hand), color));
                instances.Add(new PrimitiveInstance(PrimitiveKind.Sphere, Matrix4x4.CreateTranslation(game.Player.HandTargetWorld(hand)),
                    new Vector3(TargetMarkerRadius, 0f, 0f), color, MaterialKind.XRayFill));
            }
        }

        return (instances, lines);
    }

    private static void AddFighter(List<PrimitiveInstance> instances, PhysicsWorld world, Fighter fighter, Vector4 color, Vector4 glove)
        // 見た目は Visual/BoxerModel（剛体ごとの滑らかなメッシュ）。傷んだ部位だけ赤黒く寄せる（ゲームの情報なので残す）。
        => BoxerRenderer.Add(instances, world, fighter, color, glove,
            (part, body) => Vector4.Lerp(body, Bruise, 0.75f * fighter.Condition.Bruise(RegionOf(fighter.Parts, part))));

    /// <summary>剛体 → 傷の区分。Aigis の "Right" 部位は本人の左（Fighter.Locate と同じ対応）。</summary>
    private static BodyRegion RegionOf(HumanoidRagdoll.Parts p, int part)
        => part == p.Head ? BodyRegion.Head
            : part == p.RightUpperArm ? BodyRegion.LeftUpperArm
            : part == p.LeftUpperArm ? BodyRegion.RightUpperArm
            : part == p.RightForearm ? BodyRegion.LeftForearm
            : part == p.LeftForearm ? BodyRegion.RightForearm
            : part == p.Chest || part == p.Pelvis ? BodyRegion.Torso
            : BodyRegion.Legs;
}
