using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Collections.Concurrent;
using Aigis.Foundation;
using Aigis.Foundation.Diagnostics;

namespace Aigis.Player;

/// <summary>One fault exactly as the barrier recorded it — the shape both the on-screen notice and
/// the crash-report line are built from.</summary>
/// <param name="Site">The guarded call site's name (e.g. <c>"game.Step"</c>). Low-cardinality and
/// stable, because it is the key the repeat counter and the rate limiter group by.</param>
/// <param name="ExceptionType">The exception's runtime type name.</param>
/// <param name="Message">Its message.</param>
/// <param name="OccurrenceAtSite">How many times this site has failed so far, including this one.</param>
public readonly record struct Fault(string Site, string ExceptionType, string Message, int OccurrenceAtSite);

/// <summary>
/// The host's fault-containment layer. Every call a windowed host makes into game code goes through
/// a <c>Guard</c> overload, so a bug in one of them costs that ONE call its result instead of
/// costing the player their session.
/// <para>
/// <b>Why this exists.</b> Silk.NET invokes <c>Updated</c>/<c>Rendered</c> directly from inside
/// <c>IWindow.Run()</c>, so an exception thrown by a game's <c>Step</c> or HUD build unwinds
/// straight out of the run loop and ends the process. On a developer's terminal that at least
/// prints a stack trace; for a player who double-clicked <c>Play-Triage.cmd</c> the window simply
/// vanishes, taking the round with it and leaving nothing behind to report. This layer turns that
/// into: the frame loses whatever the broken call was going to produce, the fault is described once
/// on the console and once in a file the player can attach to a bug report, an on-screen notice
/// tells them something went wrong, and the window keeps running so they can finish, save or quit.
/// </para>
/// <para>
/// <b>What it deliberately does NOT promise.</b> It does not repair state. A <c>Step</c> that threw
/// halfway through has left the physics world however it left it, and the round may now be wrong.
/// The promise is about the SESSION, not the simulation: the player keeps their window, their input
/// and their chance to quit. That is why the barrier is installed on the human-facing windowed path
/// ONLY — the headless paths an agent verifies with (<c>harness/Aigis.Playtest</c>, the tests, the
/// golden renders) have no barrier and still fail loudly. A verification run that hid a crash behind
/// a caught exception would be exactly the "the file exists, therefore it worked" failure mode
/// docs/anti-patterns.md A6/A7 is about.
/// </para>
/// <para>
/// <b>Turning it off.</b> Set <c>AIGIS_STRICT_ERRORS=1</c> (or pass <c>strict: true</c>) and every
/// guard rethrows instead of containing — the pre-barrier behaviour, for a debugger session where
/// stopping at the real throw site is worth more than a running window.
/// </para>
/// <para>
/// <b>What no managed layer can catch.</b> <see cref="StackOverflowException"/> and an access
/// violation terminate the runtime without offering anyone a catch block, and a throw on a thread
/// this barrier does not own can only be reported (see <see cref="InstallProcessBackstop"/>), never
/// contained. Those are the honest limits of the guarantee above.
/// </para>
/// </summary>
public sealed class FaultBarrier
{
    /// <summary>Environment variable that makes every guard rethrow — see the type's doc comment.</summary>
    public const string StrictModeVariable = "AIGIS_STRICT_ERRORS";

    /// <summary>How long after a fault the host's on-screen notice stays up. Named here so that
    /// lifetime is one number rather than a literal buried in a render handler.</summary>
    public static readonly TimeSpan NoticeDuration = TimeSpan.FromSeconds(8);

    private static FaultBarrier? _processBackstop;
    private static bool _processHandlersInstalled;

    private readonly object _gate = new();
    private readonly Dictionary<string, SiteState> _sites = new(StringComparer.Ordinal);
    private readonly Stopwatch _sinceLastFault = new();
    private readonly string _label;
    private readonly TextWriter _output;
    private readonly string? _reportDirectory;
    private string? _reportPath;
    private Fault? _lastFault;
    private bool _reportingIsBroken;

    /// <param name="label">Names this host in console lines and in the report filename (e.g.
    /// <c>"triage"</c>). Hosts reuse their existing <c>DebugLabel</c>, so a crash report and an F9
    /// debug snapshot from the same session are recognisably a pair.</param>
    /// <param name="output">Where fault descriptions are printed. Defaults to
    /// <see cref="Console.Error"/> — this is diagnostic output and must not be mixed into the status
    /// line a host prints on stdout. Injectable so tests can read what was reported instead of
    /// scraping a console.</param>
    /// <param name="reportDirectory">Directory the crash-report file is written to.
    /// <see langword="null"/> uses <see cref="DefaultReportDirectory"/>; an empty or whitespace
    /// string disables file reporting entirely — which is what the tests pass, so a test run cannot
    /// litter a developer's profile directory.</param>
    /// <param name="strict">Forces strict mode on regardless of the environment. The environment
    /// variable can only turn strict mode ON, never off, so a caller that hard-codes
    /// <c>strict: true</c> cannot be silently downgraded by a stale shell variable.</param>
    public FaultBarrier(string label, TextWriter? output = null, string? reportDirectory = null, bool strict = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        _label = label;
        _output = output ?? Console.Error;
        _reportDirectory = reportDirectory is null
            ? DefaultReportDirectory
            : string.IsNullOrWhiteSpace(reportDirectory) ? null : reportDirectory;
        Strict = strict || Environment.GetEnvironmentVariable(StrictModeVariable) == "1";
    }

    /// <summary>Where crash reports go when no directory is supplied:
    /// <c>&lt;ApplicationData&gt;/Aigis/crash</c> — beside Triage's <c>progress.json</c> and
    /// <c>settings.json</c>, and deliberately NOT inside the repo, so a player who installed a build
    /// can find it and a developer's working tree never fills up with them.</summary>
    public static string DefaultReportDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Aigis",
        "crash");

    /// <summary>True when every guard rethrows instead of containing — see the type's doc comment.</summary>
    public bool Strict { get; }

    /// <summary>Faults contained since this barrier was created, across every site.</summary>
    public int TotalFaults { get; private set; }

    /// <summary>How many distinct call sites have failed at least once. A host wanting one number
    /// for "how bad is it" wants this rather than <see cref="TotalFaults"/>, which a single
    /// per-frame failure inflates without saying anything new.</summary>
    public int FaultedSiteCount
    {
        get
        {
            lock (_gate)
            {
                return _sites.Count;
            }
        }
    }

    /// <summary>The most recent contained fault, or <see langword="null"/> if there has been none.
    /// Read under the lock rather than as a plain auto-property: <see cref="Fault"/> is a multi-field
    /// value type, so a reader racing the process backstop's thread could otherwise see half of one
    /// fault and half of another.</summary>
    public Fault? LastFault
    {
        get
        {
            lock (_gate)
            {
                return _lastFault;
            }
        }
    }

    /// <summary>The crash-report file, once one has actually been written. <see langword="null"/>
    /// until the first fault — a clean session leaves no file behind — and also null when file
    /// reporting is disabled or the write itself failed.</summary>
    public string? ReportPath
    {
        get
        {
            lock (_gate)
            {
                return _reportPath;
            }
        }
    }

    /// <summary>Whether a fault happened within <paramref name="window"/> of now. Drives the host's
    /// on-screen notice, so it appears the moment something breaks and clears itself once the game
    /// has been healthy for a while.</summary>
    public bool HasFaultWithin(TimeSpan window)
    {
        lock (_gate)
        {
            return _sinceLastFault.IsRunning && _sinceLastFault.Elapsed <= window;
        }
    }

    /// <summary>Runs <paramref name="work"/>, containing any exception it throws. Returns whether it
    /// ran to completion, so a caller can skip follow-up work that only makes sense if it did.</summary>
    public bool Guard(string site, Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        try
        {
            work();
        }
        catch (Exception error) when (!Strict)
        {
            Report(site, error);
            return false;
        }

        Succeeded(site);
        return true;
    }

    /// <summary>Runs <paramref name="work"/>, returning <paramref name="fallback"/> if it throws.
    /// The fallback is this call site's answer to "what does this mean when it cannot be computed" —
    /// an empty scene, the previous status line, "no, the game did not handle Escape" — and is
    /// always chosen by the host, never defaulted here.</summary>
    public TResult Guard<TResult>(string site, Func<TResult> work, TResult fallback)
    {
        ArgumentNullException.ThrowIfNull(work);
        TResult result;
        try
        {
            result = work();
        }
        catch (Exception error) when (!Strict)
        {
            Report(site, error);
            return fallback;
        }

        Succeeded(site);
        return result;
    }

    /// <summary>
    /// The same, for a call site inside a per-frame handler: <paramref name="state"/> carries
    /// whatever the work needs, so the lambda can be <c>static</c> and its delegate cached by the
    /// compiler instead of allocated every frame.
    /// <para>
    /// This overload exists for one measured reason. A closure over a variable declared INSIDE a
    /// frame handler allocates a display class and a delegate on every frame, and the render path
    /// this barrier wraps is the one CLAUDE.md's performance section drove from 492 KB to 29 KB per
    /// frame. Adding a closure per guarded call would have quietly given part of that back.
    /// </para>
    /// </summary>
    public TResult Guard<TState, TResult>(string site, TState state, Func<TState, TResult> work, TResult fallback)
    {
        ArgumentNullException.ThrowIfNull(work);
        TResult result;
        try
        {
            result = work(state);
        }
        catch (Exception error) when (!Strict)
        {
            Report(site, error);
            return fallback;
        }

        Succeeded(site);
        return result;
    }

    /// <summary>The <see cref="Action{T}"/> form of the state-passing overload above, for the same
    /// allocation reason.</summary>
    public bool Guard<TState>(string site, TState state, Action<TState> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        try
        {
            work(state);
        }
        catch (Exception error) when (!Strict)
        {
            Report(site, error);
            return false;
        }

        Succeeded(site);
        return true;
    }

    /// <summary>
    /// Records and describes a fault the caller caught itself. Public because not everything worth
    /// containing is a call the barrier can wrap — an exception surfaced by a background task, or
    /// one a caller already has its own <c>try</c> around for a different reason, still belongs in
    /// the same console line, the same repeat counter and the same report file.
    /// <para>
    /// Never throws: the reporting path is wrapped in its own guard, and a report file that cannot
    /// be written is given up on permanently rather than retried once a frame. A crash reporter that
    /// crashes is worse than no crash reporter.
    /// </para>
    /// </summary>
    public Fault Report(string site, Exception error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(site);
        ArgumentNullException.ThrowIfNull(error);

        Fault fault;
        bool describe;
        lock (_gate)
        {
            if (!_sites.TryGetValue(site, out var state))
            {
                state = new SiteState();
                _sites[site] = state;
            }

            state.Total++;
            state.Consecutive++;
            TotalFaults++;
            _sinceLastFault.Restart();

            // Rate limit by powers of ten (1st, 10th, 100th, ...). A call site that fails EVERY
            // frame would otherwise write sixty descriptions a second, which floods the console,
            // grows the report file without bound, and — because writing to a console is itself
            // slow — turns a contained bug into an unusable framerate.
            describe = state.Total >= state.NextDescribeAt;
            if (describe)
            {
                state.NextDescribeAt = (long)state.Total * 10;
            }

            fault = new Fault(site, error.GetType().Name, error.Message, state.Total);
            _lastFault = fault;
        }

        if (describe)
        {
            Describe(fault, error);
            RaiseObserved(fault, error);
        }

        return fault;
    }

    /// <summary>
    /// Raised for every fault this process DESCRIBES — the same 1st/10th/100th thinning as the console
    /// and report file — with the barrier's label (2026-09-25). This is how a host forwards faults to a
    /// crash service without this library knowing one exists: the engine stays free of network and
    /// vendor code, and a host that subscribes nothing pays nothing.
    /// <para>
    /// A handler that throws is swallowed here, for <see cref="Report"/>'s own reason: a crash reporter
    /// that crashes is worse than none. Raised on whichever thread faulted.
    /// </para>
    /// </summary>
    public static event Action<string, Fault, Exception>? Observed;

    private void RaiseObserved(Fault fault, Exception error)
    {
        var handlers = Observed;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action<string, Fault, Exception>>())
        {
            try
            {
                handler(_label, fault, error);
            }
            catch (Exception)
            {
                // See the event's doc comment.
            }
        }
    }

    /// <summary>How many times <paramref name="site"/> has failed. Zero for a site that never has.</summary>
    public int FaultCountAt(string site)
    {
        lock (_gate)
        {
            return _sites.TryGetValue(site, out var state) ? state.Total : 0;
        }
    }

    /// <summary>How many times <paramref name="site"/> has failed in a row without a success in
    /// between — the number that separates "one bad frame" from "this call is simply broken now",
    /// which is what a host uses to decide whether a warning is worth escalating.</summary>
    public int ConsecutiveFaultsAt(string site)
    {
        lock (_gate)
        {
            return _sites.TryGetValue(site, out var state) ? state.Consecutive : 0;
        }
    }

    /// <summary>
    /// Subscribes this barrier to the two process-wide failure signals, once per process:
    /// <see cref="AppDomain.UnhandledException"/> and
    /// <see cref="TaskScheduler.UnobservedTaskException"/>.
    /// <para>
    /// These are a REPORTING backstop, not a second containment layer, and the distinction matters.
    /// An unhandled exception on a thread the host does not own terminates the process no matter
    /// what this handler does; all it can buy is a described fault and a report file, so the failure
    /// is not silent. The unobserved-task case is the one it can genuinely settle: it marks the
    /// exception observed, which is what a fire-and-forget background task (the phone controller's
    /// accept loop, for one) needs so a dropped connection cannot escalate.
    /// </para>
    /// </summary>
    public static void InstallProcessBackstop(FaultBarrier barrier)
    {
        ArgumentNullException.ThrowIfNull(barrier);
        Volatile.Write(ref _processBackstop, barrier);
        if (_processHandlersInstalled)
        {
            return;
        }

        _processHandlersInstalled = true;
        // Survived errors from libraries that may not print (hard rule 2) -- see ErrorSink.
        ErrorSink.Reported += static (site, error) => ReportToBackstop(site, error);
        AppDomain.CurrentDomain.UnhandledException += static (_, args) =>
        {
            if (Volatile.Read(ref _processBackstop) is { } current && args.ExceptionObject is Exception error)
            {
                current.Report(args.IsTerminating ? "process (terminating)" : "process", error);
            }
        };
        TaskScheduler.UnobservedTaskException += static (_, args) =>
        {
            args.SetObserved();
            Volatile.Read(ref _processBackstop)?.Report("background task", args.Exception);
        };
    }

    /// <summary>
    /// Reports <paramref name="error"/> to whichever barrier <see cref="InstallProcessBackstop"/>
    /// last installed — for game code that catches an exception itself and would otherwise leave no
    /// trace a player can send in.
    ///
    /// <para><b>Why this is needed even though <see cref="Report(string, Exception)"/> exists.</b>
    /// The barrier belongs to the host, and game code does not hold one. So a deliberate
    /// <c>catch</c> deep in a game — a background file write that failed, a recording that could
    /// not be validated — had exactly one way to say so: a line on a console the player usually
    /// does not have. That is how a 2026-09-22 report of "an exception on stage 1-3" arrived with
    /// no crash report and no file, and with two rounds' recordings quietly missing.</para>
    ///
    /// <para><b>It does not contain anything</b> — the caller has already done that. It only makes
    /// the failure visible: the same console line, the same report file, and the same on-screen
    /// notice the host shows for a contained fault.</para>
    /// </summary>
    /// <returns><see langword="false"/> when no backstop is installed (every headless path, and
    /// every test), so a caller can fall back to its own logging. Never throws.</returns>
    public static bool ReportToBackstop(string site, Exception error)
    {
        if (Volatile.Read(ref _processBackstop) is not { } barrier)
        {
            return false;
        }

        barrier.Report(site, error);
        return true;
    }

    /// <summary>A one-line end-of-session summary, or <see langword="null"/> for a clean session, so
    /// a host can print it unconditionally and say nothing when there is nothing to say.</summary>
    public string? Summary()
    {
        lock (_gate)
        {
            if (TotalFaults == 0)
            {
                return null;
            }

            var summary = new StringBuilder()
                .Append(CultureInfo.InvariantCulture, $"[{_label}] contained {TotalFaults} error(s) at {_sites.Count} call site(s) this session: ")
                .AppendJoin(", ", _sites.OrderByDescending(s => s.Value.Total).Select(s => $"{s.Key} x{s.Value.Total}"))
                .Append('.');
            if (_reportPath is { } path)
            {
                summary.Append(CultureInfo.InvariantCulture, $" Report: {path}");
            }

            return summary.ToString();
        }
    }

    /// <summary>Records that a site reached by a plain try/catch (rather than a guard) succeeded this
    /// time, so <see cref="ConsecutiveFaultsAt"/> means "failing right now" there too.</summary>
    public void MarkSucceeded(string site) => Succeeded(site);

    /// <summary>Clears a site's consecutive-failure run. Separate from the guards so all four
    /// overloads share one definition of "it worked this time".</summary>
    private void Succeeded(string site)
    {
        // Nothing has ever failed anywhere: the overwhelmingly common case, and establishing it must
        // not cost a lock on every guarded call of every frame.
        if (TotalFaults == 0)
        {
            return;
        }

        lock (_gate)
        {
            if (_sites.TryGetValue(site, out var state))
            {
                state.Consecutive = 0;
            }
        }
    }

    // Everything below catches Exception without a filter, which this file is the one legitimate
    // place for. A guard's whole promise is that control comes back to the caller; a reporter that
    // throws while describing the fault would break exactly the promise it exists to serve, and it
    // would do so from inside a catch block, replacing a diagnosable bug with an undiagnosable one.
    private void Describe(Fault fault, Exception error)
    {
        string line;
        try
        {
            var text = new StringBuilder();
            text.Append(CultureInfo.InvariantCulture, $"[{_label}] contained an error in {fault.Site}");
            if (fault.OccurrenceAtSite > 1)
            {
                text.Append(CultureInfo.InvariantCulture, $" (occurrence #{fault.OccurrenceAtSite}; further ones are reported at 10x intervals)");
            }

            text.AppendLine(":");
            AppendContext(text);
            // ToString() rather than Message: the stack trace is most of the report's value, and the
            // inner-exception chain it also prints is usually where the real cause is. It is also a
            // virtual call into the failing code's own type, which is why it is inside this try.
            text.Append(error);
            line = text.ToString();
        }
        catch (Exception)
        {
            line = $"[{_label}] contained an error in {fault.Site} ({fault.ExceptionType}); its own description could not be built.";
        }

        try
        {
            _output.WriteLine(line);
            _output.Flush();
        }
        catch (Exception)
        {
            // A closed or broken stderr must not be what ends the session. The file below is the
            // other half of the report and is attempted regardless.
        }

        AppendToReport(line);
    }

    private void AppendToReport(string line)
    {
        if (_reportDirectory is null || _reportingIsBroken)
        {
            return;
        }

        try
        {
            string path;
            bool isNew;
            lock (_gate)
            {
                isNew = _reportPath is null;
                if (isNew)
                {
                    Directory.CreateDirectory(_reportDirectory);
                    var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                    _reportPath = Path.Combine(_reportDirectory, $"{_label}-{stamp}.log");
                    PruneOldReports(_reportDirectory, _label);
                }

                path = _reportPath!;
            }

            if (isNew)
            {
                File.AppendAllText(path, Header(), Encoding.UTF8);
            }

            File.AppendAllText(
                path,
                string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}{Environment.NewLine}"),
                Encoding.UTF8);
        }
        catch (Exception)
        {
            // Read-only profile, full disk, a locked-down security policy, an exotic path: give up on
            // the FILE for the rest of the session rather than retrying once a frame, and keep the
            // console half working. The barrier's job is to survive; the report is a nice-to-have on
            // top of that, and it is not worth enumerating every way a filesystem can say no.
            _reportingIsBroken = true;
            lock (_gate)
            {
                _reportPath = null;
            }
        }
    }

    private string Header() => new StringBuilder()
        .AppendLine(CultureInfo.InvariantCulture, $"{EngineInfo.Name} {EngineInfo.Version} — contained-error report ({_label})")
        .AppendLine(CultureInfo.InvariantCulture, $"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}")
        .AppendLine(CultureInfo.InvariantCulture, $"OS: {Environment.OSVersion} ({(Environment.Is64BitProcess ? "x64" : "x86")}), .NET {Environment.Version}")
        .AppendLine(CultureInfo.InvariantCulture, $"Command line: {Environment.CommandLine}")
        .AppendLine(CultureInfo.InvariantCulture, $"CPU: {Environment.ProcessorCount} logical, culture {CultureInfo.CurrentCulture.Name}")
        .AppendLine("Every entry below is an error the game continued through. The session did not stop here.")
        .AppendLine("Each entry carries the environment and game context at the moment it happened.")
        .AppendLine()
        .ToString();

    /// <summary>How many of this label's reports the folder keeps. It was never pruned, so a player
    /// whose game contained an error every session collected one file per launch for ever.</summary>
    public const int ReportsKept = 30;

    private static void PruneOldReports(string directory, string label)
    {
        try
        {
            var old = new DirectoryInfo(directory)
                .GetFiles($"{label}-*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(ReportsKept - 1);
            foreach (var file in old)
            {
                file.Delete();
            }
        }
        catch (Exception)
        {
            // Housekeeping only: a file that cannot be deleted now is retried next launch.
        }
    }

    // ---- What every entry says about the moment it happened (2026-10-15) ----------------------
    //
    // A stack trace says WHERE; it rarely says in what situation, and "it failed on step N of which
    // stage, with how many seats, on which GPU" is what a report needs to be reproducible. Two
    // process-wide sources, both read only while describing (which is already rate-limited per site):
    // stable facts a host learns once (build, GPU) and a provider the game answers live (stage,
    // seed, clock, seats).

    private static readonly ConcurrentDictionary<string, string> EnvironmentDetails = new(StringComparer.Ordinal);

    /// <summary>Records a fact about this process every later entry repeats (e.g. <c>"gpu"</c>,
    /// <c>"build"</c>). Setting a key again replaces it.</summary>
    public static void SetEnvironmentDetail(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        EnvironmentDetails[key] = value ?? string.Empty;
    }

    /// <summary>The game's own description of what it is doing right now — called on the faulting
    /// thread while an entry is written, so it must be cheap, must not take locks the game thread
    /// holds, and may throw (that is reported in its place).</summary>
    public static Func<string?>? ContextProvider { get; set; }

    private static void AppendContext(StringBuilder text)
    {
        if (!EnvironmentDetails.IsEmpty)
        {
            text.Append("  environment: ")
                .AppendJoin(", ", EnvironmentDetails.OrderBy(d => d.Key, StringComparer.Ordinal).Select(d => $"{d.Key}={d.Value}"))
                .AppendLine();
        }

        if (ContextProvider is not { } provider)
        {
            return;
        }

        string? context;
        try
        {
            context = provider();
        }
        catch (Exception error)
        {
            context = $"(the context provider itself failed: {error.GetType().Name}: {error.Message})";
        }

        if (!string.IsNullOrEmpty(context))
        {
            text.Append("  context: ").AppendLine(context);
        }
    }

    private sealed class SiteState
    {
        public int Total;
        public int Consecutive;
        public long NextDescribeAt = 1;
    }
}
