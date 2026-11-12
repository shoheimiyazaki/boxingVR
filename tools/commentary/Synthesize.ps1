<#
.SYNOPSIS
  Synthesizes Japanese two-speaker commentary lines (announcer / commentator) to WAV files
  with Windows OneCore voices (WinRT SpeechSynthesizer), and writes a manifest with the
  actual start time and duration of each line. Lines never overlap: a line that would run
  into the next one pushes the next one later (order is kept).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Synthesize.ps1 -Script script.json -OutDir out\voices
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Script,
    [Parameter(Mandatory = $true)][string]$OutDir,
    # Voice name fragments matched against the voice Id / DisplayName (first match wins).
    [string[]]$AnnouncerVoice = @('IchiroM', 'Ichiro'),
    [string[]]$CommentatorVoice = @('AyumiM', 'Ayumi', 'HarukaM', 'Haruka'),
    # Minimum silence between two consecutive lines (seconds).
    [double]$Gap = 0.15,
    # Trailing silence below this level is cut, leaving TailSeconds of release.
    [double]$SilenceDb = -50,
    [double]$TailSeconds = 0.08
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

# Prosody per speaker. "excited" overrides the announcer/commentator base when set on a line.
$Prosody = @{
    announcer          = @{ rate = '+15%'; pitch = '+5%';  volume = '+0%' }
    announcer_excited  = @{ rate = '+35%'; pitch = '+12%'; volume = '+20%' }
    commentator        = @{ rate = '-3%';  pitch = '-2%';  volume = '+0%' }
    commentator_excited = @{ rate = '+10%'; pitch = '+4%'; volume = '+10%' }
}

# ---- WinRT bootstrap -------------------------------------------------------
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Media.SpeechSynthesis.SpeechSynthesizer, Windows.Media.SpeechSynthesis, ContentType = WindowsRuntime]
$null = [Windows.Media.SpeechSynthesis.SpeechSynthesisStream, Windows.Media.SpeechSynthesis, ContentType = WindowsRuntime]

$AsTaskGeneric = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and
    $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
} | Select-Object -First 1

function Wait-WinRt($op, [Type]$resultType) {
    $task = $AsTaskGeneric.MakeGenericMethod($resultType).Invoke($null, @($op))
    $null = $task.Wait(-1)
    return $task.Result
}

function Find-Voice([string[]]$fragments) {
    $all = [Windows.Media.SpeechSynthesis.SpeechSynthesizer]::AllVoices
    foreach ($f in $fragments) {
        $v = $all | Where-Object { $_.Id -like "*$f*" -or $_.DisplayName -like "*$f*" } | Select-Object -First 1
        if ($v) { return $v }
    }
    $names = ($all | ForEach-Object { $_.DisplayName }) -join ', '
    throw "No voice matches [$($fragments -join ', ')]. Installed: $names"
}

function Escape-Xml([string]$s) { return [System.Security.SecurityElement]::Escape($s) }

# Parses a PCM WAV (fmt + data chunks) from bytes.
function Read-Wav([byte[]]$bytes) {
    if ($bytes.Length -lt 12 -or [System.Text.Encoding]::ASCII.GetString($bytes, 0, 4) -ne 'RIFF') { throw 'Synthesizer did not return a RIFF/WAV stream' }
    $pos = 12; $fmt = $null
    while ($pos + 8 -le $bytes.Length) {
        $id = [System.Text.Encoding]::ASCII.GetString($bytes, $pos, 4)
        $size = [int][BitConverter]::ToUInt32($bytes, $pos + 4)
        $body = $pos + 8
        if ($id -eq 'fmt ') {
            $fmt = @{
                format   = [BitConverter]::ToUInt16($bytes, $body)
                channels = [BitConverter]::ToUInt16($bytes, $body + 2)
                rate     = [BitConverter]::ToUInt32($bytes, $body + 4)
                align    = [BitConverter]::ToUInt16($bytes, $body + 12)
                bits     = [BitConverter]::ToUInt16($bytes, $body + 14)
            }
        } elseif ($id -eq 'data') {
            if (-not $fmt) { throw 'WAV without fmt chunk' }
            $len = [Math]::Min($size, $bytes.Length - $body)
            return @{ fmt = $fmt; dataOffset = $body; dataLength = $len - ($len % $fmt.align) }
        }
        $pos = $body + $size + ($size % 2)
    }
    throw 'WAV without data chunk'
}

# Writes PCM data as a canonical 44-byte-header WAV; returns the duration in seconds.
function Write-Wav([string]$path, $fmt, [byte[]]$bytes, [int]$offset, [int]$length) {
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $ascii = [System.Text.Encoding]::ASCII
    $bw.Write($ascii.GetBytes('RIFF')); $bw.Write([uint32](36 + $length)); $bw.Write($ascii.GetBytes('WAVE'))
    $bw.Write($ascii.GetBytes('fmt ')); $bw.Write([uint32]16)
    $bw.Write([uint16]$fmt.format); $bw.Write([uint16]$fmt.channels); $bw.Write([uint32]$fmt.rate)
    $bw.Write([uint32]($fmt.rate * $fmt.align)); $bw.Write([uint16]$fmt.align); $bw.Write([uint16]$fmt.bits)
    $bw.Write($ascii.GetBytes('data')); $bw.Write([uint32]$length)
    $bw.Write($bytes, $offset, $length)
    $bw.Flush()
    [System.IO.File]::WriteAllBytes($path, $ms.ToArray())
    $bw.Dispose()
    return $length / [double]($fmt.rate * $fmt.align)
}

# The OneCore voices append ~0.6-0.8 s of silence; trimming it keeps lines from being
# pushed later than necessary. Only 16-bit PCM is trimmed (what OneCore returns).
function Get-TrimmedLength($wav, [byte[]]$bytes, [double]$thresholdDb, [double]$tailSeconds) {
    $f = $wav.fmt
    if ($f.format -ne 1 -or $f.bits -ne 16) { return $wav.dataLength }
    $threshold = [int](32768 * [Math]::Pow(10, $thresholdDb / 20.0))
    $end = $wav.dataOffset + $wav.dataLength - 2
    $last = -1
    for ($p = $end; $p -ge $wav.dataOffset; $p -= 2) {
        if ([Math]::Abs([int][BitConverter]::ToInt16($bytes, $p)) -gt $threshold) { $last = $p; break }
    }
    if ($last -lt 0) { return $wav.dataLength }
    $keep = ($last - $wav.dataOffset + 2) + [int]($tailSeconds * $f.rate) * $f.align
    $keep -= $keep % $f.align
    return [Math]::Min($wav.dataLength, $keep)
}

# ---- input -----------------------------------------------------------------
if (-not (Test-Path -LiteralPath $Script)) { throw "Script not found: $Script" }
$json = Get-Content -LiteralPath $Script -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $json.PSObject.Properties['lines']) { throw "Script has no 'lines' array: $Script" }
$lines = @($json.lines)
if ($lines.Count -eq 0) { throw "Script has no lines: $Script" }

$i = 0
foreach ($l in $lines) {
    foreach ($k in 'at', 'speaker', 'text') {
        if (-not $l.PSObject.Properties[$k]) { throw "Line #$i is missing '$k'" }
    }
    if ($l.speaker -notin 'announcer', 'commentator') { throw "Line #${i}: speaker must be announcer|commentator (got '$($l.speaker)')" }
    if ([string]::IsNullOrWhiteSpace($l.text)) { throw "Line #${i}: empty text" }
    if ([double]$l.at -lt 0) { throw "Line #${i}: 'at' must be >= 0" }
    $i++
}
# Keep script order but sort by requested time (stable).
$ordered = @($lines | ForEach-Object -Begin { $n = 0 } -Process {
    [pscustomobject]@{ Index = $n; Line = $_ }; $n++
} | Sort-Object -Property @{ Expression = { [double]$_.Line.at } }, Index)

$null = New-Item -ItemType Directory -Force -Path $OutDir
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path

$voices = @{
    announcer   = Find-Voice $AnnouncerVoice
    commentator = Find-Voice $CommentatorVoice
}
foreach ($k in $voices.Keys) { Write-Host ("{0,-12} -> {1} ({2})" -f $k, $voices[$k].DisplayName, $voices[$k].Id) }

$synth = New-Object Windows.Media.SpeechSynthesis.SpeechSynthesizer

# ---- synthesize --------------------------------------------------------------
$results = @()
$cursor = 0.0   # earliest time the next line may start
$seq = 0
foreach ($entry in $ordered) {
    $l = $entry.Line
    $speaker = [string]$l.speaker
    $excited = $false
    if ($l.PSObject.Properties['excited']) { $excited = [bool]$l.excited }
    $key = if ($excited) { "${speaker}_excited" } else { $speaker }
    $p = $Prosody[$key]
    $voice = $voices[$speaker]

    $ssml = "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='$($voice.Language)'>" +
            "<voice name='$(Escape-Xml $voice.DisplayName)'>" +
            "<prosody rate='$($p.rate)' pitch='$($p.pitch)' volume='$($p.volume)'>$(Escape-Xml ([string]$l.text))</prosody>" +
            "</voice></speak>"

    $synth.Voice = $voice
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $stream = Wait-WinRt ($synth.SynthesizeSsmlToStreamAsync($ssml)) ([Windows.Media.SpeechSynthesis.SpeechSynthesisStream])
    $file = Join-Path $OutDir ('{0:D3}_{1}.wav' -f $seq, $speaker)
    $netStream = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($stream)
    $mem = New-Object System.IO.MemoryStream
    try { $netStream.CopyTo($mem) } finally { $netStream.Dispose(); $stream.Dispose() }
    $bytes = $mem.ToArray()
    $sw.Stop()

    $wav = Read-Wav $bytes
    $keep = Get-TrimmedLength $wav $bytes $SilenceDb $TailSeconds
    $duration = Write-Wav $file $wav.fmt $bytes $wav.dataOffset $keep
    $requested = [double]$l.at
    $start = [Math]::Max($requested, $cursor)
    $shift = $start - $requested
    $cursor = $start + $duration + $Gap

    $results += [pscustomobject]@{
        index       = $entry.Index
        speaker     = $speaker
        excited     = $excited
        text        = [string]$l.text
        voice       = $voice.DisplayName
        requestedAt = [Math]::Round($requested, 3)
        start       = [Math]::Round($start, 3)
        duration    = [Math]::Round($duration, 3)
        end         = [Math]::Round($start + $duration, 3)
        shift       = [Math]::Round($shift, 3)
        file        = $file
    }
    $msg = "[{0:D3}] {1,-11} at {2,7:F2}s dur {3,5:F2}s synth {4,5:F2}s" -f $seq, $speaker, $start, $duration, $sw.Elapsed.TotalSeconds
    if ($shift -gt 0.0005) { $msg += (" (shifted +{0:F2}s)" -f $shift) }
    Write-Host $msg
    $seq++
}

$shifted = @($results | Where-Object { $_.shift -gt 0.0005 })
$manifest = [pscustomobject]@{
    source       = (Resolve-Path -LiteralPath $Script).Path
    gap          = $Gap
    voices       = [pscustomobject]@{ announcer = $voices.announcer.DisplayName; commentator = $voices.commentator.DisplayName }
    shiftedCount = $shifted.Count
    totalShift   = [Math]::Round((($shifted | Measure-Object -Property shift -Sum).Sum + 0), 3)
    end          = ($results | Select-Object -Last 1).end
    lines        = $results
}
$manifestPath = Join-Path $OutDir 'manifest.json'
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 5), $utf8NoBom)
Write-Host ("Manifest: {0} ({1} lines, {2} shifted)" -f $manifestPath, $results.Count, $shifted.Count)
Write-Output $manifestPath
