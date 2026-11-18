@echo off
rem VR training for katana exemplars, option 1 (docs\VR_MOTION.md sections 5 and 6). ASCII only on purpose.
rem Hold the sword with the GRIP buttons (no trigger). With both grips held the RIGHT controller moves both hands:
rem the left hand goes to the point on the hilt behind the right hand. Release the left grip to use the left controller (one hand).
rem   right stick left/right  previous/next form (the world is rebuilt at that form start distance)
rem   A+B+X+Y together        rebuild the same form (after cutting the opponent apart, or if you drifted)
rem   A alone                 height calibration (acts when A is released, unless the four-button chord happened)
rem   X / Y                   left / right kick: far = front kick, close = knee kick
rem   left stick walks. Recording is always on: one motions\DATE_katana_FORM.vrmotion per form / rebuild.
rem Options are passed to the VR host, e.g. run_vr_training_katana.cmd --runtime vd --scale 0.7
cd /d "%~dp0"
call run.cmd vr --training katana --two-hand right %*
