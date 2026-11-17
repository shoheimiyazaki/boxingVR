@echo off
set C=docs\arena\cand
for %%s in (751 877 991 1103) do (
 call %C%\run.cmd before %%s --style-katana %C%\katana_before.json --style-longsword %C%\longsword_before.json
 call %C%\run.cmd kbaim %%s --style-katana %C%\katana_before.json --style-longsword %C%\ls_aim.json
 call %C%\run.cmd khalf %%s --style-katana %C%\kt_half.json --style-longsword %C%\ls_aim.json
 call %C%\run.cmd aim %%s --style-katana %C%\kt_far.json --style-longsword %C%\ls_aim.json
 call %C%\run.cmd khalfold %%s --style-katana %C%\kt_half.json --style-longsword %C%\longsword_before.json
 call %C%\run.cmd cur %%s
)
echo finished > %C%\all5.done
