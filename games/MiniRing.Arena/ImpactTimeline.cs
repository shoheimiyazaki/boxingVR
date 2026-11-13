using System.Globalization;
using MiniRing.Audio;

namespace MiniRing.Arena;

/// <summary>音の立ち上がりと、その状態を初めて描くコマの時刻を保存する診断記録。</summary>
internal sealed class ImpactTimeline
{
    private readonly List<string> _rows = ["event_seconds,visible_frame_seconds,pcm_onset_seconds,onset_minus_frame_ms,kind,source"];

    public void Add(ImpactVoice voice, double audioAt, double visibleFrameAt)
    {
        var first = Enumerable.Range(0, voice.Pcm.Length / 2)
            .FirstOrDefault(i => Math.Abs((int)BitConverter.ToInt16(voice.Pcm, i * 2)) > 32, -1);
        if (first < 0)
        {
            return; // 測定用閾値へ届かない波形は「0 msの立ち上がり」と扱わない。
        }
        var onset = audioAt + first / (double)HitSound.SampleRate;
        var kind = voice.Cue.Hit is not null ? "hit" : voice.Cue.Shatter is not null ? "shatter" : "contact";
        _rows.Add(string.Create(CultureInfo.InvariantCulture,
            $"{audioAt:F9},{visibleFrameAt:F9},{onset:F9},{(onset - visibleFrameAt) * 1000:F4},{kind},{voice.Cue.Source}"));
    }

    public void Write(string videoPath) => File.WriteAllLines(Path.ChangeExtension(videoPath, ".audio.csv"), _rows);
}
