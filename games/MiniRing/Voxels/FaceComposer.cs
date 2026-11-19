namespace MiniRing.Voxels;

/// <summary>組み立てた顔: 任意の解像度の材質の格子。添字は x + nx * (y + ny * z)、y は上、z は顔の前、x は +X（本人の左 = 正面から見て右）。</summary>
public sealed record FaceVolume(int Nx, int Ny, int Nz, FaceMaterial[] Cells)
{
    public FaceMaterial At(int x, int y, int z) => Cells[x + (Nx * (y + (Ny * z)))];

    public int Count(FaceMaterial material) => Cells.Count(c => c == material);
}

/// <summary>
/// 顔の組み立て（見た目と接触の形。強さは変えない）: 土台の頭の輪郭（あごの型 × 基準の上からの輪郭の 3 方向の視体積）に、髪・ひげ・眉・目・唇を表面へ塗り、
/// 鼻と耳を足す。部品どうしは重み付きの和を閾値（0.5）で切って混ぜる（0/1 で元の部品、0.5 ずつで中間）。部位の位置・寸法・傾きは
/// <see cref="FaceParts"/> の値（基準の顔に対する相対値）。出力の解像度は引数で決まる（計算は参考図の格子 20 x 28 x 20 = 頭の箱の座標で、
/// セルの中心を標本化する）。同じ入力から同じ出力（乱数は座標のハッシュだけ）。
/// </summary>
public static class FaceComposer
{
    private const float HalfHeight = FacePartLibrary.RefHeight / 2f;
    private const float Mid = FacePartLibrary.RefWidth / 2f;

    /// <summary>部位の材質を表面から何セル（参考図の格子で）の深さまで塗るか。目は白目の体積を持たせるため 2、眉・唇・そばかすは 1。</summary>
    private const float EyeBand = 2f;
    private const float PaintBand = 1f;

    /// <summary>
    /// 眉・唇・目・影を塗る閾値（形の和の割合）。1 セルの細い線（唇・眉・まぶたの影）が、格子の目と合わない位置・細くした寸法で双一次の標本化に薄められて
    /// 消えないよう、輪郭の閾値（0.5）より低くする（2 つの部品を半々に混ぜたとき、片方にだけある細い線は残り、重み 0.3 以下の線は残らない）。
    /// </summary>
    private const float PaintThreshold = 0.4f;

    private static float Smooth(float v, float a, float b)
    {
        var t = Math.Clamp((v - a) / (b - a), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    /// <summary>座標だけから決まる 0..1 の値（乱数を使わない: 同じ入力は同じ顔）。</summary>
    private static float CellNoise(int x, int y, int z)
    {
        var h = unchecked((uint)((x * 73856093) ^ (y * 19349663) ^ (z * 83492791)));
        h = unchecked(h * 2654435761u);
        h ^= h >> 15;
        h = unchecked(h * 2246822519u);
        return ((h >> 8) & 0xFFFF) / 65536f;
    }

    private static float Bump(float v, float center, float sigma) => MathF.Exp(-((v - center) * (v - center)) / (2f * sigma * sigma));

    /// <summary>顔 <paramref name="spec"/> を nx × ny × nz のセルで組み立てる。</summary>
    public static FaceVolume Compose(FaceSpec spec, int nx, int ny, int nz, FacePartLibrary? library = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (nx < 1 || ny < 1 || nz < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(nx), "格子の大きさは 1 以上にしてください。");
        }

        library ??= FacePartLibrary.Default;
        spec.Validate(library);
        var p = spec.Values;
        var layout = library.Layout;
        var jaws = spec.Weighted("jaw", library);
        var hairs = spec.Weighted("hair", library);
        var beards = spec.Weighted("beard", library);
        var eyes = spec.Weighted("eyes", library);
        var brows = spec.Weighted("brows", library);
        var mouths = spec.Weighted("mouth", library);
        var noses = spec.Weighted("nose", library);
        var ears = spec.Weighted("ears", library);
        float sx = FacePartLibrary.RefWidth / (float)nx, sy = FacePartLibrary.RefHeight / (float)ny, sz = FacePartLibrary.RefDepth / (float)nz;
        int Index(int x, int y, int z) => x + (nx * (y + (ny * z)));
        float RefX(int x) => (x + 0.5f) * sx;
        float RefY(int y) => (ny - 1 - y + 0.5f) * sy; // 上から数えた行（下向きが正）
        float RefZ(int z) => (z + 0.5f) * sz;           // 後ろから数えた奥行き

        // 顔の幅の値（FaceWidth・あごの幅・頬骨）で頭の輪郭を引き伸ばす写し（頭の座標 → 基準の顔の座標）。髪・ひげは頭に合わせて同じ写しで標本化する。
        float WidthX(float rx, float ry)
        {
            var lower = Smooth(ry, 13f, 21f);
            var cheek = Bump(ry, 15.5f, 2.5f);
            return Mid + ((rx - Mid) / ((1f + ((p.JawWidth - 1f) * lower)) * (1f + ((p.Cheekbones - 1f) * 0.08f * cheek)) * p.FaceWidth));
        }

        static float Blend(IReadOnlyList<(FacePart Part, float Weight)> list, Func<FacePart, float> value)
        {
            var sum = 0f;
            foreach (var (part, weight) in list)
            {
                sum += weight * value(part);
            }

            return sum;
        }

        // 1. 頭の輪郭（視体積）と髪。
        var head = new bool[nx * ny * nz];
        var hairCand = new bool[nx * ny * nz];
        var cells = new FaceMaterial[nx * ny * nz];
        for (var z = 0; z < nz; z++)
        {
            var rz = RefZ(z);
            for (var y = 0; y < ny; y++)
            {
                var ry = RefY(y);
                var chin = Smooth(ry, 17f, 23f);
                var hairRy = ry - (p.Hairline * HalfHeight);
                for (var x = 0; x < nx; x++)
                {
                    var rx = RefX(x);
                    var wrx = WidthX(rx, ry);
                    var wrz = rz - ((p.Jaw - 1f) * 6f * chin);
                    var inNoseRows = ry >= layout.NoseRowFrom && ry < layout.NoseRowTo + 1f;
                    // 顔の形の部品（四角い・広い）は頭蓋骨の上を四角く描くものもあるので、頭の上（額より上）は基準の頭の丸い輪郭より外へ出さない。
                    var cranium = Smooth(ry, 7f, 11f);
                    var jawFront = Blend(jaws, j => j.Front.Solid(wrx, ry));
                    var front = (cranium * jawFront) + ((1f - cranium) * MathF.Min(jawFront, library.BaseFront.Solid(wrx, ry)));
                    var side = inNoseRows && wrz > layout.ProfileCap ? 0f : Blend(jaws, j => j.Side!.Solid(wrz, ry));
                    // 上からの輪郭: あごの部品が持っていればそれ（顔の形）、無ければ基準の頭。
                    var topPx = Blend(jaws, j =>
                    {
                        var t = j.Top ?? library.BaseTop;
                        return t.Solid(wrx, FacePartLibrary.RefDepth - wrz) - t.Coverage(wrx, FacePartLibrary.RefDepth - wrz, FaceMaterial.Shadow);
                    });
                    var inside = front >= 0.5f && side >= 0.5f && topPx >= 0.5f;
                    var hx = Mid + ((rx - Mid) / p.FaceWidth);
                    // 頭の上の髪は頭蓋骨に沿う: 髪の部品の輪郭が頭より 1 セル細くても、頭の中のセルは隣の 1 セルまでの髪を借りる（頭の角に地肌が出ない）。
                    var pad = inside ? 1f : 0f;
                    var hf = Blend(hairs, h => MathF.Max(h.Front.Coverage(hx, hairRy, FaceMaterial.Hair), MathF.Max(h.Front.Coverage(hx - pad, hairRy, FaceMaterial.Hair), h.Front.Coverage(hx + pad, hairRy, FaceMaterial.Hair))));
                    var hs = Blend(hairs, h => MathF.Max(h.Side!.Coverage(rz, hairRy, FaceMaterial.Hair), MathF.Max(h.Side.Coverage(rz - pad, hairRy, FaceMaterial.Hair), h.Side.Coverage(rz + pad, hairRy, FaceMaterial.Hair))));
                    head[Index(x, y, z)] = inside;
                    hairCand[Index(x, y, z)] = hf >= 0.5f && hs >= 0.5f;
                }
            }
        }

        // 髪は頭の中（皮膚の上を覆う）と、頭の輪郭の外の reach セルまで（髪の部品が持つ量。普通の髪は 1〜2 セル、アフロ・ポニーテール・長髪だけ大きい）。
        var reach = hairs.Count == 0 ? 0f : Blend(hairs, h => h.Reach);
        var near = Dilate(head, nx, ny, nz, (int)MathF.Ceiling(reach / sx), (int)MathF.Ceiling(reach / sy), (int)MathF.Ceiling(reach / sz));

        // 髪質（Frizz）: 頭の上の髪から 1 セル（縮れるほど 2 セル）の範囲に、座標のハッシュでこの割合の毛先を足す（輪郭のざらつき。まっすぐな髪は 0）。
        var frizz = hairs.Count == 0 ? 0f : Blend(hairs, h => h.Frizz);
        var onHead = new bool[cells.Length];
        for (var i = 0; i < cells.Length; i++)
        {
            onHead[i] = head[i] && hairCand[i];
        }

        var halo = frizz <= 0f ? onHead : Dilate(onHead, nx, ny, nz, (int)MathF.Ceiling((1f + frizz) / sx), (int)MathF.Ceiling((1f + frizz) / sy), (int)MathF.Ceiling((1f + frizz) / sz));
        for (var y = 0; y < ny; y++)
        {
            for (var x = 0; x < nx; x++)
            {
                // 顔の前（その柱の頭の前の面より前）には毛先を足さない（額・目にかぶせない）。
                var headFront = -1;
                for (var z = nz - 1; z >= 0 && headFront < 0; z--)
                {
                    headFront = head[Index(x, y, z)] ? z : -1;
                }

                for (var z = 0; z < nz; z++)
                {
                    var i = Index(x, y, z);
                    if (head[i])
                    {
                        cells[i] = hairCand[i] ? FaceMaterial.Hair : FaceMaterial.Skin;
                    }
                    else if (near[i] && (hairCand[i] || (halo[i] && (headFront < 0 || z <= headFront) && CellNoise(x, y, z) < frizz)))
                    {
                        cells[i] = FaceMaterial.Hair;
                    }
                }
            }
        }

        // ひげ（頭の内側の、正面と横の両方でひげの所）。
        if (beards.Count > 0)
        {
            for (var z = 0; z < nz; z++)
            {
                for (var y = 0; y < ny; y++)
                {
                    for (var x = 0; x < nx; x++)
                    {
                        var i = Index(x, y, z);
                        if (head[i] && cells[i] == FaceMaterial.Skin
                            && Blend(beards, b => b.Front.Coverage(WidthX(RefX(x), RefY(y)), RefY(y), FaceMaterial.Hair)) >= 0.5f
                            && Blend(beards, b => b.Density) > CellNoise(x, y, z)
                            && Blend(beards, b => b.Side!.Coverage(RefZ(z), RefY(y), FaceMaterial.Hair)) >= 0.5f
                            && !BetweenLips(mouths, p, RefX(x), RefY(y), layout))
                        {
                            cells[i] = FaceMaterial.Hair;
                        }
                    }
                }
            }
        }

        // 眉の張り出しと鼻筋の高さ: 眉・鼻筋の高さの前へ皮膚を足す（塗る前なので、眉・目は新しい表面に塗られる）。
        var ridges = spec.Weighted("browridge", library);
        if (ridges.Count > 0 || p.BrowRidge > 0f)
        {
            ApplyRelief(cells, head, nx, ny, ridges, layout, p, sx, sy, sz, nz);
        }

        // 2. 表面（柱ごとの、いちばん前の詰まったセル）。目・眉・唇・そばかすは表面から塗る。
        var top = new int[nx * ny];
        for (var y = 0; y < ny; y++)
        {
            for (var x = 0; x < nx; x++)
            {
                top[x + (nx * y)] = -1;
                for (var z = nz - 1; z >= 0; z--)
                {
                    if (cells[Index(x, y, z)] != FaceMaterial.Empty)
                    {
                        top[x + (nx * y)] = z;
                        break;
                    }
                }
            }
        }

        var paint = new FaceMaterial[cells.Length];
        for (var y = 0; y < ny; y++)
        {
            var ry = RefY(y);
            for (var x = 0; x < nx; x++)
            {
                var zTop = top[x + (nx * y)];
                if (zTop < 0)
                {
                    continue;
                }

                var rx = RefX(x);
                for (var z = zTop; z >= 0; z--)
                {
                    var depth = (zTop - z) * sz; // 表面から（参考図のセル）
                    if (depth >= EyeBand)
                    {
                        break;
                    }

                    var i = Index(x, y, z);
                    if (cells[i] == FaceMaterial.Hair && depth >= PaintBand)
                    {
                        continue;
                    }

                    FaceMaterial? m = null;
                    if (depth < PaintBand)
                    {
                        m = Feature(brows, p, rx, ry, layout, brow: true) ?? Lip(mouths, p, rx, ry, layout) ?? Freckle(p, x, y, rx, ry, cells[i]);
                    }

                    if (Eye(eyes, p, rx, ry, layout, depth) is { } eye)
                    {
                        m = eye;
                    }

                    if (m is { } painted)
                    {
                        paint[i] = painted;
                    }
                }
            }
        }

        for (var i = 0; i < cells.Length; i++)
        {
            if (paint[i] != FaceMaterial.Empty)
            {
                cells[i] = paint[i];
            }
        }

        // 3. 鼻（正面 × 横の積。横の 0 列目が noseSurface、先端が前の端）と耳（頭の横の外の 2 セルに、横 × 正面の厚み）を足す。
        var extra = new FaceMaterial[cells.Length];
        if (noses.Count > 0)
        {
            for (var z = 0; z < nz; z++)
            {
                var rz = RefZ(z);
                for (var y = 0; y < ny; y++)
                {
                    var ry = RefY(y);
                    for (var x = 0; x < nx; x++)
                    {
                        var rx = RefX(x);
                        var anchorY = Blend(noses, n => n.Anchor.Y);
                        var py = anchorY + ((ry - layout.NoseTopRow) / p.NoseLength);
                        var sidePx = (rz - layout.NoseSurface) / p.Nose;
                        if (sidePx < 0f)
                        {
                            continue;
                        }

                        var front = Blend(noses, n => n.Front.Solid((n.Front.Width / 2f) + ((rx - Mid) / p.NoseWidth), py));
                        var side = Blend(noses, n => n.Side!.Solid(sidePx, py));
                        if (front >= 0.5f && side >= 0.5f)
                        {
                            var nostril = Blend(noses, n => n.Front.Coverage((n.Front.Width / 2f) + ((rx - Mid) / p.NoseWidth), py, FaceMaterial.Nostril)) >= PaintThreshold;
                            extra[Index(x, y, z)] = nostril ? FaceMaterial.Nostril : FaceMaterial.Skin;
                        }
                    }
                }
            }
        }

        if (ears.Count > 0)
        {
            var earY = layout.EarSide.Y - (p.EarHeight * HalfHeight);
            for (var z = 0; z < nz; z++)
            {
                var rz = RefZ(z);
                for (var y = 0; y < ny; y++)
                {
                    var ry = RefY(y);
                    // 頭の横の端（この行・この奥行きでいちばん外の詰まったセルの外縁）。
                    var edge = new float[2];
                    for (var s = 0; s < 2; s++)
                    {
                        edge[s] = -1f;
                        for (var k = 0; k < nx; k++)
                        {
                            var x = s == 1 ? nx - 1 - k : k;
                            if (head[Index(x, y, z)])
                            {
                                edge[s] = s == 1 ? (x + 1) * sx : x * sx;
                                break;
                            }
                        }
                    }

                    for (var x = 0; x < nx; x++)
                    {
                        var s = x * 2 >= nx ? 1 : 0;
                        var rx = RefX(x);
                        if (edge[s] < 0f || cells[Index(x, y, z)] != FaceMaterial.Empty)
                        {
                            continue;
                        }

                        var outward = s == 1 ? rx - edge[s] : edge[s] - rx;
                        if (outward < 0f)
                        {
                            continue;
                        }

                        var inside = Blend(ears, e => e.Side!.Solid(e.Anchor.X + ((rz - layout.EarSide.X) / p.EarSize), e.Anchor.Y + ((ry - earY) / p.EarSize))) >= 0.5f
                            && Blend(ears, e => e.Front.Solid(outward / p.EarSize, e.Anchor.Y + ((ry - earY) / p.EarSize))) >= 0.5f;
                        if (inside)
                        {
                            var shadow = Blend(ears, e => e.Side!.Coverage(e.Anchor.X + ((rz - layout.EarSide.X) / p.EarSize), e.Anchor.Y + ((ry - earY) / p.EarSize), FaceMaterial.Shadow)) >= 0.5f;
                            extra[Index(x, y, z)] = shadow ? FaceMaterial.Shadow : FaceMaterial.Skin;
                        }
                    }
                }
            }
        }

        for (var i = 0; i < cells.Length; i++)
        {
            if (extra[i] != FaceMaterial.Empty)
            {
                cells[i] = extra[i];
            }
        }

        ShadeRelief(cells, nx, ny, nz, sx, sy, sz, layout);
        return new FaceVolume(nx, ny, nz, cells);
    }

    /// <summary>陰にする、隣の柱との前後の差（参考図のセル）。</summary>
    private const float ShadeStep = 1f;

    /// <summary>
    /// 凹凸の陰（見た目だけ）: 鼻の脇・鼻の下のように、隣の柱が表面より <see cref="ShadeStep"/> セル以上前へ出ている皮膚の表のセルを影の皮膚にする。
    /// 正面から見た鼻筋が「鼻の穴の線」にしか見えないのを、左右の影で鼻筋の高さとして見せる。
    /// </summary>
    private static void ShadeRelief(FaceMaterial[] cells, int nx, int ny, int nz, float sx, float sy, float sz, FaceLayout layout)
    {
        var height = new int[nx * ny];
        for (var y = 0; y < ny; y++)
        {
            for (var x = 0; x < nx; x++)
            {
                height[x + (nx * y)] = -1;
                for (var z = nz - 1; z >= 0; z--)
                {
                    if (cells[x + (nx * (y + (ny * z)))] != FaceMaterial.Empty)
                    {
                        height[x + (nx * y)] = z;
                        break;
                    }
                }
            }
        }

        var step = (int)MathF.Max(1f, MathF.Round(ShadeStep / sz));
        var shade = new List<int>();
        for (var y = 0; y < ny; y++)
        {
            var ry = (ny - 1 - y + 0.5f) * sy;
            if (ry < layout.NoseRowFrom || ry > layout.NoseRowTo + 4f)
            {
                continue;
            }

            for (var x = 0; x < nx; x++)
            {
                var h = height[x + (nx * y)];
                var dx = MathF.Abs(((x + 0.5f) * sx) - Mid);
                if (h < 0 || dx > 3.5f)
                {
                    continue;
                }

                var left = x > 0 ? height[x - 1 + (nx * y)] : -1;
                var right = x < nx - 1 ? height[x + 1 + (nx * y)] : -1;
                if (left >= h + step || right >= h + step)
                {
                    shade.Add(x + (nx * (y + (ny * h))));
                }

                // 鼻の下: 1 つ下の柱が鼻先より引っ込んでいれば、その表。
                var lower = y > 0 ? height[x + (nx * (y - 1))] : -1;
                if (lower >= 0 && h >= lower + step + 1 && dx <= 4f)
                {
                    shade.Add(x + (nx * (y - 1 + (ny * lower))));
                }
            }
        }

        foreach (var i in shade)
        {
            if (cells[i] == FaceMaterial.Skin)
            {
                cells[i] = FaceMaterial.Shadow;
            }
        }
    }

    /// <summary>箱の膨張（チェビシェフ距離。軸ごとの半径はセル数。分離できるので 3 回の 1 次元）。</summary>
    private static bool[] Dilate(bool[] source, int nx, int ny, int nz, int rx, int ry, int rz)
    {
        var current = (bool[])source.Clone();
        (int Axis, int Radius)[] passes = [(0, rx), (1, ry), (2, rz)];
        foreach (var (axis, radius) in passes)
        {
            if (radius <= 0)
            {
                continue;
            }

            var next = new bool[current.Length];
            for (var z = 0; z < nz; z++)
            {
                for (var y = 0; y < ny; y++)
                {
                    for (var x = 0; x < nx; x++)
                    {
                        var (n, c) = axis == 0 ? (nx, x) : axis == 1 ? (ny, y) : (nz, z);
                        var here = x + (nx * (y + (ny * z)));
                        for (var k = Math.Max(0, c - radius); k <= Math.Min(n - 1, c + radius) && !next[here]; k++)
                        {
                            var (px, py, pz) = axis == 0 ? (k, y, z) : axis == 1 ? (x, k, z) : (x, y, k);
                            next[here] = current[px + (nx * (py + (ny * pz)))];
                        }
                    }
                }
            }

            current = next;
        }

        return current;
    }

    /// <summary>唇の左右に唇がある行・位置（= 口の隙間）。ひげはここを埋めない（唇が見えるように）。</summary>
    private static bool BetweenLips(IReadOnlyList<(FacePart Part, float Weight)> mouths, FaceParts p, float rx, float ry, FaceLayout layout)
    {
        if (mouths.Count == 0)
        {
            return false;
        }

        var left = false;
        var right = false;
        for (var d = 0f; d <= 4f; d += 1f)
        {
            left |= Lip(mouths, p, rx - d, ry, layout) is not null;
            right |= Lip(mouths, p, rx + d, ry, layout) is not null;
        }

        return left && right;
    }

    /// <summary>
    /// 眉の張り出しと鼻筋の高さ。横の輪郭の部品（browridge）の行ごとの前の端（頭の横の格子の前後の位置）まで、基準の顔の前の面（<see cref="FaceLayout.ProfileCap"/>）の前へ
    /// 皮膚を足す。幅は、眉の高さでは目の幅、鼻筋の高さでは鼻の幅へ連続に狭まる。<see cref="FaceParts.BrowRidge"/> は眉の高さの前の端へ足すセル数。
    /// 皮膚だけの見た目と当たりの形（強さは変えない）。
    /// </summary>
    private static void ApplyRelief(FaceMaterial[] cells, bool[] head, int nx, int ny, IReadOnlyList<(FacePart Part, float Weight)> ridges,
        FaceLayout layout, FaceParts p, float sx, float sy, float sz, int nz)
    {
        var (anchorZ, anchorRow) = layout.BridgeAnchor;
        for (var y = 0; y < ny; y++)
        {
            var ry = (ny - 1 - y + 0.5f) * sy;
            var brow = Bump(ry, layout.BrowLeftImage.Y, 1.0f);
            var edge = layout.ProfileCap + (p.BrowRidge * brow);
            foreach (var (part, weight) in ridges)
            {
                // 部品の格子（8x12、留める点 (4, 6) = 頭の横の格子の (anchorZ, anchorRow)）の、この行の最後の詰まった列の前の縁 → 頭の前後の位置。
                var local = ry - anchorRow + part.Anchor.Y;
                var last = -1;
                for (var cx = 0; cx < part.Front.Width; cx++)
                {
                    if (part.Front.Solid(cx + 0.5f, local) >= 0.5f)
                    {
                        last = cx;
                    }
                }

                var z = last < 0 ? 0f : last + 1 - part.Anchor.X + anchorZ;
                edge += weight * MathF.Max(0f, z - layout.ProfileCap);
            }

            if (edge <= layout.ProfileCap + 0.25f)
            {
                continue;
            }

            var noseHalf = 1.6f * p.NoseWidth;
            var half = noseHalf + ((6.5f - noseHalf) * (1f - Smooth(ry, layout.NoseTopRow - 2f, layout.NoseTopRow)));
            for (var x = 0; x < nx; x++)
            {
                var dx = MathF.Abs(((x + 0.5f) * sx) - Mid) / half;
                if (dx >= 1f)
                {
                    continue;
                }

                var reach = layout.ProfileCap + ((edge - layout.ProfileCap) * (1f - (dx * dx)));
                var column = x + (nx * y);
                var top = -1;
                for (var z = nz - 1; z >= 0; z--)
                {
                    if (cells[column + (nx * ny * z)] != FaceMaterial.Empty)
                    {
                        top = z;
                        break;
                    }
                }

                if (top < 0 || !head[column + (nx * ny * top)])
                {
                    continue;
                }

                for (var z = top + 1; z < nz && (z + 0.5f) * sz < reach; z++)
                {
                    cells[column + (nx * ny * z)] = FaceMaterial.Skin;
                }
            }
        }
    }

    /// <summary>眉・影などの色の部位: (rx, ry) の眉の材質（無ければ null）。</summary>
    private static FaceMaterial? Feature(IReadOnlyList<(FacePart Part, float Weight)> brows, FaceParts p, float rx, float ry, FaceLayout layout, bool brow)
    {
        if (brows.Count == 0)
        {
            return null;
        }

        var side = rx >= Mid ? 1f : -1f;
        var image = side > 0 ? layout.BrowRightImage : layout.BrowLeftImage;
        var cx = Mid + (side * MathF.Abs(image.X - Mid) * p.EyeSpacing);
        var cy = image.Y - ((p.BrowHeight + p.EyeHeight) * HalfHeight);
        var (o, up) = (side * (rx - cx), -(ry - cy));
        var (cos, sin) = (MathF.Cos(p.BrowTilt), MathF.Sin(p.BrowTilt));
        var a = ((o * cos) + (up * sin)) / p.EyeSize;
        var b = ((-o * sin) + (up * cos)) / p.BrowThickness;
        var coverage = 0f;
        foreach (var (part, weight) in brows)
        {
            coverage += weight * part.Front.Coverage(part.Anchor.X - a, part.Anchor.Y - b, FaceMaterial.Brow);
        }

        return coverage >= PaintThreshold ? FaceMaterial.Brow : null;
    }

    private static FaceMaterial? Lip(IReadOnlyList<(FacePart Part, float Weight)> mouths, FaceParts p, float rx, float ry, FaceLayout layout)
    {
        var cy = layout.Mouth.Y - (p.MouthHeight * HalfHeight);
        var a = (rx - layout.Mouth.X) / p.MouthWidth;
        var b = -(ry - cy) / p.Lips;
        var coverage = 0f;
        foreach (var (part, weight) in mouths)
        {
            coverage += weight * part.Front.Coverage(part.Anchor.X + a, part.Anchor.Y - b, FaceMaterial.Lip);
        }

        return coverage >= PaintThreshold ? FaceMaterial.Lip : null;
    }

    /// <summary>目（白目・黒目・影）。<paramref name="depth"/> が 1 未満の表面は部品のとおり、その奥の 1 セルは黒目を白目にする（白目の体積を持たせる）。</summary>
    private static FaceMaterial? Eye(IReadOnlyList<(FacePart Part, float Weight)> eyes, FaceParts p, float rx, float ry, FaceLayout layout, float depth)
    {
        if (eyes.Count == 0)
        {
            return null;
        }

        var side = rx >= Mid ? 1f : -1f;
        var image = side > 0 ? layout.EyeRightImage : layout.EyeLeftImage;
        var cx = Mid + (side * MathF.Abs(image.X - Mid) * p.EyeSpacing);
        var cy = image.Y - (p.EyeHeight * HalfHeight);
        var (o, up) = (side * (rx - cx), -(ry - cy));
        var (cos, sin) = (MathF.Cos(p.EyeTilt), MathF.Sin(p.EyeTilt));
        var a = ((o * cos) + (up * sin)) / p.EyeSize;
        var b = ((-o * sin) + (up * cos)) / p.EyeSize;
        Span<float> cover = stackalloc float[4];
        foreach (var (part, weight) in eyes)
        {
            var (px, py) = (part.Anchor.X - a, part.Anchor.Y - b);
            cover[0] += weight * part.Front.Coverage(px, py, FaceMaterial.White);
            cover[1] += weight * part.Front.Coverage(px, py, FaceMaterial.Iris);
            cover[2] += weight * part.Front.Coverage(px, py, FaceMaterial.Shadow);
        }

        var eye = cover[0] + cover[1];
        if (depth >= PaintBand)
        {
            return eye >= PaintThreshold ? FaceMaterial.White : null;
        }

        if (cover[2] >= PaintThreshold && eye < PaintThreshold)
        {
            return FaceMaterial.Shadow;
        }

        return eye < PaintThreshold ? null : cover[1] >= cover[0] ? FaceMaterial.Iris : FaceMaterial.White;
    }

    /// <summary>そばかす: 頬と鼻の上の皮膚の表面に、座標のハッシュで点を散らす（密度 = <see cref="FaceParts.Freckles"/>）。</summary>
    private static FaceMaterial? Freckle(FaceParts p, int x, int y, float rx, float ry, FaceMaterial current)
    {
        if (p.Freckles <= 0f || current != FaceMaterial.Skin)
        {
            return null;
        }

        var zone = Bump(ry, 15f, 2.6f) * Smooth(MathF.Abs(rx - Mid), 0f, 1f) * (1f - Smooth(MathF.Abs(rx - Mid), 7f, 9f));
        var h = unchecked((uint)((x * 73856093) ^ (y * 19349663)));
        h = unchecked(h * 2654435761u);
        var u = ((h >> 8) & 0xFFFF) / 65535f;
        return u < p.Freckles * 0.18f * zone ? FaceMaterial.Freckle : null;
    }
}
