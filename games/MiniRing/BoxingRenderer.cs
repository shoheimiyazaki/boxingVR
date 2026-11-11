using System.Numerics;
using System.Runtime.CompilerServices;
using Aigis.Physics;
using Aigis.Render;
using MiniRing.LowPoly;

namespace MiniRing;

/// <summary>
/// 試合の描画データ（Aigis の PrimitiveInstance / DebugLine）。すべてワールド空間に置くので、
/// スクリーン HUD が使えない VR でもそのまま見える。
/// </summary>
public static class BoxingRenderer
{
    private const float EyeRadius = 0.005f;

    /// <summary>手の目標の玉の半径 [m]（60cm の人形で見て分かり、拳より小さい）。</summary>
    private const float TargetMarkerRadius = 0.011f;

    private static readonly Vector4 PlayerGlove = new(0.85f, 0.1f, 0.1f, 1f);
    private static readonly Vector4 CpuGlove = new(0.1f, 0.25f, 0.85f, 1f);

    /// <summary>グローブを外した素手の拳（肌の色、グローブの約半分の大きさ）。</summary>
    private static readonly Vector4 BareFist = new(0.86f, 0.6f, 0.42f, 1f);

    private const float BareFistScale = 0.55f;

    /// <summary>部屋（ローテーブルに載った箱庭のリングの周り）と会場の飾り。リングも部屋も動かないので 1 度だけ作る。</summary>
    private static readonly PrimitiveInstance[] Dressing =
        [.. LivingRoom.Visuals(BoxingGame.RingCenter, RoomAssets.Instance), .. ArenaLook.Dressing(BoxingGame.RingCenter, BoxingGame.RingSize, BoxingGame.RingSurfaceHeight)];

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
        instances.AddRange(Dressing);
        AddFighter(instances, game.World, game.Player, game.Model, game.PlayerSkin, PlayerGlove);
        AddFighter(instances, game.World, game.Cpu, game.Model, game.CpuSkin, CpuGlove);
        foreach (var weapon in game.Weapons)
        {
            // 武器もボクセルなので影の計算に入らない: 残っているボクセルを囲む見えない箱で影だけ落とす。
            if (ShadowBox(weapon.Body) is { } shadow)
            {
                instances.Add(new PrimitiveInstance(PrimitiveKind.Cube, shadow, Vector3.Zero, Vector4.One, MaterialKind.ShadowCastOnly));
            }
        }

        game.Debris.WriteInstances(instances);
        game.Wounds.WriteInstances(instances);

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
    }

    /// <summary>
    /// ボクセルの体の、ボクセル以外: グローブ（付いている剛体の先 — 前腕、切れたらその破片）。体そのものは
    /// <see cref="FillVoxels"/> の 3D テクスチャから GPU が描く。目は頭のボクセルにある。
    /// </summary>
    private static void AddVoxelFighter(List<PrimitiveInstance> instances, Fighter fighter, Voxels.BoxerBody voxels, Vector4 glove)
    {
        // ボクセルの体は影の計算に入らないので、部位と切り落とされた破片ごとに、残っているボクセルを囲む見えない箱で影だけ落とす
        // （キャンバスに立つ人の影。切れた腕の付け根は短い影、床に落ちた腕にも影）。
        foreach (var part in voxels.Parts.Concat(voxels.Fragments))
        {
            if (ShadowBox(part.Body) is { } model)
            {
                instances.Add(new PrimitiveInstance(PrimitiveKind.Cube, model, Vector3.Zero, Vector4.One, MaterialKind.ShadowCastOnly));
            }
        }

        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            // 武器を握る手はグローブを外した素手（肌色の小さな拳）。
            var wields = fighter.BareFist(hand); // 武器を放した後も素手のまま
            instances.Add(new PrimitiveInstance(PrimitiveKind.Sphere, Matrix4x4.CreateTranslation(fighter.GloveCenter(hand)),
                new Vector3(fighter.GloveRadius * (wields ? BareFistScale : 1f), 0f, 0f), wields ? BareFist : glove));
        }
    }

    /// <summary>格子ごとの「残っているボクセルを囲む箱」（格子が変わったときだけ数え直す。全セルを見るので毎フレームは重い）。</summary>
    private static readonly ConditionalWeakTable<Aigis.Foundation.Voxels.VoxelGrid, StrongBox<(int Version, (Vector3 Center, Vector3 HalfExtents)? Bounds)>> OccupiedCache = new();

    /// <summary>影だけを落とす箱（ワールド）。少し細めにして、体より太い影にしない。空の格子は null。</summary>
    private static Matrix4x4? ShadowBox(Aigis.Physics.Voxels.VoxelBody body)
    {
        var grid = body.Grid;
        var cached = OccupiedCache.GetValue(grid, static g => new StrongBox<(int, (Vector3, Vector3)?)>((g.Version, g.OccupiedBounds())));
        if (cached.Value.Version != grid.Version)
        {
            cached.Value = (grid.Version, grid.OccupiedBounds());
        }

        return cached.Value.Bounds is { } b
            ? Matrix4x4.CreateScale(b.HalfExtents * 2f * 0.85f) * Matrix4x4.CreateTranslation(b.Center) * body.GridToWorld()
            : null;
    }

    /// <summary>
    /// ボクセルの体（部位と切り落とされた破片）を描く指示を、呼び出し側のリストへ詰め直す。各部位の 3D テクスチャは
    /// 格子が変わったときだけ書き直され、GPU はそこから表面（空のボクセルとの境目の面）を作る。
    /// </summary>
    public static void FillVoxels(BoxingGame game, List<Aigis.Render.Voxels.VoxelVolumeDraw> voxels)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(voxels);
        voxels.Clear();
        foreach (var fighter in new[] { game.Player, game.Cpu })
        {
            if (fighter.Voxels is not { } body)
            {
                continue;
            }

            foreach (var part in body.Parts)
            {
                voxels.Add(part.Draw());
            }

            foreach (var fragment in body.Fragments)
            {
                voxels.Add(fragment.Draw());
            }
        }

        foreach (var weapon in game.Weapons)
        {
            voxels.Add(weapon.Draw());
        }
    }

    /// <summary>
    /// 体はローポリのメッシュ（<see cref="BoxerModel"/>）。へこんでいない部位は静的メッシュ 1 個、
    /// へこんだ部位は三角形ごとに直角三角形のインスタンスへ分解して、変形した形と変形量の色（白 → 赤）で描く。
    /// 体が白一色なので、どちらのファイターかはグローブの色で見分ける。
    /// </summary>
    private static void AddFighter(List<PrimitiveInstance> instances, PhysicsWorld world, Fighter fighter, BoxerModel model, BoxerSkin skin, Vector4 glove)
    {
        if (fighter.Voxels is { } voxels)
        {
            AddVoxelFighter(instances, fighter, voxels, glove);
            return;
        }

        foreach (var id in fighter.AllParts)
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
            // 武器を握る手はグローブを外した素手（肌色の小さな拳）。
            var wields = fighter.BareFist(hand); // 武器を放した後も素手のまま
            instances.Add(new PrimitiveInstance(PrimitiveKind.Sphere, Matrix4x4.CreateTranslation(fighter.GloveCenter(hand)),
                new Vector3(fighter.GloveRadius * (wields ? BareFistScale : 1f), 0f, 0f), wields ? BareFist : glove));
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
}
