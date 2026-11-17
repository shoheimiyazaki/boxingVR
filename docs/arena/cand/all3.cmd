@echo off
set C=docs\arena\cand
for %%s in (271 391 511 631) do (
 call %C%\run.cmd kbaim %%s --style-katana %C%\katana_before.json --style-longsword %C%\ls_aim.json
 call %C%\run.cmd kcuraim %%s --style-longsword %C%\ls_aim.json
 call %C%\run.cmd kinaim %%s --style-katana %C%\kt_in.json --style-longsword %C%\ls_aim.json
 call %C%\run.cmd aim25 %%s --style-katana %C%\kt_far.json --style-longsword %C%\ls_aim25.json
)
echo finished > %C%\all3.done
