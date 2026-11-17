@echo off
rem usage: all.bat <seeds, space separated> : base (inner 無し) と new (今の教科書) を各種で測る
for %%S in (%*) do (
  call docs\arena\cand\inner\run.bat base %%S --style-katana docs\arena\cand\inner\katana_base.json --style-longsword docs\arena\cand\inner\longsword_base.json
  call docs\arena\cand\inner\run.bat new %%S
)
