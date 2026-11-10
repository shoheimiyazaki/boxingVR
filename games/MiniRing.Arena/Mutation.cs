using System.Globalization;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>調整器が共有する、種付き乱数での値の揺らし方（決定的）。</summary>
internal sealed class Mutation(int seed)
{
    private readonly Random _random = new(seed);
    private readonly List<string> _changes = [];

    public float NextSingle() => _random.NextSingle();

    /// <summary>この候補で変えた値の記録を始め直す。</summary>
    public void Begin() => _changes.Clear();

    public string Changes => _changes.Count == 0 ? "変更なし" : string.Join(", ", _changes);

    public void Note(string change) => _changes.Add(change);

    /// <summary><paramref name="chance"/> の確率で、正規分布の <paramref name="sigma"/> だけ値を揺らす（範囲に収めた値で記録）。</summary>
    public float Tweak(string label, float value, float sigma, float min, float max, float chance = 0.35f)
    {
        if (_random.NextSingle() > chance)
        {
            return value;
        }

        var next = Math.Clamp(value + (Normal() * sigma), min, max);
        if (next != value)
        {
            _changes.Add(string.Create(CultureInfo.InvariantCulture, $"{label} {value:0.00}→{next:0.00}"));
        }

        return next;
    }

    /// <summary>防御技の動きを揺らす（<paramref name="scale"/> で揺れ幅を変える。実戦での仕上げは小さく）。</summary>
    public DefenseMove Move(string prefix, DefenseMove m, float scale, float chance, bool handsFixed = false)
    {
        var moved = m with
        {
            HeadX = Tweak(prefix + "頭の横", m.HeadX, 0.08f * scale, -0.6f, 0.6f, chance),
            HeadY = Tweak(prefix + "頭の沈み", m.HeadY, 0.08f * scale, -0.7f, 0.2f, chance),
            HeadZ = Tweak(prefix + "頭の前後", m.HeadZ, 0.06f * scale, -0.5f, 0.4f, chance),
            EndX = m.Sweep ? Tweak(prefix + "抜ける横", m.EndX, 0.08f * scale, -0.6f, 0.6f, chance) : m.EndX,
            Duration = Tweak(prefix + "時間", m.Duration, 0.05f * scale, 0.15f, 0.7f, chance),
            Move = Tweak(prefix + "足の前後", m.Move, 0.2f * scale, -1f, 1f, chance),
            Step = Tweak(prefix + "足の横", m.Step, 0.2f * scale, -1f, 1f, chance),
            Rate = Tweak(prefix + "頭の速さ", m.Rate, 6f * scale, 6f, 60f, chance),
            // ブロックは常に拳を上げる（Validated が 1 に戻す）ので揺らさない（変えていない値を記録しない）。
            HandsUp = handsFixed ? m.HandsUp : Tweak(prefix + "拳を上げる", m.HandsUp, 0.2f * scale, 0f, 1f, chance),
            GloveWidth = Tweak(prefix + "拳の幅", m.GloveWidth, 0.04f * scale, 0f, 0.35f, chance),
            GloveHeight = Tweak(prefix + "拳の高さ", m.GloveHeight, 0.04f * scale, -0.3f, 0.2f, chance),
            GloveForward = Tweak(prefix + "拳の前", m.GloveForward, 0.05f * scale, 0.05f, 0.5f, chance),
            BodyDrop = Tweak(prefix + "肘を締める", m.BodyDrop, 0.08f * scale, 0.2f, 0.9f, chance),
            TempleWidth = Tweak(prefix + "こめかみ", m.TempleWidth, 0.04f * scale, 0f, 0.4f, chance),
        };
        return moved.Validated();
    }

    public float Normal()
    {
        // Box-Muller（種付き乱数から決定的に）。
        var u = 1f - _random.NextSingle();
        var v = _random.NextSingle();
        return MathF.Sqrt(-2f * MathF.Log(u)) * MathF.Cos(2f * MathF.PI * v);
    }
}
