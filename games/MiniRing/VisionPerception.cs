using System.Numerics;

namespace MiniRing;

/// <summary>本人の左右の目に残る組織の割合。+X が左。</summary>
public readonly record struct VisionState(float Left, float Right)
{
    public static VisionState Healthy => new(1f, 1f);
    public float Binocular => 0.5f * (Left + Right);

    /// <summary>中央で両眼を混ぜ、側方ほど同側の眼に依存する連続の視野近似。</summary>
    public float At(BodyFrame frame, Vector3 point)
    {
        var direction = frame.WorldToLocal(point);
        var side = direction.X / MathF.Max(0.0001f, new Vector2(direction.X, direction.Z).Length());
        return Math.Clamp(0.5f * ((1f + side) * Left + (1f - side) * Right), 0f, 1f);
    }
}

/// <summary>固定ステップごとの視覚の低域フィルタ。描画・Sense の参照では進めない。</summary>
public sealed class VisionPerception(int seed)
{
    private readonly Random _random = new(seed);
    private FighterSense? _previous;

    public FighterSense Update(FighterSense raw, VisionState eyes, float dt)
    {
        if (!float.IsFinite(dt) || dt <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(dt));
        }
        if (eyes == VisionState.Healthy)
        {
            _previous = raw;
            return raw;
        }
        var before = _previous ?? raw;
        Vector3 Observe(Vector3 point, Vector3 old)
        {
            var visibility = eyes.At(raw.Self, point);
            var loss = 1f - visibility;
            // 失明側は記憶を遅く更新する。振れは試合から分離した種付き系列で、参照回数には依存しない。
            var alpha = dt / (FighterTuning.VisionLagSeconds * loss * loss + dt);
            var noise = new Vector3(_random.NextSingle() - 0.5f, 0f, _random.NextSingle() - 0.5f);
            return Vector3.Lerp(old, point + noise * (raw.ArmReach * FighterTuning.VisionNoiseReach * loss), alpha);
        }
        GloveSense Glove(GloveSense current, GloveSense old)
        {
            var position = Observe(current.Position, old.Position);
            var alpha = dt / (FighterTuning.VisionLagSeconds * MathF.Pow(1f - eyes.At(raw.Self, current.Position), 2f) + dt);
            return new(position, Vector3.Lerp(old.Velocity, current.Velocity * eyes.At(raw.Self, current.Position), alpha));
        }
        var blade = raw.OpponentBlade;
        var seenHead = Observe(raw.OpponentHead, before.OpponentHead);
        var result = raw with
        {
            OpponentHead = seenHead,
            OpponentChest = Observe(raw.OpponentChest, before.OpponentChest),
            OpponentLeft = Glove(raw.OpponentLeft, before.OpponentLeft),
            OpponentRight = Glove(raw.OpponentRight, before.OpponentRight),
            OpponentVelocity = Vector3.Lerp(before.OpponentVelocity, raw.OpponentVelocity * eyes.Binocular,
                dt / (FighterTuning.VisionLagSeconds * (1f - eyes.Binocular) + dt)),
            OpponentBody = new(Observe(raw.OpponentBody.LeftShoulder, before.OpponentBody.LeftShoulder),
                Observe(raw.OpponentBody.RightShoulder, before.OpponentBody.RightShoulder),
                Observe(raw.OpponentBody.Pelvis, before.OpponentBody.Pelvis)),
            OpponentBlade = blade with { Hilt = Observe(blade.Hilt, before.OpponentBlade.Hilt),
                Tip = Observe(blade.Tip, before.OpponentBlade.Tip), TipVelocity = blade.TipVelocity * eyes.Binocular },
            // TargetMap は実体への参照。傷んだ視覚から正確な現在位置を逆引きさせない。
            OpponentTargets = raw.OpponentTargets?.Perceived(raw.OpponentHead, seenHead, raw.SelfHead, eyes.Binocular),
            OpponentVision = new VisionState(
                1f - (1f - (raw.OpponentVision?.Left ?? 1f)) * eyes.Binocular,
                1f - (1f - (raw.OpponentVision?.Right ?? 1f)) * eyes.Binocular),
            SelfVision = raw.SelfVision,
        };
        _previous = result;
        return result;
    }
}
