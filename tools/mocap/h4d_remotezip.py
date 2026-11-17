# Read a remote zip's central directory with HTTP Range requests (no full download). Usage: remotezip.py URL out.json
import sys, json, struct, urllib.request
url, out = sys.argv[1], sys.argv[2]
def rng(a, b):
    req = urllib.request.Request(url, headers={"Range": f"bytes={a}-{b}", "User-Agent": "zip-index/1.0"})
    with urllib.request.urlopen(req) as r:
        return r.read(), r.headers.get("Content-Range")
data, cr = rng(0, 0)
total = int(cr.split("/")[1])
tail, _ = rng(max(0, total - 65536 - 22), total - 1)
base = total - len(tail)
i = tail.rfind(b"PK\x05\x06")
assert i >= 0, "no EOCD"
cd_size, cd_off = struct.unpack("<II", tail[i+12:i+20])
j = tail.rfind(b"PK\x06\x06")
if j >= 0:
    cd_size, cd_off = struct.unpack("<QQ", tail[j+40:j+56])
cd, _ = rng(cd_off, cd_off + cd_size - 1)
entries = []
p = 0
while p + 46 <= len(cd) and cd[p:p+4] == b"PK\x01\x02":
    method = struct.unpack("<H", cd[p+10:p+12])[0]
    csize, usize = struct.unpack("<II", cd[p+20:p+28])
    nlen, elen, clen = struct.unpack("<HHH", cd[p+28:p+34])
    loff = struct.unpack("<I", cd[p+42:p+46])[0]
    name = cd[p+46:p+46+nlen].decode("utf-8", "replace")
    extra = cd[p+46+nlen:p+46+nlen+elen]
    q = 0
    while q + 4 <= len(extra):
        hid, hlen = struct.unpack("<HH", extra[q:q+4])
        if hid == 1:
            vals = extra[q+4:q+4+hlen]; k = 0
            if usize == 0xFFFFFFFF: usize = struct.unpack("<Q", vals[k:k+8])[0]; k += 8
            if csize == 0xFFFFFFFF: csize = struct.unpack("<Q", vals[k:k+8])[0]; k += 8
            if loff == 0xFFFFFFFF: loff = struct.unpack("<Q", vals[k:k+8])[0]; k += 8
        q += 4 + hlen
    entries.append({"name": name, "method": method, "csize": csize, "usize": usize, "offset": loff})
    p += 46 + nlen + elen + clen
json.dump({"total": total, "entries": entries}, open(out, "w"))
print(total, len(entries), cd_size)
