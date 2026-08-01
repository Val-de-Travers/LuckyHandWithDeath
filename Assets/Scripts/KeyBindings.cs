using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using ISKey = UnityEngine.InputSystem.Key;
#endif

/// <summary>
/// Actions de jeu associables à une touche du clavier.
/// L'ordre sert d'index de stockage : ne pas réordonner.
/// Seules les 3 premières sont associables à une touche ; PalierCircle sert uniquement
/// au tutoriel (attendre le clic du bouton de palier), sans raccourci clavier.
/// </summary>
public enum GameAction { Roll = 0, Bank = 1, EndRound = 2, PalierCircle = 3, CardPicked = 4, ArtifactPlayed = 5, PalierClose = 6 }

/// <summary>
/// Raccourcis clavier des boutons d'action, partagés entre le menu (réglages) et
/// la partie (GameManager). Persistés dans PlayerPrefs sous forme de KeyCode.
///
/// Défauts : Roll = A, Bank = W, EndRound(Next) = D.
/// </summary>
public static class KeyBindings
{
    static readonly KeyCode[] Defaults = { KeyCode.A, KeyCode.W, KeyCode.D };
    static readonly string[] PrefKeys = { "keybind_roll", "keybind_bank", "keybind_endround" };

    static KeyCode[] bindings;

    static void EnsureLoaded()
    {
        if (bindings != null) return;
        bindings = new KeyCode[Defaults.Length];
        for (int i = 0; i < bindings.Length; i++)
            bindings[i] = (KeyCode)PlayerPrefs.GetInt(PrefKeys[i], (int)Defaults[i]);
    }

    // Seules les actions d'index < 3 sont associables à une touche (Roll/Bank/EndRound).
    static bool IsBindable(GameAction a) => (int)a >= 0 && (int)a < 3;

    public static KeyCode Get(GameAction a)
    {
        if (!IsBindable(a)) return KeyCode.None;
        EnsureLoaded();
        return bindings[(int)a];
    }

    /// <summary>
    /// Associe <paramref name="key"/> à l'action. Si une autre action utilise déjà
    /// cette touche, les deux touches sont échangées pour éviter les doublons.
    /// </summary>
    public static void Set(GameAction a, KeyCode key)
    {
        if (!IsBindable(a)) return;
        EnsureLoaded();
        int idx = (int)a;

        for (int i = 0; i < bindings.Length; i++)
        {
            if (i != idx && bindings[i] == key)
            {
                bindings[i] = bindings[idx];          // l'autre action récupère l'ancienne touche
                PlayerPrefs.SetInt(PrefKeys[i], (int)bindings[i]);
            }
        }

        bindings[idx] = key;
        PlayerPrefs.SetInt(PrefKeys[idx], (int)key);
        PlayerPrefs.Save();
    }

    public static void ResetDefaults()
    {
        EnsureLoaded();
        for (int i = 0; i < bindings.Length; i++)
        {
            bindings[i] = Defaults[i];
            PlayerPrefs.SetInt(PrefKeys[i], (int)Defaults[i]);
        }
        PlayerPrefs.Save();
    }

    // ===================== Lecture en jeu =====================

    /// <summary>La touche de l'action a-t-elle été enfoncée durant cette frame ?</summary>
    public static bool WasPressedThisFrame(GameAction a)
    {
        KeyCode key = Get(a);
        if (key == KeyCode.None) return false;

#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return false;
        BuildMaps();
        if (!ToKey.TryGetValue(key, out ISKey k)) return false;
        var ctrl = kb[k];
        return ctrl != null && ctrl.wasPressedThisFrame;
#else
        return Input.GetKeyDown(key);
#endif
    }

    /// <summary>
    /// Capture la touche enfoncée durant cette frame (pour la reconfiguration).
    /// Ignore Échap (annulation) et la souris. Renvoie false si rien de mappable.
    /// </summary>
    public static bool TryCapture(out KeyCode key)
    {
        key = KeyCode.None;

#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return false;
        BuildMaps();
        foreach (var ctrl in kb.allKeys)
        {
            if (!ctrl.wasPressedThisFrame) continue;
            if (ctrl.keyCode == ISKey.Escape) continue;
            if (FromKey.TryGetValue(ctrl.keyCode, out KeyCode mapped))
            {
                key = mapped;
                return true;
            }
        }
        return false;
#else
        if (Input.GetKeyDown(KeyCode.Escape)) return false;
        foreach (KeyCode kc in System.Enum.GetValues(typeof(KeyCode)))
        {
            if (kc == KeyCode.None) continue;
            if (kc >= KeyCode.Mouse0 && kc <= KeyCode.Mouse6) continue;
            if (Input.GetKeyDown(kc)) { key = kc; return true; }
        }
        return false;
#endif
    }

    // ===================== Affichage =====================

    /// <summary>Libellé court à afficher pour une touche (ex. "A", "1", "Espace").</summary>
    public static string Label(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.None:       return "—";
            case KeyCode.Space:      return "Espace";
            case KeyCode.Return:     return "Entrée";
            case KeyCode.Tab:        return "Tab";
            case KeyCode.Backspace:  return "Retour";
            case KeyCode.LeftShift:  return "Maj G";
            case KeyCode.RightShift: return "Maj D";
            case KeyCode.LeftControl:  return "Ctrl G";
            case KeyCode.RightControl: return "Ctrl D";
            case KeyCode.LeftAlt:    return "Alt G";
            case KeyCode.RightAlt:   return "Alt D";
            case KeyCode.UpArrow:    return "↑";
            case KeyCode.DownArrow:  return "↓";
            case KeyCode.LeftArrow:  return "←";
            case KeyCode.RightArrow: return "→";
            case KeyCode.Comma:      return ",";
            case KeyCode.Period:     return ".";
            case KeyCode.Slash:      return "/";
            case KeyCode.Semicolon:  return ";";
            case KeyCode.Minus:      return "-";
            case KeyCode.Equals:     return "=";
        }

        string s = key.ToString();
        if (s.StartsWith("Alpha")) return s.Substring(5);   // Alpha1 -> 1
        if (s.StartsWith("Keypad")) return "Num " + s.Substring(6);
        return s;                                            // lettres, F1..F12, etc.
    }

    // ===================== Correspondance KeyCode <-> Input System =====================

#if ENABLE_INPUT_SYSTEM
    static Dictionary<KeyCode, ISKey> ToKey;
    static Dictionary<ISKey, KeyCode> FromKey;

    static void BuildMaps()
    {
        if (ToKey != null) return;
        ToKey = new Dictionary<KeyCode, ISKey>();

        // Lettres A..Z (contiguës dans les deux énumérations)
        for (int i = 0; i < 26; i++)
            ToKey[KeyCode.A + i] = ISKey.A + i;

        // Chiffres du pavé principal (l'ordre diffère entre les deux énumérations)
        ToKey[KeyCode.Alpha0] = ISKey.Digit0;
        for (int i = 1; i <= 9; i++)
            ToKey[KeyCode.Alpha0 + i] = ISKey.Digit1 + (i - 1);

        // Touches de fonction F1..F12
        for (int i = 0; i < 12; i++)
            ToKey[KeyCode.F1 + i] = ISKey.F1 + i;

        // Touches diverses
        ToKey[KeyCode.Space]        = ISKey.Space;
        ToKey[KeyCode.Tab]          = ISKey.Tab;
        ToKey[KeyCode.Return]       = ISKey.Enter;
        ToKey[KeyCode.Backspace]    = ISKey.Backspace;
        ToKey[KeyCode.LeftShift]    = ISKey.LeftShift;
        ToKey[KeyCode.RightShift]   = ISKey.RightShift;
        ToKey[KeyCode.LeftControl]  = ISKey.LeftCtrl;
        ToKey[KeyCode.RightControl] = ISKey.RightCtrl;
        ToKey[KeyCode.LeftAlt]      = ISKey.LeftAlt;
        ToKey[KeyCode.RightAlt]     = ISKey.RightAlt;
        ToKey[KeyCode.UpArrow]      = ISKey.UpArrow;
        ToKey[KeyCode.DownArrow]    = ISKey.DownArrow;
        ToKey[KeyCode.LeftArrow]    = ISKey.LeftArrow;
        ToKey[KeyCode.RightArrow]   = ISKey.RightArrow;
        ToKey[KeyCode.Comma]        = ISKey.Comma;
        ToKey[KeyCode.Period]       = ISKey.Period;
        ToKey[KeyCode.Slash]        = ISKey.Slash;
        ToKey[KeyCode.Semicolon]    = ISKey.Semicolon;
        ToKey[KeyCode.Minus]        = ISKey.Minus;
        ToKey[KeyCode.Equals]       = ISKey.Equals;

        FromKey = new Dictionary<ISKey, KeyCode>();
        foreach (var kv in ToKey)
            FromKey[kv.Value] = kv.Key;
    }
#endif
}
