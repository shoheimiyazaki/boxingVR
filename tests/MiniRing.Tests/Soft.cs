namespace MiniRing.Tests;

/// <summary>
/// 警告にとどめる確認（2026-11-19、ユーザーの指示: 蹴りはまだ未成熟なので、蹴りのテストは試合を縛らない）。
/// 条件が成り立たなければ「⚠ WARNING: ...」をコンソールへ書き、<see cref="Assert.Inconclusive(string)"/> でそのテストを「結果なし」にする
/// （失敗ではない。数が戻ったら自然に通る）。物理が壊れたのでなく、まだ磨いていない所の目安に使う。
/// </summary>
internal static class Soft
{
    public static void Check(bool ok, string what)
    {
        if (ok)
        {
            return;
        }

        var text = $"⚠ WARNING: {what}";
        Console.WriteLine(text);
        Assert.Inconclusive(text);
    }
}
