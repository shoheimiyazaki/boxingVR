@echo off
rem 最終の教科書（ロングソードの zwerchhau を 0 にした後）: 練習の種と、使っていない種
for %%S in (271 391 511 631 1229 1361 1493 1627) do call docs\arena\cand\inner\run.bat fin2 %%S
