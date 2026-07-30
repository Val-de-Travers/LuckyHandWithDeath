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
    //
    // ⚠️ Padding : un padding trop faible par rapport à la taille d'échantillonnage produit des
    // artefacts SDF — typiquement une « rayure » sous une lettre dont le trait touche le bord de
    // sa case d'atlas (ex : le C de CLAUSE). On garde un ratio confortable (~18 %) et une taille
    // d'échantillonnage élevée pour les polices décoratives (Pirata One, IM Fell, Kings…).
    const int SAMPLING_POINT_SIZE = 100;
    const int ATLAS_PADDING = 18;
    const int ATLAS_SIZE = 2048;

    const string MENU = "Tools/Cosmic Wimpout/Générer les TMP Font Assets (sélection)";
    const string MENU_FORCE = "Tools/Cosmic Wimpout/Régénérer les TMP Font Assets (écrase la sélection)";
    const string MENU_FIX = "Tools/Cosmic Wimpout/Corriger la netteté des Font Assets (in-place, garde le GUID)";

    // Réglages de la correction « in-place » : padding confortable qui supprime les artefacts
    // SDF (la « rayure » sous certaines lettres) sur les polices à empattements.
    const int FIX_PADDING = 12;
    const int FIX_ATLAS = 1024;
    const float FIX_POINT_SIZE = 90f;

    // Corrige les TMP Font Assets SÉLECTIONNÉS sans changer leur GUID : on augmente le padding
    // de l'atlas et on vide les glyphes pour qu'ils soient re-rendus proprement (mode Dynamic).
    // À utiliser sur un asset « … SDF » dont le rendu bave (padding trop faible à la création).
    [MenuItem(MENU_FIX)]
    static void FixSelectedFontAssets()
    {
        var assets = new List<TMP_FontAsset>();
        foreach (var obj in Selection.objects)
        {
            var fa = obj as TMP_FontAsset;
            if (fa != null && !assets.Contains(fa)) assets.Add(fa);
        }

        if (assets.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Aucun Font Asset sélectionné",
                "Sélectionne dans le Project un ou plusieurs TMP Font Assets « … SDF » " +
                "(pas le .ttf), puis relance la commande.",
                "OK");
            return;
        }

        int fixedCount = 0;
        foreach (var fa in assets)
            if (RebuildFontAssetInPlace(fa)) fixedCount++;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[TMP Font Assets] {fixedCount} Font Asset(s) corrigé(s) (padding {FIX_PADDING}). " +
                  "Le GUID est conservé : aucune référence à réassigner. " +
                  "Si un texte reste flou, rouvre la scène pour forcer le re-rendu de l'atlas.");
    }

    // Reconstruit l'atlas d'un Font Asset EXISTANT en place (même objet, même GUID) :
    // vide les glyphes générés avec un padding trop faible, remonte le padding / la taille
    // d'atlas / la taille d'échantillonnage, et remet le material cohérent (gradient scale).
    static bool RebuildFontAssetInPlace(TMP_FontAsset fa)
    {
        if (fa == null) return false;

        // 1) Vider glyphes + atlas (générés avec l'ancien padding).
        fa.ClearFontAssetData(true);

        // 2) Régler padding / atlas / point size / mode dynamique via SerializedObject
        //    (les setters publics n'existent pas tous ; on édite les champs sérialisés).
        var so = new SerializedObject(fa);
        SetInt(so, "m_AtlasPadding", FIX_PADDING);
        SetInt(so, "m_AtlasWidth", FIX_ATLAS);
        SetInt(so, "m_AtlasHeight", FIX_ATLAS);
        SetInt(so, "m_AtlasPopulationMode", 1); // 1 = Dynamic : l'atlas se régénère à la demande
        var ps = so.FindProperty("m_FaceInfo.m_PointSize");
        if (ps != null && ps.propertyType == SerializedPropertyType.Float) ps.floatValue = FIX_POINT_SIZE;
        so.ApplyModifiedPropertiesWithoutUndo();

        // 3) Material : gradient scale = padding + 1, dimensions d'atlas cohérentes.
        var mat = fa.material;
        if (mat != null)
        {
            mat.SetFloat(ShaderUtilities.ID_GradientScale, FIX_PADDING + 1);
            mat.SetFloat(ShaderUtilities.ID_TextureWidth, FIX_ATLAS);
            mat.SetFloat(ShaderUtilities.ID_TextureHeight, FIX_ATLAS);
            EditorUtility.SetDirty(mat);
        }

        EditorUtility.SetDirty(fa);
        Debug.Log($"[TMP Font Assets] Corrigé en place : {AssetDatabase.GetAssetPath(fa)}", fa);
        return true;
    }

    static void SetInt(SerializedObject so, string propertyPath, int value)
    {
        var p = so.FindProperty(propertyPath);
        if (p != null) p.intValue = value;
    }

    // Écrase les assets déjà générés (utile après avoir changé padding / taille d'atlas).
    static bool _overwrite = false;

    [MenuItem(MENU_FORCE)]
    static void RegenerateFromSelection()
    {
        _overwrite = true;
        try { GenerateFromSelection(); }
        finally { _overwrite = false; }
    }

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

        // Par défaut on ne réécrit pas un asset déjà généré (évite de casser les références).
        // Le menu « Régénérer (écrase) » force la réécriture après un changement de réglages.
        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(outPath) != null)
        {
            if (!_overwrite)
            {
                Debug.Log($"[TMP Font Assets] Déjà présent, ignoré : {outPath} " +
                          "(utilise « Régénérer (écrase) » pour le refaire).");
                return false;
            }
            AssetDatabase.DeleteAsset(outPath);
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
