namespace MiniRing.Bots;

/// <summary>狙いとスタイルから接触形を選ぶ。打撃の威力は変更しない。</summary>
internal static class KarateHandChoice
{
    internal static HandShape Pick(IReadOnlyDictionary<HandShape, float> weights, AimGoal goal, float sample)
    {
        var choices = weights.OrderBy(p => p.Key).Select(p => (p.Key, Weight: p.Value * Affinity(p.Key, goal))).ToArray();
        var remaining = sample * choices.Sum(p => p.Weight);
        foreach (var choice in choices)
        {
            remaining -= choice.Weight;
            if (remaining < 0f)
            {
                return choice.Key;
            }
        }

        return HandShape.Seiken;
    }

    private static float Affinity(HandShape shape, AimGoal goal) => goal switch
    {
        AimGoal.Eye => shape is HandShape.OneKnuckle or HandShape.SpearHand ? 2f : 1f,
        AimGoal.SolarPlexus or AimGoal.Liver => shape is HandShape.OneKnuckle or HandShape.SpearHand ? 2f : 1f,
        AimGoal.Temple or AimGoal.GuardArm => shape is HandShape.PalmHeel or HandShape.KnifeHand ? 2f : 1f,
        _ => 1f,
    };
}
