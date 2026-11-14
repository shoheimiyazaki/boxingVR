using System.Numerics;

namespace MiniRing;

/// <summary>肩中心の体フレームでの足首の目標 [m]。膝の目標は曲げる平面を決める。姿勢・位置は直接書き込まない。</summary>
public readonly record struct LegCommand(Vector3 TargetLocal, Vector3? KneeTargetLocal = null, Quaternion? Orientation = null, float Spring = 1f);
