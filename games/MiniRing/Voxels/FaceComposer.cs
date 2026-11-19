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
        var cells = new FaceMaterial[nx * ny * nz];
        for (var z = 0; z < nz; z++)
        {
            var rz = RefZ(z);
            for (var y = 0; y < ny; y++)
            {
                var ry = RefY(y);
                var lower = Smooth(ry, 13f, 21f);
                var chin = Smooth(ry, 17f, 23f);
                var cheek = Bump(ry, 15.5f, 2.5f);
                var hairRy = ry - (p.Hairline * HalfHeight);
                for (var x = 0; x < nx; x++)
                {
                    var rx = RefX(x);
                    var wrx = Mid + ((rx - Mid) / ((1f + ((p.JawWidth - 1f) * lower)) * (1f + ((p.Cheekbones - 1f) * 0.08f * cheek)) * p.FaceWidth));
                    var wrz = rz - ((p.Jaw - 1f) * 6f * chin);
                    var inNoseRows = ry >= layout.NoseRowFrom && ry < layout.NoseRowTo + 1f;
                    var front = Blend(jaws, j => j.Front.Solid(wrx, ry));
                    var side = inNoseRows && wrz > layout.ProfileCap ? 0f : Blend(jaws, j => j.Side!.Solid(wrz, ry));
                    var topPx = library.BaseTop.Solid(wrx, FacePartLibrary.RefDepth - wrz) - library.BaseTop.Coverage(wrx, FacePartLibrary.RefDepth - wrz, FaceMaterial.Shadow);
                    var inside = front >= 0.5f && side >= 0.5f && topPx >= 0.5f;
                    var hf = Blend(hairs, h => h.Front.Coverage(rx, hairRy, FaceMaterial.Hair));
                    var hs = Blend(hairs, h => h.Side!.Coverage(rz, hairRy, FaceMaterial.Hair));
                    var hairCell = hf >= 0.5f && hs >= 0.5f && (inside || Blend(hairs, h => h.Top!.Coverage(rx, FacePartLibrary.RefDepth - rz, FaceMaterial.Hair)) >= 0.5f);
                    head[Index(x, y, z)] = inside;
                    if (inside || hairCell)
                    {
                        cells[Index(x, y, z)] = hairCell ? FaceMaterial.Hair : FaceMaterial.Skin;
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
                            && Blend(beards, b => b.Front.Coverage(RefX(x), RefY(y), FaceMaterial.Hair)) >= 0.5f
                            && Blend(beards, b => b.Side!.Coverage(RefZ(z), RefY(y), FaceMaterial.Hair)) >= 0.5f)
                        {
                            cells[i] = FaceMaterial.Hair;
                        }
                    }
                }
            }
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

        // 眉の張り出し（BrowRidge）: 眉の高さの表面の 1 セル前へ、皮膚を足す（眼窩の上の骨の出っ張り。顔の形であって強さではない）。
        // 値 0..2 を、前へ出すセル数（参考図のセル）にする。
        if (p.BrowRidge > 0f)
        {
            RaiseBrowRidge(cells, head, nx, ny, nz, sy, sz, layout, p);
        }

        return new FaceVolume(nx, ny, nz, cells);
    }

    private static void RaiseBrowRidge(FaceMaterial[] cells, bool[] head, int nx, int ny, int nz, float sy, float sz, FaceLayout layout, FaceParts p)
    {
        var rows = (int)MathF.Round(p.BrowRidge / sz);
        var browRow = layout.BrowLeftImage.Y - ((p.BrowHeight + p.EyeHeight) * HalfHeight);
        for (var y = 0; y < ny; y++)
        {
            var ry = (ny - 1 - y + 0.5f) * sy;
            if (MathF.Abs(ry - browRow) > 1.2f)
            {
                continue;
            }

            for (var x = 0; x < nx; x++)
            {
                var zTop = -1;
                for (var z = nz - 1; z >= 0; z--)
                {
                    if (cells[x + (nx * (y + (ny * z)))] != FaceMaterial.Empty)
                    {
                        zTop = z;
                        break;
                    }
                }

                if (zTop < 0 || !head[x + (nx * (y + (ny * zTop)))])
                {
                    continue;
                }

                for (var k = 1; k <= rows && zTop + k < nz; k++)
                {
                    cells[x + (nx * (y + (ny * (zTop + k))))] = FaceMaterial.Skin;
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
