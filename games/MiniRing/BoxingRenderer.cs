using System.Numerics;
using Aigis.Physics;
using Aigis.Render;
using MiniRing.LowPoly;

namespace MiniRing;

/// <summary>
/// 試合の描画データ（Aigis の PrimitiveInstance / DebugLine）。体力バーもワールド空間の箱で作るので、
/// スクリーン HUD が使えない VR でもそのまま見える。
/// </summary>
public static class BoxingRenderer
{
    private const float EyeRadius = 0.005f;
    private static readonly Vector4 PlayerGlove = new(0.85f, 0.1f, 0.1f, 1f);
    private static readonly Vector4 CpuGlove = new(0.1f, 0.25f, 0.85f, 1f);

    /// <summary>
    /// 呼び出し側のリストを空にして詰め直す。へこんだ体は 1 万インスタンス近くになるので、毎フレーム新しいリストを
    /// 伸ばしながら作ると確保とコピーだけで数 ms かかる（計測: 新規 ≈ 5ms、使い回し ≈ 0.8ms）。
    /// </summary>
    public static void Fill(BoxingGame game, bool showTargetGhosts, List<PrimitiveInstance> instances, List<DebugLine> lines)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(lines);
        instances.Clear();
        lines.Clear();
        instances.AddRange(game.Ring.Visuals);
        AddFighter(instances, game.World, game.Player, game.Model, game.PlayerSkin, PlayerGlove);
        AddFighter(instances, game.World, game.Cpu, game.Model, game.CpuSkin, CpuGlove);
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
    }

    /// <summary>
    /// 体はローポリのメッシュ（<see cref="BoxerModel"/>）。へこんでいない部位は静的メッシュ 1 個、
    /// へこんだ部位は三角形ごとに直角三角形のインスタンスへ分解して、変形した形と変形量の色（白 → 赤）で描く。
    /// 体が白一色なので、どちらのファイターかはグローブの色で見分ける。
    /// </summary>
    private static void AddFighter(List<PrimitiveInstance> instances, PhysicsWorld world, Fighter fighter, BoxerModel model, BoxerSkin skin, Vector4 glove)
    {
        foreach (var id in fighter.Parts.All)
        {
            var s = world.GetState(id);
            var body = Matrix4x4.CreateFromQuaternion(s.Orientation) * Matrix4x4.CreateTranslation(s.Position);
            var dented = skin.Part(id);
            if (!dented.IsDented)
            {
                instances.Add(new PrimitiveInstance(PrimitiveKind.Cube, body, Vector3.Zero, DentedMesh.Undamaged, Mesh: model.RestMesh(fighter.KindOf(id))));
                continue;
            }

            var (locals, colors) = dented.Triangles();
            for (var i = 0; i < locals.Count; i++)
            {
                instances.Add(new PrimitiveInstance(PrimitiveKind.Cube, locals[i] * body, Vector3.Zero, colors[i], Mesh: RightTriangles.UnitMesh));
            }
        }

        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            instances.Add(new PrimitiveInstance(PrimitiveKind.Sphere, Matrix4x4.CreateTranslation(fighter.GloveCenter(hand)),
                new Vector3(FighterTuning.Default.GloveRadius, 0f, 0f), glove));
        }

        // 顔の向きが分かるよう、眼窩のノードに目を 2 つ（へこめば目も一緒に動く）。
        var head = world.GetState(fighter.Parts.Head);
        var face = skin.Part(fighter.Parts.Head);
        foreach (var socket in new[] { BoxerShapes.EyeSockets.Left, BoxerShapes.EyeSockets.Right })
        {
            instances.Add(new PrimitiveInstance(PrimitiveKind.Sphere,
                Matrix4x4.CreateTranslation(head.Position + Vector3.Transform(face.Position(socket), head.Orientation)),
                new Vector3(EyeRadius, 0f, 0f), new Vector4(0.05f, 0.05f, 0.05f, 1f)));
        }
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
