using System.Numerics;
using Aigis.Render;
using Aigis.Render.Voxels;

namespace MiniRing;

/// <summary>
/// 物理の空間（リング・体・武器・破片。試合の計算の単位 m）から、表示の空間（部屋・テーブル・カメラ・VR のプレイヤーの立つ所。実物の m）への変換。
/// 原点を中心にした一様な縮尺 <see cref="Scale"/> だけ（回転・平行移動なし）: 表示の点 = 物理の点 × <see cref="Scale"/>。
/// <para><b>なぜ分けるか</b>（docs/plans/REAL_SCALE_PLAN.md 段 1）: 物理は実物大の人で計算し、表示（VR・デスクトップ・動画）はテーブルの上の
/// 60 cm の人形の大きさのまま見せる（実物大のホログラムを縮めて見せる）。部屋は表示の空間に属する（壁・床・テーブル・窓・照明・カメラの壁の内側）。
/// 描画・カメラ・HUD・VR の一人称は、物理の位置をこの変換に 1 回だけ通す。</para>
/// <para><b>試合の計算には入らない</b>（描画だけ）。例外は部屋の当たり判定（<see cref="LivingRoom.AddColliders"/>）: 今は表示の部屋を
/// 1/<see cref="Scale"/> 倍した「大きな部屋」を物理に置いている（段 3 で物理から外す。docs/DEBT.md）。だから <see cref="WorldScale"/> は定数で、変えると
/// 物理（PhysicsVersion）に響く。</para>
/// </summary>
/// <param name="Scale">物理の 1 m が表示で何 m か。</param>
public readonly record struct DisplaySpace(float Scale)
{
    /// <summary>
    /// 物理の 1 m が表示で何 m か（試合のすべてのホストの値）。1 = 物理と表示が同じ大きさ。
    /// </summary>
    public const float WorldScale = 1f;

    /// <summary>試合のホスト（デスクトップ・VR・動画・テスト）の変換。</summary>
    public static DisplaySpace Standard { get; } = new(WorldScale);

    /// <summary>変換が何もしないか（縮尺 1）。描画の結果をビット単位で今までと同じに保つための近道にも使う。</summary>
    public bool IsIdentity => Scale == 1f;

    /// <summary>物理の点 → 表示の点。</summary>
    public Vector3 Point(Vector3 physics) => IsIdentity ? physics : physics * Scale;

    /// <summary>物理の長さ → 表示の長さ。</summary>
    public float Length(float physics) => IsIdentity ? physics : physics * Scale;

    /// <summary>表示の点 → 物理の点。</summary>
    public Vector3 ToPhysics(Vector3 display) => IsIdentity ? display : display / Scale;

    /// <summary>表示の長さ → 物理の長さ。</summary>
    public float ToPhysics(float display) => IsIdentity ? display : display / Scale;

    /// <summary>物理の空間 → 表示の空間の行列（行ベクトル: 物理の行列の右から掛ける）。</summary>
    public Matrix4x4 Matrix => Matrix4x4.CreateScale(Scale);

    /// <summary>
    /// 物理の空間で作った描画の箱・球・メッシュを表示の空間へ。箱・円柱・メッシュは行列の大きさで、球・カプセルは <see cref="PrimitiveInstance.Dimensions"/> の半径で大きさを持つので両方を縮める
    /// （<see cref="PrimitiveInstance"/> の説明: 大きさの置き場所が種類で違う）。テクスチャの繰り返しの長さ [m] も同じ倍率。
    /// </summary>
    public PrimitiveInstance Instance(PrimitiveInstance instance)
    {
        if (IsIdentity)
        {
            return instance;
        }

        // 球・カプセルの行列は大きさ 1 のまま（半径はシェーダーが Dimensions から掛ける）: 位置だけ移して、半径を縮める。行列ごと縮めると 2 回縮む。
        var radial = instance.Kind is PrimitiveKind.Sphere or PrimitiveKind.Capsule;
        var model = instance.Model;
        if (radial)
        {
            model.Translation *= Scale;
        }
        else
        {
            model *= Matrix;
        }

        return instance with
        {
            Model = model,
            Dimensions = radial ? instance.Dimensions * Scale : instance.Dimensions,
            TextureTileMetres = instance.TextureTileMetres > 0f ? instance.TextureTileMetres * Scale : instance.TextureTileMetres,
        };
    }

    /// <summary>リストの [<paramref name="start"/>, <paramref name="end"/>) を物理の空間から表示の空間へ置き換える。</summary>
    public void Instances(List<PrimitiveInstance> instances, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(instances);
        if (IsIdentity)
        {
            return;
        }

        for (var i = start; i < end; i++)
        {
            instances[i] = Instance(instances[i]);
        }
    }

    /// <summary>物理の空間の線 → 表示。</summary>
    public DebugLine Line(DebugLine line) => IsIdentity ? line : line with { A = line.A * Scale, B = line.B * Scale };

    /// <summary>リストの線をすべて物理の空間から表示の空間へ置き換える。</summary>
    public void Lines(List<DebugLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (IsIdentity)
        {
            return;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            lines[i] = Line(lines[i]);
        }
    }

    /// <summary>物理の空間の粒（位置と大きさ）→ 表示。</summary>
    public ParticleInstance Particle(ParticleInstance particle)
        => IsIdentity ? particle : particle with { Position = particle.Position * Scale, Size = particle.Size * Scale };

    /// <summary>リストの [<paramref name="start"/>, 末尾) の粒を物理の空間から表示の空間へ置き換える。</summary>
    public void Particles(List<ParticleInstance> particles, int start)
    {
        ArgumentNullException.ThrowIfNull(particles);
        if (IsIdentity)
        {
            return;
        }

        for (var i = start; i < particles.Count; i++)
        {
            particles[i] = Particle(particles[i]);
        }
    }

    /// <summary>物理の空間のボクセルの格子（セル → 物理の行列）→ 表示。</summary>
    public VoxelVolumeDraw Voxel(VoxelVolumeDraw draw) => IsIdentity ? draw : draw with { CellToWorld = draw.CellToWorld * Matrix };

    /// <summary>リストのボクセルの格子をすべて物理の空間から表示の空間へ置き換える。</summary>
    public void Voxels(List<VoxelVolumeDraw> voxels)
    {
        ArgumentNullException.ThrowIfNull(voxels);
        if (IsIdentity)
        {
            return;
        }

        for (var i = 0; i < voxels.Count; i++)
        {
            voxels[i] = Voxel(voxels[i]);
        }
    }

    /// <summary>
    /// 物理の空間で決めたカメラ（目・注視点）を表示の空間へ移して、描画の行列を作る。<paramref name="keepInsideMargin"/> があれば、目を表示の部屋の
    /// 壁・天井の内側へ寄せる（<see cref="LivingRoom.KeepInside"/>。部屋は表示の空間にある）。近い面・遠い面 [m] は表示の空間の長さ。
    /// 返す目の位置（表示の空間）は描画の照り返しの計算に渡す。
    /// </summary>
    public (Matrix4x4 ViewProjection, Vector3 Eye) Camera(Vector3 eye, Vector3 target, Vector3 up, float fieldOfView, float aspect, float near, float far,
        float? keepInsideMargin = null)
    {
        var displayEye = Point(eye);
        if (keepInsideMargin is { } margin)
        {
            displayEye = LivingRoom.KeepInside(displayEye, margin);
        }

        return (Aigis.Render.Camera.ViewProjection(displayEye, Point(target), up, fieldOfView, aspect, near, far), displayEye);
    }
}
