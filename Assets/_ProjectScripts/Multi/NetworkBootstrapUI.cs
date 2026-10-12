using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Posé sur le MÊME GameObject que le NetworkManager, dans la scène MainMenu (persistant d'une
/// scène à l'autre, voir CLAUDE.md « Le menu principal »).
///
/// Ne gère plus les boutons Host/Server/Client (remplacés par MainMenuController, écran « Jouer »
/// de la scène MainMenu) ni la caméra de secours (chaque scène gère désormais la sienne — voir
/// ArenaFallbackCamera dans MultiTestScene). Ne reste ici que ce qui a vocation à vivre là où vit
/// le NetworkManager : les logs de connexion/déconnexion, et le banc de test à deux instances
/// ci-dessous.
/// </summary>
public class NetworkBootstrapUI : MonoBehaviour
{
#if UNITY_EDITOR
    // ------------------------------------------------------------------
    // BANC DE TEST À DEUX INSTANCES — Editor uniquement, jamais embarqué en build.
    //
    // L'instance virtuelle de Multiplayer Play Mode ne se pilote pas depuis l'Editor principal :
    // sans ces interrupteurs, observer le côté CLIENT exige quelqu'un pour cliquer « Client » et
    // pour bouger. L'Editor principal les écrit dans un petit fichier (SetCloneTestBench), le
    // clone le relit à chaque entrée en Play Mode. Éteints par défaut : le flux normal ne change pas.
    //
    // 🚨 Pas dans les EditorPrefs (2026-10-08). Ils étaient d'abord lus là, puisque les deux
    // processus partagent le même registre. Mais le clone ne voyait pas les changements : registre
    // à 1, clone qui lisait 0. Il tournait depuis le matin et a vraisemblablement gardé en mémoire
    // les valeurs lues à son démarrage. Le 2026-10-06, le banc n'avait marché que parce que le clone
    // venait de redémarrer.
    // ------------------------------------------------------------------

    /// <summary>Fichier des interrupteurs, dans le dossier local de l'utilisateur : hors du
    /// projet (rien à importer ni à committer) et commun aux deux processus.</summary>
    public static string CloneTestBenchFile => System.IO.Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "DuelArena", "banc-de-test.txt");

    /// <summary>Appelé depuis l'Editor principal, AVANT d'entrer en Play Mode. Tout à false pour
    /// éteindre le banc — à faire après chaque mesure, sinon la session suivante de l'utilisateur
    /// se connecte et tire toute seule.</summary>
    public static void SetCloneTestBench(bool autoClient, bool autopilot, bool autofire, bool autotaser = false,
                                         bool autosneak = false, bool autoaim = false,
                                         bool autodrill = false, bool cheatsprint = false, bool autoswitch = false,
                                         bool autovault = false, bool cheatvault = false)
    {
        string path = CloneTestBenchFile;
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        System.IO.File.WriteAllLines(path, new[]
        {
            "autoClient=" + (autoClient ? 1 : 0),
            "autopilot=" + (autopilot ? 1 : 0),
            "autofire=" + (autofire ? 1 : 0),
            "autotaser=" + (autotaser ? 1 : 0),
            "autosneak=" + (autosneak ? 1 : 0),
            "autoaim=" + (autoaim ? 1 : 0),
            "autodrill=" + (autodrill ? 1 : 0),
            "cheatsprint=" + (cheatsprint ? 1 : 0),
            "autoswitch=" + (autoswitch ? 1 : 0),
            "autovault=" + (autovault ? 1 : 0),
            "cheatvault=" + (cheatvault ? 1 : 0),
        });
    }

    private static bool ReadCloneTestFlag(string[] lines, string key)
    {
        foreach (string line in lines)
        {
            if (line.Trim() == key + "=1") return true;
        }
        return false;
    }

    private void Start()
    {
        if (!IsVirtualPlayerClone()) return;

        string path = CloneTestBenchFile;
        string[] lines = System.IO.File.Exists(path) ? System.IO.File.ReadAllLines(path) : new string[0];

        // Affecté dans les DEUX sens : le rechargement de domaine est désactivé à l'entrée en Play
        // Mode dans ce projet, donc un static allumé lors d'une session y resterait à la suivante.
        PlayerInputReader.AutopilotStrafe = ReadCloneTestFlag(lines, "autopilot");
        bool autofire = ReadCloneTestFlag(lines, "autofire");
        PlayerInputReader.AutopilotFire = autofire;
        WeaponController.AutopilotAimAtOpponent = autofire;
        TaserController.AutopilotStrike = ReadCloneTestFlag(lines, "autotaser");
        // Va-et-vient en sneak et en visée (2026-10-09) : l'allure où les pieds glissaient.
        PlayerInputReader.AutopilotSneak = ReadCloneTestFlag(lines, "autosneak");
        PlayerInputReader.AutopilotAim = ReadCloneTestFlag(lines, "autoaim");
        // Exercice du sprint face au rechargement et au taser (2026-10-10), honnête ou tricheur.
        PlayerInputReader.AutopilotSprintDrill = ReadCloneTestFlag(lines, "autodrill");
        PlayerLocomotion.DebugSprintIgnoresRules = ReadCloneTestFlag(lines, "cheatsprint");
        // La panoplie qui tourne (2026-10-11) : changements d'arme, chargeurs par arme, gerbe du pompe.
        WeaponController.AutopilotWeaponCycle = ReadCloneTestFlag(lines, "autoswitch");
        // Le franchissement en tirant (2026-10-11), honnête ou tricheur.
        PlayerInputReader.AutopilotVaultDrill = ReadCloneTestFlag(lines, "autovault");
        PlayerLocomotion.DebugVaultIgnoresRules = ReadCloneTestFlag(lines, "cheatvault");

        NetworkManager nm = NetworkManager.Singleton;
        if (ReadCloneTestFlag(lines, "autoClient") && nm != null && !nm.IsClient && !nm.IsServer)
        {
            // Le Host n'existe peut-être pas encore : UnityTransport réessaie de se connecter
            // pendant une minute, ce qui laisse le temps de le démarrer dans l'Editor principal.
            Debug.Log($"[Test] Instance virtuelle : connexion automatique en client (va-et-vient={PlayerInputReader.AutopilotStrafe}, sneak={PlayerInputReader.AutopilotSneak}, visée={PlayerInputReader.AutopilotAim}, tir={autofire}, taser={TaserController.AutopilotStrike}, exercice sprint={PlayerInputReader.AutopilotSprintDrill}, triche sprint={PlayerLocomotion.DebugSprintIgnoresRules}, panoplie={WeaponController.AutopilotWeaponCycle}).");
            nm.StartClient();
        }
    }

    private static bool IsVirtualPlayerClone()
        => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "--virtual-project-clone") >= 0;
#endif

    private void OnEnable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
        }
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
        }
    }

    private void HandleClientConnected(ulong clientId)
    {
        Debug.Log($"[Netcode] Client connecté : id={clientId}.");
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        Debug.Log($"[Netcode] Client déconnecté : id={clientId}.");
    }
}
