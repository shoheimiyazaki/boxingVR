using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Vr;

/// <summary>
/// VR の動きの記録を、ホストの操作（B/Y ボタン・<c>--record-motion</c>）で開始・停止してファイルへ書く。1 回の稽古 = 1 ファイル
/// （<c>motions/&lt;日時&gt;_&lt;武器&gt;.vrmotion</c>）。記録は付加機能なので、書けなくても試合は止めない（理由を表示して続ける）。
/// 試合の 1 ステップごとに <see cref="Step"/> を呼ぶ（試合のスレッドから）。終了時の <see cref="Dispose"/> は記録中なら保存する。
/// </summary>
internal sealed class VrMotionSession(WeaponKind? weapon, string directory, bool startRecording) : IDisposable
{
    private readonly object _lock = new();
    private VrMotionRecorder? _recorder = startRecording ? new VrMotionRecorder(weapon?.ToString(), "vr session") : null;
    private float _clock;

    public bool Recording
    {
        get
        {
            lock (_lock)
            {
                return _recorder is not null;
            }
        }
    }

    public void Step(in XrInputFrame frame, in FighterCommand command, VrPlayerMapper mapper, float puppetArmReach, float dt)
    {
        lock (_lock)
        {
            // 試合の刻みの和を時刻にする（記録は試合の外の付加機能。時計は使わない）。
            _clock += dt;
            if (frame.RecordTogglePressed)
            {
                if (_recorder is null)
                {
                    _recorder = new VrMotionRecorder(weapon?.ToString(), "vr session");
                    Console.WriteLine("[vr] motion recording started (B/Y to stop)");
                }
                else
                {
                    SaveLocked();
                }
            }

            _recorder?.Add(_clock, frame.ToRaw(), command, mapper, puppetArmReach);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_recorder is not null)
            {
                SaveLocked();
            }
        }
    }

    private void SaveLocked()
    {
        var recorder = _recorder;
        _recorder = null;
        var clip = recorder?.Build();
        if (clip is null)
        {
            Console.WriteLine("[vr] motion recording stopped (no frames)");
            return;
        }

        try
        {
            var path = VrMotionClip.NewPath(directory, weapon?.ToString());
            clip.Save(path);
            Console.WriteLine($"[vr] motion saved: {path} ({clip.Frames.Count} frames, {clip.Duration:0.0} s)");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[vr] motion NOT saved: cannot write to {directory} ({ex.Message})");
        }
    }
}
