using System.Numerics;
using Aigis.Render;

namespace MiniRing.Voxels;

/// <summary>
/// 当たった瞬間の光（描画のみ。試合には響かない）。飛び散るボクセル（立方体）と見間違えないよう、球とカプセルだけで作る:
/// <list type="bullet">
/// <item>光の玉: 当たった所で一気にふくらんでしぼむ球。</item>
/// <item>衝撃の輪: 拳の向きに垂直に広がる、カプセルをつないだ輪（重い 1 発だけ）。</item>
/// <item>光の筋: 拳の向きへ走る細長いカプセル。</item>
/// </list>
/// 色は打った側のグローブの色、大きさ・速さ・本数は打撃の強さ（0〜1）で決まる。
/// </summary>
public sealed class ImpactEffects
{
    /// <summary>衝撃の輪を出す強さ（これより弱い 1 発は玉と筋だけ）。</summary>
    public const float RingStrength = 0.3f;

    private const int RingSegments = 20;
    private const float PopLife = 0.2f;
    private const float RingLife = 0.26f;

    private readonly List<Effect> _effects = [];

    /// <summary>今光っているものの数。</summary>
    public int Count => _effects.Count;

    /// <summary>
    /// 1 発ぶんの光を出す。<paramref name="direction"/> は拳の進む向き、<paramref name="strength"/> は 0〜1、
    /// <paramref name="jitter"/> は −1..1 の乱数（種つきを渡す）。
    /// </summary>
    public void Spawn(Vector3 point, Vector3 direction, float strength, Vector4 color, Func<float> jitter)
    {
        ArgumentNullException.ThrowIfNull(jitter);
        var dir = direction.LengthSquared() > 1e-12f ? Vector3.Normalize(direction) : Vector3.UnitY;
        var s = Math.Clamp(strength, 0f, 1f);
        var core = Vector4.Lerp(color, Vector4.One, 0.2f) with { W = 1f };
        _effects.Add(new Effect(EffectKind.Pop, point, dir, Vector3.Zero, core, PopLife, s));
        if (s >= RingStrength)
        {
            _effects.Add(new Effect(EffectKind.Ring, point, dir, Vector3.Zero, color with { W = 1f }, RingLife, s));
        }

        var streaks = 3 + (int)(6f * s);
        for (var k = 0; k < streaks; k++)
        {
            var heading = Vector3.Normalize(dir + (new Vector3(jitter(), jitter(), jitter()) * 0.3f));
            var speed = (1.5f + (3f * s)) * (1f + (0.25f * jitter()));
            _effects.Add(new Effect(EffectKind.Streak, point, heading, heading * speed, color with { W = 1f }, 0.12f + (0.1f * s), s));
        }
    }

    /// <summary>時間を進め、消えたものを捨てる。</summary>
    public void Step(float dt)
    {
        for (var i = _effects.Count - 1; i >= 0; i--)
        {
            var e = _effects[i];
            var age = e.Age + dt;
            if (age >= e.Life)
            {
                _effects.RemoveAt(i);
                continue;
            }

            _effects[i] = e with { Age = age, Position = e.Position + (e.Velocity * dt) };
        }
    }

    public void Clear() => _effects.Clear();

    /// <summary>光る球とカプセルを足す（自分の色で光る <see cref="MaterialKind.Emissive"/>）。</summary>
    public void WriteInstances(ICollection<PrimitiveInstance> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        foreach (var e in _effects)
        {
            var t = e.Age / e.Life;
            switch (e.Kind)
            {
                case EffectKind.Pop:
                {
                    // 最初の 2 割で一気にふくらみ、残りでしぼむ。
                    var size = t < 0.2f ? t / 0.2f : 1f - ((t - 0.2f) / 0.8f);
                    var radius = (0.015f + (0.025f * e.Strength)) * size;
                    into.Add(new PrimitiveInstance(PrimitiveKind.Sphere, Matrix4x4.CreateTranslation(e.Position), new Vector3(radius, 0f, 0f), e.Color, MaterialKind.Emissive));
                    break;
                }

                case EffectKind.Ring:
                {
                    var spread = 1f - ((1f - t) * (1f - t)); // 速く広がって、ゆるむ
                    var ring = 0.025f + ((0.04f + (0.08f * e.Strength)) * spread);
                    var thickness = (0.004f + (0.005f * e.Strength)) * (1f - t);
                    var side = Vector3.Normalize(Vector3.Cross(e.Direction, MathF.Abs(e.Direction.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
                    var up = Vector3.Cross(e.Direction, side);
                    var halfLength = MathF.PI * ring / RingSegments;
                    for (var k = 0; k < RingSegments; k++)
                    {
                        var angle = 2f * MathF.PI * (k + 0.5f) / RingSegments;
                        var radial = (MathF.Cos(angle) * side) + (MathF.Sin(angle) * up);
                        var tangent = (-MathF.Sin(angle) * side) + (MathF.Cos(angle) * up);
                        into.Add(Capsule(e.Position + (radial * ring), tangent, thickness, halfLength, e.Color));
                    }

                    break;
                }

                case EffectKind.Streak:
                {
                    var fade = 1f - t;
                    into.Add(Capsule(e.Position, e.Direction, 0.003f * fade + 0.001f, (0.015f + (0.035f * e.Strength)) * fade, e.Color));
                    break;
                }
            }
        }
    }

    /// <summary>向き <paramref name="axis"/> に沿ったカプセル（エンジンのカプセルは自分の +Y に沿う）。</summary>
    private static PrimitiveInstance Capsule(Vector3 center, Vector3 axis, float radius, float halfLength, Vector4 color)
    {
        var rotation = FromTo(Vector3.UnitY, axis);
        var model = Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(center);
        return new PrimitiveInstance(PrimitiveKind.Capsule, model, new Vector3(radius, halfLength, 0f), color, MaterialKind.Emissive);
    }

    private static Quaternion FromTo(Vector3 from, Vector3 to)
    {
        var dot = Vector3.Dot(from, to);
        if (dot < -0.9999f)
        {
            return Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
        }

        var axis = Vector3.Cross(from, to);
        return Quaternion.Normalize(new Quaternion(axis, 1f + dot));
    }

    private enum EffectKind
    {
        Pop,
        Ring,
        Streak,
    }

    private readonly record struct Effect(EffectKind Kind, Vector3 Position, Vector3 Direction, Vector3 Velocity, Vector4 Color, float Life, float Strength, float Age = 0f);
}
