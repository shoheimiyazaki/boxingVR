<#
.SYNOPSIS
  LLM（既定 Haiku）に、試合ごとの実況・解説の全部の台詞（bN.extras.json）を書かせる。1 回 = 1 テイク。

.DESCRIPTION
  再現のしかた（docs\TV_BROADCAST.md §4.4「LLM の台本」）:
    - 決まった手順: プロンプトの本体は tools\tvshow\prompts\tv-duo-free.md。資料（bN.digest.md・events・draft）は試合の記録から決まる。
    - ばらつき: テイクごとに「味付け」（二人の空気 1 つ・話題 3 つ。prompts\angles.json から種で選ぶ）を変え、前のテイクと同じ言い回しを避けさせる。
      味付けは提案で、守らせる決まりは点検（tvlint）が見るものだけ。同じ種でも LLM の出力は毎回違う（種は味付けを再現するだけ）。
    - 再現: 採ったテイクの台本（bN.extras.json）があれば、声入れ・結合は同じ結果になる。テイクはすべて <out>\takes\bN\<時刻>-s<種>\ に
      prompt.md・extras.json・take.json（種・味付け・モデル・点検の結果・台詞の数）として残る。戻すには -Use <テイクのフォルダ>。

  書き手（-Mode）:
    cli      Claude Code の CLI（claude -p --model <Model>）を子として動かし、点検が OK になるまで直させる（既定。CLI のログインが要る）
    prepare  テイクのフォルダと prompt.md を作るだけ（"PROMPT: <path>" を出す）。エージェントが LLM（Haiku のサブエージェント）に読ませる
    finish   prepare のテイクを点検して記録する。OK なら終わり、だめなら直しの頼み fixN.md を書き "FIX: <path>" を出して終了コード 2

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\tvshow\write-commentary.ps1 -Card tools\tvshow\cards\wvc-boxing-tournament.json -Bout b1
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\tvshow\write-commentary.ps1 -Card <札> -Bout b1 -Mode prepare
  （エージェントが Haiku に "Read <PROMPT> and do exactly what it says" と頼む）
  powershell -ExecutionPolicy Bypass -File tools\tvshow\write-commentary.ps1 -Card <札> -Bout b1 -Mode finish
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\tvshow\write-commentary.ps1 -Card <札> -Bout b1 -Use docs\video\wvc-boxing-tournament\takes\b1\20261117-075736-kept
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Card,
    # 書く試合（b1, b2 ...）。省くと全試合。
    [string[]]$Bout,
    [ValidateSet('cli', 'prepare', 'finish')][string]$Mode = 'cli',
    # 味付けを選ぶ種。省くと毎回ちがう種（take.json に残る）。
    [int]$Seed = -1,
    # 省くと札の commentary.model、それも無ければ haiku。
    [string]$Model,
    # cli で点検が OK でないとき、直しを頼む回数の上限。
    [int]$Fixes = 2,
    # このテイクの台本を bN.extras.json に戻す（LLM は動かさない）。
    [string]$Use
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$cardData = Get-Content -Raw -Encoding UTF8 (Resolve-Path $Card).Path | ConvertFrom-Json

function Get-Field($object, [string]$name, $default) {
    if ($null -ne $object -and $object.PSObject.Properties.Name -contains $name -and $null -ne $object.$name) { return $object.$name }
    return $default
}

function Get-Relative([string]$path) { return $path.Substring($repo.Length + 1).Replace('\', '/') }

$outRel = ([string](Get-Field $cardData 'out' 'docs\video\tvshow')).Replace('\', '/')
$out = Join-Path $repo $outRel
$commentary = Get-Field $cardData 'commentary' $null
if (-not $Model) { $Model = [string](Get-Field $commentary 'model' 'haiku') }
$template = Join-Path $PSScriptRoot 'prompts\tv-duo-free.md'
$anglesPath = Join-Path $PSScriptRoot 'prompts\angles.json'
$angles = Get-Content -Raw -Encoding UTF8 $anglesPath | ConvertFrom-Json
$exeRel = 'games/MiniRing.Arena/bin/Release/net10.0/miniring-arena.exe'
$exe = Join-Path $repo $exeRel
if (-not (Test-Path $exe)) { throw 'miniring-arena.exe がありません（dotnet build games\MiniRing.Arena -c Release）' }
$utf8 = New-Object Text.UTF8Encoding $false

$bouts = @($cardData.bouts)
$keys = if ($Bout) { $Bout } else { @(for ($i = 0; $i -lt $bouts.Count; $i++) { "b$($i + 1)" }) }

function Invoke-Arena([string[]]$arguments) {
    $ErrorActionPreference = 'Continue'
    return @(& $exe @arguments 2>&1 | ForEach-Object { "$_" })
}

function Select-Angles([int]$seed) {
    # 種から、空気を 1 つと話題を 3 つ（重ならない）選ぶ。
    $random = New-Object System.Random $seed
    $mood = $angles.moods[$random.Next($angles.moods.Count)]
    $pool = [System.Collections.ArrayList]@($angles.topics)
    $topics = @(for ($k = 0; $k -lt 3 -and $pool.Count -gt 0; $k++) {
        $j = $random.Next($pool.Count); $pool[$j]; $pool.RemoveAt($j)
    })
    return @{ mood = $mood; topics = $topics }
}

function Test-Script([string]$key) {
    $script = Join-Path $out "$key.script.json"
    $merge = @(Invoke-Arena @('tvmerge', '--draft', (Join-Path $out "$key.draft.json"), '--extras', (Join-Path $out "$key.extras.json"), '--out', $script))
    $lint = @(Invoke-Arena @('tvlint', '--script', $script, '--events', (Join-Path $out "$key.events.json")))
    return @{ ok = ($lint.Count -eq 1 -and $lint[0] -eq 'OK'); lint = $lint; dropped = @($merge | Where-Object { $_ -match '入らなかった' }) }
}

function Get-TakesRoot([string]$key) { return Join-Path $out "takes\$key" }

function New-Take([string]$key) {
    foreach ($need in @('digest.md', 'events.json', 'draft.json')) {
        if (-not (Test-Path (Join-Path $out "$key.$need"))) { throw "$key.$need がありません（tvshow.ps1 -Stage digest を先に）" }
    }
    $index = [int]$key.Substring(1) - 1
    $boutData = if ($index -lt $bouts.Count) { $bouts[$index] } else { $null }
    $takeSeed = if ($Seed -ge 0) { $Seed } else { Get-Random -Minimum 1 -Maximum 1000000 }
    $pick = Select-Angles $takeSeed
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $takesRoot = Get-TakesRoot $key
    $take = Join-Path $takesRoot "$stamp-s$takeSeed"
    New-Item -ItemType Directory -Force -Path $take | Out-Null

    # 今の台本がどのテイクにも無ければ（手で書いた・前の手順で作った）、消さないよう先にテイクとして残す。
    $current = Join-Path $out "$key.extras.json"
    $previousTake = $null
    if (Test-Path $current) {
        $hash = (Get-FileHash $current).Hash
        $known = @(Get-ChildItem -Path $takesRoot -Recurse -Filter 'extras.json' -ErrorAction SilentlyContinue |
            Where-Object { (Get-FileHash $_.FullName).Hash -eq $hash })
        if ($known.Count -eq 0) {
            $kept = Join-Path $takesRoot "$stamp-kept"
            New-Item -ItemType Directory -Force -Path $kept | Out-Null
            Copy-Item $current (Join-Path $kept 'extras.json')
            $previousTake = Join-Path $kept 'extras.json'
        }
        else { $previousTake = $known[0].FullName }
    }

    $previous = ''
    if ($previousTake) {
        $previous = "- 前のテイク ``$(Get-Relative $previousTake)``（読んで、**同じ言い回し・同じ話題の並びをなるべく避け**、違う切り口で書く。事実は同じでよい）"
    }
    $cardNotes = ''
    $notes = [string](Get-Field $boutData 'notes' '')
    if ($notes) { $cardNotes = "- この試合について（札から）: $notes" }

    $fill = [ordered]@{
        '{{event}}' = [string](Get-Field $cardData 'event' 'WVC'); '{{key}}' = $key
        '{{bout}}' = [string](Get-Field $boutData 'bout' $key); '{{out}}' = $outRel; 'OUT/' = "$outRel/"
        '{{exe}}' = "./$exeRel"; '{{previous}}' = $previous; '{{seed}}' = [string]$takeSeed
        '{{mood}}' = $pick.mood; '{{topics}}' = ($pick.topics -join '、'); '{{cardNotes}}' = $cardNotes
    }
    $prompt = Get-Content -Raw -Encoding UTF8 $template
    foreach ($entry in $fill.GetEnumerator()) { $prompt = $prompt.Replace($entry.Key, $entry.Value) }
    $promptPath = Join-Path $take 'prompt.md'
    [IO.File]::WriteAllText($promptPath, $prompt, $utf8)

    $record = [ordered]@{
        bout = $key; status = 'pending'; seed = $takeSeed; model = $Model; mood = $pick.mood; topics = $pick.topics
        promptSha256 = (Get-FileHash $template).Hash; anglesSha256 = (Get-FileHash $anglesPath).Hash
        previous = $(if ($previousTake) { Get-Relative $previousTake } else { $null }); before = $(if (Test-Path $current) { (Get-FileHash $current).Hash } else { '' })
        at = (Get-Date -Format 'o')
    }
    Save-Record $take $record
    Write-Host "== $key のテイク（種 $takeSeed・$Model）: $($pick.mood) / $($pick.topics -join '、')"
    return @{ path = $take; prompt = $promptPath; record = $record }
}

function Save-Record([string]$take, $record) {
    [IO.File]::WriteAllText((Join-Path $take 'take.json'), ($record | ConvertTo-Json -Depth 4), $utf8)
}

function Get-PendingTake([string]$key) {
    $pending = @(Get-ChildItem -Path (Get-TakesRoot $key) -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending |
        Where-Object { (Test-Path (Join-Path $_.FullName 'take.json')) -and ((Get-Content -Raw -Encoding UTF8 (Join-Path $_.FullName 'take.json') | ConvertFrom-Json).status -eq 'pending') })
    if ($pending.Count -eq 0) { throw "$key に書きかけのテイクがありません（-Mode prepare を先に）" }
    return $pending[0].FullName
}

function Write-FixRequest([string]$key, [string]$take, $check, [int]$round) {
    $fix = Join-Path $take "fix$round.md"
    $body = @(
        "``$outRel/$key.extras.json`` の点検が通りませんでした。下の指摘だけを直してください（ほかの台詞の中身・味付けは変えない）。"
        "直したら ``./$exeRel tvmerge --draft $outRel/$key.draft.json --extras $outRel/$key.extras.json --out $outRel/$key.script.json`` と"
        "``./$exeRel tvlint --script $outRel/$key.script.json --events $outRel/$key.events.json`` で OK を確かめる。決まりは ``$(Get-Relative (Join-Path $take 'prompt.md'))`` の「欠かせないもの」。"
        ''
        '点検の指摘:'
    ) + @($check.lint | ForEach-Object { "- $_" })
    if ($check.dropped.Count -gt 0) { $body += @('', '入らなかった台詞:') + @($check.dropped | ForEach-Object { "- $_" }) }
    [IO.File]::WriteAllText($fix, ($body -join "`n"), $utf8)
    return $fix
}

function Complete-Take([string]$key, [string]$take, $check) {
    $current = Join-Path $out "$key.extras.json"
    $record = Get-Content -Raw -Encoding UTF8 (Join-Path $take 'take.json') | ConvertFrom-Json
    if (-not (Test-Path $current) -or (Get-FileHash $current).Hash -eq $record.before) { throw "$key の台本が書かれていません（$take）" }
    Copy-Item $current (Join-Path $take 'extras.json')
    $lines = @((Get-Content -Raw -Encoding UTF8 $current | ConvertFrom-Json).lines)
    $done = [ordered]@{}
    foreach ($property in $record.PSObject.Properties) { $done[$property.Name] = $property.Value }
    $done.status = $(if ($check.ok) { 'ok' } else { 'lint' })
    $done.announcer = @($lines | Where-Object { $_.speaker -eq 'announcer' }).Count
    $done.commentator = @($lines | Where-Object { $_.speaker -eq 'commentator' }).Count
    $done.lint = $check.lint
    $done.dropped = $check.dropped
    $done.finished = (Get-Date -Format 'o')
    Save-Record $take $done
    $state = if ($check.ok) { '点検 OK' } else { "点検 NG（$($check.lint.Count) 行）" }
    Write-Host "   ${key}: 実況 $($done.announcer)・解説 $($done.commentator)・$state → $(Get-Relative $take)"
}

function Invoke-Claude([string]$promptPath, [string]$log) {
    # プロンプトはファイルで渡す（PowerShell 5.1 は引数・標準入力の日本語を化かすため、引数は ASCII だけ）。
    $claude = Get-Command claude -ErrorAction SilentlyContinue
    if ($null -eq $claude) { throw 'claude（Claude Code の CLI）が見つかりません。-Mode prepare / finish でエージェントに書かせる' }
    Remove-Item Env:CLAUDECODE -ErrorAction SilentlyContinue # 中の Claude Code から呼ばれても入れ子として断らないように
    $ask = "Read the file $(Get-Relative $promptPath) and do exactly what it says. Answer in Japanese."
    $tools = @('Read', 'Write', 'Edit', 'Glob', 'Grep', "Bash(${exeRel}:*)", "Bash(./${exeRel}:*)")
    Push-Location $repo
    try {
        $ErrorActionPreference = 'Continue'
        & $claude.Source -p $ask --model $Model --permission-mode acceptEdits --allowedTools @tools 2>&1 |
            ForEach-Object { "$_" } | Add-Content -Encoding UTF8 $log
        if ($LASTEXITCODE -ne 0) {
            throw "claude が失敗しました（$log）。ログインが切れていたら、端末で claude を開いて /login。またはエージェントから -Mode prepare / finish"
        }
    }
    finally { Pop-Location }
}

foreach ($key in $keys) {
    if ($Use) {
        $source = Join-Path (Resolve-Path $Use).Path 'extras.json'
        Copy-Item $source (Join-Path $out "$key.extras.json")
        Write-Host "== ${key}: $(Get-Relative $source) を台本に戻しました"
        continue
    }
    switch ($Mode) {
        'prepare' {
            $take = New-Take $key
            Write-Host "PROMPT: $(Get-Relative $take.prompt)"
        }
        'finish' {
            $take = Get-PendingTake $key
            $check = Test-Script $key
            if ($check.ok) { Complete-Take $key $take $check; continue }
            $round = @(Get-ChildItem -Path $take -Filter 'fix*.md').Count + 1
            if ($round -gt $Fixes) { Complete-Take $key $take $check; continue }
            $fix = Write-FixRequest $key $take $check $round
            Write-Host "FIX: $(Get-Relative $fix)"
            exit 2
        }
        default {
            $take = New-Take $key
            $log = Join-Path $take.path 'claude.log'
            Invoke-Claude $take.prompt $log
            $check = Test-Script $key
            for ($round = 1; -not $check.ok -and $round -le $Fixes; $round++) {
                Write-Host "   直し $round"
                Invoke-Claude (Write-FixRequest $key $take.path $check $round) $log
                $check = Test-Script $key
            }
            Complete-Take $key $take.path $check
        }
    }
}
