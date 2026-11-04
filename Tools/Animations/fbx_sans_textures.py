# Réécrit un FBX binaire sans le contenu de ses nœuds Video (les textures embarquées), sans rien changer d'autre.
# Tout le reste de l'arbre est recopié octet pour octet ; seuls les décalages de fin de nœud sont recalculés.
# Usage : python fbx_sans_textures.py entree.fbx sortie.fbx
import struct, sys

MAGIC = b"Kaydara FBX Binary  \x00\x1a\x00"


class Node:
    __slots__ = ("name", "props", "num_props", "children", "had_null")


def read_node(data, pos, wide):
    """Rend (noeud ou None, position suivante)."""
    if wide:
        end, num, plen = struct.unpack_from("<QQQ", data, pos)
        pos += 24
    else:
        end, num, plen = struct.unpack_from("<III", data, pos)
        pos += 12
    nlen = data[pos]
    pos += 1
    if end == 0:
        return None, pos
    n = Node()
    n.name = data[pos:pos + nlen]
    pos += nlen
    n.num_props = num
    n.props = data[pos:pos + plen]
    pos += plen
    n.children = []
    n.had_null = False
    while pos < end:
        child, npos = read_node(data, pos, wide)
        if child is None:
            n.had_null = True
            pos = npos
            break
        n.children.append(child)
        pos = npos
    assert pos == end, (n.name, pos, end)
    return n, pos


def header_size(wide, nlen):
    return (24 if wide else 12) + 1 + nlen


def size_of(n, wide):
    s = header_size(wide, len(n.name)) + len(n.props)
    for c in n.children:
        s += size_of(c, wide)
    if n.had_null:
        s += header_size(wide, 0)
    return s


def write_node(out, n, pos, wide):
    end = pos + size_of(n, wide)
    if wide:
        out.append(struct.pack("<QQQ", end, n.num_props, len(n.props)))
    else:
        out.append(struct.pack("<III", end, n.num_props, len(n.props)))
    out.append(bytes([len(n.name)]))
    out.append(n.name)
    out.append(n.props)
    p = pos + header_size(wide, len(n.name)) + len(n.props)
    for c in n.children:
        p = write_node(out, c, p, wide)
    if n.had_null:
        out.append(b"\x00" * header_size(wide, 0))
        p += header_size(wide, 0)
    assert p == end
    return end


def strip_videos(n, stats):
    for c in n.children:
        if n.name == b"Video" and c.name == b"Content":
            stats["octets"] += len(c.props)
            stats["videos"] += 1
            # Une propriété vide du même type (R : tableau d'octets brut).
            c.props = b"R" + struct.pack("<I", 0)
            c.num_props = 1
        strip_videos(c, stats)


def parse(path):
    data = open(path, "rb").read()
    assert data[:23] == MAGIC, "pas un FBX binaire"
    version = struct.unpack_from("<I", data, 23)[0]
    wide = version >= 7500
    pos = 27
    nodes = []
    while True:
        n, pos = read_node(data, pos, wide)
        if n is None:
            break
        nodes.append(n)
    return data, version, wide, nodes, pos


def main():
    src, dst = sys.argv[1], sys.argv[2]
    data, version, wide, nodes, end_top = parse(src)
    print("version", version, "noeuds racine", [x.name.decode() for x in nodes], "fin de l'arbre", end_top, "taille", len(data))
    stats = {"octets": 0, "videos": 0}
    for n in nodes:
        strip_videos(n, stats)
    print("textures retirees", stats)

    out = [data[:27]]
    p = 27
    for n in nodes:
        p = write_node(out, n, p, wide)
    # Le nœud nul de fin de l'arbre.
    out.append(b"\x00" * header_size(wide, 0))
    p += header_size(wide, 0)
    # Le pied du fichier : un bloc de 16 octets, un bourrage jusqu'à un multiple de 16, puis la version et sa signature.
    tail = data[end_top:]
    # Le bourrage dépend de la position : on recale pour garder l'alignement sur 16 octets.
    footer_id = tail[:16]
    i = 16
    while i < len(tail) and tail[i] == 0:
        i += 1
    rest = tail[i:]
    out.append(footer_id)
    out.append(bytes(4))
    p += 20
    pad = ((p + 15) & ~15) - p
    if pad == 0:
        pad = 16
    out.append(bytes(pad))
    out.append(rest)
    open(dst, "wb").write(b"".join(out))
    print("ecrit", dst, sum(len(x) for x in out), "octets ; pied", len(tail), "bourrage", i - 16, "->", pad)

    # Relecture : même arbre, hors contenu des vidéos.
    d2, v2, w2, n2, e2 = parse(dst)

    def same(a, b, path=""):
        assert a.name == b.name, path
        assert a.num_props == b.num_props, path + a.name.decode()
        assert a.props == b.props, path + a.name.decode()
        assert len(a.children) == len(b.children), path
        for x, y in zip(a.children, b.children):
            same(x, y, path + a.name.decode() + "/")
    assert len(nodes) == len(n2)
    for a, b in zip(nodes, n2):
        same(a, b)
    print("relu, arbre identique")


main()
