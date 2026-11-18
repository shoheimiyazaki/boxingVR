@echo off
rem VR training for katana exemplars, option 2: first-person camera (docs\VR_MOTION.md section 6). ASCII only on purpose.
rem The camera stays at your head pose in VR space (not locked to the puppet). The puppet head is driven to it;
rem your own head is not drawn. Both controllers move their own hand (--two-hand both) to test the camera alone.
rem   A / X  height calibration: stand straight and press. This also fixes the VR-space-to-world transform
rem           (scale = puppet arm / your arm; the HMD is placed on the puppet head and facing).
rem   left stick  moves the VR origin in the world (the body walks after it);  grips hold the sword
rem   right stick left/right = form, down = rebuild (the transform is re-aligned);  B / Y = record
rem Options are passed to the VR host, e.g. run_vr_training_katana_fpv.cmd --runtime vd --scale 0.7
cd /d "%~dp0"
call run.cmd vr --training katana --first-person --two-hand both %*
