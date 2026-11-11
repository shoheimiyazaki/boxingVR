using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// 剣の基本の動き: 日本刀は SFU の剣道のモーションキャプチャ（data/mocap/sfu_kendo）から区間を切り出し、ロングソードは型のキー
/// （<see cref="SwordForms.Longsword"/>）から。どれも両手持ち。
/// </summary>
internal static class SwordBasics
{
    private const string Kendo = "data/mocap/sfu_kendo/";

    /// <summary>剣道のモーションキャプチャから切り出す区間（題名・ファイル・始まり・終わり [s]）。</summary>
    public static readonly (string Title, string File, float From, float To)[] KendoClips =
    [
        ("中段の構えと小さな打ち", "0015_BasicKendo001.bvh", 4f, 15f),
        ("大きく振りかぶる面", "0015_BasicKendo001.bvh", 15.5f, 22f),
        ("振りかぶりと斬り下ろし", "0015_BasicKendo001.bvh", 39f, 49f),
        ("切り返し（左右面）", "0015_Kirikaeshi001.bvh", 2f, 30f),
    ];

    public static IEnumerable<ImitationVideo.Shot> Shots(float rate)
    {
        var loaded = new Dictionary<string, BvhClip>();
        foreach (var (title, file, from, to) in KendoClips)
        {
            if (!loaded.TryGetValue(file, out var clip))
            {
                loaded[file] = clip = BvhClip.Load(Kendo + file);
            }

            yield return new ImitationVideo.Shot("日本刀: " + title, WeaponKind.Katana, MotionRetarget.FromBvh(clip, title, from, to), rate, clip, from);
        }

        foreach (var form in SwordForms.Longsword())
        {
            yield return new ImitationVideo.Shot("ロングソード: " + form.Name, WeaponKind.Longsword, form, rate);
        }
    }
}
