using System.Numerics;

namespace MiniRing.Weapons;

/// <summary>左右で独立した鎌の経路を再生し、毎歩の線分目標を出す。</summary>
public sealed class ClawMotion
{
    private ClawStroke? _stroke;
    private BodyFrame _origin;
    private float _elapsed;
    private bool _pressed;

    public bool Active => _stroke is not null;

    public HandCommand Read(Hand hand, bool pressed, bool sweep, in FighterSense sense, Vector3 aimWorld, float dt,
        float drive = 0.31f, float windup = 1f)
    {
        if (pressed && !_pressed && _stroke is null)
        {
            _origin = sense.Self;
            _stroke = ClawStroke.Create(hand, sense.ArmReach, _origin.WorldToLocal(aimWorld), sweep, drive, windup);
            _elapsed = 0f;
        }

        _pressed = pressed;
        if (_stroke is not { } stroke)
        {
            return ClawStroke.Guard(hand, sense.ArmReach).ToCommand(hand);
        }

        var self = sense.Self;
        var guard = ClawStroke.Guard(hand, sense.ArmReach);
        var recovery = new ClawSegment(_origin.WorldToLocal(self.LocalToWorld(guard.Root)), _origin.WorldToLocal(self.LocalToWorld(guard.Tip)),
            _origin.InverseRotate(self.Rotate(guard.Normal)));
        var blade = stroke.Sample(_elapsed, recovery);
        _elapsed += dt;
        if (_elapsed >= stroke.Duration)
        {
            _stroke = null;
        }

        return new ClawSegment(self.WorldToLocal(_origin.LocalToWorld(blade.Root)), self.WorldToLocal(_origin.LocalToWorld(blade.Tip)),
            self.InverseRotate(_origin.Rotate(blade.Normal))).ToCommand(hand);
    }
}
