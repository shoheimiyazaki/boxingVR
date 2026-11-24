"""Edge のニューラル音声（edge-tts、Microsoft のオンライン音声）で 1 行を読み、16 bit PCM の WAV にする。

Synthesize.ps1 が、話し手の声に "engine": "edge" があるときに 1 行ずつ呼ぶ（英語版の番組の声。2026-11-25）。
Windows の OneCore の声（日本語の 3 人）に英語が無いので、英語の実況・解説はこちらで読む。
台詞の文は Microsoft の音声のサービスへ送られる（ネットが要る）。

  py -3 tools/commentary/edge_say.py --voice en-US-GuyNeural --rate=+10% --pitch=+0Hz --volume=+0% --text-file line.txt --out line.wav
"""
import argparse
import asyncio
import os
import subprocess
import sys
import tempfile

import edge_tts

# 書き出す WAV の形（OneCore の声と同じ 16 bit・モノラル。24 kHz は Edge の声の元の速さ）。
SAMPLE_RATE = 24000
# 一時的な失敗（ネット・サービスが音を返さない "No audio was received"）で、やり直す回数と最初の待ち [s]（待ちは倍々）。
# 2026-11-25、英語版の IVF の番組で台詞が 3 回・6 回とも失敗し、数分後に手でやり直すと通った（サービスの間引き・一時的な不調）。
RETRIES = 8
RETRY_WAIT = 2.0
# 待ちの上限 [s]（倍々で伸ばしても、これより長くは待たない。8 回で合わせて約 2.5 分）。
RETRY_WAIT_MAX = 30.0


async def synthesize(text: str, voice: str, rate: str, pitch: str, volume: str, mp3: str) -> None:
    await edge_tts.Communicate(text, voice, rate=rate, pitch=pitch, volume=volume).save(mp3)


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--voice", required=True)
    p.add_argument("--rate", default="+0%")
    p.add_argument("--pitch", default="+0Hz")
    p.add_argument("--volume", default="+0%")
    # 台詞は UTF-8 のファイルで渡す（--text-file）。Windows PowerShell 5.1 は引数の中の " を正しく渡せず、
    # Marcus "The Hammer" Brooks のような台詞が崩れた（2026-11-25 のレビュー）。--text は手で試す用。
    g = p.add_mutually_exclusive_group(required=True)
    g.add_argument("--text")
    g.add_argument("--text-file")
    p.add_argument("--out", required=True)
    a = p.parse_args()
    if a.text_file:
        with open(a.text_file, encoding="utf-8-sig") as f:
            a.text = f.read().strip()
    if not a.text:
        print("empty text", file=sys.stderr)
        return 1
    fd, mp3 = tempfile.mkstemp(suffix=".mp3")
    os.close(fd)
    try:
        for attempt in range(RETRIES):
            try:
                asyncio.run(synthesize(a.text, a.voice, a.rate, a.pitch, a.volume, mp3))
                if os.path.getsize(mp3) > 0:
                    break
                raise RuntimeError("empty audio")
            except Exception as e:  # ネットの一時的な失敗はやり直す。最後は理由を出して止める
                if attempt == RETRIES - 1:
                    print(f"edge-tts failed ({a.voice}): {e}", file=sys.stderr)
                    return 1
                asyncio.run(asyncio.sleep(min(RETRY_WAIT_MAX, RETRY_WAIT * (2 ** attempt))))
        r = subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", mp3, "-ac", "1", "-ar", str(SAMPLE_RATE), "-c:a", "pcm_s16le", "-f", "wav", a.out],
                           capture_output=True, text=True)
        if r.returncode != 0:
            print(f"ffmpeg failed: {r.stderr}", file=sys.stderr)
            return 1
        return 0
    finally:
        os.remove(mp3)


if __name__ == "__main__":
    sys.exit(main())
