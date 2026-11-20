using MiniRing.Arena;
using MiniRing.Audio;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 生配信（<c>video --live</c>、docs/TV_BROADCAST.md「生配信」）の道具のうち、ffmpeg・描画に触れない部分:
/// 出口の形式と引数、実時間の歩調、声の混ぜ（ゲームの音を声の間だけ下げる）、歓声を少しずつ混ぜても一括と同じになること。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class LiveStreamTests
{
    private const int Rate = 44100;

    [TestMethod]
    [DataRow("srt://127.0.0.1:9000?mode=listener", "mpegts")]
    [DataRow("udp://127.0.0.1:5000", "mpegts")]
    [DataRow("rtmp://live.example.com/app/KEY", "flv")]
    [DataRow("rtmps://live.example.com:443/app/KEY", "flv")]
    [DataRow("out/live.ts", "mpegts")]
    [DataRow("out\\live.FLV", "flv")]
    [DataRow("live.mkv", "matroska")]
    public void Format_IsDecidedByTheUrl(string url, string expected) => Assert.AreEqual(expected, LiveOutput.Format(url));

    [TestMethod]
    public void Format_UnknownExtension_Throws() => Assert.ThrowsExactly<ArgumentException>(() => LiveOutput.Format("live.mp4"));

    [TestMethod]
    public void NormalizeUrl_AddsPacketSizeToSrtOnly()
    {
        Assert.AreEqual("srt://h:9000?pkt_size=1316", LiveOutput.NormalizeUrl("srt://h:9000"));
        Assert.AreEqual("srt://h:9000?mode=listener&pkt_size=1316", LiveOutput.NormalizeUrl("srt://h:9000?mode=listener"));
        Assert.AreEqual("srt://h:9000?pkt_size=1000&mode=caller", LiveOutput.NormalizeUrl("srt://h:9000?pkt_size=1000&mode=caller"), "あるものは足さない");
        Assert.AreEqual("udp://h:5000", LiveOutput.NormalizeUrl("udp://h:5000"));
        Assert.AreEqual("rtmp://h/app/key", LiveOutput.NormalizeUrl("rtmp://h/app/key"));
        Assert.AreEqual(LiveOutput.DefaultUrl, LiveOutput.NormalizeUrl(LiveOutput.DefaultUrl), "既定の URL はもう持っている");
    }

    [TestMethod]
    public void Encoder_NvencIsLowLatencyConstantRate_X264IsForPcsWithoutNvenc()
    {
        var nvenc = string.Join(' ', LiveOutput.EncoderArguments("nvenc", "6M", 30));
        Assert.AreEqual("-c:v h264_nvenc -preset p4 -tune ll -rc cbr -b:v 6M -g 60 -bf 0 -pix_fmt yuv420p", nvenc);
        var x264 = string.Join(' ', LiveOutput.EncoderArguments("x264", "4M", 60));
        Assert.AreEqual("-c:v libx264 -preset veryfast -tune zerolatency -b:v 4M -g 120 -bf 0 -pix_fmt yuv420p", x264);
        Assert.ThrowsExactly<ArgumentException>(() => LiveOutput.EncoderArguments("vp9", "6M", 30));
    }

    [TestMethod]
    public void EncodeArguments_VideoFromStdinAudioFromTcp_MpegtsToStdout()
    {
        var args = LiveOutput.EncodeArguments(new LiveOptions("srt://127.0.0.1:9000?mode=listener"), 1280, 720, 30, Rate, 54321);
        var text = string.Join(' ', args);

        StringAssert.Contains(text, "-f rawvideo -pix_fmt rgba -s 1280x720 -framerate 30 -i -");
        StringAssert.Contains(text, "-f s16le -ar 44100 -ac 1 -i tcp://127.0.0.1:54321?listen=1");
        StringAssert.Contains(text, "-c:v h264_nvenc");
        StringAssert.Contains(text, "-c:a aac -b:a 160k");
        Assert.AreEqual("pipe:1", args[^1], "出力は標準出力");
        Assert.AreEqual("mpegts", args[^2], "A はいつも mpegts（形式の変換は B）");
        Assert.IsFalse(text.Contains("srt://", StringComparison.Ordinal), "URL は B だけが知る");
    }

    [TestMethod]
    public void SendArguments_ReadsStdinInRealTimeAndCopies_FormatFromUrl()
    {
        var srt = LiveOutput.SendArguments("srt://127.0.0.1:9000?mode=listener");
        var text = string.Join(' ', srt);
        StringAssert.Contains(text, "-re -f mpegts -i pipe:0 -c copy");
        StringAssert.Contains(text, "-progress pipe:1");
        Assert.AreEqual("srt://127.0.0.1:9000?mode=listener&pkt_size=1316", srt[^1]);
        Assert.AreEqual("mpegts", srt[^2]);
        Assert.IsFalse(text.Contains("aac_adtstoasc", StringComparison.Ordinal));

        var flv = LiveOutput.SendArguments("rtmp://h/app/key");
        StringAssert.Contains(string.Join(' ', flv), "-bsf:a aac_adtstoasc");
        Assert.AreEqual("flv", flv[^2]);
        Assert.AreEqual("rtmp://h/app/key", flv[^1]);

        Assert.AreEqual("matroska", LiveOutput.SendArguments("out/live.mkv")[^2]);
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public void ByteQueue_BlocksWhenFull_NeverDrops_AndKeepsOrder()
    {
        var queue = new LiveByteQueue(10);
        using var cancel = new CancellationTokenSource();
        queue.Enqueue(new byte[6], cancel.Token);
        var third = false;
        var producer = Task.Run(() =>
        {
            queue.Enqueue(new byte[6], cancel.Token); // 6 + 6 > 10: 取り出されるまで待つ
            third = true;
            queue.Enqueue([9], cancel.Token);
            queue.Complete();
        });
        Thread.Sleep(300);
        Assert.IsFalse(third, "いっぱいなら入れる側が待つ");
        Assert.AreEqual(6, queue.Bytes);

        var sizes = new List<int>();
        while (queue.TryDequeue(out var chunk, cancel.Token))
        {
            sizes.Add(chunk.Length);
        }

        producer.Wait();
        CollectionAssert.AreEqual((int[])[6, 6, 1], sizes, "落とさず、入れた順");
        Assert.AreEqual(0, queue.Bytes);
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public void ByteQueue_Cancel_ReleasesAWaitingProducer()
    {
        var queue = new LiveByteQueue(4);
        using var cancel = new CancellationTokenSource();
        queue.Enqueue(new byte[4], cancel.Token);
        var waiting = Task.Run(() => queue.Enqueue(new byte[4], cancel.Token));
        Thread.Sleep(200);
        cancel.Cancel();

        Assert.ThrowsExactly<AggregateException>(waiting.Wait);
    }

    [TestMethod]
    public void Describe_DropsKeysAndQueries()
    {
        Assert.AreEqual("rtmp://a.rtmp.youtube.com/live2/…", LiveOutput.Describe("rtmp://a.rtmp.youtube.com/live2/abcd-1234-KEY"));
        Assert.AreEqual("rtmps://live.example.com:443/app/…", LiveOutput.Describe("rtmps://live.example.com:443/app/KEY?token=T"));
        Assert.AreEqual("srt://127.0.0.1:9000?…", LiveOutput.Describe("srt://127.0.0.1:9000?mode=listener&passphrase=SECRETSECRET&streamid=SID"));
        Assert.AreEqual("srt://127.0.0.1:9000", LiveOutput.Describe("srt://127.0.0.1:9000"));
        Assert.AreEqual("udp://h:5000?…", LiveOutput.Describe("udp://user:pw@h:5000?x=1").Replace("user:pw@", ""));
        Assert.AreEqual("out/live.ts", LiveOutput.Describe("out/live.ts"));
    }

    [TestMethod]
    public void Redact_HidesTheUrlInEveryFfmpegLine()
    {
        const string url = "rtmp://a.rtmp.youtube.com/live2/SECRETKEY99";
        var lines = new[]
        {
            $"[tcp @ 0000] Connection to tcp://a.rtmp.youtube.com:1935 failed; Error opening output {url}: Connection refused",
            "Error opening output files: I/O error srt://127.0.0.1:9000?mode=listener&passphrase=PASSPHRASE123&pkt_size=1316",
            $"Output #0, flv, to '{url}':",
        };

        foreach (var line in lines)
        {
            var redacted = LiveOutput.Redact(line, url);
            StringAssert.DoesNotMatch(redacted, new System.Text.RegularExpressions.Regex("SECRETKEY99|PASSPHRASE123"), redacted);
        }

        StringAssert.Contains(LiveOutput.Redact(lines[2], url), "rtmp://a.rtmp.youtube.com/live2/…");
        Assert.AreEqual("plain line", LiveOutput.Redact("plain line", url));
    }

    // 存在だけでなく、試す機能が組み込まれたビルドかを確認する。機能がある環境の検証は省かない。
    private static bool FfmpegSupports(string arguments, string feature)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ffmpeg", "-hide_banner " + arguments)
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var text = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        return process.ExitCode == 0 && text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains(feature, StringComparer.Ordinal);
    }

    private static bool HasFfmpeg()
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ffmpeg", "-version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            p!.WaitForExit(10000);
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static string FfmpegInfo(string path)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ffmpeg", $"-hide_banner -i \"{path}\"") { RedirectStandardError = true, UseShellExecute = false })!;
        var text = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return text;
    }

    /// <summary>ffmpeg を実際に動かし、エンコード側 → 中継 → 送り側（-re、遅延 1 秒）を通して x264 でファイル（受け手なし）へ流して、映像と音の両方・長さが合うことを確かめる。</summary>
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public void LiveOutput_X264ToFile_HasVideoAndAudioAndTheRightDuration()
    {
        if (!HasFfmpeg())
        {
            Assert.Inconclusive("ffmpeg が PATH にありません。");
        }

        var directory = Path.Combine(Path.GetTempPath(), "live-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "out.ts");
            const int fps = 30;
            const int frames = 90;
            var audio = new float[Rate * 3];
            for (var i = 0; i < audio.Length; i++)
            {
                audio[i] = 0.2f * MathF.Sin(i * 0.05f);
            }

            var mixer = new LiveAudioMixer(Rate, audio.Length);
            using var output = new LiveOutput(new LiveOptions(path, "x264", "1M", DelaySeconds: 1), 160, 90, fps, Rate, audio, mixer, new StringWriter());
            for (var f = 0; f < frames; f++)
            {
                var rgba = new byte[160 * 90 * 4];
                Array.Fill(rgba, (byte)(f * 2));
                output.WriteFrame(rgba, audio, mixer);
            }

            output.Finish(audio, mixer);

            Assert.IsTrue(File.Exists(path));
            var info = FfmpegInfo(path);
            StringAssert.Contains(info, "Video: h264");
            StringAssert.Contains(info, "Audio: aac");
            var match = System.Text.RegularExpressions.Regex.Match(info, @"Duration: (\d+):(\d+):(\d+\.\d+)");
            Assert.IsTrue(match.Success, info);
            var seconds = (int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 3600) + (int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) * 60) + double.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.AreEqual(frames / (double)fps, seconds, 1.0, "長さは コマ数 / fps（±1 秒）。A → 緩衝 → B（-re）を通る");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>ffmpeg が途中で終わる（出力を開けない）なら、黙って待たず IOException で知らせる。</summary>
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public void LiveOutput_FfmpegExitsEarly_ThrowsInsteadOfHanging()
    {
        if (!HasFfmpeg())
        {
            Assert.Inconclusive("ffmpeg が PATH にありません。");
        }

        var badPath = Path.Combine(Path.GetTempPath(), "live-missing-" + Guid.NewGuid().ToString("N"), "nodir", "out.ts");
        var audio = new float[Rate * 10];
        var mixer = new LiveAudioMixer(Rate, audio.Length);
        using var output = new LiveOutput(new LiveOptions(badPath, "x264", "1M", DelaySeconds: 0.5), 160, 90, 30, Rate, audio, mixer, new StringWriter());
        var rgba = new byte[160 * 90 * 4];

        var failure = Assert.ThrowsExactly<IOException>(() =>
        {
            for (var f = 0; f < 600; f++)
            {
                output.WriteFrame(rgba, audio, mixer);
            }

            output.Finish(audio, mixer);
        });
        StringAssert.Contains(failure.Message, "ffmpeg");
    }

    /// <summary>受け手が繋がないまま --wait を過ぎたら、ffmpeg を止めて「受け手が繋ぎませんでした」で失敗し、passphrase を出さない。</summary>
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public void LiveOutput_NoReceiver_FailsAfterTheWaitAndHidesThePassphrase()
    {
        if (!HasFfmpeg())
        {
            Assert.Inconclusive("ffmpeg が PATH にありません。");
        }
        if (!FfmpegSupports("-protocols", "srt"))
        {
            Assert.Inconclusive("この ffmpeg は SRT を組み込んでいません（待機の検証には SRT が必要）。");
        }

        var probe = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        var port = ((System.Net.IPEndPoint)probe.Client.LocalEndPoint!).Port;
        probe.Close();
        var url = $"srt://127.0.0.1:{port}?mode=listener&passphrase=SECRETSECRET1";
        var audio = new float[Rate * 10];
        var mixer = new LiveAudioMixer(Rate, audio.Length);
        var log = new StringWriter();
        using var output = new LiveOutput(new LiveOptions(url, "x264", "1M", WaitSeconds: 3, DelaySeconds: 0.5), 160, 90, 30, Rate, audio, mixer, log);
        var rgba = new byte[160 * 90 * 4];

        var failure = Assert.ThrowsExactly<IOException>(() =>
        {
            for (var f = 0; f < 3000; f++)
            {
                output.WriteFrame(rgba, audio, mixer);
            }

            output.Finish(audio, mixer);
        });

        StringAssert.Contains(failure.Message, "受け手が繋ぎませんでした");
        StringAssert.DoesNotMatch(failure.Message + log, new System.Text.RegularExpressions.Regex("SECRETSECRET1"));
        StringAssert.Contains(log.ToString(), "受け手を待っています");
    }

    [TestMethod]
    public void Resample_16kTo44k_KeepsTheDuration()
    {
        var source = new float[16000];
        for (var i = 0; i < source.Length; i++)
        {
            source[i] = i / (float)source.Length;
        }

        var output = LiveAudioMixer.Resample(source, 16000, Rate);

        Assert.AreEqual(Rate, output.Length, "1 秒は 1 秒のまま");
        Assert.AreEqual(source[8000], output[Rate / 2], 1e-3f, "真ん中の値が直線補間で保たれる");
    }

    [TestMethod]
    public void Mixer_DucksTheGameOnlyUnderTheVoice()
    {
        var mixer = new LiveAudioMixer(Rate, Rate * 6);
        mixer.AddVoice(new float[Rate], start: 2f, end: 3f); // 無音の声でも下げは効く
        var duckedGain = MathF.Pow(10f, -LiveAudioMixer.DuckDb / 20f);

        Assert.AreEqual(1f, mixer.GameGain((int)(1.0f * Rate)), 1e-6f, "声の前は 1 倍");
        Assert.AreEqual(duckedGain, mixer.GameGain((int)(2.5f * Rate)), 1e-4f, "声の間は −6 dB（0.501 倍）");
        Assert.AreEqual(1f, mixer.GameGain((int)(4f * Rate)), 1e-6f, "戻したあとは 1 倍");

        var attackMid = mixer.GameGain((int)((2f - (LiveAudioMixer.DuckAttackSeconds / 2f)) * Rate));
        Assert.IsTrue(attackMid < 1f && attackMid > duckedGain, $"下げ始めの途中は間の値（{attackMid}）");
        var releaseMid = mixer.GameGain((int)((3f + (LiveAudioMixer.DuckReleaseSeconds / 2f)) * Rate));
        Assert.IsTrue(releaseMid < 1f && releaseMid > duckedGain, $"戻る途中は間の値（{releaseMid}）");
        Assert.AreEqual(1f - ((1f - duckedGain) * 0.5f), attackMid, 1e-3f, "傾きは直線（半分の所で下げの半分）");
    }

    [TestMethod]
    public void Mixer_Output_SumsVoiceAndDuckedGameThenSoftLimits()
    {
        var mixer = new LiveAudioMixer(Rate, Rate * 4);
        var voice = new float[Rate];
        Array.Fill(voice, 0.2f);
        mixer.AddVoice(voice, start: 1f, end: 2f);

        var outside = mixer.Output(0.3f, (int)(3.5f * Rate));
        Assert.AreEqual((short)(MathF.Tanh(0.3f) * 32000f), outside, "声の外は声なしの mp4 の道（tanh）と同じ");
        var under = mixer.Output(0.3f, (int)(1.5f * Rate));
        var expected = (short)(MathF.Tanh((0.3f * MathF.Pow(10f, -LiveAudioMixer.DuckDb / 20f)) + 0.2f) * 32000f);
        Assert.AreEqual(expected, under, 1, "声の間は ゲーム × 0.501 + 声");
    }

    [TestMethod]
    public void Mixer_LoadManifest_ReadsTheWavSampleRateFromTheHeader()
    {
        var directory = Path.Combine(Path.GetTempPath(), "live-mixer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var wav = Path.Combine(directory, "001_announcer.wav");
            WriteWav16(wav, 16000, [.. Enumerable.Repeat((short)8192, 8000)]); // 0.5 秒・振幅 0.25
            File.WriteAllText(Path.Combine(directory, "manifest.json"),
                "{\"lines\":[{\"file\":\"001_announcer.wav\",\"start\":1.0,\"end\":1.5}]}");
            var mixer = new LiveAudioMixer(Rate, Rate * 3);

            mixer.LoadManifest(Path.Combine(directory, "manifest.json"));

            Assert.AreEqual(1, mixer.VoiceCount);
            Assert.AreEqual((short)(MathF.Tanh(0.25f) * 32000f), mixer.Output(0f, (int)(1.25f * Rate)), 2, "声の真ん中は 0.25（ゲームの音 0）");
            Assert.AreEqual((short)0, mixer.Output(0f, (int)(2.2f * Rate)), "声の後ろは無音");
            Assert.AreEqual((short)0, mixer.Output(0f, (int)(0.9f * Rate)), "声の前は無音");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>歓声: 地の歓声 ＋ 反応を少しずつ混ぜた結果は、一括（<see cref="Broadcast.MixInto"/>）と同じ。</summary>
    [TestMethod]
    public void Crowd_MixedInPiecesEqualsMixedAtOnce()
    {
        var broadcast = new Broadcast("赤", "青", "jabber", "slugger");
        using var game = new BoxingGame(seed: 3, corners: new BoxingCorners("slugger", s => BotCatalog.Create("slugger", s), s => BotCatalog.Create("jabber", s)), rules: MatchRules.Tv);
        const float seconds = 20.14f;
        const float dt = 1f / 60f;
        var pieces = new float[(int)((seconds + 4f) * Rate)];
        Broadcast.MixAmbience(pieces, Rate, seed: 3);
        var done = 0;
        var step = 0;
        for (var t = 0f; t < seconds; t += dt, step++)
        {
            game.Step(default, dt);
            broadcast.Observe(game, t, step);
            if (step % 2 == 0)
            {
                done = broadcast.MixCues(pieces, Rate, done); // 増えた分だけ（生配信は 1 コマごと）
            }
        }

        done = broadcast.MixCues(pieces, Rate, done);
        var atOnce = new float[pieces.Length];
        broadcast.MixInto(atOnce, Rate, seed: 3);

        Assert.IsGreaterThan(0, broadcast.Cues.Count(c => c.Reaction is not null), "試合の間に歓声の反応がある（ラウンドの始まりの拍手など）");
        Assert.AreEqual(broadcast.Cues.Count, done);
        CollectionAssert.AreEqual(atOnce, pieces, "足し算の順が同じなのでビット単位で同じ");
    }

    [TestMethod]
    public void ValidateDelay_RejectsADelayThatCannotFitTheBuffer()
    {
        LiveOutput.ValidateDelay(8, "6M"); // 既定: 6 Mb/s の 8 秒 = 6 MB は 64 MB の半分に収まる
        LiveOutput.ValidateDelay(40, "6M");
        var failure = Assert.ThrowsExactly<ArgumentException>(() => LiveOutput.ValidateDelay(60, "6M"));
        StringAssert.Contains(failure.Message, "長すぎます");
        Assert.ThrowsExactly<ArgumentException>(() => LiveOutput.ValidateDelay(10, "50M"));
        Assert.AreEqual(4_500_000, LiveOutput.ParseBitrate("4500k"), 1e-6);
        Assert.AreEqual(6_000_000, LiveOutput.ParseBitrate("6M"), 1e-6);
        Assert.AreEqual(6_000_000, LiveOutput.ParseBitrate("6000000"), 1e-6);
        Assert.ThrowsExactly<ArgumentException>(() => LiveOutput.ParseBitrate("fast"));
    }

    [TestMethod]
    public void Redact_HidesTheBareKeyTokenAndRtmpUsesQuietLogs()
    {
        const string url = "rtmp://a.rtmp.youtube.com/live2/abcd-1234-WXYZ";
        var redacted = LiveOutput.Redact("[rtmp @ 0000] playpath=abcd-1234-WXYZ path=/live2/abcd-1234-WXYZ", url);
        StringAssert.DoesNotMatch(redacted, new System.Text.RegularExpressions.Regex("abcd-1234-WXYZ"), redacted);
        StringAssert.Contains(string.Join(' ', LiveOutput.SendArguments(url)), "-loglevel warning", "rtmp の B は詳細ログを出さない");
    }

    private static string RunStream(LiveOptions options, int frames, int audioSeconds, TimeSpan beforeFirstFrame, long capacity, out LiveOutput output,
        int width = 160, int height = 90, Action<LiveOutput>? created = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "live-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "out.ts");
        var audio = new float[Rate * audioSeconds];
        for (var i = 0; i < audio.Length; i++)
        {
            audio[i] = 0.2f * MathF.Sin(i * 0.05f);
        }

        var mixer = new LiveAudioMixer(Rate, audio.Length);
        output = new LiveOutput(options with { Url = path }, width, height, 30, Rate, audio, mixer, new StringWriter(), capacity);
        created?.Invoke(output);
        try
        {
            Thread.Sleep(beforeFirstFrame);
            for (var f = 0; f < frames; f++)
            {
                var rgba = new byte[width * height * 4];
                Array.Fill(rgba, (byte)(f * 2));
                output.WriteFrame(rgba, audio, mixer);
            }

            output.Finish(audio, mixer);
            return FfmpegInfo(path);
        }
        finally
        {
            output.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static double DurationOf(string info)
    {
        var match = System.Text.RegularExpressions.Regex.Match(info, @"Duration: (\d+):(\d+):(\d+\.\d+)");
        Assert.IsTrue(match.Success, info);
        return (int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 3600)
            + (int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) * 60)
            + double.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>遅延（60 秒）に達する前に緩衝（100 KB）が埋まっても、送り側を始めて詰まらない。</summary>
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public void LiveOutput_BufferFillsBeforeTheDelay_StillStartsTheSender()
    {
        if (!HasFfmpeg())
        {
            Assert.Inconclusive("ffmpeg が PATH にありません。");
        }

        var info = RunStream(new LiveOptions("x", "x264", "1M", DelaySeconds: 60), frames: 150, audioSeconds: 6, TimeSpan.Zero, capacity: 100_000, out _);

        StringAssert.Contains(info, "Video: h264");
        Assert.AreEqual(5.0, DurationOf(info), 1.0);
    }

    /// <summary>最初のコマが 10 秒より遅れて来ても、音の接続の期限で失敗しない（期限は最初のコマを渡してから）。</summary>
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public void LiveOutput_SlowFirstFrame_DoesNotTripTheAudioConnectDeadline()
    {
        if (!HasFfmpeg())
        {
            Assert.Inconclusive("ffmpeg が PATH にありません。");
        }

        var info = RunStream(new LiveOptions("x", "x264", "1M", DelaySeconds: 0.5), frames: 45, audioSeconds: 3, TimeSpan.FromSeconds(11), QueueCapacityDefault, out _);

        StringAssert.Contains(info, "Audio: aac");
        Assert.AreEqual(1.5, DurationOf(info), 1.0);
    }

    /// <summary>溜めるコマ数（15）に満たない短い配信でも、音の接続で待ち続けずに終わる。</summary>
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public void LiveOutput_VeryShortStream_FinishesWithVideoAndAudio()
    {
        if (!HasFfmpeg())
        {
            Assert.Inconclusive("ffmpeg が PATH にありません。");
        }

        var info = RunStream(new LiveOptions("x", "x264", "1M", DelaySeconds: 8), frames: 5, audioSeconds: 2, TimeSpan.Zero, QueueCapacityDefault, out _);

        StringAssert.Contains(info, "Video: h264");
        StringAssert.Contains(info, "Audio: aac");
    }

    /// <summary>
    /// 終わりに溜めたコマ（0.5 秒）を渡すとき、音を先に書かないと詰まらない（回帰: 溜めたコマを全部渡してから音を書いていたので、
    /// エンコード側は映像の時刻より先の音を待って標準入力を読まず、1 コマがパイプの緩衝より大きい 720p では Finish が止まったままになった）。
    /// 小さいコマ・x264 の 45 コマでは起きなかった（パイプの緩衝に収まる）ので、本番と同じ 1280 × 720・NVENC・150 コマで確かめる（直す前は 90 秒で止まった）。止まったら Dispose で ffmpeg を止めて失敗にする。
    /// </summary>
    [TestMethod]
    [Timeout(180000, CooperativeCancellation = true)]
    public void LiveOutput_Finish_At720p_DoesNotDeadlockOnTheHeldFrames()
    {
        if (!HasFfmpeg())
        {
            Assert.Inconclusive("ffmpeg が PATH にありません。");
        }
        if (!FfmpegSupports("-h encoder=h264_nvenc", "p4"))
        {
            Assert.Inconclusive("この ffmpeg の NVENC は p4 プリセットに対応していません。");
        }

        LiveOutput? live = null;
        var run = Task.Run(() => RunStream(new LiveOptions("x", "nvenc", "6M", DelaySeconds: 0.5), frames: 150, audioSeconds: 6, TimeSpan.Zero, QueueCapacityDefault, out _,
            width: 1280, height: 720, created: o => Volatile.Write(ref live, o)));
        if (!run.Wait(TimeSpan.FromSeconds(90)))
        {
            Volatile.Read(ref live)?.Dispose(); // ffmpeg を止めて、止まった書き込みを失敗させる
            Assert.Fail("Finish が 90 秒で終わらない（溜めたコマと音の待ち合い）");
        }

        var info = run.Result;
        StringAssert.Contains(info, "Video: h264");
        StringAssert.Contains(info, "Audio: aac");
        Assert.AreEqual(5.0, DurationOf(info), 1.0);
    }

    private const long QueueCapacityDefault = LiveOutput.QueueCapacityBytes;

    private static void WriteWav16(string path, int rate, short[] samples)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(36 + (samples.Length * 2));
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples.Length * 2);
        foreach (var s in samples)
        {
            writer.Write(s);
        }
    }
}
