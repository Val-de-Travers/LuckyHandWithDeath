using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Empêche la sélection « fantôme » qui pollue la console au lancement du Play mode :
//
//     SerializedObjectNotCreatableException: Object at index 0 is null
//       UnityEditor.GameObjectInspector.OnEnable ()
//       UnityEditor.RectTransformEditor.OnEnable ()
//       TMPro.EditorUtilities.TMP_EditorPanelUI.OnEnable ()
//
// D'où ça vient : Unity MÉMORISE la sélection à travers les bascules Edit ↔ Play. Si l'objet
// sélectionné avait été créé À L'EXÉCUTION (carte d'artefact, vignette de trait, dé d'ajout,
// fantôme de placement…), il n'existe plus une fois la partie arrêtée — mais son identifiant
// reste dans la sélection. À chaque nouvel appui sur Play, l'Inspector tente de reconstruire
// ses éditeurs sur cette cible nulle et lève l'exception : une fois par éditeur concerné, d'où
// le triplet GameObject / RectTransform / TextMeshPro. L'erreur revient à CHAQUE lancement tant
// que la sélection n'est pas nettoyée — elle n'a rien à voir avec le déroulement du jeu.
//
// Le correctif : à chaque bascule, on retire de la sélection les entrées devenues nulles. Une
// sélection valide (un objet de la scène sauvegardée) n'est jamais touchée : on ne perd donc
// pas l'objet qu'on était en train d'inspecter.
[InitializeOnLoad]
static class DanglingSelectionGuard
{
    static DanglingSelectionGuard()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        // ⚠️ Point clé : au moment où l'on PEUT encore agir (juste avant la bascule), l'objet
        // sélectionné est encore VIVANT — le purger « s'il est mort » ne fait donc rien, et il
        // meurt ensuite pendant le rechargement de scène, l'Inspector se reconstruisant dessus
        // avant tout rappel suivant. C'était le défaut de la version précédente.
        //
        // On lâche donc la sélection AVANT chaque bascule, sans attendre qu'elle soit morte.
        if (state == PlayModeStateChange.ExitingEditMode ||
            state == PlayModeStateChange.ExitingPlayMode)
        {
            DropSceneSelection();
            return;
        }

        // Après la bascule : filet pour une entrée morte qui aurait malgré tout survécu.
        PruneDestroyedSelection();
    }

    // Abandonne la sélection des objets DE SCÈNE (ceux que la bascule va détruire), en
    // conservant celle des ASSETS — prefabs, ScriptableObjects… — qui, eux, y survivent.
    // On ne perd donc que ce qui allait de toute façon disparaître.
    static void DropSceneSelection()
    {
        var objs = Selection.objects;
        if (objs == null || objs.Length == 0) return;

        var kept = new List<Object>(objs.Length);
        foreach (var o in objs)
            if (o && EditorUtility.IsPersistent(o)) kept.Add(o);

        if (kept.Count == objs.Length) return;   // que des assets : rien à faire

        Selection.objects = kept.ToArray();
        if (kept.Count == 0) Selection.activeObject = null;
    }

    static void PruneDestroyedSelection()
    {
        // On s'en tient à Selection.objects / Selection.count, stables d'une version d'Unity à
        // l'autre : les API à base d'identifiants ont été renommées en 6000.5 (instanceIDs →
        // entityIds), et s'appuyer dessus lierait ce garde-fou à une version précise.
        var alive = Selection.objects;
        if (alive == null) return;

        // Selection.objects n'expose que les objets encore VIVANTS, tandis que Selection.count
        // compte les entrées réellement sélectionnées : un écart signale des cibles mortes —
        // exactement celles sur lesquelles l'Inspector va tenter de se reconstruire.
        bool hasDead = alive.Length != Selection.count;

        // Filet supplémentaire : certaines versions laissent des trous nuls dans le tableau.
        var kept = new List<Object>(alive.Length);
        foreach (var o in alive) if (o) kept.Add(o);
        if (kept.Count != alive.Length) hasDead = true;

        if (!hasDead) return;   // sélection saine : on n'y touche pas

        // Réécrire la sélection à partir des seuls objets vivants purge les entrées mortes.
        Selection.objects = kept.ToArray();
        if (kept.Count == 0) Selection.activeObject = null;
    }
}
