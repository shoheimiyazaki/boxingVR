namespace MiniRing.Bots;

internal static class CounterResponse
{
    internal static Combo CounterFor(Defense defense, float side, Hand lead, float duckChoice) => defense switch
    {
        Defense.Slip => new Combo("slip-counter", side * lead.LocalSide() > 0f ? [Technique.Cross, Technique.LeadHook] : [Technique.LeadHook, Technique.Cross]),
        Defense.Duck => new Combo("duck-counter", duckChoice < 0.5f ? [Technique.RearUppercut, Technique.LeadHook] : [Technique.LeadBodyHook, Technique.LeadHook]),
        Defense.Sway => new Combo("pull-counter", [Technique.Cross]),
        Defense.Weave => new Combo("weave-counter", side * lead.LocalSide() > 0f ? [Technique.LeadHook, Technique.Cross] : [Technique.RearHook, Technique.LeadHook]),
        Defense.SideStep => new Combo("angle-counter", [Technique.Cross, Technique.LeadHook]),
        Defense.StepBack => new Combo("step-counter", [Technique.Jab, Technique.Cross]),
        Defense.SlipIn => new Combo("inside-counter", [Technique.Cross, Technique.LeadHook]),
        Defense.Roll => new Combo("roll-counter", [Technique.LeadHook, Technique.Cross]),
        _ => new Combo("block-counter", [Technique.Jab, Technique.Cross]),
    };
}
