@echo off
rem Mini Ring Boxing launcher (ASCII only on purpose: cmd.exe tracks its read position by bytes).
rem From PowerShell type .\run.cmd (PowerShell does not run files from the current folder by bare name).
rem
rem   run.cmd                    play on the desktop (keyboard/mouse). The match is recorded to replays\
rem                              keys (intent control; "run.cmd --control classic" for the old keys):
rem                                arrows/WASD  move relative to the camera (the camera follows you; the fighter keeps facing the opponent)
rem                                J / right click   light (front hand): tap = quick flicking jab, hold + release = loaded pushing jab
rem                                K / left click    heavy (rear hand): tap = quick, hold + release = loaded
rem                                tap J J K in order for jab-jab-straight (up to 4 presses are queued for 0.5 s)
rem                                Space             guard; Space + forward = slip/duck/weave, + back = sway, + side = quick side step
rem                                J/K while holding Space = counter from the evasion (duck: body hook, slip: hook with the opposite hand)
rem                                R reset, 1/2/3 speed, middle-drag orbit the camera, Escape quit
rem   run.cmd vr [options]       play in VR. Default runtime: Meta Quest Link, then Virtual Desktop (no SteamVR)
rem                              options: --runtime auto^|meta^|vd^|steamvr^|system^|file.json  --scale 0.3..1.0
rem   run.cmd replay [file]      watch a recording in real time (default: the newest file in replays\)
rem   run.cmd verify [file]      re-simulate a recording and check it reproduces exactly (default: newest)
rem   run.cmd selftest           check the VR render path without a headset
rem   run.cmd test               run the automated tests (excluding GPU snapshots)
rem   run.cmd arena [args]       bot-vs-bot arena: league [--bouts N --seconds S] / tune --base NAME (see docs\BOTS.md)
rem                              video [--red A --blue B --seconds S --out f.mp4] renders a match (needs ffmpeg)
rem
rem Builds Release incrementally first. If the build itself crashes (e.g. MSB4166 after the PC ran out of
rem memory), it restarts the .NET build servers and tries once more.

setlocal
set "EXITCODE="
cd /d "%~dp0"
set "MODE=%~1"
set "DESKTOP=games\MiniRing.Desktop"
set "VR=games\MiniRing.Vr"
set "ARENA=games\MiniRing.Arena"

if /i "%MODE%"=="vr" goto :vr
if /i "%MODE%"=="replay" goto :replay
if /i "%MODE%"=="verify" goto :verify
if /i "%MODE%"=="selftest" goto :selftest
if /i "%MODE%"=="test" goto :test
if /i "%MODE%"=="arena" goto :arena
if not "%MODE%"=="" if not "%MODE:~0,2%"=="--" goto :usage

call :build "%DESKTOP%" || goto :end
dotnet run --project %DESKTOP% -c Release --no-build -- %*
goto :end

:vr
call :build "%VR%" || goto :end
dotnet run --project %VR% -c Release --no-build -- %2 %3 %4 %5 %6 %7 %8 %9
goto :end

:replay
call :pick "%~2"
if not defined FILE goto :nofile
call :build "%DESKTOP%" || goto :end
echo Replaying %FILE%
dotnet run --project %DESKTOP% -c Release --no-build -- --replay "%FILE%"
goto :end

:verify
call :pick "%~2"
if not defined FILE goto :nofile
call :build "%DESKTOP%" || goto :end
dotnet run --project %DESKTOP% -c Release --no-build -- --verify "%FILE%"
goto :end

:arena
rem Forward every argument after "arena" (not just %2..%9). cmd splits comma lists into separate
rem arguments; the arena joins the values that follow one --name back together.
shift
set "ARGS="
:arenaargs
if "%~1"=="" goto :arenarun
set ARGS=%ARGS% "%~1"
shift
goto :arenaargs
:arenarun
call :build "%ARENA%" || goto :end
dotnet run --project %ARENA% -c Release --no-build --%ARGS%
goto :end

:selftest
call :build "%VR%" || goto :end
dotnet run --project %VR% -c Release --no-build -- --selftest
goto :end

:test
dotnet test tests\MiniRing.Tests -c Release --filter "TestCategory!=visual"
goto :end

rem Builds the project; on failure restarts the build servers and retries once.
:build
dotnet build "%~1" -c Release -v q -nologo
if not errorlevel 1 exit /b 0
echo.
echo Build failed. Restarting the .NET build servers and trying once more...
dotnet build-server shutdown >nul 2>&1
dotnet build "%~1" -c Release -v q -nologo
if not errorlevel 1 exit /b 0
echo Build failed again. See the errors above.
exit /b 1

rem Sets FILE to the given path, or to the newest recording in replays\ when none is given.
:pick
set "FILE=%~1"
if defined FILE exit /b 0
for /f "delims=" %%f in ('dir /b /o-d "replays\*.mrrep" 2^>nul') do (
    set "FILE=replays\%%f"
    exit /b 0
)
exit /b 0

:nofile
echo No recording found. Play a match first (run.cmd or run.cmd vr); recordings go to replays\
set "EXITCODE=1"
goto :end

:usage
echo Unknown mode "%MODE%". Use: run.cmd [vr^|replay^|verify^|selftest^|test^|arena]
set "EXITCODE=1"
goto :end

:end
if not defined EXITCODE set "EXITCODE=%ERRORLEVEL%"
endlocal & exit /b %EXITCODE%
