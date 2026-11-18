@echo off
rem VR training for katana exemplars, option 1 (docs\VR_MOTION.md sections 5 and 6). ASCII only on purpose.
rem Hold the sword with the GRIP buttons (no trigger). With both grips held the RIGHT controller moves both hands:
rem the left hand goes to the point on the hilt behind the right hand. Release the left grip to use the left controller (one hand).
rem   right stick left/right  previous/next form;  right stick down = rebuild the same form
rem   B / Y                   start/stop recording to motions\DATE_katana_FORM.vrmotion
rem   A / X                   height calibration;  left stick walks
rem Options are passed to the VR host, e.g. run_vr_training_katana.cmd --runtime vd --scale 0.7
cd /d "%~dp0"
call run.cmd vr --training katana --two-hand right %*
