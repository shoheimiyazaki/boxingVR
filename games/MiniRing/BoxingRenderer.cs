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
    private const float EyeRadius = 0.01408f;

    /// <summary>手の目標の玉の半径 [m]（物理の空間。60cm の人形の大きさに縮めて見ても分かり、拳より小さい）。</summary>
    private const float TargetMarkerRadius = 0.03099f;

    private static readonly Vector4 PlayerGlove = new(0.85f, 0.1f, 0.1f, 1f);
    private static readonly Vector4 CpuGlove = new(0.1f, 0.25f, 0.85f, 1f);

    /// <summary>グローブを外した素手の拳（肌の色、グローブの約半分の大きさ）。</summary>
    private static readonly Vector4 BareFist = new(0.86f, 0.6f, 0.42f, 1f);

    private const float BareFistScale = 0.55f;

    /// <summary>
    /// 一人称の自分の体の不透明度（<see cref="BoxingGame.PlayerBodyOpacity"/>）。腕・胴の形と傷（色）は分かり、その向こうの相手の拳・体が
    /// 見える値（2026-11-24、ユーザーの要望「自分の身体がちょっと邪魔なので半透明に」。<c>--first-person-selftest</c> の画像で選んだ）。
    /// </summary>
    public const float FirstPersonBodyOpacity = 0.35f;

    /// <summary>
    /// 部屋（ローテーブルに載った箱庭のリングの周り）と会場の飾り（表示の空間）。リングも部屋も動かないので、リングの大きさごとに 1 度だけ作る。
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(RingLayout, float), PrimitiveInstance[]> Dressings = new();

    private static PrimitiveInstance[] Dressing(RingLayout layout, float windowLight) => Dressings.GetOrAdd((layout, windowLight), key =>
    {
        var display = DisplaySpace.Standard;
        return [.. LivingRoom.Visuals(key.Item1, RoomAssets.Instance, key.Item2),
            .. ArenaLook.Dressing(display.Point(key.Item1.Center), display.Length(key.Item1.Size), display.Length(BoxingGame.RingSurfaceHeight))];
    });

    /// <summary>
    /// 呼び出し側のリストを空にして詰め直す。へこんだ体は 1 万インスタンス近くになるので、毎フレーム新しいリストを
    /// 伸ばしながら作ると確保とコピーだけで数 ms かかる（計測: 新規 ≈ 5ms、使い回し ≈ 0.8ms）。
    /// リング・体・武器・破片は物理の空間で作り、最後に表示の空間（<see cref="DisplaySpace"/>）へ移す。部屋と会場の飾りは初めから表示の空間。
    /// </summary>
    public static void Fill(BoxingGame game, bool showTargetGhosts, List<PrimitiveInstance> instances, List<DebugLine> lines)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(lines);
        instances.Clear();
        lines.Clear();
        instances.AddRange(game.Ring.Visuals);
        var roomStart = instances.Count;
        instances.AddRange(Dressing(game.Layout, game.Mood.Window));
        var roomEnd = instances.Count;
        FillArena(game, showTargetGhosts, instances, lines);
        var display = DisplaySpace.Standard;
        display.Instances(instances, 0, roomStart);
        display.Instances(instances, roomEnd, instances.Count);
        display.Lines(lines);
    }

    /// <summary>物理の空間の描画（体・武器・破片・傷・息・手の目標）。</summary>
    private static void FillArena(BoxingGame game, bool showTargetGhosts, List<PrimitiveInstance> instances, List<DebugLine> lines)
    {
        AddFighter(instances, game.World, game.Player, game.Model, game.PlayerSkin, PlayerGlove);
        AddFighter(instances, game.World, game.Cpu, game.Model, game.CpuSkin, CpuGlove);
        for (var seat = 2; seat < game.AllFighters.Count; seat++)
        {
            var other = game.AllFighters[seat];
            AddFighter(instances, game.World, other, game.Model, game.SkinOf(other), game.InitialCorners.RedKit(seat) ? PlayerGlove : CpuGlove); // バトルロイヤルの追加の席
        }

        foreach (var weapon in game.Weapons)
        {
            // 武器もボクセルなので影の計算に入らない: 残っているボクセルを囲む見えない箱で影だけ落とす。
            for (var i = 0; i < weapon.Grids.Count; i++)
            {
                if (ShadowBox(weapon.Grids[i]) is { } shadow)
                {
                    instances.Add(new PrimitiveInstance(PrimitiveKind.Cube, shadow, Vector3.Zero, Vector4.One, MaterialKind.ShadowCastOnly));
                }
            }
        }

        game.Debris.WriteInstances(instances);
        game.Wounds.WriteInstances(instances);
        TiredLook.WriteInstances(game, instances); // 息が上がった側の口から白い息（見た目だけ）
        Break.BreakVaporLook.WriteInstances(game, instances); // たばこの煙・口に含んで吐いた水の飛沫（見た目だけ）

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
        foreach (var part in voxels.Layers.Concat(voxels.Fragments))
        {
            if (ShadowBox(part.Body) is { } model)
            {
                instances.Add(new PrimitiveInstance(PrimitiveKind.Cube, model, Vector3.Zero, Vector4.One, MaterialKind.ShadowCastOnly));
            }
        }

        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            // 武器を握る手はグローブを外した素手（肌色の小さな拳）。
            if (fighter.FistCovered(hand))
            {
                continue; // ボクセルのグローブ（武器と同じに描く。腕が折れた後もぶら下がる）か、手が無い
            }

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
        foreach (var fighter in game.AllFighters)
        {
            if (fighter.Voxels is not { } body)
            {
                continue;
            }

            // 目: 相手（バトルロイヤルでは狙っている相手）を見る黒目とまばたき（見た目だけ）。
            var other = game.TargetOf(fighter);
            // The angled eye pairs carry their pupils in their own voxel sockets.
            MiniRing.Voxels.EyePose? eyes = fighter.Build.FourLegged
                ? null
                : new MiniRing.Voxels.EyePose(fighter.Eyes.Closure, MiniRing.Voxels.EyeGaze.TargetOf(fighter, other));
            var partsStart = Aigis.Foundation.Voxels.StepProfile.Begin();
            foreach (var part in body.Layers)
            {
                if (game.HidePlayerHead && ReferenceEquals(fighter, game.Player) && part.Kind == BodyPartKind.Head)
                {
                    continue; // 一人称: 自分の頭は目の描画に入れない（カメラが頭の中にある）
                }

                var draw = part.Draw(fighter.Cyanosis, eyes, fighter.Pallor); // 血の酸素が減ると青紫に、心臓が送りきれないと青白く（見た目だけ）。顔の層も同じ
                voxels.Add(ReferenceEquals(fighter, game.Player) ? draw with { Opacity = game.PlayerBodyOpacity } : draw); // 一人称: 自分の体は半透明
            }

            Aigis.Foundation.Voxels.StepProfile.End(Aigis.Foundation.Voxels.StepProfile.Slot.RenderParts, partsStart);
            var fragmentsStart = Aigis.Foundation.Voxels.StepProfile.Begin();
            foreach (var fragment in body.Fragments)
            {
                voxels.Add(fragment.Draw());
            }

            Aigis.Foundation.Voxels.StepProfile.End(Aigis.Foundation.Voxels.StepProfile.Slot.RenderFragments, fragmentsStart);
            var bridgesStart = Aigis.Foundation.Voxels.StepProfile.Begin();

            // 関節のつなぎ（関節を曲げて開いた隙間を埋める肉・骨）: 関節がまだつながっている間だけ、今の両側の物質から作って描く（見た目だけ）。
            // 両側が削れればつなぎも細り、関節が外れれば描かない: 見えている首がつながっていれば、首はつながっている。
            foreach (var bridge in body.Bridges)
            {
                var (parent, child) = (bridge.Parent.Body.BodyId, bridge.Child.Body.BodyId);
                if (fighter.JointIntact(parent, child) && body.JointHeld(parent, child)
                    && !(game.HidePlayerHead && ReferenceEquals(fighter, game.Player) && bridge.Child.Kind == BodyPartKind.Head))
                {
                    voxels.Add(bridge.View().Draw());
                }
            }

            Aigis.Foundation.Voxels.StepProfile.End(Aigis.Foundation.Voxels.StepProfile.Slot.RenderBridges, bridgesStart);
        }

        foreach (var weapon in game.Weapons)
        {
            weapon.AddDraws(voxels, game.WielderOf(weapon)?.Voxels?.SkinColor); // 素手の拳は持ち主の肌の色で
        }

        foreach (var prop in game.BreakProps)
        {
            voxels.Add(prop.Draw()); // 休みの小物（ボトル・タオル・スマホ。持っている手の前腕に付いて描く）
        }

        DisplaySpace.Standard.Voxels(voxels); // 物理の空間 → 表示の空間
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
            if (fighter.FistCovered(hand))
            {
                continue; // ボクセルのグローブ（武器と同じに描く。腕が折れた後もぶら下がる）か、手が無い
            }

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
