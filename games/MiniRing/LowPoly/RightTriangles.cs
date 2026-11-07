using System.Numerics;
using Aigis.Render;

namespace MiniRing.LowPoly;

/// <summary>
/// 任意の三角形を「単位直角三角形メッシュ 1 つ」のインスタンスとして描くための分解。
/// <para>
/// Aigis の <see cref="MeshData"/> は一度 GPU に上げたら書き換えられず、色はインスタンスごとに 1 色しか持てない。
/// また <see cref="PrimitiveInstance.Model"/> は「拡大 → 回転 → 平行移動」に分解できる行列でなければならない（せん断は不可）。
/// そこで三角形を最長辺への垂線で直角三角形 2 枚に割る: 直角三角形は単位直角三角形 (0,0,0)(1,0,0)(0,1,0) を
/// 2 本の脚の長さで非一様に拡大して回転すれば、せん断なしでぴったり重なる。これで頂点を毎フレーム動かし、
/// 面ごとに色を変えても、エンジン（シェーダ・頂点形式）には手を入れずに済む。
/// </para>
/// </summary>
public static class RightTriangles
{
    /// <summary>これより短い脚の直角三角形は面積が無いので描かない [m]。</summary>
    private const float MinimumLeg = 1e-5f;

    /// <summary>単位直角三角形（法線 +Z）。全員で 1 つを使い回す（GPU へは 1 回だけ上がる）。</summary>
    public static MeshData UnitMesh { get; } = new(
    [
        0f, 0f, 0f, 0f, 0f, 1f,
        1f, 0f, 0f, 0f, 0f, 1f,
        0f, 1f, 0f, 0f, 0f, 1f,
    ], "unit-right-triangle");

    /// <summary>
    /// 三角形 (a, b, c)（反時計回りが表）を覆う 1〜2 個のモデル行列を <paramref name="output"/> に足す。
    /// 各行列は行が (脚 u, 脚 v, 法線, 直角の頂点) で、u ⊥ v なので「拡大 → 回転 → 平行移動」にそのまま分解できる。
    /// </summary>
    public static void Decompose(Vector3 a, Vector3 b, Vector3 c, List<Matrix4x4> output)
    {
        ArgumentNullException.ThrowIfNull(output);

        // 最長辺 QR と、その向かいの頂点 P（巻き順 P → Q → R を元の a → b → c と同じ向きに保つ）。
        var (p, q, r) = (b - c).LengthSquared() >= (c - a).LengthSquared() && (b - c).LengthSquared() >= (a - b).LengthSquared() ? (a, b, c)
            : (c - a).LengthSquared() >= (a - b).LengthSquared() ? (b, c, a)
            : (c, a, b);
        var edge = r - q;
        var lengthSquared = edge.LengthSquared();
        if (lengthSquared < MinimumLeg * MinimumLeg)
        {
            return;
        }

        // 最長辺に下ろした垂線の足 H は必ず辺の上にある（他の 2 角は鋭角）。
        var foot = q + (edge * Math.Clamp(Vector3.Dot(p - q, edge) / lengthSquared, 0f, 1f));
        Add(foot, p, q, output); // (H, P, Q) は (P, Q, R) と同じ向き
        Add(foot, r, p, output); // (H, R, P) も同じ
    }

    private static void Add(Vector3 corner, Vector3 alongU, Vector3 alongV, List<Matrix4x4> output)
    {
        var u = alongU - corner;
        var v = alongV - corner;
        if (u.LengthSquared() < MinimumLeg * MinimumLeg || v.LengthSquared() < MinimumLeg * MinimumLeg)
        {
            return;
        }

        var normal = Vector3.Normalize(Vector3.Cross(u, v));
        output.Add(new Matrix4x4(
            u.X, u.Y, u.Z, 0f,
            v.X, v.Y, v.Z, 0f,
            normal.X, normal.Y, normal.Z, 0f,
            corner.X, corner.Y, corner.Z, 1f));
    }
}
