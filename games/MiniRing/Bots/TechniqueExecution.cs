using System.Numerics;

namespace MiniRing.Bots;

/// <summary>選択済みの技を目標経路へ変換する。乱数・入力機器・物理状態の書き込みを持たない。</summary>
internal static class TechniqueExecution
{
    internal const float FullLoad = BotStyle.MaxTelegraph;
    // 既存ボットの値をそのまま移す。角度 [rad]、沈み・拳の引きは腕の長さ倍。
    // 25° / 12° の逆ひねりと 0.08 腕の沈みで体のバネを縮める。
    private const float LoadTwistRear = 25f * MathF.PI / 180f;
    private const float LoadTwistLead = 12f * MathF.PI / 180f;
    private const float LoadDip = 0.08f;
    private static readonly Vector3 LoadCockRear = new(0f, -0.03f, -0.1f);
    private static readonly Vector3 LoadCockLead = new(0f, -0.01f, -0.05f);

    internal static Vector3 Guard(Hand hand, Hand leadHand, float reach, Vector3 headLocal, float height, float width, float extension, float faintness)
    {
        var lead = hand == leadHand;
        var chin = headLocal.Y - (0.15f * reach);
        var low = -0.12f * reach;
        var y = low + ((chin - low) * height * (lead ? 0.85f : 1f)) - (0.35f * faintness * reach);
        return new Vector3(hand.LocalSide() * (lead ? 0.17f : 0.13f) * width * reach, y, (lead ? 0.52f : 0.36f) * extension * reach);
    }

    internal static Vector3 Punch(in PunchAction action, Hand hand, Hand lead, Vector3 guard, Vector3 aim, float reach)
    {
        var target = action.Spec.Evaluate(hand, action.Time, action.Duration, guard, aim, reach);
        if (action.Time < 0f && action.Loaded)
        {
            target += (hand == lead ? LoadCockLead : LoadCockRear) * (reach * Wound(action));
        }

        return target;
    }

    internal static float Spring(in PunchAction action)
        => action.Active && action.Spec.Extend >= 1f && action.Time >= 0f && action.Time < action.Duration * PunchSpec.OutboundShare ? 1f : 0f;

    internal static float Envelope(in PunchAction action)
    {
        if (action.Time < 0f)
        {
            return 0f;
        }

        var u = Math.Clamp(action.Time / action.Duration, 0f, 1f);
        return action.Loaded ? MathF.Max(MathF.Sin(MathF.PI * u), u < 0.5f ? 0.5f : 0f) : MathF.Sin(MathF.PI * u);
    }

    internal static float Wound(in PunchAction action)
        => action.Loaded && action.Time < 0f ? Math.Clamp((action.Time + action.Load) / FullLoad, 0f, 1f) : 0f;

    internal static Vector3 HeadShift(in PunchAction action, Hand lead, float reach)
        => AttackShift(action, lead.LocalSide(), reach) + LoadShift(action, lead.LocalSide(), reach);

    internal static Vector3 AttackShift(in PunchAction action, float lead, float reach)
        => new Vector3(action.Spec.HeadShift.X * lead, action.Spec.HeadShift.Y, action.Spec.HeadShift.Z) * (reach * Envelope(action));

    internal static Vector3 LoadShift(in PunchAction action, float lead, float reach)
        => new Vector3(-0.5f * action.Spec.HeadShift.X * lead, -LoadDip, -0.5f * action.Spec.HeadShift.Z) * (reach * Wound(action));

    internal static float Twist(in PunchAction action, Hand hand, Hand lead)
    {
        if (!action.Active || !action.Loaded)
        {
            return 0f;
        }

        var share = action.Time < 0f ? -Wound(action) : MathF.Min(1f, action.Load / FullLoad) * MathF.Max(0f, 1f - (2f * action.Time / action.Duration));
        var size = hand == lead ? LoadTwistLead : LoadTwistRear;
        return (hand == Hand.Right ? 1f : -1f) * size * share;
    }

    internal static Vector3 DefenseHead(DefenseMove m, float side, float progress)
    {
        if (!m.Sweep)
        {
            return new Vector3(side * m.HeadX, m.HeadY, m.HeadZ);
        }

        var t = Math.Clamp(progress, 0f, 1f);
        var x = side * (m.HeadX + ((m.EndX - m.HeadX) * t));
        return new Vector3(x, m.HeadY * MathF.Sin(MathF.PI * t), m.HeadZ);
    }

    internal static Vector3 DefenseGlove(DefenseMove m, PunchKind kind, float defenseSide, Hand hand, Vector3 headLocal, float stanceY, float reach, Vector3 current)
    {
        var anchor = headLocal with { Y = headLocal.Y + ((stanceY - headLocal.Y) * m.GloveAnchor) };
        var side = hand.LocalSide();
        var cover = kind == PunchKind.Body
            ? headLocal + (new Vector3(side * (m.GloveWidth + 0.07f), m.GloveHeight - m.BodyDrop, m.GloveForward * 0.8f) * reach)
            : kind == PunchKind.Hook && side == -defenseSide
                ? anchor + (new Vector3(side * m.TempleWidth, m.GloveHeight + 0.05f, m.GloveForward * 0.55f) * reach)
                : anchor + (new Vector3(side * m.GloveWidth, m.GloveHeight, m.GloveForward) * reach);
        return Vector3.Lerp(current, cover, m.HandsUp);
    }
}

/// <summary>片手の技の状態。進行・判断は操作元が持ち、経路評価は共有する。</summary>
internal readonly record struct PunchAction(Technique? Technique, PunchSpec Spec, float Time, float Duration, bool Counter,
    Vector3 AimOffset = default, Vector3 AimWorld = default, float Load = 0f, AimGoal Goal = AimGoal.Center)
{
    public bool Active => Duration > 0f && Time < Duration;
    public bool Loaded => Load > 0f;
}
