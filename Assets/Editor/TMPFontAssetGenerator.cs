using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

// Génère les TMP Font Assets (atlas SDF) à partir de fichiers .ttf/.otf.
//
// TextMeshPro ne sait pas utiliser un .ttf directement : il lui faut un « Font Asset » qui
// contient l'atlas des glyphes. Cet outil évite de passer par le Font Asset Creator fenêtre
// par fenêtre pour chaque police.
//
// Utilisation : sélectionner dans le Project un ou plusieurs .ttf/.otf (ou des DOSSIERS qui
// en contiennent), puis Tools > Cosmic Wimpout > Générer les TMP Font Assets (sélection).
// Chaque police produit un « <nom> SDF.asset » à côté du .ttf d'origine.
public static class TMPFontAssetGenerator
{
    // Réglages d'atlas. Mode Dynamic : les glyphes sont ajoutés à la demande, donc pas besoin
    // de lister les caractères à l'avance (accents français inclus automatiquement).
    const int SAMPLING_POINT_SIZE = 90;
    const int ATLAS_PADDING = 9;
    const int ATLAS_SIZE = 1024;

    const string MENU = "Tools/Cosmic Wimpout/Générer les TMP Font Assets (sélection)";

    [MenuItem(MENU)]
    static void GenerateFromSelection()
    {
        var fontPaths = CollectSelectedFontPaths();

        if (fontPaths.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Aucune police sélectionnée",
                "Sélectionne dans la fenêtre Project un ou plusieurs fichiers .ttf / .otf, " +
                "ou des dossiers qui en contiennent, puis relance la commande.",
                "OK");
            return;
        }

        int created = 0, skipped = 0;
        try
        {
            for (int i = 0; i < fontPaths.Count; i++)
            {
                string path = fontPaths[i];
                EditorUtility.DisplayProgressBar(
                    "Génération des TMP Font Assets",
                    Path.GetFileName(path),
                    (float)i / fontPaths.Count);

                if (CreateFontAssetFor(path)) created++;
                else skipped++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[TMP Font Assets] {created} police(s) générée(s), {skipped} ignorée(s). " +
                  "Assigne le nouvel asset « … SDF » dans le champ Font Asset de tes TMP_Text.");
    }

    // Récupère les .ttf/.otf de la sélection (fichiers ET dossiers, récursivement).
    static List<string> CollectSelectedFontPaths()
    {
        var paths = new List<string>();

        foreach (string guid in Selection.assetGUIDs)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) continue;

            if (AssetDatabase.IsValidFolder(path))
            {
                foreach (string g in AssetDatabase.FindAssets("t:Font", new[] { path }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(g);
                    if (IsFontFile(p) && !paths.Contains(p)) paths.Add(p);
                }
            }
            else if (IsFontFile(path) && !paths.Contains(path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    static bool IsFontFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".ttf" || ext == ".otf";
    }

    static bool CreateFontAssetFor(string fontPath)
    {
        var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
        if (font == null)
        {
            Debug.LogWarning($"[TMP Font Assets] Police illisible, ignorée : {fontPath}");
            return false;
        }

        string dir = Path.GetDirectoryName(fontPath).Replace('\\', '/');
        string assetName = Path.GetFileNameWithoutExtension(fontPath) + " SDF";
        string outPath = $"{dir}/{assetName}.asset";

        // On ne réécrit pas un asset déjà généré (évite de casser les références existantes).
        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(outPath) != null)
        {
            Debug.Log($"[TMP Font Assets] Déjà présent, ignoré : {outPath}");
            return false;
        }

        var fontAsset = TMP_FontAsset.CreateFontAsset(
            font,
            SAMPLING_POINT_SIZE,
            ATLAS_PADDING,
            GlyphRenderMode.SDFAA,
            ATLAS_SIZE,
            ATLAS_SIZE,
            AtlasPopulationMode.Dynamic,
            enableMultiAtlasSupport: true);

        if (fontAsset == null)
        {
            Debug.LogError($"[TMP Font Assets] Échec de génération pour {fontPath}");
            return false;
        }

        fontAsset.name = assetName;
        AssetDatabase.CreateAsset(fontAsset, outPath);

        // L'atlas et le material doivent être stockés DANS l'asset, sinon ils sont perdus
        // au rechargement (le Font Asset apparaîtrait vide / en rose).
        if (fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length > 0 && fontAsset.atlasTextures[0] != null)
        {
            fontAsset.atlasTextures[0].name = assetName + " Atlas";
            AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[0], fontAsset);
        }
        if (fontAsset.material != null)
        {
            fontAsset.material.name = assetName + " Material";
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
        }

        EditorUtility.SetDirty(fontAsset);
        Debug.Log($"[TMP Font Assets] Généré : {outPath}", fontAsset);
        return true;
    }
}
