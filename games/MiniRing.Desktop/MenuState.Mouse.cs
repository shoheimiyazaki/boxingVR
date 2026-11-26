using System.Numerics;

namespace MiniRing.Desktop;

/// <summary>
/// マウスだけで操作するための配置（設計の画面 640 × 464）: 左右で変える値の ◀ ▶・設定の「あり / なし」・隅の「戻る」と、押しっぱなしの連続の速さ。
/// 描画（<see cref="MenuView"/>）と指す位置の判定（<see cref="MenuState"/>）が同じ値を使う（2026-11-26）。
/// </summary>
internal static class MouseLayout
{
    /// <summary>押してから連続が始まるまで [s]（OS のキーの連続と同じくらい: 1 回押しで 1 つだけ動く）。</summary>
    public const float RepeatDelay = 0.4f;

    /// <summary>連続の間隔 [s]（1 秒に約 14 回。体重なら 1 kg ずつで、60 kg の幅を 4 秒ほどで端から端まで）。</summary>
    public const float RepeatInterval = 0.07f;

    /// <summary>◀ ▶ のボタンの大きさ（16 px の字が 1 つ入り、行の高さ 22 に収まる）。</summary>
    public static readonly Vector2 ArrowSize = new(24f, 20f);

    /// <summary>ゲームモード: 練習試合の行の「決まり ◀ 標準 ▶」（2 行目）。<paramref name="side"/> は −1 = ◀、+1 = ▶。</summary>
    public static Vector2 ModeArrow(int side) => new(side < 0 ? 112f : 266f, MenuLayout.ModeRowCenter(0).Y + 11f);

    /// <summary>作る: 体型・身長・教科書の行の ◀ ▶（行の右の部分。左は項目の名前）。</summary>
    public static Vector2 CreateArrow(int row, int side) => new(side < 0 ? 104f : 288f, MenuLayout.CampaignRowCenter(row).Y);

    /// <summary>本拠地: 次の試合の札の武器の ◀ ▶（札の右下。右上は相手の名前）。</summary>
    public static Vector2 WeaponArrow(int side) => new(side < 0 ? 470f : 598f, CampaignLayout.NextCenter.Y + 10f);

    /// <summary>設定: レフェリーの「あり」（0）・「なし」（1）。</summary>
    public static readonly Vector2 OptionSize = new(56f, 28f);
    public static Vector2 RefereeOption(int option) => MenuLayout.SettingsRowCenter(0) + new Vector2(90f + (option * 64f), 0f);

    /// <summary>行の戻るが無いページ（キャラ選択・リプレイ）の、左上の隅の「戻る」（見出しの板の上、枠の内側）。</summary>
    public static readonly Vector2 CornerBackCenter = new(58f, 28f);
    public static readonly Vector2 CornerBackSize = new(80f, 24f);

    public static bool Inside(Vector2 point, Vector2 center, Vector2 size)
    {
        var delta = Vector2.Abs(point - center);
        return delta.X <= size.X / 2 && delta.Y <= size.Y / 2;
    }
}

/// <summary>マウスの押しっぱなし（ボタンを押し続けると連続して変わる）と ◀ ▶ のクリック。</summary>
internal sealed partial class MenuState
{
    private string? _holdKey;
    private float _holdTime;

    /// <summary>マウスが指している所（設計の座標。キーボード・パッドで動かしたら消える）。行の無いボタン（◀ ▶・±kg・隅の戻る）を光らせる。</summary>
    public Vector2? Hovered { get; private set; }

    private void Track(MenuInput input)
    {
        if (input.Pointer is { } point) { Hovered = point; }
        else if (input.X != 0 || input.Y != 0 || input.Confirm || input.Cancel) { Hovered = null; }
    }

    /// <summary>指している所が、中心 <paramref name="center"/>・大きさ <paramref name="size"/> の中か。</summary>
    public bool IsHovered(Vector2 center, Vector2 size) => Hovered is { } point && MouseLayout.Inside(point, center, size);

    /// <summary>
    /// 押した瞬間に 1 回、押し続けて <see cref="MouseLayout.RepeatDelay"/> を過ぎたら <see cref="MouseLayout.RepeatInterval"/> ごとに 1 回（この入力で何回分か）。
    /// 連続は、押し始めた所（<paramref name="key"/>）を指している間だけ（外れたら止まる。別の所へ動かしても、押し直すまで始まらない）。
    /// </summary>
    private int Fire(MenuInput input, string key)
    {
        if (input.Click)
        {
            _holdKey = key;
            _holdTime = 0f;
            return 1;
        }

        if (!input.Held || _holdKey != key) { return 0; }
        static int Ticks(float t) => t < MouseLayout.RepeatDelay ? 0 : 1 + (int)((t - MouseLayout.RepeatDelay) / MouseLayout.RepeatInterval);
        var before = Ticks(_holdTime);
        _holdTime += Math.Max(0f, input.Dt);
        return Ticks(_holdTime) - before;
    }

    /// <summary>ボタンを放した（押しっぱなし・ドラッグの終わり）。</summary>
    private void Release(MenuInput input)
    {
        if (input.Held || input.Click) { return; }
        _holdKey = null;
        _dragging = false;
    }

    /// <summary>
    /// ◀ ▶ を指して押している（押しっぱなし）なら、その向きの左右（<see cref="MenuInput.X"/>）に直し、クリックは行の決定に数えない。
    /// 指しているだけ（押していない）なら左右は 0 のまま（行のホバーで行が選ばれるだけ）。
    /// </summary>
    private MenuInput Arrows(MenuInput input, Func<int, Vector2> arrow, string key)
    {
        if (input.Pointer is not { } point) { return input; }
        foreach (var side in new[] { -1, 1 })
        {
            if (MouseLayout.Inside(point, arrow(side), MouseLayout.ArrowSize))
            {
                return input with { X = side * Fire(input, key + side), Click = false };
            }
        }

        return input;
    }

    /// <summary>左上の隅の「戻る」を押したら、戻る（Esc・B と同じ）に直す。</summary>
    private static MenuInput CornerBack(MenuInput input) =>
        input is { Click: true, Pointer: { } point } && MouseLayout.Inside(point, MouseLayout.CornerBackCenter, MouseLayout.CornerBackSize)
            ? input with { Cancel = true, Click = false }
            : input;
}
