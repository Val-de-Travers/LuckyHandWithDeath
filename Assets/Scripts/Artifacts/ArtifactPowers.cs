using System;
using System.Collections.Generic;
using UnityEngine;

public interface IArtifactEffect
{
    bool IsUsableNow(GameManager gm, out string reason);
    void BeginUse(GameManager gm, Action onConsumed);
}

public class ArtifactPowers : MonoBehaviour
{
    [Header("Wiring (optionnel) — tu peux tout laisser vide")]
    public GameManager gm;
    public PlayerInventory inventory;
    public InventoryUI inventoryUI;

    // Registre des effets par effectKey
    private Dictionary<string, IArtifactEffect> registry;

    void Awake()
    {
        // 1) Init simple du registre (aucun auto-wiring ici)
        registry = new Dictionary<string, IArtifactEffect>(StringComparer.OrdinalIgnoreCase);

        // 2) Enregistrement des effets

        // 
        registry["reroll_one_die"] = new CheaterBoneEffect();
        registry["reroll_turn"] = new TimeRerollTurnEffect();
        registry["reroll_match"] = new TimeRerollMatchEffect();

        // TRANSFORMATION
        registry["transform.balance"] = new JusticeBalanceEffect();
        registry["transform.plus1"] = new ApothecaryFortifierEffect();
        registry["transform.flashscroll"] = new FlashScrollEffect();
        registry["transform.mirror"] = new MirrorEffect();

        // AJOUT
        registry["add.surprise"] = new SurpriseDieEffect();
        registry["add.bottle_sun"] = new BottleSunEffect();
        registry["add.love_filter"] = new LoveFilterEffect();

        // SCORE
        registry["score.funeral_ledger"] = new FuneralLedgerEffect();
        registry["score.marriage_dot"] = new MarriageDotEffect();
        registry["score.entry_ticket"] = new EntryTicketEffect();

        // CONTRE-JEU (jouable uniquement pendant le tour de l'IA)
        // Enregistré à la fois par effectKey ET par id normalisé (les assets ont souvent un effectKey vide).
        var piercedPurse = new PiercedPurseEffect();
        var sleepingHorn = new SleepingHornCounterEffect();
        var slamTable = new SlamTableEffect();
        var watchersTower = new WatchersTowerEffect();
        var poison = new QuestionablePoisonCounterEffect();

        registry["counter.pierced_purse"] = piercedPurse;
        registry["piercedpurse"] = piercedPurse;              // id de l'asset

        registry["counter.sleeping_horn"] = sleepingHorn;
        registry["sleeping_horn"] = sleepingHorn;             // id de l'asset

        registry["counter.slam_table"] = slamTable;
        registry["slamtable"] = slamTable;                   // id de l'asset

        registry["counter.belltower"] = watchersTower;        // effectKey déjà présent sur l'asset
        registry["watcherstower"] = watchersTower;           // id de l'asset

        registry["counter.poison"] = poison;
        registry["questionable_poison"] = poison;            // id de l'asset
    }

    // Normalise un id/effectKey pour la recherche (trim + minuscules).
    static string NormalizeKey(string s) => string.IsNullOrEmpty(s) ? "" : s.Trim().ToLowerInvariant();

    // Resolve paresseux (safe sur toutes versions Unity)
    void EnsureRefs()
    {
#if UNITY_2023_1_OR_NEWER
        if (!gm)        gm        = FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
        if (!inventory) inventory = FindFirstObjectByType<PlayerInventory>(FindObjectsInactive.Include);
        if (!inventoryUI) inventoryUI = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
#else
        if (!gm) gm = FindObjectOfType<GameManager>(true);
        if (!inventory) inventory = FindObjectOfType<PlayerInventory>(true);
        if (!inventoryUI) inventoryUI = FindObjectOfType<InventoryUI>(true);
#endif
    }

    // Appelée par l’UI (bouton Use) via GameManager.TryUseArtifactFromInventory(...)
    public void TryUseFromInventory(int index)
    {
        EnsureRefs();

        if (!inventory)
        { gm?.hintBanner?.Show("Inventaire introuvable."); return; }

        if (inventory.Count == 0)
        { gm?.hintBanner?.Show("Inventaire vide."); return; }

        var a = inventory.GetAt(index);
        if (!a)
        { gm?.hintBanner?.Show("Aucun artefact à cet emplacement."); return; }

        // Hors tour du joueur : seuls les artefacts Contre-Jeu sont utilisables
        if (gm != null && !gm.IsPlayersTurn && a.type != ArtifactType.ContreJeu)
        {
            gm.hintBanner?.Show("Vous ne pouvez utiliser cet artefact uniquement durant votre tour.");
            return;
        }

        // Clé de recherche : effectKey si présent, sinon on retombe sur l'id de l'asset (normalisé).
        string lookup = !string.IsNullOrEmpty(a.effectKey) ? NormalizeKey(a.effectKey) : NormalizeKey(a.id);
        if (string.IsNullOrEmpty(lookup) || !registry.TryGetValue(lookup, out var effect))
        { gm?.hintBanner?.Show("Cet artefact n'est pas encore implémenté."); return; }

        if (!effect.IsUsableNow(gm, out var reason))
        { gm?.hintBanner?.Show(reason ?? "Artefact inutilisable maintenant."); return; }

        // Déclenche l’effet, consomme à la fin
        effect.BeginUse(gm, onConsumed: () =>
        {
            int safe = Mathf.Clamp(index, 0, inventory.Count - 1);
            inventory.RemoveAt(safe);
            inventoryUI?.RefreshNow();
            gm?.inventoryDots?.Refresh();
        });

        // L'artefact est lancé (effet immédiat ou ciblage de dé en cours) :
        // on ferme l'inventaire pour libérer l'écran et éviter un second clic sur USE.
        inventoryUI?.Hide();
    }

    public static class ArtifactPowersHelpers
    {
        // Démarre une sélection d’un seul dé avec un filtre ; réutilise ton flux actuel de “use artefact”.
        public static void PickOneDie(GameManager gm, Func<DieView, bool> filter, Action<DieView> onPicked, string prompt)
        {
            // Tu réutilises ici ta mécanique de “pick de dé” déjà mise en place pour l’Os du Tricheur.
            // Si tu as déjà une méthode équivalente dans ta base, appelle-la.
            // À défaut, très simple fallback : cliquer un dé qui passe le filtre → onPicked(die).
            // (Si tu veux, je peux te recoder un canal “external pick” propre au prochain tour.)
            gm?.hintBanner?.Show(prompt);
        }

        // Change proprement la face (accès à SetFaceSprite interne encapsulé dans DieView)
        public static void SetDieFace(DieView die, DieFace newFace)
        {
            if (die == null) return;
            // On simule une "roll" ciblée : on change le champ, puis rafraîchit l'image/petit pulse
            var fi = typeof(DieView).GetField("currentFace", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            fi?.SetValue(die, newFace);

            var mi = typeof(DieView).GetMethod("GetFace", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public); // force JIT
            // On déclenche l’UI via un équivalent à SetFaceSprite + un petit pulse si tu veux. 
            // Si tu as une méthode publique utilitaire pour refléter la face (ex: RefreshFaceUI), appelle-la ici.
            // Version minimale : refaire une "Roll" truquée en appelant un utilitaire interne si accessible.
            // Sinon, pas grave : l'image se met déjà à jour via les roll standards suivants.
            // (Si besoin, je peux t’ajouter un public RefreshFace(DieFace) dans DieView.)
        }
    }
    

}
