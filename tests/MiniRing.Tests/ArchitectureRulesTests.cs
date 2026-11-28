using System.Text.RegularExpressions;

namespace MiniRing.Tests;

/// <summary>
/// CLAUDE.md の軸のうち、ソースの書き方で破られうるものを機械で確かめる（どのエージェントが書いても同じ所で止まるように）。
/// <list type="bullet">
/// <item>§1.6 決定的であること: 試合を計算するコード（games/MiniRing・src/Aigis.Physics・src/Aigis.Foundation）に、種なしの乱数・時計を書かない。
/// 試合の外で使う所（記録のファイル名・見出しの日時など）は、その行かすぐ上の行に <c>// determinism-ok: 理由</c> を書く。</item>
/// <item>§3 Bepu の型は Aigis.Physics の外へ出さない: ほかのプロジェクトのソースが BepuPhysics・BepuUtilities を参照しない。</item>
/// </list>
/// </summary>
[TestClass]
[TestCategory("L0")]
public sealed class ArchitectureRulesTests
{
    private const string DeterminismMarker = "determinism-ok:";

    private static readonly (Regex Pattern, string Why)[] Nondeterministic =
    [
        (new Regex(@"new\s+(System\.)?Random\s*\(\s*\)"), "種なしの Random（種を渡す: new Random(seed)）"),
        (new Regex(@"\bRandom\.Shared\b"), "共有の Random（種が無い）"),
        (new Regex(@"\bDateTime(Offset)?\.(Now|UtcNow|Today)\b"), "時計"),
        (new Regex(@"\bEnvironment\.TickCount(64)?\b"), "時計"),
        (new Regex(@"\bGuid\.NewGuid\s*\("), "毎回違う値"),
    ];

    private static readonly Regex BepuReference = new(@"^\s*(global\s+)?using\s+(static\s+)?Bepu|\bglobal::Bepu|\bBepu(Physics|Utilities)\.[A-Z]");

    [TestMethod]
    public void MatchCode_HasNoClockOrUnseededRandom_ExceptMarkedLines()
    {
        var root = RepoRoot();
        var violations = new List<string>();
        foreach (var dir in new[] { "games/MiniRing", "src/Aigis.Physics", "src/Aigis.Foundation" })
        {
            foreach (var (path, lines) in Sources(Path.Combine(root, dir)))
            {
                for (var i = 0; i < lines.Length; i++)
                {
                    var code = StripComment(lines[i]);
                    foreach (var (pattern, why) in Nondeterministic)
                    {
                        if (pattern.IsMatch(code) && !lines[i].Contains(DeterminismMarker) && (i == 0 || !lines[i - 1].Contains(DeterminismMarker)))
                        {
                            violations.Add($"{Path.GetRelativePath(root, path)}:{i + 1}: {why}: {lines[i].Trim()}");
                        }
                    }
                }
            }
        }

        Assert.AreEqual(0, violations.Count,
            "試合の計算に時計・種なしの乱数を入れない（CLAUDE.md §1.6）。試合の外の用途なら // " + DeterminismMarker + " 理由 を書く:\n" + string.Join("\n", violations));
    }

    [TestMethod]
    public void OnlyAigisPhysics_ReferencesBepu()
    {
        var root = RepoRoot();
        var violations = new List<string>();
        foreach (var dir in new[] { "games", "src", "tests" })
        {
            foreach (var (path, lines) in Sources(Path.Combine(root, dir)))
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (relative.StartsWith("src/Aigis.Physics/", StringComparison.Ordinal))
                {
                    continue;
                }

                for (var i = 0; i < lines.Length; i++)
                {
                    if (BepuReference.IsMatch(StripComment(lines[i])))
                    {
                        violations.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }
        }

        Assert.AreEqual(0, violations.Count, "Bepu の型は Aigis.Physics の外へ出さない（CLAUDE.md §3）:\n" + string.Join("\n", violations));
    }

    // ── 依存の向き（物理の憲法 §2.1・地図 V14、2026-11-28）: 下の層は上の層の型・名前を知らない。今守れている所は 0 件で固め、
    // 破っている所は「既知」の一覧に書く（直したら一覧から消す。一覧の外が増えたら落ちる。一覧の物が直ったのに残っていても落ちる）。

    /// <summary>L0（数値の土台・解き手）と L1 の局所の式が人体の部位の名前を持つファイル。V3（HumanoidRagdoll が Aigis.Physics にある）。体のグラフの段で消える。</summary>
    private static readonly HashSet<string> KnownEngineBodyNames =
    [
        "src/Aigis.Physics/HumanoidRagdoll.cs", "src/Aigis.Physics/HumanoidRagdoll.Joints.cs", "src/Aigis.Physics/HumanoidRagdoll.Pose.cs",
        "src/Aigis.Physics/Ragdoll.cs", "src/Aigis.Foundation/Voxels/StepProfile.cs", // StepProfile: 計測の欄の名前（Elbows）だけ
    ];

    private static readonly Regex BodyPartName = new(@"\b(Humanoid|Elbow|Knee|Pelvis|Shoulder|Forearm)");

    [TestMethod]
    public void TheEngine_DoesNotKnowTheHumanBody_ExceptTheKnownFiles()
    {
        var found = FilesMatching(["src/Aigis.Physics", "src/Aigis.Foundation"], BodyPartName);
        AssertKnown(found, KnownEngineBodyNames, "L0/L1（Aigis）が人体の部位の名前を持つ（物理の憲法 §1 L0「持たない」・DEBT「L0 が人体を知る」）");
    }

    /// <summary>L1 の材料の表（games/MiniRing/Materials）は体・試合・判断・キャラを知らない。</summary>
    [TestMethod]
    public void TheMaterialTable_KnowsNothingAboveIt()
    {
        var above = new Regex(@"\b(Fighter|BoxingGame|BoxerAnatomy|BoxerBody|MatchRules|Organization|CharacterFile|BotStyle|BoxerBot)\b|MiniRing\.(Bots|Characters|Voxels)");
        AssertKnown(FilesMatching(["games/MiniRing/Materials"], above), [], "L1 の材料の表が上の層を参照した");
    }

    /// <summary>
    /// L5（ボット: games/MiniRing/Bots）は剛体・ボクセル・体の真の状態（<c>Fighter</c>）・試合（<c>BoxingGame</c>）を直接読まない（感覚の出口 <c>FighterSense</c> だけ）。
    /// 既知: 試合の定数（どちらの手が武器の手か）を読む所と、ドリルの台（L6 の道具がボットの棚にある）。
    /// </summary>
    private static readonly HashSet<string> KnownBotReachesDown =
    [
        "games/MiniRing/Bots/GroundStrike.cs", // BoxingGame.WeaponHand（試合の決まりの定数）
        "games/MiniRing/Bots/BotArena.cs", "games/MiniRing/Bots/DefenseDrill.cs", // 試合を組むドリルの台（L6 の道具。Arena へ移せば消える）
    ];

    [TestMethod]
    public void Bots_ReadOnlyTheirSense_ExceptTheKnownFiles()
    {
        var below = new Regex(@"\b(PhysicsWorld|VoxelBody|VoxelGrid|BoxingGame)\b|\bFighter\b(?=\s*[.\s,)>\]])");
        AssertKnown(FilesMatching(["games/MiniRing/Bots"], below), KnownBotReachesDown, "L5 が感覚の出口の外（剛体・ボクセル・体・試合）を読んだ");
    }

    /// <summary>L6（試合・団体）が体の状態を直接書く所（V5: 休みの再生を団体の値で直接呼ぶ）。L3a の治る式へ環境の値を渡す口にする段で消える。</summary>
    private static readonly HashSet<string> KnownMatchWritesBody =
    [
        "games/MiniRing/BoxingGame.cs",
    ];

    [TestMethod]
    public void TheMatch_DoesNotHealTheBodyDirectly_ExceptTheKnownFiles()
    {
        var heal = new Regex(@"\.Regenerate\s*\(");
        var found = FilesMatching(["games/MiniRing"], heal).Where(f => !f.StartsWith("games/MiniRing/Voxels/", StringComparison.Ordinal)).ToHashSet();
        AssertKnown(found, KnownMatchWritesBody, "L6 が体（ボクセル）を直接治した（物理の憲法 §2.2: L6 → 下は環境の値と時間だけ）");
    }

    /// <summary>
    /// 感覚の出口（L3c → L5）の欄の分類（V7）。<c>FighterSense</c> の欄が増えたら、どれに当たるかをここに書く（分類の無い欄が足されたら落ちる）。
    /// <list type="bullet">
    /// <item>Own: 自分の体の感覚（関節・疲れ・傷み・自分の位置）。</item>
    /// <item>Seen: 相手について目で見て知る値。今は真の値をそのまま渡す（V7）。感覚の段（ユーザーの決定 B）で、位置・手・刃の位置を目の残りに応じて遅らせ・ぶらす欄は SeenDelayed。</item>
    /// <item>Match: 試合・場の情報（決まり・リング・時刻）。</item>
    /// </list>
    /// </summary>
    private static readonly Dictionary<string, string> SenseFields = new()
    {
        ["Self"] = "Own", ["ArmReach"] = "Own", ["SelfHead"] = "Own", ["SelfFaintness"] = "Own", ["SelfArmed"] = "Own", ["SelfWeapon"] = "Own",
        ["SelfStamina"] = "Own", ["SelfVision"] = "Own", ["SelfBuild"] = "Own", ["SelfLegLeft"] = "Own", ["SelfLegRight"] = "Own", ["SelfInjury"] = "Own",
        ["SelfSupplies"] = "Own", ["Grapple"] = "Own", ["SelfDowned"] = "Own", ["SelfLegOxygenLeft"] = "Own", ["SelfLegOxygenRight"] = "Own",
        ["SelfBody"] = "Own", ["SelfChin"] = "Own", ["SelfBladeArmLeft"] = "Own", ["SelfBladeArmRight"] = "Own", ["SelfBladeGripLeft"] = "Own", ["SelfBladeGripRight"] = "Own",
        ["OpponentHead"] = "SeenDelayed", ["OpponentChest"] = "SeenDelayed", ["OpponentLeft"] = "SeenDelayed", ["OpponentRight"] = "SeenDelayed",
        ["OpponentVelocity"] = "SeenDelayed", ["OpponentBody"] = "SeenDelayed", ["OpponentBlade"] = "SeenDelayed",
        ["OpponentAlive"] = "Seen", ["OpponentTargets"] = "Seen", ["OpponentVision"] = "Seen", ["OpponentArmReach"] = "Seen", ["OpponentDowned"] = "Seen",
        ["OpponentBuild"] = "Seen", ["OpponentInjury"] = "Seen", ["OpponentWinded"] = "Seen",
        ["Ring"] = "Match", ["MayStrikeDowned"] = "Match", ["Match"] = "Match",
    };

    [TestMethod]
    public void EverySenseField_IsClassified()
    {
        var fields = typeof(FighterSense).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First().GetParameters().Select(p => p.Name!).ToList();
        var unclassified = fields.Where(f => !SenseFields.ContainsKey(f)).ToList();
        var gone = SenseFields.Keys.Except(fields).ToList();
        Assert.IsEmpty(unclassified, "FighterSense に分類の無い欄（ArchitectureRulesTests.SenseFields に Own / Seen / SeenDelayed / Match を書く）: " + string.Join(", ", unclassified));
        Assert.IsEmpty(gone, "SenseFields に無くなった欄が残っている: " + string.Join(", ", gone));
    }

    private static HashSet<string> FilesMatching(string[] dirs, Regex pattern)
    {
        var root = RepoRoot();
        var found = new HashSet<string>();
        foreach (var dir in dirs)
        {
            var full = Path.Combine(root, dir);
            if (!Directory.Exists(full))
            {
                continue;
            }

            foreach (var (path, lines) in Sources(full))
            {
                if (lines.Any(l => pattern.IsMatch(StripComment(l))))
                {
                    found.Add(Path.GetRelativePath(root, path).Replace('\\', '/'));
                }
            }
        }

        return found;
    }

    private static void AssertKnown(HashSet<string> found, HashSet<string> known, string what)
    {
        var added = found.Except(known).Order().ToList();
        var fixedOnes = known.Except(found).Order().ToList();
        Assert.IsEmpty(added, what + "（新しく破った所）:\n" + string.Join("\n", added));
        Assert.IsEmpty(fixedOnes, "直った所が既知の一覧に残っている（一覧から消す）:\n" + string.Join("\n", fixedOnes));
    }

    private static string StripComment(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('*'))
        {
            return string.Empty;
        }

        var at = line.IndexOf("//", StringComparison.Ordinal);
        return at >= 0 ? line[..at] : line;
    }

    private static IEnumerable<(string Path, string[] Lines)> Sources(string dir)
    {
        foreach (var path in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            var sep = Path.DirectorySeparatorChar;
            if (path.Contains($"{sep}bin{sep}", StringComparison.Ordinal) || path.Contains($"{sep}obj{sep}", StringComparison.Ordinal))
            {
                continue;
            }

            yield return (path, File.ReadAllLines(path));
        }
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")) && Directory.Exists(Path.Combine(dir.FullName, "games")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("リポジトリの根（CLAUDE.md と games/ がある所）が見つからない。");
    }
}
