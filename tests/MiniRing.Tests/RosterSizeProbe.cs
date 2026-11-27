using System.Text.Json;

namespace MiniRing.Tests;

/// <summary>名簿の体格（body.build）から、見えている体の身長・体重（ボクセルの実測）を出す（docs/plans/BASE_BODY_PLAN.md §5-4）。読み込みの一致の確かめの前に使う。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class RosterSizeProbe
{
    /// <summary>
    /// IVF の選手の階級を保つ体重（2026-11-27）: 前の階級の境（64・73・80・91）の中の位置を、実物の境（61.2・72.6・79.4・90.7）の同じ位置へ写した値。
    /// 骨格（身長・腕・脚の比）はそのままに太さだけを解く（減量・増量と同じ <c>BodySizing.WithWeight</c>）。ヘビー級は上限が無いので比のまま。
    /// </summary>
    private static readonly Dictionary<string, int> IvfTargets = new()
    {
        ["anan-inthasorn"] = 56, ["andre-bishop"] = 58, ["ulugbek-nazarov"] = 71, ["abel-navarrete"] = 72,
        ["daniyar-seitkali"] = 74, ["sipho-van-wyk"] = 76, ["ivan-escobedo"] = 80, ["prasert-rattanakul"] = 89,
    };

    [TestMethod]
    public void PrintMeasured()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(root.FullName, "games", "MiniRing")))
        {
            root = root.Parent ?? throw new DirectoryNotFoundException("リポジトリの根が見つからない");
        }

        var dir = Path.Combine(root.FullName, "games", "MiniRing", "Characters", "roster");
        foreach (var file in Directory.GetFiles(dir, "*.json").Order(StringComparer.Ordinal))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var b = doc.RootElement.GetProperty("body").GetProperty("build");
            float F(string name) => b.TryGetProperty(name, out var v) ? v.GetSingle() : 1f;
            var build = new BodyBuild(F("height"), F("arms"), F("legs"), F("muscle"), b.TryGetProperty("fourLegged", out var q) && q.GetBoolean());
            var id = Path.GetFileNameWithoutExtension(file);
            if (IvfTargets.TryGetValue(id, out var target))
            {
                build = Characters.BodySizing.WithWeight(build, target);
                Console.WriteLine($"MUSCLE {id} {build.Muscle.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            }

            var h = Characters.CharacterProfile.HeightOf(build);
            var w = Characters.CharacterProfile.WeightOf(build);
            Console.WriteLine($"ROSTER {Path.GetFileNameWithoutExtension(file)} {h} {w} {Characters.BodySizing.PhysicalMassKg(build):0.00} BMI {w / (h * h / 1e4f):0.0}");
        }
    }
}
