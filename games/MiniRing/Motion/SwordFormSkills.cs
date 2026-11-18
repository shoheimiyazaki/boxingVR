using System.Text.Json;
using MiniRing.Weapons;

namespace MiniRing.Motion;

/// <summary>型の狙いのドリル（5 構え）で測った命中割合。体の強さでなく、入力元が技を選ぶための表。</summary>
internal static class SwordFormSkills
{
    private static readonly Lazy<Dictionary<string, Dictionary<BodyPartKind, float>>> Measured = new(() =>
    {
        using var stream = typeof(SwordFormSkills).Assembly.GetManifestResourceStream("MiniRing.Motion.SwordFormSkills.json")
            ?? throw new InvalidOperationException("型の狙いの測定表がありません。");
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<BodyPartKind, float>>>(stream)
            ?? throw new InvalidDataException("型の狙いの測定表が空です。");
    });

    public static IReadOnlyDictionary<BodyPartKind, float>? For(WeaponKind weapon, string name) => Measured.Value.GetValueOrDefault($"{weapon}/{name}");
}
