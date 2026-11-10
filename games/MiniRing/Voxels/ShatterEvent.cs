using System.Numerics;

namespace MiniRing.Voxels;

/// <summary>砕けたボクセル 1 個ぶんの音の粒: 何の材質だったか（<see cref="BoxerAnatomy"/> の番号）と、1 発の始まりから鳴るまで [s]。</summary>
public readonly record struct ShatterGrain(byte Material, float Delay);

/// <summary>
/// 1 発で砕けたボクセルの列（音と演出用）。粒は砕けた順（当たった所に近い順）に、少しずつ遅れて並ぶ。
/// <paramref name="Fatal"/> はこの 1 発で臓器が壊れて死んだとき。
/// </summary>
public sealed record ShatterEvent(Vector3 Point, IReadOnlyList<ShatterGrain> Grains, bool Fatal);
