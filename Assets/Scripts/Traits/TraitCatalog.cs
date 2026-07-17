using System.Collections.Generic;

// Un "Trait" (affixe) : pouvoir permanent attaché à un ennemi, au boss ou au joueur.
// Les traits des ENNEMIS sont portés par leurs champs Affliction (PalierConfig.EnemyInfo :
// afflictionKey / afflictionName / afflictionDescription).
// Les traits BOSS et JOUEUR sont définis ici, en dur.
[System.Serializable]
public class TraitDef
{
    public string key;   // clé technique (minuscules)
    public string name;  // nom affiché
    public string desc;  // règle affichée
    public UnityEngine.Sprite icon; // illustration (optionnelle — sinon lookup via GameManager.traitIcons)

    public TraitDef(string key, string name, string desc, UnityEngine.Sprite icon = null)
    {
        this.key = string.IsNullOrEmpty(key) ? "" : key.Trim().ToLowerInvariant();
        this.name = name;
        this.desc = desc;
        this.icon = icon;
    }
}

public static class TraitCatalog
{
    // ===== Traits BOSS (Palier 5) =====
    public static readonly TraitDef[] BossTraits =
    {
        new TraitDef("boss.obole",    "Obole de Charon",
            "Chaque bank du joueur perd 10% (arrondi)."),
        new TraitDef("boss.blacksun", "Soleil Noir",
            "Au premier flash du joueur, la plus petite face restante devient 2 (une seule fois)."),
        new TraitDef("boss.audit",    "Audit des Morts",
            "1×/match, le Boss annule un artefact joué et le retire de l'inventaire."),
        new TraitDef("boss.tide",     "Marée du Destin",
            "La première relance du joueur dans la manche est annulée : le Boss gagne la meilleure face du jet annulé."),
        new TraitDef("boss.tithe",    "Tithe of Time",
            "Si le joueur ne banque pas en 3 jets consécutifs, +25 requis au score cible."),
    };

    // ===== Traits JOUEUR =====
    public static readonly TraitDef[] PlayerTraits =
    {
        new TraitDef("player.tax",          "Impôts bourgeois",
            "Si les 2 joueurs ont le même score, l'adversaire vous cède 20% de son score actuel."),
        new TraitDef("player.resurrection", "Résurrection",
            "À 3 défaites, lancez le dé SUN : sur 5, 10 ou SUN, vous rejouez la partie perdue."),
    };

    // ===== Clés attendues pour les traits ENNEMIS (champ afflictionKey des assets) =====
    // Le Pendu       : "noose"       — Nœud Coulant : 50% de relancer un dé si aucun dé marquant.
    // La Magicienne  : "runes"       — Runes magiques : 20% tous les 3 tours de faire un flash aléatoire.
    // Le Tavernier   : "happyhour"   — Happy Hour terminé : au-delà de 75 pts sur un tour, bank obligatoire.
    // Le Chasseur    : "tracking"    — Pistage : premier jet de la partie avec 2 dés de 10.
    // La Chevalière  : "estoc"       — Coup d'estoc : relance exceptionnelle du dé perdant d'une Clause.
    // L'Assassin     : "poisonglass" — Verre Empoisonné : 1×/partie, adversaire > 100 pts → score divisé par 2.
    // Le Noble       : "wellborn"    — Bien né : 35 points d'office (ouverture offerte).

    public static TraitDef FindBoss(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        key = key.Trim().ToLowerInvariant();
        foreach (var t in BossTraits) if (t.key == key) return t;
        return null;
    }

    public static TraitDef FindPlayer(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        key = key.Trim().ToLowerInvariant();
        foreach (var t in PlayerTraits) if (t.key == key) return t;
        return null;
    }
}
