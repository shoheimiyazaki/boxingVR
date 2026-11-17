@echo off
rem 最終の教科書: 練習の種（fin）と、使っていない種（base と new）
for %%S in (271 391 511 631) do call docs\arena\cand\inner\run.bat fin %%S
call docs\arena\cand\inner\all.bat 1229 1361 1493 1627
