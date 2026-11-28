using System.Reflection;

namespace MiniRing.Tests;

/// <summary>
/// テストの棚の決まり（2026-11-20）: 調査用（Probe）はクラスごと <c>[TestCategory("Probe")]</c> で外す。名前の絞り込み（<c>FullyQualifiedName!~Probe</c>）には頼らない。
/// 名前に Probe を含むクラスは目印を持ち、通常のテストのクラスの中に Probe のメソッドを置かない（置くなら Probe のクラスへ移す）。
/// ここのメソッドの名前に Probe を書かない（自分で自分を捕まえる）。
/// </summary>
[TestClass]
public sealed class TestSuiteRulesTests
{
    private static bool HasCategory(MemberInfo member, string category)
        => member.GetCustomAttributes<TestCategoryAttribute>().Any(a => a.TestCategories.Contains(category));

    private static IEnumerable<Type> TestClasses()
        => typeof(TestSuiteRulesTests).Assembly.GetTypes().Where(t => t.GetCustomAttribute<TestClassAttribute>() is not null);

    [TestMethod]
    public void EveryInvestigationClass_IsMarkedWithItsCategory()
    {
        var missing = TestClasses().Where(t => t.Name.Contains("Probe", StringComparison.Ordinal) && !HasCategory(t, "Probe")).Select(t => t.Name).ToList();
        Assert.IsEmpty(missing, "add [TestCategory(\"Probe\")] to: " + string.Join(", ", missing));
    }

    [TestMethod]
    public void NoInvestigationMethod_LivesInASuiteClass()
    {
        var stray = TestClasses()
            .Where(t => !HasCategory(t, "Probe"))
            .SelectMany(t => t.GetMethods().Where(m => m.GetCustomAttribute<TestMethodAttribute>() is not null && m.Name.Contains("Probe", StringComparison.Ordinal))
                .Select(m => $"{t.Name}.{m.Name}"))
            .ToList();
        Assert.IsEmpty(stray, "move these into a Probe class: " + string.Join(", ", stray));
    }

    /// <summary>
    /// 層の印（2026-11-28、物理の憲法 §2.7）: テストのクラスに <c>[TestCategory("L0")]</c>〜<c>("L6")</c>（L3 は L3a・L3b・L3c）か、観る側の <c>("View")</c> を
    /// 1 つだけ付ける（クラスに。メソッドには付けない）。層ごとに回す: <c>dotnet test --filter TestCategory=L1</c>。印の無いクラスは今は許す（主なクラスから付けていく）。
    /// </summary>
    public static readonly IReadOnlySet<string> Layers = new HashSet<string> { "L0", "L1", "L2", "L3a", "L3b", "L3c", "L4", "L5", "L6", "View" };

    [TestMethod]
    public void LayerMarks_AreKnown_AndAtMostOnePerClass()
    {
        static bool LooksLikeLayer(string c) => c == "View" || (c.Length is 2 or 3 && c[0] == 'L' && char.IsDigit(c[1]));
        var bad = new List<string>();
        foreach (var t in TestClasses())
        {
            var marks = t.GetCustomAttributes<TestCategoryAttribute>().SelectMany(a => a.TestCategories).Where(LooksLikeLayer).ToList();
            if (marks.Count > 1 || marks.Any(m => !Layers.Contains(m)))
            {
                bad.Add($"{t.Name}: {string.Join(",", marks)}");
            }

            bad.AddRange(t.GetMethods().Where(m => m.GetCustomAttributes<TestCategoryAttribute>().SelectMany(a => a.TestCategories).Any(LooksLikeLayer))
                .Select(m => $"{t.Name}.{m.Name}: 層の印はクラスに"));
        }

        Assert.IsEmpty(bad, "層の印の誤り: " + string.Join("; ", bad));
    }
}
