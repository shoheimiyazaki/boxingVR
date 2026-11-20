namespace MiniRing.Voxels;

/// <summary>
/// 頭の形を、少ない値で決まる 2 つの塊の距離の場（SDF）で決める（2026-11-20、ユーザーの指示。部品の絵の視体積だと顔の線に変な凸凹が出た。
/// 調べたデッサンの方法（Loomis の「側面を切った球 ＋ あごの塊」、Asaro の頭の面）と、距離の場のなめらかな和（Inigo Quilez の smin）に基づく）。
/// 座標は参考図の格子（x = 0..20 の左右、y = 上から下へ 0..28、z = 後ろから前へ 0..20）。セルの中心で距離を測り、0 以下を頭の中とする。
/// <para><b>頭蓋</b> <see cref="Cranium"/>: 横の断面の指数で側面を平らにした超楕円体（Loomis の側面を切った球）。幅 = <see cref="CraniumHalf"/>。</para>
/// <para><b>顔の塊</b> <see cref="Face"/>: 前から見て台形（頬骨の幅 <see cref="CheekHalf"/> → あごの角の幅 <see cref="JawHalf"/> → あご先）、横から見てくさび
/// （前の面は目・口の深さを基準に、額・頬骨・あごの深さの値で動く。下の縁はあご先からあごの角へ斜めに上がる下顎）。前の面と横の面の角は面取りする
/// （Asaro の頬骨・あごの角）。</para>
/// <para>2 つを <see cref="Blend"/> セルの幅でなめらかにつなぐ（こめかみ・頬の後ろが自然につながる。くびれや段差が出ない）。</para>
/// <para>値の出どころ: 頭の幅・あごの幅・あご先の下端はあごの部品（jaw.*）の正面の絵から測って重みで混ぜ、<see cref="FaceParts.FaceWidth"/>・
/// <see cref="FaceParts.JawWidth"/> を掛ける。頬骨の幅と前の面の平たさは <see cref="FaceParts.Cheekbones"/>、眉の張り出しは <see cref="FaceParts.BrowRidge"/>、
/// あご先の前後は <see cref="FaceParts.Jaw"/>。どれも連続の値で、種類の場合分けは無い。見た目と当たりの形だけで、強さは変えない（§1.7）。</para>
/// </summary>
public sealed record FaceShape(float CraniumHalf, float CheekHalf, float JawHalf, float ChinBottom, float Brow, float Cheek, float Chin)
{
    /// <summary>真ん中の列（参考図の幅の半分）。</summary>
    public const float Mid = FacePartLibrary.RefWidth / 2f;

    /// <summary>頭頂の丸みの下の端の行（髪の部品の絵の頭の上の幅が頭に届く行。これより上の髪は、この行の絵で塗る）。</summary>
    public const float CrownRow = 4f;

    /// <summary>頭蓋の中心の行と、上下の半径（頭頂が行 0.5、下の端が行 20.5: 耳の下まで）。</summary>
    public const float CraniumCenterRow = 10.5f;

    public const float CraniumHalfHeight = 10f;

    /// <summary>頭蓋の前後の中心と半径（後頭が z 0.5、額が z 16.5: 土台の横の絵）。</summary>
    public const float CraniumCenterZ = 8.5f;

    public const float CraniumHalfDepth = 8f;

    /// <summary>頭蓋の横の断面の指数（1 = 楕円、小さいほど箱。0.75 で側面が平ら: 調べた頭蓋の超楕円体の当てはめの中ほど）。</summary>
    public const float SideFlatness = 0.75f;

    /// <summary>頬骨の高さ（顔の塊がいちばん広い行）と、あごの角の行。</summary>
    public const float CheekRow = 15f;

    public const float JawRow = 19.5f;

    /// <summary>あご先の幅のあごの幅に対する比（調べた比の範囲 0.30〜0.60 の中ほどより細め）。</summary>
    public const float ChinRatio = 0.42f;

    /// <summary>目の面・口の面の深さ（前の面の z。土台の横の絵の目の行・口の行の前の端）。</summary>
    public const float EyeDepth = 17f;

    public const float MouthDepth = 17.5f;

    /// <summary>顔の塊の後ろの面（耳の前で止まる。耳は z 7 あたり）。</summary>
    public const float FaceBack = 6f;

    /// <summary>顔の前の面の真ん中が横の端よりどれだけ前か（顔の平たさ、セル）。頬骨が出ているほど平ら。</summary>
    public const float Flatness = 2.5f;

    /// <summary>前の面と横の面の角の面取り（セル）。</summary>
    public const float Chamfer = 1.5f;

    /// <summary>頭蓋と顔の塊をつなぐ幅（smin の k、セル）。</summary>
    public const float Blend = 2f;

    /// <summary>頭の半幅の上限（格子の幅 20 の内側に耳の 1 セルを残す）。</summary>
    public const float MaxHalf = 9f;

    /// <summary>既定の顔（あごの部品が無く、値がすべて既定）: 土台の頭に合わせた値。</summary>
    public static FaceShape Default { get; } = new(8f, 7.5f, 6.5f, 26f, 0f, 1f, 1f);

    /// <summary>顔の値 <paramref name="p"/> と、あごの部品の重み <paramref name="jaws"/> から決める。</summary>
    public static FaceShape From(FaceParts p, IReadOnlyList<(FacePart Part, float Weight)> jaws)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(jaws);
        float head = Default.CraniumHalf, jaw = Default.JawHalf, bottom = Default.ChinBottom;
        if (jaws.Count > 0)
        {
            (head, jaw, bottom) = (0f, 0f, 0f);
            foreach (var (part, weight) in jaws)
            {
                var m = Measure(part.Front);
                head += weight * m.Head;
                jaw += weight * m.Jaw;
                bottom += weight * m.Bottom;
            }
        }

        var cranium = MathF.Min(MaxHalf, head * p.FaceWidth);
        // 頬骨の幅: 頭の幅の 0.94 倍を基準に、頬骨の値で ±（0.08 × 差）。
        var cheek = MathF.Min(cranium, cranium * 0.94f * (1f + (0.08f * (p.Cheekbones - 1f))));
        var jawHalf = MathF.Min(cheek, jaw * p.FaceWidth * p.JawWidth);
        return new FaceShape(cranium, cheek, jawHalf, Math.Clamp(bottom, 22f, FacePartLibrary.RefHeight - 1f), p.BrowRidge, p.Cheekbones, p.Jaw);
    }

    /// <summary>あごの部品の正面の絵から、頭の半幅（額〜目の行の最大）・あごの半幅（あごの角の 3 行の平均）・あご先の下端（最後の詰まった行の下の縁）を測る。</summary>
    public static (float Head, float Jaw, float Bottom) Measure(FaceRaster front)
    {
        ArgumentNullException.ThrowIfNull(front);
        float Half(int row)
        {
            int lo = -1, hi = -1;
            for (var x = 0; x < front.Width; x++)
            {
                if (front.Solid(x + 0.5f, row + 0.5f) >= 0.5f)
                {
                    lo = lo < 0 ? x : lo;
                    hi = x;
                }
            }

            return lo < 0 ? 0f : (hi + 1 - lo) / 2f;
        }

        var head = 0f;
        for (var row = (int)CrownRow; row <= 12; row++)
        {
            head = MathF.Max(head, Half(row));
        }

        var jaw = (Half(17) + Half(18) + Half(19)) / 3f;
        var bottom = 0f;
        for (var row = 0; row < front.Height; row++)
        {
            bottom = Half(row) > 0f ? row + 1f : bottom;
        }

        return (head, jaw, bottom);
    }

    /// <summary>参考図の格子の点が頭の中か（距離が 0 以下）。</summary>
    public bool Inside(float x, float y, float z) => Distance(x, y, z) <= 0f;

    /// <summary>頭の表面までの符号付きの距離のおおよそ（中が負、セル）。頭蓋と顔の塊のなめらかな和。</summary>
    public float Distance(float x, float y, float z) => SmoothMin(Cranium(x, y, z), Face(x, y, z), Blend);

    /// <summary>頭蓋（側面を平らにした超楕円体）の距離のおおよそ（陰関数の値を、いちばん小さい半径でセルの長さへ直したもの）。</summary>
    public float Cranium(float x, float y, float z)
    {
        var u = MathF.Abs(x - Mid) / CraniumHalf;
        var w = MathF.Abs(z - CraniumCenterZ) / CraniumHalfDepth;
        var v = MathF.Abs(y - CraniumCenterRow) / CraniumHalfHeight;
        var e = 2f / SideFlatness;
        var horizontal = MathF.Pow(MathF.Pow(u, e) + MathF.Pow(w, e), SideFlatness / 2f);
        var f = MathF.Sqrt((horizontal * horizontal) + (v * v));
        return (f - 1f) * MathF.Min(CraniumHalf, CraniumHalfDepth);
    }

    /// <summary>行 <paramref name="y"/> の顔の塊の半幅（頬骨 → あごの角は傾き 0 で始まり 0 で終わる 3 次、あごの角 → あご先は放物線）。</summary>
    public float FaceHalf(float y)
    {
        if (y < JawRow)
        {
            var t = Math.Clamp((y - CheekRow) / (JawRow - CheekRow), 0f, 1f);
            return CheekHalf + ((JawHalf - CheekHalf) * t * t * (3f - (2f * t)));
        }

        var c = Math.Clamp((y - JawRow) / (ChinBottom - JawRow), 0f, 1f);
        var tip = JawHalf * ChinRatio;
        return tip + ((JawHalf - tip) * (1f - (c * c)));
    }

    /// <summary>顔の前の面の真ん中の縦の線（z）。眉・目・頬（鼻の脇）・口・あご先を単調な 3 次の補間でつなぐ（行き過ぎがない）。</summary>
    public float FrontMid(float y)
    {
        var chin = MouthDepth - 0.5f + ((Chin - 1f) * 6f);
        float[] ys = [9f, 11.5f, 13.5f, 17.5f, 21.5f, 24f, ChinBottom];
        float[] zs = [EyeDepth - 0.5f + (0.5f * Brow), EyeDepth + (0.75f * Brow), EyeDepth, MouthDepth, MouthDepth, chin, chin - 2.5f];
        return Pchip(ys, zs, y);
    }

    /// <summary>
    /// 顔の塊（前から台形・横からくさびの箱）の距離のおおよそ: 横・前・後ろ・上・下の面と、前と横の角の面取りの面の、外へ向かういちばん大きな距離。
    /// 前の面は真ん中が <see cref="Flatness"/>（頬骨が出ているほど小さい）だけ前へ出る丸み。下の面は、あご先の下端からあごの角へ斜めに上がる。
    /// </summary>
    public float Face(float x, float y, float z)
    {
        var half = FaceHalf(y);
        var dx = MathF.Abs(x - Mid);
        var t = Math.Clamp(dx / MathF.Max(half, 0.5f), 0f, 1f);
        var flat = Flatness * Math.Clamp(1f - (0.5f * (Cheek - 1f)), 0.3f, 1.6f);
        // 真ん中は平ら、横の端で丸く下がる（t⁴）: t² だと目の位置で 1 セル下がり、目の間が台のように残った。
        var front = FrontMid(y) - (flat * t * t * t * t);
        // 下顎の下の縁: 前（z ≥ 14）はあご先の下端、後ろ（顔の後ろの面）ではあごの角の行。
        var under = JawRow + ((ChinBottom - JawRow) * Math.Clamp((z - FaceBack) / (14f - FaceBack), 0f, 1f));
        var side = dx - half;
        var forward = z - front;
        var chamfer = (side + forward + Chamfer) * 0.7071f;
        return MathF.Max(MathF.Max(MathF.Max(side, forward), MathF.Max(FaceBack - z, chamfer)), MathF.Max(y - under, 9f - y));
    }

    /// <summary>なめらかな最小（多項式の smin、幅 <paramref name="k"/>）: 2 つの距離の場を、角の代わりに丸みでつなぐ。</summary>
    private static float SmoothMin(float a, float b, float k)
    {
        var h = MathF.Max(k - MathF.Abs(a - b), 0f) / k;
        return MathF.Min(a, b) - (h * h * k * 0.25f);
    }

    /// <summary>単調な 3 次のエルミート補間（Fritsch–Carlson）: 制御点の間で行き過ぎない。範囲の外は端の値。</summary>
    private static float Pchip(float[] xs, float[] ys, float x)
    {
        var n = xs.Length;
        if (x <= xs[0])
        {
            return ys[0];
        }

        if (x >= xs[n - 1])
        {
            return ys[n - 1];
        }

        var k = 0;
        while (x > xs[k + 1])
        {
            k++;
        }

        float Slope(int i) => (ys[i + 1] - ys[i]) / (xs[i + 1] - xs[i]);
        float Tangent(int i)
        {
            if (i == 0)
            {
                return Slope(0);
            }

            if (i == n - 1)
            {
                return Slope(n - 2);
            }

            var (a, b) = (Slope(i - 1), Slope(i));
            if (a * b <= 0f)
            {
                return 0f;
            }

            var (wa, wb) = ((2f * (xs[i + 1] - xs[i])) + (xs[i] - xs[i - 1]), (xs[i + 1] - xs[i]) + (2f * (xs[i] - xs[i - 1])));
            return (wa + wb) / ((wa / a) + (wb / b));
        }

        var h = xs[k + 1] - xs[k];
        var s = (x - xs[k]) / h;
        var (h00, h10, h01, h11) = ((2f * s * s * s) - (3f * s * s) + 1f, (s * s * s) - (2f * s * s) + s, (-2f * s * s * s) + (3f * s * s), (s * s * s) - (s * s));
        return (h00 * ys[k]) + (h10 * h * Tangent(k)) + (h01 * ys[k + 1]) + (h11 * h * Tangent(k + 1));
    }
}
