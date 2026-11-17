import struct, sys

def read(path):
    b = open(path, 'rb').read()
    pstart = b[0]
    first, last = struct.unpack_from('<HH', b, 6)
    dstart, = struct.unpack_from('<H', b, 16)
    p = (pstart - 1) * 512
    if b[p + 3] != 84:
        raise Exception('processor %d' % b[p + 3])
    i = p + 4
    groups = {}
    params = {}
    while True:
        nlen = struct.unpack_from('<b', b, i)[0]
        gid = struct.unpack_from('<b', b, i + 1)[0]
        if nlen == 0:
            break
        name = b[i + 2:i + 2 + abs(nlen)].decode('latin1').upper()
        j = i + 2 + abs(nlen)
        off, = struct.unpack_from('<h', b, j)
        nxt = j + off
        if gid < 0:
            groups[-gid] = name
        else:
            k = j + 2
            t = struct.unpack_from('<b', b, k)[0]
            nd = b[k + 1]
            dims = list(b[k + 2:k + 2 + nd])
            k += 2 + nd
            n = 1
            for d in dims:
                n *= d
            params[(gid, name)] = (t, dims, b[k:k + n * abs(t)])
        if off == 0:
            break
        i = nxt
    g = {v: k for k, v in groups.items()}

    def P(group, name):
        t, dims, raw = params[(g[group], name)]
        if t == -1:
            if len(dims) > 1:
                w = dims[0]
                return [raw[x * w:(x + 1) * w].decode('latin1').strip() for x in range(len(raw) // w)]
            return raw.decode('latin1').strip()
        fmt = {1: 'b', 2: 'h', 4: 'f'}[abs(t)]
        return list(struct.unpack('<%d%s' % (len(raw) // abs(t), fmt), raw))

    labels = P('POINT', 'LABELS')
    for extra in ('LABELS2', 'LABELS3'):
        try:
            labels += P('POINT', extra)
        except KeyError:
            pass
    used = P('POINT', 'USED')[0]
    scale = P('POINT', 'SCALE')[0]
    rate = P('POINT', 'RATE')[0]
    analog_used = 0
    apf = 0
    if 'ANALOG' in g:
        analog_used = P('ANALOG', 'USED')[0]
        if analog_used:
            apf = int(round(P('ANALOG', 'RATE')[0] / rate))
    nframes = last - first + 1
    d = (dstart - 1) * 512
    fl = scale < 0
    frames = []
    for f in range(nframes):
        if fl:
            vals = struct.unpack_from('<%df' % (used * 4), b, d)
            d += used * 16
            pts = [tuple(vals[4 * q:4 * q + 3]) if vals[4 * q + 3] >= 0 else None for q in range(used)]
            d += analog_used * apf * 4
        else:
            vals = struct.unpack_from('<%dh' % (used * 4), b, d)
            d += used * 8
            pts = [tuple(v * scale for v in vals[4 * q:4 * q + 3]) if vals[4 * q + 3] >= 0 else None for q in range(used)]
            d += analog_used * apf * 2
        frames.append(pts)
    return labels[:used], rate, frames

if __name__ == '__main__':
    l, r, f = read(sys.argv[1])
    print(r, len(f))
    print(l)
