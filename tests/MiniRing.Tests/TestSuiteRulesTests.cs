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
}
