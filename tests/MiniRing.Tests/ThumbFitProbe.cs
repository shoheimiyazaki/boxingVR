using System.Globalization;
using System.Numerics;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>調査用: 親指の関節角の候補（環境変数 THUMB_TRY = "a,b,c;..." 度）ごとに、正拳での親指のセルの置き場（重心・前の面からの距離）と押し出された数を出す。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class ThumbFitProbe
{
    [TestMethod]
    public void Probe_ThumbAngles()
    {
        const float D = MathF.PI / 180f;
        var seiken = HandSkeleton.PoseOf(HandShape.Seiken);
        foreach (var spec in (Environment.GetEnvironmentVariable("THUMB_TRY") ?? "45,50,30").Split(';'))
        {
            var a = spec.Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture) * D).ToArray();
            var angles = Enumerable.Range(0, HandPose.JointCount).Select(j => seiken[j]).ToArray();
            for (var k = 0; k < 3; k++)
            {
                angles[2 + (HandSkeleton.Thumb * 3) + k] = a[k];
            }

            var pose = new HandPose(angles);
            var target = HandSkeleton.Positions(pose);
            var state = new BareHandState().Placed(pose, out var grid);
            var sum = Vector3.Zero;
            var n = 0;
            var displaced = 0;
            var front = target.Max(p => p.X);
            var knuckles = Enumerable.Range(0, target.Length).Where(i => HandSkeleton.Parents[i].Finger < HandSkeleton.Thumb).Max(i => target[i].X);
            var thumbFront = Enumerable.Range(0, target.Length).Where(i => HandSkeleton.Parents[i].Finger == HandSkeleton.Thumb).Max(i => target[i].X);
            for (var i = 0; i < target.Length; i++)
            {
                if (HandSkeleton.Parents[i].Finger != HandSkeleton.Thumb)
                {
                    continue;
                }

                var (x, y, z) = grid.Coordinates(state.Placement[i]);
                var got = grid.CellCenter(x, y, z);
                sum += got;
                n++;
                displaced += Vector3.Distance(got, target[i]) > HandSkeleton.Cell * 0.9f ? 1 : 0;
            }

            var perFinger = new int[BareHandState.FingerCount + 1];
            for (var i = 0; i < target.Length; i++)
            {
                var parent = HandSkeleton.Parents[i];
                if (parent.Material != Voxels.BoxerAnatomy.HandBone)
                {
                    continue;
                }

                var (bx, by, bz) = grid.Coordinates(state.Placement[i]);
                perFinger[parent.Finger] += Vector3.Distance(grid.CellCenter(bx, by, bz), target[i]) > HandSkeleton.Cell * 0.9f ? 1 : 0;
            }

            Console.WriteLine($"{spec}: displaced bones per finger (0-3, thumb, palm) = {string.Join(",", perFinger)}");
            var c = sum / n * 1000f;
            Console.WriteLine($"{spec}: thumb centroid mm=({c.X:F1},{c.Y:F1},{c.Z:F1}) knuckles={knuckles * 1000f:F1} thumbFront={thumbFront * 1000f:F1} displaced={displaced}/{n}");
        }
    }
}
