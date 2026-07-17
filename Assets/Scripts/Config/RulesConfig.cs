using UnityEngine;

[CreateAssetMenu(fileName = "RulesConfig", menuName = "CosmicWimpout/RulesConfig")]
public class RulesConfig : ScriptableObject
{
    [Header("Seuils / Objectifs")]
    public int entryThreshold = 35;      // ouvrir la partie
    public int winThreshold = 300;       // seuil de déclenchement de la phase finale

    [Header("Délais / UX")]
    public float aiDelay = 2f;
    public float clauseRepeatDelay = 2f; // délai si la face de Flash retombe
    public float clauseStartDelay = 2f; // délai avant d’entamer la Clause auto

    [Header("Règles spéciales")]
    public bool enableSupernova = true;      // 5×10 dans un même jet => élimination
    public bool allowFreightTrains = false;  // optionnel : 5 identiques = 100×face (hors 10)
    public bool instantWinOnSixes = false;  // optionnel : 5×6 => victoire instantanée

    [Header("IA")]
    public int aiBankThresholdMany = 25;   // base quand >=3 dés relançables
    public int aiBankThresholdFew = 20;   // base quand 2 dés relançables
    public int aiBankThresholdOne = 15;   // base quand 1 dé relançable
    public int aiRandomJitterMax = 6;    // jitter aléatoire [0..N]
    public int aiBehindBias = 3;    // bonus à la prise de risque si l’IA est derrière
}


