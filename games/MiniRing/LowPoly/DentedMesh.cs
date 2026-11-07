using System.Collections.Immutable;
using System.Numerics;

namespace MiniRing.LowPoly;

/// <summary>
/// 打たれてへこんだメッシュ（不変）。ノードごとの変位（部位ローカル [m]）を持ち、
/// 打撃 1 発ごとに <see cref="Impact"/> が新しいインスタンスを返す。
/// 色はノードの変位量で白 → 赤（<see cref="MaxOffset"/> で真っ赤）。
/// </summary>
public sealed class DentedMesh
{
    public static readonly Vector4 Undamaged = new(1f, 1f, 1f, 1f);
    public static readonly Vector4 Damaged = new(0.85f, 0.04f, 0.04f, 1f);

    private readonly ImmutableArray<Vector3> _offsets;
    private (Matrix4x4[] Locals, Vector4[] Colors)? _triangles;

    public DentedMesh(LowPolyMesh rest, float maxOffset)
        : this(rest, maxOffset, ImmutableArray.Create(new Vector3[rest?.Vertices.Length ?? 0]))
    {
    }

    private DentedMesh(LowPolyMesh rest, float maxOffset, ImmutableArray<Vector3> offsets)
    {
        ArgumentNullException.ThrowIfNull(rest);
        if (maxOffset <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(maxOffset), maxOffset, "変位の上限は正の値にしてください。");
        }

        Rest = rest;
        MaxOffset = maxOffset;
        _offsets = offsets;
        IsDented = offsets.Any(o => o != Vector3.Zero);
    }

    /// <summary>変形前の形（部位ローカル [m]）。</summary>
    public LowPolyMesh Rest { get; }

    /// <summary>1 ノードが元の位置から動ける最大距離 [m]（へこみすぎて裏返らないように）。</summary>
    public float MaxOffset { get; }

    /// <summary>どこか 1 ノードでも動いているか。動いていなければ静的メッシュ 1 個で描ける。</summary>
    public bool IsDented { get; }

    public int NodeCount => _offsets.Length;

    public Vector3 Offset(int node) => _offsets[node];

    public Vector3 Position(int node) => Rest.Vertices[node] + _offsets[node];

    /// <summary>ノードの変形量 0..1（<see cref="MaxOffset"/> に対する割合）。</summary>
    public float Strain(int node) => Math.Clamp(_offsets[node].Length() / MaxOffset, 0f, 1f);

    public static Vector4 StrainColor(float strain) => Vector4.Lerp(Undamaged, Damaged, Math.Clamp(strain, 0f, 1f));

    /// <summary>
    /// 拳の当たり 1 発。<paramref name="fistCenter"/> から半径 <paramref name="fistRadius"/> の拳の表面に近いノードほど大きく、
    /// <paramref name="direction"/>（力を受けた向き）へ <paramref name="depth"/> [m] まで動かす。
    /// 拳の表面から <paramref name="spread"/> [m] 以上離れたノードは動かない。すべて部位ローカル。
    /// </summary>
    public DentedMesh Impact(Vector3 fistCenter, float fistRadius, Vector3 direction, float depth, float spread)
    {
        if (depth <= 0f || spread <= 0f || direction.LengthSquared() < 1e-12f)
        {
            return this;
        }

        var push = Vector3.Normalize(direction) * depth;
        var offsets = _offsets.ToBuilder();
        var moved = false;
        for (var i = 0; i < offsets.Count; i++)
        {
            var gap = MathF.Max(0f, Vector3.Distance(Position(i), fistCenter) - fistRadius) / spread;
            if (gap >= 1f)
            {
                continue;
            }

            // 拳の面で 1、spread で 0 になる滑らかな山（(1 − x²)²）。
            var weight = (1f - (gap * gap)) * (1f - (gap * gap));
            var offset = offsets[i] + (push * weight);
            var length = offset.Length();
            offsets[i] = length > MaxOffset ? offset * (MaxOffset / length) : offset;
            moved = true;
        }

        return moved ? new DentedMesh(Rest, MaxOffset, offsets.MoveToImmutable()) : this;
    }

    /// <summary>
    /// 描画用: 変形後の各三角形を覆う直角三角形のローカル行列と、その色（三角形の 3 ノードの変形量の平均）。
    /// 不変なので初回に 1 度だけ計算する。
    /// </summary>
    public (IReadOnlyList<Matrix4x4> Locals, IReadOnlyList<Vector4> Colors) Triangles()
    {
        _triangles ??= BuildTriangles();
        return _triangles.Value;
    }

    private (Matrix4x4[] Locals, Vector4[] Colors) BuildTriangles()
    {
        var locals = new List<Matrix4x4>(Rest.TriangleCount * 2);
        var colors = new List<Vector4>(Rest.TriangleCount * 2);
        for (var t = 0; t < Rest.TriangleCount; t++)
        {
            var (a, b, c) = Rest.Triangle(t);
            var before = locals.Count;
            RightTriangles.Decompose(Position(a), Position(b), Position(c), locals);
            var color = StrainColor((Strain(a) + Strain(b) + Strain(c)) / 3f);
            for (var k = before; k < locals.Count; k++)
            {
                colors.Add(color);
            }
        }

        return ([.. locals], [.. colors]);
    }
}
