namespace MiniRing;

/// <summary>
/// 物理の版の組（2026-11-28、物理の憲法 §2.5）: 材料の版（L1、<see cref="Materials.MaterialTable.Version"/>）と体の版（L2〜L3、<see cref="Fighter.BodyVersion"/>）。
/// 判断の版（L5）は <see cref="Bots.BoxerBot.LogicVersion"/>。どれか 1 つでも違う記録は再生しない。履歴は docs/VERSIONS.md。
/// </summary>
public static class PhysicsVersions
{
    /// <summary>材料の版（L1）。</summary>
    public const int Material = Materials.MaterialTable.Version;

    /// <summary>体の版（L2〜L3）。</summary>
    public const int Body = Fighter.BodyVersion;

    /// <summary>
    /// 版の組の 1 行（機械が読む: tools/tvshow/tvshow.ps1 は行全体を比べる）。体の版を前の「physics」の欄に残し、材料の版を足す。
    /// </summary>
    public static string Label => $"versions physics {Body} material {Material} logic {Bots.BoxerBot.LogicVersion}";
}
