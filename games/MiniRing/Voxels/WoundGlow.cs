using System.Numerics;
using Aigis.Physics.Voxels;
using Aigis.Render;
using Aigis.Render.Voxels;

namespace MiniRing.Voxels;

/// <summary>
/// 削られた体の断面（えぐれた穴の内側の面）を、削れた直後だけ光らせる（描画のみ）。飛び散った欠片と同じく、熱い金属が
/// 冷めるように白 → 黄 → 赤 → 元の色と <see cref="HeatSeconds"/> 秒で変わる（<see cref="VoxelDebris.HeatColor"/>）。
/// 断面の 1 面ごとに、面より少し小さい薄い光る板を、その部位の体に貼り付けて描く（体が動けば一緒に動く）。
/// 後の打撃でそのボクセルが削れたら、その面の光も消える。
/// </summary>
public sealed class WoundGlow
{
    /// <summary>断面が光っている時間 [s]（飛び散る欠片と同じ）。</summary>
    public const float HeatSeconds = 0.5f;

    /// <summary>同時に光らせる面の上限（超えたら古いものから消す）。</summary>
    public const int Capacity = 4000;

    private const float PlateThickness = 0.12f;
    private const float PlateWidth = 0.8f;

    /// <summary>欠片より少し暗く（体の上で光りすぎない）。</summary>
    private const float Brightness = 0.8f;

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
        foreach (var (part, face, age) in _wounds)
        {
            var grid = part.Body.Grid;
            if (grid.At(face.X, face.Y, face.Z) == 0)
            {
                continue; // 後の打撃で削れた
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
            var plate = new Vector3(axis == 0 ? PlateThickness : PlateWidth, axis == 1 ? PlateThickness : PlateWidth, axis == 2 ? PlateThickness : PlateWidth) * cell;
            var center = grid.CellCenter(face.X, face.Y, face.Z) + (normal * (0.5f * Vector3.Dot(cell, normal * sign)));
            var glow = (VoxelDebris.HeatColor(Vector4.One, ShownColor(part, face), age / HeatSeconds) * Brightness) with { W = 1f };
            into.Add(new PrimitiveInstance(PrimitiveKind.Cube, Matrix4x4.CreateScale(plate) * Matrix4x4.CreateTranslation(center) * frame, Vector3.Zero, glow, MaterialKind.Emissive));
        }
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
}
