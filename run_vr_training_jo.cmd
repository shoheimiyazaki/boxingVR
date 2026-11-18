@echo off
rem VR training for the jo (130 cm staff) (docs\VR_MOTION.md sections 5 and 6). ASCII only on purpose.
rem Same controls as run_vr_training_katana.cmd: grips hold the weapon; A+B+X+Y together = rebuild; A alone = height calibration;
rem X = left kick, Y = right kick (far = front kick, close = knee); right stick left/right = form; recording is always on (one file per form/reset).
rem Two-handed: with both grips held the right controller moves both hands (--two-hand right).
cd /d "%~dp0"
call run.cmd vr --training jo --two-hand right %*
