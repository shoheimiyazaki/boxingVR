using System.Globalization;
using System.Numerics;
using Aigis.Render;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 動画に焼き込む「何を撮ったか」の 1 行（docs/plans/MOTION_TOOLS_PLAN.md §A3）: 道具のコマンド（技・スタイル・キャラの引数）・種・物理と判断の版。
/// 動画だけが手元に残っても、同じ物を撮り直せるようにする。テレビ中継の体裁の動画（団体の決まり・顔の窓）には出さない（見せ物の画面に内部の数を出さない）。
/// </summary>
internal static class ShotLabel
{
    /// <summary>文字の大きさ [px]（720p で読める最小に近い。画面の下の端に置く）。</summary>
    public const float Size = 15f;

    private static readonly Vector4 Grey = new(0.85f, 0.85f, 0.85f, 0.75f);

    /// <summary>1 行の文: "video --red champion --blue swarmer … | seed 3 | physics 102 logic 73"。</summary>
    public static string Text(int? seed, IEnumerable<string>? args = null)
    {
        var words = (args ?? Environment.GetCommandLineArgs().Skip(1)).Where(a => !a.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToArray();
        var command = string.Join(' ', words.Select(w => w.Contains(' ', StringComparison.Ordinal) ? $"\"{w}\"" : w));
        if (command.Length > 150)
        {
            command = command[..147] + "...";
        }

        return string.Create(CultureInfo.InvariantCulture,
            $"{command}{(seed is { } s ? $" | seed {s}" : "")} | physics {Fighter.PhysicsVersion} logic {BoxerBot.LogicVersion}");
    }

    /// <summary>画面の左下に置く文字。</summary>
    public static ScreenTextDraw Draw(string text, float height) => new(text, new Vector2(12f, height - (Size + 8f)), Size, Grey);
}
