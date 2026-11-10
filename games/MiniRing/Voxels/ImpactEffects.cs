using System.Numerics;
using Aigis.Render;

namespace MiniRing.Voxels;

/// <summary>
/// 当たった瞬間の光と空気（描画のみ。試合には響かない）。飛び散るボクセル（不透明な立方体）と見間違えないよう、
/// すべて半透明の柔らかい光の粒（<see cref="ParticleInstance"/>、ふちがぼける円）で作る:
/// <list type="bullet">
/// <item>光のにじみ: 当たった所に一瞬ふくらんで消える、淡い光。</item>
/// <item>空気の輪: 拳の向きに垂直に広がる、色のついた薄い膜の輪（半透明。明るい所でも見える）（重い 1 発だけ）。押し出された空気の波。</item>
/// <item>光の尾: 拳の向きへ流れる粒と、その後ろに伸びて薄れていく尾。</item>
/// <item>空気のもや: 重い 1 発で、当たった所にふわっと広がって消える白いもや。</item>
/// </list>
/// 色は打った側のグローブの色（白に寄せて淡く）、大きさ・速さ・数は打撃の強さ（0〜1）で決まる。
/// </summary>
public sealed class ImpactEffects
{
    /// <summary>空気の輪ともやを出す強さ（これより弱い 1 発は、にじみと尾だけ）。</summary>
    public const float RingStrength = 0.3f;

    /// <summary>いちばん濃い粒の不透明度。どれもこれ以下で、後ろが透けて見える。</summary>
    public const float MaxAlpha = 0.6f;

    private const int RingDots = 28;
    private const int TrailDots = 5;
    private const float GlowLife = 0.16f;
    private const float RingLife = 0.32f;
    private const float HazeLife = 0.5f;

    /// <summary>光らせる位置を、当たった面から打った側へ浮かせる距離 [m]。</summary>
    private const float SurfaceLift = 0.03f;

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
        // 当たった面の少し手前（打った側）で光らせる。面の上だと半分が体に隠れる。
        var surface = point - (dir * SurfaceLift);
        var tint = Vector4.Lerp(color, Vector4.One, 0.35f) with { W = 1f };
        _effects.Add(new Effect(EffectKind.Glow, surface, dir, Vector3.Zero, tint, GlowLife, s));
        if (s >= RingStrength)
        {
            _effects.Add(new Effect(EffectKind.Ring, surface, dir, Vector3.Zero, Vector4.Lerp(color, Vector4.One, 0.3f) with { W = 1f }, RingLife, s));
            for (var k = 0; k < 3; k++)
            {
                var drift = (-dir * 0.08f) + (new Vector3(jitter(), jitter(), jitter()) * 0.06f);
                _effects.Add(new Effect(EffectKind.Haze, surface + (drift * 0.1f), dir, drift, new Vector4(0.92f, 0.93f, 0.97f, 1f), HazeLife * (1f + (0.2f * jitter())), s));
            }
        }

        var trails = 3 + (int)(5f * s);
        for (var k = 0; k < trails; k++)
        {
            var heading = Vector3.Normalize(dir + (new Vector3(jitter(), jitter(), jitter()) * 0.35f));
            var speed = (1.2f + (2.4f * s)) * (1f + (0.25f * jitter()));
            _effects.Add(new Effect(EffectKind.Trail, point, heading, heading * speed, tint, 0.12f + (0.1f * s), s));
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

            // 空気に押し戻されるように減速する。
            _effects[i] = e with { Age = age, Position = e.Position + (e.Velocity * dt), Velocity = e.Velocity * MathF.Max(0f, 1f - (4f * dt)) };
        }
    }

    public void Clear() => _effects.Clear();

    /// <summary>光の粒を足す。光は加算（後ろを明るくするだけ）、もやは半透明で重ねる。</summary>
    public void WriteParticles(ICollection<ParticleInstance> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        foreach (var e in _effects)
        {
            var t = e.Age / e.Life;
            switch (e.Kind)
            {
                case EffectKind.Glow:
                {
                    // 最初の 2 割で一気にふくらみ、残りで薄れる。
                    var swell = t < 0.2f ? t / 0.2f : 1f;
                    var fade = t < 0.2f ? 1f : 1f - ((t - 0.2f) / 0.8f);
                    var size = (0.05f + (0.07f * e.Strength)) * (0.6f + (0.4f * swell));
                    into.Add(new ParticleInstance(e.Position, size, e.Color with { W = 0.5f * fade * (0.6f + (0.4f * e.Strength)) }, Blend: ParticleBlend.Additive));
                    into.Add(new ParticleInstance(e.Position, size * 0.35f, Vector4.One with { W = 0.5f * fade }, Blend: ParticleBlend.Additive));
                    break;
                }

                case EffectKind.Ring:
                {
                    var spread = 1f - ((1f - t) * (1f - t)); // 速く広がって、ゆるむ
                    var radius = 0.02f + ((0.05f + (0.09f * e.Strength)) * spread);
                    var fade = (1f - t) * (1f - t);
                    var side = Vector3.Normalize(Vector3.Cross(e.Direction, MathF.Abs(e.Direction.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
                    var up = Vector3.Cross(e.Direction, side);
                    // 隣の粒と重なる大きさにして、点の列ではなく、ぼけた 1 本の帯に見せる。
                    var dot = MathF.Max(0.012f, 2.6f * MathF.PI * radius / RingDots);
                    var color = e.Color with { W = 0.32f * fade * (0.6f + (0.4f * e.Strength)) };
                    // 加算ではなく半透明で重ねる: 明るい肌やキャンバスの上でも、色のついた薄い膜として見える。
                    for (var k = 0; k < RingDots; k++)
                    {
                        var angle = 2f * MathF.PI * k / RingDots;
                        var radial = (MathF.Cos(angle) * side) + (MathF.Sin(angle) * up);
                        into.Add(new ParticleInstance(e.Position + (radial * radius), dot, color, Blend: ParticleBlend.Alpha));
                    }

                    break;
                }

                case EffectKind.Trail:
                {
                    var fade = 1f - t;
                    var length = (0.02f + (0.04f * e.Strength)) * fade;
                    for (var k = 0; k < TrailDots; k++)
                    {
                        var back = k / (float)(TrailDots - 1);
                        var size = (0.012f + (0.008f * e.Strength)) * (1f - (0.6f * back));
                        var alpha = 0.45f * fade * (1f - (0.85f * back));
                        into.Add(new ParticleInstance(e.Position - (e.Direction * length * back), size, e.Color with { W = alpha }, Blend: ParticleBlend.Additive));
                    }

                    break;
                }

                case EffectKind.Haze:
                {
                    var size = (0.04f + (0.06f * e.Strength)) * (0.5f + (1.5f * MathF.Sqrt(t)));
                    var alpha = 0.16f * MathF.Sin(MathF.PI * MathF.Min(1f, t * 1.4f)) * (1f - t);
                    into.Add(new ParticleInstance(e.Position, size, e.Color with { W = alpha }, Blend: ParticleBlend.Alpha));
                    break;
                }
            }
        }
    }

    private enum EffectKind
    {
        Glow,
        Ring,
        Trail,
        Haze,
    }

    private readonly record struct Effect(EffectKind Kind, Vector3 Position, Vector3 Direction, Vector3 Velocity, Vector4 Color, float Life, float Strength, float Age = 0f);
}
