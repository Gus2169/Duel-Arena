using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// L'oreille du joueur (OneShotAudio.ActiveListener, 2026-10-13) : c'est d'elle que dépend le
/// sifflement d'une balle qui passe. L'arène compte plusieurs écouteurs, tous éteints sauf un (la caméra
/// de secours, la caméra de chaque robot) ; FindAnyObjectByType rendait la caméra de secours, éteinte,
/// et aucune balle ne sifflait (vu au banc à deux instances).
/// </summary>
public class OneShotAudioPlayModeTests
{
    [UnityTest]
    public IEnumerator LOreille_EstToujoursUnEcouteurAllume_EtSuitCeluiQuiLEst()
    {
        yield return null; // les écouteurs d'un test précédent finissent d'être détruits
        var secours = new GameObject("Caméra de secours (éteinte)").AddComponent<AudioListener>();
        secours.enabled = false;
        var joueur = new GameObject("Caméra du joueur").AddComponent<AudioListener>();
        try
        {
            yield return null;
            AudioListener ear = OneShotAudio.ActiveListener;
            Assert.IsNotNull(ear, "Garde : un écouteur est allumé.");
            Assert.IsTrue(ear.isActiveAndEnabled, "Jamais un écouteur éteint.");
            Assert.AreNotSame(secours, ear);

            // Les rôles s'échangent (une caméra s'éteint, une autre s'allume) : l'oreille suit.
            joueur.enabled = false;
            secours.enabled = true;
            yield return null;
            ear = OneShotAudio.ActiveListener;
            Assert.IsNotNull(ear);
            Assert.IsTrue(ear.isActiveAndEnabled, "Jamais un écouteur éteint, même gardé en mémoire.");
            Assert.AreNotSame(joueur, ear);
        }
        finally
        {
            Object.Destroy(secours.gameObject);
            Object.Destroy(joueur.gameObject);
        }
    }
}
