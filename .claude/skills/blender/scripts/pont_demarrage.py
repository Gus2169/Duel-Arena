"""Lance le pont de Claude à chaque ouverture de Blender.

S'installe dans le dossier de démarrage de Blender, sous le même nom :

    %APPDATA%\\Blender Foundation\\Blender\\5.2\\scripts\\startup\\pont_demarrage.py

Blender importe les modules de ce dossier à son lancement et appelle leur register().
Le pont lui-même reste dans le projet (pont_blender.py), relu à chaque lancement : une
retouche du pont vaut dès l'ouverture suivante, sans réinstaller ce fichier.

Rien ne se lance en arrière-plan (blender -b : rendus, exports). Avec deux Blender
ouverts, le premier garde le pont : le port est à lui seul. Pour ne plus lancer le pont
à l'ouverture, supprimer ce fichier du dossier de démarrage.
"""

import os

import bpy

PONT = r"D:\Documents\Unity\FPS Claude\.claude\skills\blender\scripts\pont_blender.py"


def register():
    if bpy.app.background:
        return
    if not os.path.isfile(PONT):
        print(f"[Pont Claude] introuvable : {PONT}")
        return
    try:
        with open(PONT, encoding="utf-8") as f:
            code = compile(f.read(), PONT, "exec")
        exec(code, {"__name__": "__pont_claude__", "__file__": PONT})
    except OSError as exc:  # le port est pris : un autre Blender ouvert a déjà le pont
        print(f"[Pont Claude] non lancé : {exc}")


def unregister():  # « Reload Scripts » (F8) : register() relance un pont neuf
    pont = bpy.app.driver_namespace.pop("pont_claude", None)
    if pont is not None:
        pont.arreter()
