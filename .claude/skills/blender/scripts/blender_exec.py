"""Envoie du code Python au pont Blender (pont_blender.py) et affiche ce qu'il renvoie.

    python -I blender_exec.py --ping
    python -I blender_exec.py -c "len(bpy.data.objects)"
    python -I blender_exec.py -f script.py [--delai 300] [-- arg1 arg2]

Le script reçoit ses arguments dans la variable ARGS et son chemin dans __file__.
Les variables survivent d'un appel à l'autre (même espace de noms).
Code de sortie : 0 si tout s'est bien passé, 1 si le code a levé, 2 si le pont ne répond pas.
"""

import argparse
import json
import os
import socket
import sys

FICHE = os.environ.get("CLAUDE_PONT_FICHE") or os.path.join(
    os.environ.get("LOCALAPPDATA", os.path.expanduser("~")), "ClaudeBlender", "pont.json")


def envoyer(requete, port, delai):
    with socket.create_connection(("127.0.0.1", port), timeout=10) as s:
        s.settimeout(delai + 30)
        s.sendall(json.dumps(requete, ensure_ascii=False).encode("utf-8") + b"\n")
        tampon = bytearray()
        while not tampon.endswith(b"\n"):
            morceau = s.recv(65536)
            if not morceau:
                break
            tampon += morceau
    return json.loads(tampon.decode("utf-8"))


def main():
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    argv = sys.argv[1:]
    args_script = []
    if "--" in argv:
        i = argv.index("--")
        argv, args_script = argv[:i], argv[i + 1:]
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    g = p.add_mutually_exclusive_group(required=True)
    g.add_argument("-f", "--fichier", help="script Python à exécuter dans Blender")
    g.add_argument("-c", "--code", help="code Python (une expression renvoie sa valeur)")
    g.add_argument("--ping", action="store_true", help="vérifie que le pont répond")
    p.add_argument("--delai", type=float, default=120, help="attente maximale en secondes")
    a = p.parse_args(argv)

    try:
        with open(FICHE, encoding="utf-8") as f:
            fiche = json.load(f)
    except OSError:
        print("Pont absent : Blender n'est pas ouvert, ou le pont n'y est pas lancé "
              "(pont_demarrage.py au démarrage, ou pont_blender.py dans la console Python).", file=sys.stderr)
        return 2

    requete = {"jeton": fiche["jeton"], "delai": a.delai, "args": args_script}
    if a.ping:
        requete["code"] = "bpy.app.version_string + ' | ' + (bpy.data.filepath or '(non enregistré)')"
    elif a.code is not None:
        requete["code"] = a.code
    else:
        chemin = os.path.abspath(a.fichier)
        with open(chemin, encoding="utf-8") as f:
            requete["code"] = f.read()
        requete["fichier"] = chemin

    try:
        reponse = envoyer(requete, fiche["port"], a.delai)
    except (OSError, ValueError) as exc:
        print(f"Le pont ne répond pas ({exc}). Blender est-il ouvert, et le pont lancé ?", file=sys.stderr)
        return 2

    if reponse.get("sortie"):
        print(reponse["sortie"], end="" if reponse["sortie"].endswith("\n") else "\n")
    if "valeur" in reponse:
        print(reponse["valeur"])
    if not reponse.get("ok"):
        print(reponse.get("erreur", "erreur inconnue"), file=sys.stderr)
        return 1
    if "duree" in reponse and reponse["duree"] > 1:
        print(f"[{reponse['duree']:.1f} s]", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
