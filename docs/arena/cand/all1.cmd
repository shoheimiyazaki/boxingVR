@echo off
set C=docs\arena\cand
call %C%\run.cmd before 271 --style-katana %C%\katana_before.json --style-longsword %C%\longsword_before.json
call %C%\run.cmd cur 271
call %C%\run.cmd safe 271 --style-katana %C%\kt_safe.json --style-longsword %C%\ls_safe.json
call %C%\run.cmd far 271 --style-katana %C%\kt_far.json --style-longsword %C%\ls_far.json
echo finished > %C%\all1.done
