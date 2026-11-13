using System.Numerics;
using System.Text.Json;

namespace MiniRing;

/// <summary>その時刻からの押下状態。省略した行動は離す。方向は相手基準。</summary>
public sealed record IntentCue(float At, float X = 0f, float Y = 0f, bool Light = false, bool Heavy = false, bool Evade = false)
{
    public FighterIntent Intent => new(new Vector2(X, Y), Light, Heavy, Evade);
}

/// <summary>壁時計を使わず試合の時刻で標本化するキーの台本。</summary>
public sealed class IntentScript
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };
    private readonly IntentCue[] _cues;

    public IntentScript(IEnumerable<IntentCue> cues)
    {
        ArgumentNullException.ThrowIfNull(cues);
        _cues = cues.ToArray();
        var previous = -1f;
        foreach (var cue in _cues)
        {
            if (cue is null || !float.IsFinite(cue.At) || cue.At < 0f || cue.At <= previous
                || !float.IsFinite(cue.X) || MathF.Abs(cue.X) > 1f || !float.IsFinite(cue.Y) || MathF.Abs(cue.Y) > 1f)
            {
                throw new ArgumentException("台本は時刻が昇順・非負、方向が -1..1 の状態列にしてください。", nameof(cues));
            }

            previous = cue.At;
        }
    }

    public FighterIntent At(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        var index = Array.FindLastIndex(_cues, cue => cue.At <= seconds);
        return index < 0 ? default : _cues[index].Intent;
    }

    public static IntentScript Load(string path)
        => new(JsonSerializer.Deserialize<IntentCue[]>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("台本には状態の配列が必要です。"));
}
