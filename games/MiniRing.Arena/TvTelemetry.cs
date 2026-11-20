using System.Numerics;
using System.Text.Json;
using Aigis.Physics.Voxels;
using MiniRing.Voxels;

namespace MiniRing.Arena;

/// <summary>
/// 体の細かい場所の呼び名（中継の言葉）。部位の箱の中の点（各軸 −1..1、+X が本人の左・+Y が上・+Z が前）から、
/// おでこ・目・頬・こめかみ・みぞおち…ぐらいの細かさで名前を付ける。手足は部位の名前（右前腕など）。
/// </summary>
internal static class TvBodyWords
{
    public static string Region(BodyPartKind kind, Vector3 n, string partName)
    {
        var side = n.X > 0f ? "左" : "右";
        switch (kind)
        {
            case BodyPartKind.Head:
                if (n.Y > 0.75f)
                {
                    return "頭のてっぺん";
                }

                if (n.Z < -0.35f)
                {
                    return "後頭部";
                }

                if (MathF.Abs(n.X) > 0.65f)
                {
                    return n.Y > 0.15f ? $"{side}のこめかみ" : n.Y > -0.35f ? $"{side}の耳" : $"{side}のあご";
                }

                return n.Y > 0.4f ? "おでこ"
                    : n.Y > 0.1f ? (MathF.Abs(n.X) > 0.25f ? $"{side}の目" : "眉間")
                    : n.Y > -0.3f ? (MathF.Abs(n.X) > 0.35f ? $"{side}の頬" : "鼻")
                    : n.Y > -0.6f ? (MathF.Abs(n.X) > 0.4f ? $"{side}の頬の下" : "口")
                    : "あご";
            case BodyPartKind.Chest:
                if (n.Z < -0.4f)
                {
                    return $"背中の{side}";
                }

                if (MathF.Abs(n.X) > 0.7f)
                {
                    return $"{side}のわき";
                }

                return n.Y > 0.35f ? (MathF.Abs(n.X) < 0.3f ? "鎖骨の下" : $"{side}の胸の上")
                    : n.Y > -0.25f ? (MathF.Abs(n.X) < 0.3f ? "胸の真ん中" : $"{side}の胸")
                    : MathF.Abs(n.X) < 0.35f ? "みぞおち"
                    : $"{side}の脇腹";
            case BodyPartKind.Pelvis:
                return n.Z < -0.4f ? "腰" : MathF.Abs(n.X) > 0.6f ? $"{side}の脇腹の下" : n.Y > 0f ? "へその上" : "下腹";
            default:
                return partName;
        }
    }

    public static string Material(byte m) => m switch
    {
        BoxerAnatomy.Skin => "皮膚",
        BoxerAnatomy.Muscle => "筋肉",
        BoxerAnatomy.Bone => "骨",
        BoxerAnatomy.Brain => "脳",
        BoxerAnatomy.Heart => "心臓",
        BoxerAnatomy.Lung => "肺",
        BoxerAnatomy.Liver => "肝臓",
        BoxerAnatomy.Gut => "腸",
        BoxerAnatomy.Eye or BoxerAnatomy.Pupil => "目",
        BoxerAnatomy.Hair => "髪",
        BoxerAnatomy.RedTrunks or BoxerAnatomy.BlueTrunks or BoxerAnatomy.Waistband => "トランクス",
        BoxerAnatomy.Boot => "シューズ",
        BoxerAnatomy.RedGlove or BoxerAnatomy.BlueGlove or BoxerAnatomy.RedGloveThumb or BoxerAnatomy.BlueGloveThumb or BoxerAnatomy.GloveTrim or BoxerAnatomy.Padding => "グローブ",
        BoxerAnatomy.Iron => "鉄",
        BoxerAnatomy.Wood => "木",
        _ => "装備",
    };
}

/// <summary>
/// テレビ中継の生の記録（<c>*.telemetry.json</c>、描画・音と同じく試合には触れない。読むだけ）: 実況・解説の LLM に渡す、言葉で
/// 情景を思い描けるくらいの細かい情報（2026-11-17、ユーザーの指定）。
/// <list type="bullet">
/// <item>0.168 秒（試合の時間）ごとの姿勢: 頭・両拳の位置（自分の腰から、相手の方を前にして、前・左・床からの高さ）、腰の高さ、前傾・横の傾き、
/// 相手への向き、相手との距離、スタミナ・意識・ダウン、いまの技と守り。長さは選手の身長に合わせた人の大きさの cm。</item>
/// <item>当たりごと: 打った側・技・手足・打たれた場所（細かい呼び名）・急所・速さ・運動量・エネルギー・ダメージ、その歩で削れたセル
/// （場所 × 材質）。</item>
/// <item>骨折・切断。積み上げた損傷（場所 × 材質の失ったセル）と臓器の打撲は、当たりの記録に打たれた側の今の値として付ける。</item>
/// </list>
/// </summary>
internal sealed class TvTelemetry
{
    private const float SampleSeconds = 0.168f;

    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly string _redName;
    private readonly string _blueName;
    private readonly int _redHeightCm;
    private readonly int _blueHeightCm;
    private readonly List<object> _samples = [];
    private readonly List<object> _hits = [];
    private readonly List<object> _breaks = [];
    private readonly Dictionary<VoxelBody, (int Version, byte[] Cells)> _snapshots = [];
    private readonly Dictionary<Fighter, Dictionary<string, int>> _lost = [];
    private readonly Dictionary<Fighter, float> _scale = [];
    private float _canvas = float.NaN;
    private float _nextSample;

    public TvTelemetry(string redName, string blueName, int redHeightCm, int blueHeightCm)
    {
        _redName = redName;
        _blueName = blueName;
        _redHeightCm = redHeightCm > 0 ? redHeightCm : 175;
        _blueHeightCm = blueHeightCm > 0 ? blueHeightCm : 175;
    }

    private static float R(float v, int digits = 1) => MathF.Round(v, digits);

    /// <summary>1 歩ごとに見る。<paramref name="at"/> は映像の秒（出来事の記録と同じ時計）。</summary>
    public void Observe(BoxingGame game, float matchTime, float at, (string? Technique, string? Defense) red, (string? Technique, string? Defense) blue)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (float.IsNaN(_canvas))
        {
            _canvas = MathF.Min(Floor(game.Player), Floor(game.Cpu));
            foreach (var (f, cm) in new[] { (game.Player, _redHeightCm), (game.Cpu, _blueHeightCm) })
            {
                // 頭の中心の高さは身長の約 93 %。
                _scale[f] = cm / MathF.Max(1e-3f, (f.HeadPosition.Y - _canvas) / 0.93f);
            }
        }

        var cut = Carved(game);
        foreach (var hit in game.RecentHits)
        {
            var limb = hit.Elbow ? $"{(hit.Hand == Hand.Left ? "左" : "右")}肘" : hit.Kick ? $"{(hit.Hand == Hand.Left ? "左" : "右")}{KickName(hit.KickerPart)}" : hit.Hand == Hand.Left ? "左拳" : "右拳";
            AddHit(game, at, hit.Attacker, hit.Victim, limb, hit.Point, hit.Zone, hit.Speed, hit.Momentum, hit.Energy, hit.Damage, cut, hit.Attacker == game.Player ? red : blue);
        }

        foreach (var hit in game.WeaponHits)
        {
            AddHit(game, at, hit.Attacker, hit.Victim, WeaponPart(game, hit.Attacker, hit.Point), hit.Point, hit.Zone, hit.Speed, hit.Momentum, 0.5f * hit.Momentum * hit.Speed, hit.Damage, cut, hit.Attacker == game.Player ? red : blue); // エネルギーは見積もり（運動量 × 速さ ÷ 2）
        }

        foreach (var broken in game.BrokenBones)
        {
            _breaks.Add(new { t = R(at, 2), kind = "骨折", who = Name(game, broken.Victim), part = broken.Body >= 0 ? MiniRing.Audio.Broadcast.PartName(broken.Victim, broken.Body) : "" });
        }

        foreach (var sever in game.RecentSevers)
        {
            var victim = sever.Victim ?? (Vector3.DistanceSquared(sever.Position, game.Player.PelvisPosition) < Vector3.DistanceSquared(sever.Position, game.Cpu.PelvisPosition) ? game.Player : game.Cpu);
            _breaks.Add(new { t = R(at, 2), kind = "切断", who = Name(game, victim), part = sever.Body >= 0 ? MiniRing.Audio.Broadcast.PartName(victim, sever.Body) : "" });
        }

        if (matchTime + 1e-4f >= _nextSample)
        {
            _nextSample = matchTime + SampleSeconds;
            _samples.Add(new { t = R(at, 2), red = Pose(game, game.Player, game.Cpu, red), blue = Pose(game, game.Cpu, game.Player, blue) });
        }
    }

    /// <summary>
    /// 武器の当たりの「何のどこで」（例: 刀の切っ先・棒の端・こんぼうの頭・右の籠手）。当たった点を、打った選手の握った物・はめた物のうち
    /// いちばん近い物の長さに沿った位置で言葉にする（中継の言葉。どこで当たったかは生の情報で、強さの形容は付けない）。
    /// </summary>
    private static string WeaponPart(BoxingGame game, Fighter attacker, Vector3 point)
    {
        var candidates = new[] { game.WeaponOf(attacker), game.GloveOf(attacker, Hand.Left), game.GloveOf(attacker, Hand.Right) }
            .OfType<MiniRing.Weapons.Weapon>().Distinct().ToList();
        if (candidates.Count == 0)
        {
            return "武器";
        }

        var weapon = candidates.MinBy(w => Vector3.DistanceSquared(game.World.GetState(w.Body.BodyId).Position, point))!;
        var tip = MiniRing.Weapons.Weapon.TipOf(weapon.Kind).X;
        var u = MathF.Abs(tip) < 1e-4f ? 0f : weapon.Body.WorldToGrid(point).X / tip; // 先 = 1、真ん中 = 0、後ろの端 = −1
        var side = game.GloveOf(attacker, Hand.Left) == weapon ? "左の" : game.GloveOf(attacker, Hand.Right) == weapon ? "右の" : "";
        return weapon.Kind switch
        {
            MiniRing.Weapons.WeaponKind.Katana or MiniRing.Weapons.WeaponKind.Longsword or MiniRing.Weapons.WeaponKind.IronSword =>
                $"{(weapon.Kind == MiniRing.Weapons.WeaponKind.Katana ? "刀" : weapon.Kind == MiniRing.Weapons.WeaponKind.Longsword ? "ロングソード" : "剣")}の{(u > 0.75f ? "切っ先" : u > 0.35f ? "刃の中ほど" : u > 0f ? "刃の根元" : "柄")}",
            MiniRing.Weapons.WeaponKind.WoodenClub => u > 0.5f ? "こんぼうの頭" : u > -0.3f ? "こんぼうの中ほど" : "こんぼうの柄",
            MiniRing.Weapons.WeaponKind.Staff or MiniRing.Weapons.WeaponKind.Jo =>
                $"{(weapon.Kind == MiniRing.Weapons.WeaponKind.Staff ? "棒" : "杖")}の{(u > 0.6f ? "先の端" : u < -0.6f ? "後ろの端" : "中ほど")}",
            MiniRing.Weapons.WeaponKind.Tonfa => $"{side}トンファー",
            MiniRing.Weapons.WeaponKind.Katar => $"{side}カタールの刃",
            MiniRing.Weapons.WeaponKind.KnuckleDuster => $"{side}ナックル",
            MiniRing.Weapons.WeaponKind.Gauntlet => $"{side}籠手",
            MiniRing.Weapons.WeaponKind.Shield => "盾",
            MiniRing.Weapons.WeaponKind.Glove => $"{side}グローブ",
            _ => $"{side}拳",
        };
    }

    private static string KickName(KickPart part) => part switch
    {
        KickPart.Thigh => "太もも",
        KickPart.Shin => "すね",
        KickPart.Toe => "つま先",
        _ => "足",
    };

    private string Name(BoxingGame game, Fighter f) => f == game.Player ? _redName : _blueName;

    private static float Floor(Fighter f) => MathF.Min(f.AnklePosition(Hand.Left).Y, f.AnklePosition(Hand.Right).Y);

    /// <summary>この歩で削れた・失ったセル（打たれた選手ごと、場所 × 材質）。積み上げにも足す。</summary>
    private Dictionary<Fighter, Dictionary<string, int>> Carved(BoxingGame game)
    {
        var result = new Dictionary<Fighter, Dictionary<string, int>>();
        foreach (var f in new[] { game.Player, game.Cpu })
        {
            if (f.Voxels is not { } voxels)
            {
                continue;
            }

            foreach (var part in voxels.Layers) // 顔の層（頭皮・目・髪）も
            {
                var grid = part.Body.Grid;
                if (_snapshots.TryGetValue(part.Body, out var snap) && snap.Version == grid.Version && snap.Cells.Length == grid.CellCount)
                {
                    continue;
                }

                var cells = new byte[grid.CellCount];
                for (var i = 0; i < cells.Length; i++)
                {
                    cells[i] = grid.AtIndex(i);
                }

                if (snap.Cells is { } old && old.Length == cells.Length)
                {
                    var (dx, dy, _) = grid.Dimensions;
                    var partName = MiniRing.Audio.Broadcast.PartName(f, part.Body.BodyId);
                    for (var i = 0; i < cells.Length; i++)
                    {
                        if (old[i] == 0 || cells[i] != 0)
                        {
                            continue;
                        }

                        var center = grid.CellCenter(i % dx, i / dx % dy, i / (dx * dy));
                        var region = TvBodyWords.Region(part.Kind, center / grid.HalfExtents, partName);
                        var key = $"{region}の{TvBodyWords.Material(old[i])}";
                        var step = result.TryGetValue(f, out var d) ? d : result[f] = [];
                        step[key] = step.GetValueOrDefault(key) + 1;
                        var total = _lost.TryGetValue(f, out var l) ? l : _lost[f] = [];
                        total[key] = total.GetValueOrDefault(key) + 1;
                    }
                }

                _snapshots[part.Body] = (grid.Version, cells);
            }
        }

        return result;
    }

    private void AddHit(BoxingGame game, float at, Fighter attacker, Fighter victim, string limb, Vector3 point, HitZone zone, float speed, float momentum, float energy, float damage,
        Dictionary<Fighter, Dictionary<string, int>> cut, (string? Technique, string? Defense) caption)
    {
        var region = "";
        if (victim.Voxels is { } voxels && voxels.Parts.Count > 0)
        {
            static float Outside(VoxelBody body, Vector3 world)
            {
                var local = Vector3.Abs(body.WorldToGrid(world)) - body.Grid.HalfExtents;
                return Vector3.Max(local, Vector3.Zero).LengthSquared();
            }

            var part = voxels.Parts.MinBy(p => Outside(p.Body, point))!;
            var n = Vector3.Clamp(part.Body.WorldToGrid(point) / part.Body.Grid.HalfExtents, -Vector3.One, Vector3.One);
            region = TvBodyWords.Region(part.Kind, n, MiniRing.Audio.Broadcast.PartName(victim, part.Body.BodyId));
        }

        var scale = _scale.GetValueOrDefault(attacker, 1f);
        _hits.Add(new
        {
            t = R(at, 2),
            attacker = Name(game, attacker),
            technique = caption.Technique,
            limb,
            victim = Name(game, victim),
            region,
            zone = MiniRing.Audio.Broadcast.ZoneName(zone),
            speed = R(speed * scale / 100f, 2), // 人の大きさに直した速さ [m/s]
            momentum = R(momentum, 3),
            energy = float.IsNaN(energy) ? (float?)null : R(energy, 2),
            damage = R(damage),
            carved = cut.GetValueOrDefault(victim),
            victimAfter = new
            {
                stamina = R(victim.Stamina * 100f, 0),
                consciousness = R(victim.Consciousness * 100f, 0),
                lostTotal = _lost.GetValueOrDefault(victim)?.OrderByDescending(p => p.Value).Take(8).ToDictionary(p => p.Key, p => p.Value),
                bruise = victim.Voxels is { } v ? BoxerBody.TrunkOrgans.Where(o => v.BruiseOf(o) > 0.05f).ToDictionary(o => TvBodyWords.Material(o), o => R(v.BruiseOf(o) * 100f, 0)) : null,
            },
        });
    }

    /// <summary>姿勢: 自分の腰を原点に、相手の方を前・左・床からの高さ（人の大きさの cm）。</summary>
    private object Pose(BoxingGame game, Fighter f, Fighter opponent, (string? Technique, string? Defense) caption)
    {
        var s = _scale.GetValueOrDefault(f, 1f);
        var pelvis = f.PelvisPosition;
        var toward = opponent.ChestPosition - f.ChestPosition;
        toward.Y = 0f;
        var forward = toward.LengthSquared() > 1e-8f ? Vector3.Normalize(toward) : Vector3.UnitZ;
        var left = Vector3.Cross(Vector3.UnitY, forward);
        float[] At(Vector3 p) => [R((Vector3.Dot(p - pelvis, forward)) * s, 0), R((Vector3.Dot(p - pelvis, left)) * s, 0), R((p.Y - _canvas) * s, 0)];
        var torsoUp = Vector3.Transform(Vector3.UnitY, f.TorsoOrientation);
        var torsoFront = Vector3.Transform(Vector3.UnitZ, f.TorsoOrientation);
        var facing = MathF.Atan2(Vector3.Dot(torsoFront, left), Vector3.Dot(torsoFront, forward)) * 180f / MathF.PI;
        return new
        {
            head = At(f.HeadPosition),
            leftFist = At(f.GloveCenter(Hand.Left)),
            rightFist = At(f.GloveCenter(Hand.Right)),
            pelvisHeight = R((pelvis.Y - _canvas) * s, 0),
            leanForward = R(MathF.Asin(Math.Clamp(Vector3.Dot(torsoUp, forward), -1f, 1f)) * 180f / MathF.PI, 0),
            leanLeft = R(MathF.Asin(Math.Clamp(Vector3.Dot(torsoUp, left), -1f, 1f)) * 180f / MathF.PI, 0),
            facing = R(facing, 0), // 胸の向きと相手の方向のずれ [度]（+ は左を向いている）
            distance = R(Vector3.Distance(new Vector3(f.ChestPosition.X, 0f, f.ChestPosition.Z), new Vector3(opponent.ChestPosition.X, 0f, opponent.ChestPosition.Z)) * s, 0),
            stamina = R(f.Stamina * 100f, 0),
            consciousness = R(f.Consciousness * 100f, 0),
            down = f.IsKnockedDown,
            technique = caption.Technique,
            defense = caption.Defense,
        };
    }

    public void Write(string path)
    {
        var json = JsonSerializer.Serialize(new
        {
            units = "長さは選手の身長に合わせた人の大きさの cm（前 = 相手の方、左 = 本人の左、高さ = マットから）。speed は人の大きさの m/s。momentum・energy は試合の中の値。",
            red = _redName,
            blue = _blueName,
            samples = _samples,
            hits = _hits,
            breaks = _breaks,
        }, JsonOptions);
        File.WriteAllText(path, json);
    }
}
