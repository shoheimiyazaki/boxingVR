using System.Globalization;
using System.Text;
using MiniRing.Motion;

namespace MiniRing.Arena;

/// <summary>
/// 自傷の原因の切り分け（判断の側）: 剣士の技の目標の通り道（両手の位置と剣の向き）そのものが、自分の体（<see cref="SelfClearance"/> の箱）を通るか。
/// 技ごとに、動きのフレームを一定の間隔で見て、刃の線が体の中（離れ &lt; 0）・余裕の内（離れ &lt; <see cref="SelfClearance.Margin"/>）にあるフレームの割合を、
/// 外へ傾ける前と後で並べる。後で 0 に近ければ、残る自傷は目標を追う体の遅れ・事故による。
/// </summary>
internal static class SelfPathLab
{
    private const float Step = 0.02f;

    public static string Run(SwordLibraryLab.Library library, SelfClearance clearance)
    {
        var text = new StringBuilder();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"余裕 {clearance.Margin} 腕の長さ、握りから {clearance.FromGrip} より先を見る、傾ける上限 {clearance.MaxTilt} rad。フレーム {Step} s ごと（録った速さ）。"));
        text.AppendLine();
        text.AppendLine("| 剣 | 技 | フレーム | 体の中（前） | 余裕の内（前） | 体の中（後） | 余裕の内（後） | いちばん近い体の箱（前） | 最大の傾き [度] |");
        text.AppendLine("|---|---|---:|---:|---:|---:|---:|---|---:|");
        int total = 0, insideBefore = 0, nearBefore = 0, insideAfter = 0, nearAfter = 0;
        foreach (var technique in library.Techniques.OrderBy(t => t.Weapon).ThenBy(t => t.Name, StringComparer.Ordinal))
        {
            var length = SwordLibraryLab.BladeLength(technique.Weapon);
            int n = 0, ib = 0, nb = 0, ia = 0, na = 0;
            var boxes = new Dictionary<string, int>();
            var tilt = 0f;
            for (var t = 0f; t <= technique.Motion.Duration; t += Step)
            {
                var frame = technique.Motion.Sample(t);
                var (before, _, box, _) = clearance.Along(frame.Right, frame.Blade, length, frame.Head);
                var kept = clearance.Keep(frame, length);
                var (after, _, _, _) = clearance.Along(kept.Right, kept.Blade, length, kept.Head);
                n++;
                ib += before < 0f ? 1 : 0;
                nb += before < clearance.Margin ? 1 : 0;
                ia += after < 0f ? 1 : 0;
                na += after < clearance.Margin ? 1 : 0;
                if (before < clearance.Margin)
                {
                    boxes[box] = boxes.GetValueOrDefault(box) + 1;
                }

                tilt = MathF.Max(tilt, MathF.Acos(Math.Clamp(Vector3Dot(frame, kept), -1f, 1f)));
            }

            total += n;
            insideBefore += ib;
            nearBefore += nb;
            insideAfter += ia;
            nearAfter += na;
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {technique.Weapon} | {technique.Name} | {n} | {100f * ib / n:0}% | {100f * nb / n:0}% | {100f * ia / n:0}% | {100f * na / n:0}% | {string.Join("・", boxes.OrderByDescending(b => b.Value).Select(b => $"{b.Key}×{b.Value}"))} | {tilt * 180f / MathF.PI:0} |"));
        }

        text.AppendLine();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"全体: 体の中 {100f * insideBefore / total:0.0}% → {100f * insideAfter / total:0.0}%、余裕の内 {100f * nearBefore / total:0.0}% → {100f * nearAfter / total:0.0}%（{total} フレーム）。"));
        text.AppendLine();
        text.AppendLine("構えのフレーム:");
        text.AppendLine();
        foreach (var (weapon, guard) in library.Guards)
        {
            var length = SwordLibraryLab.BladeLength(weapon);
            var (c, at, box, _) = clearance.Along(guard.Right, guard.Blade, length);
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"- {weapon}: 刃の線の離れ {c:0.00}（握りから {at:0.00} の {box}）"));
        }

        return text.ToString();
    }

    private static float Vector3Dot(MotionFrame a, MotionFrame b) => System.Numerics.Vector3.Dot(a.Blade, b.Blade);
}
