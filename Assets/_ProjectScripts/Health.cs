using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Composant de vie générique — se pose sur le joueur ET sur la cible de test.
///
/// Depuis le hit registration serveur (voir WeaponController.FireServerRpc), les dégâts ne sont
/// décidés QUE par le serveur — plus question qu'un client applique directement ses propres dégâts
/// sur un objet réseauté. Current devient donc une NetworkVariable écrite uniquement par le serveur,
/// exactement le même principe que la pose publiée par PlayerLocomotion : tout le monde voit la
/// même vie, et ApplyDamage() ne fait plus rien si un client l'appelle sur un objet réseauté (garde
/// IsServer ci-dessous — filet de sécurité, puisqu'en pratique seul WeaponController.FireServerRpc
/// l'appelle désormais).
///
/// Reste utilisable SANS réseau (ex. une cible de test posée dans la scène sans NetworkObject, pour
/// un test solo rapide de TTK) : si aucun NetworkObject n'est spawné dans la hiérarchie, ce script
/// bascule automatiquement sur un champ local classique au lieu de la NetworkVariable (voir
/// IsNetworked ci-dessous) — rien à changer pour ce cas-là.
/// </summary>
public class Health : NetworkBehaviour
{
    /// <summary>Ce qui a porté un coup : une arme, ou le gant du taser (le Gun Game ne compte pas pareil un coup
    /// de gant, GDD § 11, GG5) ; un tir dont une balle ou un plomb a touché la tête (le Tag Team la compte plus, TT6).
    /// Ajouter les valeurs à la fin.</summary>
    public enum DamageCause : byte
    {
        Shot,
        Glove,
        HeadShot,
    }

    /// <summary>Personne n'a porté le coup mortel (pas encore mort, ou mort sans auteur connu).</summary>
    public const ulong NoKiller = ulong.MaxValue;

    [SerializeField] private float maxHealth = 100f;

    private readonly NetworkVariable<float> networkCurrent = new NetworkVariable<float>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private float localCurrent = -1f;

    private bool IsNetworked => NetworkObject != null && NetworkObject.IsSpawned;

    public float Current => IsNetworked ? networkCurrent.Value : localCurrent;
    public float Max => maxHealth;
    public bool IsDead => Current <= 0f;

    /// <summary>(montant, source du dégât). NOTE : en contexte réseauté, "source" vaut toujours null
    /// — la NetworkVariable ne transporte que la valeur de vie, pas qui a tiré. À étoffer plus tard
    /// (ClientRpc dédiée avec NetworkObjectReference du tireur) si un kill-feed/hit marker en a
    /// besoin ; pas nécessaire pour cet incrément.</summary>
    public event Action<float, GameObject> OnDamaged;

    private void Awake()
    {
        localCurrent = maxHealth;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer) networkCurrent.Value = maxHealth;
        networkCurrent.OnValueChanged += HandleNetworkCurrentChanged;
    }

    public override void OnNetworkDespawn()
    {
        networkCurrent.OnValueChanged -= HandleNetworkCurrentChanged;
    }

    private void HandleNetworkCurrentChanged(float previous, float current)
    {
        float delta = previous - current;
        if (delta > 0f) OnDamaged?.Invoke(delta, null);
    }

    /// <summary>Serveur : le joueur qui a porté le coup mortel (son identifiant de client), ou NoKiller. Lu par la
    /// boucle de manches quand elle voit la mort (RoundManager, le Gun Game) ; effacé quand la vie revient.</summary>
    public ulong ServerKillerId { get; private set; } = NoKiller;

    /// <summary>Serveur : ce qui a porté le coup mortel (une arme ou le gant).</summary>
    public DamageCause ServerKillCause { get; private set; }

    /// <summary>Appelée par le serveur (WeaponController.FireServerRpc) en contexte réseauté, ou
    /// directement en local sinon (cible de test sans NetworkObject). Sans effet si un CLIENT
    /// l'appelle sur un objet réseauté : seul le serveur fait autorité sur la vie en multi. `source` est la
    /// racine du tireur, `cause` ce qui a frappé : le serveur retient l'auteur du coup mortel.</summary>
    public void ApplyDamage(float amount, GameObject source = null, DamageCause cause = DamageCause.Shot)
    {
        if (IsDead || amount <= 0f) return;

        if (IsNetworked)
        {
            if (!IsServer) return; // garde-fou anti-triche : voir doc de classe
            networkCurrent.Value = Mathf.Max(0f, networkCurrent.Value - amount);
            if (networkCurrent.Value <= 0f)
            {
                ServerKillerId = source != null && source.TryGetComponent(out NetworkObject killer) && killer.IsSpawned
                    ? killer.OwnerClientId : NoKiller;
                ServerKillCause = cause;
            }
            // HandleNetworkCurrentChanged se charge d'OnDamaged sur TOUTES les instances,
            // y compris celle du serveur lui-même (son propre OnValueChanged se déclenche aussi).
        }
        else
        {
            localCurrent = Mathf.Max(0f, localCurrent - amount);
            OnDamaged?.Invoke(amount, source);
        }
    }

    /// <summary>Pratique pour la cible de test : se relève après un délai au lieu de rester morte.</summary>
    public void ResetHealth()
    {
        ServerKillerId = NoKiller;
        if (IsNetworked)
        {
            if (IsServer) networkCurrent.Value = maxHealth;
        }
        else
        {
            localCurrent = maxHealth;
        }
    }
}
