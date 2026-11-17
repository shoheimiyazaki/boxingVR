@echo off
set C=docs\arena\cand
for %%s in (271 391 511 631) do (
 call %C%\run.cmd before %%s --style-katana %C%\katana_before.json --style-longsword %C%\longsword_before.json
 call %C%\run.cmd cur %%s
 call %C%\run.cmd far %%s --style-katana %C%\kt_far.json --style-longsword %C%\ls_far.json
 call %C%\run.cmd u1 %%s --style-katana %C%\kt_far.json --style-longsword %C%\ls_u1.json
 call %C%\run.cmd aim %%s --style-katana %C%\kt_far.json --style-longsword %C%\ls_aim.json
 call %C%\run.cmd kin %%s --style-katana %C%\kt_in.json --style-longsword %C%\ls_u1.json
)
echo finished > %C%\all2.done
