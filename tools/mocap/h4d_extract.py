# Extract selected members of a remote zip via HTTP Range (persistent connection). Usage: extract.py URL index.json filter_substring outdir
import sys, json, struct, zlib, os, urllib.request, http.client, urllib.parse
url, index, pattern, outdir = sys.argv[1:5]
entries = [x for x in json.load(open(index))["entries"] if pattern in x["name"] and not x["name"].endswith("/")]
entries.sort(key=lambda x: x["offset"])
# resolve redirect once
req = urllib.request.Request(url, method="HEAD", headers={"User-Agent": "zip-extract/1.0"})
class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *a, **k): return None
opener = urllib.request.build_opener(NoRedirect)
try:
    final = opener.open(req).geturl()
except urllib.error.HTTPError as e:
    final = e.headers["Location"]
final = urllib.parse.urljoin(url, final)
u = urllib.parse.urlsplit(final)
conn = http.client.HTTPSConnection(u.netloc, timeout=60)
path = u.path + ("?" + u.query if u.query else "")
def rng(a, b):
    global conn
    for attempt in range(3):
        try:
            conn.request("GET", path, headers={"Range": f"bytes={a}-{b}", "User-Agent": "zip-extract/1.0"})
            r = conn.getresponse(); data = r.read()
            if r.status in (200, 206): return data
            raise RuntimeError(f"status {r.status}")
        except Exception as ex:
            conn.close(); conn = http.client.HTTPSConnection(u.netloc, timeout=60)
            err = ex
    raise err
# batch contiguous entries into chunks of up to 4 MB
i = 0; done = 0
while i < len(entries):
    start = entries[i]["offset"]; j = i; end = start
    while j < len(entries) and entries[j]["offset"] + entries[j]["csize"] + 600 - start < 4_000_000:
        end = entries[j]["offset"] + entries[j]["csize"] + 600; j += 1
    j = max(j, i + 1)
    if j == i + 1: end = entries[i]["offset"] + entries[i]["csize"] + 600
    blob = rng(start, end)
    for x in entries[i:j]:
        o = x["offset"] - start
        assert blob[o:o+4] == b"PK\x03\x04", x["name"]
        nlen, elen = struct.unpack("<HH", blob[o+26:o+30])
        d0 = o + 30 + nlen + elen
        raw = blob[d0:d0 + x["csize"]]
        data = raw if x["method"] == 0 else zlib.decompress(raw, -15)
        dst = os.path.join(outdir, x["name"])
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        open(dst, "wb").write(data)
        done += 1
    i = j
print("extracted", done)
