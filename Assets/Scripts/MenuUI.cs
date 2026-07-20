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
    [SerializeField] private Button quitButton;

    [Header("Options — Résolution")]
    [Tooltip("Liste déroulante des résolutions supportées par l'écran (remplie en code).")]
    [SerializeField] private TMPro.TMP_Dropdown resolutionDropdown;

    [Header("Options — Audio")]
    [SerializeField] private Slider musicVolumeSlider;
    [SerializeField] private Slider sfxVolumeSlider;

    [Header("Options — Affichage")]
    [Tooltip("Mode d'affichage : Plein écran, Plein écran fenêtré, Fenêtré (options remplies en code).")]
    [SerializeField] private TMPro.TMP_Dropdown displayModeDropdown;

    [Header("Scene")]
    [SerializeField] private string gameSceneName = "GameScene";

    const string PREF_FULLSCREEN = "pref_fullscreen";     // ancien réglage (migration)
    const string PREF_DISPLAY_MODE = "pref_display_mode"; // 0 plein écran, 1 plein écran fenêtré, 2 fenêtré
    const string PREF_RESOLUTION = "pref_resolution";     // "largeurxhauteur"

    // Ordre des entrées du dropdown de mode d'affichage
    static readonly FullScreenMode[] DisplayModes =
    {
        FullScreenMode.ExclusiveFullScreen, // Plein écran
        FullScreenMode.FullScreenWindow,    // Plein écran fenêtré (borderless)
        FullScreenMode.Windowed,            // Fenêtré
    };
    static readonly string[] DisplayModeLabels = { "Plein écran", "Plein écran fenêtré", "Fenêtré" };
    int displayModeIndex = 1;

    // Résolutions proposées (dédoublonnées par largeur×hauteur)
    readonly System.Collections.Generic.List<Vector2Int> resolutionChoices = new();

    void Awake()
    {
        if (startButton) startButton.onClick.AddListener(StartGame);
        if (settingsButton) settingsButton.onClick.AddListener(() => ToggleSettings(true));
        if (closeSettingsButton) closeSettingsButton.onClick.AddListener(() => ToggleSettings(false));
        if (quitButton) quitButton.onClick.AddListener(QuitGame);
        if (settingsPanel) settingsPanel.gameObject.SetActive(false);

        // Mode d'affichage sauvegardé (migration depuis l'ancien réglage plein écran on/off)
        int defaultMode = PlayerPrefs.GetInt(PREF_FULLSCREEN, 1) == 1 ? 1 : 2;
        displayModeIndex = Mathf.Clamp(PlayerPrefs.GetInt(PREF_DISPLAY_MODE, defaultMode), 0, DisplayModes.Length - 1);
        ApplyDisplayMode(displayModeIndex);

        SetupDisplayModeDropdown();
        SetupResolutionDropdown();
        SetupVolumeSliders();
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

            // F11 : bascule Fenêtré ↔ Plein écran fenêtré
            if (kb.f11Key.wasPressedThisFrame)
                ToggleWindowedShortcut();
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
            ToggleWindowedShortcut();
#endif
    }

    void StartGame()
    {
        if (!string.IsNullOrEmpty(gameSceneName))
            SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
    }

    void QuitGame()
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false; // en éditeur : sortir du mode Play
#else
        Application.Quit();
#endif
    }

    // ===================== OPTIONS : RÉSOLUTION =====================

    void SetupResolutionDropdown()
    {
        if (!resolutionDropdown) return;

        // Résolutions supportées, dédoublonnées par largeur×hauteur
        // (les variantes de fréquence d'affichage sont ignorées).
        resolutionChoices.Clear();
        foreach (var r in Screen.resolutions)
        {
            var size = new Vector2Int(r.width, r.height);
            if (!resolutionChoices.Contains(size)) resolutionChoices.Add(size);
        }
        // Secours (éditeur / liste vide) : au moins la résolution actuelle
        var current = new Vector2Int(Screen.width, Screen.height);
        if (resolutionChoices.Count == 0) resolutionChoices.Add(current);

        resolutionDropdown.ClearOptions();
        var labels = new System.Collections.Generic.List<string>();
        foreach (var s in resolutionChoices) labels.Add($"{s.x} × {s.y}");
        resolutionDropdown.AddOptions(labels);

        // Sélection initiale : préférence sauvegardée, sinon la résolution actuelle
        var wanted = current;
        var saved = PlayerPrefs.GetString(PREF_RESOLUTION, "");
        var parts = saved.Split('x');
        if (parts.Length == 2 && int.TryParse(parts[0], out int sw) && int.TryParse(parts[1], out int sh))
            wanted = new Vector2Int(sw, sh);

        int index = resolutionChoices.IndexOf(wanted);
        if (index < 0) index = resolutionChoices.IndexOf(current);
        if (index < 0) index = resolutionChoices.Count - 1;

        resolutionDropdown.SetValueWithoutNotify(index);
        resolutionDropdown.RefreshShownValue();
        resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);

        // Applique la préférence sauvegardée au lancement (no-op dans l'éditeur)
        if (saved != "" && wanted != current && resolutionChoices.Contains(wanted))
            Screen.SetResolution(wanted.x, wanted.y, Screen.fullScreenMode);
    }

    void OnResolutionChanged(int index)
    {
        if (index < 0 || index >= resolutionChoices.Count) return;
        var s = resolutionChoices[index];
        Screen.SetResolution(s.x, s.y, Screen.fullScreenMode);
        PlayerPrefs.SetString(PREF_RESOLUTION, $"{s.x}x{s.y}");
        PlayerPrefs.Save();
    }

    // ===================== OPTIONS : AUDIO =====================

    void SetupVolumeSliders()
    {
        if (musicVolumeSlider)
        {
            musicVolumeSlider.minValue = 0f;
            musicVolumeSlider.maxValue = 1f;
            musicVolumeSlider.SetValueWithoutNotify(AudioManager.SavedMusicVolume);
            musicVolumeSlider.onValueChanged.AddListener(OnMusicVolumeChanged);
        }
        if (sfxVolumeSlider)
        {
            sfxVolumeSlider.minValue = 0f;
            sfxVolumeSlider.maxValue = 1f;
            sfxVolumeSlider.SetValueWithoutNotify(AudioManager.SavedSfxVolume);
            sfxVolumeSlider.onValueChanged.AddListener(OnSfxVolumeChanged);
        }
    }

    void OnMusicVolumeChanged(float v)
    {
        if (AudioManager.Instance) AudioManager.Instance.SetMusicVolume(v);
        else // pas d'AudioManager dans la scène : on persiste quand même le réglage
        {
            PlayerPrefs.SetFloat(AudioManager.PREF_MUSIC_VOLUME, Mathf.Clamp01(v));
            PlayerPrefs.Save();
        }
    }

    void OnSfxVolumeChanged(float v)
    {
        if (AudioManager.Instance) AudioManager.Instance.SetSfxVolume(v);
        else
        {
            PlayerPrefs.SetFloat(AudioManager.PREF_SFX_VOLUME, Mathf.Clamp01(v));
            PlayerPrefs.Save();
        }
    }

    void ToggleSettings(bool show)
    {
        if (settingsPanel) settingsPanel.gameObject.SetActive(show);
    }

    // ===================== OPTIONS : MODE D'AFFICHAGE =====================

    void SetupDisplayModeDropdown()
    {
        if (!displayModeDropdown) return;

        displayModeDropdown.ClearOptions();
        displayModeDropdown.AddOptions(new System.Collections.Generic.List<string>(DisplayModeLabels));
        displayModeDropdown.SetValueWithoutNotify(displayModeIndex);
        displayModeDropdown.RefreshShownValue();
        displayModeDropdown.onValueChanged.AddListener(OnDisplayModeChanged);
    }

    void OnDisplayModeChanged(int index)
    {
        if (index < 0 || index >= DisplayModes.Length) return;
        displayModeIndex = index;
        ApplyDisplayMode(index);
        PlayerPrefs.SetInt(PREF_DISPLAY_MODE, index);
        PlayerPrefs.Save();
    }

    // F11 : bascule rapide Fenêtré ↔ Plein écran fenêtré (synchronise le dropdown)
    void ToggleWindowedShortcut()
    {
        int target = (displayModeIndex == 2) ? 1 : 2;
        if (displayModeDropdown) displayModeDropdown.value = target; // déclenche OnDisplayModeChanged
        else OnDisplayModeChanged(target);
    }

    void ApplyDisplayMode(int index)
    {
        var mode = DisplayModes[Mathf.Clamp(index, 0, DisplayModes.Length - 1)];
#if UNITY_EDITOR
        // En Play dans l'éditeur : pas de vrai changement de mode possible — on maximise
        // la Game View pour les deux modes plein écran (simulateur), via reflection
        // pour ne pas référencer UnityEditor dans un build.
        bool maximize = mode != FullScreenMode.Windowed;
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
                        prop?.SetValue(gv, maximize, null);
                        var focus = gameViewType.GetMethod("Focus");
                        focus?.Invoke(gv, null);
                    }
                }
                break;
            }
        }
#else
        // En build : application directe du mode choisi.
        Screen.fullScreenMode = mode;
#endif
    }
}
