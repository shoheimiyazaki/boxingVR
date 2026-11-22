#!/bin/bash
# 駆動 DOE の実行（ビルド済みの Release を使う）。値は docs/plans/PHYSICS_DRIVE_DOE_LOG.md を参照。
cd "$(dirname "$0")/../../.."
for m in Design_WritesTheFactorTable Baseline_WritesSeries Targets_BaselineBody Doe_OneAtATime Doe_Lhs Doe_LhsInstant; do
  dotnet test tests/MiniRing.Tests -c Release --no-build --filter "FullyQualifiedName~DriveDoeProbe&Name~$m" --logger "console;verbosity=detailed" 2>&1 | grep -E "DOE|FAILED|成功|失敗|baseline" 
done
