using System.Globalization;
using System.Numerics;
using System.Text;
using MiniRing.Motion.Stick;

namespace MiniRing.Arena;

internal static class GalvaBvh
{
    public static string Write(GalvaRig rig, GalvaClip clip, int fps)
    {
        var frames = (int)MathF.Round(clip.Seconds * fps) + 1;
        var text = new StringBuilder("HIERARCHY\n");
        var order = new List<int>(rig.Bones.Count);
        WriteJoint(0, 0);
        text.Append(CultureInfo.InvariantCulture, $"MOTION\nFrames: {frames}\nFrame Time: {1f / fps:0.########}\n");
        var previous = new Vector3[rig.Bones.Count];
        for (var i = 0; i < frames; i++)
        {
            var pose = rig.Sample(clip, clip.Seconds * i / (frames - 1));
            WriteVector(pose[0].At);
            foreach (var index in order)
            {
                var parent = rig.Bones[index].Parent;
                var local = parent < 0 ? pose[index].Rotation : Quaternion.Conjugate(pose[parent].Rotation) * pose[index].Rotation;
                var angles = StickBvh.EulerZxyDegrees(Quaternion.Normalize(local));
                if (i > 0)
                {
                    var last = previous[index];
                    angles = last + new Vector3(MathF.IEEERemainder(angles.X - last.X, 360f),
                        MathF.IEEERemainder(angles.Y - last.Y, 360f), MathF.IEEERemainder(angles.Z - last.Z, 360f));
                }
                previous[index] = angles;
                WriteVector(new Vector3(angles.Z, angles.X, angles.Y));
            }
            text.Append('\n');
        }
        return text.ToString();

        void WriteJoint(int index, int depth)
        {
            var bone = rig.Bones[index];
            var indent = new string(' ', depth * 2);
            var root = bone.Parent < 0;
            order.Add(index);
            text.Append(CultureInfo.InvariantCulture, $"{indent}{(root ? "ROOT" : "JOINT")} {bone.Name}\n{indent}{{\n{indent}  OFFSET ");
            WriteVector(root ? Vector3.Zero : bone.Bind - rig.Bones[bone.Parent].Bind);
            text.Append(CultureInfo.InvariantCulture, $"\n{indent}  CHANNELS {(root ? "6 Xposition Yposition Zposition" : "3")} Zrotation Xrotation Yrotation\n");
            var children = Enumerable.Range(index + 1, rig.Bones.Count - index - 1).Where(i => rig.Bones[i].Parent == index).ToArray();
            if (children.Length == 0)
            {
                text.Append(CultureInfo.InvariantCulture, $"{indent}  End Site\n{indent}  {{\n{indent}    OFFSET ");
                WriteVector(bone.End - bone.Bind);
                text.Append(CultureInfo.InvariantCulture, $"\n{indent}  }}\n");
            }
            foreach (var child in children)
            {
                WriteJoint(child, depth + 1);
            }
            text.Append(CultureInfo.InvariantCulture, $"{indent}}}\n");
        }

        void WriteVector(Vector3 v) => text.Append(CultureInfo.InvariantCulture, $"{v.X:0.######} {v.Y:0.######} {v.Z:0.######} ");
    }
}
