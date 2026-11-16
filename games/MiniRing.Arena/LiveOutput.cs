using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace MiniRing.Arena;

/// <summary>生配信の設定（<c>video --live</c>）。</summary>
/// <param name="Url">出口。<c>srt://</c>・<c>udp://</c>（mpegts）、<c>rtmp://</c>・<c>rtmps://</c>（flv）、またはファイルのパス（.ts / .flv / .mkv）。</param>
/// <param name="Encoder"><c>nvenc</c>（h264_nvenc）か <c>x264</c>（libx264。NVENC の無い PC 向け）。</param>
/// <param name="Bitrate">映像の速さ（ffmpeg の書き方。<c>6M</c> など）。</param>
/// <param name="VoicesManifest">実況の声の manifest.json（tools/commentary/Synthesize.ps1 の出力）。null なら声なし。</param>
/// <param name="WaitSeconds">受け手が繋ぐのを待つ上限 [s]（送り側の ffmpeg が出力を開くまで）。過ぎたら全部止めて失敗にする（黙って待ち続けない）。</param>
/// <param name="DelaySeconds">配信の遅延 [s]。この秒数ぶんのコマを作ってから送り側を始める（受け手はこれだけ遅れるが、なめらかな実時間の流れを見る）。</param>
internal sealed record LiveOptions(string Url, string Encoder = LiveOutput.DefaultEncoder, string Bitrate = LiveOutput.DefaultBitrate, string? VoicesManifest = null,
    double WaitSeconds = LiveOutput.DefaultWaitSeconds, double DelaySeconds = LiveOutput.DefaultDelaySeconds);

/// <summary>
/// 容量（バイト）で上限のある、順序つきの受け渡し。いっぱいなら <see cref="Enqueue"/> が空くまで待ち（落とさない）、
/// 空なら <see cref="TryDequeue"/> が来るまで待つ。<see cref="Complete"/> のあと空になれば <see cref="TryDequeue"/> は false。
/// </summary>
internal sealed class LiveByteQueue(long capacityBytes)
{
    private readonly Queue<byte[]> _chunks = new();
    private readonly object _gate = new();
    private long _bytes;
    private bool _completed;

    /// <summary>いま溜まっているバイト数。</summary>
    public long Bytes
    {
        get { lock (_gate) { return _bytes; } }
    }

    /// <summary>入れる。容量を超えるなら空くまで待つ（1 つが容量より大きくても、空なら入れる）。取り消されたら <see cref="OperationCanceledException"/>。</summary>
    public void Enqueue(byte[] chunk, CancellationToken token)
    {
        lock (_gate)
        {
            while (_bytes > 0 && _bytes + chunk.Length > capacityBytes)
            {
                token.ThrowIfCancellationRequested();
                Monitor.Wait(_gate, 100);
            }

            token.ThrowIfCancellationRequested();
            _chunks.Enqueue(chunk);
            _bytes += chunk.Length;
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>もう入れない印。</summary>
    public void Complete()
    {
        lock (_gate)
        {
            _completed = true;
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>取り出す。空なら入るまで待ち、<see cref="Complete"/> のあと空なら false。</summary>
    public bool TryDequeue(out byte[] chunk, CancellationToken token)
    {
        lock (_gate)
        {
            while (_chunks.Count == 0)
            {
                if (_completed)
                {
                    chunk = [];
                    return false;
                }

                token.ThrowIfCancellationRequested();
                Monitor.Wait(_gate, 100);
            }

            chunk = _chunks.Dequeue();
            _bytes -= chunk.Length;
            Monitor.PulseAll(_gate);
            return true;
        }
    }
}

/// <summary>
/// 生配信の出口。2 段の ffmpeg と、その間の遅延の緩衝でできている:
/// <list type="number">
/// <item>エンコード側 A: 映像（標準入力の rawvideo RGBA）と音（TCP の s16le）を、作れる速さで mpegts にして標準出力へ出す（実時間の歩調は取らない）。</item>
/// <item>中継: A の標準出力を読み、バイト数で上限のある緩衝（<see cref="QueueCapacityBytes"/>）へ入れる。いっぱいなら A が止まり、作る側も止まる（落とさない）。</item>
/// <item>送り側 B: <c>-re</c> で実時間に読み、<c>-c copy</c> で URL（またはファイル）へ流す。作る側が <c>--delay</c> 秒ぶんのコマを作ってから始める。</item>
/// </list>
/// 物理・描画のコマごとの揺れ（リプレイの追いつきで 1 コマが数秒かかる・他の作業との取り合い）は、この遅延の緩衝が吸収する
/// （受け手は遅延だけ遅れるが、なめらかな実時間で見る）。
/// </summary>
internal sealed partial class LiveOutput : IDisposable
{
    public const string DefaultEncoder = "nvenc";
    public const string DefaultBitrate = "6M";
    public const double DefaultWaitSeconds = 120;

    /// <summary>
    /// 配信の遅延の既定 [s]。リプレイの追いつき（最大 4〜5 秒）に、同じ PC の他の作業との CPU の取り合いが重なっても途切れない長さ。
    /// 8 秒では、CPU 80 % 前後の負荷の下で緩衝の残りが 0.5 秒まで減った（2026-11-16）ので、ほぼ倍にした。6 Mb/s で約 11 MB。
    /// </summary>
    public const double DefaultDelaySeconds = 15;

    /// <summary>既定の出口: こちらが SRT の待ち側になり、OBS のメディアソースが <c>srt://127.0.0.1:9000</c> へ繋ぐ。</summary>
    public const string DefaultUrl = "srt://127.0.0.1:9000?mode=listener&pkt_size=1316";

    /// <summary>
    /// エンコード側 A へ映像を渡すのを遅らせる量 [s]。音は「いまの時刻」から先へ置かれ、いまより前へは置かないので、作ったコマの時刻
    /// <c>f / fps</c> より前の標本はもう全部置かれている。ffmpeg は映像のコマの時刻より先の音が届くまで映像をエンコーダへ送らず、
    /// 入力の緩衝は数コマしかないので、音がコマの時刻に遅れて届くと映像の標準入力が詰まって互いに待ち合う（測定で起きた）。
    /// そこで映像を溜めて遅らせ、音はその渡した映像より先まで書く。
    /// </summary>
    public const double VideoHoldSeconds = 0.5;

    /// <summary>エンコード側へ渡した映像より先に書いてある音の長さ [s]（溜める 0.5 秒 − 音の遅らせ 0.25 秒）。試合中も終わりも、この前後関係を保つ。</summary>
    private const double AudioLeadSeconds = VideoHoldSeconds - AudioDelaySeconds;

    /// <summary>作ったコマの時刻から音を遅らせて書く安全の余裕 [s]（その時刻までの音は確定している。書いたら直せないので少し残す）。</summary>
    public const double AudioDelaySeconds = 0.25;

    /// <summary>遅延の緩衝の容量 [byte]。6 Mb/s の流れで約 85 秒ぶん（1 試合ぶんが入る）。いっぱいになったら作る側が止まる。</summary>
    public const long QueueCapacityBytes = 64L * 1024 * 1024;

    /// <summary>緩衝がこの割合（容量に対して）まで溜まったら、<c>--delay</c> に満たなくても送り側を始める。始めないと、緩衝がいっぱいで中継 → A → 作る側が止まり、遅延に達する前に詰まって永久に待つ。</summary>
    private const double StartSenderFillRatio = 0.75;

    /// <summary>送り側を始めてから、SRT の待ち受けが実際に始まるまでの目安 [s]。「受け手を待っています」はこれが過ぎてから出す（先に出すと、受け手が早く繋ぎに来て失敗する）。</summary>
    private const double ListenerReadySeconds = 1.5;

    /// <summary>緩衝の残り（作ったコマの時間 − 送り側が出した時間）がこれを下回ったら「受け手が途切れる」とみなす [s]。</summary>
    public const double UnderrunLeadSeconds = 0.5;

    /// <summary>映像の SRT の 1 パケットの大きさ [byte]。mpegts の 188 byte × 7 = 1316（UDP の MTU 1500 に収まる）。</summary>
    private const string SrtPacketSize = "pkt_size=1316";

    /// <summary>ffmpeg の標準エラーのうち、失敗したときに添える行の数（A・B それぞれ）。</summary>
    private const int ErrorTailLines = 12;

    /// <summary>ffmpeg が音の TCP を聞き始めるのを待つ上限 [s]（ffmpeg の起動は数秒以内。これを過ぎたら起動に失敗している）。</summary>
    private const double AudioConnectSeconds = 10;

    private const int ConnectRetryMilliseconds = 100;

    /// <summary>受け手を待っている間、待っていることを知らせる間隔 [s]。</summary>
    private const double WaitingReportSeconds = 10;

    private readonly LiveOptions _options;
    private readonly int _fps;
    private readonly int _sampleRate;
    private readonly string _url;
    private readonly Process _encoder;
    private readonly Stream _video;
    private readonly Task<TcpClient> _audioConnection;
    private readonly CancellationTokenSource _cancel = new();
    private readonly LiveByteQueue _queue;
    private readonly long _queueCapacity;
    private readonly TaskCompletionSource _firstSent = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _relay;
    private readonly TaskCompletionSource _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly TextWriter _log;
    private readonly Queue<byte[]> _held = new();
    private readonly Queue<string> _encoderTail = new();
    private readonly Queue<string> _senderTail = new();
    private readonly int _holdFrames;
    private readonly long _delayFrames;
    private readonly object _gate = new();
    private Process? _sender;
    private Thread? _writer;
    private Thread? _watcher;
    private TcpClient? _audioClient;
    private NetworkStream? _audio;
    private int _audioWritten;
    private long _frames; // 作ったコマの数
    private long _sent; // エンコード側 A へ渡したコマの数
    private volatile bool _senderStarted;
    private volatile bool _finishing;
    private volatile bool _producerDone;
    private volatile bool _faulted;
    private string? _faultWhat;
    private Exception? _faultInner;
    private bool _disposed;
    private double _minLead = double.PositiveInfinity;
    private int _underruns;
    private bool _below;

    public LiveOutput(LiveOptions options, int width, int height, int fps, int sampleRate, float[] audio, LiveAudioMixer mixer, TextWriter log,
        long queueCapacityBytes = QueueCapacityBytes)
    {
        _options = options;
        _queueCapacity = queueCapacityBytes;
        _queue = new LiveByteQueue(queueCapacityBytes);
        _fps = fps;
        _sampleRate = sampleRate;
        _url = options.Url;
        _log = TextWriter.Synchronized(log); // ffmpeg の標準エラーは別の糸から書く
        _holdFrames = (int)Math.Round(VideoHoldSeconds * fps);
        _delayFrames = (long)Math.Ceiling(options.DelaySeconds * fps);
        Format(options.Url); // 形式の分からない出口は、ffmpeg を起動する前にはねる

        // 空いたポートを選ぶ（閉じてから ffmpeg が聞く。その間に取られる競合は小さく、取られたら ffmpeg が失敗して知らせる）。
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        _encoder = Start(EncodeArguments(options, width, height, fps, sampleRate, port), redirectOutput: true, _encoderTail, "encode");
        _video = _encoder.StandardInput.BaseStream;

        // 中継: エンコード側の標準出力 → 緩衝。
        _relay = new Thread(Relay) { IsBackground = true, Name = "live-relay" };
        _relay.Start();

        // 音の接続は別の糸で: ffmpeg は入力を順に開くので、映像の最初のコマを書くより前に音が要る・後に要るのどちらでも詰まらないように。
        // 繋いだらすぐ、最初の VideoHoldSeconds ぶん（始めの数百 ms は地の歓声と声だけで、もう確定している）を書いて ffmpeg の入力の確認を通す。
        var token = _cancel.Token;
        _audioConnection = Task.Run(() =>
        {
            var client = ConnectAudio(port, token);
            try
            {
                WriteAudioBytes(client.GetStream(), audio, mixer, 0, (int)(VideoHoldSeconds * sampleRate));
                _audioWritten = (int)(VideoHoldSeconds * sampleRate);
                return client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }, token);
        // 音の接続に失敗したら全部止める: メイン糸は映像の書き込みで詰まっているかもしれず、止めれば書き込みが失敗して本当の原因が出る。
        _audioConnection.ContinueWith(t => Fault("音の接続に失敗しました", t.Exception?.GetBaseException()), TaskContinuationOptions.OnlyOnFaulted);

        _log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"配信は {options.DelaySeconds:0.#} 秒ぶんのコマを作ってから、受け手の待ち受けを始めます（受け手は「受け手を待っています」が出てから繋いでください）"));
    }

    /// <summary>
    /// 緩衝の残り（作ったコマの時間 − 送り側が出した時間）の、受け手が繋いでから作り終えるまでの最小 [s]（診断。受け手が繋ぐ前・作り終えたあとは数えない）。
    /// まだ測っていなければ <see cref="double.PositiveInfinity"/>。
    /// </summary>
    public double MinLeadSeconds
    {
        get { lock (_gate) { return _minLead; } }
    }

    /// <summary>残りが <see cref="UnderrunLeadSeconds"/> を下回った回数（受け手が途切れる回数）。</summary>
    public int Underruns
    {
        get { lock (_gate) { return _underruns; } }
    }

    /// <summary>エンコード側 A へ映像を渡す書き込みで止まった時間の合計 [s]（診断。大きいなら A・緩衝・送り側が追いついていない）。</summary>
    public double SendSeconds { get; private set; }

    public int FramesWritten => (int)_sent;

    /// <summary>
    /// コマを 1 枚作ったことを伝える。コマは <see cref="VideoHoldSeconds"/> ぶん溜めてから古い順にエンコード側へ渡し、作った時刻までの音を TCP へ書く。
    /// 実時間の歩調は取らない（作れる速さで進む。緩衝がいっぱいになれば止まる）。<c>--delay</c> ぶん作ったら送り側を始める。
    /// </summary>
    public void WriteFrame(byte[] rgba, float[] audio, LiveAudioMixer mixer)
    {
        ThrowIfFaulted();
        _held.Enqueue(rgba);
        Interlocked.Increment(ref _frames);
        if (_held.Count > _holdFrames)
        {
            Send(_held.Dequeue());
        }

        if (_sent > 0)
        {
            // 音は映像を渡し始めたあとから（ffmpeg は入力を順に開くので、映像の最初のコマを待っている間は音の接続も開かない）。
            WriteAudioUpTo(audio, mixer, (int)(Math.Max(0.0, (Volatile.Read(ref _frames) / (double)_fps) - AudioDelaySeconds) * _sampleRate));
        }

        if (!_senderStarted && Volatile.Read(ref _frames) >= _delayFrames)
        {
            StartSender();
        }
    }

    /// <summary>
    /// 音を <paramref name="position"/>（標本の番号）の手前まで書く。書いてあれば何もしない。
    /// <paramref name="audio"/> の長さを超えた分は無音（映像の最後のリプレイは試合の時間を進めずにコマを足すので、映像の方が長くなりうる）。
    /// </summary>
    public void WriteAudioUpTo(float[] audio, LiveAudioMixer mixer, int position)
    {
        try
        {
            if (_audio is null)
            {
                _audioClient = _audioConnection.GetAwaiter().GetResult(); // 接続と最初の書き出しが済むのを待つ（_audioWritten はそのあとに確定する）
                _audio = _audioClient.GetStream();
            }

            if (position <= _audioWritten)
            {
                return;
            }

            WriteAudioBytes(_audio, audio, mixer, _audioWritten, position);
            _audioWritten = position;
        }
        catch (Exception e) when (e is IOException or SocketException or AggregateException or ObjectDisposedException or OperationCanceledException)
        {
            // 失敗で取り消された接続の例外（TaskCanceledException）も、記録された最初の原因（と標準エラー）で報告する。
            throw Failed("音の書き出しに失敗しました", e);
        }
    }

    /// <summary>
    /// 終わり: 残りの音と溜めたコマをエンコード側へ渡して閉じ、緩衝の中身がすべて送り側から出る（実時間で流れ切る）のを待つ。
    /// 作った時間が <c>--delay</c> に満たなかったなら、ここで送り側を始める。
    /// </summary>
    public void Finish(float[] audio, LiveAudioMixer mixer)
    {
        _finishing = true; // これ以降の残りの減りは「作り終えた」ためで、途切れではない
        var made = Volatile.Read(ref _frames);
        var end = (int)(made / (double)_fps * _sampleRate); // 試合は終わっているので、音はもう全部確定している
        while (_held.Count > 0)
        {
            // 溜めたコマを渡す前に、そのコマより AudioLeadSeconds 先までの音を書く（試合中の WriteFrame と同じ前後関係）。
            // 映像を全部渡してから音を書くと、エンコード側は映像の時刻より先の音を待って標準入力を読まなくなり、720p の 1 コマ（3.7 MB）が
            // パイプに入らず止まったままになった（2026-11-16、LiveOutput_Finish_At720p_DoesNotDeadlockOnTheHeldFrames）。
            // 最初のコマを渡す前は音を待たない（ffmpeg は最初の映像のコマを読むまで音の接続を聞き始めない）。
            if (_sent > 0)
            {
                WriteAudioUpTo(audio, mixer, Math.Min(end, (int)((((_sent + 1) / (double)_fps) + AudioLeadSeconds) * _sampleRate)));
            }

            Send(_held.Dequeue());
        }

        WriteAudioUpTo(audio, mixer, end);

        _producerDone = true;
        try
        {
            _video.Close();
            _audio?.Close();
        }
        catch (IOException e)
        {
            throw Failed("映像の入力を閉じられません", e);
        }

        StartSender();

        // 送り側が緩衝をすべて出し切る = 受け手が繋ぐまで（--wait）＋ 流れの長さ。それより長くかかるなら止まっている。
        var deadline = Stopwatch.StartNew();
        var limit = _options.WaitSeconds + (made / (double)_fps) + 60;
        ThrowIfFaulted();
        while (!_writer!.Join(500))
        {
            ThrowIfFaulted();
            if (deadline.Elapsed.TotalSeconds > limit)
            {
                throw Failed("送り側が終わりません", null);
            }
        }

        ThrowIfFaulted();
        var sender = SenderProcess()!;
        if (!sender.WaitForExit(30000))
        {
            throw Failed("送り側の ffmpeg が終わりません", null);
        }

        sender.WaitForExit(); // 標準エラー・標準出力の読み切りを待つ
        _encoder.WaitForExit();
        if (_encoder.ExitCode != 0)
        {
            throw Failed($"エンコード側の ffmpeg が終了コード {_encoder.ExitCode} で終わりました", null);
        }

        if (sender.ExitCode != 0)
        {
            throw Failed($"送り側の ffmpeg が終了コード {sender.ExitCode} で終わりました", null);
        }

        ThrowIfFaulted();
        _log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"live: {made} frames ({made / (double)_fps:0.0}s), delay {_options.DelaySeconds:0.#}s, min lead {FormatLead(MinLeadSeconds)}, underruns {Underruns}"));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancel.Cancel();
        Kill(_encoder);
        Kill(SenderProcess());
        try
        {
            _audioConnection.Wait(3000); // 糸が Process に触れなくなってから片付ける
        }
        catch (AggregateException)
        {
        }

        _relay.Join(3000);
        _writer?.Join(3000);
        _watcher?.Join(3000);
        _audio?.Dispose();
        if (_audioClient is not null)
        {
            _audioClient.Dispose();
        }
        else if (_audioConnection.IsCompletedSuccessfully)
        {
            _audioConnection.Result.Dispose();
        }

        _encoder.WaitForExit(3000);
        _encoder.Dispose();
        var sender = SenderProcess();
        if (sender is not null)
        {
            sender.WaitForExit(3000);
            sender.Dispose();
        }

        // _cancel は破棄しない: まだ動いている糸が Cancel・WaitHandle に触れて ObjectDisposedException で落ちるのを避ける（軽い資源で、プロセスの終わりに片付く）。
    }

    private Process? SenderProcess()
    {
        lock (_gate)
        {
            return _sender;
        }
    }

    // ---- 送り側 B ----

    /// <summary>送り側の ffmpeg（<c>-re</c> で実時間に読み、<c>-c copy</c> で流す）を始め、緩衝 → B の標準入力の書き手と、受け手の待ちの見張りを始める。</summary>
    private void StartSender()
    {
        lock (_gate)
        {
            // 始めるのは 1 回だけ（作る側の遅延到達と、中継の緩衝 75 % 到達のどちらが先でもよい）。失敗の記録と同じ錠の下で、
            // 失敗が先なら始めない・あとなら Fault が止める（B が SRT のポートを持ったまま残らない）。
            if (_sender is not null || _faulted)
            {
                return;
            }

            _sender = Start(SendArguments(_url), redirectOutput: true, _senderTail, "send", OnSenderProgress);
            _senderStarted = true;
        }

        _writer = new Thread(WriteToSender) { IsBackground = true, Name = "live-writer" };
        _writer.Start();
        _watcher = new Thread(Watch) { IsBackground = true, Name = "live-watch" };
        _watcher.Start();
    }

    private void WriteToSender()
    {
        var token = _cancel.Token;
        try
        {
            var stdin = SenderProcess()!.StandardInput.BaseStream;
            while (_queue.TryDequeue(out var chunk, token))
            {
                stdin.Write(chunk);
            }

            stdin.Close();
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException e)
        {
            Fault("送り側の ffmpeg へ書けません", e);
        }
    }

    /// <summary>
    /// 送り側の <c>-progress</c>: <c>total_size</c> が 0 を超えたら、出力が開いて最初の書き込みが済んだ = 受け手が繋いだ
    /// （SRT の待ち側は繋ぐまで出力を開かない）。<c>out_time_us</c> は送り側が出した流れの時間で、作ったコマの時間との差が緩衝の残り。
    /// </summary>
    private void OnSenderProgress(string line)
    {
        if (line.StartsWith("total_size=", StringComparison.Ordinal))
        {
            if (long.TryParse(line.AsSpan("total_size=".Length), NumberStyles.None, CultureInfo.InvariantCulture, out var size) && size > 0)
            {
                _connected.TrySetResult();
            }
        }
        else if (line.StartsWith("out_time_us=", StringComparison.Ordinal) && _connected.Task.IsCompleted && !_finishing
            && long.TryParse(line.AsSpan("out_time_us=".Length), NumberStyles.None, CultureInfo.InvariantCulture, out var micros))
        {
            var lead = (Volatile.Read(ref _sent) / (double)_fps) - (micros / 1e6); // A へ実際に渡したコマ（溜めている分は数えない）
            lock (_gate)
            {
                _minLead = Math.Min(_minLead, lead);
                if (lead < UnderrunLeadSeconds)
                {
                    if (!_below)
                    {
                        _underruns++;
                        _below = true;
                    }
                }
                else
                {
                    _below = false;
                }
            }
        }
    }

    /// <summary>
    /// 受け手が繋ぐまで、待っていることを <see cref="WaitingReportSeconds"/> ごとに知らせ、<see cref="LiveOptions.WaitSeconds"/> を過ぎたら全部止める
    /// （「受け手が繋ぎませんでした」で失敗する）。送り側を始めてから数える。
    /// </summary>
    private void Watch()
    {
        var token = _cancel.Token;
        var started = Stopwatch.StartNew();
        var lastReport = 0.0;
        var announced = false;
        while (!_connected.Task.IsCompleted && !token.IsCancellationRequested)
        {
            token.WaitHandle.WaitOne(200);
            if (!announced && started.Elapsed.TotalSeconds >= ListenerReadySeconds && !_connected.Task.IsCompleted && !token.IsCancellationRequested)
            {
                // 送り側が出力（SRT の待ち受け）を開くまでの目安が過ぎてから知らせる。先に知らせると、受け手が早く繋ぎに来て失敗する。
                announced = true;
                lastReport = started.Elapsed.TotalSeconds;
                _log.WriteLine($"受け手を待っています: {Describe(_url)}（繋ぎに失敗したら、数秒おいて繋ぎ直してください。OBS のメディアソースは再接続の遅延で自動）");
            }

            if (_connected.Task.IsCompleted || token.IsCancellationRequested || _faulted)
            {
                return;
            }

            var elapsed = started.Elapsed.TotalSeconds;
            if (elapsed >= _options.WaitSeconds)
            {
                Fault(string.Create(CultureInfo.InvariantCulture, $"受け手が繋ぎませんでした（{_options.WaitSeconds:0} 秒待ったので止めました。--wait で延ばせます）"), null);
                return;
            }

            if (announced && elapsed - lastReport >= WaitingReportSeconds)
            {
                lastReport = elapsed;
                _log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"受け手を待っています: {Describe(_url)}（{elapsed:0} 秒、上限 {_options.WaitSeconds:0} 秒）"));
            }
        }
    }

    // ---- 中継・失敗・片付け ----

    private void Relay()
    {
        var token = _cancel.Token;
        try
        {
            var stdout = _encoder.StandardOutput.BaseStream;
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = stdout.Read(buffer, 0, buffer.Length)) > 0)
            {
                _queue.Enqueue(buffer.AsSpan(0, read).ToArray(), token);
                if (!_senderStarted && _queue.Bytes >= _queueCapacity * StartSenderFillRatio)
                {
                    // 遅延に達する前に緩衝がいっぱいになりそう（長い --delay・高いビットレート）: 始めないと中継 → A → 作る側が止まって詰まる。
                    StartSender();
                }
            }

            _queue.Complete();
            if (!_producerDone)
            {
                Fault("エンコード側の ffmpeg が途中で終わりました", null);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException e)
        {
            Fault("エンコード側の出力を読めません", e);
        }
    }

    /// <summary>
    /// 失敗の最初の原因を記録し、全部（A・B・各糸）を止める。2 つ目以降の失敗（止めたことで起きる連鎖）は無視する。
    /// メイン糸は次の書き込み・<see cref="ThrowIfFaulted"/> で <see cref="Failed"/> の例外を受け取る。
    /// </summary>
    private void Fault(string what, Exception? inner)
    {
        lock (_gate)
        {
            if (_faulted)
            {
                return;
            }

            _faultWhat = what;
            _faultInner = inner;
            _faulted = true;
        }

        _cancel.Cancel();
        Kill(_encoder);
        Kill(SenderProcess()); // 始める側（StartSender）と同じ錠で読む: 失敗が先なら始めないので、B が残らない
    }

    private void ThrowIfFaulted()
    {
        if (_faulted)
        {
            throw Failed(null, null);
        }
    }

    /// <summary>失敗の例外（原因 + 伏せた URL + A・B の標準エラーの終わり）。すでに失敗が記録されていればその原因を使う。</summary>
    private IOException Failed(string? what, Exception? inner)
    {
        if (what is not null)
        {
            Fault(what, inner);
        }

        // 止めたプロセスの標準エラーが読み切られるのを少し待つ。
        WaitBriefly(_encoder);
        WaitBriefly(SenderProcess());
        string cause;
        Exception? innerCause;
        lock (_gate)
        {
            cause = _faultWhat ?? what ?? "不明";
            innerCause = _faultInner ?? inner;
        }

        string encoderTail, senderTail;
        lock (_encoderTail)
        {
            encoderTail = string.Join(Environment.NewLine, _encoderTail);
        }

        lock (_senderTail)
        {
            senderTail = string.Join(Environment.NewLine, _senderTail);
        }

        return new IOException(
            $"{cause}: {Describe(_url)}。{Environment.NewLine}エンコード側 ffmpeg の標準エラーの終わり:{Environment.NewLine}{encoderTail}{Environment.NewLine}送り側 ffmpeg の標準エラーの終わり:{Environment.NewLine}{senderTail}",
            innerCause);
    }

    private static void WaitBriefly(Process? process)
    {
        try
        {
            process?.WaitForExit(2000);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void Kill(Process? process)
    {
        try
        {
            if (process is not null && !process.HasExited)
            {
                process.Kill();
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private Process Start(IReadOnlyList<string> arguments, bool redirectOutput, Queue<string> tail, string label, Action<string>? progress = null)
    {
        var info = new ProcessStartInfo("ffmpeg") { RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = redirectOutput, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        var process = Process.Start(info) ?? throw new IOException("ffmpeg を起動できません（PATH にありますか）。");
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            // ffmpeg は失敗のとき出力の URL をそのまま書く（鍵・passphrase が入りうる）。ログにも例外にも、伏せたものだけを出す。
            var line = Redact(e.Data, _url);
            _log.WriteLine($"ffmpeg[{label}]: {line}");
            lock (tail)
            {
                tail.Enqueue(line);
                while (tail.Count > ErrorTailLines)
                {
                    tail.Dequeue();
                }
            }
        };
        process.BeginErrorReadLine();
        if (progress is not null)
        {
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    progress(e.Data);
                }
            };
            process.BeginOutputReadLine();
        }

        return process;
    }

    private void Send(byte[] rgba)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            _video.Write(rgba);
            _video.Flush();
            SendSeconds += (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
        }
        catch (IOException e)
        {
            throw Failed("エンコード側へ映像を書けません", e);
        }

        _sent++;
        _firstSent.TrySetResult();
    }

    private TcpClient ConnectAudio(int port, CancellationToken token)
    {
        // 期限は、映像の最初のコマを A に渡してから数える（A は最初のコマを読むまで音の TCP を聞き始めない。
        // 起動が遅い・最初のコマが遅いだけで失敗にしない）。それまでも繋ぎ直しは続ける。
        var deadline = Stopwatch.StartNew();
        while (!_firstSent.Task.IsCompleted || deadline.Elapsed.TotalSeconds < AudioConnectSeconds)
        {
            if (!_firstSent.Task.IsCompleted)
            {
                deadline.Restart();
            }

            token.ThrowIfCancellationRequested();
            if (_encoder.HasExited)
            {
                throw Failed("エンコード側の ffmpeg が起動できません", null);
            }

            var client = new TcpClient { NoDelay = true };
            try
            {
                client.Connect(IPAddress.Loopback, port);
                return client;
            }
            catch (SocketException)
            {
                client.Dispose();
                token.WaitHandle.WaitOne(ConnectRetryMilliseconds);
            }
        }

        throw Failed("音の接続の期限（10 秒）を過ぎました", null);
    }

    /// <summary>標本 [from, to) を s16 にして書く。<paramref name="audio"/> の長さを超えた所は無音。</summary>
    private static void WriteAudioBytes(Stream stream, float[] audio, LiveAudioMixer mixer, int from, int to)
    {
        var bytes = new byte[(to - from) * 2];
        for (var i = from; i < to; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan((i - from) * 2, 2), mixer.Output(i < audio.Length ? audio[i] : 0f, i));
        }

        stream.Write(bytes);
        stream.Flush();
    }

    private static string FormatLead(double lead) => double.IsPositiveInfinity(lead) ? "n/a" : lead.ToString("0.0", CultureInfo.InvariantCulture) + "s";

    // ---- 引数・URL（純関数。テストする） ----

    /// <summary>
    /// 出口の形式を URL で決める。<c>srt://</c>・<c>udp://</c> は mpegts、<c>rtmp://</c>・<c>rtmps://</c> は flv、
    /// それ以外はファイルのパスで、拡張子が .ts なら mpegts・.flv なら flv・.mkv なら matroska。
    /// </summary>
    public static string Format(string url)
    {
        if (url.StartsWith("srt://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("udp://", StringComparison.OrdinalIgnoreCase))
        {
            return "mpegts";
        }

        if (url.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("rtmps://", StringComparison.OrdinalIgnoreCase))
        {
            return "flv";
        }

        return Path.GetExtension(url).ToLowerInvariant() switch
        {
            ".ts" => "mpegts",
            ".flv" => "flv",
            ".mkv" => "matroska",
            _ => throw new ArgumentException($"--live '{Describe(url)}' の形式が分かりません（srt:// udp:// rtmp:// rtmps:// か、.ts .flv .mkv のファイル）。"),
        };
    }

    /// <summary>SRT の URL に <c>pkt_size=1316</c> が無ければ足す（それ以外の URL はそのまま）。</summary>
    public static string NormalizeUrl(string url)
    {
        if (!url.StartsWith("srt://", StringComparison.OrdinalIgnoreCase) || url.Contains("pkt_size=", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        return url + (url.Contains('?') ? "&" : "?") + SrtPacketSize;
    }

    /// <summary>映像のエンコーダの引数。<c>nvenc</c> は低遅延の固定速さ、<c>x264</c> は NVENC の無い PC 向け。キー（GOP）は 2 秒ごと。</summary>
    public static IReadOnlyList<string> EncoderArguments(string encoder, string bitrate, int fps) => encoder.ToLowerInvariant() switch
    {
        "nvenc" => ["-c:v", "h264_nvenc", "-preset", "p4", "-tune", "ll", "-rc", "cbr", "-b:v", bitrate, "-g", (2 * fps).ToString(CultureInfo.InvariantCulture), "-bf", "0", "-pix_fmt", "yuv420p"],
        "x264" => ["-c:v", "libx264", "-preset", "veryfast", "-tune", "zerolatency", "-b:v", bitrate, "-g", (2 * fps).ToString(CultureInfo.InvariantCulture), "-bf", "0", "-pix_fmt", "yuv420p"],
        _ => throw new ArgumentException($"--encoder '{encoder}' はありません（nvenc か x264）。"),
    };

    /// <summary>
    /// エンコード側 A の引数: 映像は標準入力の rawvideo RGBA、音は <paramref name="audioPort"/> の TCP（ffmpeg が聞く）、出力は mpegts を標準出力へ。
    /// 出力の形式（flv など）への変換は送り側 B がする。
    /// </summary>
    public static IReadOnlyList<string> EncodeArguments(LiveOptions options, int width, int height, int fps, int sampleRate, int audioPort)
    {
        // -probesize 32 -analyzeduration 0: 入力の形（rawvideo の大きさ・s16le の速さ）は引数で全部渡しているので、中身を読んで調べさせない。
        // 調べさせると ffmpeg は音を数秒ぶん待つが、音は映像と一緒に書くので、映像の書き込みが詰まって互いに待ち合う（測定で起きた）。
        return
        [
            "-y", "-hide_banner", "-loglevel", Environment.GetEnvironmentVariable("LIVE_FF_LOG") ?? "warning", "-nostats",
            "-probesize", "32", "-analyzeduration", "0", "-f", "rawvideo", "-pix_fmt", "rgba", "-s", $"{width}x{height}", "-framerate", fps.ToString(CultureInfo.InvariantCulture), "-i", "-",
            "-probesize", "32", "-analyzeduration", "0", "-f", "s16le", "-ar", sampleRate.ToString(CultureInfo.InvariantCulture), "-ac", "1", "-i", $"tcp://127.0.0.1:{audioPort}?listen=1",
            "-map", "0:v:0", "-map", "1:a:0",
            .. EncoderArguments(options.Encoder, options.Bitrate, fps),
            "-c:a", "aac", "-b:a", "160k", "-ac", "2", // モノラル → ステレオの変換（−3 dB）はファイルの道（Mix.ps1 の aformat）と同じ
            "-max_muxing_queue_size", "1024", "-flush_packets", "1",
            "-f", "mpegts", "pipe:1",
        ];
    }

    /// <summary>
    /// 送り側 B の引数: <c>-re</c> で標準入力の mpegts を実時間に読み、再エンコードせずに（<c>-c copy</c>）<paramref name="url"/> へ流す。
    /// ファイルへの出力も同じ道を通す（挙動をそろえる）。flv（rtmp）は AAC を flv 用の形に直す <c>-bsf:a aac_adtstoasc</c> が要る。
    /// <c>-progress pipe:1</c> は繋がったこと（total_size）と進み（out_time_us）を知るため。
    /// </summary>
    public static IReadOnlyList<string> SendArguments(string url)
    {
        var format = Format(url);
        // rtmp の詳細ログ（verbose・debug）は鍵を単独の語として書く（playpath・path=/app/KEY）ので、rtmp では warning に抑える。
        var level = format == "flv" ? "warning" : Environment.GetEnvironmentVariable("LIVE_FF_LOG") ?? "warning";
        return
        [
            "-y", "-hide_banner", "-loglevel", level, "-nostats", "-progress", "pipe:1",
            "-re", "-f", "mpegts", "-i", "pipe:0",
            "-c", "copy",
            .. format == "flv" ? new[] { "-bsf:a", "aac_adtstoasc" } : [],
            "-flush_packets", "1",
            "-f", format, NormalizeUrl(url),
        ];
    }

    /// <summary>
    /// ログ・例外に出してよい形の URL。srt・udp は問い合わせ（<c>passphrase=</c>・<c>streamid=</c> が入りうる）と認証情報を落とし、
    /// rtmp・rtmps は app（最初の道筋）までにして鍵（その先）を落とす。ファイルのパスはそのまま。
    /// </summary>
    public static string Describe(string url)
    {
        var scheme = url.IndexOf("://", StringComparison.Ordinal);
        if (scheme < 0)
        {
            return url;
        }

        var kind = url[..scheme].ToLowerInvariant();
        if (kind is not ("srt" or "udp" or "rtmp" or "rtmps"))
        {
            return url;
        }

        var rest = url[(scheme + 3)..];
        var at = rest.IndexOfAny(['/', '?']);
        var authority = at < 0 ? rest : rest[..at];
        authority = authority[(authority.LastIndexOf('@') + 1)..]; // user:password@ を落とす
        var tail = at < 0 ? "" : rest[at..];
        if (kind is "srt" or "udp")
        {
            return $"{kind}://{authority}{(tail.Contains('?') ? "?…" : "")}";
        }

        // rtmp: /app/KEY → /app/…（app は最初の道筋。鍵はその先）
        var path = tail.TrimStart('/').Split('?')[0].Split('/')[0];
        return $"{kind}://{authority}{(path.Length > 0 ? "/" + path : "")}/…";
    }

    /// <summary>文のなかの srt・udp・rtmp・rtmps の URL をすべて <see cref="Describe"/> の形に置き換える。<paramref name="knownUrl"/> は丸ごと置き換える。</summary>
    public static string Redact(string text, string? knownUrl = null)
    {
        if (!string.IsNullOrEmpty(knownUrl))
        {
            var described = Describe(knownUrl);
            text = text.Replace(knownUrl, described, StringComparison.Ordinal).Replace(NormalizeUrl(knownUrl), described, StringComparison.Ordinal);
            foreach (var secret in Secrets(knownUrl))
            {
                text = text.Replace(secret, "…", StringComparison.Ordinal); // 鍵が単独の語で出る行（playpath など）
            }
        }

        return UrlPattern().Replace(text, m => Describe(m.Value));
    }

    /// <summary>URL のうち、単独で出ても伏せるべき値: rtmp の app の先（鍵）、問い合わせの <c>passphrase</c>・<c>streamid</c>・<c>token</c>・<c>key</c> の値。短すぎる（4 字未満）ものは、普通の語を壊すので除く。</summary>
    public static IReadOnlyList<string> Secrets(string url)
    {
        var secrets = new List<string>();
        var scheme = url.IndexOf("://", StringComparison.Ordinal);
        if (scheme < 0)
        {
            return secrets;
        }

        var rest = url[(scheme + 3)..];
        var question = rest.IndexOf('?');
        var path = question < 0 ? rest : rest[..question];
        if (url.StartsWith("rtmp", StringComparison.OrdinalIgnoreCase))
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length > 2)
            {
                secrets.Add(string.Join('/', segments[2..]));
                secrets.AddRange(segments.Skip(2));
            }
        }

        if (question >= 0)
        {
            foreach (var pair in rest[(question + 1)..].Split('&'))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2 && parts[0].ToLowerInvariant() is "passphrase" or "streamid" or "token" or "key")
                {
                    secrets.Add(parts[1]);
                }
            }
        }

        return [.. secrets.Where(v => v.Length >= 4).Distinct().OrderByDescending(v => v.Length)];
    }

    /// <summary>ビットレート（<c>6M</c>・<c>4500k</c>・<c>6000000</c>）を bit/s に。</summary>
    public static double ParseBitrate(string bitrate)
    {
        var text = bitrate.Trim();
        var scale = 1.0;
        if (text.EndsWith('k') || text.EndsWith('K'))
        {
            scale = 1e3;
            text = text[..^1];
        }
        else if (text.EndsWith('M') || text.EndsWith('m'))
        {
            scale = 1e6;
            text = text[..^1];
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value * scale
            : throw new ArgumentException($"--bitrate '{bitrate}' が読めません（6M・4500k・6000000 のように）。");
    }

    /// <summary>
    /// <c>--delay</c> が緩衝に収まるか確かめる。遅延ぶんの流れ（ビットレート × 遅延）が緩衝の半分を超えると、
    /// 送り側を始める前に緩衝が埋まりやすい（ビットレートの超過も見込む）。
    /// </summary>
    public static void ValidateDelay(double delaySeconds, string bitrate, long capacityBytes = QueueCapacityBytes)
    {
        var bytesPerSecond = ParseBitrate(bitrate) / 8.0;
        var limit = 0.5 * capacityBytes / bytesPerSecond;
        if (delaySeconds > limit)
        {
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture,
                $"--delay {delaySeconds:0.#} 秒は長すぎます（--bitrate {bitrate} の流れ {delaySeconds:0.#} 秒ぶんが緩衝 {capacityBytes / (1024 * 1024)} MB の半分を超えます。上限 {limit:0} 秒）。"));
        }
    }

    [GeneratedRegex(@"(?:srt|udp|rtmps?)://[^\s'""]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();
}
