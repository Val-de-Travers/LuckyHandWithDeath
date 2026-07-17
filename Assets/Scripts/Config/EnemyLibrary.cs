using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Cosmic Wimpout/Enemy Library", fileName = "EnemyLibrary")]
public class EnemyLibrary : ScriptableObject
{
    // On réutilise le type déjà défini dans PalierConfig
    public List<PalierConfig.EnemyInfo> allEnemies = new List<PalierConfig.EnemyInfo>();
}
