"""Convertit une sélection du Kenney Particle Pack (CC0) en textures RGBA blanches dont l'alpha
est la luminance — teintables par la couleur de particule, utilisables en additif comme en alpha.
Fabrique aussi deux textures procédurales (profil de faisceau, bruit d'interférence)."""
import sys, math
from PIL import Image, ImageOps, ImageFilter
K, OUT = sys.argv[1], sys.argv[2]

def lum(name, size):
    im = Image.open(f"{K}/{name}.png").convert("RGBA")
    # le fond est noir opaque : la luminance porte le motif
    g = ImageOps.grayscale(im.convert("RGB")).resize((size, size), Image.LANCZOS)
    return g

def white_alpha(gray):
    rgba = Image.new("RGBA", gray.size, (255, 255, 255, 0))
    rgba.putalpha(gray)
    return rgba

def single(name, out, size=256):
    white_alpha(lum(name, size)).save(f"{OUT}/{out}.png", optimize=True)

def atlas(names, out, tile=256):
    sheet = Image.new("L", (tile * 2, tile * 2), 0)
    for i, n in enumerate(names):
        # Unity lit les flipbooks de haut en bas, gauche à droite
        sheet.paste(lum(n, tile), ((i % 2) * tile, (i // 2) * tile))
    white_alpha(sheet).save(f"{OUT}/{out}.png", optimize=True)

single("circle_05", "VFX_Glow")
single("star_06", "VFX_Star")
single("trace_01", "VFX_Streak")
single("scorch_02", "VFX_Scorch")
atlas(["spark_01", "spark_02", "spark_03", "spark_04"], "VFX_Crackle_2x2")
atlas(["muzzle_01", "muzzle_02", "muzzle_04", "muzzle_05"], "VFX_Muzzle_2x2")
atlas(["smoke_04", "smoke_05", "smoke_07", "smoke_08"], "VFX_Smoke_2x2")

# Profil de faisceau pour les LineRenderer (arc électrique, traceur) : coeur dense au centre de la
# largeur (axe V), halo doux, uniforme le long de la ligne (axe U).
W, H = 64, 64
beam = Image.new("L", (W, H))
for y in range(H):
    v = abs((y + 0.5) / H * 2 - 1)            # 0 au centre, 1 au bord
    core = max(0.0, 1 - v / 0.22) ** 1.5
    halo = math.exp(-(v / 0.45) ** 2) * 0.55
    a = min(1.0, core + halo) * (1 - v ** 8)
    for x in range(W):
        beam.putpixel((x, y), int(255 * a))
white_alpha(beam).save(f"{OUT}/VFX_Beam.png", optimize=True)
print("ok")
