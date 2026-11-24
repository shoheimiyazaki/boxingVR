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

    /// <summary>眉を塗る閾値。眉は 1 つの部品で描く（混ぜない）ので、双一次の標本化で太らない 0.5（0.4 だと太い眉が 3 行になった）。</summary>
    private const float BrowThreshold = 0.5f;

    private static float Smooth(float v, float a, float b)
    {
        var t = Math.Clamp((v - a) / (b - a), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    /// <summary>生え際のずらしが頭頂（<see cref="FaceShape.CrownRow"/>）から何行で全部になるか（額の生え際は 8〜10 行目）。</summary>
    private const float HairlineTaperRows = 5f;

    /// <summary>ひげの厚み（顔の表面から何セル（参考図）の深さまで）。</summary>
    private const float BeardDepth = 3f;

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

        // 1. 頭の輪郭（少ない制御値の式: FaceShape）と髪。
        var shape = FaceShape.From(p, jaws);
        var head = new bool[nx * ny * nz];
        var hairCand = new bool[nx * ny * nz];
        var cells = new FaceMaterial[nx * ny * nz];
        for (var z = 0; z < nz; z++)
        {
            var rz = RefZ(z);
            for (var y = 0; y < ny; y++)
            {
                var ry = RefY(y);
                // 生え際の値は額の生え際の所だけ絵をずらし、頭頂（CrownRow）へ向かってずらしを 0 にする（頭頂は自分の行の絵のまま。2026-11-24:
                // 前は絵全体をずらしたので、髪を下げると頭頂の前の方が絵の外になって地肌の点が出た）。生え際 0 では前と同じ。
                var hairRow = ry - (p.Hairline * HalfHeight * Smooth(ry, FaceShape.CrownRow, FaceShape.CrownRow + HairlineTaperRows));
                for (var x = 0; x < nx; x++)
                {
                    var rx = RefX(x);
                    var inside = shape.Inside(rx, ry, rz);
                    var hx = Mid + ((rx - Mid) / p.FaceWidth);
                    // 頭の上の丸み（CrownRow より上）は髪の部品の絵の頭より広いので、頭の中ならその下の行（CrownRow）の髪の絵を使う（頭頂に地肌の点が出ない）。
                    var hairRy = inside && ry < FaceShape.CrownRow ? FaceShape.CrownRow + 0.5f : hairRow;
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

        var halo = frizz <= 0f ? onHead : Dilate(onHead, nx, ny, nz, (int)MathF.Ceiling(1f / sx), (int)MathF.Ceiling(1f / sy), (int)MathF.Ceiling(1f / sz));
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
                    else if (near[i] && (hairCand[i] || (halo[i] && (headFront < 0 || z <= headFront) && CellNoise(x, y, z) < frizz * 0.7f)))
                    {
                        cells[i] = FaceMaterial.Hair;
                    }
                }
            }
        }

        // ひげ（頭の内側で、正面の図がひげの所の、顔の表面から 3 セル以内の殻。あごの形（横の輪郭）が変わっても顎・頬に沿う）。
        // 密度（Density）が 0.5 未満の部品は無精ひげ: 散らばった髪のセルではなく、ひげの範囲の皮膚の色を髪へ寄せる材質（Stubble）にする。
        if (beards.Count > 0)
        {
            var headTop = new int[nx * ny];
            for (var y = 0; y < ny; y++)
            {
                for (var x = 0; x < nx; x++)
                {
                    headTop[x + (nx * y)] = -1;
                    for (var z = nz - 1; z >= 0; z--)
                    {
                        if (head[Index(x, y, z)])
                        {
                            headTop[x + (nx * y)] = z;
                            break;
                        }
                    }
                }
            }

            var beardMaterial = Blend(beards, b => b.Density) >= 0.5f ? FaceMaterial.Hair : FaceMaterial.Stubble;
            for (var z = 0; z < nz; z++)
            {
                for (var y = 0; y < ny; y++)
                {
                    for (var x = 0; x < nx; x++)
                    {
                        var i = Index(x, y, z);
                        if (head[i] && cells[i] == FaceMaterial.Skin
                            && Blend(beards, b => b.Front.Coverage(WidthX(RefX(x), RefY(y)), RefY(y), FaceMaterial.Hair)) >= 0.5f
                            && (headTop[x + (nx * y)] - z) * sz < BeardDepth
                            && !BetweenLips(mouths, p, RefX(x), RefY(y), layout))
                        {
                            cells[i] = beardMaterial;
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

        var hairFront = new bool[nx * ny];
        for (var i = 0; i < hairFront.Length; i++)
        {
            hairFront[i] = top[i] >= 0 && cells[i + (nx * ny * top[i])] == FaceMaterial.Hair;
        }

        var browMask = BrowMask(brows, eyes, p, layout, nx, ny, RefX, RefY, hairFront);
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
                        m = (browMask[x + (nx * y)] ? FaceMaterial.Brow : (FaceMaterial?)null) ?? Lip(mouths, p, rx, ry, layout) ?? Freckle(p, x, y, rx, ry, cells[i]);
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

        // 3. 鼻（単純な形: NoseShape）と耳（頭の横の外の 2 セルに、横 × 正面の厚み）を足す。
        var extra = new FaceMaterial[cells.Length];
        if (noses.Count > 0)
        {
            var nose = NoseShape.From(noses, p, layout);
            foreach (var (x, y, depth, material) in nose.Cells(nx, ny, RefX, RefY))
            {
                var surface = top[x + (nx * y)];
                var z = surface + depth;
                if (surface >= 0 && z < nz)
                {
                    extra[Index(x, y, z)] = material;
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

        return new FaceVolume(nx, ny, nz, cells);
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
    /// 眉を置く正面の印（x + nx * y）。部品の形を写したあと、(1) 斜めにだけ接する所を埋めて 4 近傍でつなぎ、左右それぞれ最大のつながりだけを残す
    /// （途切れた眉は低い解像度で汚れに見える）。(2) 目（白目・黒目）と 1 マス以上離れるまで、その側の眉を上へずらす（眉の高さの値や目の大きさで
    /// 眉が目に触れると、まぶたの影のように見えた）。髪（前髪・生え際）からも 1 マス離す（髪と同じ暗い色に埋もれる）。両方から離しきれないときは、
    /// 触れる行を削る。ユーザーの指示（2026-11-20）。
    /// </summary>
    private static bool[] BrowMask(IReadOnlyList<(FacePart Part, float Weight)> brows, IReadOnlyList<(FacePart Part, float Weight)> eyes, FaceParts p,
        FaceLayout layout, int nx, int ny, Func<int, float> refX, Func<int, float> refY, bool[] hairFront)
    {
        var mask = new bool[nx * ny];
        var eye = new bool[nx * ny];
        for (var y = 0; y < ny; y++)
        {
            for (var x = 0; x < nx; x++)
            {
                mask[x + (nx * y)] = Feature(brows, p, refX(x), refY(y), layout, brow: true) is not null;
                eye[x + (nx * y)] = Eye(eyes, p, refX(x), refY(y), layout, 0f) is FaceMaterial.White or FaceMaterial.Iris;
            }
        }

        bool At(bool[] m, int x, int y) => x >= 0 && y >= 0 && x < nx && y < ny && m[x + (nx * y)];
        var result = new bool[nx * ny];
        foreach (var (from, to) in new[] { (0, nx / 2), (nx / 2, nx) })
        {
            var side = new bool[nx * ny];
            for (var y = 0; y < ny; y++)
            {
                for (var x = from; x < to; x++)
                {
                    side[x + (nx * y)] = mask[x + (nx * y)];
                }
            }

            // (1) 斜めだけで接する 2 つのセルの間を、横の 1 セルで埋める。
            for (var y = 0; y + 1 < ny; y++)
            {
                for (var x = from; x + 1 < to; x++)
                {
                    if (At(side, x, y) && At(side, x + 1, y + 1) && !At(side, x + 1, y) && !At(side, x, y + 1))
                    {
                        side[x + 1 + (nx * y)] = true;
                    }

                    if (At(side, x + 1, y) && At(side, x, y + 1) && !At(side, x, y) && !At(side, x + 1, y + 1))
                    {
                        side[x + (nx * y)] = true;
                    }
                }
            }

            var keep = Largest(side, nx, ny);

            // (2) 目と髪から 1 マス以上（8 近傍で）離す: 上へ 0〜4 マスずらした中で、目か髪に触れて削るセルがいちばん少ない位置（同じなら低い方）を選び、
            // 触れるセルを削って最大のつながりだけを残す。
            bool Near(bool[] m, int x, int y)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (At(m, x + dx, y + dy))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            bool[] Trimmed(int s)
            {
                var t = (bool[])keep.Clone();
                for (var i = 0; i < t.Length; i++)
                {
                    var (x, y) = (i % nx, (i / nx) + s);
                    t[i] = t[i] && y < ny && !Near(eye, x, y) && !Near(hairFront, x, y);
                }

                return t;
            }

            var (shift, lost) = (0, int.MaxValue);
            for (var s = 0; s <= 4; s++)
            {
                var n = keep.Count(c => c) - Trimmed(s).Count(c => c);
                if (n < lost)
                {
                    (shift, lost) = (s, n);
                }
            }

            keep = Largest(Trimmed(shift), nx, ny);

            for (var i = 0; i < keep.Length; i++)
            {
                var y = (i / nx) + shift;
                if (keep[i] && y < ny)
                {
                    result[(i % nx) + (nx * y)] = true;
                }
            }
        }

        return result;
    }

    /// <summary>正面の印のうち、4 近傍でつながった最大のかたまりだけ。</summary>
    private static bool[] Largest(bool[] mask, int nx, int ny)
    {
        var label = new int[mask.Length];
        var best = (Label: 0, Size: 0);
        var next = 0;
        var stack = new Stack<int>();
        for (var i = 0; i < mask.Length; i++)
        {
            if (!mask[i] || label[i] != 0)
            {
                continue;
            }

            next++;
            var size = 0;
            label[i] = next;
            stack.Push(i);
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                size++;
                var (x, y) = (c % nx, c / nx);
                foreach (var (ax, ay) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
                {
                    var j = ax + (nx * ay);
                    if (ax >= 0 && ay >= 0 && ax < nx && ay < ny && mask[j] && label[j] == 0)
                    {
                        label[j] = next;
                        stack.Push(j);
                    }
                }
            }

            best = size > best.Size ? (next, size) : best;
        }

        var result = new bool[mask.Length];
        for (var i = 0; i < mask.Length; i++)
        {
            result[i] = best.Label != 0 && label[i] == best.Label;
        }

        return result;
    }

    /// <summary>眉・影などの色の部位: (rx, ry) の眉の材質（無ければ null）。</summary>
    private static FaceMaterial? Feature(IReadOnlyList<(FacePart Part, float Weight)> brows, FaceParts p, float rx, float ry, FaceLayout layout, bool brow)
    {
        if (brows.Count == 0)
        {
            return null;
        }

        if (MathF.Abs(rx - Mid) < 1f)
        {
            return null; // 眉と眉の間（眉間）は空ける
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

        return coverage >= BrowThreshold ? FaceMaterial.Brow : null;
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

        // 目は部品の絵のセルをそのまま写す（白目 3x2・3x3、黒目 2x2・1x1: ユーザーの指示、2026-11-20）。大きさ（EyeSize）・傾き（EyeTilt）で
        // 引き伸ばすと 3x2 が 3x3・4 幅に崩れるので掛けず、目の中心は部品の留める点の端数に合わせてセルの格子へそろえる（左右は鏡写し）。
        var side = rx >= Mid ? 1f : -1f;
        var anchor = eyes[0].Part.Anchor;
        static float Snap(float v, float frac) => MathF.Round(v - frac) + frac;
        var (fx, fy) = (anchor.X - MathF.Floor(anchor.X), anchor.Y - MathF.Floor(anchor.Y));
        var left = Snap(Mid - (MathF.Abs(layout.EyeLeftImage.X - Mid) * p.EyeSpacing), fx);
        var cx = side > 0 ? (2f * Mid) - left : left;
        var cy = Snap(layout.EyeLeftImage.Y - (p.EyeHeight * HalfHeight), fy);
        var (a, b) = (side * (rx - cx), -(ry - cy));
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
