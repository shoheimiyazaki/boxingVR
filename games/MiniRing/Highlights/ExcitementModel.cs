namespace MiniRing.Highlights;

/// <summary>
/// ドラマ（docs/EXCITEMENT.md §A。Ely・Frankel・Kamenica「Suspense and Surprise」2015）: 観客の見込み p_red(t) の動き。
/// <list type="bullet">
/// <item><b>驚き</b>（Surprise）= Σ |Δp|。見込みがどれだけ動いたか。逆転は大きな累積の揺れとして、特別な出来事にせずに出てくる。</item>
/// <item><b>はらはら</b>（Suspense）= 各時刻の「次の見込みの動きの大きさの期待」。結果の不確かさ 4p(1−p) × いまの見込みの揺れやすさ
/// （|Δp|/秒 の指数移動平均、半減期 <see cref="ExcitementTuning.VolatilityHalfLife"/>）。</item>
/// <item>一番大きな 1 回の揺れ（<see cref="ExcitementTuning.SwingSeconds"/> 秒の中の p の変化の最大）、見込みの入れ替わり（0.5 を、
/// ヒステリシス <see cref="ExcitementTuning.LeadHysteresis"/> 付きで横切った回数）。</item>
/// </list>
/// </summary>
public sealed record Drama(float[] Times, float[] P, float[] Suspense, float Surprise, float MeanSuspense, float LargestSwing, int LeadChanges, float Score)
{
    public static Drama From(IReadOnlyList<(float Time, float P)> belief, float seconds, ExcitementTuning t)
    {
        ArgumentNullException.ThrowIfNull(belief);
        ArgumentNullException.ThrowIfNull(t);
        var n = belief.Count;
        var times = belief.Select(b => b.Time).ToArray();
        var p = belief.Select(b => b.P).ToArray();
        var suspense = new float[n];
        var surprise = 0f;
        var volatility = 0f;
        var largest = 0f;
        var leads = 0;
        var side = 0;
        var meanSuspense = 0f;
        for (var i = 0; i < n; i++)
        {
            if (i > 0)
            {
                var dt = MathF.Max(1e-4f, times[i] - times[i - 1]);
                var dp = MathF.Abs(p[i] - p[i - 1]);
                surprise += dp;
                var keep = MathF.Pow(0.5f, dt / t.VolatilityHalfLife);
                volatility = (keep * volatility) + ((1f - keep) * dp / dt);
                for (var j = i - 1; j >= 0 && times[i] - times[j] <= t.SwingSeconds; j--)
                {
                    largest = MathF.Max(largest, MathF.Abs(p[i] - p[j]));
                }
            }

            suspense[i] = 4f * p[i] * (1f - p[i]) * volatility;
            meanSuspense += suspense[i];
            var now = p[i] >= 0.5f + t.LeadHysteresis ? 1 : p[i] <= 0.5f - t.LeadHysteresis ? -1 : 0;
            if (now != 0 && side != 0 && now != side)
            {
                leads++;
            }

            side = now != 0 ? now : side;
        }

        meanSuspense /= Math.Max(1, n);
        var minutes = MathF.Max(seconds, 1f) / 60f;
        var score = (t.SurpriseWeight * surprise / minutes) + (t.SuspenseWeight * meanSuspense) + (t.SwingWeight * largest) + (t.LeadChangeWeight * leads);
        return new Drama(times, p, suspense, surprise, meanSuspense, largest, leads, score);
    }
}

/// <summary>
/// 緩急（docs/EXCITEMENT.md §B）: 慣れのある興奮の曲線。
/// <list type="bullet">
/// <item>出来事ごとに、興奮が 重み × min(強さ, 上限) × 目新しさ だけ上がる。目新しさ = 1 / (1 + 同じ種類の最近の記憶 + 割合 × 同じ選手の最近の記憶)。
/// 記憶は出来事 1 つで 1 増え、半減期 <see cref="ExcitementTuning.NoveltyHalfLife"/> で減る。同じ打ち合いの繰り返しは興奮しなくなる。</item>
/// <item>見込みの揺れ（ドラマの驚き）も興奮を上げる: |Δp| × <see cref="ExcitementTuning.SurpriseArousal"/>。派手さとドラマが 1 本の曲線になる。</item>
/// <item>興奮は半減期 <see cref="ExcitementTuning.ArousalHalfLife"/> で基準（0）へ戻る。</item>
/// </list>
/// 曲線から: 退屈（<see cref="ExcitementTuning.BoredLevel"/> 未満）の割合・最長の退屈・山（ため → 山 → 抜け: 高さ
/// <see cref="ExcitementTuning.PeakProminence"/> 以上で、山の後に同じだけ下がって戻った山）の数・山の高さの平均・後半の山が前半より高いか。
/// 平らで高い曲線は慣れで下がり、平らで低い曲線は退屈が長く、上下して山が育つ曲線が高い点になる。
/// </summary>
public sealed record Arousal(float[] Curve, float SampleSeconds, float BoredShare, float LongestBored, int Peaks, float MeanPeak, float Growth, float Score)
{
    public float PeaksPerMinute(float seconds) => seconds > 0f ? Peaks / (seconds / 60f) : 0f;

    public static Arousal From(IReadOnlyList<Highlight> events, IReadOnlyList<(float Time, float P)> belief, float origin, float seconds, ExcitementTuning t)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(belief);
        ArgumentNullException.ThrowIfNull(t);
        var dt = t.ArousalSample;
        var n = Math.Max(1, (int)MathF.Ceiling(seconds / dt));
        var input = new float[n];
        var kindMemory = new Dictionary<HighlightKind, float>();
        var sideMemory = new Dictionary<bool, float>();
        var lastTime = origin;
        foreach (var e in events.OrderBy(e => e.Time))
        {
            var decay = MathF.Pow(0.5f, MathF.Max(0f, e.Time - lastTime) / t.NoveltyHalfLife);
            foreach (var k in kindMemory.Keys.ToList())
            {
                kindMemory[k] *= decay;
            }

            foreach (var k in sideMemory.Keys.ToList())
            {
                sideMemory[k] *= decay;
            }

            lastTime = e.Time;
            var novelty = 1f / (1f + kindMemory.GetValueOrDefault(e.Kind) + (t.SideNoveltyShare * sideMemory.GetValueOrDefault(e.Red)));
            var i = Math.Clamp((int)((e.Time - origin) / dt), 0, n - 1);
            input[i] += t.Weights.GetValueOrDefault(e.Kind) * MathF.Min(e.Intensity, t.IntensityCap) * novelty;
            kindMemory[e.Kind] = kindMemory.GetValueOrDefault(e.Kind) + 1f;
            sideMemory[e.Red] = sideMemory.GetValueOrDefault(e.Red) + 1f;
        }

        for (var j = 1; j < belief.Count; j++)
        {
            var i = Math.Clamp((int)((belief[j].Time - origin) / dt), 0, n - 1);
            input[i] += t.SurpriseArousal * MathF.Abs(belief[j].P - belief[j - 1].P);
        }

        var curve = new float[n];
        var keep = MathF.Pow(0.5f, dt / t.ArousalHalfLife);
        var level = 0f;
        for (var i = 0; i < n; i++)
        {
            level = (level * keep) + input[i];
            curve[i] = level;
        }

        // 退屈
        var bored = 0;
        var run = 0;
        var longestRun = 0;
        foreach (var v in curve)
        {
            run = v < t.BoredLevel ? run + 1 : 0;
            bored += v < t.BoredLevel ? 1 : 0;
            longestRun = Math.Max(longestRun, run);
        }

        // 山: 直前の谷から PeakProminence 以上上がり、その後 PeakProminence 以上下がった極大。
        var peaks = new List<float>();
        var trough = curve[0];
        var top = curve[0];
        var rising = true;
        foreach (var v in curve)
        {
            if (rising)
            {
                top = MathF.Max(top, v);
                if (top - trough >= t.PeakProminence && top - v >= t.PeakProminence)
                {
                    peaks.Add(top);
                    rising = false;
                    trough = v;
                }
                else if (v < trough)
                {
                    trough = v;
                    top = v;
                }
            }
            else
            {
                trough = MathF.Min(trough, v);
                if (v - trough >= 1e-3f && v > trough)
                {
                    rising = true;
                    top = v;
                }
            }
        }

        var meanPeak = peaks.Count > 0 ? peaks.Average() : 0f;
        var half = peaks.Count / 2;
        var growth = peaks.Count >= 2
            ? Math.Clamp((peaks.Skip(half).Average() - peaks.Take(Math.Max(1, half)).Average()) / (meanPeak + 1f), -1f, 1f)
            : 0f;
        var minutes = MathF.Max(seconds, 1f) / 60f;
        var boredShare = bored / (float)n;
        var longestBored = longestRun * dt;
        var score = MathF.Max(0f, (t.PeakWeight * peaks.Count / minutes) + (t.PeakHeightWeight * MathF.Min(meanPeak, t.PeakHeightCap)) + (t.GrowthWeight * growth)
            - (t.BoredPenalty * boredShare) - (t.LongBoredPenalty * Math.Clamp(longestBored / MathF.Max(seconds, 1f), 0f, 1f))); // 平らで低い曲線は 0（何も無い試合は 0 点）
        return new Arousal(curve, dt, boredShare, longestBored, peaks.Count, meanPeak, growth, score);
    }
}
