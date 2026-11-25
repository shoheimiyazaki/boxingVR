using System.Numerics;
using MiniRing.Characters;

namespace MiniRing.Desktop;

/// <summary>
/// キャラ選択の格子（2026-11-26。純粋: 描画・入力を持たない）。名簿を分け方（<see cref="FighterCategories.Of"/>）ごとの節に並べ、節の頭に見出しの行を置き、
/// 節の中は <see cref="Columns"/> 人ずつの行にする。画面に入りきらない行は縦に送る（<see cref="FirstRow"/> から見える分だけ描く）ので、人数が増えても崩れない。
/// 位置は設計の画面（640 × 464）の座標。カーソルは名簿の添字（<see cref="MenuState.RedCursor"/>）のままで、並びと位置だけをここで決める。
/// </summary>
internal sealed class SelectGrid
{
    public const int Columns = 6;
    public const float Left = 172f;
    public const float Right = 468f;
    public const float Top = 96f;
    public const float Bottom = 382f;
    public const float HeaderHeight = 18f;
    public const float TileRowHeight = 66f;
    public const float ColumnStep = 48f;
    public static readonly Vector2 TileSize = new(44f, 62f);

    /// <summary>1 行: 見出し（<see cref="Members"/> が空）か、同じ分け方の人の並び。</summary>
    internal sealed record Row(FighterCategory Category, bool IsHeader, IReadOnlyList<int> Members, int Count)
    {
        public float Height => IsHeader ? HeaderHeight : TileRowHeight;
    }

    private readonly Dictionary<int, (int Row, int Column)> _where = [];
    private readonly List<int> _tileRows = [];

    public SelectGrid(IReadOnlyList<FighterCharacter> roster)
    {
        ArgumentNullException.ThrowIfNull(roster);
        Categories = [.. roster.Select(FighterCategories.Of)];
        var rows = new List<Row>();
        var order = new List<int>();
        foreach (var category in FighterCategories.Order)
        {
            var members = Enumerable.Range(0, roster.Count).Where(i => Categories[i] == category).ToList();
            if (members.Count == 0) { continue; }
            rows.Add(new Row(category, true, [], members.Count));
            for (var start = 0; start < members.Count; start += Columns)
            {
                var line = members.Skip(start).Take(Columns).ToList();
                for (var c = 0; c < line.Count; c++) { _where[line[c]] = (rows.Count, c); }
                _tileRows.Add(rows.Count);
                rows.Add(new Row(category, false, line, line.Count));
            }

            order.AddRange(members);
        }

        Rows = rows;
        Order = order;
        MaxFirstRow = Enumerable.Range(0, rows.Count).FirstOrDefault(first => rows.Skip(first).Sum(r => r.Height) <= Bottom - Top);
    }

    /// <summary>名簿の添字ごとの分け方。</summary>
    public IReadOnlyList<FighterCategory> Categories { get; }

    /// <summary>上から順の行（見出しを含む）。</summary>
    public IReadOnlyList<Row> Rows { get; }

    /// <summary>画面の並び（名簿の添字）。左右の移動はこの順に進む。</summary>
    public IReadOnlyList<int> Order { get; }

    /// <summary>送りの最大（ここから下が全部見える最初の行）。</summary>
    public int MaxFirstRow { get; }

    /// <summary>いちばん上に見えている行。</summary>
    public int FirstRow { get; private set; }

    /// <summary>行の上端の y（見えていなければ null）。</summary>
    public float? RowTop(int row)
    {
        if (row < FirstRow) { return null; }
        var y = Top;
        for (var r = FirstRow; r < row; r++) { y += Rows[r].Height; }
        return y + Rows[row].Height <= Bottom + 0.01f ? y : null;
    }

    /// <summary>見えている行（上から）。</summary>
    public IEnumerable<(int Row, float Top)> VisibleRows()
    {
        var y = Top;
        for (var r = FirstRow; r < Rows.Count && y + Rows[r].Height <= Bottom + 0.01f; r++)
        {
            yield return (r, y);
            y += Rows[r].Height;
        }
    }

    /// <summary>札の中心（見えていなければ null）。</summary>
    public Vector2? TileCenter(int index)
    {
        if (!_where.TryGetValue(index, out var at) || RowTop(at.Row) is not { } top) { return null; }
        var width = Columns * ColumnStep;
        var x0 = Left + (((Right - Left) - width) / 2f);
        return new Vector2(x0 + (at.Column * ColumnStep) + (ColumnStep / 2f), top + (TileRowHeight / 2f));
    }

    /// <summary>指している札（名簿の添字）。</summary>
    public int? Hit(Vector2 point)
    {
        foreach (var (row, _) in VisibleRows())
        {
            foreach (var index in Rows[row].Members)
            {
                if (TileCenter(index) is { } c && Vector2.Abs(point - c) is var d && d.X <= TileSize.X / 2 && d.Y <= TileSize.Y / 2)
                {
                    return index;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// カーソルを動かす: 左右は画面の並びで前後へ（端で回る）、上下は上・下の人の行の同じ列へ（短い行なら行の最後、端で回る）。
    /// <paramref name="allowed"/> に合わない人は飛ばす（大会に出られない人など。誰も合わなければ動かない）。
    /// </summary>
    public int Move(int index, int x, int y, Func<int, bool>? allowed = null)
    {
        allowed ??= _ => true;
        if (!_where.TryGetValue(index, out _)) { return index; }
        if (x != 0)
        {
            var at = IndexInOrder(index);
            for (var step = 1; step <= Order.Count; step++)
            {
                var next = Order[Wrap(at + (Math.Sign(x) * step), Order.Count)];
                if (allowed(next)) { index = next; break; }
            }
        }

        if (y != 0)
        {
            var (row, column) = _where[index];
            var line = _tileRows.IndexOf(row);
            for (var step = 1; step <= _tileRows.Count; step++)
            {
                var members = Rows[_tileRows[Wrap(line + (Math.Sign(y) * step), _tileRows.Count)]].Members;
                var pick = members.Take(column + 1).Reverse().Concat(members.Skip(column + 1)).FirstOrDefault(m => allowed(m), -1);
                if (pick >= 0) { index = pick; break; }
            }
        }

        return index;
    }

    /// <summary>画面の並びで最初に <paramref name="allowed"/> に合う人（いなければ null）。</summary>
    public int? First(Func<int, bool> allowed) => Order.Where(allowed).Select(i => (int?)i).FirstOrDefault();

    /// <summary>この人の札が見えるように送る（見出しも見えるよう、節の最初の行なら見出しから）。</summary>
    public void Reveal(int index)
    {
        if (!_where.TryGetValue(index, out var at)) { return; }
        var wanted = at.Row > 0 && Rows[at.Row - 1].IsHeader ? at.Row - 1 : at.Row;
        if (wanted < FirstRow)
        {
            FirstRow = wanted;
            return;
        }

        while (RowTop(at.Row) is null && FirstRow < MaxFirstRow) { FirstRow++; }
    }

    /// <summary>ホイールなどで送る（+ が下）。</summary>
    public void Scroll(int rows) => FirstRow = Math.Clamp(FirstRow + rows, 0, MaxFirstRow);

    /// <summary>見えている割合（スクロールバーの位置と長さ: 0..1）。</summary>
    public (float Start, float Length) ScrollBar()
    {
        var total = Rows.Sum(r => r.Height);
        var before = Rows.Take(FirstRow).Sum(r => r.Height);
        var shown = VisibleRows().Sum(v => Rows[v.Row].Height);
        return total <= 0f ? (0f, 1f) : (before / total, shown / total);
    }

    private int IndexInOrder(int index)
    {
        for (var i = 0; i < Order.Count; i++) { if (Order[i] == index) { return i; } }
        return 0;
    }

    private static int Wrap(int index, int count) => ((index % count) + count) % count;
}
