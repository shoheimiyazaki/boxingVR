<#
.SYNOPSIS
  WVC のテレビ中継の動画を、大会の札（card JSON）から作る。実況・解説の台本（bN.script.json）を書く所だけが人・エージェントの仕事。

.DESCRIPTION
  段（-Stage）:
    scout    種が "auto" の試合を下見して、一番見ごたえのある種を選ぶ（結果は <out>\card.resolved.json に残す。次からは下見しない）
    render   各試合を撮る（<out>\bN.mp4 と bN.events.json。あれば飛ばす。-Force で撮り直す）
    digest   台本の資料（<out>\bN.digest.md）と実況の骨組みの下書き（bN.draft.json）を書く
    dub      台本（下書き bN.draft.json ＋ 足す台詞 bN.extras.json → bN.script.json）に声を入れ（bN_live.mp4）、台本を点検する
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
    [ValidateSet('all', 'scout', 'render', 'digest', 'dub', 'package')][string]$Stage = 'all',
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
$breakSeconds = [double](Get-Field $cardData 'break' 20)
$eventTitle = [string](Get-Field $cardData 'event' 'WVC')
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
    if ($LASTEXITCODE -ne 0) { throw "miniring-arena $($arguments -join ' ') が失敗しました:`n$($lines -join "`n")" }
    return $lines
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

# 1. 種を決める（"auto" は下見で選び、決めた種を残す）。物理・ボットの版が変わると同じ種でも別の試合になるので、版も残し、
#    版が変わっていたら選び直す（撮った動画も古いので撮り直しを知らせる）。
$versions = (Invoke-Arena @('version') | Select-Object -Last 1).Trim()
$resolvedPath = Join-Path $out 'card.resolved.json'
$resolved = @{}
if ((Test-Path $resolvedPath) -and -not $Force) {
    $saved = Get-Content -Raw -Encoding UTF8 $resolvedPath | ConvertFrom-Json
    if ((Get-Field $saved 'versions' '') -eq $versions) {
        $saved.seeds.PSObject.Properties | ForEach-Object { $resolved[$_.Name] = [int]$_.Value }
    }
    else {
        Write-Warning "種を選んだときと版が違います（$(Get-Field $saved 'versions' '?') → $versions）。種を選び直します。撮影済みの動画は -Force で撮り直してください。"
    }
}

for ($i = 0; $i -lt $bouts.Count; $i++) {
    $bout = $bouts[$i]
    $key = "b$($i + 1)"
    $seed = Get-Field $bout 'seed' 'auto'
    if ("$seed" -ne 'auto') { $resolved[$key] = [int]$seed; continue }
    if ($resolved.ContainsKey($key)) { continue }
    if ($Stage -notin @('all', 'scout', 'render')) { throw "$key の種が決まっていません（-Stage scout を先に）" }
    Write-Host "== scout $key ($($bout.red) vs $($bout.blue))"
    $lines = Invoke-Arena @('tvscout', '--red-char', $bout.red, '--blue-char', $bout.blue, '--rounds', "$rounds", '--break', "$breakSeconds", '--seeds', $scoutSeeds)
    $lines | ForEach-Object { Write-Host $_ }
    $best = $lines | Where-Object { $_ -match '^best seed (\d+)$' } | Select-Object -Last 1
    if (-not $best) { throw "$key の種を選べませんでした" }
    $resolved[$key] = [int]([regex]::Match($best, '\d+').Value)
}

@{ versions = $versions; seeds = $resolved } | ConvertTo-Json | Set-Content -Encoding UTF8 $resolvedPath
if ($Stage -eq 'scout') { return }

# 2. 撮る。
if ($Stage -in @('all', 'render')) {
    for ($i = 0; $i -lt $bouts.Count; $i++) {
        $bout = $bouts[$i]
        $key = "b$($i + 1)"
        $video = Join-Path $out "$key.mp4"
        if ((Test-Path $video) -and -not $Force) { Write-Host "== render ${key}: 撮影済み（-Force で撮り直す）"; continue }
        $label = [string](Get-Field $bout 'bout' "第$($i + 1)試合")
        Write-Host "== render $key ($($bout.red) vs $($bout.blue), seed $($resolved[$key]))"
        $lines = Invoke-Arena @('video', '--tv', 'on', '--facecam', 'on', '--rounds', "$rounds", '--break', "$breakSeconds",
            '--event', $eventTitle, '--bout', $label, '--red-char', $bout.red, '--blue-char', $bout.blue, '--seed', "$($resolved[$key])", '--out', $video)
        $lines | Where-Object { $_ -notmatch 'elapsed' } | Select-Object -Last 2 | ForEach-Object { Write-Host $_ }
    }
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
#    足す台詞が無く、札の autoCommentary が true なら、下書きだけで台本にする（エージェントなしの実況）。
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
