@echo off
set C=docs\arena\cand
for %%s in (271 391 511 631) do (
 call %C%\run.cmd khalf %%s --style-katana %C%\kt_half.json --style-longsword %C%\ls_aim.json
 call %C%\run.cmd kkiri %%s --style-katana %C%\kt_kiri.json --style-longsword %C%\ls_aim.json
)
echo finished > %C%\all4.done
