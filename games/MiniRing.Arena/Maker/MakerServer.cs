using System.Diagnostics;
using System.Net;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aigis.Foundation;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Voxels;

namespace MiniRing.Arena.Maker;

/// <summary>
/// キャラメイクの画面（<c>miniring-arena maker</c>）: この PC の中だけで動く小さな HTTP の口（<c>http://localhost:&lt;port&gt;/</c>）と、1 枚の HTML（<c>maker.html</c>）。
/// <para>
/// 画面を Web にした理由: 名前の入力・写真のファイルの選択・目印の点のドラッグ・スライダーは、ブラウザがそのまま持っている（ゲームの UI は図形だけで文字の入力が無い）。
/// プレビューの絵はゲームの物で、試合の頭（<see cref="FaceSheet.HeadViews"/>: 頭の格子と顔の層）と全身（<see cref="CharacterPortraits"/>: 試合の体と同じ格子）を
/// CPU で写す（描き起こさない）。保存する前に名簿の読み込み（<see cref="CharacterMaker.ToFile"/>）を通すので、保存したファイルはそのまま試合に使える。
/// </para>
/// <para>
/// 写真: 顔の目印はブラウザの中で検出する（MediaPipe、Apache-2.0。ライブラリとモデルは CDN から読むが、写真はどこへも送らない）か、手で置く。
/// 画素と点はこの口（localhost）にだけ送られ、<see cref="PhotoFace"/> が顔の値にする。写真はメモリの中だけで扱い、ファイルに書かない。
/// </para>
/// </summary>
internal sealed class MakerServer
{
    private const long MaxRequestBytes = 24L * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string? _rosterDir;
    private readonly string _saveDir;
    private readonly int _port;

    public MakerServer(string? rosterDir, string saveDir, int port)
    {
        _rosterDir = rosterDir;
        _saveDir = saveDir;
        _port = port;
    }

    /// <summary>コマンド: 口を開いて、止めるまで答える。</summary>
    public static int Run(int port, string? saveDir, bool open)
    {
        var rosterDir = FindRosterDir();
        var dir = saveDir is not null ? Path.GetFullPath(saveDir) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniRing", "characters");
        var server = new MakerServer(rosterDir, dir, port);
        using var listener = new HttpListener();
        var url = $"http://localhost:{port}/";
        listener.Prefixes.Add(url);
        listener.Start();
        Console.WriteLine($"キャラメイク: {url} （止めるには Ctrl+C）");
        Console.WriteLine($"  保存先: 名簿 {(rosterDir ?? "（見つからない: リポジトリの根で動かすと使える）")} / ファイル {dir}");
        if (open)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                Console.Error.WriteLine($"ブラウザを開けませんでした（{ex.Message}）。{url} を開いてください。");
            }
        }

        while (listener.IsListening)
        {
            var context = listener.GetContext();
            server.Handle(context);
        }

        return 0;
    }

    /// <summary>リポジトリの名簿のディレクトリ（今のディレクトリから上へ探す）。無ければ null。</summary>
    private static string? FindRosterDir()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "games", "MiniRing", "Characters", "roster");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private void Handle(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        try
        {
            var path = request.Url?.AbsolutePath ?? "/";
            if (Refusal(request) is { } refusal)
            {
                WriteError(response, 403, refusal);
                return;
            }

            if (request.HttpMethod == "GET" && path is "/" or "/index.html")
            {
                WriteBytes(response, "text/html; charset=utf-8", Page());
                return;
            }

            if (request.HttpMethod == "GET" && path == "/api/catalog")
            {
                WriteJson(response, Catalog());
                return;
            }

            if (request.HttpMethod != "POST")
            {
                WriteError(response, 404, $"{request.HttpMethod} {path} はありません。");
                return;
            }

            var body = ReadBody(request);
            WriteJson(response, path switch
            {
                "/api/new" => New(body),
                "/api/root" => Root(body),
                "/api/weight" => Weight(body),
                "/api/preview" => Preview(body),
                "/api/save" => Save(body),
                "/api/load" => Load(body),
                "/api/import" => Import(body),
                "/api/photo" => Photo(body),
                _ => throw new FileNotFoundException($"POST {path} はありません。"),
            });
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or JsonException or FormatException)
        {
            // 入力の誤り: 画面に出す（どの欄かは例外のメッセージにある）。
            WriteError(response, 400, ex.Message.Split(" (Parameter")[0]);
        }
        catch (FileNotFoundException ex)
        {
            WriteError(response, 404, ex.Message);
        }
        catch (Exception ex)
        {
            // 予期しない失敗でも口を止めない（ログに全部、画面には短く）。返事が書けない（切れた）ときは捨てる。
            Console.Error.WriteLine($"キャラメイク: {request.Url}: {ex}");
            try
            {
                WriteError(response, 500, $"処理に失敗しました: {ex.Message}");
            }
            catch (Exception writeFailure) when (writeFailure is HttpListenerException or ObjectDisposedException or InvalidOperationException or IOException)
            {
                response.Abort();
            }
        }
    }

    /// <summary>
    /// この画面からの要求でなければ断る理由（受けるなら null）。口はファイルを書くので、ほかのサイトのページがブラウザ越しに送る要求
    /// （CSRF・DNS の付け替え）を受けない: 相手がこの PC・Host が localhost のこの口・Origin があればこの口・POST は JSON（ほかのサイトからは事前の確認が要り、この口は答えない）。
    /// </summary>
    internal string? Refusal(HttpListenerRequest request)
    {
        var self = $"localhost:{_port}";
        if (!request.IsLocal || !string.Equals(request.UserHostName, self, StringComparison.OrdinalIgnoreCase))
        {
            return $"この口は http://{self}/ からだけ使えます。";
        }

        if (request.Headers["Origin"] is { } origin && !string.Equals(origin, $"http://{self}", StringComparison.OrdinalIgnoreCase))
        {
            return "ほかのページからの要求は受けません。";
        }

        if (request.HttpMethod == "POST" && request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true)
        {
            return "POST は application/json だけ受けます。";
        }

        return null;
    }

    private static byte[] Page()
    {
        using var stream = typeof(MakerServer).Assembly.GetManifestResourceStream("MiniRing.Arena.Maker.maker.html")
            ?? throw new InvalidOperationException("組み込みの maker.html が見つかりません（MiniRing.Arena.csproj の EmbeddedResource）。");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>画面の選択肢（教科書・国・はめる物・武器・体型・ルーツ・顔の部品と値の幅・目印の点・名簿）。</summary>
    internal JsonObject Catalog()
    {
        var library = FacePartLibrary.Default;
        var parts = new JsonObject();
        foreach (var category in library.AllCategories)
        {
            parts[category] = new JsonArray([.. library.Parts(category).Select(p => (JsonNode)new JsonObject { ["id"] = p.Id, ["name"] = p.Name })]);
        }

        return new JsonObject
        {
            ["textbooks"] = new JsonArray([.. BotCatalog.Styles.Select(s => (JsonNode)s.Name)]),
            ["countries"] = new JsonArray([.. CharacterRoster.Countries.Values.Select(c => (JsonNode)new JsonObject { ["id"] = c.Id, ["name"] = c.Name })]),
            ["wears"] = new JsonArray([.. Enum.GetValues<Weapons.HandWear>().Select(w => (JsonNode)new JsonObject { ["id"] = CharacterFile.WearName(w), ["name"] = FighterCharacter.WearLabel(w) })]),
            ["weapons"] = new JsonArray([.. CharacterFile.Weapons.Select(w => (JsonNode)new JsonObject { ["id"] = CharacterFile.WeaponName(w), ["name"] = FighterCharacter.WeaponLabel(w) })]),
            ["orgs"] = new JsonArray("wvc", "vfc", "ivf"),
            ["bodyTypes"] = JsonSerializer.SerializeToNode(CharacterMaker.BodyTypes, Json),
            ["roots"] = new JsonArray([.. CharacterMaker.Roots.Select(r => (JsonNode)new JsonObject
            {
                ["id"] = r.Id, ["label"] = r.Label,
                ["skins"] = Colors(r.Skins), ["hairs"] = Colors(r.Hairs), ["irises"] = Colors(r.Irises),
            })]),
            ["faceParts"] = parts,
            ["optionalParts"] = new JsonArray([.. FaceSpec.Optional.Order(StringComparer.Ordinal).Select(c => (JsonNode)c)]),
            ["singleParts"] = new JsonArray("eyes", "brows"),
            ["faceValues"] = FaceValueRanges(),
            ["pointNames"] = new JsonArray([.. PhotoFace.PointNames.Select(n => (JsonNode)n)]),
            ["roster"] = new JsonArray([.. CharacterRoster.All.Select(c => (JsonNode)new JsonObject { ["id"] = c.Id, ["name"] = c.Name })]),
            ["rosterDir"] = _rosterDir,
            ["saveDir"] = _saveDir,
        };

        static JsonArray Colors(IReadOnlyList<Vector3> cs) => new([.. cs.Select(c => (JsonNode)new JsonArray(c.X, c.Y, c.Z))]);
    }

    /// <summary>顔の値の幅（<see cref="FaceParts.Validate"/> と同じ。名前は camelCase）。範囲の外を 1 つずつ試して読み取る（幅を 2 か所に書かない）。</summary>
    private static JsonArray FaceValueRanges()
    {
        var defaults = JsonSerializer.SerializeToNode(FaceParts.Default, Json)!.AsObject();
        var list = new JsonArray();
        foreach (var (key, value) in defaults)
        {
            var d = value!.GetValue<float>();
            float Edge(float step)
            {
                var v = d;
                for (var i = 0; i < 400; i++)
                {
                    var next = MathF.Round(v + step, 3);
                    var probe = (JsonObject)defaults.DeepClone();
                    probe[key] = next;
                    try
                    {
                        _ = probe.Deserialize<FaceParts>(Json)!.Validate();
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        return v;
                    }

                    v = next;
                }

                return v;
            }

            list.Add(new JsonObject { ["key"] = key, ["min"] = Edge(-0.005f), ["max"] = Edge(0.005f), ["default"] = d });
        }

        return list;
    }

    private static JsonObject New(JsonNode body)
    {
        var draft = CharacterMaker.NewDraft(body["bodyType"]?.GetValue<string>() ?? "standard", body["root"]?.GetValue<string>() ?? CharacterMaker.Roots[0].Id,
            body["heightCm"]?.GetValue<int>() ?? CharacterProfile.StandardHeightCm);
        return new JsonObject { ["draft"] = JsonSerializer.SerializeToNode(draft, Json) };
    }

    /// <summary>身長・腕・脚のときの体重の幅と身長の幅（脚の長さのとき）。muscle があれば、その太さの体重（体型を選んだとき）。</summary>
    private static JsonObject Weight(JsonNode body)
    {
        var height = body["heightCm"]?.GetValue<int>() ?? CharacterProfile.StandardHeightCm;
        var arms = body["arms"]?.GetValue<float>() ?? 1f;
        var legs = body["legs"]?.GetValue<float>() ?? 1f;
        var (hlo, hhi) = (CharacterProfile.HeightOf(new BodyBuild(BodySizing.MinRatio, 1f, legs, 1f)), CharacterProfile.HeightOf(new BodyBuild(BodySizing.MaxRatio, 1f, legs, 1f)));
        height = Math.Clamp(height, hlo + 1, hhi - 1);
        var (lo, hi) = BodySizing.WeightRange(height, arms, legs);
        var result = new JsonObject { ["range"] = new JsonArray(lo, hi), ["heightRange"] = new JsonArray(hlo + 1, hhi - 1) };
        if (body["muscle"] is JsonValue m && m.TryGetValue<float>(out var muscle))
        {
            result["weightKg"] = BodySizing.WeightFor(height, muscle, arms, legs);
        }

        return result;
    }

    /// <summary>ルーツのテンプレートの顔（色は候補の番号）。</summary>
    private static JsonObject Root(JsonNode body)
    {
        var root = CharacterMaker.RootOf(body["root"]?.GetValue<string>() ?? throw new InvalidDataException("root がありません。"));
        var face = root.Face(body["skin"]?.GetValue<int>() ?? 0, body["hair"]?.GetValue<int>() ?? 0, body["iris"]?.GetValue<int>() ?? 0);
        return new JsonObject { ["face"] = JsonSerializer.SerializeToNode(FaceDraft.From(face, CharacterMaker.LookOf(face)), Json) };
    }

    /// <summary>下書きを名簿のファイルにして検証し、体の数字とプレビューの絵を返す。壊れていれば 400（どの欄か）。</summary>
    private static JsonObject Preview(JsonNode body)
    {
        var draft = DraftOf(body["draft"] ?? body);
        var (character, json) = CharacterMaker.ToFile(draft);
        var result = BodyNumbers(draft);
        result["json"] = json;
        if (body["images"]?.GetValue<bool>() != false)
        {
            var head = FaceSheet.HeadViews(character, [0, 3, 1]);
            result["faceImage"] = PngDataUrl(head.Width, head.Height, head.Rgba);
            var portrait = CharacterPortraits.Build(character);
            var full = PortraitExport.Rgba(portrait.Body, 3);
            result["bodyImage"] = PngDataUrl(full.Width, full.Height, full.Rgba);
        }

        return result;
    }

    /// <summary>身長・体重・体格・物理の質量（体格が解けないときは体重の幅だけ）。</summary>
    private static JsonObject BodyNumbers(CharacterDraft draft)
    {
        var (lo, hi) = BodySizing.WeightRange(draft.HeightCm, draft.Arms, draft.Legs);
        var build = BodySizing.Solve(draft.HeightCm, draft.WeightKg, draft.Arms, draft.Legs);
        return new JsonObject
        {
            ["weightRange"] = new JsonArray(lo, hi),
            ["build"] = new JsonObject { ["height"] = build.Height, ["arms"] = build.Arms, ["legs"] = build.Legs, ["muscle"] = build.Muscle },
            ["massRatio"] = build.MassRatio,
            ["physicalMassKg"] = BodySizing.PhysicalMassKg(build),
            ["armStrength"] = build.SectionRatio,
        };
    }

    /// <summary>保存: 名簿（roster/、ビルドで組み込み）か、ファイルの場所（アプリのデータ）。同じ名前のファイルがあれば overwrite が要る。</summary>
    private JsonObject Save(JsonNode body)
    {
        var draft = DraftOf(body["draft"] ?? throw new InvalidDataException("draft がありません。"));
        var target = body["target"]?.GetValue<string>() ?? "dir";
        var overwrite = body["overwrite"]?.GetValue<bool>() ?? false;
        var (character, json) = CharacterMaker.ToFile(draft);
        string dir;
        if (target == "roster")
        {
            dir = _rosterDir ?? throw new InvalidDataException("名簿のディレクトリが見つかりません（リポジトリの根で maker を動かしてください）。");
        }
        else
        {
            dir = _saveDir;
            if (CharacterRoster.Find(character.Id) is not null)
            {
                throw new InvalidDataException($"id '{character.Id}' は組み込みの名簿にいます。別の id にしてください。");
            }
        }

        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, character.Id + ".json"); // id は名簿の読み込みが小文字・数字・-・_ だけと確かめた
        if (File.Exists(path) && !overwrite)
        {
            return new JsonObject { ["exists"] = true, ["path"] = path };
        }

        File.WriteAllText(path, json, new UTF8Encoding(false));
        Console.WriteLine($"キャラメイク: 保存 {path}");
        return new JsonObject
        {
            ["path"] = path,
            ["rebuild"] = target == "roster",
            ["video"] = target == "roster"
                ? $"dotnet run --project games/MiniRing.Arena -c Release -- video --red-char {character.Id} --blue-char marcus --seconds 40 --out docs/video/{character.Id}.mp4"
                : $"dotnet run --project games/MiniRing.Arena -c Release -- video --red-char \"{path}\" --blue-char marcus --seconds 40 --out {character.Id}.mp4",
        };
    }

    /// <summary>名簿のキャラ（組み込み）を下書きにする（作り始めの例・直すため）。</summary>
    private static JsonObject Load(JsonNode body)
    {
        var id = body["id"]?.GetValue<string>() ?? throw new InvalidDataException("id がありません。");
        return new JsonObject { ["draft"] = JsonSerializer.SerializeToNode(CharacterMaker.FromCharacter(CharacterRoster.Get(id)), Json) };
    }

    /// <summary>キャラクターのファイル（JSON の文字列）を読んで下書きにする（名簿の読み込みで検証）。</summary>
    private static JsonObject Import(JsonNode body)
    {
        var text = body["json"]?.GetValue<string>() ?? throw new InvalidDataException("json がありません。");
        var id = JsonNode.Parse(text)?["id"]?.GetValue<string>() ?? throw new InvalidDataException("ファイルに id がありません。");
        var character = CharacterRoster.FromFiles([($"{id}.json", text)], [.. CharacterRoster.All.Select(c => c.Id)]).Single();
        return new JsonObject { ["draft"] = JsonSerializer.SerializeToNode(CharacterMaker.FromCharacter(character), Json) };
    }

    /// <summary>写真（PNG の data URL）と目印の点から顔を作る。写真は保存しない。</summary>
    private static JsonObject Photo(JsonNode body)
    {
        var dataUrl = body["png"]?.GetValue<string>() ?? throw new InvalidDataException("png がありません。");
        var comma = dataUrl.IndexOf(',', StringComparison.Ordinal);
        var bytes = Convert.FromBase64String(comma >= 0 ? dataUrl[(comma + 1)..] : dataUrl);
        const int MaxPhotoSide = 2048; // 画面は 640 px に縮めて送る。大きすぎる PNG（展開の爆弾）は読まない
        DecodedImage image;
        try
        {
            // 展開する前に、PNG の頭（IHDR）の幅・高さで大きさを確かめる。
            if (bytes.Length < 24 || System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16)) > MaxPhotoSide
                || System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20)) > MaxPhotoSide)
            {
                throw new InvalidDataException($"写真は {MaxPhotoSide} px 以下の PNG にしてください。");
            }

            image = PngReader.ReadRgba(new MemoryStream(bytes));
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or EndOfStreamException)
        {
            throw new InvalidDataException($"写真の PNG が読めません: {ex.Message}", ex);
        }

        var points = new Dictionary<string, Vector2>();
        foreach (var (name, p) in body["points"]?.AsObject() ?? throw new InvalidDataException("points がありません。"))
        {
            points[name] = new Vector2(p?["x"]?.GetValue<float>() ?? float.NaN, p?["y"]?.GetValue<float>() ?? float.NaN);
        }

        var basis = body["face"] is { } f ? f.Deserialize<FaceDraft>(Json)!.ToSpec() : CharacterMaker.Roots[0].Face();
        var gain = body["gain"] is JsonValue g && g.TryGetValue<float>(out var gv) ? gv : Caricature.DefaultGain;
        var result = PhotoFace.FromPhoto(image.Width, image.Height, image.Rgba, points, basis, gain: gain);
        return new JsonObject
        {
            ["face"] = JsonSerializer.SerializeToNode(FaceDraft.From(result.Face, CharacterMaker.LookOf(result.Face)), Json),
            ["measured"] = JsonSerializer.SerializeToNode(result.Measured, Json),
        };
    }

    private static CharacterDraft DraftOf(JsonNode node)
        => node.Deserialize<CharacterDraft>(Json) ?? throw new InvalidDataException("下書きが空です。");

    private static string PngDataUrl(int width, int height, byte[] rgba)
    {
        using var memory = new MemoryStream();
        PngWriter.WriteRgba(memory, width, height, rgba);
        return "data:image/png;base64," + Convert.ToBase64String(memory.ToArray());
    }

    private static JsonNode ReadBody(HttpListenerRequest request)
    {
        if (request.ContentLength64 > MaxRequestBytes)
        {
            throw new InvalidDataException($"送られた物が大きすぎます（{MaxRequestBytes / 1024 / 1024} MB まで。写真は縮めて送ります）。");
        }

        // 長さの無い（chunked）要求も上限までしか読まない。
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = request.InputStream.Read(buffer)) > 0)
        {
            if (memory.Length + read > MaxRequestBytes)
            {
                throw new InvalidDataException($"送られた物が大きすぎます（{MaxRequestBytes / 1024 / 1024} MB まで）。");
            }

            memory.Write(buffer, 0, read);
        }

        return JsonNode.Parse(memory.ToArray()) ?? throw new InvalidDataException("空の JSON です。");
    }

    private static void WriteJson(HttpListenerResponse response, JsonNode node)
        => WriteBytes(response, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(node.ToJsonString(Json)));

    private static void WriteError(HttpListenerResponse response, int status, string message)
    {
        response.StatusCode = status;
        WriteJson(response, new JsonObject { ["error"] = message });
    }

    private static void WriteBytes(HttpListenerResponse response, string type, byte[] bytes)
    {
        response.ContentType = type;
        response.Headers["Cache-Control"] = "no-store";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes);
        response.OutputStream.Close();
    }
}
