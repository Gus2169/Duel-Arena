using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Le menu d'Échap en partie (2026-10-12, relecture complète) : reprendre, quitter la partie pour
/// revenir au menu principal, ou quitter le jeu. Avant, rien ne permettait de quitter une partie :
/// le curseur restait verrouillé, et seul Alt+F4 en sortait.
///
/// Posé sur le GameObject du NetworkManager, à côté de SessionFlow : il voyage avec la session et ne
/// s'affiche que lorsqu'un joueur local existe. Le jeu ne se met pas en pause (c'est une partie en
/// réseau) : menu ouvert, les commandes du joueur sont coupées, il s'arrête et ne tire plus.
///
/// PLACEHOLDER OnGUI, au même titre que CrosshairUI et l'affichage de RoundManager : à refaire avec
/// le HUD en UI Toolkit. Catégorie C pure.
/// </summary>
[DisallowMultipleComponent]
public class PauseMenu : MonoBehaviour
{
    private bool open;
    private PlayerInputReader localInput;

    // Jamais sérialisés : à chaque recompilation, l'Editor sérialise aussi les champs privés des
    // objets de la scène ouverte et recrée un GUIStyle VIDE pour chacun. Comme le Play Mode ne
    // recharge pas la scène dans ce projet, le style vide paraissait déjà construit : texte minuscule,
    // boutons sans fond (vécu en le testant, 2026-10-12).
    [System.NonSerialized] private GUIStyle titleStyle;
    [System.NonSerialized] private GUIStyle buttonStyle;

    /// <summary>Le menu d'Échap est-il ouvert sur cette machine ?</summary>
    public static bool IsOpen { get; private set; }

    private void Update()
    {
        PlayerInputReader player = LocalPlayerInput();
        if (player == null)
        {
            if (open) Close(null);
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            if (open) Close(player);
            else Open(player);
        }
    }

    private static PlayerInputReader LocalPlayerInput()
    {
        if (SceneManager.GetActiveScene().name == SessionFlow.MenuSceneName) return null;
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsClient || nm.LocalClient == null || nm.LocalClient.PlayerObject == null) return null;
        return nm.LocalClient.PlayerObject.GetComponent<PlayerInputReader>();
    }

    private void Open(PlayerInputReader player)
    {
        open = IsOpen = true;
        localInput = player;
        localInput.SetGameplayEnabled(false);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Close(PlayerInputReader player)
    {
        open = IsOpen = false;
        if (player != null)
        {
            player.SetGameplayEnabled(true);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        localInput = null;
    }

    private void OnDisable()
    {
        IsOpen = false;
    }

    private void OnGUI()
    {
        if (!open) return;
        BuildStylesIfNeeded();
        GUI.depth = -100; // par-dessus le HUD et le décompte de RoundManager

        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float width = Mathf.Max(320f, Screen.width * 0.22f);
        float height = Mathf.Max(56f, Screen.height * 0.065f);
        float x = (Screen.width - width) * 0.5f;
        float y = Screen.height * 0.48f;

        GUI.Label(new Rect(0f, y - height * 1.6f, Screen.width, height), "Pause", titleStyle);
        if (GUI.Button(new Rect(x, y, width, height), "Reprendre (Échap)", buttonStyle)) Close(localInput);
        y += height * 1.25f;
        if (GUI.Button(new Rect(x, y, width, height), "Quitter la partie", buttonStyle))
        {
            Close(null);
            SessionFlow.ReturnToMenu("");
        }
        y += height * 1.25f;
        if (GUI.Button(new Rect(x, y, width, height), "Quitter le jeu", buttonStyle)) Quit();
    }

    private static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // Styles construits une seule fois : OnGUI tourne à chaque image.
    private void BuildStylesIfNeeded()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.Max(32, Screen.height / 14),
            fontStyle = FontStyle.Bold,
        };
        titleStyle.normal.textColor = Color.white;
        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = Mathf.Max(18, Screen.height / 36),
            fontStyle = FontStyle.Bold,
        };
    }
}
