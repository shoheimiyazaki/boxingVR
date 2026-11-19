using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics;
using MiniRing.LowPoly;
using MiniRing.Voxels;

namespace MiniRing.Characters;

/// <summary>
/// 肖像の 1 枚（四角の並び = ドット絵）。試合の体のボクセルを正面（+Z 側）から見て、いちばん手前のセルの材質を写したもの。
/// 絵を描き起こさない（見えている体 = 試合の体、CLAUDE.md §1.7）。明るさは、手前のセルの奥行きの段差（隣との差）から出した
/// 光の当たり方（左上から）だけで、顔の起伏（鼻・あご・頬骨）が見える。色は材質の表（<see cref="Palette"/>）から引く。
/// </summary>
public sealed class PortraitImage(int width, int height, byte[] materials, float[] shade, VoxelPalette palette, float pixelMeters)
{
    public int Width { get; } = width;
    public int Height { get; } = height;

    /// <summary>1 ピクセルの実寸 [m]。</summary>
    public float PixelMeters { get; } = pixelMeters;

    /// <summary>ピクセルごとの材質（0 = 何も無い）。行は上から。</summary>
    public IReadOnlyList<byte> Materials { get; } = materials;

    /// <summary>ピクセルごとの明るさの倍率。</summary>
    public IReadOnlyList<float> Shade { get; } = shade;

    public VoxelPalette Palette { get; } = palette;

    /// <summary>描いたピクセルの数。</summary>
    public int FilledCount => materials.Count(m => m != 0);

    public byte MaterialAt(int x, int y) => materials[x + (y * Width)];

    /// <summary>
    /// ピクセルの色（何も無ければ透明）。<paramref name="blueCorner"/> が true なら、赤コーナーのトランクスを青コーナーの色へ替える
    /// （試合では 2 人のトランクスの色がコーナーで決まる）。
    /// </summary>
    public Vector4 ColorAt(int x, int y, bool blueCorner = false)
    {
        var material = MaterialAt(x, y);
        if (material == 0)
        {
            return Vector4.Zero;
        }

        if (blueCorner && material == BoxerAnatomy.RedTrunks)
        {
            material = BoxerAnatomy.BlueTrunks;
        }

        var color = Palette[material].SurfaceColor;
        var s = shade[x + (y * Width)];
        return new Vector4(Math.Clamp(color.X * s, 0f, 1f), Math.Clamp(color.Y * s, 0f, 1f), Math.Clamp(color.Z * s, 0f, 1f), 1f);
    }
}

/// <summary>1 人の肖像: 顔（頭だけ・セルの大きさ）と全身（体格の寸法のまま）。</summary>
/// <param name="Face">頭の正面。1 ピクセル = ボクセル 1 つ。</param>
/// <param name="Body">全身の正面（腕は体の脇へ下ろす）。1 ピクセルの実寸は <see cref="PortraitImage.PixelMeters"/>。</param>
/// <param name="HeightMeters">全身の高さ [m]（頭の上から足の裏まで）。</param>
/// <param name="WidthMeters">全身の幅 [m]（腕を下ろした肩幅）。</param>
public sealed record CharacterPortrait(PortraitImage Face, PortraitImage Body, float HeightMeters, float WidthMeters);

/// <summary>
/// 名簿のキャラクターの肖像を、試合で使う体のボクセルから作る。部位の格子は試合の体と同じ関数（<see cref="BoxerBody.BuildPartGrid"/>）で作り、
/// 立った姿（<see cref="Fighter.Spawn"/> の T ポーズ）に置いて、腕だけ肩を軸に体の脇へ下ろす（どの部位も回すだけ。作り直さない）。
/// 正面から奥へ光線を通し、最初に当たったセルの材質をピクセルにする。
/// </summary>
public static class CharacterPortraits
{
    /// <summary>全身の写しの 1 ピクセルの実寸 [m]。ボクセル 1 つ（5 mm）= 1 ピクセル（顔の目・髪・ひげが見える細かさ）。</summary>
    public const float BodyPixelMeters = 0.005f;

    /// <summary>1 人の肖像を作る（背景スレッドから呼べる。物理のワールドは使い捨て）。</summary>
    public static CharacterPortrait Build(FighterCharacter character)
    {
        ArgumentNullException.ThrowIfNull(character);
        var tuning = FighterTuning.Default with { Build = character.Build };
        var standard = FighterTuning.Default with { Build = BodyBuild.Standard };
        var model = BoxerModel.Build(standard.HalfExtents, tuning.ModelSubdivisions);
        using var world = new PhysicsWorld(Vector3.Zero);
        var fighter = Fighter.Spawn(world, tuning, "portrait", Vector3.Zero, 0f);
        var parts = new List<Placed>();
        VoxelPalette? palette = null;
        var head = new List<Placed>();

        foreach (var id in fighter.AllParts)
        {
            var kind = fighter.KindOf(id);
            var half = fighter.HalfExtentsOf(id);
            var grid = BoxerBody.BuildPartGrid(kind, half, tuning, model, model, Outfit.RedCorner, BoxerVoxelOverrides.Shipped, FaceLayer.LookWith(character.Look, character.Face), ref palette);
            var state = world.GetState(id);
            var (position, orientation) = (state.Position, state.Orientation);
            if (kind is BodyPartKind.UpperArm or BodyPartKind.Forearm)
            {
                // 肩（上腕の体に近い端）を軸に、腕を体の脇へ下ろす（外向き ±X → 下向き −Y）。前腕も同じ回転（まっすぐ下ろす）。
                var upper = fighter.Parts;
                var isRight = id == upper.RightUpperArm || id == upper.RightForearm;
                var side = isRight ? 1f : -1f;
                var upperId = isRight ? upper.RightUpperArm : upper.LeftUpperArm;
                var upperState = world.GetState(upperId);
                var upperHalf = fighter.HalfExtentsOf(upperId);
                var shoulder = upperState.Position - new Vector3(side * upperHalf.X, 0f, 0f);
                var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -side * MathF.PI / 2f);
                position = shoulder + Vector3.Transform(position - shoulder, turn);
                orientation = Quaternion.Concatenate(orientation, turn);
            }

            if (id == fighter.Parts.Head)
            {
                // 頭は試合の体と同じく、頭蓋骨の中（5 mm）と顔の層（2.5 mm、キャラの顔: FaceLayer）の 2 つの格子。
                var (inner, faceLayer) = FaceLayer.Split(grid, character.Face);
                head.Add(new Placed(inner, position, orientation, half));
                head.Add(new Placed(faceLayer, position, orientation, half));
                parts.AddRange(head);
                continue;
            }

            parts.Add(new Placed(grid, position, orientation, half));
        }

        if (palette is null) { throw new InvalidOperationException("体の部位が 1 つも作られませんでした。"); }
        var body = Render(parts, palette, BodyPixelMeters, out var height, out var width);
        var face = Render(head, palette, tuning.VoxelSize / FaceLayer.Subdivision, out _, out _); // 顔は顔の層の細かさ（1 ピクセル = 約 2.5 mm）
        return new CharacterPortrait(face, body, height, width);
    }

    private readonly record struct Placed(VoxelGrid Grid, Vector3 Position, Quaternion Orientation, Vector3 Half)
    {
        public float Radius => Half.Length();
        public Quaternion Inverse => Quaternion.Conjugate(Orientation);
    }

    /// <summary>
    /// 部位を正面（+Z から −Z へ）の光線で写す。ピクセルの中心ごとに、各部位の格子の中へ奥行き方向に刻んで進み、
    /// 最初に当たった（いちばん手前の）セルを採る。空の行・列は切り取り、左右は体の中心線に対して対称に切る。
    /// </summary>
    private static PortraitImage Render(List<Placed> parts, VoxelPalette palette, float pixel, out float heightMeters, out float widthMeters)
    {
        var (min, max) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
        foreach (var p in parts)
        {
            min = Vector3.Min(min, p.Position - new Vector3(p.Radius));
            max = Vector3.Max(max, p.Position + new Vector3(p.Radius));
        }

        var axisX = parts.All(p => p.Position == parts[0].Position) ? parts[0].Position.X : 0f; // 頭だけ（重ねた格子）なら頭の中心
        var halfWidth = MathF.Max(MathF.Abs(max.X - axisX), MathF.Abs(min.X - axisX));
        var columns = (int)MathF.Ceiling(halfWidth / pixel) * 2;
        var rows = (int)MathF.Ceiling((max.Y - min.Y) / pixel);
        var materials = new byte[columns * rows];
        var depth = new float[columns * rows];
        var cell = 0.00125f; // 奥行きの刻み: 顔の層のセル（約 2.5 mm）の半分
        for (var row = 0; row < rows; row++)
        {
            var y = max.Y - ((row + 0.5f) * pixel);
            for (var column = 0; column < columns; column++)
            {
                var x = axisX + (((column + 0.5f) - (columns * 0.5f)) * pixel);
                var best = float.MinValue;
                byte bestMaterial = 0;
                foreach (var p in parts)
                {
                    var dx = x - p.Position.X;
                    var dy = y - p.Position.Y;
                    var radius = p.Radius;
                    if ((dx * dx) + (dy * dy) > radius * radius)
                    {
                        continue;
                    }

                    var reach = MathF.Sqrt((radius * radius) - (dx * dx) - (dy * dy));
                    var dims = p.Grid.Dimensions;
                    for (var z = p.Position.Z + reach; z > p.Position.Z - reach && z > best; z -= cell)
                    {
                        var local = Vector3.Transform(new Vector3(x, y, z) - p.Position, p.Inverse);
                        var c = (local + p.Half) / (2f * p.Half);
                        if (c.X < 0f || c.Y < 0f || c.Z < 0f || c.X >= 1f || c.Y >= 1f || c.Z >= 1f)
                        {
                            continue;
                        }

                        var m = p.Grid.At((int)(c.X * dims.X), (int)(c.Y * dims.Y), (int)(c.Z * dims.Z));
                        if (m != 0)
                        {
                            best = z;
                            bestMaterial = m;
                            break;
                        }
                    }
                }

                materials[column + (row * columns)] = bestMaterial;
                depth[column + (row * columns)] = best;
            }
        }

        return Crop(materials, depth, columns, rows, palette, pixel, out heightMeters, out widthMeters);
    }

    private static PortraitImage Crop(byte[] materials, float[] depth, int columns, int rows, VoxelPalette palette, float pixel,
        out float heightMeters, out float widthMeters)
    {
        int top = rows, bottom = -1, left = columns, right = -1;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                if (materials[column + (row * columns)] != 0)
                {
                    (top, bottom) = (Math.Min(top, row), Math.Max(bottom, row));
                    (left, right) = (Math.Min(left, column), Math.Max(right, column));
                }
            }
        }

        if (bottom < 0)
        {
            throw new InvalidOperationException("肖像に何も写りませんでした（体の格子が空）。");
        }

        // 左右は体の中心線に対して対称（中心の列からの遠い方に合わせる）。
        var center = columns / 2;
        var span = Math.Max(center - left, right - center + 1);
        left = center - span;
        right = center + span - 1;
        var width = right - left + 1;
        var height = bottom - top + 1;
        var cropped = new byte[width * height];
        var shade = new float[width * height];
        float Z(int column, int row, float fallback) => column < left || column > right || row < top || row > bottom
            || materials[column + (row * columns)] == 0 ? fallback : depth[column + (row * columns)];
        for (var row = top; row <= bottom; row++)
        {
            for (var column = left; column <= right; column++)
            {
                var i = column + (row * columns);
                var (cx, cy) = (column - left, row - top);
                cropped[cx + (cy * width)] = materials[i];
                if (materials[i] == 0)
                {
                    continue;
                }

                // 奥行きの傾き（セルの大きさ当たり）から法線を出し、左上から当てる。平らな面は 1 に近い明るさ。
                var z = depth[i];
                var dzdx = (Z(column + 1, row, z) - Z(column - 1, row, z)) / (2f * pixel);
                var dzdy = (Z(column, row + 1, z) - Z(column, row - 1, z)) / (2f * pixel);
                var normal = Vector3.Normalize(new Vector3(-dzdx, dzdy, 1f));
                var light = Vector3.Normalize(new Vector3(-0.35f, 0.45f, 0.82f));
                shade[cx + (cy * width)] = MathF.Round(Math.Clamp(0.55f + (0.55f * Math.Max(0f, Vector3.Dot(normal, light))), 0.55f, 1.05f) * 24f) / 24f; // 24 段に丸める（同じ明るさの連なりを 1 つの四角にまとめられる）
            }
        }

        heightMeters = height * pixel;
        widthMeters = width * pixel;
        return new PortraitImage(width, height, cropped, shade, palette, pixel);
    }
}
