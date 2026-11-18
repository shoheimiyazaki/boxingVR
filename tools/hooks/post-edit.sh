#!/usr/bin/env bash
# Claude Code PostToolUse hook (Edit|Write|MultiEdit): reminds of the repo rules that are easy to forget
# (CLAUDE.md §4: shader SPIR-V, versions). Says each reminder once per session; exits 2 so the agent sees it.
input=$(cat)
file=$(printf '%s' "$input" | sed -n 's/.*"file_path"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -1 | tr '\\' '/' | sed 's#//#/#g')
session=$(printf '%s' "$input" | sed -n 's/.*"session_id"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -1)
[ -n "$file" ] || exit 0

remind() { # key message
  local mark="${TMPDIR:-/tmp}/miniring-hook-${session:-none}-$1"
  [ -e "$mark" ] && return 0
  : > "$mark"
  echo "$2" >&2
  said=1
}

said=0
case "$file" in
  */src/Aigis.Render/ShaderSource.cs)
    remind shader "ShaderSource.cs を変えた: PrecompiledShaders の SPIR-V を shaderc で作り直す（CLAUDE.md §4.4）。" ;;
  */games/MiniRing/Bots/*|*/games/MiniRing/IntentControl.cs|*/games/MiniRing/Bots/weapons/*)
    remind logic "ボットの判断に触れた: 試合の結果が変わるなら BoxerBot.LogicVersion を上げ docs/VERSIONS.md に 1 行（skill: bump-version）。" ;;
  */games/MiniRing/Fighter*.cs|*/games/MiniRing/BoxingGame*.cs|*/games/MiniRing/Footwork.cs|*/games/MiniRing/Voxels/*|*/games/MiniRing/Weapons/*|*/games/MiniRing/LivingRoom.cs|*/games/MiniRing/RingLayout.cs|*/src/Aigis.Physics/*)
    remind physics "体の物理・当たりに触れた: 試合の結果が変わるなら Fighter.PhysicsVersion を上げ docs/VERSIONS.md に 1 行（skill: bump-version）。近道を入れたら docs/DEBT.md に。" ;;
  */tests/MiniRing.Tests/*.cs)
    remind tests "テストを足した・変えた: 試合や長い物理を回すクラスには [TestCategory(\"slow\")]（目安: クラスで 3 秒以上）。" ;;
esac
[ "$said" = 1 ] && exit 2
exit 0
