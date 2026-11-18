@echo off
rem VR training for katana exemplars (docs\VR_MOTION.md section 5). ASCII only on purpose.
rem You cut into a still opponent who holds the katana in seigan (middle guard), and record your motion.
rem   right stick left/right  previous/next form; the world is rebuilt at that form's starting distance
rem   right stick down        rebuild at the same form (after cutting the opponent apart, or if you drifted)
rem   B / Y                   start/stop recording to motions\DATE_katana_FORM.vrmotion
rem   A / X                   height calibration (stand straight and press)
rem   trigger                 hold the grip; left stick walks
rem Options are passed to the VR host, e.g. run_vr_training_katana.cmd --runtime vd --scale 0.7
cd /d "%~dp0"
call run.cmd vr --training katana %*
