param([string]$Folder = '.testresults/sound_wav')
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Collections.Generic;
public static class SoundWaveMetrics {
    public static string Measure(string path) {
        using (var reader = new BinaryReader(File.OpenRead(path))) {
        reader.BaseStream.Position = 12;
        byte[] pcm = null;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length) {
            string tag = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));
            int size = reader.ReadInt32();
            if (tag == "fmt ") {
                long end = reader.BaseStream.Position + size;
                int format = reader.ReadInt16(), channels = reader.ReadInt16(), rate = reader.ReadInt32();
                reader.ReadInt32(); reader.ReadInt16(); int bits = reader.ReadInt16();
                if (format != 1 || channels != 1 || rate != 44100 || bits != 16) throw new InvalidDataException("Expected 44.1kHz mono PCM16 WAV.");
                reader.BaseStream.Position = end;
            } else if (tag == "data") { pcm = reader.ReadBytes(size); break; }
            else { reader.BaseStream.Position += size; }
            if ((size & 1) != 0) reader.BaseStream.Position++;
        }
        if (pcm == null) throw new InvalidDataException("No PCM data.");
        double sum = 0, peak = 0, window = 0;
        int samples = pcm.Length / 2, onsets = 0, active = 0;
        bool above = false; int last = -100;
        var envelope = new List<string> { "seconds,rms" };
        for (int i = 0; i < samples; i++) {
            double s = BitConverter.ToInt16(pcm, i * 2) / 32768.0;
            sum += s * s; window += s * s; peak = Math.Max(peak, Math.Abs(s));
            if (i % 441 == 440) {
                double rms = Math.Sqrt(window / 441); int bin = i / 441;
                bool now = rms >= 0.01;
                if (now && !above && bin - last >= 6) { onsets++; last = bin; }
                if (now) active++;
                above = now; window = 0;
                envelope.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F2},{1:F6}", bin * 0.01, rms));
            }
        }
        File.WriteAllLines(Path.ChangeExtension(path, ".envelope.csv"), envelope);
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, "| {0} | {1:F3} | {2:F4} | {3:F4} | {4} | {5:F2} |", Path.GetFileNameWithoutExtension(path), samples / 44100.0, peak, Math.Sqrt(sum / samples), onsets, active * 0.01);
        }
    }
}
'@
Write-Output '| Audio | Seconds | Peak | RMS | Envelope onsets | Seconds RMS>=0.01 |'
Write-Output '|---|---:|---:|---:|---:|---:|'
Get-ChildItem -LiteralPath $Folder -Filter '*.wav' | Sort-Object Name | ForEach-Object { [SoundWaveMetrics]::Measure($_.FullName) }
