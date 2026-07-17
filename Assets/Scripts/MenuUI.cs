using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

#if TMP_PRESENT || TEXTMESHPRO_PRESENT
using TMPro;
#endif

#if UNITY_EDITOR
using UnityEditor;
#endif

// NEW INPUT SYSTEM
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;   // Keyboard.current, Gamepad.current, etc.
#endif

public class MenuUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private RectTransform settingsPanel;
    [SerializeField] private Button closeSettingsButton;

    [Header("Fullscreen UI")]
    [SerializeField] private Button fullscreenButton;
#if TMP_PRESENT || TEXTMESHPRO_PRESENT
    [SerializeField] private TMP_Text fullscreenTmpLabel;
#endif
    [SerializeField] private Text fullscreenUiTextLabel;

    [Header("Scene")]
    [SerializeField] private string gameSceneName = "GameScene";

    const string PREF_FULLSCREEN = "pref_fullscreen";
    bool isFullscreen;

    void Awake()
    {
        if (startButton) startButton.onClick.AddListener(StartGame);
        if (settingsButton) settingsButton.onClick.AddListener(() => ToggleSettings(true));
        if (closeSettingsButton) closeSettingsButton.onClick.AddListener(() => ToggleSettings(false));
        if (settingsPanel) settingsPanel.gameObject.SetActive(false);

        // Charger préférence plein écran (1 par défaut)
        isFullscreen = PlayerPrefs.GetInt(PREF_FULLSCREEN, 1) == 1;
        ApplyFullscreen(isFullscreen);

        if (fullscreenButton)
            fullscreenButton.onClick.AddListener(() => ToggleFullscreen());

        RefreshFullscreenLabel();
    }

    void Update()
    {
        // ----- Clavier (Escape pour fermer, F11 pour basculer plein écran)
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (settingsPanel && settingsPanel.gameObject.activeSelf && kb.escapeKey.wasPressedThisFrame)
                ToggleSettings(false);

            // F11
            if (kb.f11Key.wasPressedThisFrame)
                ToggleFullscreen();
        }

        // Optionnel : Gamepad (Start/Options pour ouvrir/fermer les réglages)
        var pad = Gamepad.current;
        if (pad != null && pad.startButton.wasPressedThisFrame)
        {
            bool show = !(settingsPanel && settingsPanel.gameObject.activeSelf);
            ToggleSettings(show);
        }
#else
        // Legacy Input Manager
        if (settingsPanel && settingsPanel.gameObject.activeSelf && Input.GetKeyDown(KeyCode.Escape))
            ToggleSettings(false);
        if (Input.GetKeyDown(KeyCode.F11))
            ToggleFullscreen();
#endif
    }

    void StartGame()
    {
        if (!string.IsNullOrEmpty(gameSceneName))
            SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
    }

    void ToggleSettings(bool show)
    {
        if (settingsPanel) settingsPanel.gameObject.SetActive(show);
    }

    void ToggleFullscreen()
    {
        isFullscreen = !isFullscreen;
        ApplyFullscreen(isFullscreen);
        PlayerPrefs.SetInt(PREF_FULLSCREEN, isFullscreen ? 1 : 0);
        PlayerPrefs.Save();
        RefreshFullscreenLabel();
    }

    void ApplyFullscreen(bool on)
    {
#if UNITY_EDITOR
        // En Play dans l'éditeur : on maximise la Game View (simulateur de plein écran)
        // NB: ne pas référencer UnityEditor ici (ça casserait le build). On utilise reflection safe de l'assembly Editor.
        var editorAsm = System.AppDomain.CurrentDomain.GetAssemblies();
        foreach (var asm in editorAsm)
        {
            if (asm.GetName().Name == "UnityEditor")
            {
                var gameViewType = asm.GetType("UnityEditor.GameView");
                if (gameViewType != null)
                {
                    var getWindow = gameViewType.GetMethod("GetWindow", new System.Type[] { });
                    var gv = getWindow?.Invoke(null, null) as UnityEngine.ScriptableObject;
                    if (gv != null)
                    {
                        var prop = gameViewType.GetProperty("maximized");
                        prop?.SetValue(gv, on, null);
                        var focus = gameViewType.GetMethod("Focus");
                        focus?.Invoke(gv, null);
                    }
                }
                break;
            }
        }
#elif UNITY_STANDALONE
        // En build desktop : vrai plein écran borderless (recommandé)
        Screen.fullScreenMode = on ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        Screen.fullScreen = on;
#else
        Screen.fullScreen = on;
#endif
    }

    void RefreshFullscreenLabel()
    {
        string label = isFullscreen ? "Mode : Plein écran" : "Mode : Fenêtré";
#if TMP_PRESENT || TEXTMESHPRO_PRESENT
        if (fullscreenTmpLabel) fullscreenTmpLabel.text = label;
#endif
        if (fullscreenUiTextLabel) fullscreenUiTextLabel.text = label;
    }
}
