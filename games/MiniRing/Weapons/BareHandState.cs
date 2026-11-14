using System.Numerics;
using Aigis.Foundation.Voxels;

namespace MiniRing.Weapons;

/// <summary>各組織セルを親として保持する。接触形を変えても損傷・体積は変わらない。</summary>
public sealed class BareHandState
{
    private static readonly VoxelGrid Closed = Weapon.Build(WeaponKind.Fist);
    private static readonly int[] Source = Enumerable.Range(0, Closed.CellCount).Where(i => Closed.MaterialAt(i) != 0).ToArray();
    private static readonly Lazy<int[][]> Maps = new(CreateMaps);
    private readonly (byte Material, float Health, bool Surface, float Stain)[] _cells;
    public BareHandState() : this(Source.Select(Closed.CellState).ToArray(), HandShape.Seiken) { }
    private BareHandState((byte Material, float Health, bool Surface, float Stain)[] cells, HandShape shape)
    {
        _cells = cells;
        Shape = shape;
    }

    public HandShape Shape { get; }
    public IReadOnlyList<(byte Material, float Health, bool Surface, float Stain)> Cells => Array.AsReadOnly(_cells);

    /// <summary>指の本数（人さし指・中指・薬指・小指）。握りの寸法は <see cref="Weapon"/> の正拳（FingerSpacing 6 mm）に合わせる。</summary>
    public const int FingerCount = 4;

    /// <summary>
    /// 元のセルがどの指のどの節か（<see cref="FingerCount"/> = 掌・中手骨）。指の骨・皮膚のセルは正拳の生成時の位置で決める
    /// （指の帯 z と、前へ出た拳頭か畳んだ指かの x）。どの手形に変えても同じセルは同じ指・同じ節の部品として数える。
    /// </summary>
    private static readonly (int Finger, FingerSegment Segment)[] Assignment = Source.Select(index =>
    {
        var c = Closed.Coordinates(index);
        var p = Closed.CellCenter(c.X, c.Y, c.Z);
        if (p.X < 0.002f)
        {
            return (FingerCount, FingerSegment.Palm);
        }

        var finger = Math.Clamp((int)MathF.Round((p.Z / Weapon.FingerPitch) + 1.5f), 0, FingerCount - 1);
        var front = finger < 2 ? Weapon.KnuckleFront : Weapon.FoldedKnuckleFront;
        return (finger, p.X >= front - Weapon.KnuckleRadiusOf ? FingerSegment.Knuckle : FingerSegment.Folded);
    }).ToArray();

    /// <summary>
    /// 指ごと・節ごとの親データ（残っている骨・皮膚の割合と、ひび）。手形に依らず元のセルから数えるので、ある形で打って削れた節は
    /// ほかのどの形でも同じ量だけ欠けて見え、効く（折れた指で貫手を打てば、その指の骨が足りない）。ひび = 1 − 骨の残りで、
    /// 骨が削られた量がそのまま折れやすさの目安になる。
    /// </summary>
    public FingerPartState Part(int finger, FingerSegment segment)
    {
        if (finger is < 0 or > FingerCount || (finger == FingerCount) != (segment == FingerSegment.Palm))
        {
            throw new ArgumentOutOfRangeException(nameof(finger));
        }

        float bone = 0f, boneFull = 0f, skin = 0f, skinFull = 0f;
        var cells = 0;
        for (var i = 0; i < _cells.Length; i++)
        {
            if (Assignment[i].Finger != finger || Assignment[i].Segment != segment)
            {
                continue;
            }

            var original = Closed.MaterialAt(Source[i]);
            var full = Closed.Palette[original].Toughness;
            var left = _cells[i].Material == 0 ? 0f : Math.Clamp(_cells[i].Health, 0f, full);
            if (original == Voxels.BoxerAnatomy.Bone)
            {
                bone += left;
                boneFull += full;
            }
            else if (original == Voxels.BoxerAnatomy.Skin)
            {
                skin += left;
                skinFull += full;
            }

            cells++;
        }

        var boneLeft = boneFull > 0f ? bone / boneFull : 1f;
        return new FingerPartState(cells, boneLeft, skinFull > 0f ? skin / skinFull : 1f, 1f - boneLeft);
    }

    /// <summary>その指の骨の残り（0..1、節ごとの最小。拳頭か畳んだ節のどちらかが削られれば、その指の骨は弱い）。</summary>
    public float FingerBone(int finger) => MathF.Min(Part(finger, FingerSegment.Knuckle).Bone, Part(finger, FingerSegment.Folded).Bone);
    public BareHandState Capture(VoxelGrid grid) => new(Maps.Value[(int)Shape].Select(grid.CellState).ToArray(), Shape);
    public BareHandState WithShape(HandShape shape)
    {
        Validate(shape);
        return new([.. _cells], shape);
    }

    public VoxelGrid Build(HandShape shape)
    {
        Validate(shape);
        var map = Maps.Value[(int)shape];
        var lookup = map.Select((index, cell) => (index, cell)).ToDictionary(p => p.index, p => p.cell);
        var grid = Empty((_, index) => lookup.TryGetValue(index, out var cell) ? SourceMaterial(cell) : (byte)0);
        for (var i = 0; i < map.Length; i++)
        {
            grid.SetCellState(map[i], _cells[i]);
        }

        return grid;
    }

    private static byte SourceMaterial(int cell) => Closed.MaterialAt(Source[cell]);
    private static VoxelGrid Empty(Func<Vector3, int, byte> fill)
    {
        var index = 0;
        return new(Closed.Palette, Closed.HalfExtents + new Vector3(Closed.CellSize.X * Closed.Dimensions.X, 0f, 0f),
            Closed.CellSize.X, (p, _) => fill(p, index++));
    }

    private static int[][] CreateMaps()
    {
        var grid = Empty((_, _) => 0);
        return Enum.GetValues<HandShape>().Select(shape =>
        {
            var used = new HashSet<int>();
            return Source.Select(index =>
            {
                var c = Closed.Coordinates(index);
                var p = Closed.CellCenter(c.X, c.Y, c.Z);
                var target = shape switch
                {
                    HandShape.SpearHand => p with { X = p.X * 1.8f + 0.007f },
                    HandShape.KnifeHand => p with { X = p.X * 1.5f + 0.004f },
                    HandShape.OneKnuckle => p with { X = p.X + (p.Z < 0f && p.Z > -0.007f ? 0.009f : 0f) },
                    HandShape.PalmHeel => p with { X = -p.X },
                    HandShape.BackFist => p with { X = p.Y, Y = -p.X },
                    _ => p,
                };
                // A stable nearest vacant cell conserves every tissue cell at all shapes.
                var selected = Enumerable.Range(0, grid.CellCount).Where(i => !used.Contains(i))
                    .MinBy(i => { var q = grid.Coordinates(i); return Vector3.DistanceSquared(target, grid.CellCenter(q.X, q.Y, q.Z)); });
                used.Add(selected);
                return selected;
            }).ToArray();
        }).ToArray();
    }

    private static void Validate(HandShape shape)
    {
        if (!Enum.IsDefined(shape))
        {
            throw new ArgumentOutOfRangeException(nameof(shape));
        }
    }
}

/// <summary>指・掌の節。</summary>
public enum FingerSegment { Knuckle, Folded, Palm }

/// <summary>1 つの節の親データ: セル数・骨の残り・皮膚の残り（0..1）・ひび（1 − 骨の残り）。</summary>
public readonly record struct FingerPartState(int Cells, float Bone, float Skin, float Cracked);
