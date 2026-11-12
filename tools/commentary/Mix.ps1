<#
.SYNOPSIS
  Mixes the commentary clips listed in a manifest (from Synthesize.ps1) onto a video.
  Each clip is delayed to its start time, clips are summed, the game audio is ducked
  (default -6 dB, with smooth ramps) while a voice is speaking, a limiter keeps the peak
  below -1 dBFS, and the video stream is copied untouched. Audio is encoded as AAC.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Mix.ps1 -Video in.mp4 -Manifest voices\manifest.json -Out out.mp4
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Video,
    [Parameter(Mandatory = $true)][string]$Manifest,
    [Parameter(Mandatory = $true)][string]$Out,
    [double]$DuckDb = 6.0,        # how much the game audio is lowered under a voice
    [double]$DuckAttack = 0.15,   # seconds: ramp down before the voice starts
    [double]$DuckRelease = 0.40,  # seconds: ramp back up after the voice ends
    [double]$VoiceGainDb = 0.0,   # extra gain on the summed voices
    [double]$LimitDb = -1.0,      # limiter ceiling (dBFS)
    [string]$AudioBitrate = '192k'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2
$inv = [System.Globalization.CultureInfo]::InvariantCulture
function F([double]$x) { return $x.ToString('0.######', $inv) }

function Invoke-Native([string]$exe, [string[]]$arguments) {
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = & $exe @arguments 2>&1 | ForEach-Object { "$_" } }
    finally { $ErrorActionPreference = $prev }
    if ($LASTEXITCODE -ne 0) { throw "$exe failed (exit $LASTEXITCODE):`n$($output -join "`n")" }
    return $output
}

if (-not (Get-Command 'ffmpeg' -ErrorAction SilentlyContinue)) { throw "ffmpeg not found on PATH" }

# Duration and presence of an audio stream, read from `ffmpeg -i` (ffprobe is not always installed).
function Get-MediaInfo([string]$path) {
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $text = (& ffmpeg -hide_banner -i $path 2>&1 | ForEach-Object { "$_" }) -join "`n" }
    finally { $ErrorActionPreference = $prev }
    $d = [regex]::Match($text, 'Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)')
    if (-not $d.Success) { throw "Could not read the duration of ${path}:`n$text" }
    $secs = [int]$d.Groups[1].Value * 3600 + [int]$d.Groups[2].Value * 60 + [double]::Parse($d.Groups[3].Value, $inv)
    return @{ duration = $secs; hasAudio = ($text -match 'Stream #\d+:\d+[^\n]*: Audio:') }
}
if (-not (Test-Path -LiteralPath $Video)) { throw "Video not found: $Video" }
if (-not (Test-Path -LiteralPath $Manifest)) { throw "Manifest not found: $Manifest" }

$Video = (Resolve-Path -LiteralPath $Video).Path
$Manifest = (Resolve-Path -LiteralPath $Manifest).Path
$outDir = Split-Path -Parent ([System.IO.Path]::GetFullPath($Out))
if ($outDir -and -not (Test-Path -LiteralPath $outDir)) { $null = New-Item -ItemType Directory -Force -Path $outDir }
$Out = [System.IO.Path]::GetFullPath($Out)
if ($Out -eq $Video) { throw "Out must differ from Video" }

$m = Get-Content -LiteralPath $Manifest -Raw -Encoding UTF8 | ConvertFrom-Json
$lines = @($m.lines)
if ($lines.Count -eq 0) { throw "Manifest has no lines: $Manifest" }

# Clips are referenced by file name relative to the manifest folder (ffmpeg runs there),
# which avoids filter-graph escaping of drive letters and keeps the command line short.
$voiceDir = Split-Path -Parent $Manifest
foreach ($l in $lines) {
    $name = Split-Path -Leaf $l.file
    if (-not (Test-Path -LiteralPath (Join-Path $voiceDir $name))) { throw "Clip missing next to manifest: $name" }
    if ($name -notmatch '^[A-Za-z0-9_.-]+$') { throw "Unsupported clip file name: $name" }
}

# ---- probe the video -----------------------------------------------------------
$info = Get-MediaInfo $Video
$videoDur = $info.duration
$hasAudio = $info.hasAudio
$lastEnd = ($lines | Measure-Object -Property end -Maximum).Maximum
if ($lastEnd -gt $videoDur) {
    Write-Warning ("Commentary ends at {0:F2}s but the video is {1:F2}s; the tail will be cut." -f $lastEnd, $videoDur)
}

# ---- filter graph ----------------------------------------------------------------
$fmt = 'aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo'
$g = New-Object System.Collections.Generic.List[string]
$labels = @()
for ($i = 0; $i -lt $lines.Count; $i++) {
    $l = $lines[$i]
    $ms = [long][Math]::Round([double]$l.start * 1000)
    $g.Add("amovie=$(Split-Path -Leaf $l.file),$fmt,adelay=${ms}:all=1[v$i]")
    $labels += "[v$i]"
}
if ($lines.Count -eq 1) { $g.Add("[v0]volume=$(F $VoiceGainDb)dB[voice]") }
else { $g.Add("$($labels -join '')amix=inputs=$($lines.Count):normalize=0:duration=longest,volume=$(F $VoiceGainDb)dB[voice]") }

# Duck envelope: for each line a trapezoid (attack ramp, hold, release ramp); summed and
# clamped to 1, then gain = 1 - depth*envelope, so the game drops by exactly DuckDb under a voice.
$depth = 1.0 - [Math]::Pow(10, - $DuckDb / 20.0)
$terms = foreach ($l in $lines) {
    $s = [double]$l.start; $e = [double]$l.end
    "clip((t-$(F ($s - $DuckAttack)))/$(F $DuckAttack),0,1)*clip(($(F ($e + $DuckRelease))-t)/$(F $DuckRelease),0,1)"
}
$envelope = "min(1,$($terms -join '+'))"
if ($hasAudio) {
    $g.Add("[0:a]$fmt,volume='1-$(F $depth)*$envelope':eval=frame[game]")
} else {
    $g.Add("anullsrc=r=48000:cl=stereo,atrim=0:$(F $videoDur),$fmt[game]")
}
$limit = [Math]::Pow(10, $LimitDb / 20.0)
$g.Add("[game][voice]amix=inputs=2:normalize=0:duration=first,alimiter=limit=$(F $limit):level=disabled,$fmt[aout]")

$graphFile = Join-Path $voiceDir 'mix.filtergraph.txt'
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($graphFile, ($g -join ";`n"), $utf8NoBom)

# ---- run ffmpeg ------------------------------------------------------------------
$ffArgs = @('-hide_banner', '-loglevel', 'error', '-y', '-i', $Video,
    '-filter_complex_script', $graphFile,
    '-map', '0:v:0', '-map', '[aout]', '-c:v', 'copy', '-c:a', 'aac', '-b:a', $AudioBitrate,
    '-movflags', '+faststart', $Out)
Push-Location -LiteralPath $voiceDir
try {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $null = Invoke-Native 'ffmpeg' $ffArgs
    Write-Host ("Mixed {0} clips into {1} in {2:F1}s (duck {3} dB, graph: {4})" -f $lines.Count, $Out, $sw.Elapsed.TotalSeconds, $DuckDb, $graphFile)
} finally { Pop-Location }
Write-Output $Out
