using System.Globalization;
using System.Numerics;
using Aigis.Render;

namespace MiniRing.Arena;

/// <summary>
/// 中継のバイタルモニター（VFC の下の帯、顔の窓の下）: 動脈圧・呼吸・脳波の波形と、心拍数・SpO2・脳波レベルの数字。見た目だけ（試合の計算に入れない）。
/// 値はすべて体のモデルから写す（CLAUDE.md §1.7。写し方は docs/TV_BROADCAST.md「バイタルモニター」、近道は docs/DEBT.md）:
/// 心拍 = <see cref="Fighter.HeartRate"/>（心臓の仕事の割合）、動脈圧の波 = <see cref="Fighter.HeartPhase"/> の拍子 × 心臓の働き、
/// 呼吸の波 = <see cref="Fighter.BreathPhase"/> × 肺の働き、SpO2 = 血の酸素 <see cref="Fighter.BloodOxygen"/>、脳波 = 意識 <see cref="Fighter.Consciousness"/>。
/// 説明の文字は出さない（ユーザーの指定: 色分けと波の形で「ヤバそう・戻ってきた・死んだ」が伝わればよい）。描くたびに 1 コマ分の値を足して流す（1 コマに 1 回呼ぶ）。
/// </summary>
internal sealed class VitalsMonitor
{
    /// <summary>流す波の長さ [コマ]（30 fps で約 5.7 秒。同じ幅に 4 秒を流した最初の版の 7 割の速さ: 2026-11-19、ユーザー）。</summary>
    private const int Samples = 171;

    /// <summary>0 の線（各段の下の端）の色: 薄く。</summary>
    private static readonly Vector4 ZeroLine = new(1f, 1f, 1f, 0.18f);

    private static readonly Vector4 Pressure = new(1f, 0.25f, 0.25f, 1f);
    private static readonly Vector4 Respiration = new(1f, 0.85f, 0.2f, 1f);
    private static readonly Vector4 Brain = new(0.75f, 0.45f, 1f, 1f);
    private static readonly Vector4 Pulse = new(0.3f, 1f, 0.45f, 1f);
    private static readonly Vector4 Oxygen = new(0.3f, 0.9f, 1f, 1f);
    private static readonly Vector4 Grid = new(1f, 1f, 1f, 0.08f);

    private readonly Dictionary<Fighter, Trace> _traces = [];

    /// <summary>1 人の 3 本の波（0..1 に正規化した値）。</summary>
    private sealed class Trace
    {
        public readonly float[] Pressure = new float[Samples];
        public readonly float[] Breath = new float[Samples];
        public readonly float[] Brain = new float[Samples];
        public int Head;
        public int Count;
        public int Frame;
    }

    /// <summary>
    /// 動脈圧の 1 拍の形（0..1）: 速い立ち上がり（収縮期）・ゆっくりした下り・途中の小さな切れ込みの戻り（重複切痕）。<paramref name="beat"/> は拍の中の位置 0..1。
    /// </summary>
    internal static float PressureShape(float beat)
    {
        var p = beat - MathF.Floor(beat);
        var rise = 0.12f;
        var main = p < rise ? MathF.Sin(p / rise * MathF.PI * 0.5f) : MathF.Exp(-(p - rise) * 3.2f);
        var notch = 0.14f * MathF.Exp(-MathF.Pow((p - 0.42f) / 0.05f, 2f));
        return Math.Clamp(main + notch, 0f, 1f);
    }

    /// <summary>
    /// 動脈圧 [mmHg] の値（0..1 に正規化した縦の位置は ÷ <see cref="PressureScale"/>）。拡張期は心臓の働きに比例（75 mmHg）、脈の振れ幅は心臓の働き × 送りきれている割合（45 mmHg）。死ねば 0。
    /// </summary>
    internal static float ArterialPressure(Fighter f)
    {
        if (f.IsDead)
        {
            return 0f;
        }

        var heart = f.HeartFunction;
        var swing = 45f * heart * (1f - (0.6f * f.Pallor));
        return (75f * heart) + (swing * PressureShape(f.HeartPhase));
    }

    /// <summary>動脈圧の波の縦の範囲 [mmHg]（下の端・上の端）。ふつうの 75〜120 が縦の半分ほどを占める。死ねば下の端に張り付く。</summary>
    private const float PressureFloor = 40f;

    private const float PressureScale = 140f;

    /// <summary>
    /// SpO2 [%]: 血の酸素の残り（0..1）を、ヘモグロビンの酸素飽和の曲線に近い形で写す（残りが半分まではゆっくり下がり、それより下で急に落ちる）。死ねば null（表示は「--」）。
    /// 近道: 血の酸素は酸素の蓄えの割合で、本当の飽和度ではない（docs/DEBT.md）。
    /// </summary>
    internal static float? SpO2(Fighter f) => f.IsDead ? null : 99f * MathF.Pow(Math.Clamp(f.BloodOxygen, 0f, 1f), 0.3f);

    /// <summary>呼吸の波（0..1）: 吸って吐く形（位相の余弦）× 肺の働き × 息の深さ（息が上がるほど深い）。死ねば 0。</summary>
    internal static float BreathWave(Fighter f)
    {
        if (f.IsDead)
        {
            return 0f;
        }

        var depth = f.LungFunction * (0.35f + (0.65f * f.Winded));
        return depth * (0.5f - (0.5f * MathF.Cos(2f * MathF.PI * f.BreathPhase)));
    }

    /// <summary>脳波の線（0..1）: 意識の高さを中心に、意識に比例した細かい揺れ（種は回数から: 見た目だけ）。意識が無いほど平らで低い。</summary>
    internal static float BrainWave(Fighter f, int frame)
    {
        var level = f.Consciousness;
        var noise = (Hash(frame) - 0.5f) * 0.35f * level;
        return Math.Clamp((0.15f + (0.7f * level)) + noise, 0f, 1f);
    }

    private static float Hash(int n)
    {
        unchecked
        {
            var x = (uint)n * 747796405u + 2891336453u;
            x = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u;
            return ((x >> 22) ^ x) / (float)uint.MaxValue;
        }
    }

    /// <summary>1 コマ分の値を足す。</summary>
    public void Sample(Fighter f)
    {
        if (!_traces.TryGetValue(f, out var t))
        {
            t = new Trace();
            _traces[f] = t;
        }

        t.Pressure[t.Head] = Math.Clamp((ArterialPressure(f) - PressureFloor) / (PressureScale - PressureFloor), 0f, 1f);
        t.Breath[t.Head] = BreathWave(f);
        t.Brain[t.Head] = BrainWave(f, t.Frame);
        t.Head = (t.Head + 1) % Samples;
        t.Count = Math.Min(Samples, t.Count + 1);
        t.Frame++;
    }

    /// <summary>
    /// 1 人のモニターを描く: 左に 3 本の波（上から動脈圧・呼吸・脳波）、右に数字（心拍 bpm・SpO2 %・脳波 %）。<paramref name="topLeft"/> から幅 <paramref name="width"/>、高さ <paramref name="height"/>。
    /// </summary>
    public void Draw(Fighter f, Vector2 topLeft, float width, float height, List<ScreenTextDraw> text, List<UiShapeDraw> shapes)
    {
        if (!_traces.TryGetValue(f, out var t) || t.Count == 0 || width < 120f || height < 30f)
        {
            return;
        }

        var numbers = width * 0.34f;
        var waveWidth = width - numbers - 10f;
        var row = height / 3f;
        var rows = new[] { (t.Pressure, Pressure), (t.Breath, Respiration), (t.Brain, Brain) };
        for (var r = 0; r < rows.Length; r++)
        {
            var top = topLeft.Y + (r * row);
            shapes.Add(Box(topLeft.X, top + row - 2f, topLeft.X + waveWidth, top + row - 1f, Grid));
            var zero = top + 2f + (row - 5f);
            shapes.Add(Box(topLeft.X, zero - 0.5f, topLeft.X + waveWidth, zero + 0.5f, ZeroLine));
            Wave(rows[r].Item1, t, new Vector2(topLeft.X, top + 2f), waveWidth, row - 5f, rows[r].Item2, shapes);
        }

        var x = topLeft.X + waveWidth + 10f;
        var size = MathF.Min(19f, row * 0.8f);
        var hr = f.IsDead ? "--" : Math.Round(f.HeartRate).ToString(CultureInfo.InvariantCulture);
        var spo2 = SpO2(f) is { } s ? Math.Round(s).ToString(CultureInfo.InvariantCulture) : "--";
        var eeg = Math.Round(f.Consciousness * 100f).ToString(CultureInfo.InvariantCulture);
        Number(text, $"{hr} bpm", x, topLeft.Y, size, Pulse);
        Number(text, $"SpO2 {spo2} %", x, topLeft.Y + row, size, Oxygen);
        Number(text, $"EEG {eeg} %", x, topLeft.Y + (2f * row), size, Brain);
    }

    private static void Number(List<ScreenTextDraw> text, string value, float x, float y, float size, Vector4 color) =>
        text.Add(new ScreenTextDraw(value, new Vector2(x, y), size, color));

    /// <summary>波を細い四角の列で描く（隣のコマとの間を縦の四角でつなぐ）。古い方が左、新しい方が右。</summary>
    private static void Wave(float[] values, Trace t, Vector2 topLeft, float width, float height, Vector4 color, List<UiShapeDraw> shapes)
    {
        var step = width / (Samples - 1);
        var start = Samples - t.Count;
        float? previous = null;
        for (var i = 0; i < t.Count; i++)
        {
            var value = values[(t.Head - t.Count + i + Samples) % Samples];
            var y = topLeft.Y + ((1f - value) * height);
            var x = topLeft.X + ((start + i) * step);
            var from = previous ?? y;
            shapes.Add(Box(x - 1f, MathF.Min(from, y) - 1f, x + step, MathF.Max(from, y) + 1f, Shade(color, value)));
            previous = y;
        }
    }

    /// <summary>
    /// 波の高さで色を変える: 上（1）ほど明るく鮮やか、下（0）ほどくすむ。下の端でも色相が分かる程度に留める（彩度 55 %・明るさ 60 % まで）。
    /// </summary>
    internal static Vector4 Shade(Vector4 color, float value)
    {
        var v = Math.Clamp(value, 0f, 1f);
        var gray = (0.3f * color.X) + (0.59f * color.Y) + (0.11f * color.Z);
        var saturation = 0.55f + (0.45f * v);
        var brightness = 0.6f + (0.4f * v);
        var rgb = (new Vector3(gray) + ((new Vector3(color.X, color.Y, color.Z) - new Vector3(gray)) * saturation)) * brightness;
        return new Vector4(rgb, color.W);
    }

    private static UiShapeDraw Box(float left, float top, float right, float bottom, Vector4 color) =>
        new(new Vector2((left + right) * 0.5f, (top + bottom) * 0.5f), new Vector2(MathF.Max(1f, right - left), MathF.Max(1f, bottom - top)), color);
}
