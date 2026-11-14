using System.Numerics;
using Aigis.Foundation.Voxels;

namespace MiniRing.Voxels;

/// <summary>
/// 顔の部位ごとの寸法・位置・向き（キャラごとの顔の定義。すべて基準の顔に対する相対値で、既定値 = 基準の顔）。
/// 頭のボクセルの表面（正規化座標: 横 X・高さ Y・前 Z を頭の外形で −1..1）に <see cref="FaceSculptor"/> が巻き付ける。
/// 長さの値は倍率、位置の値は正規化座標のずれ、傾きは [rad]（+ は目・眉の外側の端が上がる）。
/// </summary>
public sealed record FaceParts(
    float EyeSpacing = 1f, float Nose = 1f, float Jaw = 1f,
    float EyeSize = 1f, float EyeHeight = 0f, float EyeTilt = 0f,
    float BrowHeight = 0f, float BrowTilt = 0f, float BrowThickness = 1f,
    float NoseWidth = 1f, float NoseLength = 1f,
    float MouthWidth = 1f, float MouthHeight = 0f, float Lips = 1f,
    float EarSize = 1f, float EarHeight = 0f,
    float JawWidth = 1f, float Cheekbones = 1f, float Hairline = 0f)
{
    public static FaceParts Default => new();

    public FaceParts Validate()
    {
        Check(EyeSpacing, 0.75f, 1.25f);
        Check(Nose, 0.75f, 1.3f);
        Check(Jaw, 0.75f, 1.3f);
        Check(EyeSize, 0.7f, 1.35f);
        Check(EyeHeight, -0.1f, 0.1f);
        Check(EyeTilt, -0.45f, 0.45f);
        Check(BrowHeight, -0.08f, 0.08f);
        Check(BrowTilt, -0.5f, 0.5f);
        Check(BrowThickness, 0.6f, 1.8f);
        Check(NoseWidth, 0.7f, 1.5f);
        Check(NoseLength, 0.7f, 1.4f);
        Check(MouthWidth, 0.6f, 1.4f);
        Check(MouthHeight, -0.07f, 0.07f);
        Check(Lips, 0.5f, 1.8f);
        Check(EarSize, 0.6f, 1.5f);
        Check(EarHeight, -0.1f, 0.1f);
        Check(JawWidth, 0.8f, 1.25f);
        Check(Cheekbones, 0.5f, 1.8f);
        Check(Hairline, -0.12f, 0.12f);
        return this;
    }

    private static void Check(float value, float lo, float hi)
    {
        if (!float.IsFinite(value) || value < lo || value > hi)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"顔の値 {value} は {lo}〜{hi} の有限値で指定してください。");
        }
    }

    /// <summary>名前から決まる顔（同じ名前はいつも同じ顔。部位ごとに許す幅の中で散らす）。</summary>
    public static FaceParts For(string identity)
    {
        var h = Bots.BotNames.Hash(identity);
        float Next(float lo, float hi)
        {
            h = unchecked((h * 1664525u) + 1013904223u);
            return lo + (((h >> 8) & 0xFFFF) / 65535f * (hi - lo));
        }

        // 乱数の引く順を変えると、同じ名前の顔が変わる（記録の再生に響かない: 顔は描画と接触の形だけ）。
        return new FaceParts(
            EyeSpacing: Next(0.8f, 1.2f), Nose: Next(0.8f, 1.25f), Jaw: Next(0.8f, 1.25f),
            EyeSize: Next(0.8f, 1.25f), EyeHeight: Next(-0.06f, 0.06f), EyeTilt: Next(-0.3f, 0.3f),
            BrowHeight: Next(-0.05f, 0.05f), BrowTilt: Next(-0.35f, 0.35f), BrowThickness: Next(0.7f, 1.6f),
            NoseWidth: Next(0.8f, 1.35f), NoseLength: Next(0.8f, 1.3f),
            MouthWidth: Next(0.7f, 1.3f), MouthHeight: Next(-0.04f, 0.04f), Lips: Next(0.6f, 1.6f),
            EarSize: Next(0.7f, 1.4f), EarHeight: Next(-0.05f, 0.05f),
            JawWidth: Next(0.85f, 1.2f), Cheekbones: Next(0.6f, 1.6f), Hairline: Next(-0.08f, 0.08f));
    }
}

/// <summary>
/// 顔のテンプレートを頭のボクセルへ巻き付ける（見た目だけ。頭の中身（脳・骨）は動かさない）。
/// <list type="number">
/// <item>起伏: 鼻・あご・頬骨（前へ）と耳・あごの幅（横へ）を、柱ごとに表面を前後（左右）へずらして作る。皮膚は 1 層のまま、
/// その下の肉・骨が引き伸ばされる（空洞を作らない）。</item>
/// <item>目: 目のボクセル（白目・黒目）を作り直す。目の寸法・間隔・高さ・傾きから、基準の目を逆写しして置く（表面からの深さを保つ）。
/// 目は視力の材料（<see cref="BoxerBody.Vision"/>）なので、潰せる白目の体積を持ったまま動かす。</item>
/// <item>色の部位: 眉・口（唇）・ひげ・髪を、表面の皮膚へ塗る（部位ごとの帯の形を寸法・傾きで置く）。</item>
/// </list>
/// </summary>
public static class FaceSculptor
{
    /// <summary>基準の目の位置（<see cref="BoxerAnatomy"/> の目の帯の中心。正規化座標）。</summary>
    private const float BaseEyeSide = 0.38f;
    private const float BaseEyeHeight = 0.18f;

    /// <summary>頭の外形（cell 単位の中心と半分の大きさ）。</summary>
    public readonly record struct Frame(Vector3 Center, Vector3 Half);

    /// <summary>頭の格子（中身の詰まったセルの範囲を外形とする）を作り直す。<paramref name="look"/> の髪型・眉・ひげ・顔の寸法を使う。</summary>
    public static byte[] Sculpt(VoxelGrid grid, FaceLook look, out Frame frame)
    {
        var parts = look.Parts.Validate();
        var (nx, ny, nz) = grid.Dimensions;
        var (lo, hi) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
        var cells = new byte[nx * ny * nz];
        for (var z = 0; z < nz; z++)
        {
            for (var y = 0; y < ny; y++)
            {
                for (var x = 0; x < nx; x++)
                {
                    var m = grid.At(x, y, z);
                    cells[Index(nx, ny, x, y, z)] = m;
                    if (m != 0)
                    {
                        lo = Vector3.Min(lo, new Vector3(x, y, z));
                        hi = Vector3.Max(hi, new Vector3(x + 1, y + 1, z + 1));
                    }
                }
            }
        }

        frame = new Frame((lo + hi) * 0.5f, Vector3.Max((hi - lo) * 0.5f, Vector3.One));
        var f = frame;
        var dims = (nx, ny, nz);

        // 1. 起伏（前 = +Z、左右 = ±X）。
        cells = Warp(cells, dims, 2, 1, 0.6f * f.Half.Z, (x, y) => FrontRelief(parts, f, x, y), f);
        foreach (var side in new[] { 1, -1 })
        {
            cells = Warp(cells, dims, 0, side, 0.7f * f.Half.X, (y, z) => SideRelief(parts, f, side, y, z), f);
        }

        // 凹み・端で皮膚の下の肉・骨が表に出たら、その表を皮膚に戻す（顔に肉の赤を出さない）。
        cells = Reskin(cells, dims);

        // 2. 目。
        cells = PlaceEyes(cells, dims, parts, f);

        // 3. 色の部位（眉・口・ひげ・髪）。
        return Paint(cells, dims, parts, look, f);
    }

    private static int Index(int nx, int ny, int x, int y, int z) => x + (nx * (y + (ny * z)));

    private static float Bump(float dx, float sx, float dy, float sy) => MathF.Exp(-((dx * dx / (sx * sx)) + (dy * dy / (sy * sy))));

    /// <summary>前の面の起伏 [cell]（+ は前へ盛る、− は凹む）。<paramref name="x"/>・<paramref name="y"/> は柱の位置（cell）。</summary>
    private static float FrontRelief(FaceParts p, Frame f, int x, int y)
    {
        var n = new Vector2((x + 0.5f - f.Center.X) / f.Half.X, (y + 0.5f - f.Center.Y) / f.Half.Y);
        var hz = f.Half.Z;
        // 鼻: 鼻筋（弱く長い）と鼻先（強く短い）。頭の前の面（目の高さの下の真ん中）。
        var nose = p.Nose * 0.13f * hz * ((0.5f * Bump(n.X, 0.06f * p.NoseWidth, n.Y - 0.1f, 0.16f)) + Bump(n.X, 0.12f * p.NoseWidth, n.Y + (0.14f * p.NoseLength), 0.1f * p.NoseLength));
        // あご: 基準の顎からの突き出し（1 で基準）。
        var chin = (p.Jaw - 1f) * 0.25f * hz * Bump(n.X, 0.3f, n.Y + 0.72f, 0.12f);
        // 頬骨（左右）。
        var cheek = p.Cheekbones * 0.07f * hz * (Bump(MathF.Abs(n.X) - 0.55f, 0.17f, n.Y + 0.12f, 0.14f));
        return nose + chin + cheek;
    }

    /// <summary>横の面の起伏 [cell]（外向きが +）。<paramref name="y"/>・<paramref name="z"/> は柱の位置（cell）。</summary>
    private static float SideRelief(FaceParts p, Frame f, int side, int y, int z)
    {
        var n = new Vector2((y + 0.5f - f.Center.Y) / f.Half.Y, (z + 0.5f - f.Center.Z) / f.Half.Z);
        var hx = f.Half.X;
        // 耳: 頭の横の真ん中、耳たぶは目の高さ、上は眉の高さ。
        var ear = 0.24f * hx * Bump(n.X - p.EarHeight - 0.02f, 0.2f * p.EarSize, n.Y + 0.05f, 0.17f * p.EarSize);
        // あごの幅（下顎の角）。
        var jaw = (p.JawWidth - 1f) * 0.45f * hx * Bump(n.X + 0.6f, 0.2f, n.Y - 0.25f, 0.4f);
        return ear + jaw;
    }

    /// <summary>
    /// 柱ごとに表面を displacement の返す cell 数だけずらす（軸 <paramref name="axis"/> の + 側 / − 側、<paramref name="side"/>）。
    /// 中心から <paramref name="innerRel"/>（cell）までは動かさず（前の面は脳より外の骨・肉・皮膚だけが動く）、表面へ向かってなめらかにずらす量を増やす（皮膚は 1 層のまま、中身が伸びる）。
    /// </summary>
    private static byte[] Warp(byte[] src, (int X, int Y, int Z) dims, int axis, int side, float innerRel, Func<int, int, float> displacement, Frame f)
    {
        var result = (byte[])src.Clone();
        var (nx, ny, nz) = dims;
        var center = axis == 0 ? f.Center.X : f.Center.Z;
        var half = axis == 0 ? f.Half.X : f.Half.Z;
        var length = axis == 0 ? nx : nz;
        var (a, b) = axis == 0 ? (ny, nz) : (nx, ny);
        for (var v = 0; v < b; v++)
        {
            for (var u = 0; u < a; u++)
            {
                var d = displacement(u, v);
                if (MathF.Abs(d) < 0.01f)
                {
                    continue;
                }

                for (var t = 0; t < length; t++)
                {
                    var rel = side * (t + 0.5f - center);
                    if (rel <= 0f)
                    {
                        continue;
                    }

                    var w = rel <= innerRel ? 0f : Math.Clamp((rel - innerRel) / MathF.Max(0.5f, half - innerRel), 0f, 1f);
                    w = w * w * (3f - (2f * w));
                    var s = (int)MathF.Round(center + (side * (rel - (d * w))) - 0.5f);
                    var (x, y, z) = axis == 0 ? (t, u, v) : (u, v, t);
                    var (sx, sy, sz) = axis == 0 ? (s, u, v) : (u, v, s);
                    result[Index(nx, ny, x, y, z)] = sx < 0 || sy < 0 || sz < 0 || sx >= nx || sy >= ny || sz >= nz ? (byte)0 : src[Index(nx, ny, sx, sy, sz)];
                }
            }
        }

        return result;
    }

    private static byte[] Reskin(byte[] src, (int X, int Y, int Z) dims)
    {
        var result = (byte[])src.Clone();
        for (var z = 0; z < dims.Z; z++)
        {
            for (var y = 0; y < dims.Y; y++)
            {
                for (var x = 0; x < dims.X; x++)
                {
                    var i = Index(dims.X, dims.Y, x, y, z);
                    if (src[i] is BoxerAnatomy.Muscle or BoxerAnatomy.Bone && FaceSideExposed(src, dims, x, y, z))
                    {
                        result[i] = BoxerAnatomy.Skin;
                    }
                }
            }
        }

        return result;
    }

    /// <summary>顔の面（前と左右）に空きが接しているか。首の付け根・うなじ・頭頂の切り口は含めない。</summary>
    private static bool FaceSideExposed(byte[] cells, (int X, int Y, int Z) dims, int x, int y, int z)
    {
        foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1) })
        {
            var (ax, az) = (x + dx, z + dz);
            if (ax < 0 || az < 0 || ax >= dims.X || az >= dims.Z || cells[Index(dims.X, dims.Y, ax, y, az)] == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static int FrontZ(byte[] cells, (int X, int Y, int Z) dims, int x, int y)
    {
        for (var z = dims.Z - 1; z >= 0; z--)
        {
            if (cells[Index(dims.X, dims.Y, x, y, z)] != 0)
            {
                return z;
            }
        }

        return -1;
    }

    private static bool Exposed(byte[] cells, (int X, int Y, int Z) dims, int x, int y, int z)
    {
        foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
        {
            var (ax, ay, az) = (x + dx, y + dy, z + dz);
            if (ax < 0 || ay < 0 || az < 0 || ax >= dims.X || ay >= dims.Y || az >= dims.Z || cells[Index(dims.X, dims.Y, ax, ay, az)] == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEye(byte m) => m is BoxerAnatomy.Eye or BoxerAnatomy.Pupil;

    private static byte[] PlaceEyes(byte[] src, (int X, int Y, int Z) dims, FaceParts p, Frame f)
    {
        var (nx, ny, nz) = dims;
        var result = (byte[])src.Clone();
        // 前の面（柱ごとの最前のセル）。
        var front = new int[nx * ny];
        for (var y = 0; y < ny; y++)
        {
            for (var x = 0; x < nx; x++)
            {
                front[x + (nx * y)] = FrontZ(src, dims, x, y);
            }
        }

        // 基準の目を消す（表の目は皮膚に、奥の目は肉に）。
        for (var z = 0; z < nz; z++)
        {
            for (var y = 0; y < ny; y++)
            {
                for (var x = 0; x < nx; x++)
                {
                    if (IsEye(src[Index(nx, ny, x, y, z)]))
                    {
                        result[Index(nx, ny, x, y, z)] = z >= front[x + (nx * y)] ? BoxerAnatomy.Skin : BoxerAnatomy.Muscle;
                    }
                }
            }
        }

        var cos = MathF.Cos(p.EyeTilt);
        var sin = MathF.Sin(p.EyeTilt);
        for (var z = 0; z < nz; z++)
        {
            for (var y = 0; y < ny; y++)
            {
                for (var x = 0; x < nx; x++)
                {
                    var index = Index(nx, ny, x, y, z);
                    if (src[index] == 0 && z > front[x + (nx * y)])
                    {
                        continue;
                    }

                    var n = new Vector3((x + 0.5f - f.Center.X) / f.Half.X, (y + 0.5f - f.Center.Y) / f.Half.Y, (z + 0.5f - f.Center.Z) / f.Half.Z);
                    if (n.Z < 0.3f || MathF.Abs(n.X) < 0.02f)
                    {
                        continue;
                    }

                    var side = n.X > 0f ? 1f : -1f;
                    // 目の中心からのずれ（外向き = +）→ 傾きを戻し、寸法で割って、基準の目の位置へ。
                    var lx = (n.X - (side * BaseEyeSide * p.EyeSpacing)) * side;
                    var ly = n.Y - (BaseEyeHeight + p.EyeHeight);
                    var sx = ((lx * cos) + (ly * sin)) / p.EyeSize;
                    var sy = ((-lx * sin) + (ly * cos)) / p.EyeSize;
                    var srcX = (int)MathF.Floor(f.Center.X + (side * (BaseEyeSide + sx) * f.Half.X));
                    var srcY = (int)MathF.Floor(f.Center.Y + ((BaseEyeHeight + sy) * f.Half.Y));
                    if (srcX < 0 || srcX >= nx || srcY < 0 || srcY >= ny || front[srcX + (nx * srcY)] < 0 || front[x + (nx * y)] < 0)
                    {
                        continue;
                    }

                    var srcZ = front[srcX + (nx * srcY)] - (front[x + (nx * y)] - z);
                    if (srcZ < 0 || srcZ >= nz || !IsEye(src[Index(nx, ny, srcX, srcY, srcZ)]) || src[index] == 0)
                    {
                        continue;
                    }

                    result[index] = BoxerAnatomy.Eye;
                }
            }
        }

        PlacePupils(result, dims, p, f);
        return result;
    }

    /// <summary>黒目: 左右の目それぞれ、表に出ている白目のうち、目の中心にいちばん近い 1 つ。</summary>
    private static void PlacePupils(byte[] cells, (int X, int Y, int Z) dims, FaceParts p, Frame f)
    {
        var (nx, ny, nz) = dims;
        foreach (var side in new[] { 1f, -1f })
        {
            var target = new Vector2(side * BaseEyeSide * p.EyeSpacing, BaseEyeHeight + p.EyeHeight);
            EnsureEye(cells, dims, f, side, target);
            var best = (X: -1, Y: -1, Z: -1);
            var bestDistance = float.MaxValue;
            for (var z = 0; z < nz; z++)
            {
                for (var y = 0; y < ny; y++)
                {
                    for (var x = 0; x < nx; x++)
                    {
                        if (cells[Index(nx, ny, x, y, z)] != BoxerAnatomy.Eye || !Exposed(cells, dims, x, y, z))
                        {
                            continue;
                        }

                        var n = new Vector3((x + 0.5f - f.Center.X) / f.Half.X, (y + 0.5f - f.Center.Y) / f.Half.Y, (z + 0.5f - f.Center.Z) / f.Half.Z);
                        if ((n.X > 0f) != (side > 0f))
                        {
                            continue;
                        }

                        var d = Vector2.DistanceSquared(new Vector2(n.X, n.Y), target) - (0.01f * n.Z);
                        if (d < bestDistance)
                        {
                            bestDistance = d;
                            best = (x, y, z);
                        }
                    }
                }
            }

            if (best.X >= 0)
            {
                cells[Index(nx, ny, best.X, best.Y, best.Z)] = BoxerAnatomy.Pupil;
            }
        }
    }

    /// <summary>
    /// 粗い頭で目の寸法が小さいと、片方の白目が 0 セルになりうる（視力 0 = 生まれつきの失明になる）。その側に白目が無ければ、
    /// 目の中心にいちばん近い表の皮膚を白目にして、最低 1 セルは潰せる目を残す。
    /// </summary>
    private static void EnsureEye(byte[] cells, (int X, int Y, int Z) dims, Frame f, float side, Vector2 target)
    {
        var (nx, ny, nz) = dims;
        var best = -1;
        var bestDistance = float.MaxValue;
        for (var z = 0; z < nz; z++)
        {
            for (var y = 0; y < ny; y++)
            {
                for (var x = 0; x < nx; x++)
                {
                    var i = Index(nx, ny, x, y, z);
                    var n = new Vector3((x + 0.5f - f.Center.X) / f.Half.X, (y + 0.5f - f.Center.Y) / f.Half.Y, (z + 0.5f - f.Center.Z) / f.Half.Z);
                    if ((n.X > 0f) != (side > 0f))
                    {
                        continue;
                    }

                    if (cells[i] == BoxerAnatomy.Eye)
                    {
                        return;
                    }

                    if (cells[i] == BoxerAnatomy.Skin && n.Z > 0.3f && Exposed(cells, dims, x, y, z))
                    {
                        var d = Vector2.DistanceSquared(new Vector2(n.X, n.Y), target) - (0.01f * n.Z);
                        if (d < bestDistance)
                        {
                            bestDistance = d;
                            best = i;
                        }
                    }
                }
            }
        }

        if (best >= 0)
        {
            cells[best] = BoxerAnatomy.Eye;
        }
    }

    private static byte[] Paint(byte[] src, (int X, int Y, int Z) dims, FaceParts p, FaceLook look, Frame f)
    {
        var (nx, ny, nz) = dims;
        var result = (byte[])src.Clone();
        for (var z = 0; z < nz; z++)
        {
            for (var y = 0; y < ny; y++)
            {
                for (var x = 0; x < nx; x++)
                {
                    var material = src[Index(nx, ny, x, y, z)];
                    if (material is not (BoxerAnatomy.Skin or BoxerAnatomy.Hair) || !Exposed(src, dims, x, y, z))
                    {
                        continue;
                    }

                    var n = new Vector3((x + 0.5f - f.Center.X) / f.Half.X, (y + 0.5f - f.Center.Y) / f.Half.Y, (z + 0.5f - f.Center.Z) / f.Half.Z);
                    result[Index(nx, ny, x, y, z)] = Surface(p, look, material == BoxerAnatomy.Hair, n);
                }
            }
        }

        return result;
    }

    /// <summary>表面の 1 セル（正規化座標 <paramref name="n"/>）の材質: 唇 → 眉・ひげ・髪 → 皮膚の順に決める。</summary>
    private static byte Surface(FaceParts p, FaceLook look, bool wasHair, Vector3 n)
    {
        var face = n.Z > 0.45f;
        var line = n.Z > 0.3f && MathF.Abs(n.X) < 0.3f * p.MouthWidth && MathF.Abs(n.Y + 0.42f - p.MouthHeight) < 0.075f * p.Lips;
        if (line)
        {
            return BoxerAnatomy.Lip;
        }

        var hair = look.Style switch
        {
            Hairstyle.Full => BoxerAnatomy.IsHair(new Vector3(n.X, n.Y - p.Hairline, n.Z)),
            Hairstyle.Bald => false,
            Hairstyle.Crop => wasHair && n.Y > 0.45f + p.Hairline,
            Hairstyle.Mohawk => wasHair && n.Y > 0.2f && MathF.Abs(n.X) < 0.22f,
            Hairstyle.Afro => wasHair || (n.Y > 0.05f && !face),
            _ => wasHair,
        };
        var brow = look.Brows && n.Z > 0.3f && BrowBand(p, n);
        var beard = look.Beard && n.Z > 0.2f && n.Y < -0.5f;
        return hair || brow || beard ? BoxerAnatomy.Hair : BoxerAnatomy.Skin;
    }

    /// <summary>眉の帯: 目の上、外側が上がる向きに傾けた細い帯（基準は高さ 0.52・長さ ±0.26・厚み ±0.07。頭が 14 段しかないので、目の段（0.07〜0.38）の 1 段上）。</summary>
    private static bool BrowBand(FaceParts p, Vector3 n)
    {
        var side = n.X > 0f ? 1f : -1f;
        var lx = (n.X - (side * 0.385f * p.EyeSpacing)) * side;
        var ly = n.Y - (0.52f + p.BrowHeight + p.EyeHeight);
        var cos = MathF.Cos(p.BrowTilt);
        var sin = MathF.Sin(p.BrowTilt);
        var u = (lx * cos) + (ly * sin);
        var v = (-lx * sin) + (ly * cos);
        return MathF.Abs(u) < 0.26f * p.EyeSize && MathF.Abs(v) < 0.07f * p.BrowThickness;
    }
}
