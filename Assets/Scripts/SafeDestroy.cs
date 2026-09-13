using UnityEngine;

// Destruction d'objets créés À L'EXÉCUTION (cartes d'artefact, vignettes de traits, dés
// d'ajout…) sans polluer la console de l'éditeur.
//
// Le problème : si l'objet détruit est SÉLECTIONNÉ dans l'Inspector, l'éditeur tente de se
// rouvrir sur une cible devenue nulle et lève
//     SerializedObjectNotCreatableException: Object at index 0 is null
// une fois par inspecteur concerné (GameObjectInspector, RectTransformEditor, et
// TMP_EditorPanelUI dès que l'objet porte un texte TextMeshPro). Rien n'est cassé côté jeu —
// l'erreur n'existe qu'en éditeur — mais elle noie la console et masque les vraies erreurs.
//
// La parade est de désélectionner l'objet juste avant de le détruire. Le code de jeu, lui,
// ne change pas : en build, tout ce bloc disparaît.
public static class SafeDestroy
{
    public static void Now(GameObject go)
    {
        if (!go) return;
        ClearEditorSelection(go);
        Object.Destroy(go);
    }

    // Variante pour un composant : détruit l'objet qui le porte.
    public static void Now(Component c)
    {
        if (c) Now(c.gameObject);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    static void ClearEditorSelection(GameObject go)
    {
#if UNITY_EDITOR
        // Sélection multiple comprise : il suffit qu'UN des objets sélectionnés soit l'objet
        // détruit (ou l'un de ses enfants) pour que l'inspecteur se casse.
        var selected = UnityEditor.Selection.objects;
        if (selected == null || selected.Length == 0) return;

        var root = go.transform;
        foreach (var o in selected)
        {
            var g = o as GameObject;
            if (!g) continue;
            if (g == go || g.transform.IsChildOf(root))
            {
                UnityEditor.Selection.objects = new Object[0];
                return;
            }
        }
#endif
    }
}
