namespace MiniRing.Tests;

/// <summary>
/// 警告にとどめる確認（claude/glove-v2 の Soft と同じ形）。条件が成り立たなければ「⚠ WARNING: ...」をコンソールへ書き、
/// <see cref="Assert.Inconclusive(string)"/> でそのテストを「結果なし」にする（失敗ではない）。まだ届いていない目標の目安に使う
/// （例: 試合の面白さの「もっと面白くあるべき」目標、HighlightRegressionTests）。
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
