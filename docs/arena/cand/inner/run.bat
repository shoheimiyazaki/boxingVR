@echo off
rem usage: run.bat <tag> <seed> [extra handlelab args]   -> docs\arena\cand\inner\res_<tag>_<seed>.md
set TAG=%1
set SEED=%2
shift
shift
dotnet run --project games\MiniRing.Arena -c Release -- handlelab --pairs jin:konrad,jin:jin,konrad:sean --bouts 4 --seconds 30 --seed %SEED% --out docs\arena\cand\inner\res_%TAG%_%SEED%.md %1 %2 %3 %4 %5 %6
