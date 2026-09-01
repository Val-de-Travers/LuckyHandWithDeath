using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "CosmicWimpout/Artifact Library", fileName = "ArtifactLibrary")]
public class ArtifactLibrary : ScriptableObject
{
    public List<Artifact> artifacts = new List<Artifact>();

    // ===================== RARETÉ : PROBABILITÉS =====================
    // Sans pondération, le tirage était UNIFORME sur toute la bibliothèque. Comme les raretés
    // élevées y sont presque aussi nombreuses que les communes (12 communs / 5 rares / 5 épiques
    // / 5 légendaires), un légendaire sortait dans près d'UNE OFFRE SUR DEUX.
    // On tire donc d'abord une RARETÉ selon les poids ci-dessous, puis un artefact au hasard
    // DANS cette rareté.
    [Header("Probabilités de rareté (poids relatifs)")]
    [Tooltip("Poids de tirage des artefacts Communs (vert).")]
    [Min(0f)] public float weightCommon = 62f;
    [Tooltip("Poids de tirage des artefacts Rares (bleu).")]
    [Min(0f)] public float weightRare = 25f;
    [Tooltip("Poids de tirage des artefacts Épiques (jaune).")]
    [Min(0f)] public float weightEpic = 10f;
    [Tooltip("Poids de tirage des artefacts Légendaires (rouge).")]
    [Min(0f)] public float weightLegendary = 3f;

    [Tooltip("Empêche d'afficher deux artefacts de MÊME rareté élevée dans une offre " +
             "(jamais deux Épiques ni deux Légendaires côte à côte). Les Communs et Rares, " +
             "eux, peuvent se répéter : les contraindre aussi forcerait une rareté différente " +
             "par carte et ferait au contraire remonter les Épiques/Légendaires.")]
    public bool uniqueHighRarityPerOffer = true;

    [Tooltip("Rareté minimale soumise à la règle ci-dessus (par défaut : Épique).")]
    public ArtifactRarity uniqueFromRarity = ArtifactRarity.Epic;

    float WeightOf(ArtifactRarity r) => r switch
    {
        ArtifactRarity.Common    => weightCommon,
        ArtifactRarity.Rare      => weightRare,
        ArtifactRarity.Epic      => weightEpic,
        ArtifactRarity.Legendary => weightLegendary,
        _ => 0f
    };

    static readonly ArtifactRarity[] AllRarities =
    {
        ArtifactRarity.Common, ArtifactRarity.Rare, ArtifactRarity.Epic, ArtifactRarity.Legendary
    };

    /// <summary>
    /// Tire `count` artefacts DISTINCTS en respectant les probabilités de rareté.
    /// Chaque emplacement tire une rareté sur la table de poids COMPLÈTE (jamais renormalisée :
    /// renormaliser sur les raretés restantes redistribuerait le poids des communs vers les
    /// paliers supérieurs et ferait remonter les légendaires). Si la rareté tirée n'est pas
    /// disponible (épuisée, ou déjà servie et soumise à la règle d'unicité), on se rabat sur la
    /// rareté disponible immédiatement INFÉRIEURE, puis supérieure en dernier recours.
    /// </summary>
    public List<Artifact> GetRandomDistinct(int count, System.Random rng = null)
    {
        var result = new List<Artifact>();
        var pool = artifacts.FindAll(a => a != null);
        if (pool.Count == 0) return result;

        rng ??= new System.Random();
        int n = Mathf.Min(count, pool.Count);

        var chosen = new HashSet<Artifact>();
        var usedRarities = new HashSet<ArtifactRarity>();

        while (result.Count < n)
        {
            // Artefacts encore disponibles, regroupés par rareté.
            var avail = new Dictionary<ArtifactRarity, List<Artifact>>();
            foreach (var a in pool)
            {
                if (chosen.Contains(a)) continue;
                if (!avail.TryGetValue(a.rarity, out var list))
                    avail[a.rarity] = list = new List<Artifact>();
                list.Add(a);
            }
            if (avail.Count == 0) break; // bibliothèque épuisée

            var rarity = ResolveRarity(PickWeightedRarity(rng), avail, usedRarities, rng);
            var bucket = avail[rarity];

            var pick = bucket[rng.Next(0, bucket.Count)];
            result.Add(pick);
            chosen.Add(pick);
            usedRarities.Add(rarity);
        }

        return result;
    }

    // Tirage d'une rareté sur la table de poids complète.
    ArtifactRarity PickWeightedRarity(System.Random rng)
    {
        float total = 0f;
        foreach (var r in AllRarities) total += Mathf.Max(0f, WeightOf(r));

        // Poids tous nuls / mal configurés → repli uniforme.
        if (total <= 0f) return AllRarities[rng.Next(0, AllRarities.Length)];

        double roll = rng.NextDouble() * total;
        foreach (var r in AllRarities)
        {
            roll -= Mathf.Max(0f, WeightOf(r));
            if (roll <= 0d) return r;
        }
        return ArtifactRarity.Common; // sécurité (arrondis flottants)
    }

    // Une rareté est utilisable si elle a encore des artefacts et qu'elle ne viole pas la règle
    // d'unicité des raretés élevées.
    bool IsRarityUsable(ArtifactRarity r,
                        Dictionary<ArtifactRarity, List<Artifact>> avail,
                        HashSet<ArtifactRarity> used)
    {
        if (!avail.ContainsKey(r)) return false;
        if (uniqueHighRarityPerOffer && r >= uniqueFromRarity && used.Contains(r)) return false;
        return true;
    }

    // Rabat la rareté tirée sur la plus proche rareté réellement disponible : d'abord vers le
    // BAS (un légendaire indisponible devient épique, puis rare, puis commun), sinon vers le haut.
    ArtifactRarity ResolveRarity(ArtifactRarity wanted,
                                 Dictionary<ArtifactRarity, List<Artifact>> avail,
                                 HashSet<ArtifactRarity> used,
                                 System.Random rng)
    {
        if (IsRarityUsable(wanted, avail, used)) return wanted;

        for (int r = (int)wanted - 1; r >= 0; r--)
            if (IsRarityUsable((ArtifactRarity)r, avail, used)) return (ArtifactRarity)r;

        for (int r = (int)wanted + 1; r < AllRarities.Length; r++)
            if (IsRarityUsable((ArtifactRarity)r, avail, used)) return (ArtifactRarity)r;

        // Toutes les raretés sont déjà servies : on relâche la contrainte d'unicité plutôt
        // que de rendre une offre incomplète.
        var keys = new List<ArtifactRarity>(avail.Keys);
        return keys[rng.Next(0, keys.Count)];
    }
}
