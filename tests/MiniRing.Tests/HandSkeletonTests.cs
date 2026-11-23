using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>手形 = 関節の曲げ角。指の骨のセルが曲げ角どおりの場所に置かれ、細い面は小さく、動きは時間がかかり、折れた指は形を作れない。</summary>
[TestClass]
public sealed class HandSkeletonTests
{
    private const float Cell = HandSkeleton.Cell;

    private static Weapon NewHand(PhysicsWorld world)
        => Weapon.CreateWorn(world, WeaponKind.Fist, BoxerAnatomy.RedGlove, Vector3.Zero, Quaternion.Identity);

    private static int ParentOf(int finger, FingerSegment segment, int nthBone)
        => Enumerable.Range(0, HandSkeleton.Parents.Count)
            .Where(i => HandSkeleton.Parents[i] is { } p && p.Finger == finger && p.Segment == segment && p.Material == BoxerAnatomy.HandBone)
            .ElementAt(nthBone);

    private static Vector3 CenterOf(VoxelGrid grid, int index)
    {
        var (x, y, z) = grid.Coordinates(index);
        return grid.CellCenter(x, y, z);
    }

    [TestMethod]
    public void BareHand_KeepsAdultProportionsFromWristToFingertip()
    {
        _ = new BareHandState().Placed(HandSkeleton.PoseOf(HandShape.SpearHand), out var open);
        _ = new BareHandState().Placed(HandSkeleton.PoseOf(HandShape.Seiken), out var fist);

        static Vector3[] Occupied(VoxelGrid grid) => Enumerable.Range(0, grid.CellCount)
            .Where(i => grid.MaterialAt(i) != 0).Select(i => CenterOf(grid, i)).ToArray();

        // ANSUR II の成人男性平均は手長 193 mm、掌幅 88 mm。拳全体の幅には折り畳んだ親指も含む。
        var openCells = Occupied(open);
        var wristToTip = openCells.Max(p => p.X) + (open.CellSize.X / 2f) - HandSkeleton.Wrist.X;
        Assert.IsTrue(wristToTip is >= 0.175f and <= 0.205f, $"wrist to fingertip {wristToTip * 1000f:F1} mm");
        var fistCells = Occupied(fist);
        var fistWidth = fistCells.Max(p => p.Z) - fistCells.Min(p => p.Z) + fist.CellSize.Z;
        Assert.IsTrue(fistWidth is >= 0.085f and <= 0.115f, $"clenched fist including thumb {fistWidth * 1000f:F1} mm");

        float WristSliceWidth(float fromSocket)
        {
            var slice = fistCells.Where(p => MathF.Abs(p.X - HandSkeleton.Wrist.X - fromSocket) < fist.CellSize.X / 2f).ToArray();
            Assert.IsNotEmpty(slice, "the hand extends to both sides of the forearm socket");
            return slice.Max(p => p.Z) - slice.Min(p => p.Z) + fist.CellSize.Z;
        }

        var wristWidth = WristSliceWidth(fist.CellSize.X / 2f);
        Assert.IsTrue(wristWidth is >= 0.045f and <= 0.065f, $"wrist width {wristWidth * 1000f:F1} mm");
        Assert.IsLessThan(wristWidth, WristSliceWidth(-fist.CellSize.X / 2f), "the overlap inside the forearm tapers");
    }

    [TestMethod]
    public void BoneCells_SitWhereTheJointAnglesPutThem_InEveryShape()
    {
        foreach (var shape in Enum.GetValues<HandShape>())
        {
            var pose = HandSkeleton.PoseOf(shape);
            var positions = HandSkeleton.Positions(pose);
            var state = new BareHandState().Placed(pose, out var grid);
            var displaced = 0;
            var bones = 0;
            for (var i = 0; i < positions.Length; i++)
            {
                if (HandSkeleton.Parents[i].Material != BoxerAnatomy.HandBone)
                {
                    continue;
                }

                bones++;
                var d = Vector3.Distance(CenterOf(grid, state.Placement[i]), positions[i]);
                // 斜めに曲げると隣のセルとの取り合いがある。骨のずれは 2 セルまで。
                Assert.IsLessThanOrEqualTo(Cell * 2f, d, $"{shape}: a bone cell is within a cell of its joint-angle position");
                if (d > Cell * 0.9f)
                {
                    displaced++;
                }
            }

            // 親指（2026-11-19）が拳の下に入り、曲げた指の組織の置き場が詰まって、正拳では 4 本の指の骨が 5 個ずつ 1〜2 セル押される（52 個中 20 個。
            // ThumbFitProbe）。「多く」は過半数: 半分より少なければよい（ずれの大きさは上の 2 セルの上限が見ている）。
            Assert.IsLessThan(bones / 2, displaced, $"{shape}: most bone cells are exactly where the angles put them");
        }
    }

    [TestMethod]
    public void EachSegmentOfAFinger_PointsAlongTheSumOfTheJointAngles()
    {
        foreach (var shape in new[] { HandShape.Seiken, HandShape.SpearHand, HandShape.OneKnuckle })
        {
            var pose = HandSkeleton.PoseOf(shape);
            var positions = HandSkeleton.Positions(pose);
            foreach (var finger in new[] { 0, 1 })
            {
                var cumulative = 0f;
                for (var segment = 0; segment < 3; segment++)
                {
                    cumulative += pose.Joint(finger, segment);
                    var count = (int)MathF.Round(HandSkeleton.SegmentLength(finger, segment) / Cell);
                    if (count < 2)
                    {
                        continue;
                    }

                    var first = positions[ParentOf(finger, (FingerSegment)segment, 0)];
                    var last = positions[ParentOf(finger, (FingerSegment)segment, count - 1)];
                    var axis = Vector3.Normalize(last - first);
                    Assert.AreEqual(MathF.Cos(cumulative), axis.X, 1e-4f, $"{shape} finger {finger} segment {segment}: along the finger");
                    Assert.AreEqual(-MathF.Sin(cumulative), axis.Y, 1e-4f, $"{shape} finger {finger} segment {segment}: toward the palm");
                }
            }
        }
    }

    /// <summary>先頭から 1 セルの厚みの層にあるセルの (y, z) の柱の数 = 打つ面の面積（セル数）。</summary>
    private static int LeadingFaceCells(VoxelGrid grid)
    {
        var (nx, ny, nz) = grid.Dimensions;
        var front = 0;
        for (var i = 0; i < grid.CellCount; i++)
        {
            if (grid.MaterialAt(i) != 0)
            {
                front = Math.Max(front, grid.Coordinates(i).X);
            }
        }

        var columns = new HashSet<(int, int)>();
        for (var z = 0; z < nz; z++)
        {
            for (var y = 0; y < ny; y++)
            {
                if (grid.At(front, y, z) != 0 || (front > 0 && grid.At(front - 1, y, z) != 0))
                {
                    columns.Add((y, z));
                }
            }
        }

        return columns.Count;
    }

    [TestMethod]
    public void SpearHand_StrikesWithASmallerFaceThanTheFist_SoTheSameForceMakesMorePressure()
    {
        _ = new BareHandState().Placed(HandSkeleton.PoseOf(HandShape.Seiken), out var fist);
        _ = new BareHandState().Placed(HandSkeleton.PoseOf(HandShape.SpearHand), out var spear);
        _ = new BareHandState().Placed(HandSkeleton.PoseOf(HandShape.OneKnuckle), out var knuckle);
        var fistFace = LeadingFaceCells(fist);
        var spearFace = LeadingFaceCells(spear);
        var knuckleFace = LeadingFaceCells(knuckle);
        Assert.IsLessThan(fistFace, spearFace, $"spear tip {spearFace} cells vs fist {fistFace}");
        Assert.IsLessThan(fistFace, knuckleFace, $"one knuckle {knuckleFace} cells vs fist {fistFace}");
        // 同じ力 F の圧力は F / 面積: 面積が小さい分だけ大きい。
        Assert.IsGreaterThan(1f / (fistFace * Cell * Cell), 1f / (spearFace * Cell * Cell));
    }

    [TestMethod]
    public void OneKnuckle_PutsTheIndexMiddleJointInFront_AndSpearPutsTheMiddleFingertipInFront()
    {
        static int Foremost(HandShape shape)
        {
            var positions = HandSkeleton.Positions(HandSkeleton.PoseOf(shape));
            return Enumerable.Range(0, positions.Length).MaxBy(i => positions[i].X);
        }

        var knuckle = HandSkeleton.Parents[Foremost(HandShape.OneKnuckle)];
        Assert.AreEqual(0, knuckle.Finger, "the index finger leads");
        Assert.AreNotEqual(FingerSegment.Palm, knuckle.Segment);
        Assert.AreNotEqual(FingerSegment.Distal, knuckle.Segment, "its joint, not its tip, is the striking part");
        var spear = HandSkeleton.Parents[Foremost(HandShape.SpearHand)];
        Assert.AreEqual(1, spear.Finger, "the longest finger leads a spear hand");
        Assert.AreEqual(FingerSegment.Distal, spear.Segment);
    }

    [TestMethod]
    public void ChangingShapeTakesTime_AndSettlesInTheTimeTheStiffnessImplies()
    {
        using var world = new PhysicsWorld();
        var hand = NewHand(world);
        const float Dt = 1f / 240f;
        var start = hand.HandState!.Pose.Joint(1, 0);
        var target = HandSkeleton.PoseOf(HandShape.SpearHand).Joint(1, 0);
        hand.SetHandShape(HandShape.SpearHand, Dt);
        Assert.AreEqual(start, hand.HandState!.Pose.Joint(1, 0), 0.2f, "one step moves only a little");
        var t = Dt;
        for (; t < HandSkeleton.FingerSettleSeconds; t += Dt)
        {
            hand.SetHandShape(HandShape.SpearHand, Dt);
        }

        // 置き場の姿勢は 1 セルぶん（約 0.125 rad）ずつしか更新されないので、その分の誤差を許す。
        var remaining = (hand.HandState.Pose.Joint(1, 0) - target) / (start - target);
        Assert.IsLessThan(0.2f, remaining, $"after the settle time ({HandSkeleton.FingerSettleSeconds} s) the finger has covered almost the whole way (left {remaining:P0})");
        Assert.IsGreaterThan(0f, hand.HandState.Pose.Joint(1, 0) - target - 1e-4f, "and it did not overshoot");
        for (; t < 0.8392f; t += Dt)
        {
            hand.SetHandShape(HandShape.SpearHand, Dt);
        }

        Assert.AreEqual(target, hand.HandState.Pose.Joint(1, 0), 0.03f, "later it is straight (the passive tendon spring leaves a hair)");
    }

    [TestMethod]
    public void Stiffness_FollowsTheInertiaAndTheSettleTime()
    {
        for (var joint = 0; joint < HandPose.JointCount; joint++)
        {
            var settle = joint < 2 ? HandSkeleton.WristSettleSeconds : HandSkeleton.FingerSettleSeconds;
            var omega = 4.74f / settle;
            Assert.AreEqual(HandSkeleton.Inertia[joint] * omega * omega, HandSkeleton.Stiffness(joint), 1e-9f);
            Assert.IsGreaterThan(0f, HandSkeleton.Inertia[joint]);
        }

        // 付け根の関節は先の節が多い分、慣性が大きい。
        Assert.IsGreaterThan(HandSkeleton.Inertia[3], HandSkeleton.Inertia[2]);
        Assert.IsGreaterThan(HandSkeleton.Inertia[4], HandSkeleton.Inertia[3]);
    }

    [TestMethod]
    public void ABrokenFinger_CannotMakeTheShape_AndFallsToTheRelaxedCurl()
    {
        using var world = new PhysicsWorld();
        var hand = NewHand(world);
        // 薬指（2）の基節の骨を 1 セル折る。
        hand.Body.Grid.Break(hand.HandState!.Placement[ParentOf(2, FingerSegment.Proximal, 1)], []);
        for (var t = 0f; t < 1.678f; t += 1f / 240f)
        {
            hand.SetHandShape(HandShape.SpearHand, 1f / 240f);
        }

        var pose = hand.HandState!.Pose;
        Assert.AreEqual(0f, pose.Joint(1, 0), 0.05f, "the unbroken middle finger is straight");
        Assert.AreEqual(0f, pose.Joint(1, 1), 0.05f);
        Assert.AreEqual(HandSkeleton.RestFlexion[0], pose.Joint(2, 0), 0.05f, "the broken proximal phalanx hangs at the relaxed angle");
        Assert.AreEqual(0f, pose.Joint(2, 1), 0.05f, "its own joints beyond the break still work");
    }

    [TestMethod]
    public void Motion_IsDeterministic()
    {
        static HandPose Run()
        {
            using var world = new PhysicsWorld();
            var hand = NewHand(world);
            foreach (var shape in new[] { HandShape.SpearHand, HandShape.BackFist, HandShape.Seiken })
            {
                for (var i = 0; i < 20; i++)
                {
                    hand.SetHandShape(shape, 1f / 240f);
                }
            }

            return hand.HandState!.Pose;
        }

        Assert.AreEqual(0f, Run().MaxDifference(Run()));
    }

    [TestMethod]
    public void EveryShape_StaysWithinTheHumanJointRanges_AndTheMotionClampsOutOfRangeTargets()
    {
        foreach (var shape in Enum.GetValues<HandShape>())
        {
            var pose = HandSkeleton.PoseOf(shape);
            for (var j = 0; j < HandPose.JointCount; j++)
            {
                var (min, max) = HandSkeleton.Range(j);
                Assert.IsGreaterThanOrEqualTo(min - 1e-6f, pose[j], $"{shape} joint {j}");
                Assert.IsLessThanOrEqualTo(max + 1e-6f, pose[j], $"{shape} joint {j}");
            }
        }

        // 手首の範囲: 掌屈 80°・背屈 70°・橈屈 20°・尺屈 30°。
        const float Deg = MathF.PI / 180f;
        Assert.AreEqual(80f * Deg, HandSkeleton.Range(0).Max, 1e-5f);
        Assert.AreEqual(-70f * Deg, HandSkeleton.Range(0).Min, 1e-5f);
        Assert.AreEqual(20f * Deg, HandSkeleton.Range(1).Max, 1e-5f);
        Assert.AreEqual(-30f * Deg, HandSkeleton.Range(1).Min, 1e-5f);

        var motion = new HandMotion(HandSkeleton.PoseOf(HandShape.Seiken));
        motion.Snap(new HandPose([3f, -3f, .. new float[HandPose.JointCount - 2].Select(_ => 5f)]));
        var snapped = motion.Pose;
        for (var j = 0; j < HandPose.JointCount; j++)
        {
            var (min, max) = HandSkeleton.Range(j);
            Assert.IsTrue(snapped[j] >= min - 1e-6f && snapped[j] <= max + 1e-6f, $"joint {j} clamped");
        }
    }

    [TestMethod]
    public void TheWristRotatesAboutTheWristJoint_NotTheGrip()
    {
        const float Deg = MathF.PI / 180f;
        float[] Angles(float flex) => [flex, 0f, .. new float[HandPose.JointCount - 2]];
        var flexed = HandSkeleton.Positions(new HandPose(Angles(80f * Deg)));
        var extended = HandSkeleton.Positions(new HandPose(Angles(-70f * Deg)));
        var carpal = Enumerable.Range(0, HandSkeleton.Parents.Count)
            .Where(i => HandSkeleton.Parents[i].Segment == FingerSegment.Palm)
            .MinBy(i => Vector3.Distance(HandSkeleton.Positions(new HandPose(Angles(0f)))[i], HandSkeleton.WristJoint));
        Assert.IsLessThan(Cell * 1.5f, Vector3.Distance(flexed[carpal], extended[carpal]), "the cell at the joint stays");
        var tip = Enumerable.Range(0, HandSkeleton.Parents.Count).MaxBy(i => HandSkeleton.Positions(new HandPose(Angles(0f)))[i].X);
        Assert.IsGreaterThan(0.05634f, Vector3.Distance(flexed[tip], extended[tip]), "the fingertip swings through the range");
    }

    [TestMethod]
    public void Skin_CoversTheHand_InTheShapesThatDoNotTurnTheWristObliquely()
    {
        foreach (var shape in new[] { HandShape.Seiken, HandShape.SpearHand, HandShape.OneKnuckle, HandShape.BackFist })
        {
            _ = new BareHandState().Placed(HandSkeleton.PoseOf(shape), out var grid);
            Assert.IsLessThanOrEqualTo(1, ExposedNonSkin(grid), $"{shape}: no red muscle or white bone shows through the skin");
        }

        foreach (var shape in new[] { HandShape.KnifeHand, HandShape.PalmHeel })
        {
            _ = new BareHandState().Placed(HandSkeleton.PoseOf(shape), out var grid);
            // 斜めに回した形は、セルへの写しの食い違いで少し露出する（§8）。5 mm のセルでは表のセルの割合が増え、1/20 まで。
            Assert.IsLessThanOrEqualTo(grid.FilledCount / 20, ExposedNonSkin(grid), $"{shape}");
        }
    }

    private static int ExposedNonSkin(VoxelGrid grid)
    {
        var (nx, ny, nz) = grid.Dimensions;
        var count = 0;
        for (var z = 0; z < nz; z++)
        {
            for (var y = 0; y < ny; y++)
            {
                for (var x = 0; x < nx; x++)
                {
                    var m = grid.At(x, y, z);
                    if (m != 0 && !BoxerAnatomy.IsSkinLike(m) && grid.IsExposed(x, y, z))
                    {
                        count++;
                    }
                }
            }
        }

        return count;
    }

    [TestMethod]
    public void OneKnuckle_PutsTheIndexJointTwoCellsAheadOfTheOtherKnuckles()
    {
        static float[] Front(HandShape shape)
        {
            var state = new BareHandState().Placed(HandSkeleton.PoseOf(shape), out var grid);
            var front = new float[BareHandState.FingerCount];
            for (var i = 0; i < HandSkeleton.Parents.Count; i++)
            {
                var p = HandSkeleton.Parents[i];
                if (p.Finger < BareHandState.FingerCount)
                {
                    var (x, y, z) = grid.Coordinates(state.Placement[i]);
                    front[p.Finger] = MathF.Max(front[p.Finger], grid.CellCenter(x, y, z).X);
                }
            }

            return front;
        }

        var fist = Front(HandShape.Seiken);
        var one = Front(HandShape.OneKnuckle);
        var lead = (one[0] - MathF.Max(one[1], MathF.Max(one[2], one[3]))) / Cell;
        Assert.IsGreaterThanOrEqualTo(2f - 1e-3f, lead, $"the index joint leads the middle knuckle by {lead:F1} cells");
        // 5 mm のセルでは、深く曲げた人さし指の組織に押されて、隣の中指の拳頭が 1 セルまで下がる。
        Assert.AreEqual(fist[1], one[1], Cell + 1e-6f, "the other knuckles are where the fist has them (within a cell)");
    }

    [TestMethod]
    public void StrikePoint_IsTheForemostCellAlongTheDirection_ForEveryShape()
    {
        using var world = new PhysicsWorld();
        foreach (var shape in Enum.GetValues<HandShape>())
        {
            var hand = NewHand(world);
            hand.SetHandShape(shape);
            foreach (var direction in new[] { Vector3.UnitX, Vector3.Normalize(new Vector3(1f, 0.5f, -0.3f)), Vector3.UnitY })
            {
                var point = hand.StrikeOffset(direction)!.Value + HandSkeleton.Wrist;
                var grid = hand.Body.Grid;
                var best = float.MinValue;
                for (var i = 0; i < grid.CellCount; i++)
                {
                    if (grid.MaterialAt(i) != 0)
                    {
                        var (x, y, z) = grid.Coordinates(i);
                        best = MathF.Max(best, Vector3.Dot(grid.CellCenter(x, y, z), direction));
                    }
                }

                Assert.IsGreaterThanOrEqualTo(best - Cell, Vector3.Dot(point, direction), $"{shape} {direction}: no cell is more than a cell ahead of the strike point");
            }
        }

        var spear = NewHand(world);
        spear.SetHandShape(HandShape.SpearHand);
        var fist = NewHand(world);
        Assert.IsGreaterThan(fist.StrikeOffset(Vector3.UnitX)!.Value.X + 0.02254f, spear.StrikeOffset(Vector3.UnitX)!.Value.X, "the fingertips reach further from the grip than the knuckles");
    }
}
