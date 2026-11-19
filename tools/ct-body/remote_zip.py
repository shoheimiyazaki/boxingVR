"""Read single members of a large remote zip over HTTP range requests (no full download).

Zenodo serves the TotalSegmentator archives with range support, so the central directory and one subject's
files can be pulled without downloading 3.2 GB / 23.6 GB. Standard library only.
"""
from __future__ import annotations

import http.client
import io
import time
import urllib.error
import urllib.request
import zipfile

_USER_AGENT = "ct-body-tool/1.0"


class RangeFile(io.RawIOBase):
    """A seekable, read-only file whose bytes come from HTTP range requests. Small reads are served from a block cache."""

    BLOCK = 1 << 20  # 1 MiB per request: the central directory and local headers are read in many small pieces.

    def __init__(self, url: str) -> None:
        super().__init__()
        self._url = url
        self._pos = 0
        self._size = self._probe_size()
        self._cache: dict[int, bytes] = {}

    RETRIES = 5

    def _request(self, start: int, end: int) -> bytes:
        """One range request, retried on truncated or dropped connections (Zenodo sometimes cuts a response short)."""
        last_error: Exception | None = None
        for attempt in range(self.RETRIES):
            req = urllib.request.Request(self._url, headers={"Range": f"bytes={start}-{end}", "User-Agent": _USER_AGENT})
            try:
                with urllib.request.urlopen(req, timeout=120) as resp:
                    if resp.status != 206:
                        raise IOError(f"server ignored the range request (HTTP {resp.status}) for {self._url}")
                    data = resp.read()
                if len(data) == end - start + 1:
                    return data
                last_error = IOError(f"short read {len(data)} of {end - start + 1} bytes")
            except (http.client.IncompleteRead, urllib.error.URLError, ConnectionError, TimeoutError) as error:
                last_error = error
            time.sleep(1.0 + attempt)
        raise IOError(f"range {start}-{end} of {self._url} failed after {self.RETRIES} attempts: {last_error}")

    def _probe_size(self) -> int:
        last_error: Exception | None = None
        for attempt in range(self.RETRIES):
            req = urllib.request.Request(self._url, headers={"Range": "bytes=0-0", "User-Agent": _USER_AGENT})
            try:
                with urllib.request.urlopen(req, timeout=60) as resp:
                    content_range = resp.headers.get("Content-Range", "")
                    if resp.status != 206 or "/" not in content_range:
                        raise IOError(f"range requests not supported for {self._url} (HTTP {resp.status})")
                    return int(content_range.rsplit("/", 1)[1])
            except (urllib.error.URLError, ConnectionError, TimeoutError) as error:
                last_error = error
                time.sleep(1.0 + attempt)
        raise IOError(f"could not reach {self._url}: {last_error}")

    def readable(self) -> bool:
        return True

    def seekable(self) -> bool:
        return True

    def tell(self) -> int:
        return self._pos

    def seek(self, offset: int, whence: int = io.SEEK_SET) -> int:
        base = {io.SEEK_SET: 0, io.SEEK_CUR: self._pos, io.SEEK_END: self._size}[whence]
        self._pos = max(0, base + offset)
        return self._pos

    def _block(self, index: int) -> bytes:
        if index not in self._cache:
            start = index * self.BLOCK
            self._cache[index] = self._request(start, min(start + self.BLOCK, self._size) - 1)
            if len(self._cache) > 64:  # keep memory bounded (this PC has little RAM)
                self._cache.pop(next(iter(self._cache)))
        return self._cache[index]

    def read(self, size: int = -1) -> bytes:
        if size is None or size < 0:
            size = self._size - self._pos
        size = min(size, self._size - self._pos)
        if size <= 0:
            return b""
        if size > 4 * self.BLOCK:  # big member data: bypass the cache, in 8 MiB requests (a retry repeats one piece only)
            step = 8 * self.BLOCK
            data = b"".join(self._request(s, min(s + step, self._pos + size) - 1) for s in range(self._pos, self._pos + size, step))
        else:
            parts = []
            pos, remaining = self._pos, size
            while remaining > 0:
                block = self._block(pos // self.BLOCK)
                offset = pos % self.BLOCK
                chunk = block[offset:offset + remaining]
                parts.append(chunk)
                pos += len(chunk)
                remaining -= len(chunk)
            data = b"".join(parts)
        self._pos += len(data)
        return data

    def readinto(self, buffer) -> int:  # type: ignore[override]
        data = self.read(len(buffer))
        buffer[:len(data)] = data
        return len(data)


def open_remote_zip(url: str) -> zipfile.ZipFile:
    return zipfile.ZipFile(io.BufferedReader(RangeFile(url), buffer_size=1 << 16))
