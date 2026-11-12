<#
.SYNOPSIS
  One-shot dubbing: synthesizes the commentary script and mixes it onto the video.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\commentary\dub.ps1 -Video in.mp4 -Script script.json -Out out.mp4
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Video,
    [Parameter(Mandatory = $true)][string]$Script,
    [Parameter(Mandatory = $true)][string]$Out,
    # Folder for the WAV clips + manifest. Default: <Out without extension>_voices
    [string]$WorkDir,
    [double]$DuckDb = 6.0,
    [double]$Gap = 0.15
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$fullOut = [System.IO.Path]::GetFullPath($Out)
if (-not $WorkDir) {
    $WorkDir = Join-Path (Split-Path -Parent $fullOut) ([System.IO.Path]::GetFileNameWithoutExtension($fullOut) + '_voices')
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$manifest = & (Join-Path $PSScriptRoot 'Synthesize.ps1') -Script $Script -OutDir $WorkDir -Gap $Gap | Select-Object -Last 1
$result = & (Join-Path $PSScriptRoot 'Mix.ps1') -Video $Video -Manifest $manifest -Out $fullOut -DuckDb $DuckDb | Select-Object -Last 1
Write-Host ("Done in {0:F1}s: {1}" -f $sw.Elapsed.TotalSeconds, $result)
