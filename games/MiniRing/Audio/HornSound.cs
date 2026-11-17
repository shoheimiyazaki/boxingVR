namespace MiniRing.Audio;

/// <summary>
/// ラウンドの合図の音の選択（団体のファイルの <c>bell</c>）。<c>gong</c> は <see cref="GongSound"/>（今のゴング、音のバイト列は変えない）、
/// <c>horn</c> は <see cref="HornSound"/>（低く太いホーン）。
/// </summary>
public static class BellSound
{
    /// <summary>音の id の一覧。</summary>
    public static readonly IReadOnlyList<string> Ids = ["gong", "horn"];

    /// <summary>id の合図の音（16-bit モノラル）。<see cref="RoundBell.None"/> なら null。知らない id は例外。</summary>
    public static byte[]? Synthesize(string id, RoundBell bell) => id switch
    {
        "gong" => GongSound.Synthesize(bell),
        "horn" => HornSound.Synthesize(bell),
        _ => throw new ArgumentException($"合図の音 '{id}' は知りません（{string.Join(", ", Ids)}）。", nameof(id)),
    };

    /// <summary>この id の音の鳴る長さの目安 [s]（音が収まる長さの確認用）。</summary>
    public static float Seconds(string id, RoundBell bell)
        => Synthesize(id, bell) is { } pcm ? pcm.Length / 2f / HitSound.SampleRate : 0f;
}

/// <summary>
/// ラウンドの合図のホーン（素材ファイルなし・合成、VFC の中継）。大きな会場の電気ホーンのように、低い基音（約 98 Hz）と整数倍の倍音（鋸歯状波に近い、
/// 高い倍音ほど弱い）が揃って鳴る太い 1 本の音。ほんの少し高さのずれた 2 本（うなり）を重ねて厚みを出し、立ち上がりは少しだけゆっくり（60 ms）、
/// 吹き終わりは短く落とす。始まりは 1 回、終わりは長く 1 回（<see cref="EndBlast"/>）。決まった音（乱数は使わない）。
/// 音程と長さは見た目に合わせた値で、実物の録音から測ったものではない（ゴングと違い、素材は使っていない）。
/// </summary>
public static class HornSound
{
    public const int SampleRate = HitSound.SampleRate;

    /// <summary>基音の高さ [Hz]（低い電気ホーン）。</summary>
    private const float Fundamental = 98f;

    /// <summary>2 本目の高さのずれ [Hz]（うなりの速さ。1 秒に 1.5 回のゆるいうなり）。</summary>
    private const float Detune = 1.5f;

    /// <summary>重ねる倍音の数。</summary>
    private const int Harmonics = 14;

    /// <summary>始まりの 1 回の長さ [s]。</summary>
    private const float StartBlast = 0.9f;

    /// <summary>終わりの 1 回の長さ [s]（試合の締めは長く）。</summary>
    private const float EndBlast = 1.6f;

    /// <summary>立ち上がりの長さ [s]。</summary>
    private const float Attack = 0.06f;

    /// <summary>吹き終わりを落とす長さ [s]。</summary>
    private const float Release = 0.18f;

    /// <summary>鳴り終わりの響き [s]（部屋の残響が尾を引く分）。</summary>
    private const float Tail = 0.4f;

    /// <summary>音量（ゴングと同じ 0.7。砕ける音は 1 前後）。</summary>
    public const float Loudness = 0.7f;

    /// <summary>ホーンの音（16-bit モノラル）。<see cref="RoundBell.None"/> なら null。</summary>
    public static byte[]? Synthesize(RoundBell bell)
    {
        var blast = bell switch
        {
            RoundBell.Start => StartBlast,
            RoundBell.End => EndBlast,
            _ => 0f,
        };
        if (blast <= 0f)
        {
            return null;
        }

        var samples = new float[(int)((blast + Tail) * SampleRate)];
        var blown = (int)(blast * SampleRate);
        var attack = Attack * SampleRate;
        var release = Release * SampleRate;
        for (var n = 0; n < samples.Length; n++)
        {
            var t = n / (float)SampleRate;
            // 吹いている間は 1、吹き終わりで 0 へ落とす（その後は響きだけ）。
            var envelope = MathF.Min(1f, n / attack) * (n < blown ? 1f : MathF.Max(0f, 1f - ((n - blown) / release)));
            var sum = 0f;
            for (var h = 1; h <= Harmonics; h++)
            {
                var weight = 1f / MathF.Pow(h, 0.9f);
                sum += weight * MathF.Sin(2f * MathF.PI * Fundamental * h * t);
                sum += weight * 0.8f * MathF.Sin(2f * MathF.PI * (Fundamental + Detune) * h * t);
            }

            samples[n] = envelope * sum;
        }

        HitSound.AddRoom(samples, 0.25f);
        return HitSound.ToPcm16(samples, Loudness);
    }
}
