$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$folder = Join-Path $root 'docs/video'
foreach ($kind in @('glove', 'knuckles', 'sword')) {
    $offset = if ($kind -eq 'sword') { 0 } else { 3.5 }
    $before = Join-Path $folder "sound_v7_${kind}_v6_same_scene.mp4"
    $after = Join-Path $folder "sound_v7_${kind}_after.mp4"
    $out = Join-Path $folder "sound_v7_${kind}_compare.mp4"
    $filter = "[0:v]trim=start=${offset}:duration=12,setpts=PTS-STARTPTS,scale=960:540,setsar=1,drawtext=text='v6':x=30:y=30:fontsize=40:fontcolor=white:box=1:boxcolor=black[v0];[1:v]trim=start=${offset}:duration=12,setpts=PTS-STARTPTS,scale=960:540,setsar=1,drawtext=text='v7':x=30:y=30:fontsize=40:fontcolor=white:box=1:boxcolor=black[v1];[0:a]atrim=start=${offset}:duration=12,asetpts=PTS-STARTPTS[a0];[1:a]atrim=start=${offset}:duration=12,asetpts=PTS-STARTPTS[a1];[v0][a0][v1][a1]concat=n=2:v=1:a=1[v][a]"
    & ffmpeg -y -loglevel error -i $before -i $after -filter_complex $filter -map '[v]' -map '[a]' -c:v libx264 -crf 28 -preset fast -c:a aac -b:a 96k $out
    if ($LASTEXITCODE -ne 0) { throw "Comparison failed: $kind" }
}
