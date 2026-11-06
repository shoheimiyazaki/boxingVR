namespace Aigis.Foundation.Diagnostics;

/// <summary>
/// Where an engine library hands an exception it has decided to SURVIVE, so the host can still
/// record it (2026-10-15, user request 「例外処理をしっかり。全部拾えるように。問題がおこったときに十分な
/// 情報がエラーログからとれること」).
/// <para>
/// <b>Why it exists.</b> Libraries may not print (hard rule 2), so a library that degrades instead of
/// throwing — an audio device that went away, an undecodable clip, a trace file that cannot be
/// written — used to swallow the exception without a word: the game went silent and no log said why.
/// Reporting here costs a library nothing when no host listens, and a host that does (Aigis.Player's
/// process backstop) turns it into an ordinary contained-error entry with its stack trace.
/// </para>
/// <para>
/// <b>Not a substitute for handling.</b> The caller still decides what to do next; this only makes the
/// decision visible. Call it from the catch block that already exists, once per distinct failure where
/// a loop could repeat it — the host rate-limits per site, but a site name that embeds a varying value
/// defeats that.
/// </para>
/// </summary>
public static class ErrorSink
{
    /// <summary>Raised for every report. Handlers must not throw; one that does is ignored so the
    /// reporting library's own recovery still runs.</summary>
    public static event Action<string, Exception>? Reported;

    /// <summary>Hands a survived exception to whoever records them.</summary>
    /// <param name="site">A stable name for where it happened (e.g. <c>"audio: OpenAL play"</c>).</param>
    /// <param name="error">The exception that was caught.</param>
    public static void Report(string site, Exception error)
    {
        if (Reported is not { } handlers || error is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action<string, Exception>>())
        {
            try
            {
                handler(site, error);
            }
            catch (Exception)
            {
                // A recorder that fails must not turn a survived error into a crash in the code that
                // was surviving it.
            }
        }
    }
}
