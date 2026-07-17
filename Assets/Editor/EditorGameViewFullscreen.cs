// Assets/Editor/EditorGameViewFullscreen.cs
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
#endif

public static class EditorGameViewFullscreen
{
    /// <summary>Maximise ou restaure la Game View dans l'éditeur.</summary>
    public static void SetMaximize(bool on)
    {
        var gv = GetGameView();
        if (gv == null)
        {
            // Ouvre la Game View si elle n'est pas visible
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            gv = GetGameView();
        }

        if (gv != null)
        {
            gv.maximized = on;   // "plein écran" façon éditeur
            gv.Focus();
        }
    }

    /// <summary>Bascule l'état Maximize de la Game View.</summary>
    [MenuItem("Tools/Game View/Toggle Maximize _F11")]
    public static void ToggleMaximizeMenu()
    {
        var gv = GetGameView();
        if (gv == null)
        {
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            gv = GetGameView();
        }

        if (gv != null)
        {
            gv.maximized = !gv.maximized;
            gv.Focus();
        }
    }

    static EditorWindow GetGameView()
    {
        var type = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
        return EditorWindow.GetWindow(type);
    }
}

