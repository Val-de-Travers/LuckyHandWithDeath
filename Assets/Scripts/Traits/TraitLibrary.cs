using System.Collections.Generic;
using UnityEngine;

// Catalogue de traits (comme EnemyLibrary pour les ennemis).
// Crée DEUX assets : un pour les traits Joueur, un pour les traits Boss.
// Chaque entrée : clé technique + nom + description + icône.
//
// ⚠️ La CLÉ pilote l'effet en jeu (codé en dur côté GameManager). Utilise les clés
// attendues (voir le menu contextuel "Remplir avec les traits par défaut") ; tu peux
// librement changer nom, description et icône.
[CreateAssetMenu(menuName = "Cosmic Wimpout/Trait Library", fileName = "TraitLibrary")]
public class TraitLibrary : ScriptableObject
{
    public enum Kind { Player, Boss }

    [Tooltip("Type de traits contenus (sert au bouton de remplissage par défaut).")]
    public Kind kind = Kind.Player;

    [System.Serializable]
    public class Entry
    {
        public string key;                    // ex: player.tax, boss.obole
        public string traitName;              // nom affiché
        [TextArea] public string description;  // règle affichée
        public Sprite icon;                    // illustration
    }

    public List<Entry> traits = new List<Entry>();

    public List<TraitDef> ToTraitDefs()
    {
        var list = new List<TraitDef>();
        if (traits != null)
            foreach (var e in traits)
                if (e != null && !string.IsNullOrEmpty(e.key))
                    list.Add(new TraitDef(e.key, e.traitName, e.description, e.icon));
        return list;
    }

    // Remplit la liste avec les traits par défaut du code (clés + noms + descriptions),
    // sans écraser les entrées existantes (mise à jour du nom/desc si clé déjà présente,
    // l'icône est préservée). Accessible via clic droit sur l'asset.
    [ContextMenu("Remplir avec les traits par défaut")]
    public void FillDefaults()
    {
        var defs = (kind == Kind.Boss) ? TraitCatalog.BossTraits : TraitCatalog.PlayerTraits;
        if (traits == null) traits = new List<Entry>();

        foreach (var d in defs)
        {
            var existing = traits.Find(e => e != null &&
                !string.IsNullOrEmpty(e.key) && e.key.Trim().ToLowerInvariant() == d.key);
            if (existing != null)
            {
                existing.key = d.key;
                if (string.IsNullOrEmpty(existing.traitName)) existing.traitName = d.name;
                if (string.IsNullOrEmpty(existing.description)) existing.description = d.desc;
            }
            else
            {
                traits.Add(new Entry { key = d.key, traitName = d.name, description = d.desc, icon = null });
            }
        }

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
}
