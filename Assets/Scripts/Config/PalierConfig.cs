using UnityEngine;
using UnityEngine.Serialization;
using System;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Cosmic Wimpout/Palier Config", fileName = "PalierConfig")]
public class PalierConfig : ScriptableObject
{
    [Serializable]
    public class EnemyInfo
    {
        public string name;

        [FormerlySerializedAs("portrait")] // garde tes anciennes données
        public Sprite icon;               // ex-portrait -> sert pour Map/HUD/hover

        public Sprite portrait;           // grande image centrale (près de la table)
        public string description;        // hover panel

        [Header("Affliction")]
        public string afflictionName;           // affliction name
        public string afflictionDescription;    // affliction text
        public string afflictionKey;            // affliction clé
    }


    [Serializable]
    public class PalierDef
    {
        public string palierName = "Palier";
        [Min(1)] public int winScore = 300;

        [Tooltip("Nombre d'adversaires prévu pour ce palier (1 à 3). Si 0, on utilise la taille de 'enemies' ou un fallback.")]
        [Range(0,3)] public int expectedEnemyCount = 3;

        [Tooltip("Fallback si aucun roster runtime n'est construit.")]
        public List<EnemyInfo> enemies = new List<EnemyInfo>();
    }

    [Tooltip("Définis ici tes 5 paliers.")]
    public List<PalierDef> paliers = new List<PalierDef>();

    // ==== Roster runtime (généré depuis EnemyLibrary.BuildRoster) ====
    [NonSerialized] private List<List<EnemyInfo>> _runtimeRoster;

    // ---------- API ----------
    public int GetPalierCount()
    {
        return (paliers != null && paliers.Count > 0) ? paliers.Count : 5;
    }

    public PalierDef GetPalier(int index)
    {
        if (paliers == null || paliers.Count == 0) return null;
        index = Mathf.Clamp(index, 0, paliers.Count - 1);
        return paliers[index];
    }

    public int GetWinScore(int palierIndex, int fallback = 300)
    {
        var p = GetPalier(palierIndex);
        return p != null ? Mathf.Max(1, p.winScore) : fallback;
    }

    public int GetEnemyCount(int palierIndex)
    {
        // Priorité au roster runtime
        if (_runtimeRoster != null
            && palierIndex >= 0 && palierIndex < _runtimeRoster.Count
            && _runtimeRoster[palierIndex] != null)
        {
            return _runtimeRoster[palierIndex].Count;
        }

        // Fallback config
        var p = GetPalier(palierIndex);
        if (p == null) return (palierIndex == GetPalierCount() - 1) ? 1 : 3;

        if (p.expectedEnemyCount > 0) return p.expectedEnemyCount;
        if (p.enemies != null && p.enemies.Count > 0) return Mathf.Min(3, p.enemies.Count);

        int last = GetPalierCount() - 1;
        return (palierIndex == last) ? 1 : 3;
    }

    public EnemyInfo GetEnemy(int palierIndex, int enemyIndex)
    {
        // Priorité au roster runtime
        if (_runtimeRoster != null
            && palierIndex >= 0 && palierIndex < _runtimeRoster.Count
            && _runtimeRoster[palierIndex] != null
            && enemyIndex >= 0 && enemyIndex < _runtimeRoster[palierIndex].Count)
        {
            return _runtimeRoster[palierIndex][enemyIndex];
        }

        // Fallback config
        var p = GetPalier(palierIndex);
        if (p == null || p.enemies == null) return null;
        if (enemyIndex < 0 || enemyIndex >= p.enemies.Count) return null;
        return p.enemies[enemyIndex];
    }

    // --------- Sécurités & build ---------
    public void EnsureDefaultsIfEmpty()
    {
        if (paliers != null && paliers.Count > 0) return;

        paliers = new List<PalierDef>
        {
            new PalierDef{ palierName="Palier 1", winScore=300, expectedEnemyCount=3 },
            new PalierDef{ palierName="Palier 2", winScore=500, expectedEnemyCount=3 },
            new PalierDef{ palierName="Palier 3", winScore=700, expectedEnemyCount=3 },
            new PalierDef{ palierName="Palier 4", winScore=900, expectedEnemyCount=3 },
            new PalierDef{ palierName="Palier 5", winScore=1200, expectedEnemyCount=1 },
        };
    }

    public void InvalidateRoster()
    {
        _runtimeRoster = null;
    }

    /// <summary>
    /// Construit le roster aléatoire pour chaque palier depuis une EnemyLibrary.
    /// - Sans répétition par palier si possible, sinon doublons permis.
    /// </summary>
    public void BuildRoster(EnemyLibrary library, System.Random rng)
    {
        EnsureDefaultsIfEmpty();

        int palierCount = GetPalierCount();
        _runtimeRoster = new List<List<EnemyInfo>>(palierCount);

        var pool = (library != null && library.allEnemies != null) ? library.allEnemies : new List<EnemyInfo>();
        int poolCount = pool.Count;

        for (int p = 0; p < palierCount; p++)
        {
            int need = GetEnemyCount(p);
            var picked = new List<EnemyInfo>(need);

            if (poolCount >= need && need > 0)
            {
                var bag = new List<EnemyInfo>(pool);
                for (int i = 0; i < need; i++)
                {
                    int k = rng.Next(bag.Count);
                    picked.Add(bag[k]);
                    bag.RemoveAt(k);
                }
            }
            else
            {
                for (int i = 0; i < need; i++)
                {
                    if (poolCount == 0) { picked.Add(null); continue; }
                    int k = rng.Next(poolCount);
                    picked.Add(pool[k]);
                }
            }

            _runtimeRoster.Add(picked);
        }
    }
}
