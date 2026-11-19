@echo off
rem VR training for the longsword, first person (docs\VR_MOTION.md sections 5 and 6). ASCII only on purpose.
rem Same controls as run_vr_training_katana.cmd: grips hold the weapon; A+B+X+Y together = rebuild; A alone = height calibration;
rem X = left kick, Y = right kick (far = front kick, close = knee); right stick left/right = form; recording is always on (one file per form/reset).
rem First-person camera; both controllers move their own hand (--two-hand both).
cd /d "%~dp0"
call run.cmd vr --training longsword --first-person --two-hand both %*
