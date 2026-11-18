<#
.SYNOPSIS
  WVC のテレビ中継の動画を、大会の札（card JSON）から作る。実況・解説の台本（bN.script.json）を書く所だけが人・エージェントの仕事。

.DESCRIPTION
  段（-Stage）:
    scout    種が "auto" の試合を下見して、一番見ごたえのある種を選ぶ（結果は <out>\card.resolved.json に残す。次からは下見しない）
    render   各試合を撮る（<out>\bN.mp4 と bN.events.json。あれば飛ばす。-Force で撮り直す）
    digest   台本の資料（<out>\bN.digest.md）と実況の骨組みの下書き（bN.draft.json）を書く
    write    LLM（札の commentary.model、既定 haiku）に全試合の台詞の新しいテイクを書かせる（tools\tvshow\write-commentary.ps1。テイクは <out>\takes\bN\ に残る）
    dub      台本（下書き bN.draft.json ＋ 足す台詞 bN.extras.json → bN.script.json）に声を入れ（bN_live.mp4）、台本を点検する
    liveprep 生の中継の準備だけ（種 → 各試合を描かずに下回しして出来事 → 資料・下書き → 台本 → 声）。<out>\bN_live_voices\manifest.json まで作る
    live     生の中継: 準備（liveprep）のあと、試合を順に、実時間で描いて声を混ぜながら札の live.url へ流す（OBS の受け側は docs\TV_BROADCAST.md「生配信」）
    package  全試合をつなぎ（<out>\broadcast_full.mp4）、送れる大きさ（既定 29 MiB）ごとの部分（broadcast_partN.mp4）に縮める（音は残す）
    all      上をすべて（既定）。台本が無い試合があれば dub・package の前で止まり、書くべき台本の場所を言う

  手順（docs\TV_BROADCAST.md）:
    1. powershell -ExecutionPolicy Bypass -File tools\tvshow\tvshow.ps1 -Card tools\tvshow\cards\wvc-ishu.json
    2. 足す台詞を書く（エージェント: .claude\skills\tv-commentary）。<out>\bN.digest.md と bN.draft.json を読み、<out>\bN.extras.json を書く
    3. もう一度 1 を回す（撮影は済んでいるので、声入れと結合だけが走る）

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\tvshow\tvshow.ps1 -Card tools\tvshow\cards\wvc-ishu.json -Stage render
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Card,
    [ValidateSet('all', 'scout', 'render', 'digest', 'write', 'dub', 'package', 'liveprep', 'live')][string]$Stage = 'all',
    [switch]$Force,
    # 部分の大きさの上限 [MiB]（スマホへ送れる大きさ）。札の partLimitMiB があればそちら。
    [double]$PartLimitMiB = 29
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$cardPath = (Resolve-Path $Card).Path
$cardData = Get-Content -Raw -Encoding UTF8 $cardPath | ConvertFrom-Json

function Get-Field($object, [string]$name, $default) {
    if ($object.PSObject.Properties.Name -contains $name -and $null -ne $object.$name) { return $object.$name }
    return $default
}

$out = Join-Path $repo (Get-Field $cardData 'out' 'docs\video\tvshow')
New-Item -ItemType Directory -Force -Path $out | Out-Null
$rounds = [int](Get-Field $cardData 'rounds' 3)
$breakSeconds = [double](Get-Field $cardData 'break' 25) # 25 秒: 2 本目のリプレイの後に、解説のなぜ効くかと問いと答えが入る長さ
# 主催団体（games/MiniRing.Arena/Orgs/<id>.json）。画面の色と名前・紋章・合図の音・下見の点数・台本の資料と言い回しが団体のファイルで変わる。既定は wvc。
$orgId = ([string](Get-Field $cardData 'org' 'wvc')).ToLowerInvariant()
$eventTitle = [string](Get-Field $cardData 'event' $orgId.ToUpperInvariant())
$scoutSeeds = @(Get-Field $cardData 'scoutSeeds' @(1, 2, 3, 4, 5, 6, 7, 8)) -join ','
$PartLimitMiB = [double](Get-Field $cardData 'partLimitMiB' $PartLimitMiB)
$bouts = @($cardData.bouts)
if ($bouts.Count -eq 0) { throw "札に bouts がありません: $cardPath" }

$exe = Join-Path $repo 'games\MiniRing.Arena\bin\Release\net10.0\miniring-arena.exe'
$voices = Join-Path $repo 'tools\commentary\dub.ps1'

function Invoke-Arena([string[]]$arguments) {
    # 外の道具が標準エラーに書いた行を、PowerShell 5.1 は Stop のときエラーとして止めるので、ここだけ Continue（失敗は終了コードで見る）。
    $ErrorActionPreference = 'Continue'
    $lines = & $exe @arguments 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) { throw "miniring-arena $(Hide-Secrets ($arguments -join ' ')) が失敗しました:`n$(Hide-Secrets ($lines -join "`n"))" }
    return $lines
}

# 配信先の URL には鍵・passphrase が入りうる。ログ・例外に出す前に、--live の値と srt/udp/rtmp の URL を伏せる。
function Hide-Secrets([string]$text) {
    $text = $text -replace '(--live\s+)\S+', '$1<url>'
    return $text -replace '(?i)(srt|udp|rtmps?)://\S+', '$1://<url>'
}

# 流しながら画面へ出す版（生配信: 受け手を待っている間の「受け手を待っています」を、その場で見せる）。出力は arena が伏せたもの。
function Invoke-ArenaStreaming([string[]]$arguments) {
    $ErrorActionPreference = 'Continue'
    $collected = New-Object System.Collections.Generic.List[string]
    & $exe @arguments 2>&1 | ForEach-Object { $line = "$_"; Write-Host $line; $collected.Add($line) }
    if ($LASTEXITCODE -ne 0) { throw "miniring-arena $(Hide-Secrets ($arguments -join ' ')) が失敗しました:`n$(Hide-Secrets ($collected -join "`n"))" }
    return $collected
}

function Get-Duration([string]$path) {
    $ErrorActionPreference = 'Continue' # ffmpeg -i は情報を標準エラーに書き、終了コードも 1 になる
    $info = & ffmpeg -hide_banner -i $path 2>&1 | Out-String
    if ($info -notmatch 'Duration: (\d+):(\d+):([\d.]+)') { throw "長さが読めません: $path" }
    return ([int]$Matches[1] * 3600) + ([int]$Matches[2] * 60) + [double]::Parse($Matches[3], [Globalization.CultureInfo]::InvariantCulture)
}

function Invoke-Ffmpeg([string[]]$arguments) {
    & ffmpeg -y -hide_banner -loglevel error @arguments
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg $($arguments -join ' ') が失敗しました" }
}

# 0. 道具をビルドする（Release。撮影は GPU、下見は CPU）。
Write-Host '== build'
& dotnet build (Join-Path $repo 'games\MiniRing.Arena') -c Release -nologo -v q | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'miniring-arena のビルドに失敗しました' }

# 1. 試合を札の順に 1 つずつ: 顔ぶれを決める → 種を決める → 撮る。
#    顔ぶれ: red / blue は名簿の id か "winner:N"（N 試合目の勝者。トーナメントの勝ち上がり）。"winner:N" は N 試合目の
#    中継の記録（bN.events.json の outcome）から決まるので、N 試合目を撮ってからでないと決まらない。引き分けは与えたダメージの
#    多い側（outcome.lead）が勝ち上がる（大会の決まり）。
#    種: "auto" は下見で一番見ごたえのある種を選んで残す。物理・ボットの版が変わると同じ種でも別の試合になるので版も残し、
#    版が変わっていたら選び直す（撮った動画も古いので撮り直しを知らせる）。顔ぶれが変わった試合も選び直す。
#    プログラム: 試合ごとに bN.program.json（その日の試合の順・済んだ試合の結果・この試合に懸かるもの）を書き、冒頭の
#    「本日のプログラム」の表に出す。前の試合の結果が変わって表が変わったら、その試合は撮り直す。
$versions = (Invoke-Arena @('version') | Select-Object -Last 1).Trim()
$resolvedPath = Join-Path $out 'card.resolved.json'
$resolved = @{}
$resolvedFighters = @{}
if ((Test-Path $resolvedPath) -and -not $Force) {
    $saved = Get-Content -Raw -Encoding UTF8 $resolvedPath | ConvertFrom-Json
    if ((Get-Field $saved 'versions' '') -eq $versions) {
        $saved.seeds.PSObject.Properties | ForEach-Object { $resolved[$_.Name] = [int]$_.Value }
        $savedFighters = Get-Field $saved 'fighters' $null
        if ($null -ne $savedFighters) { $savedFighters.PSObject.Properties | ForEach-Object { $resolvedFighters[$_.Name] = [string]$_.Value } }
    }
    else {
        Write-Warning "種を選んだときと版が違います（$(Get-Field $saved 'versions' '?') → $versions）。種を選び直します。撮影済みの動画は -Force で撮り直してください。"
    }
}

$programTitle = [string](Get-Field $cardData 'program' $eventTitle)

function Get-BoutLabel([int]$index) { return [string](Get-Field $bouts[$index] 'bout' "第$($index + 1)試合") }

# N 試合目（0 始まり）の結果: @{ winner = 'red'|'blue'; method = 'K.O.' など }。まだ撮っていなければ $null。
function Get-Outcome([int]$index) {
    $events = Join-Path $out "b$($index + 1).events.json"
    if (-not (Test-Path $events)) { return $null }
    $outcome = Get-Field (Get-Content -Raw -Encoding UTF8 $events | ConvertFrom-Json) 'outcome' $null
    if ($null -eq $outcome) { throw "b$($index + 1).events.json に結果（outcome）がありません。-Force で撮り直してください。" }
    $winner = [string]$outcome.winner
    if ($winner -eq 'draw') { return @{ winner = [string]$outcome.lead; method = '優勢' } } # 引き分けは与えたダメージの多い側が勝ち上がる
    return @{ winner = $winner; method = [string]$outcome.method; cause = [string](Get-Field $outcome 'deathPhrase' '') }
}

# 試合の 1 つのコーナーの名簿の id。"winner:N" で N 試合目がまだなら $null。
function Resolve-Fighter([int]$index, [string]$corner) {
    $spec = [string]$bouts[$index].$corner
    if ($spec -notmatch '^winner:(\d+)$') { return $spec }
    $from = [int]$Matches[1] - 1
    if ($from -lt 0 -or $from -ge $index) { throw "$(Get-BoutLabel $index) の $corner（$spec）は、この試合より前の試合を指してください。" }
    $outcome = Get-Outcome $from
    if ($null -eq $outcome) { return $null }
    return Resolve-Fighter $from $outcome.winner
}

# この試合（0 始まり）の時点のプログラム（JSON の文字列）。
function Get-ProgramJson([int]$current) {
    $rows = for ($j = 0; $j -lt $bouts.Count; $j++) {
        $row = [ordered]@{ bout = (Get-BoutLabel $j) }
        foreach ($corner in @('red', 'blue')) {
            $id = Resolve-Fighter $j $corner
            if ($null -eq $id -or $j -gt $current) {
                $spec = [string]$bouts[$j].$corner
                $id = if ($spec -match '^winner:(\d+)$') { "$(Get-BoutLabel ([int]$Matches[1] - 1))の勝者" } else { $spec }
            }
            $row[$corner] = $id
        }
        if ($j -lt $current) {
            $outcome = Get-Outcome $j
            if ($null -eq $outcome) { throw "b$($j + 1).events.json がありません（$(Get-BoutLabel $j) の結果が読めない）。-Force で撮り直してください。" }
            $row.winner = $outcome.winner
            $row.method = $outcome.method
            if ($outcome.ContainsKey('cause') -and $outcome.cause) { $row.cause = $outcome.cause }
        }
        $row.current = ($j -eq $current)
        $row.stake = [string](Get-Field $bouts[$j] 'stake' '')
        $row.next = [string](Get-Field $bouts[$j] 'next' '')
        $row
    }
    return ([ordered]@{ title = $programTitle; bouts = @($rows) } | ConvertTo-Json -Depth 4)
}

function Save-Resolved { @{ versions = $versions; seeds = $resolved; fighters = $resolvedFighters } | ConvertTo-Json | Set-Content -Encoding UTF8 $resolvedPath }

for ($i = 0; $i -lt $bouts.Count; $i++) {
    $bout = $bouts[$i]
    $key = "b$($i + 1)"
    $red = Resolve-Fighter $i 'red'
    $blue = Resolve-Fighter $i 'blue'
    if ($null -eq $red -or $null -eq $blue) {
        if ($Stage -eq 'scout') { Write-Host "== scout ${key}: 前の試合を撮るまで顔ぶれが決まりません（飛ばす）"; continue }
        throw "$key の顔ぶれが決まりません（前の試合の撮影がまだ）"
    }
    $fighters = "$red-$blue"
    if ($resolvedFighters.ContainsKey($key) -and $resolvedFighters[$key] -ne $fighters) {
        Write-Host "== ${key}: 顔ぶれが変わりました（$($resolvedFighters[$key]) → $fighters）。種を選び直します"
        $resolved.Remove($key)
    }
    $resolvedFighters[$key] = $fighters

    $seed = Get-Field $bout 'seed' 'auto'
    if ("$seed" -ne 'auto') { $resolved[$key] = [int]$seed }
    elseif (-not $resolved.ContainsKey($key)) {
        if ($Stage -notin @('all', 'scout', 'render', 'liveprep', 'live')) { throw "$key の種が決まっていません（-Stage scout を先に）" }
        Write-Host "== scout $key ($red vs $blue)"
        $lines = Invoke-Arena @('tvscout', '--red-char', $red, '--blue-char', $blue, '--rounds', "$rounds", '--break', "$breakSeconds", '--org', $orgId, '--seeds', $scoutSeeds)
        $lines | ForEach-Object { Write-Host $_ }
        $best = $lines | Where-Object { $_ -match '^best seed (\d+)$' } | Select-Object -Last 1
        if (-not $best) { throw "$key の種を選べませんでした" }
        $resolved[$key] = [int]([regex]::Match($best, '\d+').Value)
    }
    Save-Resolved
    if ($Stage -notin @('all', 'render')) { continue }

    # 2. 撮る（撮影済みでも、プログラムの表が変わっていたら撮り直す）。
    $video = Join-Path $out "$key.mp4"
    $programPath = Join-Path $out "$key.program.json"
    $program = Get-ProgramJson $i
    $programChanged = -not (Test-Path $programPath) -or ((Get-Content -Raw -Encoding UTF8 $programPath).Trim() -ne $program.Trim())
    if ((Test-Path $video) -and -not $Force -and -not $programChanged) { Write-Host "== render ${key}: 撮影済み（-Force で撮り直す）"; continue }
    # 撮影が途中で失敗したときに古い動画・古い結果が残って「撮影済み」と見なされないよう、先に消す。
    Remove-Item -ErrorAction SilentlyContinue $video, (Join-Path $out "$key.events.json")
    $program | Set-Content -Encoding UTF8 $programPath
    Write-Host "== render $key ($red vs $blue, seed $($resolved[$key]))"
    $lines = Invoke-Arena @('video', '--tv', 'on', '--facecam', 'on', '--rounds', "$rounds", '--break', "$breakSeconds",
        '--org', $orgId, '--event', $eventTitle, '--bout', (Get-BoutLabel $i), '--red-char', $red, '--blue-char', $blue, '--seed', "$($resolved[$key])",
        '--program', $programPath, '--out', $video)
    $lines | Where-Object { $_ -notmatch 'elapsed' } | Select-Object -Last 2 | ForEach-Object { Write-Host $_ }
    $outcome = Get-Outcome $i
    Write-Host "   ${key}: $(if ($outcome.winner -eq 'red') { $red } else { $blue }) の勝ち（$($outcome.method)）"
}

if ($Stage -eq 'scout') { return }

# 生の中継（liveprep・live）。準備 = 各試合を描かずに回して出来事を作り（撮影済みの bN.events.json があればそれを使う。同じ内容）、
# 資料・下書き・台本・声を作る。流す段 = 試合ごとに実時間で描き、声を混ぜながら札の live.url へ流す（試合と試合の間で接続は切れる）。
if ($Stage -in @('liveprep', 'live')) {
    $liveCard = Get-Field $cardData 'live' ([pscustomobject]@{})
    $liveUrl = [string](Get-Field $liveCard 'url' 'on')
    $liveWidth = [int](Get-Field $liveCard 'width' 1280)
    $liveHeight = [int](Get-Field $liveCard 'height' 720)
    $liveEncoder = [string](Get-Field $liveCard 'encoder' 'nvenc')
    $liveBitrate = [string](Get-Field $liveCard 'bitrate' '6M')
    $liveWait = Get-Field $liveCard 'wait' $null # 受け手が繋ぐのを待つ上限 [s]（省略時は video の既定 120）
    $liveDelay = Get-Field $liveCard 'delay' $null # 配信の遅延 [s]（省略時は video の既定 15）
    $synth = Join-Path $repo 'tools\commentary\Synthesize.ps1'

    function Get-BoutArguments($bout, [int]$i) {
        $key = "b$($i + 1)"
        $label = [string](Get-Field $bout 'bout' "第$($i + 1)試合")
        return @('--tv', 'on', '--facecam', 'on', '--rounds', "$rounds", '--break', "$breakSeconds",
            '--org', $orgId, '--event', $eventTitle, '--bout', $label, '--red-char', (Resolve-Fighter $i 'red'), '--blue-char', (Resolve-Fighter $i 'blue'), '--seed', "$($resolved[$key])")
    }

    for ($i = 0; $i -lt $bouts.Count; $i++) {
        $bout = $bouts[$i]
        $key = "b$($i + 1)"
        $events = Join-Path $out "$key.events.json"
        # 出来事は毎回作り直す（描かないので 1 試合 20 秒ほど）。前に作った bN.events.json を使い回すと、物理・ボットの版が
        # 変わったとき、流す試合と実況の台本がずれる（中身が同じなら tvdraft は書き直さないので、声も作り直さない）。
        Write-Host "== liveprep $key ($(Resolve-Fighter $i 'red') vs $(Resolve-Fighter $i 'blue'), seed $($resolved[$key])): 描かずに出来事だけ作る"
        $lines = Invoke-Arena (@('video') + (Get-BoutArguments $bout $i) + @('--render', 'off', '--out', (Join-Path $out "$key.mp4")))
        $lines | Where-Object { $_ -notmatch 'elapsed' } | Select-Object -Last 1 | ForEach-Object { Write-Host $_ }

        $digest = Join-Path $out "$key.digest.md"
        $draft = Join-Path $out "$key.draft.json"
        $extras = Join-Path $out "$key.extras.json"
        $script = Join-Path $out "$key.script.json"
        Invoke-Arena @('tvdigest', '--events', $events, '--out', $digest) | Out-Null
        Invoke-Arena @('tvdraft', '--events', $events, '--out', $draft) | Out-Null
        # 台本: 足す台詞があれば下書きに重ね、無ければ下書きだけ（生配信は autoCommentary を問わない）。
        if (Test-Path $extras) {
            Invoke-Arena @('tvmerge', '--draft', $draft, '--extras', $extras, '--out', $script) | Where-Object { $_ -match '入らなかった' } | ForEach-Object { Write-Warning "${key}: $_" }
        }
        else { Copy-Item $draft $script -Force }

        # 声だけ作る（本番で混ぜる）。
        $voiceDir = Join-Path $out "${key}_live_voices"
        $ErrorActionPreference = 'Continue'
        & powershell -NoProfile -ExecutionPolicy Bypass -File $synth -Script $script -OutDir $voiceDir | Out-Null
        $code = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
        if ($code -ne 0) { throw "$key の声の合成に失敗しました（tools\commentary\Synthesize.ps1 を直接動かすと理由が出ます）" }
        Invoke-Arena @('tvlint', '--script', $script, '--events', $events, '--manifest', (Join-Path $voiceDir 'manifest.json')) |
            ForEach-Object { if ($_ -eq 'OK') { Write-Host "   ${key}: 点検 OK" } else { Write-Warning "${key}: $_" } }
    }

    Write-Host "== liveprep 完了: <out>\bN_live_voices\manifest.json"
    if ($Stage -eq 'liveprep') { return }

    # 流す。試合ごとに 1 回。出来事は <out>\bN.live.events.json（下回しの bN.events.json と同じ内容になるはず）。
    for ($i = 0; $i -lt $bouts.Count; $i++) {
        $bout = $bouts[$i]
        $key = "b$($i + 1)"
        $manifest = Join-Path $out "${key}_live_voices\manifest.json"
        Write-Host "== live $key ($(Resolve-Fighter $i 'red') vs $(Resolve-Fighter $i 'blue')) -> $(Hide-Secrets $liveUrl)"
        $waitArguments = @(if ($null -ne $liveWait) { '--wait', "$liveWait" }) + @(if ($null -ne $liveDelay) { '--delay', "$liveDelay" })
        $lines = Invoke-ArenaStreaming (@('video') + (Get-BoutArguments $bout $i) + @('--width', "$liveWidth", '--height', "$liveHeight",
            '--live', $liveUrl, '--voices', $manifest, '--encoder', $liveEncoder, '--bitrate', $liveBitrate) + $waitArguments + @('--out', (Join-Path $out "$key.live.ts")))
        $lines | Where-Object { $_ -match '^live:' } | ForEach-Object { Write-Host "   $key $_" }
        $same = (Get-FileHash (Join-Path $out "$key.events.json")).Hash -eq (Get-FileHash (Join-Path $out "$key.live.events.json")).Hash
        if (-not $same) { Write-Warning "${key}: 本番の出来事が下回しと違います（試合が決定的でなくなっています）" }
    }

    Write-Host '== live 完了'
    return
}

# 3. 台本の資料。
if ($Stage -in @('all', 'render', 'digest')) {
    for ($i = 0; $i -lt $bouts.Count; $i++) {
        $key = "b$($i + 1)"
        $events = Join-Path $out "$key.events.json"
        if (Test-Path $events) {
            Invoke-Arena @('tvdigest', '--events', $events, '--out', (Join-Path $out "$key.digest.md")) | Out-Null
            # 実況の骨組みの下書き（選手紹介・ダウンのコールとカウント・立った・骨折と切断・ラウンドの区切り・リプレイの入り・決着・勝者）。
            Invoke-Arena @('tvdraft', '--events', $events, '--out', (Join-Path $out "$key.draft.json")) | Out-Null
        }
    }

    Write-Host "== digest: $out\bN.digest.md と bN.draft.json"
}

if ($Stage -in @('render', 'digest')) { return }

# 4. 台本を作って声を入れる。台本 = 下書き（bN.draft.json）＋書き手の足す台詞（bN.extras.json。tvmerge で重ねる）。
#    札の commentary.writer が "llm" なら、足す台詞の無い試合は LLM が書く（tools\tvshow\write-commentary.ps1。-Stage write は全試合の新しいテイク）。
#    足す台詞が無く、札の autoCommentary が true なら、下書きだけで台本にする（エージェントなしの実況）。
$commentaryCard = Get-Field $cardData 'commentary' $null
#    "prospective" なら、2 エージェントのプロスペクティブ制（tools\tvshow\prospective\run.js、docs\commentary\styles\prospective.md）。
$writerKind = [string](Get-Field $commentaryCard 'writer' '')
$llmWriter = $writerKind -in @('llm', 'prospective')
if ($writerKind -eq 'prospective' -and ($Stage -eq 'write' -or $Stage -in @('all', 'dub'))) {
    # プロスペクティブ制（Haiku 2 体の生実況）: 台本の無い試合を commentary.parallel（既定 2）本ずつ並べて動かす。止まった試合は 1 回だけやり直す
    # （同じ問いは記録 calls.jsonl の答えを使うので、やり直しは止まった所から先だけ LLM に聞く）。-Stage write は全試合を頭から。
    $runJs = Join-Path $PSScriptRoot 'prospective\run.js'
    $model = [string](Get-Field $commentaryCard 'model' 'haiku')
    $parallel = [Math]::Max(1, [int](Get-Field $commentaryCard 'parallel' 2))
    $todo = @(for ($i = 0; $i -lt $bouts.Count; $i++) { $k = "b$($i + 1)"; if ($Stage -eq 'write' -or -not (Test-Path (Join-Path $out "$k.extras.json"))) { $k } })
    foreach ($attempt in 1, 2) {
        $failed = @()
        for ($s = 0; $s -lt $todo.Count; $s += $parallel) {
            $batch = @($todo[$s..([Math]::Min($todo.Count, $s + $parallel) - 1)])
            $procs = foreach ($key in $batch) {
                $log = Join-Path $out "prospective-$key.log"
                $arguments = @("`"$runJs`"", '--card', "`"$cardPath`"", '--bout', $key, '--model', $model)
                if ($Stage -eq 'write' -and $attempt -eq 1) { $arguments += '--fresh' }
                Write-Host "== prospective $key（$model）"
                $proc = Start-Process node -ArgumentList $arguments -NoNewWindow -PassThru -RedirectStandardOutput $log -RedirectStandardError "$log.err"
                $null = $proc.Handle # ハンドルを先に握る（握らないと終わった後の ExitCode が空になり、成功しても止まったと見なした）
                @{ key = $key; proc = $proc }
            }
            foreach ($p in $procs) {
                $p.proc.WaitForExit()
                if ($p.proc.ExitCode -ne 0) { $failed += $p.key; Write-Warning "$($p.key) のプロスペクティブ制が止まりました（$out\prospective-$($p.key).log.err）" }
                else { Get-Content -Encoding UTF8 (Join-Path $out "prospective-$($p.key).log") | Select-Object -Last 1 | ForEach-Object { Write-Host "   $_" } }
            }
        }
        $todo = $failed
        if ($todo.Count -eq 0) { break }
    }
    if ($todo.Count -gt 0) { throw "プロスペクティブ制の中継が止まった試合があります: $($todo -join ', ')" }
    if ($Stage -eq 'write') { return }
}
elseif ($Stage -eq 'write' -or ($Stage -in @('all', 'dub') -and $llmWriter)) {
    $writer = Join-Path $PSScriptRoot 'write-commentary.ps1'
    for ($i = 0; $i -lt $bouts.Count; $i++) {
        $key = "b$($i + 1)"
        if ($Stage -ne 'write' -and (Test-Path (Join-Path $out "$key.extras.json"))) { continue }
        if ($writerKind -eq 'prospective') {
            & node (Join-Path $PSScriptRoot 'prospective\run.js') --card $cardPath --bout $key --model ([string](Get-Field $commentaryCard 'model' 'haiku'))
            if ($LASTEXITCODE -ne 0) { throw "$key のプロスペクティブ制の中継が止まりました（$out\prospective\$key\calls.jsonl）" }
            continue
        }
        & powershell -NoProfile -ExecutionPolicy Bypass -File $writer -Card $cardPath -Bout $key
        if ($LASTEXITCODE -ne 0) { throw "$key の台本を LLM が書けませんでした（$out\takes\$key の claude.log）" }
    }
    if ($Stage -eq 'write') { return }
}
$autoCommentary = [bool](Get-Field $cardData 'autoCommentary' $false)
$missing = @()
for ($i = 0; $i -lt $bouts.Count; $i++) {
    $key = "b$($i + 1)"
    $script = Join-Path $out "$key.script.json"
    $draft = Join-Path $out "$key.draft.json"
    $extras = Join-Path $out "$key.extras.json"
    if ((Test-Path $extras) -and (Test-Path $draft) -and ($Force -or -not (Test-Path $script) -or (Get-Item $extras).LastWriteTime -gt (Get-Item $script).LastWriteTime -or (Get-Item $draft).LastWriteTime -gt (Get-Item $script).LastWriteTime)) {
        Invoke-Arena @('tvmerge', '--draft', $draft, '--extras', $extras, '--out', $script) | Where-Object { $_ -match '入らなかった' } | ForEach-Object { Write-Warning "${key}: $_" }
    }
    elseif ($autoCommentary -and -not (Test-Path $extras) -and (Test-Path $draft) -and ($Force -or -not (Test-Path $script) -or (Get-Item $draft).LastWriteTime -gt (Get-Item $script).LastWriteTime)) {
        Copy-Item $draft $script
    }

    if (-not (Test-Path $script)) { $missing += $extras; continue }
    $live = Join-Path $out "${key}_live.mp4"
    if ($Stage -in @('all', 'dub') -and ($Force -or -not (Test-Path $live) -or (Get-Item $script).LastWriteTime -gt (Get-Item $live).LastWriteTime)) {
        Write-Host "== dub $key"
        $ErrorActionPreference = 'Continue'
        & powershell -NoProfile -ExecutionPolicy Bypass -File $voices -Video (Join-Path $out "$key.mp4") -Script $script -Out $live | Out-Null
        $code = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
        if ($code -ne 0) { throw "$key の声入れに失敗しました（tools\commentary\dub.ps1 を直接動かすと理由が出ます）" }
        # 台本の点検（主語・長さ・言わない言葉・言い直し・休みの掛け合い・コール・リプレイの入り・ずれ。docs\TV_BROADCAST.md §4）。
        Invoke-Arena @('tvlint', '--script', $script, '--events', (Join-Path $out "$key.events.json"), '--manifest', (Join-Path $out "${key}_live_voices\manifest.json")) |
            ForEach-Object { if ($_ -eq 'OK') { Write-Host "   ${key}: 点検 OK" } else { Write-Warning "${key}: $_" } }
    }
}

if ($missing.Count -gt 0) {
    Write-Host '== 足す台詞がまだありません。bN.digest.md と bN.draft.json を読み、bN.extras.json を書いてください（.claude\skills\tv-commentary）。下書きだけで作るなら札に "autoCommentary": true:'
    $missing | ForEach-Object { Write-Host "   $_" }
    return
}

if ($Stage -eq 'dub') { return }

# 5. つないで、送れる大きさの部分に縮める。
Write-Host '== package'
$lives = @(for ($i = 0; $i -lt $bouts.Count; $i++) { Join-Path $out "b$($i + 1)_live.mp4" })
$list = Join-Path $out 'broadcast.txt'
$lives | ForEach-Object { "file '$([IO.Path]::GetFileName($_))'" } | Set-Content -Encoding ASCII $list
Push-Location $out
try {
    Invoke-Ffmpeg @('-f', 'concat', '-safe', '0', '-i', 'broadcast.txt', '-c', 'copy', 'broadcast_full.mp4')
    # 試合の切れ目で部分に分ける: 1 部分の長さは「上限の大きさ ÷ 最低限の画質（映像 500 kb/s + 音 80 kb/s）」まで。
    $limitBits = $PartLimitMiB * 1024 * 1024 * 8
    $maxSeconds = $limitBits / 580000
    $parts = @()
    $current = @()
    $currentSeconds = 0.0
    foreach ($live in $lives) {
        $d = Get-Duration $live
        if ($current.Count -gt 0 -and $currentSeconds + $d -gt $maxSeconds) { $parts += , @($current); $current = @(); $currentSeconds = 0.0 }
        $current += [IO.Path]::GetFileName($live)
        $currentSeconds += $d
    }

    if ($current.Count -gt 0) { $parts += , @($current) }
    Get-ChildItem -Filter 'broadcast_part*.mp4' | Remove-Item
    for ($p = 0; $p -lt $parts.Count; $p++) {
        $partList = "part$($p + 1).txt"
        $parts[$p] | ForEach-Object { "file '$_'" } | Set-Content -Encoding ASCII $partList
        $seconds = ($parts[$p] | ForEach-Object { Get-Duration $_ } | Measure-Object -Sum).Sum
        # 映像の速さ: 上限に 5% の余裕を残し、音（80 kb/s）を引いた残り。画質の上限は 900 kb/s。
        $videoRate = [int][Math]::Min(900000, (($limitBits * 0.95) / $seconds) - 80000)
        Invoke-Ffmpeg @('-f', 'concat', '-safe', '0', '-i', $partList, '-vcodec', 'libx264', '-b:v', "$videoRate", '-maxrate', "$([int]($videoRate * 1.4))", '-bufsize', "$([int]($videoRate * 2.8))",
            '-preset', 'slow', '-vf', 'scale=960:-2', '-c:a', 'aac', '-b:a', '80k', "broadcast_part$($p + 1).mp4")
        $size = (Get-Item "broadcast_part$($p + 1).mp4").Length / 1MB
        Write-Host ("   broadcast_part{0}.mp4: {1} ({2:F0} s, {3:F1} MiB)" -f ($p + 1), ($parts[$p] -join ' + '), $seconds, $size)
        Remove-Item $partList
    }
}
finally {
    Pop-Location
}

Write-Host "== done: $out\broadcast_full.mp4 と broadcast_partN.mp4"
