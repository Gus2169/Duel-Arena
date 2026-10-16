"""Vitesse moyenne du bassin d'un clip Mixamo (FBX binaire), sans Unity.

Lit les courbes de translation de mixamorig:Hips (AnimationCurveNode "T" relié au modèle Hips) et
rapporte le déplacement horizontal entre la première et la dernière clé à la durée du clip.
Usage : python -I fbxspeed.py fichier.fbx [...]
"""
import struct
import sys
import zlib

KTIME = 46186158000  # unités FBX par seconde


def read_nodes(data, offset, end, wide):
    nodes = []
    while offset < end:
        if wide:
            end_off, nprops, plen = struct.unpack_from('<QQQ', data, offset)
            offset += 24
        else:
            end_off, nprops, plen = struct.unpack_from('<III', data, offset)
            offset += 12
        name_len = data[offset]
        offset += 1
        if end_off == 0:
            break
        name = data[offset:offset + name_len].decode('ascii', 'replace')
        offset += name_len
        props = []
        p = offset
        for _ in range(nprops):
            t = chr(data[p]); p += 1
            if t == 'Y': props.append(struct.unpack_from('<h', data, p)[0]); p += 2
            elif t == 'C': props.append(data[p] != 0); p += 1
            elif t == 'I': props.append(struct.unpack_from('<i', data, p)[0]); p += 4
            elif t == 'F': props.append(struct.unpack_from('<f', data, p)[0]); p += 4
            elif t == 'D': props.append(struct.unpack_from('<d', data, p)[0]); p += 8
            elif t == 'L': props.append(struct.unpack_from('<q', data, p)[0]); p += 8
            elif t in 'fdlib':
                n, enc, clen = struct.unpack_from('<III', data, p); p += 12
                raw = data[p:p + clen]; p += clen
                if enc == 1: raw = zlib.decompress(raw)
                fmt = {'f': 'f', 'd': 'd', 'l': 'q', 'i': 'i', 'b': 'B'}[t]
                props.append(list(struct.unpack('<%d%s' % (n, fmt), raw[:n * struct.calcsize(fmt)])))
            elif t in 'SR':
                n = struct.unpack_from('<I', data, p)[0]; p += 4
                props.append(data[p:p + n]); p += n
            else:
                raise ValueError('type inconnu ' + t)
        offset = p
        children = read_nodes(data, offset, end_off, wide) if offset < end_off else []
        nodes.append((name, props, children))
        offset = end_off
    return nodes


def find(nodes, name):
    for n in nodes:
        if n[0] == name:
            return n
    return None


def speed(path):
    data = open(path, 'rb').read()
    version = struct.unpack_from('<I', data, 23)[0]
    nodes = read_nodes(data, 27, len(data), version >= 7500)
    objects = find(nodes, 'Objects')[2]
    conns = find(nodes, 'Connections')[2]
    by_id = {}
    for n in objects:
        if n[1] and isinstance(n[1][0], int):
            by_id[n[1][0]] = n
    # Le modèle des hanches
    hips = None
    for oid, n in by_id.items():
        if n[0] == 'Model' and b'Hips' in n[1][1]:
            hips = oid
    # AnimationCurveNode "T" relié aux hanches (OP, ..., "Lcl Translation")
    tnode = None
    for c in conns:
        props = c[1]
        if props[0] == b'OP' and props[2] == hips and len(props) > 3 and b'Translation' in props[3]:
            tnode = props[1]
    curves = {}
    for c in conns:
        props = c[1]
        if props[0] == b'OP' and props[2] == tnode:
            curves[props[3]] = by_id[props[1]]
    def keys(curve):
        t = find(curve[2], 'KeyTime')[1][0]
        v = find(curve[2], 'KeyValueFloat')[1][0]
        return t, v
    tx, vx = keys(curves[b'd|X'])
    tz, vz = keys(curves[b'd|Z'])
    duration = (tx[-1] - tx[0]) / KTIME
    dx = (vx[-1] - vx[0]) / 100.0
    dz = (vz[-1] - vz[0]) / 100.0
    import math
    return duration, math.hypot(dx, dz) / duration if duration > 0 else 0.0, math.degrees(math.atan2(dx, dz))


if __name__ == '__main__':
    for f in sys.argv[1:]:
        try:
            d, s, h = speed(f)
            print('%-48s %5.2f s  %5.2f m/s  cap %4.0f°' % (f.split('/')[-1].split('\\')[-1], d, s, h))
        except Exception as e:
            print(f, 'ERREUR', e)
