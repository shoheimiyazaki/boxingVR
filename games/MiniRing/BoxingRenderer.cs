using System.Numerics;
using Aigis.Physics;
using Aigis.Render;

namespace MiniRing;

/// <summary>
/// 試合の描画データ（Aigis の PrimitiveInstance / DebugLine）。体力バーもワールド空間の箱で作るので、
/// スクリーン HUD が使えない VR でもそのまま見える。
/// </summary>
public static class BoxingRenderer
{
    private static readonly Vector4 PlayerColor = new(0.9f, 0.55f, 0.45f, 1f);
    private static readonly Vector4 PlayerGlove = new(0.85f, 0.1f, 0.1f, 1f);
    private static readonly Vector4 CpuColor = new(0.55f, 0.65f, 0.9f, 1f);
    private static readonly Vector4 CpuGlove = new(0.1f, 0.25f, 0.85f, 1f);
    private static readonly Vector4 Skin = new(0.95f, 0.8f, 0.68f, 1f);
    private static readonly Vector4 Bruise = new(0.45f, 0.05f, 0.1f, 1f);

    public static (IReadOnlyList<PrimitiveInstance> Instances, IReadOnlyList<DebugLine> Lines) Build(BoxingGame game, bool showTargetGhosts)
    {
        ArgumentNullException.ThrowIfNull(game);
        var instances = new List<PrimitiveInstance>(game.Ring.Visuals);
        var lines = new List<DebugLine>();
        AddFighter(instances, game.World, game.Player, PlayerColor, PlayerGlove);
        AddFighter(instances, game.World, game.Cpu, CpuColor, CpuGlove);
        AddBars(instances, game.Player, new Vector4(0.95f, 0.3f, 0.25f, 1f));
        AddBars(instances, game.Cpu, new Vector4(0.3f, 0.5f, 1f, 1f));

        if (showTargetGhosts)
        {
            // 手の目標（バネの引っ張り先）を細線で見せる: 「自分の手がここへ引っ張っている」ことが分かる。
            foreach (var hand in new[] { Hand.Left, Hand.Right })
            {
                var color = game.Player.IsHolding(hand) ? new Vector4(1f, 0.55f, 0.15f, 1f)
                    : game.Player.LastCommand.For(hand).Grip ? new Vector4(1f, 0.85f, 0.2f, 1f)
                    : new Vector4(0.4f, 0.9f, 1f, 1f);
                lines.Add(new DebugLine(game.Player.GloveCenter(hand), game.Player.HandTargetWorld(hand), color));
                instances.Add(new PrimitiveInstance(PrimitiveKind.Sphere, Matrix4x4.CreateTranslation(game.Player.HandTargetWorld(hand)),
                    new Vector3(0.008f, 0f, 0f), color, MaterialKind.Emissive));
            }
        }

        return (instances, lines);
    }

    private static void AddFighter(List<PrimitiveInstance> instances, PhysicsWorld world, Fighter fighter, Vector4 color, Vector4 glove)
    {
        foreach (var part in HumanoidRagdoll.DescribeForRender(fighter.Parts, fighter.HalfExtents))
        {
            if (part.Role == HumanoidRagdoll.RenderPartRole.Hand)
            {
                continue; // 手はグローブ球で描く
            }

            var s = world.GetState(part.Id);
            var tint = part.Id == fighter.Parts.Head ? Skin
                : part.Role == HumanoidRagdoll.RenderPartRole.ChestFrontPanel ? HumanoidRagdoll.AnteriorPanelColor(color)
                : color;
            tint = Vector4.Lerp(tint, Bruise, 1f - Health(fighter, part.Id));
            instances.Add(new PrimitiveInstance(PrimitiveKind.Cube, part.ModelMatrix(s.Position, s.Orientation), Vector3.Zero, tint));
        }

        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            instances.Add(new PrimitiveInstance(PrimitiveKind.Sphere, Matrix4x4.CreateTranslation(fighter.GloveCenter(hand)),
                new Vector3(FighterTuning.Default.GloveRadius, 0f, 0f), glove));
        }

        // 顔の向きが分かるよう目を 2 つ。
        var head = world.GetState(fighter.Parts.Head);
        foreach (var side in new[] { -1f, 1f })
        {
            var eye = new Vector3(side * fighter.HalfExtents.Head.X * 0.4f, fighter.HalfExtents.Head.Y * 0.2f, fighter.HalfExtents.Head.Z);
            instances.Add(new PrimitiveInstance(PrimitiveKind.Sphere, Matrix4x4.CreateTranslation(head.Position + Vector3.Transform(eye, head.Orientation)),
                new Vector3(0.005f, 0f, 0f), new Vector4(0.05f, 0.05f, 0.05f, 1f)));
        }
    }

    /// <summary>傷んだ部位ほど赤黒く（肩・肘・脚・頭）。打たれた場所と性能低下が見て分かる。</summary>
    private static float Health(Fighter fighter, int part)
    {
        var p = fighter.Parts;
        var c = fighter.Condition;
        // Aigis の "Right" 部位は本人の左（Fighter.Locate と同じ対応）。
        return part == p.Head ? Math.Min(c.Consciousness, c.Neck)
            : part == p.RightUpperArm ? c.Shoulder(Hand.Left)
            : part == p.LeftUpperArm ? c.Shoulder(Hand.Right)
            : part == p.RightForearm ? c.Elbow(Hand.Left)
            : part == p.LeftForearm ? c.Elbow(Hand.Right)
            : part == p.Chest ? Math.Min(c.Heart, 0.5f + (0.5f * c.Stamina))
            : part == p.Pelvis ? 1f
            : c.Legs;
    }

    /// <summary>頭上に 3 本: 意識（チーム色）・スタミナ（黄）・脚（緑）。ワールド空間なので VR でも見える。</summary>
    private static void AddBars(List<PrimitiveInstance> instances, Fighter fighter, Vector4 color)
    {
        var top = fighter.HeadPosition + new Vector3(0f, fighter.HalfExtents.Head.Y + 0.06f, 0f);
        AddBar(instances, top, fighter.Condition.Consciousness, color, 0.12f, 0.012f);
        AddBar(instances, top - new Vector3(0f, 0.014f, 0f), fighter.Condition.Stamina, new Vector4(0.95f, 0.8f, 0.2f, 1f), 0.12f, 0.006f);
        AddBar(instances, top - new Vector3(0f, 0.023f, 0f), fighter.Condition.Legs, new Vector4(0.3f, 0.85f, 0.4f, 1f), 0.12f, 0.006f);
    }

    private static void AddBar(List<PrimitiveInstance> instances, Vector3 top, float ratio, Vector4 color, float width, float height)
    {
        var r = Math.Clamp(ratio, 0f, 1f);
        instances.Add(new PrimitiveInstance(PrimitiveKind.Cube, Matrix4x4.CreateScale(new Vector3(width + 0.004f, height + 0.003f, 0.004f)) * Matrix4x4.CreateTranslation(top),
            Vector3.Zero, new Vector4(0.05f, 0.05f, 0.05f, 1f)));
        if (r > 0f)
        {
            var center = top + new Vector3(-width * (1f - r) * 0.5f, 0f, 0f);
            instances.Add(new PrimitiveInstance(PrimitiveKind.Cube, Matrix4x4.CreateScale(new Vector3(width * r, height, 0.006f)) * Matrix4x4.CreateTranslation(center),
                Vector3.Zero, color, MaterialKind.Emissive));
        }
    }
}
