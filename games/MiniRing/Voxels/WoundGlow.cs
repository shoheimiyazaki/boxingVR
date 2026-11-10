using System.Numerics;
using Aigis.Physics.Voxels;
using Aigis.Render;
using Aigis.Render.Voxels;

namespace MiniRing.Voxels;

/// <summary>
/// 削られた体の断面（えぐれた穴の内側の面）を、削れた直後だけ光らせる（描画のみ）。飛び散った欠片と同じく、熱い金属が
/// 冷めるように白 → 黄 → 赤 → 元の色と変わる（<see cref="VoxelDebris.HeatColor"/>）が、体に残る分、欠片より長く
/// <see cref="HeatSeconds"/> 秒かけて冷める。
/// 断面の 1 面ごとに、面を覆う薄い光る板を、その部位の体に貼り付けて描く（体が動けば一緒に動く）。板は HDR で明るく描き
/// ブルームでにじませ、さらに熱いうちは面の外側に加算の光の玉（<see cref="WriteHalos"/>）を置いて、穴のまわりまで照らす。
/// 後の打撃でそのボクセルが削れたら、その面の光も消える。
/// </summary>
public sealed class WoundGlow
{
    /// <summary>断面が光っている時間 [s]。</summary>
    public const float HeatSeconds = 1.5f;

    /// <summary>同時に光らせる面の上限（超えたら古いものから消す）。</summary>
    public const int Capacity = 4000;

    private const float PlateThickness = 0.12f;
    private const float PlateWidth = 1f;

    /// <summary>熱いうちの板の明るさ（ブルームでにじむくらい強く）。</summary>
    private const float HotBrightness = 2.4f;

    /// <summary>冷めきる間際の板の明るさ（体の色に馴染んで消える）。</summary>
    private const float CooledBrightness = 0.9f;

    /// <summary>光の玉の大きさ（ボクセルの辺の何倍か）と、熱いうちの濃さ。</summary>
    private const float HaloCells = 3.5f;
    private const float HaloAlpha = 0.3f;

    private readonly List<Wound> _wounds = [];
    private readonly Dictionary<VoxelPart, Matrix4x4> _frames = new(ReferenceEqualityComparer.Instance);

    /// <summary>今光っている面の数。</summary>
    public int Count => _wounds.Count;

    /// <summary><paramref name="part"/> に削れてできた断面を光らせ始める。</summary>
    public void Add(VoxelPart part, IEnumerable<VoxelCutFace> faces)
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(faces);
        foreach (var face in faces)
        {
            _wounds.Add(new Wound(part, face, 0f));
        }

        if (_wounds.Count > Capacity)
        {
            _wounds.RemoveRange(0, _wounds.Count - Capacity); // 古い順に並んでいる
        }
    }

    /// <summary>時間を進め、冷めたものを捨てる。</summary>
    public void Step(float dt)
    {
        for (var i = 0; i < _wounds.Count; i++)
        {
            _wounds[i] = _wounds[i] with { Age = _wounds[i].Age + dt };
        }

        _wounds.RemoveAll(w => w.Age >= HeatSeconds);
    }

    public void Clear() => _wounds.Clear();

    /// <summary>光っている断面の板を足す（自ら光る描き方）。</summary>
    public void WriteInstances(ICollection<PrimitiveInstance> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        _frames.Clear();
        foreach (var wound in _wounds)
        {
            if (!TryPlace(wound, out var face))
            {
                continue;
            }

            var plate = new Vector3(face.Axis == 0 ? PlateThickness : PlateWidth, face.Axis == 1 ? PlateThickness : PlateWidth, face.Axis == 2 ? PlateThickness : PlateWidth) * face.Cell;
            var cooling = wound.Age / HeatSeconds;
            var gain = HotBrightness + ((CooledBrightness - HotBrightness) * SmoothStep(0.5f, 1f, cooling));
            var glow = (VoxelDebris.HeatColor(Vector4.One, ShownColor(wound.Part, wound.Face), cooling) * gain) with { W = 1f };
            into.Add(new PrimitiveInstance(PrimitiveKind.Cube, Matrix4x4.CreateScale(plate) * Matrix4x4.CreateTranslation(face.Center) * face.Frame, Vector3.Zero, glow, MaterialKind.Emissive));
        }
    }

    /// <summary>
    /// 熱いうちの断面の外側に、加算の光の玉を足す。白・黄・赤と冷めるにつれて薄れ、元の色に戻る頃には消える。
    /// </summary>
    public void WriteHalos(ICollection<ParticleInstance> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        _frames.Clear();
        foreach (var wound in _wounds)
        {
            if (!TryPlace(wound, out var face))
            {
                continue;
            }

            var cooling = wound.Age / HeatSeconds;
            var heat = 1f - SmoothStep(0f, 0.75f, cooling);
            if (heat <= 0f)
            {
                continue;
            }

            var edge = MathF.Max(face.Cell.X, MathF.Max(face.Cell.Y, face.Cell.Z));
            var outward = face.Center + (face.Normal * (0.6f * edge));
            var color = VoxelDebris.HeatColor(Vector4.One, Vector4.Zero, cooling);
            var peak = MathF.Max(color.X, MathF.Max(color.Y, color.Z));
            var hue = peak > 0f ? color / peak : Vector4.One;
            into.Add(new ParticleInstance(Vector3.Transform(outward, face.Frame), HaloCells * edge, hue with { W = HaloAlpha * heat }, Blend: ParticleBlend.Additive));
        }
    }

    /// <summary>その断面の面が、体の格子のどこにどちら向きにあるか。削れてもう無ければ false。</summary>
    private bool TryPlace(Wound wound, out PlacedFace placed)
    {
        var (part, face, _) = wound;
        var grid = part.Body.Grid;
        if (grid.At(face.X, face.Y, face.Z) == 0)
        {
            placed = default;
            return false; // 後の打撃で削れた
        }

        if (!_frames.TryGetValue(part, out var frame))
        {
            frame = part.Body.GridToWorld();
            _frames[part] = frame;
        }

        var cell = grid.CellSize;
        var axis = face.Face / 2;
        var sign = face.Face % 2 == 0 ? 1f : -1f;
        var normal = axis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ } * sign;
        var center = grid.CellCenter(face.X, face.Y, face.Z) + (normal * (0.5f * Vector3.Dot(cell, normal * sign)));
        placed = new PlacedFace(frame, center, normal, cell, axis);
        return true;
    }

    private static float SmoothStep(float a, float b, float x)
    {
        var u = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return u * u * (3f - (2f * u));
    }

    /// <summary>そのボクセルが体の上で見えている色（血の染みも込み）。冷めきったときの色。</summary>
    private static Vector4 ShownColor(VoxelPart part, VoxelCutFace face)
    {
        var volume = part.Volume;
        var t = 4 * (face.X + (volume.Width * (face.Y + (volume.Height * face.Z))));
        var rgba = volume.Rgba;
        return t + 2 < rgba.Length ? new Vector4(rgba[t] / 255f, rgba[t + 1] / 255f, rgba[t + 2] / 255f, 1f) : Vector4.One;
    }

    private readonly record struct Wound(VoxelPart Part, VoxelCutFace Face, float Age);

    /// <summary>体の格子の中の面の中心と向き（<see cref="Frame"/> で世界へ）。</summary>
    private readonly record struct PlacedFace(Matrix4x4 Frame, Vector3 Center, Vector3 Normal, Vector3 Cell, int Axis);
}
