#!/bin/bash
# v2: 縮約モデルの胴のひねりの候補の予測の列つき（LHS 250 点、自由 / 固定）。
cd "$(dirname "$0")/../../.."
DRIVE_DOE_LABEL=doe_lhs_v4 DRIVE_DOE_N=250 DRIVE_DOE_THREADS=6 dotnet test tests/MiniRing.Tests -c Release --no-build --filter "FullyQualifiedName~DriveDoeProbe&Name=Doe_Lhs" --logger "console;verbosity=detailed" 2>&1 | grep -E "DOE|FAILED|成功|失敗"
