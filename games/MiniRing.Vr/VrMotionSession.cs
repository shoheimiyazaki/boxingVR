using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Vr;

/// <summary>
/// VR の動きの記録を、ホストの操作（B/Y ボタン・<c>--record-motion</c>）で開始・停止してファイルへ書く。1 回の稽古 = 1 ファイル
/// （<c>motions/&lt;日時&gt;_&lt;武器&gt;.vrmotion</c>）。記録は付加機能なので、書けなくても試合は止めない（理由を表示して続ける）。
/// 試合の 1 ステップごとに <see cref="Step"/> を呼ぶ（試合のスレッドから）。終了時の <see cref="Dispose"/> は記録中なら保存する。
/// <paramref name="label"/> はファイル名の武器の所（既定は武器の名前。稽古では「katana_shomen」のように型を足す）、<paramref name="note"/> は記録の注（既定 "vr session"）。
/// どちらも記録を始めるときに読む。
/// </summary>
internal sealed class VrMotionSession(WeaponKind? weapon, string directory, bool startRecording, Func<string>? label = null, Func<string>? note = null) : IDisposable
{
    private readonly object _lock = new();
    private VrMotionRecorder? _recorder = startRecording ? new VrMotionRecorder(weapon?.ToString(), note?.Invoke() ?? "vr session") : null;
    private string? _label = startRecording ? label?.Invoke() : null;
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
                    _recorder = new VrMotionRecorder(weapon?.ToString(), note?.Invoke() ?? "vr session");
                    _label = label?.Invoke();
                    Console.WriteLine($"[vr] motion recording started{(_label is null ? "" : $" ({_label})")} (B/Y to stop)");
                }
                else
                {
                    SaveLocked();
                }
            }

            _recorder?.Add(_clock, frame.ToRaw(), command, mapper, puppetArmReach);
        }
    }

    /// <summary>記録中なら保存して止める（稽古で型を変えるとき）。</summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (_recorder is not null)
            {
                SaveLocked();
            }
        }
    }

    /// <summary>
    /// 記録中なら保存し、新しい記録を始める（稽古: 型を変える・作り直すたびに 1 ファイル。ファイル名と注はこのとき読み直す）。
    /// 記録していなければ始めるだけ。
    /// </summary>
    public void Restart()
    {
        lock (_lock)
        {
            if (_recorder is not null)
            {
                SaveLocked();
            }

            _recorder = new VrMotionRecorder(weapon?.ToString(), note?.Invoke() ?? "vr session");
            _label = label?.Invoke();
            _clock = 0f;
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
            var path = VrMotionClip.NewPath(directory, _label ?? weapon?.ToString());
            clip.Save(path);
            Console.WriteLine($"[vr] motion saved: {path} ({clip.Frames.Count} frames, {clip.Duration:0.0} s)");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[vr] motion NOT saved: cannot write to {directory} ({ex.Message})");
        }
    }
}
