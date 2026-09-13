using UnityEngine;

// Banque des sons de dés. UN SEUL asset suffit pour tout le jeu :
//   Assets/Resources/DiceSfxLibrary.asset
// (clic droit dans le Project → Create → Cosmic Wimpout → Dice SFX Library)
// AudioManager le charge tout seul au démarrage : aucune scène à recâbler, aucun
// prefab à retoucher. Il n'y a plus qu'à glisser les .wav dans les cases.
//
// Chaque entrée accepte PLUSIEURS clips : le jeu en tire un au hasard à chaque
// déclenchement, sans jamais rejouer la même variante deux fois de suite. Un seul
// clip par entrée fonctionne très bien pour commencer ; ajoute des variantes plus
// tard sans toucher au code.
[CreateAssetMenu(fileName = "DiceSfxLibrary", menuName = "Cosmic Wimpout/Dice SFX Library")]
public class DiceSfxLibrary : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        [Tooltip("Variantes du même son ; tirage au sort à chaque lecture.")]
        public AudioClip[] clips;

        [Range(0f, 2f)]
        [Tooltip("Volume relatif de CE son. Le volume SFX global (options) s'applique par-dessus. " +
                 "Sert à équilibrer les sons entre eux sans réexporter les fichiers.")]
        public float volume = 1f;

        [Range(0f, 0.5f)]
        [Tooltip("Variation aléatoire de hauteur (±). 0.05 suffit à casser la répétition ; " +
                 "0 = son strictement identique à chaque fois.")]
        public float pitchJitter = 0.05f;

        [Tooltip("Délai minimal (s) entre deux lectures de ce son : garde-fou anti-mitraillette.")]
        public float minInterval = 0.03f;

        [System.NonSerialized] int lastIndex = -1;
        [System.NonSerialized] float nextAllowedTime;

        public bool IsEmpty => clips == null || clips.Length == 0;

        // Renvoie null si l'entrée est vide ou si le garde-fou temporel n'est pas écoulé.
        public AudioClip Pick()
        {
            if (IsEmpty) return null;
            if (Time.unscaledTime < nextAllowedTime) return null;
            nextAllowedTime = Time.unscaledTime + Mathf.Max(0f, minInterval);

            if (clips.Length == 1) { lastIndex = 0; return clips[0]; }

            int i = Random.Range(0, clips.Length);
            if (i == lastIndex) i = (i + 1) % clips.Length; // jamais deux fois la même d'affilée
            lastIndex = i;
            return clips[i];
        }

        public float RandomPitch() => 1f + Random.Range(-pitchJitter, pitchJitter);
    }

    [Header("Manipulation des dés")]
    [Tooltip("DropDie — un dé se pose dans la zone de sélection, ou retombe à sa place sur la table.")]
    public Entry dropDie;
    [Tooltip("TakeDie — le joueur attrape un dé.")]
    public Entry takeDie;
    [Tooltip("TakeDice — 2 à 5 dés attrapés d'un coup (sélection automatique : Flash, Full…).")]
    public Entry takeDice;

    [Header("Jets")]
    [Tooltip("Roll1Die — un seul dé lancé.")]
    public Entry roll1Die;
    [Tooltip("Roll2-3Dice — 2 ou 3 dés lancés.")]
    public Entry roll2to3Dice;
    [Tooltip("Roll4-5Dice — 4 dés ou plus lancés.")]
    public Entry roll4to5Dice;

    [Header("Divers (optionnel)")]
    [Tooltip("Dépôt refusé : le dé tremble et repart. Laisse vide tant que le son n'existe pas.")]
    public Entry refuseDie;

    // ---- Choix de l'entrée selon le nombre de dés concernés ------------------------------
    // Les replis (fallback) permettent de tester avec un jeu de sons incomplet : tant que
    // Roll4-5 est vide, un jet de 5 dés emprunte le son de 2-3, puis celui d'un dé seul.

    public Entry ForRoll(int count)
    {
        if (count >= 4) return First(roll4to5Dice, roll2to3Dice, roll1Die);
        if (count >= 2) return First(roll2to3Dice, roll4to5Dice, roll1Die);
        return First(roll1Die, roll2to3Dice);
    }

    public Entry ForTake(int count)
        => count >= 2 ? First(takeDice, takeDie) : First(takeDie, takeDice);

    public Entry ForDrop(int count) => dropDie;

    static Entry First(params Entry[] candidates)
    {
        foreach (var e in candidates) if (e != null && !e.IsEmpty) return e;
        return null;
    }
}
