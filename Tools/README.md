# Outils de génération des assets

Hors de `Assets/` : Unity ne les importe pas. Ils servent à régénérer ou retoucher les sons et
les textures d'effets, de façon reproductible (tout est tiré au hasard avec des graines fixes :
relancer un script redonne exactement les mêmes fichiers).

## Sons — `SoundSynth/`

Toute la banque de `Assets/_ProjectArt/Audio/SoundsBank/{Robot,Weapon,Taser,Impacts,Feedback,Round,Ambience}`
est **synthétisée** par `make_sounds.py` : aucun échantillon tiers, donc aucune licence à suivre.

```bash
pip install numpy scipy matplotlib
python3 Tools/SoundSynth/make_sounds.py /tmp/sons          # génère dans /tmp/sons/<Dossier>/
python3 Tools/SoundSynth/analyze.py /tmp/sons /tmp/spectro # niveaux + planches de spectrogrammes
```

Puis copier les `.wav` voulus par-dessus ceux du projet. **Ne pas toucher aux `.meta`** : ils
portent les GUID que référencent `PlayerSounds.asset`, `MP5.asset`, `VfxLibrary.asset` et le
prefab `Player`.

Chaque famille a sa fonction (`footstep`, `servo`, `smg_shot`, `arc_crack`, `taser_pulses`,
`impact_concrete`, `impact_chassis`…). Pour changer le caractère d'un son, c'est là qu'on règle.
Deux contraintes à garder :

- `Taser_Idle_Loop` dure **exactement 2,000 s** et claque **18 fois par seconde** :
  `ElectricArc` cale ses impulsions visuelles sur ce rythme (`pulsesPerSecond`).
- Les boucles (`Taser_Idle_Loop`, `Ambience_Underground_Loop`) sont filtrées **avant** le
  fondu enchaîné qui les rend bouclables ; un filtre appliqué après fait claquer la couture.

## Textures d'effets — `VfxTextures/`

`make_textures.py` convertit une sélection du **Kenney Particle Pack** (CC0, licence jointe) en
textures blanches dont l'alpha est la luminance, et fabrique le profil de faisceau des arcs.
Source : le miroir https://github.com/Calinou/kenney-particle-pack (dossier
`addons/kenney_particle_pack`).

```bash
python3 Tools/VfxTextures/make_textures.py <dossier kenney_particle_pack> Assets/_ProjectArt/VFX/Textures
```
