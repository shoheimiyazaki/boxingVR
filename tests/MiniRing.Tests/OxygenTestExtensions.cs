namespace MiniRing.Tests;

/// <summary>疲れた体を作るテストの道具（呼吸・循環のモデル、2026-11-18）。</summary>
internal static class OxygenTestExtensions
{
    /// <summary>
    /// 両腕の酸素の蓄えだけを空にする（全力の連打の後の体: 脚・胴・血の酸素は満ちている）。<see cref="Fighter.Stamina"/> = 0 はすべての部位と血を
    /// 空にするので、脚と胴の太い血管が心臓の送る酸素の大半を取り、腕の戻りを測る試験にならない。
    /// </summary>
    public static void EmptyArms(this Fighter fighter)
    {
        fighter.SetOxygen(MuscleRegion.LeftArm, 0f);
        fighter.SetOxygen(MuscleRegion.RightArm, 0f);
    }
}
