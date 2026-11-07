using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace MiniRing.Replay;

/// <summary>
/// 試合の自動記録。ホストは <see cref="BoxingGame.Step"/> の代わりに <see cref="Step"/> を呼ぶだけでよい。
/// <para>
/// 記録するのは「毎ステップの入力と時間刻み」と、見出しの「乱数の種・調整値」だけ。物理（Bepu・単一スレッド）と
/// CPU（種付き乱数）は決定的なので、これで全員の動きが最初から完全に再現できる（<see cref="ReplayPlayer"/>）。
/// 1 秒ごとに状態のチェックサムも書き、再生時にずれた瞬間を見つけられるようにする。
/// </para>
/// クラッシュしても直前まで残るよう、約 1 秒ごとにディスクへ書き出す。
/// </summary>
public sealed class ReplayRecorder : IDisposable
{
    /// <summary>チェックサムを書く間隔 [ステップ]（60Hz で約 1 秒）。</summary>
    public const int ChecksumInterval = 60;

    private readonly BinaryWriter _writer;
    private int _steps;
    private bool _faulted;

    /// <summary><paramref name="game"/> は作ったばかり（まだ 1 ステップも進めていない）の試合。種と調整値を見出しに書く。</summary>
    public ReplayRecorder(Stream output, BoxingGame game, string host)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(game);
        _writer = new BinaryWriter(output);
        ReplayFile.WriteHeader(_writer, new ReplayHeader(game.Seed, JsonSerializer.Serialize(game.Tuning), host, AppVersion, DateTime.UtcNow));
        _writer.Flush();
    }

    public int StepCount => _steps;

    /// <summary>試合の 1 ステップが例外で終わり、以降の記録をやめたか（記録はその直前までなら完全に再現できる）。</summary>
    public bool Faulted => _faulted;

    /// <summary>このビルドの版（リプレイと実行ファイルの組み合わせを確かめる手がかり）。</summary>
    public static string AppVersion =>
        typeof(BoxingGame).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(BoxingGame).Assembly.GetName().Version?.ToString() ?? "unknown";

    /// <summary>
    /// <paramref name="directory"/> に日時入りの名前で記録を始める（例: replays/miniring_desktop_20261107_153012.mrrep）。
    /// 同じ秒に起動が重なったら _2, _3 … を付ける。
    /// </summary>
    public static ReplayRecorder StartFile(string directory, BoxingGame game, string host, out string path)
    {
        Directory.CreateDirectory(directory);
        var stem = string.Create(CultureInfo.InvariantCulture, $"miniring_{host}_{DateTime.Now:yyyyMMdd_HHmmss}");
        for (var n = 1; ; n++)
        {
            path = Path.Combine(directory, n == 1 ? stem + ReplayFile.Extension : string.Create(CultureInfo.InvariantCulture, $"{stem}_{n}{ReplayFile.Extension}"));
            FileStream file;
            try
            {
                file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            }
            catch (IOException) when (n < 100 && File.Exists(path))
            {
                continue;
            }

            try
            {
                return new ReplayRecorder(file, game, host);
            }
            catch
            {
                file.Dispose();
                throw;
            }
        }
    }

    /// <summary>
    /// <see cref="StartFile"/> の失敗しない版。記録は付加機能なので、書けない（権限・ディスク）ときは
    /// null と理由を返し、ホストは記録なしで試合を続ける。
    /// </summary>
    public static ReplayRecorder? TryStartFile(string directory, BoxingGame game, string host, out string message)
    {
        try
        {
            var recorder = StartFile(directory, game, host, out var path);
            message = $"Recording to {path}";
            return recorder;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message = $"Recording disabled: cannot write to {directory} ({ex.Message})";
            return null;
        }
    }

    /// <summary>
    /// 1 ステップ進め、その入力を記録する。一定間隔で状態のチェックサムも記録する。
    /// ステップが例外で終わったら以降は記録しない（壊れた状態は再現の対象外。ファイルは直前までで有効）。
    /// </summary>
    public void Step(BoxingGame game, BoxingInput input, float dt)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (_faulted)
        {
            game.Step(input, dt);
            return;
        }

        try
        {
            game.Step(input, dt);
        }
        catch
        {
            _faulted = true;
            _writer.Flush();
            throw;
        }

        ReplayFile.WriteStep(_writer, new ReplayStep(input, dt));
        _steps++;
        if (_steps % ChecksumInterval == 0)
        {
            ReplayFile.WriteChecksum(_writer, _steps, game.StateChecksum());
            _writer.Flush();
        }
    }

    public void Dispose()
    {
        _writer.Flush();
        _writer.Dispose();
    }
}
