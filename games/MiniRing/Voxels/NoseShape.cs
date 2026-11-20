namespace MiniRing.Voxels;

/// <summary>
/// 鼻を単純な形で描く（2026-11-20、ユーザーの指示: 鼻の穴が 1 本につながって見える・鼻を単純な幾何で）。デッサンの鼻の組み立て
/// （鼻すじの三角柱 ＋ 鼻先と小鼻の塊 ＋ 下の面の鼻柱と 2 つの穴）を、セルの単位で書く。座標は参考図の格子（y は上から下）。
/// <list type="bullet">
/// <item>鼻すじ: 幅 2 セル、上（鼻根 <see cref="TopRow"/>）から鼻先へ、前への高さが直線で増える（三角柱を横から見たくさび）。</item>
/// <item>鼻先の行: 鼻先の幅（<see cref="TipHalf"/> の 2 倍）で、真ん中 2 セルがいちばん前（<see cref="Projection"/>）、小鼻は 1 セル低い。</item>
/// <item>底の行: 鼻先と同じ幅で前へ 1 セル。真ん中 2 セルは鼻柱（皮膚）、その外の 1 セルずつが鼻の穴: 2 つの穴の間に必ず皮膚がある。</item>
/// </list>
/// 長さ・高さ・幅は、名簿の鼻の部品（nose.*）の絵から測った値（正面のいちばん広い行の半幅・横のいちばん前の列・行の数）を重みで混ぜ、
/// <see cref="FaceParts.NoseLength"/>・<see cref="FaceParts.Nose"/>・<see cref="FaceParts.NoseWidth"/> を掛けてセルへ丸める。種類の場合分けは無い。
/// </summary>
/// <param name="Top">鼻根の行（参考図、上から）。</param>
/// <param name="Length">鼻根から底の行までの行の数（底の行を含む）。</param>
/// <param name="Projection">鼻先の、顔の面からの高さ（セル）。</param>
/// <param name="TipHalf">鼻先・小鼻の半幅（セル、2 か 3: 穴を左右に 1 つずつ、鼻柱を真ん中の 2 セルに置ける幅）。</param>
public sealed record NoseShape(float Top, int Length, int Projection, int TipHalf)
{
    /// <summary>鼻根を目の中心の行からこの行数だけ下げる（目の中心から始めると、目のすぐ下から前へ出て、目の間の出っ張りに見えた）。</summary>
    public const float Drop = 1f;

    /// <summary>鼻根の行（目の中心の行 + <see cref="Drop"/>）。</summary>
    public static float TopRow(FaceLayout layout) => layout.NoseTopRow + Drop;

    /// <summary>鼻の部品の重みと顔の値から決める。</summary>
    public static NoseShape From(IReadOnlyList<(FacePart Part, float Weight)> noses, FaceParts p, FaceLayout layout)
    {
        ArgumentNullException.ThrowIfNull(noses);
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(layout);
        float half = 0f, reach = 0f, rows = 0f;
        foreach (var (part, weight) in noses)
        {
            var m = Measure(part, layout);
            half += weight * m.Half;
            reach += weight * m.Reach;
            rows += weight * m.Rows;
        }

        // 底の行は口の 1 行上まで（長い鼻が口を覆わない）。
        var top = TopRow(layout);
        var room = (int)MathF.Floor(layout.Mouth.Y - (p.MouthHeight * FacePartLibrary.RefHeight / 2f) - 1f - top);
        var length = Math.Clamp((int)MathF.Round(rows * p.NoseLength), 3, Math.Max(3, room));
        var projection = Math.Clamp((int)MathF.Round(reach * p.Nose), 1, 3);
        var tip = Math.Clamp((int)MathF.Round(half * p.NoseWidth), 2, 3);
        return new NoseShape(top, length, projection, tip);
    }

    /// <summary>
    /// 鼻の部品の絵から測る: 正面のいちばん広い行の半幅、横の絵のいちばん前の列が顔の面（<see cref="FaceShape.EyeDepth"/>）からいくつ前か、
    /// 鼻根から底（正面の最後の詰まった行）までの行の数（鼻根を <see cref="Drop"/> 下げた分を引く）。
    /// </summary>
    public static (float Half, float Reach, float Rows) Measure(FacePart part, FaceLayout layout)
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(layout);
        var front = part.Front;
        var side = part.Side ?? throw new InvalidDataException($"{part.Id} に横の絵がありません。");
        float half = 0f, last = 0f, reach = 0f;
        for (var row = 0; row < front.Height; row++)
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

            if (lo >= 0)
            {
                half = MathF.Max(half, (hi + 1 - lo) / 2f);
                last = row + 1f;
            }

            for (var z = side.Width - 1; z >= 0; z--)
            {
                if (side.Solid(z + 0.5f, row + 0.5f) >= 0.5f)
                {
                    reach = MathF.Max(reach, layout.NoseSurface + z + 1f - FaceShape.EyeDepth);
                    break;
                }
            }
        }

        return (half, reach, last - part.Anchor.Y - Drop);
    }

    /// <summary>
    /// 格子 nx × ny（y は下から上の添字）での鼻のセル: (x, y, 顔の面の柱のいちばん前のセルから何セル前か（1 以上）, 材質)。
    /// </summary>
    public IEnumerable<(int X, int Y, int Depth, FaceMaterial Material)> Cells(int nx, int ny, Func<int, float> refX, Func<int, float> refY)
    {
        ArgumentNullException.ThrowIfNull(refX);
        ArgumentNullException.ThrowIfNull(refY);
        for (var y = 0; y < ny; y++)
        {
            var row = (int)MathF.Floor(refY(y) - Top);
            if (row < 0 || row >= Length)
            {
                continue;
            }

            var isBase = row == Length - 1;
            var isTip = row == Length - 2;
            for (var x = 0; x < nx; x++)
            {
                // 真ん中から外へ数えたセル: 0 が真ん中の 2 セル、1 がその外、…
                var dx = MathF.Abs(refX(x) - FaceShape.Mid);
                var ring = (int)MathF.Floor(dx);
                if (isBase || isTip)
                {
                    if (ring >= TipHalf)
                    {
                        continue;
                    }

                    if (isBase)
                    {
                        // 底（下の面）: [小鼻][穴][鼻柱][穴][小鼻]。鼻柱（真ん中 2 セル）は穴より 1 段前（鼻先の 1 つ下まで）、穴と小鼻は前へ 1 セル
                        // （鼻先のひさしの下）。2 つの穴の間に必ず皮膚がある（デッサンの鼻の下の面: 鼻柱が 2 つの穴を分ける）。
                        var depth = ring == 0 ? Math.Max(1, Projection - 1) : 1;
                        for (var d = 1; d <= depth; d++)
                        {
                            yield return (x, y, d, ring == 1 && d == depth ? FaceMaterial.Nostril : FaceMaterial.Skin);
                        }

                        continue;
                    }

                    // 鼻先: 真ん中がいちばん前、小鼻は 1 セル低い。
                    var height = ring == 0 ? Projection : Math.Max(1, Projection - 1);
                    for (var d = 1; d <= height; d++)
                    {
                        yield return (x, y, d, FaceMaterial.Skin);
                    }

                    continue;
                }

                if (ring >= 1)
                {
                    continue;
                }

                // 鼻すじ: 鼻根の行は高さ 0（顔の面のまま: 目の間は出っ張らない）から、鼻先の行の高さへ直線で増える（0 セルの行は描かない）。
                var bridge = (int)MathF.Round(Projection * row / (float)Math.Max(1, Length - 2));
                for (var d = 1; d <= bridge; d++)
                {
                    yield return (x, y, d, FaceMaterial.Skin);
                }
            }
        }
    }
}
