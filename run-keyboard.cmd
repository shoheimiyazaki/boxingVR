@echo off
rem Mini Ring Boxing - try the intent keyboard control on the desktop (ASCII only on purpose).
rem
rem   run-keyboard.cmd                  intent control (default), gloves
rem   run-keyboard.cmd --wear none      bare fists (glove^|none^|knuckles^|katar^|gauntlet)
rem   run-keyboard.cmd --control classic   the old control, for comparison
rem
rem Keys (docs\INTENT_CONTROL.md):
rem   Arrows / WASD   move relative to the camera; face stays toward the opponent
rem   J / right click light attack (lead hand). Hold to wind up, release to throw
rem   K / left click  heavy attack (rear hand). Direction + attack changes the technique
rem   Space           evade (forward = slip/duck/weave, back = sway, side = sidestep); no direction = guard
rem   R               restart the round

setlocal
cd /d "%~dp0"
echo Keys: move = Arrows/WASD, light = J, heavy = K, evade = Space, restart = R
dotnet build games\MiniRing.Desktop -c Release -v q -nologo
if errorlevel 1 (
  echo Build failed.
  pause
  exit /b 1
)
dotnet run --project games\MiniRing.Desktop -c Release --no-build -- --control intent %*
endlocal
