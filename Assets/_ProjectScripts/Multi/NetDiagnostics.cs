using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// Les compteurs du réseau, relevés par NetDiagnostics (2026-10-14). De simples entiers, incrémentés là
/// où les choses se passent (PlayerLocomotion), compilés partout, builds comprises : c'est ce qui
/// permet de mesurer une partie jouée sur deux PC, sans l'Editor.
/// </summary>
public static class NetStats
{
    // Chez le joueur qui prédit (client).
    public static int Corrections, Resyncs, StaleCorrections, InputPacketsSent, InputsSent, PosesReceived;
    public static float ErrorMax, RttSum;
    public static int RttSamples;

    // Sur le serveur, pour les joueurs qu'il simule.
    public static int InputPacketsReceived, InputsReceived, InputsLost, UselessPackets, QueueMax;

    public static void Reset()
    {
        Corrections = Resyncs = StaleCorrections = InputPacketsSent = InputsSent = PosesReceived = 0;
        ErrorMax = RttSum = 0f;
        RttSamples = 0;
        InputPacketsReceived = InputsReceived = InputsLost = UselessPackets = QueueMax = 0;
    }
}

/// <summary>
/// Le journal de diagnostic d'une instance du jeu (2026-10-14), dans les builds comme dans l'Editor :
/// toutes les 2 s, ce qu'il faut pour comprendre une partie qui « lague » : images par seconde et pires
/// images, aller-retour réseau (celui du transport et celui des inputs), recalages, paquets envoyés et
/// reçus, inputs perdus, et la fluidité des robots des AUTRES joueurs tels que cette machine les montre
/// (images où un robot qui court ne bouge pas, plus grand saut d'une image à l'autre).
///
/// Écrit dans le journal du joueur (Player.log) et dans un fichier par instance,
/// `persistentDataPath/Diagnostics/&lt;profil&gt;.log` : deux builds lancées sur un même PC ont chacune le
/// sien (le profil est celui de la connexion aux services, « joueur », « joueur_1 »...). Le lancement
/// précédent est gardé en `.prev.log`. Ne lit rien que la simulation relise : catégorie C. Coupé par
/// l'argument de lancement `-nodiag`.
/// </summary>
[DefaultExecutionOrder(10000)] // après tout le monde : les robots ont fini de bouger pour cette image
public class NetDiagnostics : MonoBehaviour
{
    private const float Window = 2f;
    private const float HitchSeconds = 0.05f;

    private static NetDiagnostics instance;

    private StreamWriter file;
    private float windowStart;
    private int frames, hitches, gcAtStart;
    private float worstFrame;
    private string lastConnectionLine;

    private struct RemoteTrack
    {
        public Vector3 last;
        public float speed;
        public int moving, frozen;
        public float maxStep;
    }

    private readonly Dictionary<PlayerLocomotion, RemoteTrack> remotes = new Dictionary<PlayerLocomotion, RemoteTrack>();
    private readonly List<PlayerLocomotion> gone = new List<PlayerLocomotion>();
    private readonly StringBuilder line = new StringBuilder(512);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (instance != null) return;
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-nodiag") >= 0) return;
        // Pas de DontSave : un tel objet survit à la sortie du Play Mode, et l'horloge, qui repart de zéro
        // à l'entrée suivante, le laissait muet plusieurs minutes.
        var go = new GameObject("Diagnostic réseau") { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(go);
        instance = go.AddComponent<NetDiagnostics>();
    }

    private void Awake()
    {
        try
        {
            string folder = Path.Combine(Application.persistentDataPath, "Diagnostics");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, OnlineSession.ProfileName() + ".log");
            string previous = Path.ChangeExtension(path, ".prev.log");
            if (File.Exists(path))
            {
                if (File.Exists(previous)) File.Delete(previous);
                File.Move(path, previous);
            }
            file = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = false };
            Write($"[DIAG] début {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} version={Application.version} " +
                  $"{(Application.isEditor ? "editeur" : "build")} profil={OnlineSession.ProfileName()} " +
                  $"ecran={Screen.currentResolution.width}x{Screen.currentResolution.height}@{Screen.currentResolution.refreshRateRatio.value:F0}Hz " +
                  $"vsync={QualitySettings.vSyncCount} plafond_images={Application.targetFrameRate} cpu={SystemInfo.processorType} gpu={SystemInfo.graphicsDeviceName}");
            Debug.Log($"[DIAG] journal de diagnostic : {path}");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[DIAG] journal de diagnostic impossible : {e.Message}");
        }
        Application.logMessageReceived += OnLog;
        StartWindow();
    }

    private void OnDestroy()
    {
        Application.logMessageReceived -= OnLog;
        file?.Flush();
        file?.Dispose();
        file = null;
        if (instance == this) instance = null;
    }

    /// <summary>Les erreurs et avertissements du jeu vont aussi dans le fichier, avec leur pile.</summary>
    private void OnLog(string message, string stackTrace, LogType type)
    {
        if (file == null || message.StartsWith("[DIAG")) return;
        if (type == LogType.Log && !message.StartsWith("[")) return; // les journaux du jeu, pas ceux d'Unity
        string text = type == LogType.Exception || type == LogType.Error
            ? $"{Time.realtimeSinceStartup:F1} {type} {message}\n{stackTrace}"
            : $"{Time.realtimeSinceStartup:F1} {message}";
        file.WriteLine(text);
    }

    private void Write(string text)
    {
        Debug.Log(text);
        file?.WriteLine(text);
        file?.Flush();
    }

    private void StartWindow()
    {
        windowStart = Time.realtimeSinceStartup;
        frames = hitches = 0;
        worstFrame = 0f;
        gcAtStart = System.GC.CollectionCount(0);
        NetStats.Reset();
        foreach (PlayerLocomotion key in new List<PlayerLocomotion>(remotes.Keys))
        {
            RemoteTrack t = remotes[key];
            t.moving = t.frozen = 0;
            t.maxStep = 0f;
            remotes[key] = t;
        }
    }

    private void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;
        frames++;
        if (dt > worstFrame) worstFrame = dt;
        if (dt > HitchSeconds) hitches++;
        TrackRemotes(Time.deltaTime);
        LogConnectionIfChanged();
        MeasureRelayRegionsOnce();
        if (Time.realtimeSinceStartup < windowStart) StartWindow(); // horloge repartie de zéro
        else if (Time.realtimeSinceStartup - windowStart >= Window) Report();
    }

    private bool relayRegionsMeasured;

    /// <summary>
    /// Une fois connecté aux services : la latence de cette machine vers chaque région de relais, mesurée
    /// par le service d'Unity (deux fois, la mesure est bruitée). Les relais sont chez Google Cloud : mesuré
    /// le 2026-10-14 sur le PC de l'utilisateur, l'UDP vers Google avait 150 à 500 ms de médiane et des
    /// pertes, vers Cloudflare 11 ms. C'est ce qui dit si une partie en ligne peut être fluide d'ici.
    /// </summary>
    private void MeasureRelayRegionsOnce()
    {
        if (relayRegionsMeasured) return;
        if (Unity.Services.Core.UnityServices.State != Unity.Services.Core.ServicesInitializationState.Initialized) return;
        if (!Unity.Services.Authentication.AuthenticationService.Instance.IsSignedIn) return;
        relayRegionsMeasured = true;
        MeasureRelayRegions();
    }

    private async void MeasureRelayRegions()
    {
        for (int run = 1; run <= 2; run++)
        {
            try
            {
                var results = await Unity.Services.Qos.QosService.Instance.GetSortedQosResultsAsync("relay", null);
                if (this == null) return;
                var text = new StringBuilder("[DIAG] relais (mesure ").Append(run).Append(") :");
                foreach (var r in results)
                {
                    if (r.AverageLatencyMs > 5000) continue; // région injoignable
                    text.Append(' ').Append(r.Region).Append('=').Append(r.AverageLatencyMs).Append("ms");
                    if (r.PacketLossPercent > 0f) text.Append('/').Append(r.PacketLossPercent.ToString("F0")).Append('%');
                }
                Write(text.ToString());
            }
            catch (System.Exception e)
            {
                if (this != null) Write($"[DIAG] relais : mesure impossible ({e.Message})");
                return;
            }
            await Awaitable.WaitForSecondsAsync(3f);
            if (this == null) return;
        }
    }

    /// <summary>Les robots des autres joueurs, tels que cette machine les montre : une image où un robot
    /// qui avance (plus de 1 m/s en moyenne) ne bouge pas du tout est une saccade.</summary>
    private void TrackRemotes(float dt)
    {
        if (dt <= 0f) return;
        foreach (PlayerLocomotion player in PlayerLocomotion.SpawnedPlayers)
        {
            if (player == null || !player.IsSpawned || player.IsOwner) continue;
            Vector3 position = player.transform.position;
            if (!remotes.TryGetValue(player, out RemoteTrack t))
            {
                remotes[player] = new RemoteTrack { last = position };
                continue;
            }
            float step = Vector3.Distance(new Vector3(position.x, 0f, position.z), new Vector3(t.last.x, 0f, t.last.z));
            t.last = position;
            if (step > 3f) { remotes[player] = t; continue; } // un replacement, pas une saccade
            t.speed = Mathf.Lerp(t.speed, step / dt, 1f - Mathf.Exp(-dt / 0.15f));
            if (t.speed > 1f)
            {
                t.moving++;
                if (step < 1e-5f) t.frozen++;
                if (step > t.maxStep) t.maxStep = step;
            }
            remotes[player] = t;
        }
        gone.Clear();
        foreach (PlayerLocomotion player in remotes.Keys) if (player == null) gone.Add(player);
        foreach (PlayerLocomotion player in gone) remotes.Remove(player);
    }

    /// <summary>Une fois par connexion : direct ou par les relais, et lequel.</summary>
    private void LogConnectionIfChanged()
    {
        NetworkManager nm = NetworkManager.Singleton;
        string text = "hors ligne";
        if (nm != null && nm.IsListening)
        {
            string role = nm.IsHost ? "hote" : nm.IsServer ? "serveur" : "client";
            string path = "?";
            if (nm.NetworkConfig.NetworkTransport is UnityTransport transport)
            {
                path = transport.Protocol == UnityTransport.ProtocolType.RelayUnityTransport
                    ? "relais " + RelayEndpoint(transport)
                    : $"direct {transport.ConnectionData.Address}:{transport.ConnectionData.Port}";
            }
            text = $"{role} {path} tick={nm.NetworkConfig.TickRate}";
        }
        if (text == lastConnectionLine) return;
        lastConnectionLine = text;
        Write($"[DIAG] connexion : {text}");
    }

    private static string RelayEndpoint(UnityTransport transport)
    {
        try
        {
            var field = typeof(UnityTransport).GetField("m_RelayServerData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            object data = field?.GetValue(transport);
            object endpoint = data?.GetType().GetField("Endpoint")?.GetValue(data);
            return endpoint != null ? endpoint.ToString() : "?";
        }
        catch { return "?"; }
    }

    private void Report()
    {
        float elapsed = Time.realtimeSinceStartup - windowStart;
        NetworkManager nm = NetworkManager.Singleton;
        line.Clear();
        line.Append("[DIAG] t=").Append(Time.realtimeSinceStartup.ToString("F0"))
            .Append(" fps=").Append((frames / elapsed).ToString("F0"))
            .Append(" pire_image=").Append((worstFrame * 1000f).ToString("F0")).Append("ms")
            .Append(" images>50ms=").Append(hitches)
            .Append(" gc=").Append(System.GC.CollectionCount(0) - gcAtStart)
            .Append(" memoire=").Append((System.GC.GetTotalMemory(false) / (1024 * 1024)).ToString()).Append("Mo");

        if (nm != null && nm.IsListening && nm.NetworkConfig.NetworkTransport is UnityTransport transport)
        {
            if (!nm.IsServer)
            {
                // L'aller-retour mesuré sur les inputs (input envoyé → sa correction). Celui du transport, chez
                // le client, se fige : il n'envoie presque plus rien en fiable.
                line.Append(" | client rtt_inputs=").Append(NetStats.RttSamples > 0 ? (NetStats.RttSum / NetStats.RttSamples * 1000f).ToString("F0") : "-").Append("ms")
                    .Append(" recalages=").Append(NetStats.Resyncs).Append('/').Append(NetStats.Corrections)
                    .Append(" erreur_max=").Append((NetStats.ErrorMax * 100f).ToString("F1")).Append("cm")
                    .Append(" envois=").Append((NetStats.InputPacketsSent / elapsed).ToString("F0")).Append("/s")
                    .Append(" inputs_par_envoi=").Append(NetStats.InputPacketsSent > 0 ? ((float)NetStats.InputsSent / NetStats.InputPacketsSent).ToString("F1") : "-")
                    .Append(" corrections_perimees=").Append(NetStats.StaleCorrections)
                    .Append(" poses_recues=").Append((NetStats.PosesReceived / elapsed).ToString("F0")).Append("/s");
            }
            else
            {
                line.Append(" | serveur paquets_inputs=").Append((NetStats.InputPacketsReceived / elapsed).ToString("F0")).Append("/s")
                    .Append(" inputs=").Append((NetStats.InputsReceived / elapsed).ToString("F0")).Append("/s")
                    .Append(" perdus=").Append(NetStats.InputsLost)
                    .Append(" sans_neuf=").Append(NetStats.UselessPackets)
                    .Append(" file_max=").Append(NetStats.QueueMax);
                foreach (ulong id in nm.ConnectedClientsIds)
                {
                    if (id == NetworkManager.ServerClientId) continue;
                    line.Append(" rtt_client").Append(id).Append('=').Append(transport.GetCurrentRtt(id)).Append("ms");
                }
            }
        }

        foreach (KeyValuePair<PlayerLocomotion, RemoteTrack> pair in remotes)
        {
            if (pair.Key == null || pair.Value.moving == 0) continue;
            line.Append(" | robot ").Append(pair.Key.OwnerClientId)
                .Append(" immobile=").Append((100f * pair.Value.frozen / pair.Value.moving).ToString("F0")).Append('%')
                .Append(" saut_max=").Append((pair.Value.maxStep * 100f).ToString("F0")).Append("cm");
        }

        Write(line.ToString());
        StartWindow();
    }
}
