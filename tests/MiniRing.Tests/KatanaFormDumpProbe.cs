using System.Globalization;
using System.Text;
using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 調査用: 武器の型（<see cref="FormSwordsman.Forms"/>）の経路を JSON に書き出す。VR の稽古の記録と型を比べる道具
/// （<c>tools/vrmotion_pick.py</c>、docs/arena/vrmotion_pick_2026-11-24.md）が読む。出力先は環境変数 <c>KATANA_FORMS_OUT</c>（無ければ一時フォルダ）、
/// 武器は <c>FORMS_WEAPON</c>（<see cref="WeaponKind"/> の名前、既定 Katana）。刀身の長さ（握りから切っ先、腕の長さ = 1、人形の腕 0.53 m で）も書く。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class KatanaFormDumpProbe
{
    [TestMethod]
    [TestCategory("Probe")]
    public void DumpKatanaForms()
    {
        var path = Environment.GetEnvironmentVariable("KATANA_FORMS_OUT") ?? Path.Combine(Path.GetTempPath(), "katana_forms.json");
        var weapon = Enum.Parse<WeaponKind>(Environment.GetEnvironmentVariable("FORMS_WEAPON") ?? nameof(WeaponKind.Katana), ignoreCase: true);
        var sb = new StringBuilder("{\"weapon\":\"").Append(weapon).Append("\",\"blade\":");
        sb.Append(FormSwordsman.BladeLength(weapon, 0.53f).ToString(CultureInfo.InvariantCulture)).Append(",\"forms\":{");
        var first = true;
        foreach (var form in FormSwordsman.Forms(weapon))
        {
            sb.Append(first ? "" : ",").Append('"').Append(form.Name).Append("\":{\"dt\":")
              .Append(form.FrameTime.ToString(CultureInfo.InvariantCulture)).Append(",\"frames\":[");
            first = false;
            for (var i = 0; i < form.Frames.Count; i++)
            {
                var f = form.Frames[i];
                float[] n = [f.Right.X, f.Right.Y, f.Right.Z, f.Blade.X, f.Blade.Y, f.Blade.Z, f.Edge.X, f.Edge.Y, f.Edge.Z, f.Velocity.Z, f.Left.X, f.Left.Y, f.Left.Z];
                sb.Append(i == 0 ? "[" : ",[").AppendJoin(',', n.Select(v => v.ToString("0.#####", CultureInfo.InvariantCulture))).Append(']');
            }

            sb.Append("]}");
        }

        sb.Append("}}");
        File.WriteAllText(path, sb.ToString());
        Console.WriteLine($"wrote {path}");
    }
}
