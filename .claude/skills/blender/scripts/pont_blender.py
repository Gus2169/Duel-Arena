"""Pont local entre Claude Code et une instance de Blender ouverte.

Lancé à chaque ouverture de Blender par pont_demarrage.py, installé dans le dossier de
démarrage de Blender (voir ce fichier). Sans lui, à lancer une fois par session de
Blender, dans la console Python (onglet Scripting) :

    exec(open(r"D:\\Documents\\Unity\\FPS Claude\\.claude\\skills\\blender\\scripts\\pont_blender.py", encoding="utf-8").read())

Le pont écoute sur 127.0.0.1 seulement (jamais sur le réseau) et n'exécute que les
requêtes qui portent le jeton tiré à son lancement, écrit dans
%LOCALAPPDATA%\\ClaudeBlender\\pont.json (lu par blender_exec.py). Le code reçu
s'exécute dans le fil principal de Blender, par une minuterie : c'est le seul
endroit où bpy est sûr. Il survit à l'ouverture d'un autre fichier, et relancer le
script remplace l'ancien pont.

Arrêt : bpy.app.driver_namespace["pont_claude"].arreter()
"""

import contextlib
import io
import json
import os
import queue
import secrets
import socket
import threading
import time
import traceback

import bpy
from bpy.app.handlers import persistent

PORT = 9877
TAILLE_MAX_REQUETE = 32 * 1024 * 1024
TAILLE_MAX_SORTIE = 512 * 1024
FICHE = os.environ.get("CLAUDE_PONT_FICHE") or os.path.join(
    os.environ.get("LOCALAPPDATA", os.path.expanduser("~")), "ClaudeBlender", "pont.json")
DOSSIER_FICHE = os.path.dirname(FICHE)


def contexte_3d():
    """Arguments de bpy.context.temp_override pour la première vue 3D ouverte.

    Une minuterie n'a ni fenêtre ni zone : les opérateurs qui en demandent une
    (mode édition, dépliage UV, vue) s'appellent dans ce contexte.
    """
    wm = bpy.context.window_manager
    for fenetre in wm.windows:
        for zone in fenetre.screen.areas:
            if zone.type == "VIEW_3D":
                region = next((r for r in zone.regions if r.type == "WINDOW"), None)
                return {"window": fenetre, "screen": fenetre.screen, "area": zone, "region": region}
    fenetre = wm.windows[0]
    return {"window": fenetre, "screen": fenetre.screen}


class PontClaude:
    def __init__(self, port=PORT):
        self.port = port
        self.jeton = secrets.token_hex(24)
        self.taches = queue.Queue()
        self.espace = {"bpy": bpy, "__name__": "__pont_claude__", "contexte_3d": contexte_3d}
        self.actif = True
        self.serveur = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        if hasattr(socket, "SO_EXCLUSIVEADDRUSE"):
            # Windows : personne d'autre ne peut se lier au même port.
            self.serveur.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
        self.serveur.bind(("127.0.0.1", port))
        self.serveur.listen(4)
        self.serveur.settimeout(0.5)
        self.fil = threading.Thread(target=self._ecouter, name="pont_claude", daemon=True)
        self.fil.start()

        def minuterie():
            return self._executer_taches()

        self._minuterie = minuterie
        bpy.app.timers.register(minuterie, first_interval=0.1, persistent=True)

        @persistent
        def apres_chargement(*_):
            # Blender vide driver_namespace à chaque ouverture de fichier, le pont
            # continue (minuterie persistante) : sans sa référence, ni l'arrêter ni le
            # remplacer (le port resterait pris).
            bpy.app.driver_namespace["pont_claude"] = self

        self._apres_chargement = apres_chargement
        bpy.app.handlers.load_post.append(apres_chargement)
        self._ecrire_fiche()
        print(f"[Pont Claude] à l'écoute sur 127.0.0.1:{port}")

    # --- fil d'écoute : ne touche jamais à bpy -------------------------------

    def _ecouter(self):
        while self.actif:
            try:
                connexion, _ = self.serveur.accept()
            except socket.timeout:
                continue
            except OSError:
                break
            threading.Thread(target=self._servir, args=(connexion,), daemon=True).start()

    def _servir(self, connexion):
        with connexion:
            try:
                connexion.settimeout(30)
                tampon = bytearray()
                while not tampon.endswith(b"\n"):
                    morceau = connexion.recv(65536)
                    if not morceau:
                        return
                    tampon += morceau
                    if len(tampon) > TAILLE_MAX_REQUETE:
                        raise ValueError("requête trop longue")
                requete = json.loads(tampon.decode("utf-8"))
                if not secrets.compare_digest(str(requete.get("jeton", "")), self.jeton):
                    reponse = {"ok": False, "erreur": "jeton refusé"}
                else:
                    delai = float(requete.get("delai", 120))
                    tache = {"requete": requete, "fini": threading.Event(), "reponse": None}
                    self.taches.put(tache)
                    if tache["fini"].wait(delai):
                        reponse = tache["reponse"]
                    else:
                        reponse = {"ok": False, "erreur": f"délai dépassé ({delai:.0f} s) : la tâche continue dans Blender"}
                connexion.settimeout(60)
                connexion.sendall(json.dumps(reponse, ensure_ascii=False).encode("utf-8") + b"\n")
            except Exception as exc:  # une requête malformée ne doit pas tuer le pont
                try:
                    connexion.sendall(json.dumps({"ok": False, "erreur": repr(exc)}).encode("utf-8") + b"\n")
                except OSError:
                    pass

    # --- fil principal de Blender --------------------------------------------

    def _executer_taches(self):
        if not self.actif:
            return None
        try:
            tache = self.taches.get_nowait()
        except queue.Empty:
            return 0.05
        tache["reponse"] = self._executer(tache["requete"])
        tache["fini"].set()
        return 0.0

    def _executer(self, requete):
        code = requete.get("code", "")
        if requete.get("fichier"):
            self.espace["__file__"] = requete["fichier"]
        self.espace["ARGS"] = list(requete.get("args", []))
        sortie = io.StringIO()
        debut = time.perf_counter()
        reponse = {"ok": True}
        nom = requete.get("fichier") or "<claude>"
        try:
            expression = compile(code, nom, "eval")  # une expression seule renvoie sa valeur
        except SyntaxError:
            expression = None
        try:
            with contextlib.redirect_stdout(sortie), contextlib.redirect_stderr(sortie):
                if expression is None:
                    exec(compile(code, nom, "exec"), self.espace)
                else:
                    valeur = eval(expression, self.espace)
                    if valeur is not None:
                        reponse["valeur"] = repr(valeur)
        except BaseException:  # SystemExit compris : un script ne ferme pas Blender
            reponse["ok"] = False
            reponse["erreur"] = traceback.format_exc()
        texte = sortie.getvalue()
        if len(texte) > TAILLE_MAX_SORTIE:
            texte = texte[:TAILLE_MAX_SORTIE] + "\n[… sortie tronquée]"
        reponse["sortie"] = texte
        reponse["duree"] = round(time.perf_counter() - debut, 3)
        self._redessiner()
        return reponse

    @staticmethod
    def _redessiner():
        try:
            for fenetre in bpy.context.window_manager.windows:
                for zone in fenetre.screen.areas:
                    zone.tag_redraw()
        except Exception:
            pass

    # --- cycle de vie ---------------------------------------------------------

    def _ecrire_fiche(self):
        os.makedirs(DOSSIER_FICHE, exist_ok=True)
        fiche = {"port": self.port, "jeton": self.jeton, "pid": os.getpid(),
                 "blender": bpy.app.version_string, "lance_a": time.strftime("%Y-%m-%d %H:%M:%S")}
        temporaire = FICHE + ".tmp"
        with open(temporaire, "w", encoding="utf-8") as f:
            json.dump(fiche, f)
        os.replace(temporaire, FICHE)

    def arreter(self):
        self.actif = False
        try:
            self.serveur.close()
        except OSError:
            pass
        try:
            with open(FICHE, encoding="utf-8") as f:
                a_nous = json.load(f).get("jeton") == self.jeton
            if a_nous:  # fermé d'abord : Windows ne supprime pas un fichier ouvert
                os.remove(FICHE)
        except (OSError, ValueError):
            pass
        if bpy.app.timers.is_registered(self._minuterie):
            bpy.app.timers.unregister(self._minuterie)
        if self._apres_chargement in bpy.app.handlers.load_post:
            bpy.app.handlers.load_post.remove(self._apres_chargement)
        print("[Pont Claude] arrêté")


def lancer(port=PORT):
    ancien = bpy.app.driver_namespace.get("pont_claude")
    if ancien is not None:
        ancien.arreter()
        time.sleep(0.6)  # le temps que l'ancien fil lâche le port
    pont = PontClaude(port)
    bpy.app.driver_namespace["pont_claude"] = pont
    return pont


if not bpy.app.background:  # en arrière-plan (tests), on appelle lancer() soi-même
    lancer()
