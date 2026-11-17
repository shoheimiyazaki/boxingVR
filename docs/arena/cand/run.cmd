@echo off
rem run.cmd <name> <seed> [style args...]  : handlelab の比べ（刀・ロングソード・こんぼうの 3 組）。結果は res_<name>_<seed>.md
set N=%1
set S=%2
shift
shift
dotnet run --project games\MiniRing.Arena -c Release --no-build -- handlelab --pairs jin:konrad,jin:jin,konrad:sean --bouts 4 --seconds 30 --parallel 4 --seed %S% %1 %2 %3 %4 --title "%N% seed %S%" > docs\arena\cand\res_%N%_%S%.md 2> docs\arena\cand\log_%N%_%S%.txt
