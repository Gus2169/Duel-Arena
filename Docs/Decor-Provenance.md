# Provenance des ressources du décor

*Ce qui vient d'ailleurs dans le décor des arènes : d'où, sous quelle licence, où ça sert. Tenu à jour à chaque ressource ajoutée (skill `ressources-libres`). Ce qui n'y figure pas est fait par script dans le projet (Blender, Unity).*

## Polices

| Ressource | Auteur | Licence | Source | Récupérée | SHA-256 | Usage |
|---|---|---|---|---|---|---|
| Saira Stencil One (`SairaStencilOne-Regular.ttf`) | The Saira Stencil Project Authors (Omnibus-Type) | SIL Open Font License 1.1 (`Tools/Blender/polices/OFL.txt`) | https://github.com/google/fonts/raw/main/ofl/sairastencilone/SairaStencilOne-Regular.ttf | 2026-10-27 | `781496fdaf8e04cf6741b31025f6b4ba84f66021b097a8e0d85cbea2180cf223` | Les pochoirs du décor (caisses, conteneur, voyant « TIR EN COURS », marquages peints), rastérisés en géométrie par Blender (`Tools/Blender/decor/`). Le fichier de la police n'entre pas dans la build. |
| Bahnschrift | Microsoft (police de Windows) | Licence de Windows | `C:\Windows\Fonts\bahnschrift.ttf` | — | — | Comme pour les armes : de la géométrie de lettres faite dans Blender. Le fichier n'entre ni dans le projet ni dans la build. |

## Textures, modèles

Aucun pour l'instant : les matériaux du décor sont procéduraux (shader `DuelArena/Decor`), les props faits dans Blender.
