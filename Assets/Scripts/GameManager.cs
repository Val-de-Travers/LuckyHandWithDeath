using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class GameManager : MonoBehaviour
{
    [Header("Config (ScriptableObjects)")]
    public RulesConfig rules;
    public PalierConfig palierConfig;

    // ===================== CAMPAGNE / PALIERS =====================
    [Header("Campaign / Paliers")]
    public TMP_Text palierHudText;              // "Palier 2/5"
    public TMP_Text tourHudText;                // "Adversaire 1/3"
    public TMP_Text defeatsHudText;             // "Défaites 1/3"
    public Image[] enemyHudSlots;               // 3 vignettes (courant + 2 suivants)

    // État campagne
    int palierIndex = 0;
    int enemyIndex = 0;
    int defeatsCount = 0;
    bool awaitingNextMatch = false;             // devient true UNIQUEMENT dans EndMatch
    string lastMatchWinner = "";

    // Garde-fou progression
    bool matchOver => awaitingNextMatch && !string.IsNullOrEmpty(lastMatchWinner);

    // ===================== UI DE MATCH =====================
    [Header("UI (Match)")]
    public TMP_Text playerScoreText;
    public TMP_Text aiScoreText;
    public TMP_Text turnScoreText;
    public TMP_Text turnLabel;

    public Button rollButton;
    public Button bankButton;
    public Button endRoundButton;

    [Header("Opponent (Center Portrait)")]
    public Image opponentPortraitImage; // assigner l'Image près de la table

    [Header("Dice")]
    public List<DieView> dice;

    [Header("UX Helpers")]
    public HintBanner hintBanner;
    public UILog uiLog;

    // Fin de match annoncée : on attend que le joueur appuie sur Next avant la sélection d'artefact
    private bool awaitingVictoryNext = false;
    // Nombre d'artefacts offerts à la prochaine phase d'obtention (3 en victoire, 1 en défaite)
    private int pendingArtifactPickCount = 3;
    // Phases d'obtention SUPPLÉMENTAIRES à enchaîner après la phase courante.
    // Récompense de fin de Palier 3 : 2 phases au total (voir OnPressNext).
    private int extraArtifactPicks = 0;

    // ====== HintBanner fusion (priorité aux annonces HintBanner) ======
    enum BannerMode { None, State, Action }
    BannerMode _bannerMode = BannerMode.None;
    string _lastStateBanner = "";

    [Header("Map")]
    public MapView mapView;


    void ShowAction(string message)
    {
        if (hintBanner != null) hintBanner.Show(message);
        _bannerMode = BannerMode.Action;
    }
    void ShowState(string message)
    {
        if (hintBanner == null) return;
        if (_bannerMode == BannerMode.Action) return;           // ne pas écraser une annonce d'action récente
        if (_lastStateBanner == message) return;                // évite de spammer
        hintBanner.Show(message);
        _bannerMode = BannerMode.State;
        _lastStateBanner = message ?? "";
    }
    void ResetBannerState()
    {
        _bannerMode = BannerMode.None;
        _lastStateBanner = "";
    }

    [Header("Enemy Selection")]
    public EnemyLibrary enemyLibrary;
    public int campaignSeed = 0; // 0 => seed aléatoire au démarrage



    // ===================== UI GAME OVER =====================
    [Header("Game Over UI")]
    public GameObject gameOverPanel;            // Canvas/Panel à afficher
    public TMP_Text gameOverTitleText;          // optionnel (ex: "Game Over")
    public TMP_Text gameOverDescText;           // optionnel (ex: "3 défaites — Campagne perdue")
    public Button replayButton;                 // bouton "Rejouer"
    bool isGameOverScreen = false;

    // ===================== ARTEFACTS =====================
    [Header("Artifacts (Selection & Inventory)")]
    public ArtifactLibrary artifactLibrary;
    public RectTransform artifactOptionsRoot;       // conteneur des 3 cartes au moment du choix
    public ArtifactCardView artifactCardPrefab;     // PREFAB unifié

    private readonly List<Artifact> offeredArtifacts = new();
    private readonly List<ArtifactCardView> offeredCardViews = new();


    // === AJOUT (bonus dice) ===
    [Header("AJOUT (bonus dice)")]
    public GameObject surpriseDiePrefab;            // prefab projet (optionnel)
    public Transform surpriseDiceParent;            // parent d’affichage si prefab
    public DieView surpriseDieTemplateInScene;      // instance de scène déjà placée (optionnel)

    class AddedDie
    {
        public DieView view;
        public bool destroyOnCleanup;
        public bool keepVisibleUntilNextAction; // ← reste affiché jusqu’au prochain ROLL/NEXT, mais hors pool
    }
    private readonly List<AddedDie> addedDice = new();

    // Réf directe pratique (premier dé d’ajout courant)
    public DieView currentAddedDie;

    // Faces forcées au prochain ROLL (Soleil en bouteille / Filtre d’amour…)
    private readonly Dictionary<DieView, DieFace> pendingForcedFaces = new();

    // Bonus Filtre d’amour
    private bool loveFilterBonusArmed = false;
    private DieFace loveFilterTarget;
    private DieView loveFilterDie;

    [Header("Score Mod / UI")]
    public TMP_Text scoreModLabel;   // Assigne un TMP_Text près du score de jet

    [Header("Dev Tools - Add Points")]
    public Button devAdd50Button;          // +50 rapide
    public Button devAddCustomButton;      // applique la valeur du champ
    public TMP_InputField devCustomInput;  // montant à ajouter (ex: 120)
    public Toggle devAddToAIToggle;        // si coché -> ajouter à l'IA au lieu du Joueur


    [System.Serializable]
    public struct ActiveScoreMod
    {
        public enum Mode { None, FuneralLedger, MarriageDot } // + MarriageDot

        public Mode mode;
        public int threshold;
        public float bonusPct;
        public int penaltyFlat;

        // Multiplicateurs "purs" (ex: ×2 Dot de mariage)
        public float multiplier;

        public bool IsActive => mode != Mode.None;
        public static ActiveScoreMod None() => new ActiveScoreMod { mode = Mode.None, multiplier = 1f };
    }


    private ActiveScoreMod _scoreMod = ActiveScoreMod.None();

    [Header("Inventory (UI & Data)")]
    public PlayerInventory playerInventory;
    public InventoryUI inventoryUI;
    public InventoryDots inventoryDots;


    [Header("UI Root (optional)")]
    public Canvas uiCanvas; // non obligatoire


    [Header("UI Tooltip")]
    public ArtifactTooltip artifactTooltip; // instance du TooltipPanel en scène

    // État artefacts
    private bool awaitingArtifactPick = false;

    private string rollOriginalLabel = "ROLL";
    private string bankOriginalLabel = "BANK";
    private string endOriginalLabel = "CONTINUE";

    [Header("Dev Tools")]
    public bool showDevTools = true;   // décoche en build si tu veux les masquer
    public Button devWinButton;        // assigne le bouton “DEV: Win”
    public Button devLoseButton;       // assigne le bouton “DEV: Perdre”

    [Header("Dev Tools - Artifact List")]
    public Button devShowArtifactListButton;   // bouton qui ouvre/ferme la liste complète
    public RectTransform devArtifactListRoot;  // conteneur des cartes (panel, désactivé par défaut)

    [Header("Dev Tools - Force Dice")]
    public TMP_InputField devForceFacesInput;  // ex: "5 5 10 2 6" (S ou 100 = Sun), dans l'ordre des dés
    public Button devForceFacesButton;         // applique les valeurs au PROCHAIN jet

    private enum Turn { Player, AI }
    private Turn currentTurn = Turn.Player;

    private enum Phase { AwaitFirstRoll, Normal, Clause, WaitEnd, AITurnPlaying, AITurnWaitEnd, AITurnPreBankCounter, OpeningRollOff }

    private Phase phase = Phase.AwaitFirstRoll;

    // Scores (match courant)
    private int playerScore = 0;
    private int aiScore = 0;
    private int turnScore = 0;

    // Règles
    private int ENTRY_THRESHOLD = 35;
    private int WIN_THRESHOLD = 300; // mis à jour depuis le Palier
    private float AI_DELAY = 1.2f;
    [Tooltip("Délai entre chaque dé verrouillé par l'IA (pour que le joueur suive).")]
    public float aiSelectDelay = 0.45f;
    private float CLAUSE_REPEAT_DELAY = 1.5f;
    private float CLAUSE_START_DELAY = 0.9f;
    private bool ENABLE_SUPERNOVA = true;
    private const int INVENTORY_CAPACITY = 3;

    // Ouverture (35)
    private bool playerOpened = false;
    private bool aiOpened = false;

    // Flash & Clause
    private DieFace currentFlashFace = DieFace.Two;
    private readonly Dictionary<DieFace, int> FLASH_SCORE = new()
    {
        { DieFace.Two, 20 }, { DieFace.Three, 30 }, { DieFace.Four, 40 },
        { DieFace.Five, 50 }, { DieFace.Six, 60 }, { DieFace.Ten, 100 }
    };
    private bool flashPendingResolution = false;

    // Sélections / verrous
    private readonly HashSet<int> frozenLocks = new();
    private readonly HashSet<int> mutableLocks = new();
    private readonly HashSet<int> flashLockIndices = new();
    // VISUEL UNIQUEMENT : dés d'un Flash non suivis par flashLockIndices — Flash de l'IA
    // et Flash créé par un artefact. Sert au surlignage orange, n'influence aucune règle.
    private readonly HashSet<int> flashVisualIndices = new();
    private readonly Dictionary<int, int> mutablePoints = new();
    private readonly HashSet<int> eligibleLockIndices = new();
    private readonly Dictionary<int, int> eligibleLockPoints = new();
    private readonly List<int> lastRolledIndices = new();

    // Vrai tant que le Flash n'a été "dégagé" que par une sélection encore annulable ;
    // si le joueur désélectionne tout, la Clause doit se réarmer.
    private bool clauseClearedBySelection = false;

    // Wimpout du joueur alors qu'il possède encore un artefact de sauvetage : la vérification
    // de fin de match est REPORTÉE au CONTINUE, sinon un tour décisif (phase finale) se
    // solderait par une défaite immédiate sans laisser jouer l'artefact.
    private bool pendingWinCheckOnContinue = false;
    // Score du tour perdu au dernier wimpout du joueur — restauré si un artefact
    // de relance (Os du Tricheur) offre une seconde chance sur ce jet.
    private int wimpoutLostTurnScore = 0;

    [Header("Dev/Artifact Pick Actions")]
    public Button devSkipArtifactPickButton;       // "Skip"
    public Button devDestroyCurrentArtifactButton; // "Destroy current"

    // IA + phase finale
    private System.Random aiRng;
    private const string PLAYER_NAME = "Joueur";
    private const string AI_NAME = "IA";
    private bool finalPhase = false;
    private int targetScore = 0;
    private Turn challenger = Turn.Player;
    private bool gameOver = false; // fin de MATCH (pas campagne)


    private bool aiHasBankedThisTurn = false;

    // ===== Contre-Jeu (interruptions du tour IA) =====
    // Fenêtre ouverte : l'IA est en pause, le joueur peut jouer un artefact de Contre-Jeu ou Next.
    private bool awaitingCounterPlay = false;
    // PiercedPurse : -25% sur la bank de l'IA pour CE tour (0.75).
    private float aiBankMultiplier = 1f;
    // Dés du Flash IA en cours (valide uniquement pendant la fenêtre qui suit un Flash).
    private readonly List<int> aiFlashIndices = new List<int>();
    // Un contre-jeu a touché un dé du Flash → le Flash est invalidé (pas de Clause).
    private bool aiFlashCancelled = false;
    // Un contre-jeu a modifié les dés → à la fermeture de la fenêtre, l'IA re-sélectionne
    // les dés marquants libres (elle ne les relance pas).
    private bool aiDiceChangedByCounter = false;

    [Header("Contre-Jeu / Timer")]
    [Tooltip("Durée (s) de la fenêtre de contre-jeu avant auto-Next.")]
    public float counterPlayTimeout = 3f;
    [Tooltip("Barre de temps (Image en mode Filled) qui se vide pendant la fenêtre de contre-jeu.")]
    public Image counterPlayTimerBar;
    [Tooltip("Coché : aucune fenêtre de contre-jeu n'est proposée (ni pause, ni barre de temps). " +
             "Le tour de l'adversaire se déroule normalement, comme si le joueur n'avait pas d'artefact " +
             "de Contre-Jeu. L'état reste tel quel jusqu'à ce que le joueur décoche la case.")]
    public Toggle disableCounterPlayToggle;

    // ===== Tirage au sort du premier joueur =====
    private bool openingRollOffBusy = false; // anti double-clic pendant l'animation du tirage
    private bool awaitingRollOffNext = false; // tirage résolu : on attend Next avant de commencer
    private bool rollOffWinnerIsPlayer = true;

    // ===================== TRAITS / AFFIXES =====================
    // Traits acquis par le joueur (persistent sur toute la campagne)
    private readonly List<TraitDef> playerTraits = new();
    // Traits ennemis actifs pour LE MATCH EN COURS (vide si aucun).
    // Palier 5 : peut contenir le trait propre du boss ET un trait boss aléatoire.
    private readonly HashSet<string> activeEnemyTraitKeys = new();
    // Traits ennemis actifs affichables (nom + description + icône) pour l'AddBox de l'IA
    private readonly List<TraitDef> activeEnemyTraitInfos = new();

    [Header("Traits / Catalogues (ScriptableObjects)")]
    [Tooltip("Catalogue des traits JOUEUR (nom/desc/icône). Laisser vide → valeurs par défaut du code.")]
    public TraitLibrary playerTraitLibrary;
    [Tooltip("Catalogue des traits BOSS (nom/desc/icône). Laisser vide → valeurs par défaut du code.")]
    public TraitLibrary bossTraitLibrary;

    // Pools effectifs (library si assignée, sinon catalogue statique)
    List<TraitDef> PlayerTraitPool()
        => (playerTraitLibrary != null && playerTraitLibrary.traits != null && playerTraitLibrary.traits.Count > 0)
            ? playerTraitLibrary.ToTraitDefs() : new List<TraitDef>(TraitCatalog.PlayerTraits);

    List<TraitDef> BossTraitPool()
        => (bossTraitLibrary != null && bossTraitLibrary.traits != null && bossTraitLibrary.traits.Count > 0)
            ? bossTraitLibrary.ToTraitDefs() : new List<TraitDef>(TraitCatalog.BossTraits);

    [Header("Traits / UI (AddBox)")]
    [Tooltip("Conteneur (AddBoxP1) où sont instanciées les vignettes des traits du joueur.")]
    public RectTransform playerTraitsIconsRoot;
    [Tooltip("Conteneur (AddBoxP2) où sont instanciées les vignettes du/des trait(s) de l'adversaire.")]
    public RectTransform aiTraitsIconsRoot;
    [Tooltip("Taille des vignettes de trait.")]
    public Vector2 traitIconSize = new Vector2(48f, 48f);

    [System.Serializable]
    public class TraitIconEntry
    {
        public string key;   // clé du trait (rempli auto : boss.*, player.*)
        public string label; // nom lisible (lecture seule, aide au repérage dans l'Inspector)
        public Sprite icon;  // ← glisse ton sprite ici
    }
    [Tooltip("Icônes des traits Boss & Joueur. Les entrées (clé + nom) sont pré-remplies : il suffit de glisser un sprite sur chacune. Les traits ennemis, eux, utilisent le champ 'Affliction Icon' de l'ennemi.")]
    public List<TraitIconEntry> traitIcons = new();

    // Garantit que la liste traitIcons contient une entrée par trait Boss/Joueur connu,
    // pour que l'utilisateur n'ait qu'à glisser les sprites dans l'Inspector.
    void EnsureTraitIconEntries()
    {
        if (traitIcons == null) traitIcons = new List<TraitIconEntry>();

        void Ensure(TraitDef t)
        {
            if (t == null) return;
            foreach (var e in traitIcons)
                if (e != null && NormalizeTraitKey(e.key) == t.key) { e.label = t.name; return; }
            traitIcons.Add(new TraitIconEntry { key = t.key, label = t.name, icon = null });
        }

        foreach (var t in TraitCatalog.BossTraits) Ensure(t);
        foreach (var t in TraitCatalog.PlayerTraits) Ensure(t);
    }

    void OnValidate()
    {
        EnsureTraitIconEntries();
    }

    Sprite GetTraitIcon(TraitDef t)
    {
        if (t == null) return null;
        if (t.icon != null) return t.icon;
        if (traitIcons != null)
            foreach (var e in traitIcons)
                if (e != null && NormalizeTraitKey(e.key) == t.key) return e.icon;
        return null;
    }

    [Header("Inventaire adversaire (UI)")]
    [Tooltip("Icône de l'artefact possédé par l'adversaire (grisée quand consommé).")]
    public Image aiArtifactIcon;
    [Tooltip("Nom de l'artefact possédé par l'adversaire.")]
    public TMP_Text aiArtifactLabel;
    [Tooltip("Inventaire de l'adversaire : un composant PlayerInventory dédié (voir InventoryUI de l'IA).")]
    public PlayerInventory aiInventory;

    // Met à jour l'inventaire visible de l'adversaire (artefact possédé ce match).
    // Un artefact CONSOMMÉ disparaît complètement (icône cachée, label vidé).
    void RefreshAIArtifactUI()
    {
        // Synchronise l'inventaire IA : l'artefact consommé disparaît de la liste
        if (aiInventory && aiArtifactUsed && aiInventory.Count > 0)
            aiInventory.ClearAll();

        bool show = aiArtifact != null && !aiArtifactUsed;

        if (aiArtifactIcon)
        {
            aiArtifactIcon.gameObject.SetActive(show && aiArtifact.icon != null);
            if (show && aiArtifact.icon != null)
            {
                aiArtifactIcon.sprite = aiArtifact.icon;
                aiArtifactIcon.preserveAspect = true;
                aiArtifactIcon.color = Color.white;
            }
        }

        if (aiArtifactLabel)
            aiArtifactLabel.text = show ? aiArtifact.displayName : "";
    }

    // Reconstruit les vignettes de traits (icône + tooltip) dans les AddBox des deux joueurs.
    // À appeler quand la composition change (nouveau match, trait acquis/perdu, replay).
    void RebuildTraitIcons()
    {
        RebuildTraitIconsInto(playerTraitsIconsRoot, playerTraits, isPlayerSide: true);
        RebuildTraitIconsInto(aiTraitsIconsRoot, activeEnemyTraitInfos, isPlayerSide: false);
    }

    // Un trait à effet UNIQUE (1×/match) a-t-il déjà été consommé ?
    // Le Verre Empoisonné existe des deux côtés : le drapeau dépend du porteur.
    bool IsTraitConsumed(string key, bool isPlayerSide)
    {
        switch (NormalizeTraitKey(key))
        {
            case "poisonglass":   return isPlayerSide ? traitPoisonGlassUsedVsAI : traitPoisonGlassUsedVsPlayer;
            // Côté joueur : 1×/match. Côté ennemi (Chevalière) : 1×/Clause, jamais « épuisé ».
            case "estoc":         return isPlayerSide && estocUsedThisMatchPlayer;
            case "boss.blacksun": return traitBlackSunUsed;
            case "boss.audit":    return traitAuditUsed;
            case "boss.tide":     return traitTideUsed;
            default:              return false; // traits permanents ou à usage non limité
        }
    }

    void RebuildTraitIconsInto(RectTransform root, List<TraitDef> traits, bool isPlayerSide)
    {
        if (!root) return;

        for (int i = root.childCount - 1; i >= 0; i--)
            Destroy(root.GetChild(i).gameObject);

        // Layout horizontal par défaut si le conteneur n'en a pas
        if (!root.GetComponent<UnityEngine.UI.LayoutGroup>())
        {
            var h = root.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.spacing = 4f;
        }

        var tooltip = EnsureTooltip();
        foreach (var t in traits)
        {
            if (t == null) continue;

            var go = new GameObject("Trait_" + t.key, typeof(RectTransform));
            go.transform.SetParent(root, false);

            var img = go.AddComponent<Image>();
            var view = go.AddComponent<TraitIconView>();
            view.icon = img;

            // Fallback texte si aucune icône
            TMP_Text label = null;
            var sprite = GetTraitIcon(t);
            if (sprite == null)
            {
                var labelGO = new GameObject("Label", typeof(RectTransform));
                labelGO.transform.SetParent(go.transform, false);
                label = labelGO.AddComponent<TextMeshProUGUI>();
                label.fontSize = 12f;
                label.alignment = TextAlignmentOptions.Center;
                label.textWrappingMode = TextWrappingModes.Normal;
                var lrt = (RectTransform)labelGO.transform;
                lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
                lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
                img.color = new Color(0f, 0f, 0f, 0.35f); // fond sombre derrière le texte
            }
            view.fallbackLabel = label;

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = traitIconSize.x;
            le.preferredHeight = traitIconSize.y;

            view.Setup(sprite, t.name, t.desc, tooltip, IsTraitConsumed(t.key, isPlayerSide));
        }
    }

    // ===== Artefact de l'adversaire (chance par palier) =====
    [Header("Traits / Artefacts ennemis")]
    [Tooltip("Chance (0..1) qu'un adversaire possède un artefact, par palier (le dernier = boss).")]
    public float[] enemyArtifactChanceByPalier = { 0f, 0.20f, 0.35f, 0.50f, 1f };

    private Artifact aiArtifact = null;   // artefact possédé par l'IA ce match (null si aucun)
    private bool aiArtifactUsed = false;

    // Artefacts que l'IA sait jouer (effets implémentés côté IA)
    static readonly string[] AIUsableArtifactKeys =
    {
        "score.entry_ticket",   // Ticket d'entrée : ouverture immédiate
        "score.marriage_dot",   // Dot de mariage : bank ×2
        "score.funeral_ledger", // Livre des Comptes : bank +50% si ≥ 70
        "reroll_one_die",       // Os du Tricheur : relance 1 dé sur wimpout
        "reroll_turn",          // Temps niv.1 : relance le jet sur wimpout
        "transform.balance",    // Balance : 2→5 et 6→10
    };

    static string NormalizeTraitKey(string s)
        => string.IsNullOrEmpty(s) ? "" : s.Trim().ToLowerInvariant();

    string AIArtifactKey() => aiArtifact ? NormalizeTraitKey(aiArtifact.effectKey) : "";
    // Ennemis vaincus dans le palier courant (pour le choix de trait de fin de palier)
    private readonly List<PalierConfig.EnemyInfo> defeatedEnemiesThisPalier = new();

    // État par MATCH des effets de traits
    private bool traitPoisonGlassUsedVsPlayer = false; // Verre Empoisonné (ennemi → joueur)
    private bool traitPoisonGlassUsedVsAI = false;     // Verre Empoisonné (joueur → IA)
    private bool traitBlackSunUsed = false;            // Soleil Noir (boss)
    private bool traitAuditUsed = false;               // Audit des Morts (boss)
    private bool traitTideUsed = false;                // Marée du Destin (boss)
    private int  traitTitheExtra = 0;                  // Tithe of Time : +25 cumulés sur le score cible du joueur
    private int  traitTitheRollStreak = 0;             // jets consécutifs du joueur sans bank
    private bool aiHasRolledThisMatch = false;         // Pistage (IA)
    private bool playerHasRolledThisMatch = false;     // Pistage (joueur)
    private int  aiTurnsThisMatch = 0;                 // Runes magiques (IA)
    private int  playerTurnsThisMatch = 0;             // Runes magiques (joueur)
    private bool estocUsedThisMatchPlayer = false;     // Coup d'estoc (joueur, 1×/match — réarmé au prochain adversaire)
    // État campagne
    private bool resurrectionUsed = false;             // Résurrection (1×/campagne)

    // Sélection de trait (cartes cliquables, comme la phase d'artefact)
    private bool awaitingTraitPick = false;
    private readonly List<TraitDef> offeredTraits = new();
    private readonly List<ArtifactCardView> offeredTraitCards = new();
    private Action afterTraitPick = null;

    bool PlayerHasTrait(string key)
    {
        foreach (var t in playerTraits) if (t.key == key) return true;
        return false;
    }

    bool EnemyTraitActive(string key)
        => !gameOver && !isGameOverScreen && activeEnemyTraitKeys.Contains(key);

    // Impôts bourgeois : si les 2 joueurs ont le même score, l'adversaire cède 20% de son score.
    void CheckBourgeoisTax()
    {
        if (!PlayerHasTrait("player.tax")) return;
        if (gameOver || matchOver || isGameOverScreen) return;
        if (playerScore <= 0 || playerScore != aiScore) return;

        int transfer = Mathf.RoundToInt(aiScore * 0.2f);
        if (transfer <= 0) return;

        aiScore -= transfer;
        playerScore += transfer;
        ShowAction($"Impôts bourgeois : égalité parfaite ! L'adversaire vous cède {transfer} pts.");
        uiLog?.Append($"Trait Impôts bourgeois : transfert de {transfer} pts (IA → Joueur).");
        UpdateUI();
    }

    // Audit des Morts (boss) : 1×/match, annule l'artefact que le joueur vient de jouer.
    // Retourne true si l'artefact est annulé (il sera retiré de l'inventaire sans effet).
    public bool Boss_TryAuditCancelArtifact()
    {
        if (!EnemyTraitActive("boss.audit") || traitAuditUsed) return false;
        traitAuditUsed = true;
        RebuildTraitIcons(); // marque le trait « (utilisé) » dans l'AddBox
        hintBanner?.Show("Audit des Morts : le Boss annule votre artefact et le confisque !");
        uiLog?.Append("Trait Boss Audit des Morts : artefact annulé et retiré.");
        return true;
    }

    // Campagne
    private System.Random campaignRng;
    private PalierConfig.EnemyInfo currentEnemyInfo;


    // --- Exposés pour les artefacts ---
    public bool IsPlayersTurn => currentTurn == Turn.Player;


    // ========= Helpers Palier =========
    void PalierEnsureDefaults()
    {
        if (palierConfig == null) return;
        try { palierConfig.EnsureDefaultsIfEmpty(); } catch { }
    }
    int GetPalierCount()
    {
        return (palierConfig != null) ? palierConfig.GetPalierCount() : 0;
    }

    int GetEnemyCountInPalier(int pIndex)
    {
        return (palierConfig != null) ? palierConfig.GetEnemyCount(pIndex) : 0;
    }

    int GetWinThresholdForPalier(int pIndex)
    {
        PalierEnsureDefaults();
        if (palierConfig != null)
        {
            var t = palierConfig.GetType();
            var fiPaliers = t.GetField("paliers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var list = fiPaliers?.GetValue(palierConfig) as System.Collections.IList;
            if (list != null && pIndex >= 0 && pIndex < list.Count)
            {
                var palier = list[pIndex];
                var fiScore = palier.GetType().GetField("winScore", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fiScore != null && fiScore.GetValue(palier) is int sc && sc > 0) return sc;
            }
        }
        int[] defs = { 300, 500, 700, 900, 1200 };
        int idx = Mathf.Clamp(pIndex, 0, defs.Length - 1);
        return defs[idx];
    }
    Sprite GetEnemyPortrait(int pIndex, int eIndex)
    {
        if (palierConfig == null) return null;
        var info = palierConfig.GetEnemy(pIndex, eIndex);
        return (info != null) ? info.icon : null; // icon pour HUD/minimap
    }

    void UpdateOpponentPortrait(PalierConfig.EnemyInfo info)
    {
        if (!opponentPortraitImage) return;
        var sp = (info != null) ? info.portrait : null; // grande image
        opponentPortraitImage.sprite = sp;
        opponentPortraitImage.enabled = (sp != null);
        opponentPortraitImage.preserveAspect = true;

        // Hover d'info ennemi (nom + description + trait) sur le portrait P2, comme sur la mini-map.
        if (mapView) mapView.AttachHoverTo(opponentPortraitImage, info);
    }


    // ========= Hot-dice =========
    bool MustRollAllNow() => dice != null && dice.Count > 0 && dice.All(d => d != null && d.isLocked);

    static GameManager _instance;

    void Awake()
    {
        aiRng = new System.Random();

        if (surpriseDieTemplateInScene != null)
            surpriseDieTemplateInScene.gameObject.SetActive(false);


        if (rules != null)
        {
            ENTRY_THRESHOLD = Mathf.Max(1, rules.entryThreshold);
            AI_DELAY = Mathf.Max(0f, rules.aiDelay);
            CLAUSE_REPEAT_DELAY = Mathf.Max(0f, rules.clauseRepeatDelay);
            CLAUSE_START_DELAY = Mathf.Max(0f, rules.clauseStartDelay);
            ENABLE_SUPERNOVA = rules.enableSupernova;
        }

        // Empêche les doublons
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;

        // IMPORTANT : NE PAS persister GameManager si tu le places seulement dans la scène de jeu
        // (sinon, commente la ligne suivante s'il ne doit pas survivre au changement de scène)
        // DontDestroyOnLoad(gameObject);

        // Sanity checks (évite les compteurs 1/1 si assets non assignés)
        if (rules == null)
            Debug.LogError("RulesConfig non assigné sur GameManager !");
        if (palierConfig == null)
            Debug.LogError("PalierConfig non assigné sur GameManager !");
    }

    int PalierCount()
    {
        // utilise la taille réelle du config
        if (palierConfig != null && palierConfig.paliers != null && palierConfig.paliers.Count > 0)
            return palierConfig.paliers.Count;
        return 5; // fallback ultime si vraiment rien n’est assigné
    }

    int EnemiesPerPalier(int palierIndex)
    {
        // si tu as un expectedEnemyCount dans ton PalierConfig, utilise-le
        var def = (palierConfig != null && palierConfig.paliers != null && palierIndex >= 0 && palierIndex < palierConfig.paliers.Count)
            ? palierConfig.paliers[palierIndex] : null;

        if (def != null && def.expectedEnemyCount > 0)
            return def.expectedEnemyCount;

        // fallback raisonnable
        return 3;
    }

    // Puis pour remplir l’UI :
    void RefreshHud()
    {
        palierHudText.text = $"Palier {palierIndex + 1}/{PalierCount()}";
        tourHudText.text = $"Adversaire {enemyIndex + 1}/{EnemiesPerPalier(palierIndex)}";
    }


    void Start()
    {
        if (rollButton) rollButton.onClick.AddListener(OnPressRoll);
        if (bankButton) bankButton.onClick.AddListener(OnPressBank);
        if (endRoundButton) endRoundButton.onClick.AddListener(OnPressEndRound);

        if (dice != null)
            foreach (var d in dice) if (d != null) d.onClicked = OnDieClicked;

        // Game Over UI
        if (gameOverPanel) gameOverPanel.SetActive(false);
        if (replayButton) replayButton.onClick.AddListener(OnPressReplay);

        // mémoriser labels d’origine
        var rt = GetButtonLabel(rollButton); if (rt) rollOriginalLabel = rt.text;
        var bt = GetButtonLabel(bankButton); if (bt) bankOriginalLabel = bt.text;
        var et = GetButtonLabel(endRoundButton); if (et) endOriginalLabel = et.text;

        // Dev tools
        if (devWinButton)
        {
            devWinButton.gameObject.SetActive(showDevTools);
            devWinButton.onClick.AddListener(OnPressDevWin);
        }
        if (devLoseButton)
        {
            devLoseButton.gameObject.SetActive(showDevTools);
            devLoseButton.onClick.AddListener(OnPressDevLose);
        }

        if (devSkipArtifactPickButton)
        {
            devSkipArtifactPickButton.gameObject.SetActive(false);
            devSkipArtifactPickButton.onClick.AddListener(OnPressSkipArtifactPick);
        }
        if (devDestroyCurrentArtifactButton)
        {
            devDestroyCurrentArtifactButton.gameObject.SetActive(false);
            devDestroyCurrentArtifactButton.onClick.AddListener(OnPressDestroyCurrentArtifact);
        }

        // État initial propre
        RefreshArtifactPickActionButtons();

        if (inventoryDots) inventoryDots.Refresh();

        if (campaignSeed == 0)
            campaignSeed = UnityEngine.Random.Range(1, int.MaxValue);
        campaignRng = new System.Random(campaignSeed);


        // ---- Dev Add Points UI ----
        if (devAdd50Button)
        {
            devAdd50Button.gameObject.SetActive(showDevTools);
            devAdd50Button.onClick.AddListener(() => DevAddPoints(50));
        }
        if (devAddCustomButton)
        {
            devAddCustomButton.gameObject.SetActive(showDevTools);
            devAddCustomButton.onClick.AddListener(OnPressDevAddCustomPoints);
        }
        if (devCustomInput)
        {
            devCustomInput.gameObject.SetActive(showDevTools);
        }
        if (devAddToAIToggle)
        {
            devAddToAIToggle.gameObject.SetActive(showDevTools);
        }

        // ---- Dev Artifact List ----
        if (devShowArtifactListButton)
        {
            devShowArtifactListButton.gameObject.SetActive(showDevTools);
            devShowArtifactListButton.onClick.AddListener(ToggleDevArtifactList);
        }
        {
            var devListPanel = GetDevArtifactListPanel();
            if (devListPanel) devListPanel.SetActive(false);
        }

        // ---- Dev Force Dice ----
        if (devForceFacesInput)
            devForceFacesInput.gameObject.SetActive(showDevTools);
        if (devForceFacesButton)
        {
            devForceFacesButton.gameObject.SetActive(showDevTools);
            devForceFacesButton.onClick.AddListener(OnPressDevForceFaces);
        }

        // Barre de temps du contre-jeu masquée par défaut
        if (counterPlayTimerBar) counterPlayTimerBar.gameObject.SetActive(false);



        StartNewMatch();
    }

    // ===================== CYCLE CAMPAGNE =====================
    void StartNewMatch()
    {
        // nettoyer tout résidu d’une éventuelle sélection d’artefact
        CancelArtifactPickUI();

        // Aucune phase d'obtention bonus ne survit d'un match à l'autre
        extraArtifactPicks = 0;

        // Aucun artefact "armé" (ciblage, face forcée, bonus en attente) ne survit d'un match à l'autre
        CancelExternalDiePick();
        ClearExternalDiePick();
        pendingForcedFaces.Clear();
        loveFilterBonusArmed = false;
        loveFilterDie = null;

        // L'inventaire ne reste pas ouvert entre deux matchs
        if (inventoryUI != null && inventoryUI.IsOpen) inventoryUI.Hide();

        playerScore = aiScore = turnScore = 0;
        playerOpened = aiOpened = false;
        finalPhase = false;
        targetScore = 0;
        challenger = Turn.Player;
        gameOver = false;

        // nouveau match ≠ "terminé"
        awaitingNextMatch = false;
        lastMatchWinner = "";
        awaitingVictoryNext = false;

        if (dice != null)

            hintBanner?.Hide();
        ResetBannerState();
        foreach (var d in dice) if (d != null) d.ResetForNewTurn();

        ClearScoreModifier();

        frozenLocks.Clear();
        mutableLocks.Clear();
        flashLockIndices.Clear();
        flashVisualIndices.Clear();
        mutablePoints.Clear();
        eligibleLockIndices.Clear();
        eligibleLockPoints.Clear();
        lastRolledIndices.Clear();
        flashPendingResolution = false;
        clauseClearedBySelection = false;
        wimpoutLostTurnScore = 0;
        pendingWinCheckOnContinue = false;

        // Tirage au sort : chaque joueur lance un dé, le plus fort commence (égalité → relance)
        // Seuls le dé 1 (joueur) et le dé SUN (adversaire) sont visibles/utilisés.
        currentTurn = Turn.Player;
        openingRollOffBusy = false;
        awaitingRollOffNext = false;
        SetPhase(Phase.OpeningRollOff);
        ShowOnlyRollOffDice();
        UpdateUI();
        RefreshCampaignUI();
        uiLog?.Append($"Match vs Adversaire {enemyIndex + 1}/{GetEnemyCountInPalier(palierIndex)} — Palier {palierIndex + 1}/{GetPalierCount()}");
        uiLog?.Append("Tirage au sort du premier joueur — ROLL.");

        currentEnemyInfo = PickEnemy(palierIndex, enemyIndex);
        if (mapView) mapView.ShowEnemy(currentEnemyInfo);

        UpdateOpponentPortrait(currentEnemyInfo);

        // ---- Traits / affixes : activation et effets de début de match ----
        SetupTraitsForMatch();
    }

    // Règles d'activation liées au palier (0-based) :
    // P1 : aucun trait · artefact IA 0%
    // P2 : trait du 1er adversaire · artefact IA 20%
    // P3 : traits des 1er et 3e adversaires · artefact IA 35%
    // P4 : tous les adversaires ont leur trait · artefact IA 50%
    // P5 (dernier) : trait propre du boss + 1 trait boss aléatoire · artefact IA 100%
    void SetupTraitsForMatch()
    {
        // Reset des états de traits par match (le trait du personnage précédent NE persiste PAS)
        activeEnemyTraitKeys.Clear();
        activeEnemyTraitInfos.Clear();
        aiArtifact = null;
        aiArtifactUsed = false;
        traitPoisonGlassUsedVsPlayer = false;
        traitPoisonGlassUsedVsAI = false;
        traitBlackSunUsed = false;
        traitAuditUsed = false;
        traitTideUsed = false;
        traitTitheExtra = 0;
        traitTitheRollStreak = 0;
        aiHasRolledThisMatch = false;
        playerHasRolledThisMatch = false;
        aiTurnsThisMatch = 0;
        playerTurnsThisMatch = 0;
        estocUsedThisMatchPlayer = false;

        int lastPalier = GetPalierCount() - 1;
        bool traitActive =
            (palierIndex == 1 && enemyIndex == 0) ||
            (palierIndex == 2 && (enemyIndex == 0 || enemyIndex == 2)) ||
            (palierIndex == 3) ||
            (palierIndex >= lastPalier);

        // Trait PROPRE de l'adversaire : TOUJOURS affiché dans l'AddBox P2.
        // S'il n'est pas activé à ce palier, on préfixe "(Pas activé)" et on n'ajoute pas
        // sa clé aux traits actifs (aucun effet en jeu).
        string ownKey = (currentEnemyInfo != null) ? NormalizeTraitKey(currentEnemyInfo.afflictionKey) : "";
        if (!string.IsNullOrEmpty(ownKey))
        {
            string baseName = string.IsNullOrEmpty(currentEnemyInfo.afflictionName) ? ownKey : currentEnemyInfo.afflictionName;
            string dispName = traitActive ? baseName : $"(Pas activé) {baseName}";

            activeEnemyTraitInfos.Add(new TraitDef(ownKey, dispName,
                currentEnemyInfo.afflictionDescription, currentEnemyInfo.afflictionIcon));

            if (traitActive)
            {
                activeEnemyTraitKeys.Add(ownKey);
                hintBanner?.Show($"Trait ennemi actif : {baseName} — {currentEnemyInfo.afflictionDescription}");
                uiLog?.Append($"Trait ennemi actif : {baseName} ({ownKey})");
            }
            else
            {
                uiLog?.Append($"Trait ennemi PAS activé à ce palier : {baseName} ({ownKey})");
            }
        }

        // Palier 5 : + 1 trait de Boss aléatoire EN PLUS du trait propre
        var bossPool = BossTraitPool();
        if (traitActive && palierIndex >= lastPalier && bossPool.Count > 0)
        {
            var boss = bossPool[campaignRng.Next(bossPool.Count)];
            activeEnemyTraitKeys.Add(boss.key);
            activeEnemyTraitInfos.Add(boss);
            hintBanner?.Show($"Trait de Boss : {boss.name} — {boss.desc}");
            uiLog?.Append($"Trait de Boss ajouté : {boss.name}");
        }

        // ---- Artefact de l'adversaire (chance par palier) ----
        float chance = 0f;
        if (enemyArtifactChanceByPalier != null && enemyArtifactChanceByPalier.Length > 0)
        {
            int ci = Mathf.Clamp(palierIndex, 0, enemyArtifactChanceByPalier.Length - 1);
            chance = Mathf.Clamp01(enemyArtifactChanceByPalier[ci]);
        }

        if (chance > 0f && campaignRng.NextDouble() < chance && artifactLibrary != null && artifactLibrary.artifacts != null)
        {
            // Ne retenir que les artefacts que l'IA sait réellement jouer
            var usable = new List<Artifact>();
            foreach (var a in artifactLibrary.artifacts)
            {
                if (a == null) continue;
                var k = NormalizeTraitKey(a.effectKey);
                foreach (var ok in AIUsableArtifactKeys)
                    if (k == ok) { usable.Add(a); break; }
            }

            if (usable.Count > 0)
            {
                aiArtifact = usable[campaignRng.Next(usable.Count)];
                hintBanner?.Show($"L'adversaire possède un artefact : « {aiArtifact.displayName} » !");
                uiLog?.Append($"Artefact IA : {aiArtifact.displayName} ({AIArtifactKey()}).");
            }
        }

        // Vignettes de traits des deux AddBox + inventaire de l'IA
        RebuildTraitIcons();
        if (aiInventory)
        {
            aiInventory.ClearAll();
            if (aiArtifact != null) aiInventory.TryAdd(aiArtifact);
        }
        RefreshAIArtifactUI(); // visible dès la phase de tirage d'ordre
        uiLog?.Append(activeEnemyTraitKeys.Count == 0
            ? "Traits actifs de l'adversaire : aucun."
            : $"Traits actifs de l'adversaire : {string.Join(", ", activeEnemyTraitKeys)}.");

        // ---- Effets immédiats de début de match ----
        // (les gardes EnemyTraitActive / PlayerHasTrait suffisent : le trait joueur
        //  "Bien né" s'applique même si l'ennemi n'a aucun trait actif)
        // Bien né (ennemi) : l'IA gagne 35 points d'office (ouverture offerte)
        if (EnemyTraitActive("wellborn"))
        {
            aiScore += 35;
            aiOpened = true;
            uiLog?.Append("Bien né : l'ennemi démarre avec 35 points (ouvert).");
            UpdateUI();
        }
        // Bien né (joueur, trait volé) : idem pour le joueur
        if (PlayerHasTrait("wellborn"))
        {
            playerScore += 35;
            playerOpened = true;
            uiLog?.Append("Bien né (joueur) : vous démarrez avec 35 points (ouvert).");
            UpdateUI();
        }
    }

    void RefreshCampaignUI()
    {
        // Sécurités
        int palierCount = GetPalierCount();
        int enemiesThisPalier = GetEnemyCountInPalier(palierIndex);

        // ---------- Textes HUD ----------
        if (palierHudText)
            palierHudText.text = $"Palier {Mathf.Clamp(palierIndex + 1, 1, Mathf.Max(1, palierCount))}/{Mathf.Max(1, palierCount)}";

        if (tourHudText)
            tourHudText.text = $"Adversaire {Mathf.Clamp(enemyIndex + 1, 1, Mathf.Max(1, enemiesThisPalier))}/{Mathf.Max(1, enemiesThisPalier)}";

        if (defeatsHudText)
            defeatsHudText.text = $"Défaites {Mathf.Max(0, defeatsCount)}/3";

        // ---------- Vignettes ennemis (courant + suivants) ----------
        if (enemyHudSlots != null && enemyHudSlots.Length > 0)
        {
            for (int i = 0; i < enemyHudSlots.Length; i++)
            {
                var img = enemyHudSlots[i];
                if (!img) continue;

                int idx = enemyIndex + i;
                var ei = (idx < GetEnemyCountInPalier(palierIndex)) ? PickEnemy(palierIndex, idx) : null;

                img.sprite = (ei != null) ? ei.icon : null;   // <-- icon ici
                img.enabled = true;
                img.color = img.sprite ? Color.white : new Color(1f, 1f, 1f, 0.2f);
            }
        }

        // ---------- Map ----------
        if (mapView)
        {
            var ei = PickEnemy(palierIndex, enemyIndex);
            mapView.ShowEnemy(ei);
            UpdateOpponentPortrait(ei); // <-- portrait central
        }
    }


    // ⛔ On n’avance que si le MATCH est fini (EndMatch appelé)

    void AdvanceToNextOpponentOrPalier()
    {
        // ⛔ Empêche d’avancer pendant une phase de sélection
        if (awaitingArtifactPick || awaitingTraitPick)
        {
            uiLog?.Append("Avance ignorée : sélection en cours.");
            return;
        }

        if (!matchOver)
        {
            uiLog?.Append("Impossible d’avancer : le match n’est pas clôturé.");
            return;
        }

        bool isDefeat = lastMatchWinner == AI_NAME;
        if (isDefeat)
        {
            defeatsCount++;
            uiLog?.Append($"Défaite enregistrée ({defeatsCount}/3).");

            if (defeatsCount >= 3)
            {
                // Trait Résurrection : lancer le dé SUN pour revenir dans la partie
                if (PlayerHasTrait("player.resurrection") && !resurrectionUsed)
                {
                    StartCoroutine(ResolveResurrection());
                    return;
                }

                ShowGameOver("3 défaites — Campagne perdue");
                return;
            }
        }

        bool crossedPalier = false;
        enemyIndex++;
        int enemiesInThisPalier = GetEnemyCountInPalier(palierIndex);
        if (enemyIndex >= enemiesInThisPalier)
        {
            crossedPalier = true;
            palierIndex++;
            enemyIndex = 0;

            // (Plus d'effacement automatique de défaite au passage de palier :
            //  le joueur peut à la place DÉTRUIRE un artefact pour retirer une défaite.)

            // (Plus d'octroi automatique de trait en fin de palier : le joueur choisit
            //  uniquement via les phases de sélection dédiées.)

            if (palierIndex >= GetPalierCount())
            {
                hintBanner?.Show("Campagne terminée ! (Tous les Paliers joués)");
                uiLog?.Append("Campagne terminée — retour au Palier 1.");
                palierIndex = 0;
                defeatsCount = 0;
                playerTraits.Clear();
                resurrectionUsed = false;
                RebuildTraitIcons();
            }
        }

        awaitingNextMatch = false;
        lastMatchWinner = "";

        // Fin de palier (après la phase d'achat) : choix d'un trait parmi les ennemis vaincus
        if (crossedPalier)
        {
            var options = new List<TraitDef>();
            var seen = new HashSet<string>();
            foreach (var e in defeatedEnemiesThisPalier)
            {
                if (e == null || string.IsNullOrEmpty(e.afflictionKey)) continue;
                var key = e.afflictionKey.Trim().ToLowerInvariant();
                if (PlayerHasTrait(key) || !seen.Add(key)) continue;
                options.Add(new TraitDef(key, e.afflictionName, e.afflictionDescription, e.afflictionIcon));
            }
            defeatedEnemiesThisPalier.Clear();

            if (options.Count > 0)
            {
                EnterTraitPick(options,
                    "Fin de palier : choisissez le trait d'un ennemi vaincu.",
                    StartNewMatch);
                return;
            }
        }

        StartNewMatch();
    }

    // Résurrection : à 3 défaites, on lance le dé SUN ; 5, 10 ou SUN → on rejoue le match perdu.
    IEnumerator ResolveResurrection()
    {
        resurrectionUsed = true;

        // Retire le trait (consommé, réussite ou non)
        for (int i = playerTraits.Count - 1; i >= 0; i--)
            if (playerTraits[i].key == "player.resurrection") playerTraits.RemoveAt(i);
        RebuildTraitIcons();

        // Le dé SUN de la table (ou le premier dé disponible en secours)
        DieView sunDie = null;
        foreach (var d in dice) if (d != null && d.isSunDie) { sunDie = d; break; }
        if (sunDie == null && dice.Count > 0) sunDie = dice[0];

        // Mise en scène : SEUL le dé SUN est visible pendant le lancé du destin.
        foreach (var d in dice)
            if (d != null) d.gameObject.SetActive(d == sunDie);

        hintBanner?.Show("Résurrection : lancez votre destin — le dé SUN décide...");
        yield return new WaitForSeconds(0.8f);

        if (sunDie != null)
        {
            sunDie.Roll();
            sunDie.Pulse(0.3f, 1.2f);
        }
        yield return new WaitForSeconds(1.0f);

        var f = sunDie != null ? sunDie.GetFace() : DieFace.Two;
        bool success = (f == DieFace.Five || f == DieFace.Ten || f == DieFace.Sun);

        if (success)
        {
            defeatsCount = 2; // on revient à 2 défaites et on rejoue le match perdu
            hintBanner?.Show($"Résurrection ({(f == DieFace.Sun ? "SUN" : ((int)f).ToString())}) ! Vous rejouez la partie perdue.");
            uiLog?.Append("Résurrection réussie — le match perdu est rejoué.");
            awaitingNextMatch = false;
            lastMatchWinner = "";
            yield return new WaitForSeconds(1.0f);
            StartNewMatch(); // même palierIndex / enemyIndex : on rejoue le même adversaire
        }
        else
        {
            hintBanner?.Show($"Résurrection échouée ({(int)f})...");
            uiLog?.Append("Résurrection échouée — Game Over.");
            yield return new WaitForSeconds(1.0f);
            ShowGameOver("3 défaites — Campagne perdue");
        }
    }


    bool IsInventoryFull()
    {
        if (playerInventory == null) return false;
        return playerInventory.Count >= INVENTORY_CAPACITY;
    }


    // ===================== GAME OVER =====================
    void ShowGameOver(string description)
    {
        isGameOverScreen = true;

        if (rollButton) rollButton.interactable = false;
        if (bankButton) bankButton.interactable = false;
        if (endRoundButton) endRoundButton.interactable = false;

        if (gameOverPanel) gameOverPanel.SetActive(true);
        if (gameOverTitleText) gameOverTitleText.text = "Game Over";
        if (gameOverDescText) gameOverDescText.text = string.IsNullOrEmpty(description) ? "Campagne terminée." : description;

        uiLog?.Append("Game Over — en attente de Rejouer.");
    }

    public void OnPressReplay()
    {
        if (gameOverPanel) gameOverPanel.SetActive(false);
        isGameOverScreen = false;

        palierIndex = 0;
        enemyIndex = 0;
        defeatsCount = 0;

        awaitingNextMatch = false;
        lastMatchWinner = "";

        // Reset campagne du système de traits
        playerTraits.Clear();
        defeatedEnemiesThisPalier.Clear();
        resurrectionUsed = false;
        RebuildTraitIcons();

        StartNewMatch();
    }

    // ===================== GESTION TOUR / PHASE / UI =====================
    void StartNewTurn(Turn who)
    {
        CleanupLingeringAddedDice();
        CleanupAddedDice(); // le(s) dé(s) surprise disparaissent au changement de tour
        ClearScoreModifier();

        currentTurn = who;

        if (who == Turn.AI)
        {
            aiHasBankedThisTurn = false;  // ← reset du garde-fou
            aiTurnsThisMatch++;           // compteur pour Runes magiques (IA)
        }
        else
        {
            playerTurnsThisMatch++;       // compteur pour Runes magiques (joueur)
            // (Coup d'estoc : plus de réarmement par tour — 1×/match, remis à zéro
            //  uniquement au match suivant, avec les autres drapeaux de traits.)
        }

        // Reset des états de Contre-Jeu à chaque changement de tour
        awaitingCounterPlay = false;
        aiBankMultiplier = 1f;
        aiFlashIndices.Clear();
        aiFlashCancelled = false;
        aiDiceChangedByCounter = false;

        turnScore = 0;
        flashPendingResolution = false;
        clauseClearedBySelection = false;
        wimpoutLostTurnScore = 0;
        pendingWinCheckOnContinue = false;

        frozenLocks.Clear();
        mutableLocks.Clear();
        flashLockIndices.Clear();
        flashVisualIndices.Clear();
        mutablePoints.Clear();
        eligibleLockIndices.Clear();
        eligibleLockPoints.Clear();
        lastRolledIndices.Clear();

        if (dice != null)
            foreach (var d in dice) if (d != null) d.ResetForNewTurn();

        // On efface l'ancienne annonce AVANT SetPhase/UpdateUI pour que la nouvelle
        // annonce d'état (ex: "A vous de jouer ! Appuyer sur Roll.") s'affiche et RESTE.
        if (who == Turn.Player)
        {
            hintBanner?.Hide();
            ResetBannerState();
        }

        SetPhase(currentTurn == Turn.Player ? Phase.AwaitFirstRoll : Phase.AITurnPlaying);
        UpdateUI();

        // 👇 Force l’aperçu à se mettre à jour (masquera si IA)
        RefreshScoreModRuntimePreview();

        if (currentTurn == Turn.AI)
            StartCoroutine(RunAITurn());
        else
            uiLog?.Append("Tour du Joueur");
    }

    void CleanupAddedDice()
    {
        if (addedDice.Count == 0) return;
        foreach (var ad in addedDice)
        {
            if (ad.view == null) continue;
            dice.Remove(ad.view);
            if (ad.destroyOnCleanup) Destroy(ad.view.gameObject);
            else ad.view.gameObject.SetActive(false); // instance de scène: juste cacher
        }
        addedDice.Clear();
        currentAddedDie = null;
    }


    void SetPhase(Phase p)
    {
        if (gameOver || awaitingNextMatch) return;

        phase = p;

        // ➜ Suggestion d'artefact quand le tour du joueur se termine.
        // On ne propose que s'il existe un artefact utilisable MAINTENANT (hors Contre-Jeu,
        // qui ne se joue que pendant le tour de l'IA).
        if (phase == Phase.WaitEnd && currentTurn == Turn.Player)
        {
            // Ne pas écraser le message de sauvetage après un wimpout (score récupérable)
            bool rescuePending = wimpoutLostTurnScore > 0 && PlayerHasRescueArtifact();
            bool hasUsableNow = PlayerHasNonCounterArtifact();
            if (hasUsableNow && !rescuePending)
                ShowAction("Souhaitez-vous utiliser un Artefact ? Glissez-le de l’inventaire sur la table — sinon CONTINUE.");
        }

        // Quand l’IA commence à jouer, on efface l’annonce en cours (fade-out configurable)
        if (currentTurn == Turn.AI && p == Phase.AITurnPlaying)
        {
            hintBanner?.Hide();
            ResetBannerState();
            ClearScoreModifier();
        }

        else
        {
            if (phase == Phase.AITurnWaitEnd && endRoundButton) endRoundButton.interactable = true;
        }

        ApplyButtonsState();
        UpdateUI();
    }

    // Applique le surlignage orange aux dés faisant partie d'un Flash (joueur ou adversaire),
    // bleu (couleur de scène) pour une sélection normale. Appelé depuis UpdateUI : couvre
    // ainsi tous les chemins (jet, Clause, artefacts, IA) sans toucher aux sites de verrouillage.
    void RefreshDiceFlashHighlights()
    {
        if (dice == null) return;
        for (int i = 0; i < dice.Count; i++)
        {
            if (dice[i] == null) continue;
            dice[i].SetFlashHighlight(flashLockIndices.Contains(i) || flashVisualIndices.Contains(i));
        }
    }

    void UpdateUI()
    {
        RefreshDiceFlashHighlights();
        WIN_THRESHOLD = GetWinThresholdForPalier(palierIndex);

        // Scores visibles : uniquement "score / objectif pts" (pas de nom).
        // Avant l'ouverture → objectif = seuil d'entrée (35).
        // Après l'ouverture → objectif = score du palier, ou le score à battre si une
        // phase finale a été déclenchée (mis à jour pour les DEUX joueurs).
        int goal = finalPhase ? targetScore : WIN_THRESHOLD;
        // Tithe of Time (boss) : le score cible du joueur est majoré
        int playerGoal = finalPhase ? targetScore : WIN_THRESHOLD + traitTitheExtra;

        if (playerScoreText)
            playerScoreText.text = playerOpened
                ? $"{playerScore} / {playerGoal} pts"
                : $"{playerScore} / {ENTRY_THRESHOLD} pts";

        if (aiScoreText)
            aiScoreText.text = aiOpened
                ? $"{aiScore} / {goal} pts"
                : $"{aiScore} / {ENTRY_THRESHOLD} pts";

        if (turnScoreText) turnScoreText.text = $"Score du tour : {turnScore}";

        // Inventaire visible de l'adversaire (léger : simples assignations)
        RefreshAIArtifactUI();

        // Si on est en mode sélection d’artefact, message dédié dans le bandeau
        if (awaitingArtifactPick)
        {
            ShowState("Choisis un artefact en cliquant sur sa carte.");
            if (turnLabel) turnLabel.text = ""; // on ne garde pas le TurnLabel pour les annonces
            return;
        }

        string finalTag = "";
        if (finalPhase)
        {
            string who = challenger == Turn.Player ? PLAYER_NAME : AI_NAME;
            finalTag = $" — Score à battre : {targetScore} (posé par {who})";
        }

        string baseText;
        if (currentTurn == Turn.Player)
        {
            baseText = phase switch
            {
                Phase.OpeningRollOff => "Tirage au sort : chaque joueur lance un dé — ROLL !",
                Phase.AwaitFirstRoll => playerOpened ? "A vous de jouer ! Appuyer sur Roll." : $"Au tour du Joueur — Lance ! (ouvrir ≥ {ENTRY_THRESHOLD})",
                Phase.Normal => flashPendingResolution ? $"Au tour du Joueur — Flash à dégager (face {(int)currentFlashFace})" : "Au tour du Joueur — Sélectionne puis ROLL",
                Phase.Clause => $"Au tour du Joueur — Clause (face {(int)currentFlashFace}) — Sélection facultative",
                Phase.WaitEnd => matchOver ? "Match terminé — CONTINUE" : "Tour terminé — CONTINUE",
                _ => "Au tour du Joueur"
            };
        }
        else
        {
            baseText = phase switch
            {
                Phase.AITurnPlaying => aiOpened ? "Au tour de l'IA" : $"Au tour de l'IA — doit ouvrir (≥ {ENTRY_THRESHOLD})",
                Phase.Clause => $"Au tour de l'IA — Clause…",
                Phase.AITurnWaitEnd => matchOver ? "Match IA terminé — CONTINUE" : "Tour de l’IA terminé — CONTINUE",
                _ => "Au tour de l'IA",
            };
        }

        // Annonce d'état via HintBanner uniquement
        ShowState(baseText + finalTag);

        // On laisse le TurnLabel vide pour libérer l'espace graphique
        if (turnLabel) turnLabel.text = "";

        RefreshScoreModRuntimePreview();
    }

    public bool CanUseRelanceNow()
    {
        if (gameOver || matchOver || isGameOverScreen) return false;
        if (currentTurn != Turn.Player) return false;

        // Autorise Normal, Clause, et WaitEnd (post-jet)
        bool inPostRollPhase = phase == Phase.Normal || phase == Phase.Clause || phase == Phase.WaitEnd;

        if (!inPostRollPhase) return false;

        // Sécurité: il doit y avoir eu au moins un jet (un sprite affiché suffit)
        bool hasFaces = dice != null && dice.Exists(d => d != null && d.faceImage && d.faceImage.sprite != null);
        return hasFaces;
    }


    // Vrai si le joueur peut relancer en phase Normal.
    // Miroir exact de la garde dans OnPressRoll : on doit avoir sélectionné un dé marquant
    // (sauf hot-dice où tous les dés scorent et la relance complète est imposée).
    bool CanPlayerRollInNormal()
    {
        if (dice == null || dice.Count == 0) return false;

        // TRAIT Happy Hour terminé : au-delà de 75 pts sur le tour, le joueur DOIT banker
        if (EnemyTraitActive("happyhour") && turnScore > 75) return false;

        bool anyUnlocked = dice.Any(d => d != null && !d.isLocked);
        bool allLocked = dice.All(d => d != null && d.isLocked);

        // Rien à relancer (et pas de reset hot-dice possible)
        if (!anyUnlocked && !allLocked) return false;

        // Hot-dice : les 5 dés scorent → relance complète obligatoire, toujours permise
        if (MustRollAllNow()) return true;

        // S'il existe un dé marquant disponible mais qu'aucun n'est sélectionné, la relance est interdite
        bool markingAvailable = HasAvailableMarkingSingles(out _) || eligibleLockIndices.Count > 0;
        if (markingAvailable && mutableLocks.Count == 0) return false;

        return anyUnlocked || allLocked;
    }

    void ApplyButtonsState()
    {
        // Mode sélection d’artefact OU de trait : le choix se fait en cliquant les cartes,
        // les boutons de jeu sont désactivés.
        if (awaitingArtifactPick || awaitingTraitPick)
        {
            if (rollButton) rollButton.interactable = false;
            if (bankButton) bankButton.interactable = false;
            if (endRoundButton) endRoundButton.interactable = false;
            return;
        }


        if (rollButton) rollButton.interactable = false;
        if (bankButton) bankButton.interactable = false;
        if (endRoundButton) endRoundButton.interactable = false;

        // Fenêtre de Contre-Jeu (tour IA en pause) : seul Next reste actif
        if (awaitingCounterPlay)
        {
            if (endRoundButton) endRoundButton.interactable = true;
            return;
        }

        // UI Game Over prime sur tout
        if (isGameOverScreen) return;

        if (matchOver)
        {
            if (endRoundButton) endRoundButton.interactable = true; // CONTINUE = avancer campagne (sauf artefacts)
            return;
        }
        if (gameOver) return;

        if (currentTurn == Turn.Player)
        {
            switch (phase)
            {
                case Phase.OpeningRollOff:
                    // ROLL pour lancer/relancer (égalité) ; une fois résolu, seul Next est actif.
                    if (rollButton) rollButton.interactable = !openingRollOffBusy && !awaitingRollOffNext;
                    if (endRoundButton) endRoundButton.interactable = awaitingRollOffNext;
                    break;

                case Phase.AwaitFirstRoll:
                    if (rollButton) rollButton.interactable = true; break;

                case Phase.Normal:
                    {
                        int entry = ENTRY_THRESHOLD;
                        int entryCheckScore = GetEffectiveTurnScoreForEntry(); // ← score effectif (mods)

                        bool allLocked = dice != null && dice.Count > 0 && dice.All(d => d != null && d.isLocked);

                        // ROLL n'est cliquable QUE si la relance est réellement autorisée
                        // (miroir exact de la garde dans OnPressRoll : un dé marquant doit être sélectionné).
                        if (rollButton) rollButton.interactable = CanPlayerRollInNormal();

                        // ✅ Utilise le score effectif pour l’ouverture.
                        // Anti-"bank sans sélection" : s'il existe des dés marquants disponibles
                        // sur ce jet (eligibleLockIndices), il faut en avoir sélectionné au moins un
                        // (mutableLocks) pour banquer. S'il n'y a aucun dé marquant à sélectionner
                        // (ex : points ajoutés par un artefact), la banque reste possible.
                        bool selectionOk = mutableLocks.Count > 0 || eligibleLockIndices.Count == 0;
                        bool canBank = !flashPendingResolution && selectionOk &&
                                    (playerOpened ? (turnScore > 0) : (entryCheckScore >= entry));

                        if (allLocked) canBank = false; // relance obligatoire si 5 scorent
                        if (bankButton) bankButton.interactable = canBank;
                    }
                    break;

                case Phase.Clause:
                    // Pendant une Clause, on peut relancer (tenter la Clause) mais PAS banquer :
                    // le Flash doit d'abord être dégagé en sélectionnant un dé marquant
                    // (ce qui bascule en phase Normal où BANK redevient disponible).
                    if (rollButton) rollButton.interactable = true;
                    if (bankButton) bankButton.interactable = false;
                    break;

                case Phase.WaitEnd:
                    if (endRoundButton) endRoundButton.interactable = true; break;
            }
        }
        else
        {
            if (phase == Phase.AITurnWaitEnd && endRoundButton) endRoundButton.interactable = true;
        }
    }

    // ===== Extern: ciblage de dé pour artefacts =====
    private bool extPickActive = false;
    private System.Func<int, bool> extPickFilter = null;
    private System.Action<int> extOnPicked = null;
    void CancelExternalDiePick()
    {
        extPickActive = false; extPickFilter = null; extOnPicked = null;
    }


    // ===================== INPUTS =====================
    void OnDieClicked(DieView die)
    {
        // 🔹 Interception: ciblage artefact avant la logique standard
        if (extPickActive)
        {
            int dieIndex = dice.IndexOf(die); // ← AU LIEU DE int idx = ...
            if (dieIndex >= 0 && (extPickFilter == null || extPickFilter(dieIndex)))
            {
                var cb = extOnPicked;
                CancelExternalDiePick();
                cb?.Invoke(dieIndex);
            }
            else
            {
                ShowAction("Choisis un dé valide pour cet artefact.");
            }
            return; // on ne passe PAS dans la logique normale
        }

        // --- Priorité: cible demandée par un artefact ---
        if (onExternalDiePicked != null)
        {
            int k = dice.IndexOf(die);
            if (k >= 0 && (externalDiePickFilter == null || externalDiePickFilter(k)))
            {
                var cb = onExternalDiePicked;
                ClearExternalDiePick();
                cb?.Invoke(k);
            }
            else
            {
                ShowAction("Cible invalide pour l’artefact.");
            }
            return;
        }

        if (awaitingArtifactPick || awaitingTraitPick) return; // pas de sélection de dé durant une sélection de carte
        if (gameOver || matchOver || isGameOverScreen) return;
        if (currentTurn != Turn.Player) return;
        if (phase != Phase.Normal && phase != Phase.Clause) return;

        // Filet de sécurité : un Flash « en attente » en phase Normale est un état incohérent
        // (la résolution d'un Flash se fait en phase Clause). Le laisser passer bloquait la
        // partie — les clics étaient ignorés sans message et ROLL/BANK restaient grisés.
        // On répare l'état au lieu de figer le jeu.
        if (flashPendingResolution && phase == Phase.Normal)
        {
            ClearClauseState();
            clauseClearedBySelection = false;
            uiLog?.Append("État incohérent corrigé : Flash en attente hors Clause.");
            ApplyButtonsState();
        }

        int idx = dice.IndexOf(die);
        if (idx < 0) return;

        if (flashLockIndices.Contains(idx) || (frozenLocks.Contains(idx) && !mutableLocks.Contains(idx)))
        {
            ShowAction("Ce dé est bloqué (Flash ou jet précédent).");
            return;
        }

        bool isEligible = eligibleLockIndices.Contains(idx);

        if (die.isLocked)
        {
            if (!mutableLocks.Contains(idx))
            {
                ShowAction("Ce dé n'est plus modifiable.");
                return;
            }

            die.SetLocked(false);
            mutableLocks.Remove(idx);
            if (mutablePoints.TryGetValue(idx, out int pts))
            {
                turnScore -= pts;
                mutablePoints.Remove(idx);
                UpdateUI();
            }

            // Le Flash n'avait été dégagé que par cette sélection : plus aucun dé
            // sélectionné → la Clause redevient active.
            if (clauseClearedBySelection && mutableLocks.Count == 0)
            {
                clauseClearedBySelection = false;
                flashPendingResolution = true;
                ShowAction($"Flash {(int)currentFlashFace} à dégager — sélectionne un 5/10/SUN ou ROLL pour la Clause.");
                uiLog?.Append("Sélection annulée : la Clause est réarmée.");
                SetPhase(Phase.Clause);
            }

            ApplyButtonsState();

            if (MustRollAllNow())
                ShowAction("Tous les dés scorent : relance OBLIGATOIRE. Appuie sur ROLL.");
            return;
        }
        else
        {
            if (!isEligible)
            {
                ShowAction("Sélectionne uniquement un 5, 10 (ou SUN=10) disponible sur ce jet.");
                return;
            }

            int add = eligibleLockPoints.TryGetValue(idx, out int pts2) ? pts2 : 0;
            die.SetLocked(true);
            mutableLocks.Add(idx);
            mutablePoints[idx] = add;
            turnScore += add;
            UpdateUI();
            ApplyButtonsState();

            if (phase == Phase.Clause && flashPendingResolution && mutableLocks.Count > 0)
            {
                flashPendingResolution = false;
                clauseClearedBySelection = true; // annulable tant que la sélection n'est pas figée (ROLL/BANK)

                // Message clair selon qu'on peut banker maintenant ou non (seuil d'ouverture).
                int entryCheck = GetEffectiveTurnScoreForEntry();
                if (!playerOpened && entryCheck < ENTRY_THRESHOLD)
                    ShowAction($"Flash dégagé ! Il te faut ≥ {ENTRY_THRESHOLD} pour ouvrir (tu as {entryCheck}) — ROLL pour continuer.");
                else
                    ShowAction("Flash dégagé ! Tu peux BANK, ou ROLL pour continuer.");

                uiLog?.Append("Clause dégagée par sélection du joueur.");
                SetPhase(Phase.Normal);
            }

            if (MustRollAllNow())
                ShowAction("Tous les dés scorent : relance OBLIGATOIRE. Appuie sur ROLL.");
        }
    }

    void OnPressRoll()
    {
        CleanupLingeringAddedDice();
        // Pendant la sélection d’artefact ou de trait, le choix se fait en cliquant les cartes
        if (awaitingArtifactPick || awaitingTraitPick) return;

        if (gameOver || matchOver || isGameOverScreen) return;

        if (currentTurn == Turn.Player)
        {
            // Tirage au sort du premier joueur
            if (phase == Phase.OpeningRollOff)
            {
                if (!openingRollOffBusy) StartCoroutine(ResolveOpeningRollOff());
                return;
            }

            if (phase == Phase.Normal)
            {
                // Un dé marquant DOIT être sélectionné pour relancer, sauf hot-dice
                // (tous les dés verrouillés → relance complète obligatoire).
                if (!CanPlayerRollInNormal())
                {
                    if (EnemyTraitActive("happyhour") && turnScore > 75)
                        ShowAction("Happy Hour terminé : plus de 75 points sur ce tour — tu dois BANK !");
                    else
                        ShowAction("Sélectionne au moins un dé marquant (5/10/SUN) avant de relancer.");
                    return;
                }
            }

            if (phase == Phase.AwaitFirstRoll || phase == Phase.Normal)
            { StartCoroutine(ResolvePlayerRoll()); return; }
            if (phase == Phase.Clause)
            { StartCoroutine(ResolvePlayerClauseStep()); return; }
        }
    }

    // Tirage au sort : dice[0] = dé du Joueur, dice[1] = dé de l'IA.
    // Le plus fort commence son tour ; égalité → on relance.
    // Les deux dés SIMPLES (non-Soleil) utilisés pour le tirage : dé du joueur et dé de l'IA.
    void GetRollOffDice(out DieView playerDie, out DieView aiDie)
    {
        playerDie = null;
        aiDie = null;
        if (dice == null) return;
        foreach (var d in dice)
        {
            if (d == null || d.isSunDie) continue; // on ignore le dé Soleil
            if (playerDie == null) playerDie = d;
            else if (aiDie == null) { aiDie = d; break; }
        }
    }

    // Pendant le tirage : seuls les 2 dés simples du tirage sont visibles.
    void ShowOnlyRollOffDice()
    {
        if (dice == null) return;
        GetRollOffDice(out var pDie, out var aDie);
        foreach (var d in dice)
        {
            if (d == null) continue;
            d.gameObject.SetActive(d == pDie || d == aDie);
        }
    }

    IEnumerator ResolveOpeningRollOff()
    {
        GetRollOffDice(out var playerDie, out var aiDie);

        if (playerDie == null || aiDie == null || playerDie == aiDie)
        {
            // Pas assez de dés pour un tirage : le joueur commence par défaut.
            HideDice(false);
            StartNewTurn(Turn.Player);
            yield break;
        }

        openingRollOffBusy = true;
        ApplyButtonsState();

        playerDie.Roll();
        yield return new WaitForSeconds(0.45f);
        aiDie.Roll();
        yield return new WaitForSeconds(0.6f);

        int pv = HighValueOf(playerDie.GetFace());
        int av = HighValueOf(aiDie.GetFace());

        if (pv == av)
        {
            ShowAction($"Égalité ({pv}) ! Relancez les dés — ROLL.");
            uiLog?.Append($"Tirage au sort : égalité ({pv}). Relance.");
            openingRollOffBusy = false;
            ApplyButtonsState();
            yield break;
        }

        bool playerStarts = pv > av;
        rollOffWinnerIsPlayer = playerStarts;
        ShowAction(playerStarts
            ? $"Vous remportez le tirage ({pv} contre {av}) — à vous de commencer ! Appuyez sur Next."
            : $"L'IA remporte le tirage ({av} contre {pv}) — elle commence. Appuyez sur Next.");
        uiLog?.Append($"Tirage au sort : Joueur {pv} / IA {av} → {(playerStarts ? "Joueur" : "IA")} commence.");

        // On attend que le joueur appuie sur Next (pas de démarrage automatique).
        openingRollOffBusy = false;
        awaitingRollOffNext = true;
        if (endRoundButton) endRoundButton.interactable = true;
    }

    void OnPressBank()
    {
        // Pendant la sélection d’artefact ou de trait, le choix se fait en cliquant les cartes
        if (awaitingArtifactPick || awaitingTraitPick) return;

        if (gameOver || matchOver || isGameOverScreen) return;
        if (currentTurn != Turn.Player || phase != Phase.Normal) return;
        if (turnScore <= 0) return;

        if (flashPendingResolution)
        { ShowAction("Un Flash doit être dégagé avant de banquer."); return; }

        // Il faut avoir sélectionné au moins un dé marquant si un tel dé est disponible sur CE jet
        // (empêche de banquer un Flash / des points d'un jet précédent sans rien sélectionner).
        if (mutableLocks.Count == 0 && eligibleLockIndices.Count > 0)
        { ShowAction("Sélectionne au moins un dé marquant (5/10/SUN) avant de banquer."); return; }

        int entry = (rules != null) ? rules.entryThreshold : 35;
        int entryCheckScore = GetEffectiveTurnScoreForEntry(); // ← score effectif (mods)    

        if (!playerOpened && entryCheckScore < entry)
        {
            ShowAction($"Besoin de ≥ {entry} points pour ouvrir.");
            return;
        }

        if (!playerOpened && entryCheckScore >= entry)
            playerOpened = true;

        if (MustRollAllNow())
        { ShowAction("Relance obligatoire des 5 dés avant de pouvoir banquer."); return; }

        foreach (var idx in mutableLocks) frozenLocks.Add(idx);
        mutableLocks.Clear();
        mutablePoints.Clear();
        eligibleLockIndices.Clear();
        eligibleLockPoints.Clear();
        clauseClearedBySelection = false; // sélection figée par la banque
        wimpoutLostTurnScore = 0;

        // ==== NOUVEAU : appliquer le modificateur avant d'ajouter au total ====
        string modBadge;
        int adjusted = ApplyScoreModsOnBank(turnScore, out modBadge);

        // ---- TRAIT Boss Obole de Charon : chaque bank du joueur perd 10% ----
        if (EnemyTraitActive("boss.obole"))
        {
            int before = adjusted;
            adjusted = Mathf.RoundToInt(adjusted * 0.9f);
            uiLog?.Append($"Trait Boss Obole de Charon : bank {before} → {adjusted} (-10%).");
            hintBanner?.Show($"Obole de Charon : le Boss prélève sa dîme ({before} → {adjusted}).");
        }

        // ==== Crédit du tour au score total du joueur ====
        playerScore += adjusted;
        turnScore = 0;
        uiLog?.Append($"Joueur banque {adjusted} points (total : {playerScore}).");
        UpdateUI();

        // ---- TRAIT Verre Empoisonné (ennemi) : 1×/match, joueur > 100 pts → score divisé par 2 ----
        if (EnemyTraitActive("poisonglass") && !traitPoisonGlassUsedVsPlayer && playerScore > 100)
        {
            traitPoisonGlassUsedVsPlayer = true;
            RebuildTraitIcons();
            int before = playerScore;
            playerScore = Mathf.RoundToInt(playerScore / 2f);
            ShowAction($"Verre Empoisonné : votre score est divisé par 2 ({before} → {playerScore}) !");
            uiLog?.Append($"Trait Verre Empoisonné : score joueur {before} → {playerScore}.");
            UpdateUI();
        }

        // ---- TRAIT Boss Tithe of Time : banker remet le compteur de jets à zéro ----
        traitTitheRollStreak = 0;

        // ---- TRAIT Impôts bourgeois (joueur) : égalité de scores → l'adversaire cède 20% ----
        CheckBourgeoisTax();

        // Badge pour les mods "du tour"
        if (scoreModLabel)
        {
            if (!string.IsNullOrEmpty(modBadge))
            {
                scoreModLabel.gameObject.SetActive(true);
                scoreModLabel.text = $"= {adjusted} ({modBadge})";
            }
            else
            {
                scoreModLabel.gameObject.SetActive(false);
                scoreModLabel.text = "";
            }
        }

        // Affichage final (per-turn)
        if (scoreModLabel)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(modBadge)) parts.Add(modBadge);

            if (parts.Count > 0)
            {
                scoreModLabel.gameObject.SetActive(true);
            }
            else
            {
                scoreModLabel.gameObject.SetActive(false);
                scoreModLabel.text = "";
            }
        }

        // --- DÉMARRAGE AUTO DE LA "CHASE" IA S'IL FAUT ---
        bool finalPhaseAvant = finalPhase;
        CheckWinConditionOnTurnEnded();

        // Si cette bank vient de déclencher la phase finale côté Joueur,
        // on donne immédiatement la main à l'IA (pas besoin d'appuyer sur CONTINUE).
        if (!finalPhaseAvant && finalPhase && challenger == Turn.Player && !gameOver && !matchOver && !isGameOverScreen)
        {
            StartNewTurn(Turn.AI);
            return;
        }

        // Sinon, comportement normal
        SetPhase(Phase.WaitEnd);
    }




    void OnPressEndRound()
    {
        // Pendant la sélection d’artefact ou de trait, le choix se fait en cliquant les cartes
        if (awaitingArtifactPick || awaitingTraitPick) return;

        // Tirage résolu → Next lance le premier tour.
        if (awaitingRollOffNext)
        {
            awaitingRollOffNext = false;
            if (endRoundButton) endRoundButton.interactable = false;
            HideDice(false);
            StartNewTurn(rollOffWinnerIsPlayer ? Turn.Player : Turn.AI);
            return;
        }

        // Fenêtre de Contre-Jeu ouverte : Next = laisser l'IA continuer/terminer son tour
        if (awaitingCounterPlay)
        {
            awaitingCounterPlay = false;
            // Annule un éventuel ciblage de dé en attente (artefact non finalisé)
            CancelExternalDiePick();
            ClearExternalDiePick();
            if (endRoundButton) endRoundButton.interactable = false;
            return;
        }

        // Fin de match annoncée → Next ouvre la phase d’obtention d’artefact.
        // À la fin des paliers 2 et 4 (dernier adversaire), le joueur choisit d'abord
        // un trait de joueur, AVANT la phase d'achat.
        if (awaitingVictoryNext)
        {
            awaitingVictoryNext = false;
            if (endRoundButton) endRoundButton.interactable = false;

            bool lastOfPalier = enemyIndex >= GetEnemyCountInPalier(palierIndex) - 1;
            bool palier2or4 = (palierIndex == 1 || palierIndex == 3);

            // Récompense de fin de Palier 3 (palierIndex == 2) : DEUX phases d'obtention
            // au lieu d'une. Uniquement sur une victoire — une défaite garde sa phase
            // de consolation unique.
            if (lastOfPalier && palierIndex == 2 && lastMatchWinner == PLAYER_NAME)
            {
                extraArtifactPicks = 1;
                hintBanner?.Show("Palier 3 franchi : deux phases d'obtention d'artefact !");
                uiLog?.Append("Récompense de Palier 3 — 2 phases d'obtention.");
            }

            if (lastOfPalier && palier2or4)
            {
                var options = new List<TraitDef>();
                foreach (var t in PlayerTraitPool())
                    if (!PlayerHasTrait(t.key)) options.Add(t);

                if (options.Count > 0)
                {
                    int count = pendingArtifactPickCount;
                    EnterTraitPick(options,
                        "Fin de palier : choisissez un trait de Joueur en cliquant sur sa carte.",
                        () => EnterArtifactPick(count));
                    return;
                }
            }

            EnterArtifactPick(pendingArtifactPickCount);
            return;
        }

        // CONTINUE → si (et seulement si) MATCH fini
        if (matchOver) { AdvanceToNextOpponentOrPalier(); return; }
        if (gameOver || isGameOverScreen) return;

        bool canEnd =
            (currentTurn == Turn.Player && phase == Phase.WaitEnd) ||
            (currentTurn == Turn.AI && phase == Phase.AITurnWaitEnd);

        if (!canEnd) return;

        // Wimpout avec sauvetage possible : la fin de match a été différée jusqu'ici.
        // Le joueur a eu sa fenêtre pour jouer un artefact ; on tranche maintenant.
        if (pendingWinCheckOnContinue)
        {
            CheckWinConditionOnTurnEnded(); // remet aussi le drapeau à false
            if (gameOver || matchOver || isGameOverScreen) { ApplyButtonsState(); return; }
        }

        var next = (currentTurn == Turn.Player) ? Turn.AI : Turn.Player;
        ClearScoreModifier();
        StartNewTurn(next);
    }

    // ===================== ROLL / CLAUSE / IA =====================
    IEnumerator ResolvePlayerRoll()
    {
        bool isReroll = (phase == Phase.Normal); // relance (≠ premier jet du tour)

        foreach (var idx in mutableLocks) frozenLocks.Add(idx);
        mutableLocks.Clear();
        mutablePoints.Clear();
        clauseClearedBySelection = false; // la sélection est figée, le Flash est définitivement dégagé
        eligibleLockIndices.Clear();
        eligibleLockPoints.Clear();

        var indicesToRoll = dice.Select((d, i) => (d, i)).Where(t => t.d != null && !t.d.isLocked).Select(t => t.i).ToList();

        if (indicesToRoll.Count == 0)
        {
            // Hot-dice : relance complète obligatoire. Les dés d'AJOUT (LovePotion, Dé surprise…)
            // ne sont PAS relancés — ils sont retirés/détruits à ce moment (usage unique).
            CleanupLingeringAddedDice();
            CleanupAddedDice();

            foreach (var d in dice) if (d != null) d.SetLocked(false);
            frozenLocks.Clear();
            flashLockIndices.Clear();
            flashVisualIndices.Clear();
            indicesToRoll = Enumerable.Range(0, dice.Count).ToList();
        }

        foreach (var idx in indicesToRoll) dice[idx].Roll();
        lastRolledIndices.Clear();
        lastRolledIndices.AddRange(indicesToRoll);
        yield return null;

        ApplyPendingFaceOverrides(lastRolledIndices); // faces forcées (dev / artefacts)

        // ---- TRAIT Pistage (joueur) : le premier jet du match a 2 dés à 10 ----
        if (!playerHasRolledThisMatch && PlayerHasTrait("tracking") && lastRolledIndices.Count >= 2)
        {
            dice[lastRolledIndices[0]].SetFace(DieFace.Ten);
            dice[lastRolledIndices[1]].SetFace(DieFace.Ten);
            ShowAction("Pistage : votre premier jet contient 2 dés de 10 !");
        }
        playerHasRolledThisMatch = true;

        // ---- TRAIT Runes magiques (joueur) : 20% tous les 3 tours → flash aléatoire ----
        if (!isReroll && PlayerHasTrait("runes") && playerTurnsThisMatch > 0
            && playerTurnsThisMatch % 3 == 0 && lastRolledIndices.Count >= 3
            && aiRng.NextDouble() < 0.20)
        {
            var faces = new[] { DieFace.Two, DieFace.Three, DieFace.Four, DieFace.Five, DieFace.Six, DieFace.Ten };
            var flashFace = faces[aiRng.Next(faces.Length)];
            for (int k = 0; k < 3; k++) dice[lastRolledIndices[k]].SetFace(flashFace);
            ShowAction($"Runes magiques : un flash de {(int)flashFace} se matérialise !");
            uiLog?.Append($"Trait Runes magiques (joueur) : flash de {(int)flashFace}.");
        }

        // ---- TRAIT Boss Marée du Destin : la 1re relance du joueur est annulée,
        //      le Boss gagne la meilleure face du jet annulé ----
        if (isReroll && !traitTideUsed && EnemyTraitActive("boss.tide"))
        {
            traitTideUsed = true;
            RebuildTraitIcons();
            int best = lastRolledIndices.Max(i => HighValueOf(dice[i].GetFace()));
            aiScore += best;
            ShowAction($"Marée du Destin : votre relance est engloutie — le Boss gagne {best} pts !");
            uiLog?.Append($"Trait Boss Marée du Destin : +{best} au Boss, jet relancé.");
            UpdateUI();
            yield return new WaitForSeconds(0.9f);
            foreach (var idx in lastRolledIndices) dice[idx].Roll(); // le vrai jet (miroir consommé)
            yield return null;
        }

        // ---- TRAIT Boss Tithe of Time : 3 jets consécutifs sans bank → +25 au score cible ----
        if (EnemyTraitActive("boss.tithe"))
        {
            traitTitheRollStreak++;
            if (traitTitheRollStreak >= 3)
            {
                traitTitheRollStreak = 0;
                traitTitheExtra += 25;
                ShowAction($"Tithe of Time : +25 au score à atteindre (total +{traitTitheExtra}) !");
                uiLog?.Append($"Trait Boss Tithe of Time : score cible du joueur +25 (cumul +{traitTitheExtra}).");
            }
        }

        if (ENABLE_SUPERNOVA && lastRolledIndices.Count == 5 && lastRolledIndices.All(i => dice[i].GetFace() == DieFace.Ten))
        {
            ShowAction($"SUPERNOVA ! 5×10 — {PLAYER_NAME} gagne.");
            uiLog?.Append("SUPERNOVA — match terminé.");
            EndMatch(PLAYER_NAME);
            yield break;
        }

        var eval = EvaluateRoll(lastRolledIndices, scoreSingles: false);

        if (eval.createdFlash)
        {
            foreach (var i in eval.flashLockIndices)
            {
                dice[i].SetLocked(true);
                frozenLocks.Add(i);
                flashLockIndices.Add(i);
            }
            turnScore += eval.pointsGained;
            currentFlashFace = eval.flashFace;
            flashPendingResolution = true;
            UpdateUI();

            // --- APRÈS (laisse le choix au joueur) ---
            var remaining = lastRolledIndices.Where(i => !eval.flashLockIndices.Contains(i)).ToList();

            // ---- TRAIT Boss Soleil Noir : au premier flash du joueur, la plus petite
            //      face restante devient 2 (une seule fois) ----
            if (EnemyTraitActive("boss.blacksun") && !traitBlackSunUsed && remaining.Count > 0)
            {
                traitBlackSunUsed = true;
                RebuildTraitIcons();
                int lowest = remaining[0];
                foreach (var i in remaining)
                    if (HighValueOf(dice[i].GetFace()) < HighValueOf(dice[lowest].GetFace())) lowest = i;
                dice[lowest].SetFace(DieFace.Two);
                ShowAction("Soleil Noir : votre plus petite face est corrompue en 2 !");
                uiLog?.Append("Trait Boss Soleil Noir : un dé devient 2.");
            }

            // Au lieu d'auto-locker et d’ajouter les points, on ne fait que
            // calculer les dés marquants restants pour que le joueur choisisse.
            FillEligibleFromIndices(remaining);
            UpdateUI();

            if (eligibleLockIndices.Count > 0)
            {
                ShowAction($"FLASH {(int)currentFlashFace} (+{eval.pointsGained}) — sélectionne un 5/10/SUN pour dégager le Flash (puis BANK/ROLL), ou ROLL pour tenter la Clause.");
                uiLog?.Append($"FLASH {(int)currentFlashFace} (+{eval.pointsGained}) → Clause (choix laissé au joueur).");
            }
            else
            {
                ShowAction($"FLASH {(int)currentFlashFace} — ROLL pour tenter la Clause.");
                uiLog?.Append($"FLASH {(int)currentFlashFace} (+{eval.pointsGained}) → Clause (aucun single disponible).");
            }

            SetPhase(Phase.Clause);
            yield break;
        }
        else
        {
            FillEligibleFromIndices(lastRolledIndices);

            // ---- TRAIT Nœud Coulant (joueur) : 50% de relancer un dé si aucun dé marquant ----
            if (eligibleLockIndices.Count == 0 && PlayerHasTrait("noose")
                && lastRolledIndices.Count > 0 && aiRng.NextDouble() < 0.5)
            {
                ShowAction("Nœud Coulant : une seconde chance — un dé est relancé !");
                yield return new WaitForSeconds(0.6f);
                int pick = lastRolledIndices[aiRng.Next(lastRolledIndices.Count)];
                dice[pick].Roll();
                dice[pick].Pulse(0.22f, 1.12f);
                yield return new WaitForSeconds(0.3f);
                FillEligibleFromIndices(lastRolledIndices);
            }

            if (eligibleLockIndices.Count == 0)
            {
                wimpoutLostTurnScore = turnScore; // récupérable via un artefact de sauvetage
                turnScore = 0;
                UpdateUI();

                // Un artefact de Transformation/Relance peut encore renverser ce jet.
                bool canStillRescue = PlayerHasRescueArtifact();
                if (canStillRescue)
                    ShowAction(wimpoutLostTurnScore > 0
                        ? $"Wimpout ! Vous pouvez encore sauver vos {wimpoutLostTurnScore} pts : glissez un artefact sur la table — sinon CONTINUE."
                        : "Wimpout ! Un artefact peut encore sauver ce jet : glissez-le sur la table — sinon CONTINUE.");
                else
                    ShowAction("Wimpout ! Aucun point sur ce jet.");
                uiLog?.Append("Wimpout (aucun 5/10/SUN=10 et pas de Flash).");

                // Tant qu'un sauvetage reste possible, on ne clôt PAS le match : en phase finale
                // la vérification déclencherait une défaite avant que l'artefact soit joué.
                if (canStillRescue)
                {
                    pendingWinCheckOnContinue = true;
                    uiLog?.Append("Fin de match différée : un artefact de sauvetage est disponible.");
                }
                else CheckWinConditionOnTurnEnded();

                yield return new WaitForSeconds(0.25f);
                SetPhase(Phase.WaitEnd);
                yield break;
            }

            ShowAction("Sélectionne au moins un dé marquant (5/10/SUN), puis ROLL pour relancer le reste.");
            SetPhase(Phase.Normal);
        }
    }

    IEnumerator ResolvePlayerClauseStep()
    {
        var indicesToRoll = dice.Select((d, i) => (d, i)).Where(t => t.d != null && !t.d.isLocked).Select(t => t.i).ToList();

        if (indicesToRoll.Count == 0)
        {
            foreach (var d in dice) if (d != null) d.SetLocked(false);
            frozenLocks.Clear();
            flashLockIndices.Clear();
            flashVisualIndices.Clear();
            indicesToRoll = Enumerable.Range(0, dice.Count).ToList();
        }

        foreach (var idx in indicesToRoll) dice[idx].Roll();
        lastRolledIndices.Clear();
        lastRolledIndices.AddRange(indicesToRoll);
        yield return null;

        ApplyPendingFaceOverrides(lastRolledIndices); // faces forcées (dev / artefacts)

        if (ENABLE_SUPERNOVA && lastRolledIndices.Count == 5 && lastRolledIndices.All(i => dice[i].GetFace() == DieFace.Ten))
        { ShowAction($"SUPERNOVA ! 5×10 — {PLAYER_NAME} gagne."); EndMatch(PLAYER_NAME); yield break; }

        var eval = EvaluateRoll(lastRolledIndices, scoreSingles: false);

        if (eval.createdFlash)
        {
            foreach (var i in eval.flashLockIndices)
            {
                dice[i].SetLocked(true);
                frozenLocks.Add(i);
                flashLockIndices.Add(i);
            }
            turnScore += eval.pointsGained;
            UpdateUI();

            currentFlashFace = eval.flashFace;
            flashPendingResolution = true;

            var remaining = lastRolledIndices.Where(i => !eval.flashLockIndices.Contains(i)).Where(i => !dice[i].isLocked).ToList();
            FillEligibleFromIndices(remaining);

            ShowAction($"NOUVEAU FLASH {(int)currentFlashFace} — sélectionne 5/10/SUN ou ROLL pour continuer la Clause.");
            uiLog?.Append($"Nouveau FLASH {(int)currentFlashFace} (+{FLASH_SCORE[currentFlashFace]}).");
            SetPhase(Phase.Clause);
            yield break;
        }

        FillEligibleFromIndices(lastRolledIndices);

        if (eligibleLockIndices.Count > 0)
        { ShowAction("Tu peux sélectionner un 5/10/SUN pour prendre les points et dégager le Flash, ou ROLL pour continuer la Clause."); SetPhase(Phase.Clause); yield break; }

        // La face du Flash retombe ? On vérifie TOUS les dés libres (pas seulement le dernier jet),
        // pour ne jamais rater un dé qui autorise la poursuite de la Clause.
        var freeDice = Enumerable.Range(0, dice.Count).Where(i => dice[i] != null && !dice[i].isLocked).ToList();
        bool matchedFlashFace = freeDice.Any(idx => MapFlashableFace(dice[idx].GetFace()) == currentFlashFace);
        uiLog?.Append($"Clause {(int)currentFlashFace} : faces libres = {string.Join(",", freeDice.Select(i => (int)dice[i].GetFace()))} → retombe: {matchedFlashFace}.");
        if (matchedFlashFace)
        { ShowAction($"Clause : {(int)currentFlashFace} retombe — appuie sur ROLL pour continuer."); SetPhase(Phase.Clause); yield break; }

        // ---- TRAIT Coup d'estoc (joueur) : la Clause perdue offre une relance exceptionnelle
        //      (1×/match — le trait se réarme au prochain adversaire) ----
        if (PlayerHasTrait("estoc") && !estocUsedThisMatchPlayer)
        {
            estocUsedThisMatchPlayer = true;
            RebuildTraitIcons(); // marque le trait « (utilisé) » dans l'AddBox
            ShowAction("Coup d'estoc : le dé perdant vous accorde une relance — appuie sur ROLL !");
            uiLog?.Append("Trait Coup d'estoc (joueur) : relance exceptionnelle de la Clause.");
            SetPhase(Phase.Clause);
            yield break;
        }
        else if (!PlayerHasTrait("estoc"))
        {
            // Diagnostic : si le joueur pense posséder Coup d'estoc, la clé stockée diffère peut-être.
            uiLog?.Append($"Clause perdue — pas de Coup d'estoc (traits joueur : {(playerTraits.Count == 0 ? "aucun" : string.Join(", ", playerTraits.Select(t => t.key)))}).");
        }

        wimpoutLostTurnScore = turnScore; // récupérable via un artefact de sauvetage
        turnScore = 0;
        UpdateUI();

        // La Clause est perdue : le Flash n'est plus « en attente de résolution ».
        // Sans ce nettoyage, flashPendingResolution restait vrai en phase WaitEnd et un
        // artefact de relance ramenait le jeu en phase Normal avec un Flash fantôme —
        // les dés devenaient alors silencieusement incliquables (jeu bloqué).
        ClearClauseState();

        bool canRescueClause = PlayerHasRescueArtifact();
        if (canRescueClause)
            ShowAction(wimpoutLostTurnScore > 0
                ? $"Wimpout pendant Clause ! Vous pouvez encore sauver vos {wimpoutLostTurnScore} pts : glissez un artefact sur la table — sinon CONTINUE."
                : "Wimpout pendant Clause ! Un artefact peut encore sauver ce jet : glissez-le sur la table — sinon CONTINUE.");
        else
            ShowAction("Wimpout pendant Clause.");

        // Même report qu'au wimpout normal : pas de clôture tant qu'un sauvetage est possible.
        if (canRescueClause)
        {
            pendingWinCheckOnContinue = true;
            uiLog?.Append("Fin de match différée : un artefact de sauvetage est disponible (Clause).");
        }
        else CheckWinConditionOnTurnEnded();
        yield return new WaitForSeconds(0.25f);
        SetPhase(Phase.WaitEnd);
    }

    // ===================== CONTRE-JEU =====================
    // Vrai si une fenêtre de contre-jeu est ouverte pendant le tour de l'IA.
    public bool IsCounterPlayWindowOpen() => awaitingCounterPlay && currentTurn == Turn.AI
        && !gameOver && !matchOver && !isGameOverScreen;

    // Le joueur possède-t-il au moins un artefact de Contre-Jeu ?
    public bool PlayerHasCounterArtifact()
    {
        if (playerInventory == null) return false;
        for (int i = 0; i < playerInventory.Count; i++)
        {
            var a = playerInventory.GetAt(i);
            if (a != null && a.type == ArtifactType.ContreJeu) return true;
        }
        return false;
    }

    // Le joueur possède-t-il un artefact capable de SAUVER un tour perdu ?
    // Transformation (Fortifiant, Balance, Miroir), Relance (Os du Tricheur, Temps)
    // et Ajout (un dé supplémentaire peut créer un marquant ou un Flash).
    public bool PlayerHasRescueArtifact()
    {
        if (playerInventory == null) return false;
        for (int i = 0; i < playerInventory.Count; i++)
        {
            var a = playerInventory.GetAt(i);
            if (a != null && (a.type == ArtifactType.Transformation
                           || a.type == ArtifactType.Relance
                           || a.type == ArtifactType.Ajout))
                return true;
        }
        return false;
    }

    // Le joueur possède-t-il au moins un artefact NON Contre-Jeu (utilisable pendant son tour) ?
    public bool PlayerHasNonCounterArtifact()
    {
        if (playerInventory == null) return false;
        for (int i = 0; i < playerInventory.Count; i++)
        {
            var a = playerInventory.GetAt(i);
            if (a != null && a.type != ArtifactType.ContreJeu) return true;
        }
        return false;
    }

    // Fenêtre "avant/après un lancé de l'IA" : si le joueur a un artefact de Contre-Jeu,
    // on met l'IA en pause et on lui propose de le jouer (USE) ou de laisser filer (Next).
    IEnumerator AICounterPlayWindow()
    {
        if (gameOver || matchOver || isGameOverScreen) yield break;
        if (currentTurn != Turn.AI) yield break;       // jamais pendant le tour du joueur
        if (!PlayerHasCounterArtifact()) yield break; // rien à proposer → l'IA enchaîne

        // Le joueur a désactivé le Contre-Jeu : aucune pause, aucune barre de temps.
        // Le tour de l'IA se déroule comme s'il n'avait pas d'artefact de Contre-Jeu.
        if (disableCounterPlayToggle && disableCounterPlayToggle.isOn)
        {
            if (counterPlayTimerBar) counterPlayTimerBar.gameObject.SetActive(false);
            yield break;
        }

        awaitingCounterPlay = true;
        ShowAction("Contre-Jeu : glissez votre artefact sur la table pour le jouer, ou appuyez sur Next pour laisser l'IA jouer.");
        if (endRoundButton) endRoundButton.interactable = true;

        // Timer : barre qui se vide en counterPlayTimeout secondes → auto-Next à zéro.
        float elapsed = 0f;
        float duration = Mathf.Max(0.1f, counterPlayTimeout);
        if (counterPlayTimerBar)
        {
            counterPlayTimerBar.gameObject.SetActive(true);
            counterPlayTimerBar.fillAmount = 1f;
        }

        // Attente de la décision du joueur : Next, usage d'artefact(s), ou expiration du timer.
        while (awaitingCounterPlay && !gameOver && !matchOver && !isGameOverScreen)
        {
            // Le timer est suspendu pendant que le joueur délibère : inventaire ouvert
            // ou ciblage de dé en cours (artefact armé).
            bool timerPaused = (inventoryUI != null && inventoryUI.IsOpen)
                            || onExternalDiePicked != null
                            || extPickActive;

            if (!timerPaused)
            {
                elapsed += Time.deltaTime;
                if (counterPlayTimerBar)
                    counterPlayTimerBar.fillAmount = Mathf.Clamp01(1f - elapsed / duration);

                if (elapsed >= duration)
                {
                    // Auto-Next : la fenêtre expire, l'IA reprend
                    CancelExternalDiePick();
                    ClearExternalDiePick();
                    awaitingCounterPlay = false;
                    uiLog?.Append("Contre-Jeu : temps écoulé — l'IA reprend.");
                    break;
                }
            }

            yield return null;
        }

        if (counterPlayTimerBar) counterPlayTimerBar.gameObject.SetActive(false);
        awaitingCounterPlay = false;
        if (endRoundButton) endRoundButton.interactable = false;

        // Un contre-jeu a bougé des dés : l'IA RE-SÉLECTIONNE les dés marquants libres
        // (elle ne les relance pas — le résultat du contre-jeu est définitif).
        if (aiDiceChangedByCounter)
        {
            aiDiceChangedByCounter = false;
            yield return StartCoroutine(AIReselectScoringDice());
        }
    }

    // Après un contre-jeu, l'IA re-sélectionne (verrouille) ce qui est marquant parmi ses dés
    // restés libres, un par un, SANS les relancer — le résultat du contre-jeu est définitif.
    //
    // Un Flash formable parmi ces dés libres est reverrouillé en priorité : sans cela, un trio
    // désélectionné par un contre-jeu (ex : trois 6, qui ne valent rien à l'unité) resterait
    // libre et l'IA le relancerait au lieu de reformer son Flash.
    // ⚠️ Le Flash est reformé dès qu'il est possible, même si le trio résulte du contre-jeu
    // lui-même : un contre-jeu peut donc offrir un Flash à l'IA (choix de design assumé).
    // Aucune Clause n'est accordée ici — le Flash reformé rapporte ses points, sans continuation.
    IEnumerator AIReselectScoringDice()
    {
        if (dice == null) yield break;

        var unlocked = Enumerable.Range(0, dice.Count)
            .Where(i => dice[i] != null && !dice[i].isLocked).ToList();
        if (unlocked.Count == 0) yield break;

        // ---- 1) Flash formable parmi les dés libres (3 identiques, ou paire + Sun) ----
        var byFace = new Dictionary<DieFace, List<int>>();
        var suns = new List<int>();
        foreach (var i in unlocked)
        {
            var f = dice[i].GetFace();
            if (f == DieFace.Sun) suns.Add(i);
            else { if (!byFace.ContainsKey(f)) byFace[f] = new List<int>(); byFace[f].Add(i); }
        }

        var flashIdx = new List<int>();
        DieFace flashFace = DieFace.Two;
        var faceOrder = new List<DieFace> { DieFace.Ten, DieFace.Six, DieFace.Five, DieFace.Four, DieFace.Three, DieFace.Two };
        foreach (var face in faceOrder)
        {
            int n = byFace.ContainsKey(face) ? byFace[face].Count : 0;
            if (n >= 3)
            {
                flashFace = face;
                for (int k = 0; k < 3; k++) flashIdx.Add(byFace[face][k]);
                break;
            }
            if (n == 2 && suns.Count > 0)
            {
                flashFace = face;
                flashIdx.Add(byFace[face][0]);
                flashIdx.Add(byFace[face][1]);
                flashIdx.Add(suns[0]);
                break;
            }
        }

        if (flashIdx.Count == 3)
        {
            foreach (var i in flashIdx) flashVisualIndices.Add(i); // surlignage orange

            yield return StartCoroutine(AILockDiceOneByOne(
                flashIdx, pointsFor: null, lumpSumAfter: FLASH_SCORE[flashFace]));

            hintBanner?.Show($"L'adversaire reforme son Flash de {(int)flashFace} (+{FLASH_SCORE[flashFace]}).");
            uiLog?.Append($"IA : Flash de {(int)flashFace} reformé (+{FLASH_SCORE[flashFace]}).");

            // Les dés du Flash ne sont plus libres pour l'étape des singles.
            unlocked = unlocked.Where(i => !flashIdx.Contains(i)).ToList();
            if (unlocked.Count == 0) yield break;
        }

        // ---- 2) Singles marquants (5 / 10 / Sun) sur ce qui reste libre ----
        var counts = new Dictionary<DieFace, int>();
        foreach (var i in unlocked)
        {
            var f = dice[i].GetFace();
            if (f == DieFace.Sun) continue;
            if (!counts.ContainsKey(f)) counts[f] = 0;
            counts[f]++;
        }
        bool pairExists = counts.Values.Any(v => v >= 2);

        var toLock = new List<int>();
        foreach (var i in unlocked)
        {
            var f = dice[i].GetFace();
            if (f == DieFace.Five || f == DieFace.Ten) toLock.Add(i);
            else if (f == DieFace.Sun && !pairExists) toLock.Add(i);
        }
        if (toLock.Count == 0) yield break;

        yield return StartCoroutine(AILockDiceOneByOne(
            toLock,
            pointsFor: i =>
            {
                var f = dice[i].GetFace();
                if (f == DieFace.Five) return 5;
                if (f == DieFace.Ten || f == DieFace.Sun) return 10;
                return 0;
            }));
    }

    // ---- Effets de Contre-Jeu (appelés par les artefacts pendant la fenêtre) ----

    // Bourse percée : -25% sur la bank de l'IA pour ce tour.
    public void CounterPlay_PiercedPurse()
    {
        aiBankMultiplier = 0.75f;
        hintBanner?.Show("Bourse percée : la prochaine bank de l'IA sera réduite de 25%.");
        uiLog?.Append("Contre-Jeu : Bourse percée (bank IA -25%).");
    }

    // Un Flash de l'IA est-il actif (fenêtre juste après le Flash) ? — requis par la Corne de sommeil.
    public bool AIHasActiveFlash() => aiFlashIndices.Count >= 3 && IsCounterPlayWindowOpen();

    // Corne de sommeil : annule le Flash de l'IA et RELANCE immédiatement les trois dés concernés.
    // L'effet est appliqué tout de suite (pas besoin d'attendre Next) ; le joueur appuie ensuite
    // sur Next pour laisser l'IA reprendre (elle re-sélectionnera les dés marquants).
    public void CounterPlay_SleepingHorn()
    {
        if (aiFlashIndices.Count < 3)
        {
            hintBanner?.Show("Corne de sommeil : aucun Flash de l'IA à annuler.");
            return;
        }

        foreach (var i in aiFlashIndices)
        {
            if (i < 0 || i >= dice.Count || dice[i] == null) continue;
            dice[i].SetLocked(false);   // le Flash est annulé : les dés sont désélectionnés
            dice[i].Roll();             // ... et relancés immédiatement
            dice[i].Pulse(0.22f, 1.12f);
        }
        aiFlashIndices.Clear();
        aiFlashCancelled = true;        // le Flash ne sera pas rejoué (pas de Clause)

        ClearClauseState();
        RecomputeAIScoreFromLockedDice(); // retire les points du Flash annulé
        aiDiceChangedByCounter = true;    // l'IA re-sélectionnera après Next

        hintBanner?.Show("Corne de sommeil : Flash annulé — les trois dés sont relancés. Appuyez sur Next.");
        uiLog?.Append("Contre-Jeu : Corne de sommeil (Flash IA annulé + 3 dés relancés).");
    }

    // Valeur "haute" d'une face pour comparer les dés (Sun compté comme 10).
    static int HighValueOf(DieFace f) => (f == DieFace.Sun) ? 10 : (int)f;

    // Recalcule ENTIÈREMENT le score du tour de l'IA à partir de ses dés VERROUILLÉS actuels.
    // Détecte un éventuel Flash (3 identiques, ou paire + Sun) puis les singles (5/10/Sun) restants.
    // Utilisé après un artefact de Contre-Jeu qui modifie un dé, pour réévaluer proprement.
    void RecomputeAIScoreFromLockedDice()
    {
        var locked = Enumerable.Range(0, dice.Count).Where(i => dice[i] != null && dice[i].isLocked).ToList();

        var byFace = new Dictionary<DieFace, List<int>>();
        var suns = new List<int>();
        foreach (var i in locked)
        {
            var f = dice[i].GetFace();
            if (f == DieFace.Sun) suns.Add(i);
            else { if (!byFace.ContainsKey(f)) byFace[f] = new List<int>(); byFace[f].Add(i); }
        }

        int score = 0;
        var usedForFlash = new HashSet<int>();
        var faceOrder = new List<DieFace> { DieFace.Ten, DieFace.Six, DieFace.Five, DieFace.Four, DieFace.Three, DieFace.Two };

        // Un seul Flash possible avec 5 dés : on prend le premier trouvé (priorité aux hautes faces).
        foreach (var face in faceOrder)
        {
            int n = byFace.ContainsKey(face) ? byFace[face].Count : 0;
            if (n >= 3)
            {
                for (int k = 0; k < 3; k++) usedForFlash.Add(byFace[face][k]);
                score += FLASH_SCORE[face];
                break;
            }
            if (n == 2 && suns.Count > 0)
            {
                usedForFlash.Add(byFace[face][0]);
                usedForFlash.Add(byFace[face][1]);
                usedForFlash.Add(suns[0]);
                score += FLASH_SCORE[face];
                break;
            }
        }

        // Singles sur les dés verrouillés non utilisés par le Flash.
        foreach (var i in locked)
        {
            if (usedForFlash.Contains(i)) continue;
            var f = dice[i].GetFace();
            if (f == DieFace.Five) score += 5;
            else if (f == DieFace.Ten) score += 10;
            else if (f == DieFace.Sun) score += 10;
        }

        turnScore = score;
        UpdateUI();
    }

    // Relance UNE FOIS un dé posé de l'IA et le DÉSÉLECTIONNE (il quitte les dés posés).
    // Le score est réévalué sans lui ; après Next, l'IA le re-sélectionnera s'il est marquant
    // (elle ne le relancera pas une seconde fois).
    void CounterReturnLockedDie(int i)
    {
        if (i < 0 || i >= dice.Count || dice[i] == null) return;
        BreakAIFlashIfDieBelongsToIt(i);
        dice[i].Roll();               // relance unique
        dice[i].SetLocked(false);     // désélectionné
        dice[i].Pulse(0.22f, 1.12f);
        RecomputeAIScoreFromLockedDice();
        aiDiceChangedByCounter = true; // l'IA re-sélectionnera après Next (sans relancer)
    }

    // Si le dé ciblé appartient au Flash de l'IA, le Flash est brisé :
    // TOUS les dés du trio sont désélectionnés (ils ne forment plus rien).
    // L'IA re-sélectionnera ensuite ceux qui sont marquants par eux-mêmes (10, 5...).
    void BreakAIFlashIfDieBelongsToIt(int i)
    {
        if (!aiFlashIndices.Contains(i)) return;

        foreach (var k in aiFlashIndices)
            if (k >= 0 && k < dice.Count && dice[k] != null)
                dice[k].SetLocked(false);

        aiFlashCancelled = true;
        aiDiceChangedByCounter = true;
    }

    // Coup de table : relance un dé VERROUILLÉ (déjà scoré) aléatoire de l'IA, puis réévalue.
    public void CounterPlay_SlamTable()
    {
        var locked = Enumerable.Range(0, dice.Count).Where(i => dice[i] != null && dice[i].isLocked).ToList();
        if (locked.Count == 0)
        {
            hintBanner?.Show("Coup de table : l'IA n'a aucun dé posé à relancer.");
            return;
        }
        int idx = locked[aiRng.Next(locked.Count)];
        CounterReturnLockedDie(idx);
        hintBanner?.Show("Coup de table : un dé posé de l'IA est renvoyé et relancé.");
        uiLog?.Append($"Contre-Jeu : Coup de table (dé {idx + 1} relancé, score IA réévalué).");
    }

    // Clocher des Veilleurs : cible AUTOMATIQUEMENT le dé posé de plus HAUTE valeur.
    // Si plusieurs dés partagent cette valeur (ex: 3×10), le joueur choisit lequel.
    public void CounterPlay_WatchersTower()
    {
        var locked = Enumerable.Range(0, dice.Count).Where(i => dice[i] != null && dice[i].isLocked).ToList();
        if (locked.Count == 0)
        {
            hintBanner?.Show("Clocher des Veilleurs : l'IA n'a aucun dé posé.");
            return;
        }

        int maxVal = locked.Max(i => HighValueOf(dice[i].GetFace()));
        var top = locked.Where(i => HighValueOf(dice[i].GetFace()) == maxVal).ToList();

        if (top.Count == 1)
        {
            CounterReturnLockedDie(top[0]);
            hintBanner?.Show($"Clocher des Veilleurs : le dé le plus fort ({maxVal}) est renvoyé et relancé.");
            uiLog?.Append($"Contre-Jeu : Clocher des Veilleurs (dé {top[0] + 1} relancé).");
        }
        else
        {
            BeginExternalDiePick(i => top.Contains(i), i =>
            {
                CounterReturnLockedDie(i);
                hintBanner?.Show("Clocher des Veilleurs : le dé désigné est renvoyé et relancé.");
                uiLog?.Append($"Contre-Jeu : Clocher des Veilleurs (dé {i + 1} relancé).");
            }, $"Clocher des Veilleurs : plusieurs dés à {maxVal} — choisis lequel relancer.");
        }
    }

    // Un dé peut-il encore être affaibli par le Poison ? (un 2 est déjà au minimum)
    public bool CanPoisonTargetExist()
        => dice != null && Enumerable.Range(0, dice.Count)
            .Any(i => dice[i] != null && dice[i].isLocked && dice[i].GetFace() != DieFace.Two);

    // Poison douteux : le joueur désigne un dé VERROUILLÉ de l'IA et lui enlève 1 (face précédente),
    // puis on réévalue entièrement le score de l'IA (un Flash cassé perd ses points).
    // Un dé déjà sur 2 ne peut pas être ciblé (impossible de descendre plus bas).
    public void CounterPlay_QuestionablePoison()
    {
        bool Filter(int i) => i >= 0 && i < dice.Count && dice[i] != null
                             && dice[i].isLocked && dice[i].GetFace() != DieFace.Two;
        if (!Enumerable.Range(0, dice.Count).Any(Filter))
        {
            hintBanner?.Show("Poison douteux : aucun dé posé de l'IA ne peut être affaibli.");
            return;
        }
        BeginExternalDiePick(Filter, i =>
        {
            var f = dice[i].GetFace();
            var lower = f switch
            {
                DieFace.Sun => DieFace.Ten,
                DieFace.Ten => DieFace.Six,
                DieFace.Six => DieFace.Five,
                DieFace.Five => DieFace.Four,
                DieFace.Four => DieFace.Three,
                DieFace.Three => DieFace.Two,
                _ => DieFace.Two
            };
            BreakAIFlashIfDieBelongsToIt(i); // Flash brisé → tout le trio est désélectionné
            dice[i].SetFace(lower);
            RecomputeAIScoreFromLockedDice();
            aiDiceChangedByCounter = true;
            hintBanner?.Show($"Poison douteux : le dé passe à {(int)lower} (score IA réévalué).");
            uiLog?.Append($"Contre-Jeu : Poison douteux (dé {i + 1} → {(int)lower}).");
        }, "Poison douteux : choisis un dé posé de l'IA à affaiblir (-1).");
    }

    // Applique l'artefact de score de l'IA (Dot de mariage / Livre des Comptes) sur sa bank.
    int ApplyAIArtifactOnBank(int baseGain)
    {
        if (aiArtifact == null || aiArtifactUsed) return baseGain;

        var k = AIArtifactKey();
        if (k == "score.marriage_dot")
        {
            aiArtifactUsed = true;
            int boosted = baseGain * 2;
            ShowAction($"L'adversaire utilise « {aiArtifact.displayName} » : sa bank est doublée ({baseGain} → {boosted}) !");
            uiLog?.Append($"Artefact IA : {aiArtifact.displayName} (bank ×2).");
            return boosted;
        }
        if (k == "score.funeral_ledger" && baseGain >= 70)
        {
            aiArtifactUsed = true;
            int boosted = Mathf.RoundToInt(baseGain * 1.5f);
            ShowAction($"L'adversaire utilise « {aiArtifact.displayName} » : sa bank gagne +50% ({baseGain} → {boosted}) !");
            uiLog?.Append($"Artefact IA : {aiArtifact.displayName} (+50%).");
            return boosted;
        }
        return baseGain;
    }

    IEnumerator RunAITurn()
    {
        if (gameOver || matchOver || isGameOverScreen) yield break;
        if (aiHasBankedThisTurn) yield break;
        yield return new WaitForSeconds(AI_DELAY);

        // Artefact IA : Ticket d'entrée → ouverture immédiate (+35)
        if (aiArtifact != null && !aiArtifactUsed && !aiOpened && AIArtifactKey() == "score.entry_ticket")
        {
            aiArtifactUsed = true;
            aiScore += ENTRY_THRESHOLD;
            aiOpened = true;
            ShowAction($"L'adversaire utilise « {aiArtifact.displayName} » : +{ENTRY_THRESHOLD} pts et ouverture immédiate !");
            uiLog?.Append($"Artefact IA : {aiArtifact.displayName} (ouverture).");
            UpdateUI();
            yield return new WaitForSeconds(0.9f);
        }

        // ⚠️ Pas de fenêtre de contre-jeu AVANT le premier lancé : l'IA lance d'abord.
        yield return StartCoroutine(ResolveAIRoll(allUnlocked: true));
        if (phase == Phase.AITurnWaitEnd || gameOver || matchOver || isGameOverScreen) yield break;

        while (phase == Phase.AITurnPlaying && !gameOver && !matchOver && !isGameOverScreen)
        {
            // Fenêtre de contre-jeu APRÈS le lancé précédent (avant que l'IA rejoue/banque)
            yield return StartCoroutine(AICounterPlayWindow());
            if (gameOver || matchOver || isGameOverScreen) yield break;

            yield return new WaitForSeconds(AI_DELAY);

            if (dice != null && dice.All(d => d != null && d.isLocked))
            {
                yield return StartCoroutine(ResolveAIRoll(allUnlocked: true));
                if (phase == Phase.AITurnWaitEnd || gameOver || matchOver || isGameOverScreen) yield break;
                continue;
            }

            if (phase == Phase.Clause)
            {
                yield return StartCoroutine(AutoResolveClause(isAI: true));
                if (phase == Phase.AITurnWaitEnd || gameOver || matchOver || isGameOverScreen) yield break;
                continue;
            }

            int palierWin = GetWinThresholdForPalier(palierIndex);
            bool canOpenThisTurnIA = (turnScore >= ENTRY_THRESHOLD);
            bool iaCanBank = aiOpened || canOpenThisTurnIA;

            bool shouldBank = false;
            if (iaCanBank)
            {
                // ➜ CHASE posée par le joueur : banque dès que l'IA dépasse la barre
                if (finalPhase && challenger == Turn.Player && (aiScore + turnScore > targetScore))
                {
                    shouldBank = true;
                }
                else
                {
                    shouldBank = (aiScore + turnScore >= palierWin) || (turnScore >= 25);
                }
            }

            // GARDE UNIQUEMENT CE if(shouldBank) — celui avec la fenêtre Contre-Jeu :
            if (shouldBank)
            {

                // ⛔ Empêche tout double banking dans le même tour
                if (aiHasBankedThisTurn) { SetPhase(Phase.AITurnWaitEnd); yield break; }
                aiHasBankedThisTurn = true;

                // ➜ ICI SEULEMENT on ajoute le tour au total IA (avec éventuel malus Contre-Jeu)
                int aiGain = Mathf.RoundToInt(turnScore * aiBankMultiplier);
                if (aiBankMultiplier < 1f)
                    uiLog?.Append($"Bourse percée : bank IA {turnScore} → {aiGain} (×{aiBankMultiplier:0.##}).");
                aiGain = ApplyAIArtifactOnBank(aiGain); // Dot de mariage / Livre des Comptes de l'IA
                aiScore += aiGain;
                turnScore = 0;
                aiBankMultiplier = 1f; // consommé
                UpdateUI();

                if (!aiOpened && aiScore >= ENTRY_THRESHOLD) aiOpened = true;

                // ---- TRAIT Verre Empoisonné (joueur, volé) : IA > 100 pts → score divisé par 2 ----
                if (PlayerHasTrait("poisonglass") && !traitPoisonGlassUsedVsAI && aiScore > 100)
                {
                    traitPoisonGlassUsedVsAI = true;
                    RebuildTraitIcons();
                    int before = aiScore;
                    aiScore = Mathf.RoundToInt(aiScore / 2f);
                    ShowAction($"Verre Empoisonné : le score de l'IA est divisé par 2 ({before} → {aiScore}) !");
                    uiLog?.Append($"Trait Verre Empoisonné (joueur) : score IA {before} → {aiScore}.");
                    UpdateUI();
                }

                // ---- TRAIT Impôts bourgeois : égalité de scores après la bank IA ----
                CheckBourgeoisTax();

                bool finalPhaseAvant = finalPhase;
                Turn challengerAvant = challenger;
                CheckWinConditionOnTurnEnded();
                if (gameOver || matchOver || isGameOverScreen) yield break;

                // Fin de tour IA “classique”
                SetPhase(Phase.AITurnWaitEnd);
                yield break;
            }


            yield return StartCoroutine(ResolveAIRoll(allUnlocked: false));
            if (phase == Phase.AITurnWaitEnd || gameOver || matchOver || isGameOverScreen) yield break;
        }
    }


    void ClearClauseState()
    {
        flashPendingResolution = false;
        currentFlashFace = DieFace.Two;
        flashLockIndices.Clear();
        flashVisualIndices.Clear(); // le Flash n'existe plus : retour au surlignage normal
    }

    // Verrouille une série de dés de l'IA UN PAR UN, avec un petit délai + pulse,
    // pour que le joueur puisse suivre la sélection. Ajoute optionnellement des points par dé.
    IEnumerator AILockDiceOneByOne(List<int> indices, System.Func<int, int> pointsFor = null, int lumpSumAfter = 0)
    {
        if (indices != null)
        {
            foreach (var i in indices)
            {
                if (i < 0 || i >= dice.Count || dice[i] == null) continue;
                dice[i].SetLocked(true);
                dice[i].Pulse(0.22f, 1.12f);
                if (pointsFor != null)
                {
                    int pts = pointsFor(i);
                    if (pts != 0) { turnScore += pts; }
                }
                UpdateUI();
                yield return new WaitForSeconds(aiSelectDelay);
            }
        }

        if (lumpSumAfter != 0)
        {
            turnScore += lumpSumAfter;
            UpdateUI();
        }
    }

    IEnumerator ResolveAIRoll(bool allUnlocked)
    {
        List<int> indicesToRoll;
        if (allUnlocked)
        {
            foreach (var d in dice) if (d != null) d.SetLocked(false);
            frozenLocks.Clear();
            ClearClauseState();
            indicesToRoll = Enumerable.Range(0, dice.Count).ToList();
        }
        else
        {
            indicesToRoll = dice.Select((d, i) => (d, i)).Where(t => t.d != null && !t.d.isLocked).Select(t => t.i).ToList();
            if (indicesToRoll.Count == 0)
            {
                foreach (var d in dice) if (d != null) d.SetLocked(false);
                frozenLocks.Clear();
                ClearClauseState();
                indicesToRoll = Enumerable.Range(0, dice.Count).ToList();
            }
        }

        foreach (var idx in indicesToRoll) dice[idx].Roll();
        yield return new WaitForSeconds(0.1f);

        // ---- TRAIT Pistage (ennemi) : son premier jet de la partie a 2 dés de 10 ----
        if (!aiHasRolledThisMatch && EnemyTraitActive("tracking") && indicesToRoll.Count >= 2)
        {
            dice[indicesToRoll[0]].SetFace(DieFace.Ten);
            dice[indicesToRoll[1]].SetFace(DieFace.Ten);
            hintBanner?.Show("Pistage : le Chasseur ouvre avec 2 dés de 10 !");
            uiLog?.Append("Trait Pistage : premier jet IA avec 2×10.");
        }
        aiHasRolledThisMatch = true;

        // ---- TRAIT Runes magiques (ennemi) : 20% tous les 3 tours → flash aléatoire ----
        if (allUnlocked && EnemyTraitActive("runes") && aiTurnsThisMatch > 0
            && aiTurnsThisMatch % 3 == 0 && indicesToRoll.Count >= 3
            && aiRng.NextDouble() < 0.20)
        {
            var runeFaces = new[] { DieFace.Two, DieFace.Three, DieFace.Four, DieFace.Five, DieFace.Six, DieFace.Ten };
            var runeFace = runeFaces[aiRng.Next(runeFaces.Length)];
            for (int k = 0; k < 3; k++) dice[indicesToRoll[k]].SetFace(runeFace);
            hintBanner?.Show($"Runes magiques : la Magicienne invoque un flash de {(int)runeFace} !");
            uiLog?.Append($"Trait Runes magiques : flash IA de {(int)runeFace}.");
        }

        // ---- Artefact IA : Balance de la justice (2→5, 6→10) si au moins 2 dés concernés ----
        if (aiArtifact != null && !aiArtifactUsed && AIArtifactKey() == "transform.balance")
        {
            int balanceTargets = indicesToRoll.Count(i =>
                dice[i].GetFace() == DieFace.Two || dice[i].GetFace() == DieFace.Six);
            if (balanceTargets >= 2)
            {
                aiArtifactUsed = true;
                foreach (var i in indicesToRoll)
                {
                    var f = dice[i].GetFace();
                    if (f == DieFace.Two) dice[i].SetFace(DieFace.Five);
                    else if (f == DieFace.Six) dice[i].SetFace(DieFace.Ten);
                }
                hintBanner?.Show($"L'adversaire utilise « {aiArtifact.displayName} » : ses 2 et 6 se transforment !");
                uiLog?.Append($"Artefact IA : {aiArtifact.displayName} ({balanceTargets} dés transformés).");
                yield return new WaitForSeconds(0.7f);
            }
        }

        if (ENABLE_SUPERNOVA && indicesToRoll.Count == 5 && indicesToRoll.All(i => dice[i].GetFace() == DieFace.Ten))
        { EndMatch(AI_NAME); yield break; }

        var evalFlashOnly = EvaluateRoll(indicesToRoll, scoreSingles: false);

        if (evalFlashOnly.createdFlash)
        {
            currentFlashFace = evalFlashOnly.flashFace;

            // Surlignage orange du trio (visuel uniquement)
            foreach (var i in evalFlashOnly.flashLockIndices) flashVisualIndices.Add(i);

            // Verrouille le trio du Flash un par un, puis crédite les points du Flash
            yield return StartCoroutine(AILockDiceOneByOne(
                evalFlashOnly.flashLockIndices,
                pointsFor: null,
                lumpSumAfter: evalFlashOnly.pointsGained));

            // Fenêtre de contre-jeu JUSTE APRÈS le Flash de l'IA.
            // Les dés du Flash sont exposés pour la Corne de sommeil (qui l'annule et les relance
            // immédiatement). Le score n'est pas "validé" tant que le joueur n'a pas joué son
            // contre-jeu ou appuyé sur Next.
            aiFlashIndices.Clear();
            aiFlashIndices.AddRange(evalFlashOnly.flashLockIndices);

            yield return StartCoroutine(AICounterPlayWindow());
            aiFlashIndices.Clear(); // le Flash n'est plus "actif" pour la Corne hors de cette fenêtre
            if (gameOver || matchOver || isGameOverScreen) yield break;

            // Le Flash tient-il toujours ? (un contre-jeu a pu l'annuler / casser le trio)
            bool flashStillValid = !aiFlashCancelled
                && evalFlashOnly.flashLockIndices.Count >= 3
                && evalFlashOnly.flashLockIndices.All(i => dice[i] != null && dice[i].isLocked);
            if (flashStillValid)
            {
                int sameCount = evalFlashOnly.flashLockIndices.Count(i => dice[i].GetFace() == currentFlashFace);
                int sunCount = evalFlashOnly.flashLockIndices.Count(i => dice[i].GetFace() == DieFace.Sun);
                flashStillValid = (sameCount >= 3) || (sameCount >= 2 && sunCount >= 1);
            }
            aiFlashCancelled = false; // consommé

            // Flash brisé/annulé par un contre-jeu (Corne de sommeil, Coup de table, Clocher, Poison) :
            // les effets ont déjà réévalué le score et re-sélectionné ce qu'il fallait.
            // On saute simplement la Clause.
            if (!flashStillValid)
            {
                ClearClauseState();
                hintBanner?.Show("Le Flash de l'IA a été brisé.");
                uiLog?.Append("Contre-Jeu : Flash IA brisé (dés conservés).");
                SetPhase(Phase.AITurnPlaying);
                yield break;
            }

            // Flash conservé → on traite les singles restants, puis Clause si besoin.
            var remaining = indicesToRoll.Except(evalFlashOnly.flashLockIndices).ToList();
            var counts = new Dictionary<DieFace, int>();
            foreach (var i in remaining)
            {
                var f = dice[i].GetFace();
                if (f == DieFace.Sun) continue;
                if (!counts.ContainsKey(f)) counts[f] = 0;
                counts[f]++;
            }
            bool pairExists = counts.Values.Any(v => v >= 2);

            var singlesToLock = new List<int>();
            foreach (var i in remaining)
            {
                var f = dice[i].GetFace();
                if (f == DieFace.Five || f == DieFace.Ten) singlesToLock.Add(i);
                else if (f == DieFace.Sun && !pairExists) singlesToLock.Add(i);
            }

            if (singlesToLock.Count > 0)
            {
                // Verrouille les singles un par un, en créditant chaque dé au passage
                yield return StartCoroutine(AILockDiceOneByOne(
                    singlesToLock,
                    pointsFor: i =>
                    {
                        var f = dice[i].GetFace();
                        if (f == DieFace.Five) return 5;
                        if (f == DieFace.Ten || f == DieFace.Sun) return 10;
                        return 0;
                    }));

                ClearClauseState();
                SetPhase(Phase.AITurnPlaying);
                yield break;
            }

            SetPhase(Phase.Clause);
            yield return new WaitForSeconds(CLAUSE_START_DELAY);
            yield return StartCoroutine(AutoResolveClause(isAI: true));
            yield break;
        }

        var evalSingles = EvaluateRoll(indicesToRoll, scoreSingles: true);

        // ---- TRAIT Nœud Coulant (ennemi) : 50% de relancer un dé si aucun dé marquant ----
        if (!evalSingles.anyScoring && !evalSingles.createdFlash
            && EnemyTraitActive("noose") && indicesToRoll.Count > 0
            && aiRng.NextDouble() < 0.5)
        {
            hintBanner?.Show("Nœud Coulant : le Pendu relance un dé !");
            uiLog?.Append("Trait Nœud Coulant : relance d'un dé IA.");
            yield return new WaitForSeconds(0.6f);
            int pick = indicesToRoll[aiRng.Next(indicesToRoll.Count)];
            dice[pick].Roll();
            dice[pick].Pulse(0.22f, 1.12f);
            yield return new WaitForSeconds(0.4f);
            evalSingles = EvaluateRoll(indicesToRoll, scoreSingles: true);

            if (evalSingles.createdFlash)
            {
                // Le dé relancé complète un flash : on le verrouille avec ses points (sans Clause)
                foreach (var i in evalSingles.lockedThisRoll) dice[i].SetLocked(true);
                turnScore += evalSingles.pointsGained;
                UpdateUI();
                SetPhase(Phase.AITurnPlaying);
                yield break;
            }
        }

        // ---- Artefact IA : Os du Tricheur / Temps niv.1 → relance sur wimpout ----
        if (!evalSingles.anyScoring && !evalSingles.createdFlash
            && aiArtifact != null && !aiArtifactUsed && indicesToRoll.Count > 0
            && (AIArtifactKey() == "reroll_one_die" || AIArtifactKey() == "reroll_turn"))
        {
            aiArtifactUsed = true;
            bool rerollAll = AIArtifactKey() == "reroll_turn";
            ShowAction($"L'adversaire utilise « {aiArtifact.displayName} » : relance !");
            uiLog?.Append($"Artefact IA : {aiArtifact.displayName} (relance {(rerollAll ? "du jet" : "d'un dé")}).");
            yield return new WaitForSeconds(0.6f);

            if (rerollAll)
            {
                foreach (var i in indicesToRoll) dice[i].Roll();
            }
            else
            {
                int pick = indicesToRoll[aiRng.Next(indicesToRoll.Count)];
                dice[pick].Roll();
                dice[pick].Pulse(0.22f, 1.12f);
            }
            yield return new WaitForSeconds(0.4f);
            evalSingles = EvaluateRoll(indicesToRoll, scoreSingles: true);

            if (evalSingles.createdFlash)
            {
                foreach (var i in evalSingles.lockedThisRoll) dice[i].SetLocked(true);
                turnScore += evalSingles.pointsGained;
                UpdateUI();
                SetPhase(Phase.AITurnPlaying);
                yield break;
            }
        }

        if (!evalSingles.anyScoring)
        {
            turnScore = 0;
            UpdateUI();

            bool wasFinal = finalPhase;
            Turn wasChallenger = challenger;

            CheckWinConditionOnTurnEnded();

            if (gameOver || matchOver || isGameOverScreen) yield break;

            if (wasFinal && wasChallenger == Turn.Player)
            {
                // L’IA a échoué à dépasser → victoire immédiate du joueur.
                EndMatch(PLAYER_NAME);
                yield break;
            }

            // Fin de tour IA standard si on n’était pas dans la chase.
            SetPhase(Phase.AITurnWaitEnd);
            yield break;
        }

        // Verrouille les dés marquants un par un (avec crédit progressif) pour que le joueur suive
        yield return StartCoroutine(AILockDiceOneByOne(
            evalSingles.lockedThisRoll,
            pointsFor: i =>
            {
                var f = dice[i].GetFace();
                if (f == DieFace.Five) return 5;
                if (f == DieFace.Ten || f == DieFace.Sun) return 10;
                return 0;
            }));
    }

    IEnumerator AutoResolveClause(bool isAI)
    {
        bool estocRetryUsed = false; // Coup d'estoc (Chevalière) : une relance exceptionnelle par Clause

        var indicesToRoll = dice.Select((d, i) => (d, i)).Where(t => t.d != null && !t.d.isLocked).Select(t => t.i).ToList();
        if (indicesToRoll.Count == 0)
        {
            foreach (var d in dice) if (d != null) d.SetLocked(false);
            indicesToRoll = Enumerable.Range(0, dice.Count).ToList();
        }

        while (true)
        {
            foreach (var idx in indicesToRoll) dice[idx].Roll();
            yield return new WaitForSeconds(0.1f);

            if (ENABLE_SUPERNOVA && indicesToRoll.Count == 5 && indicesToRoll.All(i => dice[i].GetFace() == DieFace.Ten))
            { EndMatch(isAI ? AI_NAME : PLAYER_NAME); yield break; }

            if (isAI && indicesToRoll.Count == 1)
            {
                int onlyIdx = indicesToRoll[0];
                if (dice[onlyIdx].GetFace() == DieFace.Sun)
                {
                    dice[onlyIdx].SetLocked(true);
                    turnScore += 10;
                    UpdateUI();
                    SetPhase(isAI ? Phase.AITurnPlaying : Phase.Normal);
                    yield break;
                }
            }

            var eval = EvaluateRoll(indicesToRoll, scoreSingles: true);

            if (eval.anyScoring || eval.createdFlash)
            {
                foreach (var i in eval.lockedThisRoll) dice[i].SetLocked(true);
                turnScore += eval.pointsGained;
                UpdateUI();

                if (eval.createdFlash)
                {
                    currentFlashFace = eval.flashFace;
                    yield return new WaitForSeconds(CLAUSE_START_DELAY);
                    indicesToRoll = dice.Select((d, i) => (d, i)).Where(t => t.d != null && !t.d.isLocked).Select(t => t.i).ToList();
                    if (indicesToRoll.Count == 0)
                    {
                        foreach (var d in dice) if (d != null) d.SetLocked(false);
                        indicesToRoll = Enumerable.Range(0, dice.Count).ToList();
                    }
                    continue;
                }

                SetPhase(isAI ? Phase.AITurnPlaying : Phase.Normal);
                yield break;
            }

            bool matchedFlashFace = indicesToRoll.Any(idx => MapFlashableFace(dice[idx].GetFace()) == currentFlashFace);
            if (matchedFlashFace)
            {
                yield return new WaitForSeconds(CLAUSE_REPEAT_DELAY);
                continue;
            }

            // ---- TRAIT Coup d'estoc (ennemi) : la Chevalière relance le dé perdant (1×/Clause) ----
            if (isAI && EnemyTraitActive("estoc") && !estocRetryUsed)
            {
                estocRetryUsed = true;
                hintBanner?.Show("Coup d'estoc : la Chevalière relance le dé perdant !");
                uiLog?.Append("Trait Coup d'estoc : relance exceptionnelle de la Clause IA.");
                yield return new WaitForSeconds(CLAUSE_REPEAT_DELAY);
                continue;
            }

            // Wimpout
            if (!isAI) wimpoutLostTurnScore = turnScore; // récupérable via Os du Tricheur
            turnScore = 0;
            UpdateUI();
            if (isAI)
            {
                CheckWinConditionOnTurnEnded();
                SetPhase(Phase.AITurnWaitEnd);
            }
            else
            {
                SetPhase(Phase.WaitEnd);
            }
            yield break;
        }
    }

    // ===================== FIN DE MATCH & CONDITIONS =====================
    void CheckWinConditionOnTurnEnded()
    {
        // La vérification a lieu : un éventuel report devient caduc (évite un double passage,
        // qui en phase finale clôturerait le match à tort après une bank réussie).
        pendingWinCheckOnContinue = false;

        if (gameOver || matchOver || isGameOverScreen) return;

        int palierWin = GetWinThresholdForPalier(palierIndex);

        if (!finalPhase)
        {
            // Tithe of Time (boss) : le score cible du JOUEUR est majoré de traitTitheExtra
            if (playerScore >= palierWin + traitTitheExtra)
            {
                finalPhase = true; targetScore = playerScore; challenger = Turn.Player;
                ShowAction($"Le Joueur atteint {playerScore} ! L’IA doit dépasser.");
            }
            else if (aiScore >= palierWin)
            {
                finalPhase = true; targetScore = aiScore; challenger = Turn.AI;
                ShowAction($"L’IA atteint {aiScore} ! Le Joueur doit dépasser.");
            }
            return;
        }

        Turn chaser = (challenger == Turn.Player) ? Turn.AI : Turn.Player;
        if (currentTurn == chaser)
        {
            int chaserScore = (chaser == Turn.Player) ? playerScore : aiScore;
            if (chaserScore > targetScore)
            {
                targetScore = chaserScore;
                challenger = chaser;
                ShowAction($"Nouveau score à battre : {targetScore} !");
            }
            else
            {
                EndMatch(challenger == Turn.Player ? PLAYER_NAME : AI_NAME);
            }
        }
    }

    void EndMatch(string winner)
    {
        if (gameOver) return;
        gameOver = true;

        // marquer le match comme terminé (état de campagne)
        awaitingNextMatch = true;
        lastMatchWinner = winner;

        // Le trait de l'adversaire s'éteint avec le match (aucun effet résiduel,
        // et l'AddBoxP2 se vide immédiatement)
        activeEnemyTraitKeys.Clear();
        activeEnemyTraitInfos.Clear();
        RebuildTraitIcons();

        // Un artefact "armé" (ciblage de dé en attente) ne doit pas survivre à la fin du match
        CancelExternalDiePick();
        ClearExternalDiePick();

        // L'inventaire se ferme automatiquement en fin de match
        if (inventoryUI != null && inventoryUI.IsOpen) inventoryUI.Hide();

        // Désactiver les boutons de jeu standard; pendant la phase d’obtention on gère 1/2/3 à part
        if (rollButton) rollButton.interactable = false;
        if (bankButton) bankButton.interactable = false;
        if (endRoundButton) endRoundButton.interactable = false;

        if (winner == PLAYER_NAME)
        {
            // Trait de l'ennemi vaincu mémorisé pour le choix de fin de palier
            if (currentEnemyInfo != null && !string.IsNullOrEmpty(currentEnemyInfo.afflictionKey))
                defeatedEnemiesThisPalier.Add(currentEnemyInfo);

            // 👉 Annonce de victoire ; le joueur appuie sur Next pour ouvrir la phase d’obtention (3 artefacts)
            awaitingVictoryNext = true;
            pendingArtifactPickCount = 3;
            ShowAction("Victoire ! Le joueur remporte le match. Appuyer sur Next.");
            uiLog?.Append("Victoire du Joueur — Next pour la sélection d’artefact.");
            if (endRoundButton) endRoundButton.interactable = true;
            return;
        }

        // Défaite joueur.
        // Si c'est la 3e défaite (fin de campagne), on NE propose PAS de phase d'achat :
        // on va directement au Game Over via CONTINUE.
        bool willBeGameOver = (defeatsCount + 1) >= 3;
        if (willBeGameOver)
        {
            awaitingVictoryNext = false;
            ShowAction($"Défaite ! {winner} remporte le match. 3e défaite — CONTINUE pour terminer.");
            uiLog?.Append("3e défaite — Game Over (pas de phase d’obtention).");
            if (turnLabel) turnLabel.text = $"Défaite — {winner} a gagné. Appuie sur CONTINUE.";
            if (endRoundButton) endRoundButton.interactable = true; // CONTINUE → Advance → ShowGameOver
            RefreshCampaignUI();
            return;
        }

        // Sinon → phase d’obtention de consolation avec UN SEUL artefact
        awaitingVictoryNext = true;
        pendingArtifactPickCount = 1;
        ShowAction($"Défaite ! {winner} remporte le match. Appuyer sur Next pour choisir un artefact.");
        uiLog?.Append("Défaite du Joueur — Next pour la sélection d’artefact (1 seul).");
        if (endRoundButton) endRoundButton.interactable = true;
        RefreshCampaignUI();
    }


    // ===================== OUTILS SCORE =====================
    DieFace MapFlashableFace(DieFace f) => (f == DieFace.Sun) ? DieFace.Sun : f;

    struct RollEval
    {
        public bool anyScoring;
        public int pointsGained;
        public bool createdFlash;
        public DieFace flashFace;
        public List<int> lockedThisRoll;
        public List<int> flashLockIndices;
    }

    RollEval EvaluateRoll(List<int> indicesRolled, bool scoreSingles)
    {
        var eval = new RollEval
        {
            anyScoring = false,
            pointsGained = 0,
            createdFlash = false,
            flashFace = DieFace.Two,
            lockedThisRoll = new List<int>(),
            flashLockIndices = new List<int>()
        };

        var counts = new Dictionary<DieFace, List<int>>();
        var sunIdx = new List<int>();

        foreach (var idx in indicesRolled)
        {
            var f = dice[idx].GetFace();
            if (f == DieFace.Sun) sunIdx.Add(idx);
            else
            {
                if (!counts.ContainsKey(f)) counts[f] = new List<int>();
                counts[f].Add(idx);
            }
        }

        var faceOrder = new List<DieFace> { DieFace.Ten, DieFace.Six, DieFace.Five, DieFace.Four, DieFace.Three, DieFace.Two };

        // FLASH (3 naturels ou paire + SUN)
        foreach (var face in faceOrder)
        {
            int n = counts.ContainsKey(face) ? counts[face].Count : 0;

            if (n >= 3)
            {
                var triple = counts[face].Take(3).ToList();
                foreach (var i in triple) eval.flashLockIndices.Add(i);
                eval.pointsGained += FLASH_SCORE[face];
                eval.createdFlash = true;
                eval.flashFace = face;
                break;
            }
            if (n == 2 && sunIdx.Count > 0)
            {
                var triple = new List<int> { counts[face][0], counts[face][1], sunIdx[0] };
                foreach (var i in triple) eval.flashLockIndices.Add(i);
                eval.pointsGained += FLASH_SCORE[face];
                eval.createdFlash = true;
                eval.flashFace = face;
                break;
            }
        }

        if (eval.createdFlash)
        {
            foreach (var i in eval.flashLockIndices) eval.lockedThisRoll.Add(i);
            return eval;
        }

        // singles (5/10/SUN=10 si aucune paire à compléter)
        bool pairExists = counts.Any(kv => kv.Value.Count >= 2);
        foreach (var idx in indicesRolled)
        {
            var f = dice[idx].GetFace();
            if (f == DieFace.Five || f == DieFace.Ten) eval.anyScoring = true;
            else if (f == DieFace.Sun && !pairExists) eval.anyScoring = true;
        }

        if (!scoreSingles) return eval;

        foreach (var idx in indicesRolled)
        {
            var f = dice[idx].GetFace();
            if (f == DieFace.Five) { eval.pointsGained += 5; eval.lockedThisRoll.Add(idx); }
            else if (f == DieFace.Ten) { eval.pointsGained += 10; eval.lockedThisRoll.Add(idx); }
            else if (f == DieFace.Sun && !pairExists) { eval.pointsGained += 10; eval.lockedThisRoll.Add(idx); }
        }
        eval.anyScoring = eval.pointsGained > 0 || eval.createdFlash;
        return eval;
    }

    void FillEligibleFromIndices(List<int> indices)
    {
        eligibleLockIndices.Clear();
        eligibleLockPoints.Clear();

        var counts = new Dictionary<DieFace, int>();
        foreach (var idx in indices)
        {
            var f = dice[idx].GetFace();
            if (f == DieFace.Sun) continue;
            if (!counts.ContainsKey(f)) counts[f] = 0;
            counts[f]++;
        }
        bool pairExists = counts.Values.Any(v => v >= 2);

        foreach (var idx in indices)
        {
            var f = dice[idx].GetFace();
            if (f == DieFace.Five) { eligibleLockIndices.Add(idx); eligibleLockPoints[idx] = 5; }
            else if (f == DieFace.Ten) { eligibleLockIndices.Add(idx); eligibleLockPoints[idx] = 10; }
            else if (f == DieFace.Sun && !pairExists) { eligibleLockIndices.Add(idx); eligibleLockPoints[idx] = 10; }
        }
    }

    bool HasAvailableMarkingSingles(out Dictionary<int, int> map)
    {
        map = new Dictionary<int, int>();
        var unlocked = dice.Select((d, i) => (d, i)).Where(t => t.d != null && !t.d.isLocked).Select(t => t.i).ToList();

        var counts = new Dictionary<DieFace, int>();
        foreach (var idx in unlocked)
        {
            var f = dice[idx].GetFace();
            if (f == DieFace.Sun) continue;
            if (!counts.ContainsKey(f)) counts[f] = 0;
            counts[f]++;
        }
        bool pairExists = counts.Values.Any(v => v >= 2);

        foreach (var idx in unlocked)
        {
            var f = dice[idx].GetFace();
            if (f == DieFace.Five) map[idx] = 5;
            else if (f == DieFace.Ten) map[idx] = 10;
            else if (f == DieFace.Sun && !pairExists) map[idx] = 10;
        }
        return map.Count > 0;
    }

    // ===================== ARTEFACTS : UI & LOGIQUE =====================
    TMP_Text GetButtonLabel(Button b) => b ? b.GetComponentInChildren<TMP_Text>(true) : null;

    // Retourne l'instance déjà présente dans la scène (prefab), sans créer quoi que ce soit.
    ArtifactTooltip EnsureTooltip()
    {
        if (artifactTooltip) return artifactTooltip;

#if UNITY_2023_1_OR_NEWER
            artifactTooltip = FindFirstObjectByType<ArtifactTooltip>(FindObjectsInactive.Include);
#else
        artifactTooltip = FindObjectOfType<ArtifactTooltip>(true);
#endif

        if (!artifactTooltip)
            Debug.LogWarning("ArtifactTooltip: aucune instance trouvée. Place un TooltipPanel dans la scène et assigne-le au champ 'artifactTooltip' du GameManager.");

        return artifactTooltip;
    }

    void SetButtonsAs123(bool on)
    {
        var t1 = GetButtonLabel(rollButton); if (t1) t1.text = on ? "1" : rollOriginalLabel;
        var t2 = GetButtonLabel(bankButton); if (t2) t2.text = on ? "2" : bankOriginalLabel;
        var t3 = GetButtonLabel(endRoundButton); if (t3) t3.text = on ? "3" : endOriginalLabel;
    }

    RectTransform GetArtifactRoot()
    {
        if (artifactOptionsRoot) return artifactOptionsRoot;
        if (dice != null && dice.Count > 0 && dice[0] != null)
        {
            var p = dice[0].transform.parent as RectTransform;
            if (p) return p;
        }
        return GetComponentInParent<Canvas>()?.GetComponent<RectTransform>();
    }

    void HideDice(bool hide)
    {
        if (dice == null) return;
        foreach (var d in dice) if (d) d.gameObject.SetActive(!hide);
    }

    void EnterArtifactPick(int count = 3)
    {
        if (awaitingArtifactPick) return;

        count = Mathf.Max(1, count);

        // 1) Masquer les dés & le score du tour
        HideDice(true);
        SetTurnScoreVisible(false);

        // 2) Préparer l’offre
        offeredArtifacts.Clear();
        var picks = artifactLibrary ? artifactLibrary.GetRandomDistinct(count) : new List<Artifact>();
        if (picks != null) offeredArtifacts.AddRange(picks);

        // 3) Nettoyer l’UI précédente et instancier les cartes
        foreach (var v in offeredCardViews) if (v) Destroy(v.gameObject);
        offeredCardViews.Clear();

        var root = GetArtifactRoot();
        if (root && offeredArtifacts.Count > 0)
        {
            for (int i = 0; i < offeredArtifacts.Count; i++)
            {
                var view = Instantiate(artifactCardPrefab, root);
                view.Setup(offeredArtifacts[i], EnsureTooltip(), ArtifactCardMode.Selection, i);

                // Choix au CLIC sur la carte
                int captured = i;
                view.onClicked = _ => SelectArtifact(captured);

                offeredCardViews.Add(view);
            }
        }

        // 4) État & UI
        awaitingArtifactPick = true;
        ApplyButtonsState();

        // Skip & Destroy visibles dès l’entrée; Destroy sera grisé si inventaire fermé/vide
        RefreshArtifactPickActionButtons();

        // Message unique au DÉBUT si inventaire plein
        if (IsInventoryFull())
        {
            if (turnLabel) turnLabel.text = "Inventaire plein — détruisez un artefact ou SKIP.";
            hintBanner?.Show("Inventaire plein (3/3). Détruisez un artefact ou appuyez sur SKIP.");
        }
        else
        {
            if (turnLabel) turnLabel.text = "Choisis un artefact en cliquant sur sa carte.";
            hintBanner?.Show("Choisis un artefact en cliquant sur sa carte.");
        }

        uiLog?.Append("Sélection d’artefact ouverte.");
    }

    // ===================== TRAITS : SÉLECTION (cartes cliquables) =====================
    void EnterTraitPick(List<TraitDef> options, string bannerMsg, Action onDone)
    {
        if (options == null || options.Count == 0) { onDone?.Invoke(); return; }
        if (awaitingTraitPick) { onDone?.Invoke(); return; }

        awaitingTraitPick = true;
        afterTraitPick = onDone;

        HideDice(true);
        SetTurnScoreVisible(false);

        offeredTraits.Clear();
        offeredTraits.AddRange(options);

        foreach (var v in offeredTraitCards) if (v) Destroy(v.gameObject);
        offeredTraitCards.Clear();

        var root = GetArtifactRoot();
        if (root && artifactCardPrefab)
        {
            for (int i = 0; i < offeredTraits.Count; i++)
            {
                var view = Instantiate(artifactCardPrefab, root);
                view.UpdateMode(ArtifactCardMode.Selection);
                view.SetTooltip(EnsureTooltip());
                view.BindRaw(offeredTraits[i].name, offeredTraits[i].desc, GetTraitIcon(offeredTraits[i]), "Trait");

                int captured = i;
                view.onClicked = _ => SelectTrait(captured);
                offeredTraitCards.Add(view);
            }
        }

        hintBanner?.Show(bannerMsg);
        if (turnLabel) turnLabel.text = bannerMsg;
        ApplyButtonsState();
    }

    void SelectTrait(int index)
    {
        if (!awaitingTraitPick) return;
        if (index < 0 || index >= offeredTraits.Count) return;

        // Durcissement : désarme immédiatement TOUTES les cartes pour qu'aucun
        // autre clic/callback ne puisse ajouter un second trait.
        foreach (var v in offeredTraitCards) if (v) v.onClicked = null;

        var chosen = offeredTraits[index];
        if (!PlayerHasTrait(chosen.key))
        {
            playerTraits.Add(chosen);
            uiLog?.Append($"Trait acquis : {chosen.name} ({chosen.key}) — total joueur : {playerTraits.Count}.");
        }
        else
        {
            uiLog?.Append($"Trait déjà possédé : {chosen.name} ({chosen.key}).");
        }

        hintBanner?.Show($"Trait acquis : « {chosen.name} ».");
        RebuildTraitIcons();

        ExitTraitPick();
    }

    void ExitTraitPick()
    {
        // La description au survol ne doit pas rester affichée après le choix
        if (artifactTooltip) artifactTooltip.Hide();

        foreach (var v in offeredTraitCards) if (v) Destroy(v.gameObject);
        offeredTraitCards.Clear();
        offeredTraits.Clear();
        awaitingTraitPick = false;

        HideDice(false);
        SetTurnScoreVisible(true);

        var cb = afterTraitPick;
        afterTraitPick = null;
        cb?.Invoke();
    }

    // Utilisable si on est après au moins un jet du joueur
    bool IsPostRollWindow()
    {
        if (gameOver || matchOver || isGameOverScreen) return false;
        // pas pendant l'IA ni avant le premier jet
        // autorisé: Normal, Clause, WaitEnd
        if (currentTurn != Turn.Player) return false;
        if (phase == Phase.AITurnPlaying || phase == Phase.AITurnWaitEnd) return false;
        if (phase == Phase.AwaitFirstRoll) return false;

        // au moins un dé a une face affichée => on a déjà jeté
        return dice != null && dice.Any(d => d != null && d.faceImage && d.faceImage.sprite != null);
    }

    bool IsDiePickableForRelance(int idx)
    {
        if (idx < 0 || idx >= dice.Count) return false;
        var d = dice[idx]; if (d == null) return false;

        // Interdit sur les dés verrouillés "figés" (flash ou freeze), OK sur les locks "mutables"
        if (flashLockIndices.Contains(idx)) return false;
        if (frozenLocks.Contains(idx) && !mutableLocks.Contains(idx)) return false;
        return true;
    }

    bool HasAnyPickableDieForRelance()
        => dice != null && dice.Select((d, i) => (d, i)).Any(t => IsDiePickableForRelance(t.i));

    // ⚡ Pouvoir: Os du Tricheur (Relance 1 dé)
    void ApplyRelance_RerollOne(int idx)
    {
        var die = dice[idx];
        if (die == null) return;

        // Seconde chance après wimpout : restaure le score perdu du tour
        if (wimpoutLostTurnScore > 0 && turnScore == 0)
        {
            turnScore = wimpoutLostTurnScore;
            wimpoutLostTurnScore = 0;
            UpdateUI();
        }

        // Si le dé était "sélectionné" ce tour (mutable), on annule d'abord sa sélection/points
        if (die.isLocked && mutableLocks.Contains(idx))
        {
            die.SetLocked(false);
            if (mutablePoints.TryGetValue(idx, out int pts))
            {
                turnScore -= pts;
                mutablePoints.Remove(idx);
            }
            mutableLocks.Remove(idx);
        }

        bool wasClause = flashPendingResolution;

        // Relance d’1 dé
        die.Roll();
        lastRolledIndices.Clear();
        lastRolledIndices.Add(idx);

        // Recalcul des éligibles d’après CE mini-jet
        FillEligibleFromIndices(lastRolledIndices);

        if (wasClause)
        {
            var f = die.GetFace();
            bool matchedFlashFace = lastRolledIndices.Any(i => MapFlashableFace(dice[i].GetFace()) == currentFlashFace);

            if (eligibleLockIndices.Count > 0)
            {
                ShowAction("Clause : sélectionne le dé marquant pour dégager le Flash, ou ROLL pour retenter.");
                SetPhase(Phase.Clause);
            }
            else if (matchedFlashFace)
            {
                ShowAction($"Clause : {(int)currentFlashFace} retombe — appuie sur ROLL pour continuer.");
                SetPhase(Phase.Clause);
            }
            else
            {
                // Pas de points et pas de retombée sur la face du Flash → wimpout
                wimpoutLostTurnScore = turnScore;
                turnScore = 0;
                UpdateUI();
                ShowAction("Wimpout (Clause).");
                SetPhase(Phase.WaitEnd);
            }
        }
        else
        {
            if (eligibleLockIndices.Count > 0)
            {
                ShowAction("Tu peux sélectionner un 5/10/SUN, puis ROLL pour relancer le reste.");
                SetPhase(Phase.Normal);
            }
            else
            {
                wimpoutLostTurnScore = turnScore;
                turnScore = 0; UpdateUI();
                ShowAction("Toujours aucun point — Wimpout.");
                SetPhase(Phase.WaitEnd);
            }
        }

        UpdateUI();
        ApplyButtonsState();
    }



    void SelectArtifact(int index)
    {
        if (!awaitingArtifactPick) return;
        if (index < 0 || index >= offeredArtifacts.Count) return;

        // Si l’inventaire est plein, on bloque le choix (rappel à l'appui sur une carte)
        if (IsInventoryFull())
        {
            hintBanner?.Show("Inventaire plein (3/3). Détruisez un artefact ou appuyez sur SKIP.");
            return;
        }

        var chosen = offeredArtifacts[index];
        bool added = false;

        if (chosen != null && playerInventory != null)
        {
            added = playerInventory.TryAdd(chosen);

            // Rafraîchir l'UI d'inventaire et les 3 points
            inventoryUI?.RefreshNow();
            inventoryDots?.Refresh();

            // On ne spam pas la HintBanner ici si l'ajout échoue (capacité) — l'info a déjà été donnée à l'entrée de phase
            if (!added)
                uiLog?.Append("Ajout d’artefact refusé (inventaire plein).");
        }

        // Log + feedback (safe null check)
        uiLog?.Append($"Artefact choisi : {(chosen != null ? chosen.displayName : "(null)")}{(added ? " (ajouté)" : "")}");
        if (added) hintBanner?.Show($"Artefact ajouté : « {chosen.displayName} ».");

        // Sortie de la phase (réactive TurnScoreText via CancelArtifactPickUI → SetTurnScoreVisible(true))
        ExitArtifactPickAndAdvance();
    }

    // (Le bouton USE a été retiré : l'activation d'un artefact passe uniquement
    //  par le drag & drop de la carte d'inventaire sur la table — voir InventoryUI/ArtifactDropZone.)


    void ExitArtifactPickAndAdvance()
    {
        // Nettoyer UI / états de sélection
        CancelArtifactPickUI();

        // Récompense de Palier : une phase d'obtention supplémentaire reste à jouer.
        // (Vaut aussi après un SKIP : la phase offerte est consommée dans tous les cas.)
        if (extraArtifactPicks > 0)
        {
            extraArtifactPicks--;
            uiLog?.Append("Phase d'obtention supplémentaire (récompense de palier).");
            EnterArtifactPick(pendingArtifactPickCount);
            return;
        }

        // Avancer immédiatement (matchOver déjà true)
        AdvanceToNextOpponentOrPalier();
    }

    void CancelArtifactPickUI()
    {
        if (!awaitingArtifactPick)
        {
            SetButtonsAs123(false);
            return;
        }

        if (artifactTooltip) artifactTooltip.Hide();

        foreach (var v in offeredCardViews) if (v) Destroy(v.gameObject);
        offeredCardViews.Clear();
        offeredArtifacts.Clear();

        awaitingArtifactPick = false;
        SetButtonsAs123(false);
        ApplyButtonsState();

        // Cache Skip/Destroy
        if (devSkipArtifactPickButton) devSkipArtifactPickButton.gameObject.SetActive(false);
        if (devDestroyCurrentArtifactButton) devDestroyCurrentArtifactButton.gameObject.SetActive(false);

        // Réaffiche les dés + le score du tour
        HideDice(false);
        SetTurnScoreVisible(true);

        RefreshCampaignUI();
        UpdateUI();
    }

    // --- DEV: victoire instantanée ---
    public void OnPressDevWin()
    {
        if (isGameOverScreen) return;

        if (awaitingArtifactPick)
        {
            ShowAction("Sélection d’artefact en cours.");
            return;
        }
        if (matchOver)
        {
            ShowAction("Match déjà terminé.");
            return;
        }

        StopAllCoroutines();
        uiLog?.Append("[DEV] Victoire instantanée du Joueur.");
        ShowAction("DEV: victoire instantanée.");

        EndMatch(PLAYER_NAME); // ouvre directement la sélection d’artefact
    }

    // --- DEV: défaite instantanée ---
    public void OnPressDevLose()
    {
        if (isGameOverScreen) return;

        if (awaitingArtifactPick)
        {
            ShowAction("Sélection d’artefact en cours.");
            return;
        }
        if (matchOver)
        {
            ShowAction("Match déjà terminé.");
            return;
        }

        StopAllCoroutines();
        uiLog?.Append("[DEV] Défaite instantanée du Joueur.");
        ShowAction("DEV: défaite instantanée.");

        EndMatch(AI_NAME);
    }

    PalierConfig.EnemyInfo PickEnemy(int pIndex, int eIndex)
    {
        // 1) Si la config Palier a un ennemi à cet index, on le prend
        var fromPalier = (palierConfig != null) ? palierConfig.GetEnemy(pIndex, eIndex) : null;
        if (fromPalier != null && fromPalier.portrait != null) return fromPalier;

        // 2) Sinon, on pioche dans la librairie globale (si dispo)
        if (enemyLibrary != null && enemyLibrary.allEnemies != null && enemyLibrary.allEnemies.Count > 0)
        {
            // hash déterministe basé sur (palier, ennemi) + seed
            int hash = pIndex * 73856093 ^ eIndex * 19349663 ^ campaignSeed;
            int idx = Mathf.Abs(hash) % enemyLibrary.allEnemies.Count;
            return enemyLibrary.allEnemies[idx];
        }
        return null;
    }

    // SKIP: sortir de la phase et avancer (sans prendre d'artefact)
    public void OnPressSkipArtifactPick()
    {
        if (!awaitingArtifactPick) return;
        uiLog?.Append("Phase d’obtention SKIPPED par le joueur.");
        hintBanner?.Show("Phase d’obtention ignorée.");
        ExitArtifactPickAndAdvance();
    }

    // DESTROY: détruire l’artefact actuellement affiché dans l’inventaire
    public void OnPressDestroyCurrentArtifact()
    {
        if (!awaitingArtifactPick) return;

        if (inventoryUI == null || !inventoryUI.IsOpen)
        {
            hintBanner?.Show("Ouvre l’inventaire pour choisir quel artefact détruire.");
            return;
        }
        if (playerInventory == null || playerInventory.Count == 0)
        {
            hintBanner?.Show("Aucun artefact à détruire.");
            return;
        }

        bool removed = inventoryUI.TryDestroyCurrent();
        if (removed)
        {
            inventoryDots?.Refresh();
            inventoryUI.RefreshNow();

            // Sacrifice : détruire un artefact retire UNE défaite (s'il y en a).
            if (defeatsCount > 0)
            {
                defeatsCount--;
                hintBanner?.Show($"Artefact sacrifié : une défaite vous est retirée ({defeatsCount}/3) !");
                uiLog?.Append($"Artefact détruit — une défaite effacée ({defeatsCount}/3).");
                RefreshCampaignUI();
            }
            else
            {
                hintBanner?.Show("Artefact détruit. Vous pouvez maintenant choisir un artefact.");
                uiLog?.Append("Artefact détruit pendant la phase d’obtention (aucune défaite à retirer).");
            }

            // ⇩ grise/dégrise Destroy selon l’état actuel (ouvert/vide)
            RefreshArtifactPickActionButtons();

            // Si de la place est libérée, réactiver 1/2/3 si besoin
            ApplyButtonsState();
            if (!IsInventoryFull() && turnLabel) turnLabel.text = "Choisis un artefact en cliquant sur sa carte.";
        }
        else
        {
            hintBanner?.Show("Impossible de détruire l’artefact.");
        }
    }

    // --- Helpers phase d’obtention ---
    void SetTurnScoreVisible(bool v)
    {
        if (turnScoreText) turnScoreText.gameObject.SetActive(v);
    }

    // Affiche Skip & Destroy pendant la phase d’obtention.
    // Destroy n'est cliquable que si l'inventaire est ouvert (panel visible) ET qu'il contient au moins 1 artefact.
    void RefreshArtifactPickActionButtons()
    {
        bool inPick = awaitingArtifactPick;

        if (devSkipArtifactPickButton)
            devSkipArtifactPickButton.gameObject.SetActive(inPick);

        if (devDestroyCurrentArtifactButton)
        {
            devDestroyCurrentArtifactButton.gameObject.SetActive(inPick);

            bool invOpen = (inventoryUI != null && inventoryUI.IsOpen);
            bool hasAny = (playerInventory != null && playerInventory.Count > 0);

            devDestroyCurrentArtifactButton.interactable = inPick && invOpen && hasAny;
        }
    }

    // Appelé par InventoryUI quand on ouvre/ferme le panneau
    public void OnInventoryOpenChanged(bool isOpen)
    {
        if (awaitingArtifactPick)
            RefreshArtifactPickActionButtons();
    }

    public ArtifactPowers artifactPowers; // assigne dans l'Inspector

    public bool IsPlayerTurn => currentTurn == Turn.Player;
    public bool IsPostRollPhase => phase == Phase.Normal || phase == Phase.Clause;

    // --- Helpers d'accès sûrs pour les artefacts (lecture seule) ---
    public bool WasInLastRoll(int idx)
    {
        return lastRolledIndices != null && lastRolledIndices.Contains(idx);
    }

    public bool IsDieLockedAt(int idx)
    {
        if (idx < 0 || dice == null || idx >= dice.Count) return false;
        var d = dice[idx];
        return d != null && d.isLocked;
    }

    // Dé "sélectionné ce jet" (verrou annulable), par opposition aux verrous figés (Flash / jets précédents)
    public bool IsDieSelectedThisRoll(int idx) => mutableLocks.Contains(idx);

    // Annule la sélection d'un dé (verrou mutable) : rend le dé, retire ses points du tour,
    // et réarme la Clause si cette sélection était la seule à avoir dégagé le Flash.
    public bool Artifact_TryUnselectDie(int idx)
    {
        if (idx < 0 || dice == null || idx >= dice.Count) return false;
        if (!mutableLocks.Contains(idx)) return false;

        var die = dice[idx];
        if (die != null) die.SetLocked(false);
        mutableLocks.Remove(idx);
        if (mutablePoints.TryGetValue(idx, out int pts))
        {
            turnScore -= pts;
            mutablePoints.Remove(idx);
        }

        if (clauseClearedBySelection && mutableLocks.Count == 0)
        {
            clauseClearedBySelection = false;
            flashPendingResolution = true;
            SetPhase(Phase.Clause);
        }

        UpdateUI();
        return true;
    }

    public int DiceCount => (dice != null ? dice.Count : 0);


    // --- Ciblage externe d'un dé (utilisé par les artefacts) ---
    public Func<int, bool> externalDiePickFilter;
    public Action<int> onExternalDiePicked;

    public void BeginExternalDiePick(Func<int, bool> filter, Action<int> onPicked, string prompt)
    {
        externalDiePickFilter = filter;
        onExternalDiePicked = onPicked;
        hintBanner?.Show(prompt);
    }

    public void ClearExternalDiePick()
    {
        externalDiePickFilter = null;
        onExternalDiePicked = null;
    }

    public IEnumerator Artifact_RerollOneDie(int idx)
    {
        if (idx < 0 || idx >= dice.Count) yield break;
        if (dice[idx] == null) yield break;

        // Seconde chance après wimpout : le score perdu du tour est restauré,
        // il redeviendra bancable si la relance sauve le jet.
        if (wimpoutLostTurnScore > 0 && turnScore == 0)
        {
            turnScore = wimpoutLostTurnScore;
            wimpoutLostTurnScore = 0;
            uiLog?.Append($"Os du Tricheur : score du tour restauré ({turnScore}).");
            UpdateUI();
        }

        // On ne touche pas aux locks déjà posés; on relance juste le dé ciblé.
        dice[idx].Roll();
        lastRolledIndices.Clear();
        lastRolledIndices.Add(idx);
        yield return null;

        // On réévalue TOUT le pool non verrouillé : le dé relancé ET les dés marquants
        // déjà présents non sélectionnés doivent tous pouvoir être choisis par le joueur.
        var unlocked = Enumerable.Range(0, dice.Count).Where(i => dice[i] != null && !dice[i].isLocked).ToList();
        var eval = EvaluateRoll(unlocked, scoreSingles: false);

        if (phase == Phase.Clause)
        {
            if (eval.createdFlash)
            {
                foreach (var i in eval.flashLockIndices) { dice[i].SetLocked(true); frozenLocks.Add(i); flashLockIndices.Add(i); }
                turnScore += eval.pointsGained;
                currentFlashFace = eval.flashFace;
                flashPendingResolution = true;
                UpdateUI();
                FillEligibleFromIndices(unlocked.Where(i => !eval.flashLockIndices.Contains(i)).ToList());
                ShowAction($"FLASH {(int)currentFlashFace} (via artefact).");
                SetPhase(Phase.Clause);
                yield break;
            }

            FillEligibleFromIndices(unlocked);

            if (eligibleLockIndices.Count > 0)
            {
                ShowAction("Clause : sélectionne un 5/10/SUN pour dégager le Flash, ou ROLL pour retenter.");
                SetPhase(Phase.Clause);
            }
            else
            {
                bool matchedFlashFace = unlocked.Any(i => MapFlashableFace(dice[i].GetFace()) == currentFlashFace);
                if (matchedFlashFace)
                {
                    ShowAction($"Clause : {(int)currentFlashFace} retombe — appuie sur ROLL pour continuer.");
                    SetPhase(Phase.Clause);
                }
                else
                {
                    wimpoutLostTurnScore = turnScore;
                    turnScore = 0;
                    UpdateUI();
                    ShowAction("Wimpout (Clause).");
                    SetPhase(Phase.WaitEnd);
                }
            }
        }
        else
        {
            // Phase normale
            if (eval.createdFlash)
            {
                foreach (var i in eval.flashLockIndices) { dice[i].SetLocked(true); frozenLocks.Add(i); flashLockIndices.Add(i); }
                turnScore += eval.pointsGained;
                currentFlashFace = eval.flashFace;
                flashPendingResolution = true;
                UpdateUI();
                FillEligibleFromIndices(unlocked.Where(i => !eval.flashLockIndices.Contains(i)).ToList());
                SetPhase(Phase.Clause);
                ShowAction($"FLASH {(int)currentFlashFace} (via artefact) — Clause.");
                yield break;
            }

            // Aucun Flash créé par la relance : on ne doit surtout pas entrer en phase
            // Normale avec un Flash resté « en attente » (état hérité d'une Clause perdue),
            // sinon OnDieClicked ignore silencieusement tous les clics et le jeu se bloque.
            ClearClauseState();
            clauseClearedBySelection = false;

            FillEligibleFromIndices(unlocked);
            if (eligibleLockIndices.Count > 0)
            {
                ShowAction("Sélectionne les dés marquants (5/10/SUN), puis BANK ou ROLL.");
                SetPhase(Phase.Normal);
            }
            else
            {
                // Aucun point nulle part → wimpout immédiat
                wimpoutLostTurnScore = turnScore;
                turnScore = 0;
                UpdateUI();
                ShowAction("Wimpout — tour terminé.");
                SetPhase(Phase.WaitEnd);
            }
        }
    }

    public System.Collections.IEnumerator Artifact_RerollTurn()
    {
        // "Temps niv.1" : on refait un LANCÉ ENTIER (les 5 dés, même ceux mis de côté),
        // PAS un tour complet : le score total du tour est CONSERVÉ.
        // Si une Clause vient d'être perdue (wimpout), le score perdu est restauré :
        // l'artefact "rembobine" ce lancé raté.
        if (dice == null || dice.Count == 0) yield break;

        if (wimpoutLostTurnScore > 0 && turnScore == 0)
        {
            turnScore = wimpoutLostTurnScore;
            uiLog?.Append($"Artefact du temps : score du tour restauré ({turnScore}).");
        }
        wimpoutLostTurnScore = 0;

        foreach (var d in dice) if (d != null) d.SetLocked(false);
        frozenLocks.Clear();
        mutableLocks.Clear();
        mutablePoints.Clear();
        flashLockIndices.Clear();
        flashVisualIndices.Clear();
        eligibleLockIndices.Clear();
        eligibleLockPoints.Clear();
        clauseClearedBySelection = false;
        flashPendingResolution = false;
        // ⚠️ turnScore n'est PAS remis à zéro : seul le lancé est rejoué.
        UpdateUI();

        var rollableIndices = Enumerable.Range(0, dice.Count).Where(i => dice[i] != null).ToList();
        foreach (var i in rollableIndices)
            dice[i].Roll();

        lastRolledIndices.Clear();
        lastRolledIndices.AddRange(rollableIndices);
        yield return null;

        ApplyPendingFaceOverrides(lastRolledIndices);

        if (ENABLE_SUPERNOVA && lastRolledIndices.Count == 5 && lastRolledIndices.All(i => dice[i].GetFace() == DieFace.Ten))
        {
            ShowAction($"SUPERNOVA ! 5×10 — {PLAYER_NAME} gagne.");
            EndMatch(PLAYER_NAME);
            yield break;
        }

        var eval = EvaluateRoll(lastRolledIndices, scoreSingles: false);
        CheckLoveFilterBonus(eval.createdFlash, eval.flashFace, lastRolledIndices);

        if (eval.createdFlash)
        {
            foreach (var i in eval.flashLockIndices)
            {
                dice[i].SetLocked(true);
                frozenLocks.Add(i);
                flashLockIndices.Add(i);
            }
            turnScore += eval.pointsGained;
            currentFlashFace = eval.flashFace;
            flashPendingResolution = true;
            UpdateUI();

            // Après Flash, étape Clause
            FillEligibleFromIndices(lastRolledIndices.Where(i => !eval.flashLockIndices.Contains(i)).ToList());
            SetPhase(Phase.Clause);
            ShowAction($"FLASH {(int)currentFlashFace} — Clause.");
            yield break;
        }

        // Pas de Flash : points simples ?
        FillEligibleFromIndices(lastRolledIndices);

        if (eligibleLockIndices.Count > 0)
        {
            ShowAction("Coup rejoué ! Sélectionne tes 5/10/SUN pour marquer, puis ROLL pour continuer.");
            SetPhase(Phase.Normal);
        }
        else
        {
            // Wimpout : le score conservé est perdu (risque du relancé), récupérable via Os du Tricheur
            wimpoutLostTurnScore = turnScore;
            turnScore = 0;
            UpdateUI();
            ShowAction("Wimpout.");
            SetPhase(Phase.WaitEnd);
        }

        UpdateUI();
        ApplyButtonsState();
    }

    // Retourne true si l'artefact a réellement été joué (usage accepté).
    public bool TryUseArtifactFromInventory(int inventoryIndex)
    {
        // Si tu as intégré le système ArtifactPowers (étape précédente) :
        if (artifactPowers != null)
            return artifactPowers.TryUseFromInventory(inventoryIndex);

        // Sinon, message de secours (à retirer quand ArtifactPowers est branché).
        hintBanner?.Show("Aucun système de pouvoirs n'est configuré pour 'Use'.");
        return false;
    }

    // ✅ Fenêtre d'usage pour "relancer son coup"
    public bool CanUseRerollTurnNow()
    {
        if (gameOver || matchOver || isGameOverScreen) return false;
        if (currentTurn != Turn.Player) return false;

        // Post-jet autorisé: Normal, Clause, WaitEnd
        bool inPostRoll = phase == Phase.Normal || phase == Phase.Clause || phase == Phase.WaitEnd;
        if (!inPostRoll) return false;

        // Au moins une face déjà affichée (un jet a eu lieu)
        bool hasFaces = dice != null && dice.Any(d => d != null && d.faceImage && d.faceImage.sprite != null);
        if (!hasFaces) return false;

        // Au moins un dé relançable (non verrouillé)
        bool anyRollable = dice != null && dice.Select((d, i) => (d, i)).Any(t => t.d != null && !t.d.isLocked);
        return anyRollable;
    }

    // ✅ Fenêtre d'usage pour "relancer la partie"
    public bool CanRestartMatchNow()
    {
        // Avant une défaite : match en cours (pas terminé), pas d’écran de fin
        if (gameOver || matchOver || isGameOverScreen) return false;
        // Tu peux restreindre encore si besoin (ex: pas pendant le tour IA)
        if (currentTurn != Turn.Player) return false;
        return true;
    }

    public void Artifact_RestartMatch()
    {
        // Redémarre proprement le match contre le même adversaire.
        // StartNewMatch() remet déjà playerScore/aiScore/turnScore, les locks,
        // la phase, l’UI et appelle ResetForNewTurn() sur chaque dé.
        StartNewMatch();
    }

    public void Artifacts_RefreshEligibilityAndUI()
    {
        // si on a la trace du dernier roll, repart de là; sinon, des dés non lockés
        var pool = (lastRolledIndices != null && lastRolledIndices.Count > 0)
            ? lastRolledIndices.Where(i => dice[i] != null && !dice[i].isLocked).ToList()
            : dice.Select((d, i) => (d, i)).Where(t => t.d != null && !t.d.isLocked).Select(t => t.i).ToList();

        FillEligibleFromIndices(pool); // ← existe déjà chez toi :contentReference[oaicite:3]{index=3}
        ApplyButtonsState();
        UpdateUI();
    }

    // 🌩️ Créer un FLASH artificiel à partir de 3 indices (puis passer en Clause)
    public void Artifacts_CreateFlashFromIndices(List<int> tripleIndices, DieFace face)
    {
        if (tripleIndices == null || tripleIndices.Count != 3) return;

        // Si le tour venait d'être perdu (wimpout), le flash créé par l'artefact SAUVE le tour :
        // on restaure d'abord le score perdu, puis on ajoute les points du flash.
        if (phase == Phase.WaitEnd) RestoreWimpoutScoreIfAny();

        // 1) lock des 3 dés + points du FLASH
        foreach (var i in tripleIndices)
            if (i >= 0 && i < dice.Count && dice[i] != null)
            {
                dice[i].SetLocked(true);
                flashVisualIndices.Add(i); // surlignage orange
            }

        if (FLASH_SCORE.TryGetValue(face, out var pts)) turnScore += pts;
        UpdateUI();

        // 2) état de Clause standard (mêmes variables que ton flow normal)
        currentFlashFace = face;
        flashPendingResolution = true;

        var remaining = Enumerable.Range(0, dice.Count)
            .Where(i => !tripleIndices.Contains(i) && dice[i] != null && !dice[i].isLocked)
            .ToList();

        FillEligibleFromIndices(remaining); // singles sur le reste des dés
        ShowAction($"FLASH {(int)currentFlashFace} — sélectionne 5/10/SUN ou ROLL pour continuer la Clause.");
        SetPhase(Phase.Clause);
    }

    // Restaure le score du tour perdu au wimpout (l'artefact vient de sauver le tour).
    void RestoreWimpoutScoreIfAny()
    {
        if (wimpoutLostTurnScore > 0 && turnScore == 0)
        {
            turnScore = wimpoutLostTurnScore;
            wimpoutLostTurnScore = 0;
            uiLog?.Append($"Artefact : tour sauvé, score du tour restauré ({turnScore}).");
        }
    }

    public void Artifacts_ReevaluateAfterDiceChanged(string banner = null)
    {
        // Recalcule ce qui est sélectionnable parmi les dés NON verrouillés
        var unlocked = dice.Select((d, i) => (d, i))
                        .Where(t => t.d != null && !t.d.isLocked)
                        .Select(t => t.i).ToList();

        // 1) Un contre-jeu/transfo (ex: Miroir) peut avoir CRÉÉ un FLASH parmi les dés libres :
        //    on le détecte et on le déclenche en priorité (3 identiques, ou paire + SUN).
        if (!flashPendingResolution)
        {
            var evalFlash = EvaluateRoll(unlocked, scoreSingles: false);
            if (evalFlash.createdFlash && evalFlash.flashLockIndices.Count == 3)
            {
                // Si le tour était perdu, le flash le sauve : on restaure d'abord le score.
                if (phase == Phase.WaitEnd) RestoreWimpoutScoreIfAny();
                Artifacts_CreateFlashFromIndices(evalFlash.flashLockIndices, evalFlash.flashFace);
                if (!string.IsNullOrEmpty(banner)) uiLog?.Append(banner);
                ApplyButtonsState();
                UpdateUI();
                return;
            }
        }

        // 2) Sinon, on calcule les dés marquants (singles) disponibles.
        FillEligibleFromIndices(unlocked);

        // Si on était bloqué en WaitEnd (tour perdu) et qu’on a maintenant du marquant,
        // on rend la main au joueur ET on RESTAURE le score perdu au wimpout.
        if (phase == Phase.WaitEnd && eligibleLockIndices.Count > 0)
        {
            RestoreWimpoutScoreIfAny();
            if (turnScore > 0)
                ShowAction($"Tour sauvé ! Score restauré ({turnScore} pts) — sélectionne un dé marquant.");
            SetPhase(Phase.Normal);
            if (!string.IsNullOrEmpty(banner)) ShowAction(banner);
        }

        ApplyButtonsState();
        UpdateUI();
    }

    public System.Collections.IEnumerator Artifact_AddSurpriseDie()
    {
        GameObject go = null;
        DieView dv = null;
        bool destroyOnCleanup = false;

        if (surpriseDieTemplateInScene != null)
        {
            dv = surpriseDieTemplateInScene;
            go = dv.gameObject;
            go.SetActive(true);         // reste là où TU l’as placé
            destroyOnCleanup = false;
        }
        else if (surpriseDiePrefab != null)
        {
            var parent = surpriseDiceParent != null ? surpriseDiceParent : transform;
            go = Instantiate(surpriseDiePrefab, parent);
            dv = go.GetComponentInChildren<DieView>(true) ?? go.GetComponent<DieView>();
            if (!dv) { Debug.LogError($"[Ajout] Le prefab '{surpriseDiePrefab.name}' n’a pas de DieView."); Destroy(go); yield break; }
            destroyOnCleanup = true;
        }
        else if (dice != null && dice.Count > 0 && dice[0] != null)
        {
            go = Instantiate(dice[0].gameObject, surpriseDiceParent ? surpriseDiceParent : dice[0].transform.parent);
            go.name = "SurpriseDie(Fallback)";
            dv = go.GetComponent<DieView>();
            destroyOnCleanup = true;
        }
        else
        {
            Debug.LogError("[Ajout] Impossible de créer un Dé d’ajout.");
            yield break;
        }

        // VIERGE jusqu’au premier roll
        dv.ResetForNewTurn();                 // efface le sprite
        SetDieFaceSilently(dv, DieFace.Two);  // face interne non marquante
        dv.SetLocked(false);

        // Wiring clic & pool
        dv.onClicked = OnDieClicked;
        if (dv.sprites == null && dice != null && dice.Count > 0 && dice[0] != null)
            dv.sprites = dice[0].sprites;

        if (!dice.Contains(dv)) dice.Add(dv);
        addedDice.Add(new AddedDie { view = dv, destroyOnCleanup = destroyOnCleanup });
        currentAddedDie = dv;

        // Redonne la main s’il fallait
        if (phase == Phase.WaitEnd) SetPhase(Phase.Normal);

        hintBanner?.Show("Dé d’ajout prêt — il roulera avec les autres au prochain ROLL.");
        ApplyButtonsState();
        UpdateUI();
        yield break;
    }

    void SetDieFaceSilently(DieView dv, DieFace f)
    {
        var fi = typeof(DieView).GetField("currentFace", BindingFlags.Instance | BindingFlags.NonPublic);
        if (fi != null) fi.SetValue(dv, f); // change la face interne sans toucher à l'image
    }

    void ApplyPendingFaceOverrides(List<int> rolledIndices)
    {
        if (rolledIndices == null || rolledIndices.Count == 0) return;
        foreach (var idx in rolledIndices)
        {
            var dv = dice[idx];
            if (dv != null && pendingForcedFaces.TryGetValue(dv, out var f))
            {
                dv.SetFace(f);
                pendingForcedFaces.Remove(dv);
            }
        }
    }

    public void ArmLoveFilterBonus(DieView dv, DieFace face)
    {
        loveFilterBonusArmed = true;
        loveFilterTarget = face;
        loveFilterDie = dv;
    }

    void CheckLoveFilterBonus(bool createdFlash, DieFace evalFlashFace, List<int> rolledIndices)
    {
        if (!loveFilterBonusArmed) return;

        if (createdFlash)
        {
            int loveIdx = dice.IndexOf(loveFilterDie);
            if (loveIdx >= 0 && rolledIndices.Contains(loveIdx) && evalFlashFace == loveFilterTarget)
            {
                turnScore += 10;
                hintBanner?.Show("Filtre d’amour : +10 bonus (flash créé).");
                loveFilterBonusArmed = false;
                UpdateUI();
            }
        }
    }

    public void Artifact_ForceFaceOnNextRoll(DieView dv, DieFace face)
    {
        if (dv == null) return;
        pendingForcedFaces[dv] = face;
    }

    public void Artifacts_RefreshUI()
    {
        ApplyButtonsState();
        UpdateUI();
    }

    public void Artifacts_AddTurnBonus(int pts, string banner = null)
    {
        if (pts == 0) return;
        turnScore += pts;
        if (!string.IsNullOrEmpty(banner)) hintBanner?.Show(banner);
        UpdateUI();
    }

    // Filtre d'amour : +30% sur la bank de CE tour (au lieu d'un bonus fixe).
    // Réutilise le système de modificateur de score ; ne remplace pas un mod déjà actif.
    public void Artifacts_ActivateLoveFilterBankBonus(float multiplier = 1.3f)
    {
        if (_scoreMod.IsActive)
        {
            hintBanner?.Show("Filtre d’amour : un modificateur de score est déjà actif — bonus non appliqué.");
            uiLog?.Append("Filtre d’amour : bonus ×1.3 ignoré (mod déjà actif).");
            return;
        }

        ActivateScoreModifier(new ActiveScoreMod
        {
            mode = ActiveScoreMod.Mode.MarriageDot, // simple multiplicateur "pur"
            multiplier = multiplier
        });
        hintBanner?.Show($"Filtre d’amour : +{Mathf.RoundToInt((multiplier - 1f) * 100)}% sur la bank de ce tour !");
        uiLog?.Append($"Filtre d’amour : bonus ×{multiplier:0.##} sur la bank du tour.");
    }

    // Appelle cette routine pour "rouler" le dé d'ajout maintenant, avec variantes.
    // forcedFace: null = roll aléatoire ; sinon impose la face
    // autoBankSingles: si true, on crédite immédiatement les singles (5/10/SUN) sans demander de sélection
    // ephemeral: si true, on retire le dé d'ajout aussitôt (si singles) ou après Clause (si flash)
    public System.Collections.IEnumerator Artifact_RollAddDieNow(
        DieFace? forcedFace,
        bool autoBankSingles,
        bool ephemeral,
        bool bonusIfFlash = false,
        DieFace bonusFlashTarget = DieFace.Ten,
        bool lingerIfBanked = false,
        bool canCauseWimpout = true
    )
    {
        // 1) s'assurer qu'un dé d'ajout existe et est intégré au pool
        if (currentAddedDie == null)
            yield return StartCoroutine(Artifact_AddSurpriseDie()); // crée/affiche/intègre (vierge, non marquant)

        var dv = currentAddedDie;
        if (!dv) yield break;

        // 2) lui donner une face "roulée" maintenant
        if (forcedFace.HasValue)
        {
            dv.SetFace(forcedFace.Value);
        }
        else
        {
            dv.Roll();
        }

        // 3) évaluer ce "mini-jet" (uniquement ce dé) immédiatement
        int idx = dice.IndexOf(dv);
        if (idx < 0) yield break;

        lastRolledIndices.Clear();
        lastRolledIndices.Add(idx);
        yield return null; // laisse l'UI respirer

        // ⚠️ Le dé d'ajout doit pouvoir COMPLÉTER un FLASH avec les dés LIBRES déjà posés
        // (ex: deux 6 sur le board + un 6 ajouté = FLASH de 6 ; ou paire + SUN).
        var flashPool = Enumerable.Range(0, dice.Count)
            .Where(i => dice[i] != null && !dice[i].isLocked)
            .ToList();
        if (!flashPool.Contains(idx)) flashPool.Add(idx);

        var eval = EvaluateRoll(flashPool, scoreSingles: false);

        // On ne déclenche le flash que s'il implique le dé d'ajout
        // (une combinaison uniquement "board" aurait déjà été résolue au jet précédent).
        bool flashWithAddedDie = eval.createdFlash && eval.flashLockIndices.Contains(idx);

        // --- FLASH créé ? ---
        if (flashWithAddedDie)
        {
            // lock des 3 dés du flash + ajout des points flash
            foreach (var i in eval.flashLockIndices)
            {
                dice[i].SetLocked(true);
                frozenLocks.Add(i);
                flashLockIndices.Add(i);
            }
            turnScore += eval.pointsGained;
            currentFlashFace = eval.flashFace;
            flashPendingResolution = true;
            UpdateUI();

            // Bonus Filtre d'amour (+10 si on le réclame et que la face correspond)
            if (bonusIfFlash && eval.flashFace == bonusFlashTarget)
            {
                turnScore += 10;
                hintBanner?.Show("Filtre d’amour : +10 bonus (flash créé).");
                UpdateUI();
            }

            // Passage en Clause comme d’habitude
            var remaining = Enumerable.Range(0, dice.Count)
                .Where(i => !eval.flashLockIndices.Contains(i) && dice[i] != null && !dice[i].isLocked)
                .ToList();

            FillEligibleFromIndices(remaining);
            SetPhase(Phase.Clause);
            hintBanner?.Show($"FLASH {(int)currentFlashFace} — Clause.");

            // ⚠️ Si l’artefact est éphémère, on NE retire PAS le dé tout de suite (sinon on casse le flash).
            // On le retirera au NEXT via CleanupAddedDice().
            ApplyButtonsState();
            yield break;
        }

        // --- PAS DE FLASH ---
        // Calcule les singles sur ce seul dé (sans les ajouter automatiquement)
        FillEligibleFromIndices(lastRolledIndices);

        int pointsSingle = 0;
        var face = dv.GetFace();
        if (face == DieFace.Five) pointsSingle = 5;
        else if (face == DieFace.Ten || face == DieFace.Sun) pointsSingle = 10;

        if (autoBankSingles && pointsSingle > 0)
        {
            // Ajout immédiat des points
            turnScore += pointsSingle;
            UpdateUI();
            hintBanner?.Show($"+{pointsSingle} points.");

            if (phase == Phase.WaitEnd) SetPhase(Phase.Normal);

            // ⬇️ RETIRER DU POOL pour empêcher tout re-roll
            if (ephemeral)
            {
                RemoveAddedDieFromPool(dv, keepVisibleUntilNextAction: false);
            }
            else if (lingerIfBanked)
            {
                // reste visible jusqu'au prochain ROLL / NEXT (mais hors pool)
                RemoveAddedDieFromPool(dv, keepVisibleUntilNextAction: true);
            }

            // 🔽 AJOUT : si on était en Clause, ce SUN doit dégager le Flash
            if (flashPendingResolution && phase == Phase.Clause)
            {
                flashPendingResolution = false;
                ShowAction("Flash dégagé (Soleil en bouteille). Tu peux BANK ou ROLL le reste.");
                uiLog?.Append("Clause dégagée par Soleil en bouteille.");
                SetPhase(Phase.Normal);     // retour au flow normal (sélection/banque/relance)
                ApplyButtonsState();
            }

            Artifacts_RefreshUI();
            yield break;
        }

        // --- Pas de flash et pas de banc auto (ou 0 point) ---
        // ✅ IMPORTANT : regarder le board global pour éviter un wimpout injuste
        var allUnlocked = Enumerable.Range(0, dice.Count)
            .Where(i => dice[i] != null && !dice[i].isLocked).ToList();

        FillEligibleFromIndices(allUnlocked);

        // Clause en cours ?
        if (flashPendingResolution)
        {
            SetPhase(Phase.Clause);
            hintBanner?.Show("Le dé d’ajout n’est pas marquant — continue ta Clause ou ROLL.");
            // ⬇️ RETIRER DU POOL pour éviter qu’il re-roll
            RemoveAddedDieFromPool(dv, keepVisibleUntilNextAction: true);
            Artifacts_RefreshUI();
            yield break;
        }

        // Singles ailleurs ?
        if (eligibleLockIndices.Count > 0)
        {
            SetPhase(Phase.Normal);
            hintBanner?.Show("Le dé d’ajout n’est pas marquant — tu peux sélectionner/banquer ou ROLL.");
            // ⬇️ RETIRER DU POOL pour éviter qu’il re-roll
            RemoveAddedDieFromPool(dv, keepVisibleUntilNextAction: true);
            Artifacts_RefreshUI();
            yield break;
        }

        // Rien de marquant sur le board
        if (canCauseWimpout)
        {
            // wimpout réel (jamais utilisé par Dé surprise)
            turnScore = 0;
            UpdateUI();
            SetPhase(Phase.WaitEnd);
            hintBanner?.Show("Wimpout.");
        }
        else
        {
            // Dé surprise : PAS de wimpout, et on le retire du pool pour ne plus jamais le relancer
            SetPhase(Phase.Normal);
            hintBanner?.Show("Le dé d’ajout n’est pas marquant — tu peux encore ROLL ou NEXT.");
            RemoveAddedDieFromPool(dv, keepVisibleUntilNextAction: true);
        }

        Artifacts_RefreshUI();
    }

    // Retire proprement un dé d’ajout (utilisé par Filtre d’amour / Soleil après scoring)
    public void Artifacts_RemoveAddedDie(DieView dv)
    {
        if (dv == null) return;
        var ad = addedDice.FirstOrDefault(a => a.view == dv);
        dice.Remove(dv);
        if (ad != null)
        {
            if (ad.destroyOnCleanup) Destroy(dv.gameObject);
            else dv.gameObject.SetActive(false);
            addedDice.Remove(ad);
        }
        if (currentAddedDie == dv) currentAddedDie = null;
    }

    // Si on était en WaitEnd et qu’on vient de marquer via un artefact, repasse en Normal
    public void Artifacts_SetPhaseNormalIfWaitEnd()
    {
        if (currentTurn == Turn.Player)
        {
            // 'phase' et 'SetPhase' sont privés → wrapper interne
            var phaseField = typeof(GameManager).GetField("phase",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var val = phaseField?.GetValue(this)?.ToString();
            if (val != null && val.Contains("WaitEnd"))
            {
                // Appelle SetPhase(Phase.Normal) par réflexion
                var enumType = typeof(GameManager).GetNestedType("Phase",
                    System.Reflection.BindingFlags.NonPublic);
                var normalVal = System.Enum.Parse(enumType, "Normal");
                var setPhase = typeof(GameManager).GetMethod("SetPhase",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                setPhase?.Invoke(this, new object[] { normalVal });
            }
        }
    }



    void RemoveAddedDieFromPool(DieView dv, bool keepVisibleUntilNextAction)
    {
        if (!dv) return;
        var entry = addedDice.FirstOrDefault(a => a.view == dv);
        // Retirer du pool logique
        dice.Remove(dv);

        if (entry != null)
        {
            entry.keepVisibleUntilNextAction = keepVisibleUntilNextAction;
            if (keepVisibleUntilNextAction)
            {
                // on le laisse à l’écran, mais il ne doit plus interagir
                dv.onClicked = null;
                dv.SetLocked(true);
            }
            else
            {
                if (entry.destroyOnCleanup) Destroy(dv.gameObject);
                else dv.gameObject.SetActive(false);
                addedDice.Remove(entry);
            }
        }
        if (currentAddedDie == dv) currentAddedDie = null;
    }

    void CleanupLingeringAddedDice()
    {
        if (addedDice.Count == 0) return;
        for (int i = addedDice.Count - 1; i >= 0; i--)
        {
            var ad = addedDice[i];
            if (!ad.keepVisibleUntilNextAction) continue;
            if (!ad.view)
            {
                addedDice.RemoveAt(i);
                continue;
            }
            if (ad.destroyOnCleanup) Destroy(ad.view.gameObject);
            else ad.view.gameObject.SetActive(false);
            addedDice.RemoveAt(i);
        }
    }

    public bool HasActiveScoreModifier() => _scoreMod.IsActive;

    // Fenêtre "pré-jet" adaptée à ton code existant
    public bool CanActivateScorePreRollNow()
    {
        // Utilisable au tour du joueur, pas de clause en attente,
        // et AVANT ou juste après le 1er roll (si tu veux limiter vraiment au pré-jet, garde seulement AwaitFirstRoll).
        if (gameOver || matchOver || isGameOverScreen) return false;
        return currentTurn == Turn.Player
            && !flashPendingResolution
            && (phase == Phase.AwaitFirstRoll || phase == Phase.Normal);
    }


    public void ActivateScoreModifier(ActiveScoreMod mod)
    {
        _scoreMod = mod;
        UpdateScoreModBadge(preview: true);      // texte générique d’activation
        RefreshScoreModRuntimePreview();         // puis aperçu chiffré basé sur turnScore courant
    }

    public void ClearScoreModifier()
    {
        _scoreMod = ActiveScoreMod.None();
        UpdateScoreModBadge(preview: false);     // masque le label
        // Pas besoin d’appeler RefreshScoreModRuntimePreview ici
    }

    private void UpdateScoreModBadge(bool preview)
    {
        if (!scoreModLabel) return;

        scoreModLabel.gameObject.SetActive(true);

        switch (_scoreMod.mode)
        {
            case ActiveScoreMod.Mode.FuneralLedger:
                if (preview)
                    scoreModLabel.text = $"Mod: +{Mathf.RoundToInt(_scoreMod.bonusPct * 100)}% si ≥ {_scoreMod.threshold}, sinon {_scoreMod.penaltyFlat}";
                break;

            case ActiveScoreMod.Mode.MarriageDot:
                if (preview)
                    scoreModLabel.text = $"Mod: ×{_scoreMod.multiplier:0.##} sur la bank de ce tour";
                break;
        }

    }


    int ApplyScoreModsOnBank(int baseJetScore, out string appliedBadge)
    {
        appliedBadge = null;

        // TRACE DIAG
        uiLog?.Append($"[BANK] turnScore={baseJetScore}, ScoreModActive={_scoreMod.IsActive}, mode={_scoreMod.mode}");

        if (!_scoreMod.IsActive) return baseJetScore;

        int result = baseJetScore;

        switch (_scoreMod.mode)
        {
            case ActiveScoreMod.Mode.FuneralLedger:
            {
                int   thr  = _scoreMod.threshold;
                int   pen  = _scoreMod.penaltyFlat;
                float mult = 1f + _scoreMod.bonusPct;

                // ⬇️ NOUVEAU : on évalue le seuil sur le score DÉJÀ multiplié
                int boostedForThreshold = Mathf.RoundToInt(baseJetScore * mult);

                if (boostedForThreshold >= thr)
                {
                    int boosted = boostedForThreshold; // identique, mais plus lisible
                    result = boosted;
                    appliedBadge = $"×{mult:0.##}";

                    hintBanner?.Show(
                        $"Livre des Comptes Funèbres : seuil testé après × → {baseJetScore}×{mult:0.##}={boostedForThreshold} ≥ {thr} > bonus appliqué = {result}"
                    );
                    uiLog?.Append($"[LEDGER+] seuil après × : base={baseJetScore}, mult={mult:0.##}, boosted={boostedForThreshold} ≥ {thr} → OK");
                }
                else
                {
                    result = baseJetScore + pen;
                    appliedBadge = $"{pen}";

                    hintBanner?.Show(
                        $"Livre des Comptes Funèbres : {baseJetScore}×{mult:0.##}={boostedForThreshold} < {thr} > pénalité {pen} = {result}"
                    );
                    uiLog?.Append($"[LEDGER-] seuil après × : base={baseJetScore}, mult={mult:0.##}, boosted={boostedForThreshold} < {thr} → pénalité {pen}");
                }
                break;
            }

            default:
                // Juste au cas où un autre mod arrive plus tard
                uiLog?.Append("[BANK] Mod inconnu ignoré.");
                break;

            case ActiveScoreMod.Mode.MarriageDot:
                {
                    float mult = (_scoreMod.multiplier <= 0f) ? 1f : _scoreMod.multiplier;
                    int boosted = Mathf.RoundToInt(baseJetScore * mult);
                    result = boosted;
                    appliedBadge = $"×{mult:0.##}";
                    hintBanner?.Show($"Dot de mariage : {baseJetScore} × {mult:0.##} = {result}");
                    uiLog?.Append($"[MARRIAGE×] ×{mult:0.##} appliqué: {baseJetScore} → {result}");
                    break;
                }
        }

        // consommé après application
        ClearScoreModifier();
        return result;
    }

    /// <summary>
    /// Score "virtuel" utilisé UNIQUEMENT pour tester l'ouverture (≥ ENTRY_THRESHOLD).
    /// - Applique les multiplicateurs actifs (ex: Dot de mariage ×2, Ledger +20%).
    /// - N'applique PAS de pénalité éventuelle.
    /// - Ne modifie PAS turnScore; c'est juste un calcul de preview pour l'autorisation de BANK.
    /// </summary>
    int GetEffectiveTurnScoreForEntry()
    {
        int s = turnScore;
        if (_scoreMod.IsActive)
        {
            switch (_scoreMod.mode)
            {
                case ActiveScoreMod.Mode.FuneralLedger:
                    s = Mathf.RoundToInt(s * (1f + _scoreMod.bonusPct));
                    break;
                case ActiveScoreMod.Mode.MarriageDot:
                    s = Mathf.RoundToInt(s * ((_scoreMod.multiplier <= 0f) ? 1f : _scoreMod.multiplier));
                    break;
            }
        }

        return s;
    }


    // Affiche en temps réel l'impact du mod sur le score du tour.
    // Si le seuil est atteint, montre le score ajusté (turnScore → turnScore×1.2).
    // Sinon, affiche juste le rappel du seuil.
    // Affiche en temps réel un aperçu du score multiplié, même si le seuil n'est pas atteint.
    // NB: Cet aperçu est purement informatif. Au moment de BANK, l'application réelle
    //     suit ApplyScoreModsOnBank(): bonus si ≥ seuil, sinon pénalité.
    private void RefreshScoreModRuntimePreview()
    {
        if (!scoreModLabel) return;

        // On masque si rien à montrer (pas de mod "du tour")
        bool hasPerTurn = _scoreMod.IsActive;
        if (!hasPerTurn)
        {
            scoreModLabel.text = "";
            scoreModLabel.gameObject.SetActive(false);
            return;
        }

        scoreModLabel.gameObject.SetActive(true);

        // 1) Prévisualisation des MODS "du tour" (Ledger, Dot de mariage, etc.)
        int baseScore = turnScore;
        int previewAfterPerTurn = baseScore;
        string perTurnPreview = null;

        if (hasPerTurn)
        {
            switch (_scoreMod.mode)
            {
                case ActiveScoreMod.Mode.FuneralLedger:
                    {
                        float mult = 1f + _scoreMod.bonusPct;
                        int adj = Mathf.RoundToInt(baseScore * mult);
                        int thr = _scoreMod.threshold;
                        int pen = _scoreMod.penaltyFlat;

                        if (baseScore >= thr)
                            perTurnPreview = $"{baseScore}→{adj} (×{mult:0.##})";
                        else
                            perTurnPreview = $"{baseScore}→{adj} (×{mult:0.##}) · non appliqué si < {thr} ; BANK:{pen}";
                        previewAfterPerTurn = adj; // preview (même si Ledger sous-seuil ne s’appliquera pas à la bank)
                        break;
                    }
                case ActiveScoreMod.Mode.MarriageDot:
                    {
                        float mult = (_scoreMod.multiplier <= 0f) ? 1f : _scoreMod.multiplier;
                        int adj = Mathf.RoundToInt(baseScore * mult);
                        perTurnPreview = $"{baseScore}→{adj} (×{mult:0.##})";
                        previewAfterPerTurn = adj;
                        break;
                    }
            }
        }
    }


    // Banque immédiatement le tour de l'IA et gère la logique de "chase".
    // À placer dans GameManager (même région que les coroutines IA).
    private IEnumerator AIBankNowAndHandleChase()
    {
        if (aiHasBankedThisTurn) yield break;
        aiHasBankedThisTurn = true;

        aiScore += ApplyAIArtifactOnBank(Mathf.RoundToInt(turnScore * aiBankMultiplier));     // ajoute UNE SEULE FOIS (malus Contre-Jeu + artefact IA)
        turnScore = 0;
        aiBankMultiplier = 1f;
        UpdateUI();

        if (!aiOpened && aiScore >= ENTRY_THRESHOLD)
            aiOpened = true;

        // Traits : Verre Empoisonné (joueur) + Impôts bourgeois
        if (PlayerHasTrait("poisonglass") && !traitPoisonGlassUsedVsAI && aiScore > 100)
        {
            traitPoisonGlassUsedVsAI = true;
            RebuildTraitIcons();
            int before = aiScore;
            aiScore = Mathf.RoundToInt(aiScore / 2f);
            ShowAction($"Verre Empoisonné : le score de l'IA est divisé par 2 ({before} → {aiScore}) !");
            UpdateUI();
        }
        CheckBourgeoisTax();

        // Sauvegarde d'état avant recalcul
        bool wasFinal = finalPhase;
        Turn prevChallenger = challenger;

        CheckWinConditionOnTurnEnded();
        if (gameOver || matchOver || isGameOverScreen) yield break;

        if (wasFinal && prevChallenger == Turn.Player)
        {
            // Le joueur avait posé la barre
            if (aiScore <= targetScore)
            {
                EndMatch(PLAYER_NAME);
                yield break;
            }
            else
            {
                StartNewTurn(Turn.Player);
                yield break;
            }
        }

        SetPhase(Phase.AITurnWaitEnd);
    }


    // ===================== DEV : LISTE COMPLÈTE DES ARTEFACTS =====================

    // Le bouton montre/cache le ScrollView ENTIER si le conteneur en fait partie,
    // sinon le conteneur lui-même.
    GameObject GetDevArtifactListPanel()
    {
        if (!devArtifactListRoot) return null;
        var scroll = devArtifactListRoot.GetComponentInParent<ScrollRect>(true);
        return scroll ? scroll.gameObject : devArtifactListRoot.gameObject;
    }

    void ToggleDevArtifactList()
    {
        var panel = GetDevArtifactListPanel();
        if (panel == null)
        {
            ShowAction("[DEV] Assigne 'devArtifactListRoot' (Content du scroll) dans l'Inspector.");
            return;
        }

        bool show = !panel.activeSelf;
        if (show)
        {
            BuildDevArtifactList();
            panel.transform.SetAsLastSibling();
        }
        panel.SetActive(show);
    }

    void BuildDevArtifactList()
    {
        // Reconstruit la liste à chaque ouverture (simple et toujours à jour)
        for (int i = devArtifactListRoot.childCount - 1; i >= 0; i--)
            Destroy(devArtifactListRoot.GetChild(i).gameObject);

        if (artifactLibrary == null || artifactLibrary.artifacts == null)
        {
            ShowAction("[DEV] ArtifactLibrary manquante.");
            return;
        }

        // Layout vertical simple (une ligne par artefact) si le conteneur n'en a pas déjà un,
        // + ContentSizeFitter pour que le scroll couvre la totalité de la liste.
        if (!devArtifactListRoot.GetComponent<UnityEngine.UI.LayoutGroup>())
        {
            var v = devArtifactListRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.spacing = 2f;
            v.padding = new RectOffset(8, 8, 8, 8);
        }
        if (!devArtifactListRoot.GetComponent<ContentSizeFitter>())
        {
            var fit = devArtifactListRoot.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        foreach (var a in artifactLibrary.artifacts)
        {
            if (a == null) continue;
            var captured = a;

            // Ligne texte simple : nom de l'artefact, cliquable
            var rowGO = new GameObject("Row_" + a.name, typeof(RectTransform));
            rowGO.transform.SetParent(devArtifactListRoot, false);

            var label = rowGO.AddComponent<TextMeshProUGUI>();
            label.text = string.IsNullOrEmpty(a.displayName) ? a.name : a.displayName;
            label.fontSize = 20f;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.raycastTarget = true;

            var le = rowGO.AddComponent<LayoutElement>();
            le.preferredHeight = 26f;
            le.minHeight = 22f;

            var btn = rowGO.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() =>
            {
                bool added = (playerInventory != null) && playerInventory.TryAdd(captured);
                if (added)
                {
                    inventoryUI?.RefreshNow();
                    inventoryDots?.Refresh();
                    ShowAction($"[DEV] Artefact ajouté : {captured.displayName}");
                    uiLog?.Append($"[DEV] Artefact ajouté à l'inventaire : {captured.displayName}");
                }
                else
                {
                    ShowAction("[DEV] Inventaire plein (3/3) — impossible d'ajouter.");
                }
            });
        }
    }

    // ===================== DEV : FORCER LES DÉS DU PROCHAIN JET =====================
    // Syntaxe du champ : valeurs séparées par espaces/virgules, dans l'ordre des dés.
    // Valeurs acceptées : 2, 3, 4, 5, 6, 10, et S / SUN / 100 pour le Soleil.
    // Exemple : "6 6 5 10 S" force les 5 dés ; "5 5" ne force que les deux premiers.
    private void OnPressDevForceFaces()
    {
        if (!devForceFacesInput || string.IsNullOrWhiteSpace(devForceFacesInput.text))
        { ShowAction("[DEV] Entre des valeurs (ex: 6 6 5 10 S)."); return; }
        if (dice == null || dice.Count == 0) return;

        var tokens = devForceFacesInput.text
            .Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

        var faces = new List<DieFace>();
        foreach (var raw in tokens)
        {
            var t = raw.Trim().ToLowerInvariant();
            DieFace? face = t switch
            {
                "2" => DieFace.Two,
                "3" => DieFace.Three,
                "4" => DieFace.Four,
                "5" => DieFace.Five,
                "6" => DieFace.Six,
                "10" => DieFace.Ten,
                "s" or "sun" or "100" => DieFace.Sun,
                _ => null
            };
            if (face == null)
            { ShowAction($"[DEV] Valeur invalide : '{raw}' (attendu 2/3/4/5/6/10/S)."); return; }
            faces.Add(face.Value);
        }

        int applied = Mathf.Min(faces.Count, dice.Count);
        for (int i = 0; i < applied; i++)
        {
            if (dice[i] != null)
                pendingForcedFaces[dice[i]] = faces[i];
        }

        ShowAction($"[DEV] Prochain jet forcé : {devForceFacesInput.text} ({applied} dé{(applied > 1 ? "s" : "")}).");
        uiLog?.Append($"[DEV] Faces forcées au prochain jet : {devForceFacesInput.text}");
    }

    private void OnPressDevAddCustomPoints()
    {
        if (!devCustomInput) { ShowAction("Champ custom manquant."); return; }
        if (!int.TryParse(devCustomInput.text, out int amount)) { ShowAction("Montant invalide."); return; }
        DevAddPoints(amount);
    }

    /// <summary>
    /// Ajoute des points (joueur par défaut, IA si toggle actif). Met à jour l'UI, 
    /// vérifie la phase finale et applique les gardes-fous basiques.
    /// </summary>
    private void DevAddPoints(int amount)
    {
        if (amount == 0) { ShowAction("Montant 0 ignoré."); return; }
        if (gameOver || matchOver || isGameOverScreen) { ShowAction("Match terminé."); return; }

        bool toAI = (devAddToAIToggle && devAddToAIToggle.isOn);

        if (toAI)
        {
            aiScore += amount;
            // optionnel: considérer l’ouverture atteinte si le total passe au-dessus du seuil
            if (!aiOpened && aiScore >= ENTRY_THRESHOLD) aiOpened = true;
            uiLog?.Append($"[DEV] +{amount} points à l'IA (total: {aiScore}).");
            ShowAction($"+{amount} points à l’IA.");
        }
        else
        {
            playerScore += amount;
            if (!playerOpened && playerScore >= ENTRY_THRESHOLD) playerOpened = true;
            uiLog?.Append($"[DEV] +{amount} points au Joueur (total: {playerScore}).");
            ShowAction($"+{amount} points au Joueur.");
        }

        UpdateUI();

        // ⚠️ important : réévaluer les conditions de victoire / phase finale
        CheckWinConditionOnTurnEnded();

        // Réapplique l'état des boutons (au cas où BANK/CONTINUE devient pertinent)
        ApplyButtonsState();
    }

    // Helpers d'état utiles aux artefacts "score"
    public bool IsMatchOverOrScreen()
    {
        return gameOver || matchOver || isGameOverScreen;
    }

    public bool IsPlayerAlreadyOpened() => playerOpened;

    public int EntryThreshold => ENTRY_THRESHOLD;

    /// <summary>
    /// Ouvre immédiatement le joueur (comme si le seuil d’entrée était atteint).
    /// Si grantPoints=true, crédite aussi le total joueur de ENTRY_THRESHOLD points.
    /// </summary>
    public void Artifact_GrantEntryNow(bool grantPoints)
    {
        if (IsMatchOverOrScreen()) return;

        if (!playerOpened)
            playerOpened = true;

        if (grantPoints)
        {
            playerScore += ENTRY_THRESHOLD;   // <-- crédite les 35 pts d'entrée
            uiLog?.Append($"+{ENTRY_THRESHOLD} (Ticket d’entrée).");
        }

        UpdateUI();

        // Important : réévaluer la phase finale si on passe un palier de victoire
        CheckWinConditionOnTurnEnded();

        ApplyButtonsState();
    }

    // ✅ Utilisable AVANT de résoudre une Clause (juste après la création d'un FLASH)
    public bool CanActivateScorePreClauseNow()
    {
        // même garde-fous que les autres artefacts de score : tour du joueur, match en cours
        if (gameOver || matchOver || isGameOverScreen) return false;
        return currentTurn == Turn.Player
            && flashPendingResolution           // un FLASH attend d’être dégagé
            && phase == Phase.Clause;           // on est bien dans la phase de Clause
    }



}
