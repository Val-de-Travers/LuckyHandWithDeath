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
    // Chemin boisé : le prochain match perdu est concédé SANS perte de vie (une seule fois).
    bool concedeNoLifeLoss = false;
    bool awaitingNextMatch = false;             // devient true UNIQUEMENT dans EndMatch
    string lastMatchWinner = "";

    // Garde-fou progression
    bool matchOver => awaitingNextMatch && !string.IsNullOrEmpty(lastMatchWinner);

    // ===================== UI DE MATCH =====================
    [Header("UI (Match)")]
    public TMP_Text playerScoreText;   // PlayerScoreText : score courant du joueur
    public TMP_Text aiScoreText;       // AIScoreText : score courant de l'adversaire
    [Tooltip("PlayerScoreGameText : score à atteindre par le JOUEUR (seuil d'ouverture tant qu'il " +
             "n'est pas ouvert, sinon objectif du palier ou score à battre en phase finale). " +
             "Laisser vide conserve l'ancien affichage combiné « score / objectif pts ».")]
    public TMP_Text playerScoreGoalText;
    [Tooltip("Équivalent côté ADVERSAIRE : score à atteindre. " +
             "Laisser vide conserve l'ancien affichage combiné « score / objectif pts ».")]
    public TMP_Text aiScoreGoalText;
    public TMP_Text turnScoreText;
    public TMP_Text turnLabel;
    [Tooltip("TableScore : image de fond du score du tour. Masquée pendant les phases de choix " +
             "d'artefact/trait (comme le texte du score du tour).")]
    public GameObject tableScoreObject;

    [Header("Score du tour — mise en valeur au seuil d'ouverture")]
    [Tooltip("AddScoreTurn : petit texte « +X » qui apparaît 2 s à côté du score du tour à chaque " +
             "sélection de dé marquant (rouge, sans contour).")]
    public TMP_Text addScoreTurnText;
    [Tooltip("Épaisseur du contour blanc du score du tour quand le seuil de 35 est atteint.")]
    public float turnScoreReachedOutlineWidth = 0.2f;
    [Tooltip("Durée TOTALE d'affichage (s) du texte « +X », fondus compris.")]
    public float addScoreFxDuration = 2f;
    [Tooltip("Durée (s) du fondu d'apparition et de disparition du « +X ».")]
    public float addScoreFadeDuration = 0.25f;
    [Tooltip("Recale automatiquement le « +X » juste à droite du nombre (suit sa largeur). " +
             "Nécessite que AddScoreTurn et le score du tour aient le même parent.")]
    public bool addScoreAutoPosition = true;
    [Tooltip("Écart horizontal (px) entre le bord droit du nombre et le « +X ».")]
    public float addScoreGapPx = 12f;
    [Tooltip("Décalage vertical (px) du « +X » par rapport au score du tour.")]
    public float addScoreOffsetYPx = 0f;

    // Couleur d'origine du texte du score du tour (restaurée sous le seuil d'ouverture).
    Color _turnScoreBaseColor = Color.white;
    // Contour d'origine de la typo (gris) : restauré quand le score n'est PAS en rouge.
    Color _turnScoreBaseOutlineColor = Color.clear;
    float _turnScoreBaseOutlineWidth = 0f;
    Coroutine _addScoreFxCo;
    // Position verticale d'origine du « +X » (figée pour éviter qu'il redescende sous le score).
    float _addScoreBaseY = 0f;

    public Button rollButton;
    public Button bankButton;
    public Button endRoundButton;

    [Header("Opponent (Center Portrait)")]
    public Image opponentPortraitImage; // assigner l'Image près de la table

    [Header("Dice")]
    public List<DieView> dice;

    // ===================== HOOKS TUTORIEL =====================
    // Renseignés par TutorialManager le cas échéant (aucun impact hors tutoriel).
    // Événement émis quand le joueur déclenche une action (Roll/Bank/EndRound).
    [System.NonSerialized] public System.Action<GameAction> onActionPerformed;
    // Filtre optionnel : renvoie false pour interdire (désactiver) une action.
    [System.NonSerialized] public System.Func<GameAction, bool> tutorialActionFilter;
    // Force optionnel : renvoie true pour ACTIVER un bouton même si le jeu le griserait
    // (étape de navigation, ex. NEXT hors phase de fin).
    [System.NonSerialized] public System.Func<GameAction, bool> tutorialForceEnable;

    public bool IsActionAllowedByTutorial(GameAction a)
        => tutorialActionFilter == null || tutorialActionFilter(a);

    // Faces forcées pour le PROCHAIN jet. Elles sont appliquées AU MOMENT DU JET, DANS L'ORDRE,
    // aux dés réellement relancés (les non-verrouillés). Sur une relance partielle : on n'inscrit
    // donc que les faces des dés NON sélectionnés — quel que soit le moment de la sélection.
    private readonly List<DieFace> tutorialNextRollFaces = new();
    public void Tutorial_ForceNextRoll(params DieFace[] faces)
    {
        tutorialNextRollFaces.Clear();
        if (faces != null) tutorialNextRollFaces.AddRange(faces);
    }

    // Recalcule l'état "interactable" des boutons d'action. Le tutoriel l'appelle à
    // chaque étape : sans ça, un bouton désactivé par le filtre resterait grisé même
    // une fois l'action ré-autorisée (ApplyButtonsState n'est sinon rappelé que sur
    // changement d'état de jeu).
    public void Tutorial_RefreshButtons() => ApplyButtonsState();

    // Tutoriel : mode actif → le jeu ne démarre PAS de partie tout seul.
    [System.NonSerialized] public bool tutorialMode = false;
    // Tutoriel : plateau "inerte" → aucun bouton actionnable (piloté par les Setup Phase).
    [System.NonSerialized] public bool tutorialInert = false;
    // Tutoriel : autorise UN tour d'IA (posé par les Setup Phase IA). Hors ça, l'IA ne joue pas
    // automatiquement en mode tutoriel — seule la mise en scène des phases dirige la partie.
    [System.NonSerialized] public bool tutorialAIAuthorized = false;
    // Tutoriel : la fenêtre de Contre-Jeu boucle (le timer ne s'écoule jamais) pour la démo.
    [System.NonSerialized] public bool tutorialLoopCounterPlay = false;

    // Tutoriel : force l'issue du tirage au sort d'ouverture (null = selon les faces jouées).
    [System.NonSerialized] public bool? tutorialRollOffPlayerWins = null;
    public void Tutorial_PlayerWinsRollOff() => tutorialRollOffPlayerWins = true;
    public void Tutorial_AIWinsRollOff() => tutorialRollOffPlayerWins = false;

    // --- Mise en scène : forcer l'état du jeu pour une étape de tutoriel ---

    // Tirage au sort d'ouverture (par défaut le joueur gagne, cf. tutorialRollOffPlayerWins).
    public void Tutorial_EnterRollOff()
    {
        tutorialInert = false;
        Tutorial_TeardownPicks();
        openingRollOffBusy = false;
        awaitingRollOffNext = false;
        currentTurn = Turn.Player;
        HideDice(false);
        ShowOnlyRollOffDice();
        SetPhase(Phase.OpeningRollOff);
        ApplyButtonsState();
    }

    public void Tutorial_StartPlayerTurn() { tutorialInert = false; Tutorial_TeardownPicks(); HideDice(false); StartNewTurn(Turn.Player); }

    // ===================== ACTIONS IA ATOMIQUES (tutoriel scripté) =====================

    // Démarre un tour IA "figé" : dés vierges affichés, aucune action automatique.
    public void Tutorial_AIStartTurn()
    {
        tutorialInert = false;
        Tutorial_TeardownPicks();
        tutorialAIAuthorized = false;   // pas d'auto-play
        StartNewTurn(Turn.AI);          // reset (turnScore=0, dés vierges), phase AITurnPlaying
        HideDice(false);
        Tutorial_ShowScore();
    }

    // Jet IA : lance les dés non verrouillés (faces forcées) et SÉLECTIONNE les dés marquants.
    public void Tutorial_AIRoll() => Tutorial_AIRollInternal(wimpoutIfNothing: false);

    // Relance IA : relance les dés non verrouillés (faces forcées). Si rien ne marque → wimpout
    // (l'IA perd le score du tour). Sinon sélectionne les marquants.
    public void Tutorial_AIReroll() => Tutorial_AIRollInternal(wimpoutIfNothing: true);

    void Tutorial_AIRollInternal(bool wimpoutIfNothing)
    {
        if (dice == null) return;
        tutorialInert = false;
        tutorialAIAuthorized = false;
        if (currentTurn != Turn.AI) StartNewTurn(Turn.AI); // sécurité : ouvre le tour IA au besoin
        HideDice(false);
        Tutorial_ShowScore();

        // Dés à (re)lancer : les non verrouillés ; si tous verrouillés (hot-dice) → tous.
        var toRoll = new List<int>();
        for (int i = 0; i < dice.Count; i++)
            if (dice[i] != null && !dice[i].isLocked) toRoll.Add(i);
        if (toRoll.Count == 0)
        {
            foreach (var d in dice) if (d != null) d.SetLocked(false);
            frozenLocks.Clear();
            flashVisualIndices.Clear();
            toRoll = Enumerable.Range(0, dice.Count).ToList();
        }

        // Lance avec les faces forcées (sinon aléatoire).
        int tf = 0;
        foreach (var idx in toRoll)
        {
            if (tf < tutorialNextRollFaces.Count) dice[idx].SetFace(tutorialNextRollFaces[tf++]);
            else dice[idx].Roll();
        }
        tutorialNextRollFaces.Clear();

        Tutorial_AILockScoring(toRoll, wimpoutIfNothing);
        UpdateUI();
        ApplyButtonsState();
    }

    // Verrouille les dés marquants du jet (flash puis singles) et crédite le score du tour.
    void Tutorial_AILockScoring(List<int> rolled, bool wimpoutIfNothing)
    {
        var eval = EvaluateRoll(rolled, scoreSingles: true);

        if (!eval.anyScoring && !eval.createdFlash)
        {
            if (wimpoutIfNothing)
            {
                turnScore = 0;
                JournalAI("Wimpout : aucun dé marquant — tour perdu !");
                SetPhase(Phase.AITurnWaitEnd);
            }
            return;
        }

        if (eval.createdFlash)
        {
            foreach (var i in eval.flashLockIndices) { dice[i].SetLocked(true); frozenLocks.Add(i); flashVisualIndices.Add(i); }
            turnScore += eval.pointsGained;
            // Ligne dédiée au FLASH (le mot est colorisé par le journal).
            JournalAI($"{DieTag(eval.flashFace)} FLASH (+{eval.pointsGained} pts)");
            // Puis chaque dé marquant restant, une ligne chacun (comme un dé sélectionné à part).
            var remaining = rolled.Where(i => !eval.flashLockIndices.Contains(i)).ToList();
            var eval2 = EvaluateRoll(remaining, scoreSingles: true);
            foreach (var i in eval2.lockedThisRoll) Tutorial_AILockSingle(i);
        }
        else
        {
            foreach (var i in eval.lockedThisRoll) Tutorial_AILockSingle(i);
        }
    }

    // Verrouille un dé marquant "single" et le comptabilise sur sa propre ligne de journal.
    void Tutorial_AILockSingle(int i)
    {
        dice[i].SetLocked(true);
        frozenLocks.Add(i);
        var f = dice[i].GetFace();
        int pts = (f == DieFace.Five) ? 5 : (f == DieFace.Ten || f == DieFace.Sun) ? 10 : 0;
        turnScore += pts;
        JournalAI($"{DieTag(f)} (+{pts} pts)");
    }

    // Démo Contre-Jeu : jet IA forcé créant un FLASH, puis fenêtre de contre-jeu EN BOUCLE.
    // Le joueur doit avoir (ou recevoir via On Enter) un artefact de contre-jeu (sleeping_horn).
    public void Tutorial_CounterPlayDemo()
    {
        if (dice == null) return;
        tutorialInert = false;
        tutorialAIAuthorized = false;
        if (currentTurn != Turn.AI) StartNewTurn(Turn.AI);
        HideDice(false);
        Tutorial_ShowScore();

        // Jet IA avec faces forcées → doit créer un FLASH.
        foreach (var d in dice) if (d != null) d.SetLocked(false);
        frozenLocks.Clear(); flashVisualIndices.Clear();
        var toRoll = Enumerable.Range(0, dice.Count).ToList();
        int tf = 0;
        foreach (var idx in toRoll)
        {
            if (tf < tutorialNextRollFaces.Count) dice[idx].SetFace(tutorialNextRollFaces[tf++]);
            else dice[idx].Roll();
        }
        tutorialNextRollFaces.Clear();

        // Verrouille le trio du flash et arme aiFlashIndices (nécessaire au contre-jeu).
        var eval = EvaluateRoll(toRoll, scoreSingles: false);
        aiFlashIndices.Clear();
        if (eval.createdFlash)
        {
            foreach (var i in eval.flashLockIndices) { dice[i].SetLocked(true); frozenLocks.Add(i); flashVisualIndices.Add(i); }
            turnScore += eval.pointsGained;
            currentFlashFace = eval.flashFace;
            aiFlashIndices.AddRange(eval.flashLockIndices);
            JournalAI($"{DieTag(eval.flashFace)} FLASH (+{eval.pointsGained} pts)");
        }
        UpdateUI();

        // Fenêtre de contre-jeu EN BOUCLE (le timer ne s'écoule pas).
        tutorialLoopCounterPlay = true;
        StartCoroutine(AICounterPlayWindow());
    }

    // L'IA banque le score du tour. Le score du tour reste AFFICHÉ (non remis à zéro) pour
    // l'étape suivante du tutoriel ; le prochain StartNewTurn le remettra à zéro.
    public void Tutorial_AIBank()
    {
        int banked = turnScore;
        aiScore += banked;
        JournalAI($"Banque {banked} pts (total {aiScore} pts).");
        if (!aiOpened && aiScore >= ENTRY_THRESHOLD) aiOpened = true;
        SetPhase(Phase.AITurnWaitEnd);
        UpdateUI();
    }
    public void Tutorial_EnterArtifactPick() { tutorialInert = false; Tutorial_TeardownPicks(); EnterArtifactPick(3); }

    // Choix d'artefacts IMPOSÉS (assets glissés dans l'étape). Liste vide → 3 aléatoires.
    // selectable = false → cartes affichées mais non cliquables (démonstration).
    public void Tutorial_EnterArtifactPick(List<Artifact> specific, bool selectable = true)
    {
        tutorialInert = false;
        Tutorial_TeardownPicks();
        var list = (specific != null) ? specific.Where(a => a != null).ToList() : null;
        if (list != null && list.Count > 0) EnterArtifactPick(list.Count, list, selectable);
        else EnterArtifactPick(3, null, selectable);
    }

    // Choix d'artefacts IMPOSÉS (par id, effectKey ou nom affiché). Ex : "cheaters_bone", "mirror".
    public void Tutorial_EnterArtifactPickByIds(params string[] ids)
    {
        tutorialInert = false;
        Tutorial_TeardownPicks();               // réinitialise un éventuel choix déjà ouvert
        var list = Tutorial_FindArtifacts(ids);
        EnterArtifactPick(list.Count, list);
    }

    // Remplit les DEUX journaux d'actions avec du contenu de démonstration (étape "journal").
    public void Tutorial_FillJournalsDemo()
    {
        ClearJournals();
        JournalPlayer($"{DieTag(DieFace.Ten)} FLASH (+100 pts)");
        JournalPlayer($"{DieTag(DieFace.Five)} (+5 pts)");
        JournalPlayer($"{DieTag(DieFace.Sun)} (+10 pts)");
        JournalPlayer("CLAUSE réussie");
        JournalPlayer("Banque 120 pts (total 120 pts)");

        JournalAI($"{DieTag(DieFace.Ten)} (+10 pts)");
        JournalAI($"{DieTag(DieFace.Five)} (+5 pts)");
        JournalAI("Wimpout : aucun dé marquant — tour perdu !");
        JournalAI($"{DieTag(DieFace.Six)} FULL (victoire !)");
    }

    // Donne un artefact (par id / effectKey / nom) à l'inventaire du joueur (étapes artefact).
    public void Tutorial_GiveArtifact(string id)
    {
        var a = Tutorial_FindArtifacts(new[] { id }).FirstOrDefault();
        Tutorial_GiveArtifactAsset(a);
    }

    // Donne un asset Artifact précis à l'inventaire du joueur (glissé dans une étape).
    public void Tutorial_GiveArtifactAsset(Artifact a)
    {
        if (a == null || playerInventory == null) return;
        playerInventory.TryAdd(a);
        inventoryUI?.RefreshNow();
        inventoryDots?.Refresh();
    }

    // Affiche VISUELLEMENT des traits sur l'adversaire (clés player.* ou boss.*), sans effet
    // de jeu — pour la démo (étape Boss). Câblable On Enter, clés séparées par virgule.
    public void Tutorial_SetOpponentTraits(string commaSeparatedKeys)
    {
        activeEnemyTraitInfos.Clear();
        foreach (var raw in (commaSeparatedKeys ?? "").Split(new[] { ',', ';' }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            var k = raw.Trim();
            var t = TraitCatalog.FindBoss(k) ?? TraitCatalog.FindPlayer(k);
            if (t != null) activeEnemyTraitInfos.Add(t);
        }
        RebuildTraitIcons();
    }

    // Version câblable dans un UnityEvent (On Enter) : ids séparés par virgule/point-virgule.
    public void Tutorial_ArtifactPickList(string commaSeparatedIds)
    {
        var ids = (commaSeparatedIds ?? "")
            .Split(new[] { ',', ';' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).ToArray();
        Tutorial_EnterArtifactPickByIds(ids);
    }

    List<Artifact> Tutorial_FindArtifacts(string[] ids)
    {
        var result = new List<Artifact>();
        if (artifactLibrary == null || artifactLibrary.artifacts == null || ids == null) return result;
        foreach (var id in ids)
        {
            var a = artifactLibrary.artifacts.FirstOrDefault(x => x != null &&
                (string.Equals(x.id, id, System.StringComparison.OrdinalIgnoreCase)
                 || string.Equals(x.effectKey, id, System.StringComparison.OrdinalIgnoreCase)
                 || string.Equals(x.displayName, id, System.StringComparison.OrdinalIgnoreCase)));
            if (a != null) result.Add(a);
        }
        return result;
    }

    // Ferme proprement un écran de choix (artefact/trait) laissé par une étape précédente,
    // pour ne pas bloquer les boutons ni laisser traîner des cartes.
    void Tutorial_TeardownPicks()
    {
        awaitingArtifactPick = false;
        awaitingTraitPick = false;
        foreach (var v in offeredCardViews) if (v) Destroy(v.gameObject);
        offeredCardViews.Clear();
        foreach (var v in offeredTraitCards) if (v) Destroy(v.gameObject);
        offeredTraitCards.Clear();
        ClosePalierPanel();
    }

    public void Tutorial_EnterTraitPick()
    {
        tutorialInert = false;
        Tutorial_TeardownPicks();
        var pool = PlayerTraitPool();
        var opts = (pool != null) ? pool.Take(3).ToList() : new List<TraitDef>();
        EnterTraitPick(opts, "Choisis un trait en cliquant sur sa carte.", null);
    }

    // Choix de traits IMPOSÉS par clés (player.* ou boss.*), séparées par virgule. Câblable On Enter.
    public void Tutorial_TraitPickList(string commaSeparatedKeys)
    {
        tutorialInert = false;
        Tutorial_TeardownPicks();
        var opts = new List<TraitDef>();
        foreach (var raw in (commaSeparatedKeys ?? "").Split(new[] { ',', ';' }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            var k = raw.Trim();
            var t = TraitCatalog.FindPlayer(k) ?? TraitCatalog.FindBoss(k);
            if (t != null) opts.Add(t);
        }
        if (opts.Count > 0) EnterTraitPick(opts, "Choisis un trait en cliquant sur sa carte.", null);
    }

    // Plateau vide : dés et score du tour masqués, écrans de choix fermés, aucune action.
    public void Tutorial_ClearBoard()
    {
        Tutorial_TeardownPicks();
        HideDice(true);
        SetTurnScoreVisible(false);
        tutorialInert = true;
        ApplyButtonsState();
    }

    void Tutorial_ShowScore() => Tutorial_SetScoreVisible(true);

    // Affiche/masque les éléments de score connus (score du tour + table).
    public void Tutorial_SetScoreVisible(bool visible)
    {
        SetTurnScoreVisible(visible);
        if (tableScoreObject) tableScoreObject.SetActive(visible);
    }

    // Tour du JOUEUR déjà lancé : le jeu est en phase de sélection des dés marquants
    // (comme une vraie partie : il faut sélectionner un dé marquant pour pouvoir relancer).
    public void Tutorial_SelectDicePlayer()
    {
        tutorialInert = false;
        Tutorial_TeardownPicks();
        HideDice(false);
        StartNewTurn(Turn.Player);
        Tutorial_ShowScore();
        StartCoroutine(ResolvePlayerRoll()); // premier jet → phase Normal (sélection)
    }

    // Tour de l'IA : elle lance et sélectionne automatiquement (démonstration).
    public void Tutorial_SelectDiceAI()
    {
        tutorialInert = false;
        Tutorial_TeardownPicks();
        HideDice(false);
        Tutorial_ShowScore();
        tutorialAIAuthorized = true;
        StartNewTurn(Turn.AI); // déclenche RunAITurn (l'IA joue et sélectionne)
    }

    // Réinitialise toutes les faces des dés (efface + masque) pour repartir d'un plateau propre.
    public void Tutorial_ResetDice()
    {
        if (dice == null) return;
        foreach (var d in dice)
        {
            if (d == null) continue;
            d.ResetForNewTurn();
            d.gameObject.SetActive(false);
        }
    }

    // Affiche des faces sur les dés immédiatement, sans lancer (pour montrer les faces).
    // Seuls les dés recevant une face sont affichés ; les autres sont réinitialisés et
    // masqués (pas de dé « vide » ni de face résiduelle d'une étape précédente).
    public void Tutorial_ShowDiceFaces(params DieFace[] faces)
    {
        if (dice == null) return;
        int n = (faces != null) ? Mathf.Min(faces.Length, dice.Count) : 0;
        for (int i = 0; i < dice.Count; i++)
        {
            var d = dice[i];
            if (d == null) continue;
            if (i < n)
            {
                d.gameObject.SetActive(true);
                d.SetFace(faces[i]);
            }
            else
            {
                d.ResetForNewTurn();            // efface toute face résiduelle
                d.gameObject.SetActive(false);  // dé sans face : on ne l'affiche pas
            }
        }
    }

    // Affiche ou masque le bandeau d'aide du jeu (pour ne pas parasiter le dialogue de tuto).
    public void Tutorial_SetHintBannerVisible(bool visible)
    {
        if (hintBanner) hintBanner.gameObject.SetActive(visible);
    }

    [Header("UX Helpers")]
    public HintBanner hintBanner;
    public UILog uiLog;

    [Header("Journaux personnels (actions du tour)")]
    [Tooltip("Texte du journal du JOUEUR : liste les actions de son tour courant (une par ligne).")]
    public TMP_Text playerJournalText;
    [Tooltip("Texte du journal de l'ADVERSAIRE : liste les actions de son tour courant (une par ligne).")]
    public TMP_Text aiJournalText;
    [Tooltip("Optionnel : ScrollRect contenant le texte du journal joueur (auto-scroll vers le bas).")]
    public ScrollRect playerJournalScroll;
    [Tooltip("Optionnel : ScrollRect contenant le texte du journal adverse (auto-scroll vers le bas).")]
    public ScrollRect aiJournalScroll;
    [Tooltip("Marges internes (px) du texte des journaux : X=gauche, Y=haut, Z=droite, W=bas.")]

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

    // ===================== JOURNAUX PERSONNELS =====================
    // Chaque camp possède son propre journal qui accumule TOUTES les actions de son tour
    // courant (une ligne par action). Le journal d'un camp est remis à zéro au début de
    // SON tour, puis il se remplit au fil des actions ; il reste ensuite affiché tel quel
    // jusqu'au tour suivant de ce même camp.
    static string FaceLabel(DieFace f) => f == DieFace.Sun ? "SUN" : ((int)f).ToString();

    [Header("Journaux — image du dé (optionnel)")]
    [Tooltip("Sprite Asset TMP contenant les faces de dé (sprites nommés die_2 … die_10, die_sun). " +
             "Assigné : les dés cités dans le journal s'affichent en IMAGE. Vide : ils restent en texte.")]
    public TMP_SpriteAsset diceJournalSpriteAsset;
    [Tooltip("Préfixe des noms de sprites dans le Sprite Asset (die_2, die_10, die_sun…).")]
    public string diceSpriteNamePrefix = "die_";
    [Tooltip("Taille de l'image de dé dans le journal, en % de la taille du texte (200 = 2× plus grand).")]
    public float diceSpriteScalePercent = 200f;
    [Tooltip("Marge à GAUCHE de l'image de dé (en em) pour ne pas empiéter sur le mot précédent.")]
    public float diceSpriteLeftSpaceEm = 0.2f;
    [Tooltip("Décalage VERTICAL de l'image (en em). Négatif = vers le bas (centre un gros dé sur la ligne).")]
    public float diceSpriteVOffsetEm = -0.2f;
    [Tooltip("Marge à DROITE de l'image (en em). NÉGATIF pour rapprocher le mot suivant si le dé " +
             "laisse trop d'espace (l'avance du sprite est doublée par l'agrandissement).")]
    public float diceSpriteRightSpaceEm = -0.2f;

    // Clé de face pour les noms de sprites / tokens : 2..6, 10, sun.
    static string DieKey(DieFace f) => f == DieFace.Sun ? "sun" : ((int)f).ToString();

    // Points d'un dé marquant à l'unité : 5 → 5, 10/SUN → 10.
    static int SinglePoints(DieFace f) => (f == DieFace.Five) ? 5 : 10;

    // Token de dé DANS LE TEXTE BRUT du journal : neutre pour le stockage/retrait de lignes,
    // converti à l'affichage en <sprite> (si un Sprite Asset est branché) ou en texte.
    static string DieTag(DieFace f) => $"[[die:{DieKey(f)}]]";

    // Construit la balise TMP d'affichage d'un dé : marge à gauche (<space>), agrandissement
    // (<size>) et décalage vertical (<voffset>) autour du <sprite>. Les réglages viennent de
    // l'Inspector pour un ajustement fin sans recompiler.
    string BuildDieSpriteTag(string key)
    {
        var sb = new System.Text.StringBuilder();

        if (diceSpriteLeftSpaceEm > 0f)
            sb.Append($"<space={diceSpriteLeftSpaceEm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}em>");

        bool useVoff = Mathf.Abs(diceSpriteVOffsetEm) > 0.001f;
        bool useSize = Mathf.Abs(diceSpriteScalePercent - 100f) > 0.01f;

        // voffset AUTOUR de size : l'em du décalage reste relatif à la taille normale du texte.
        if (useVoff) sb.Append($"<voffset={diceSpriteVOffsetEm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}em>");
        if (useSize) sb.Append($"<size={diceSpriteScalePercent.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}%>");

        sb.Append($"<sprite name=\"{diceSpriteNamePrefix}{key}\">");

        if (useSize) sb.Append("</size>");
        if (useVoff) sb.Append("</voffset>");

        // Marge à droite (souvent négative) pour compenser l'avance du sprite.
        if (Mathf.Abs(diceSpriteRightSpaceEm) > 0.0001f)
            sb.Append($"<space={diceSpriteRightSpaceEm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}em>");

        return sb.ToString();
    }

    private readonly List<string> playerJournalLines = new();
    private readonly List<string> aiJournalLines = new();

    // Lignes de journal correspondant aux dés ACTUELLEMENT sélectionnés (verrous mutables) du
    // joueur : permet de retirer la ligne « Garde un X » si le dé est désélectionné. Vidé quand
    // la sélection est figée (ROLL/BANK) : les lignes deviennent alors des actions définitives.
    private readonly Dictionary<int, string> playerSelectionJournalLine = new();

    // Ligne « Clause réussie » en attente : encore annulable tant que la sélection qui a dégagé
    // le Flash n'est pas figée (ROLL/BANK). Si le joueur ré-arme la Clause (désélection), on la retire.
    private string playerClauseClearedLine = null;

    List<string> JournalBuffer(Turn who) => (who == Turn.Player) ? playerJournalLines : aiJournalLines;
    TMP_Text JournalTextFor(Turn who) => (who == Turn.Player) ? playerJournalText : aiJournalText;
    ScrollRect JournalScrollFor(Turn who) => (who == Turn.Player) ? playerJournalScroll : aiJournalScroll;

    // ---- Code couleur des journaux (rich text TMP) ----
    // FLASH = jaune · CLAUSE = vert · points = rouge (couleurs partagées, voir GameColors).
    // FLASH, CLAUSE et FULL sont aussi passés en MAJUSCULES à l'affichage.
    static readonly System.Text.RegularExpressions.Regex RxJournalPoints =
        new System.Text.RegularExpressions.Regex(@"[+-]?\d+\s*pts",
            System.Text.RegularExpressions.RegexOptions.Compiled);
    static readonly System.Text.RegularExpressions.Regex RxJournalTotal =
        new System.Text.RegularExpressions.Regex(@"(?<=total\s)\d+",
            System.Text.RegularExpressions.RegexOptions.Compiled |
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    static readonly System.Text.RegularExpressions.Regex RxJournalFlash =
        new System.Text.RegularExpressions.Regex(@"\bflashs?\b",
            System.Text.RegularExpressions.RegexOptions.Compiled |
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    static readonly System.Text.RegularExpressions.Regex RxJournalClause =
        new System.Text.RegularExpressions.Regex(@"\bclauses?\b",
            System.Text.RegularExpressions.RegexOptions.Compiled |
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    static readonly System.Text.RegularExpressions.Regex RxJournalFull =
        new System.Text.RegularExpressions.Regex(@"\bfulls?\b",
            System.Text.RegularExpressions.RegexOptions.Compiled |
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    static readonly System.Text.RegularExpressions.Regex RxDieToken =
        new System.Text.RegularExpressions.Regex(@"\[\[die:(\w+)\]\]",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    // Applique tokens de dé + majuscules + code couleur à l'affichage uniquement : le buffer garde
    // le texte BRUT (indispensable pour retirer une ligne par correspondance exacte à l'annulation).
    string ColorizeJournalLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return line;

        // Les points d'abord : leurs motifs ne contiennent aucun mot-clé, et les balises
        // insérées ensuite ne contiennent aucun chiffre suivi de « pts ».
        line = RxJournalPoints.Replace(line, m => $"<color=#{GameColors.PointsHex}>{m.Value}</color>");
        line = RxJournalTotal.Replace(line, m => $"<color=#{GameColors.PointsHex}>{m.Value}</color>");

        // Tokens de dé → <sprite> (image) si un Sprite Asset est branché, sinon libellé texte.
        bool useSprites = diceJournalSpriteAsset != null;
        line = RxDieToken.Replace(line, m =>
        {
            string key = m.Groups[1].Value;
            if (useSprites) return BuildDieSpriteTag(key);
            return key == "sun" ? "SUN" : key; // repli texte
        });

        // ToUpperInvariant conserve le pluriel éventuel (Flashs → FLASHS).
        line = RxJournalFlash.Replace(line, m => $"<color=#{GameColors.FlashHex}>{m.Value.ToUpperInvariant()}</color>");
        line = RxJournalClause.Replace(line, m => $"<color=#{GameColors.ClauseHex}>{m.Value.ToUpperInvariant()}</color>");
        line = RxJournalFull.Replace(line, m => m.Value.ToUpperInvariant());
        return line;
    }

    void RenderJournal(Turn who)
    {
        // On mémorise AVANT la mise à jour si l'utilisateur était déjà en bas : si oui, on suit
        // la nouvelle ligne ; s'il avait remonté le fil pour relire, on ne le dérange pas.
        var sr = JournalScrollFor(who);
        bool wasAtBottom = JournalIsAtBottom(sr);

        var txt = JournalTextFor(who);
        if (txt)
        {
            txt.richText = true;
            txt.text = string.Join("\n", JournalBuffer(who).Select(ColorizeJournalLine));
        }

        if (sr && wasAtBottom) StickJournalToBottom(sr);
    }

    // Colle la vue au bas de façon FIABLE : on force d'abord le recalcul de la mise en page
    // (ContentSizeFitter/LayoutGroup) — sinon la taille du contenu est périmée et le "bas" est
    // faux — puis on re-colle au frame suivant (le fitter peut se stabiliser un cran plus tard).
    void StickJournalToBottom(ScrollRect sr)
    {
        if (!sr) return;
        if (sr.content) LayoutRebuilder.ForceRebuildLayoutImmediate(sr.content);
        Canvas.ForceUpdateCanvases();
        sr.verticalNormalizedPosition = 0f;
        if (isActiveAndEnabled) StartCoroutine(StickBottomNextFrame(sr));
    }

    System.Collections.IEnumerator StickBottomNextFrame(ScrollRect sr)
    {
        yield return null; // laisse la mise en page se stabiliser
        if (!sr) yield break;
        if (sr.content) LayoutRebuilder.ForceRebuildLayoutImmediate(sr.content);
        sr.verticalNormalizedPosition = 0f;
    }

    // Vrai si la vue est en bas OU si le contenu ne dépasse pas encore (rien à scroller).
    static bool JournalIsAtBottom(ScrollRect sr)
    {
        if (!sr) return true;
        var content = sr.content;
        var vp = sr.viewport ? sr.viewport : (RectTransform)sr.transform;
        if (content == null) return true;
        if (content.rect.height <= vp.rect.height + 1f) return true; // pas de débordement
        return sr.verticalNormalizedPosition <= 0.05f;               // proche du bas
    }

    // Configure un ScrollRect de journal pour qu'il défile réellement :
    // Content ancré en haut + ContentSizeFitter (grandit vers le bas) + texte en wrap.
    // Robuste quelle que soit la hiérarchie (texte = Content, ou texte enfant du Content).
    void ConfigureJournalScrollView(ScrollRect sr, TMP_Text txt)
    {
        if (!sr || !txt) return;

        sr.horizontal = false;
        sr.vertical = true;
        if (sr.movementType == ScrollRect.MovementType.Unrestricted)
            sr.movementType = ScrollRect.MovementType.Clamped;
        if (sr.scrollSensitivity < 1f) sr.scrollSensitivity = 20f;

        // Sprite Asset des dés (facultatif) pour afficher les faces en image dans le journal.
        if (diceJournalSpriteAsset != null) txt.spriteAsset = diceJournalSpriteAsset;

        // Le Content = zone défilante (assignée dans le ScrollRect, sinon le parent du texte).
        var content = sr.content ? sr.content : txt.rectTransform.parent as RectTransform;
        if (content == null) return;
        sr.content = content;

        // Ancrage haut + pivot haut : le contenu s'étend vers le BAS quand il grandit.
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot     = new Vector2(0.5f, 1f);
        content.anchoredPosition = new Vector2(content.anchoredPosition.x, 0f);
        content.sizeDelta = new Vector2(0f, content.sizeDelta.y);

        // Le Content épouse la hauteur de son contenu (c'est ce qui rend la zone « scrollable »).
        var fitter = content.GetComponent<ContentSizeFitter>();
        if (!fitter) fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        // Si le texte est un ENFANT du Content, un VerticalLayoutGroup fait remonter sa hauteur
        // préférée jusqu'au Content (sinon le ContentSizeFitter n'aurait rien à mesurer).
        // Les marges sont portées par le padding du layout (réserve l'espace dans le contenu défilant).
        if (txt.rectTransform != content)
        {
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            if (!vlg) vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
        }

        // Texte : retour à la ligne (pas de scroll horizontal), débordement vertical autorisé.
        txt.textWrappingMode = TextWrappingModes.Normal;
        txt.overflowMode = TextOverflowModes.Overflow;
        txt.raycastTarget = true; // permet d'attraper le glisser-pour-scroller au-dessus du texte

        // Viewport (masque) : indispensable pour couper ce qui dépasse. On récupère au moins
        // le parent du Content si rien n'est assigné.
        if (sr.viewport == null && content.parent is RectTransform vp) sr.viewport = vp;

        var viewport = sr.viewport;
        if (viewport)
        {
            // Un masque coupe le contenu qui dépasse (sinon le texte déborde hors du cadre).
            if (!viewport.GetComponent<RectMask2D>() && !viewport.GetComponent<Mask>())
                viewport.gameObject.AddComponent<RectMask2D>();

            // Un graphic « raycast target » (même invisible) permet à la molette et au glisser
            // de scroller partout sur la zone, pas seulement au-dessus du texte.
            var vpGraphic = viewport.GetComponent<Graphic>();
            if (vpGraphic == null)
            {
                var img = viewport.gameObject.AddComponent<Image>();
                img.color = new Color(0f, 0f, 0f, 0f); // invisible mais capte le raycast
                img.raycastTarget = true;
            }
            else vpGraphic.raycastTarget = true;
        }
    }

    // Ajoute une action au journal du camp indiqué. Les doublons consécutifs sont conservés :
    // sélectionner un 2e dé identique doit apparaître comme une action distincte.
    void AppendJournal(Turn who, string action)
    {
        if (string.IsNullOrEmpty(action)) return;
        JournalBuffer(who).Add(action);
        RenderJournal(who);
    }

    // Retire la DERNIÈRE occurrence d'une ligne (utilisé quand une action est annulée).
    void RemoveJournalLine(Turn who, string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        var buf = JournalBuffer(who);
        int idx = buf.LastIndexOf(line);
        if (idx >= 0) { buf.RemoveAt(idx); RenderJournal(who); }
    }

    void JournalPlayer(string action) => AppendJournal(Turn.Player, action);
    void JournalAI(string action) => AppendJournal(Turn.AI, action);
    // Route vers le journal du camp qui joue actuellement.
    void JournalCurrent(string action) => AppendJournal(currentTurn, action);

    // Wrappers publics : utilisés par les artefacts (toujours des actions du JOUEUR,
    // même joués pendant le tour de l'IA en Contre-Jeu).
    public void JournalPlayerAction(string action) => AppendJournal(Turn.Player, action);
    public void JournalAIAction(string action) => AppendJournal(Turn.AI, action);

    // Journalise la sélection d'un dé et retient sa ligne pour un éventuel retrait (désélection).
    void JournalPlayerSelectDie(int idx, DieFace face, int pts)
    {
        string line = $"Garde un {DieTag(face)} (+{pts} pts)";
        JournalPlayer(line);
        playerSelectionJournalLine[idx] = line;
    }

    // Retire du journal la ligne de sélection d'un dé qui vient d'être désélectionné.
    void JournalPlayerUnselectDie(int idx)
    {
        if (playerSelectionJournalLine.TryGetValue(idx, out var line))
        {
            RemoveJournalLine(Turn.Player, line);
            playerSelectionJournalLine.Remove(idx);
        }
    }

    // La sélection est figée (ROLL/BANK/Flash) : ses lignes deviennent définitives, on oublie
    // leur correspondance dé→ligne pour ne plus jamais les retirer par erreur.
    void FreezePlayerSelectionJournal()
    {
        playerSelectionJournalLine.Clear();
        playerClauseClearedLine = null; // la Clause réussie est désormais actée (non annulable)
    }

    // Retire du journal la ligne « Clause réussie » encore annulable (Clause ré-armée).
    void RemovePlayerClauseClearedLine()
    {
        if (playerClauseClearedLine != null)
        {
            RemoveJournalLine(Turn.Player, playerClauseClearedLine);
            playerClauseClearedLine = null;
        }
    }

    // Vide le journal d'un camp (début de son tour) et rafraîchit l'affichage.
    void ResetJournalFor(Turn who)
    {
        JournalBuffer(who).Clear();
        if (who == Turn.Player) { playerSelectionJournalLine.Clear(); playerClauseClearedLine = null; }
        RenderJournal(who);
    }

    void ClearJournals()
    {
        playerJournalLines.Clear();
        aiJournalLines.Clear();
        playerSelectionJournalLine.Clear();
        playerClauseClearedLine = null;
        if (playerJournalText) playerJournalText.text = "";
        if (aiJournalText) aiJournalText.text = "";
    }

    // ===================== PANNEAU DE PALIER =====================
    [Header("Panneau de Palier")]
    [Tooltip("Panneau informatif du palier, ouvert/fermé par PalierCircle.")]
    public GameObject palierPanel;
    [Tooltip("PalierCircle : bouton qui ouvre/ferme le panneau de palier.")]
    public Button palierCircleButton;
    [Tooltip("Optionnel : bouton de fermeture à l'intérieur du panneau.")]
    public Button palierPanelCloseButton;
    [Tooltip("NumPalier : numéro du palier courant, affiché en chiffres ROMAINS.")]
    public TMP_Text palierNumberText;

    [Header("Panneau de Palier — liste verticale des ennemis")]
    [Tooltip("Une Image par adversaire du palier, dans l'ordre de la liste verticale.")]
    public List<Image> palierEnemyIcons = new();
    [Tooltip("Icône d'un adversaire pas encore joué / en cours (icon_enemy_ingame).")]
    public Sprite enemyIconInGame;
    [Tooltip("Icône d'un adversaire battu (icon_enemy_win).")]
    public Sprite enemyIconWin;
    [Tooltip("Icône d'un adversaire contre lequel on a perdu (icon_enemy_loose).")]
    public Sprite enemyIconLoose;
    [Tooltip("Épaisseur (px) du contour rouge sur l'adversaire en cours.")]
    public float currentEnemyOutlineWidth = 4f;

    // Résultat de chaque adversaire du palier COURANT : 0 = pas encore joué, 1 = gagné, -1 = perdu.
    private readonly List<int> palierEnemyResults = new();

    [Header("Palier Panel (récapitulatif de progression)")]
    [Tooltip("Panneau complet de progression (lignes, ennemis, traits). Voir PalierPanelView.")]
    public PalierPanelView palierPanelView;

    // Historique des résultats par palier sur toute la partie ([palier][ennemi] : 0/1/-1).
    private readonly List<List<int>> palierResultsHistory = new();
    // Traits obtenus à chaque franchissement de palier (clé = index de segment).
    // Un segment peut en contenir PLUSIEURS (ex : trait de joueur + trait d'ennemi).
    private readonly Dictionary<int, List<TraitDef>> segmentTraits = new();

    [Header("Vies (3 défaites max)")]
    [Tooltip("Images des vies, dans l'ordre. Une vie perdue est grisée (de la droite vers la gauche).")]
    public List<Image> lifeIcons = new();
    [Tooltip("Teinte appliquée à une vie perdue.")]
    public Color lifeLostTint = new Color(0.3f, 0.3f, 0.3f, 0.6f);

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
    [Tooltip("Optionnel : zone de dépôt posée sur le PERSONNAGE. Pendant le choix d'artefact/trait, " +
             "on peut glisser une carte dessus pour la choisir (en plus du clic).")]
    public ArtifactDropZone artifactPickDropZone;   // zone « personnage » pour choisir par drag & drop

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

    [Header("Dev Tools - Master Toggle")]
    [Tooltip("Toggle qui affiche/masque TOUS les objets de la scène dont le nom commence par \"Dev\". " +
             "⚠️ Ne pas nommer ce Toggle \"Dev...\" (il s'exclut de lui-même, mais autant éviter).")]
    public Toggle devMasterToggle;

    readonly List<GameObject> devObjectsCache = new();

    // Recense tous les objets "Dev*" de la scène (y compris inactifs), hors le Toggle maître.
    void CacheDevObjects()
    {
        devObjectsCache.Clear();
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            {
                if (!tr || !tr.name.StartsWith("Dev")) continue;
                if (devMasterToggle &&
                    (tr.gameObject == devMasterToggle.gameObject ||
                     tr.IsChildOf(devMasterToggle.transform) ||
                     devMasterToggle.transform.IsChildOf(tr)))
                    continue; // ne jamais se masquer soi-même
                devObjectsCache.Add(tr.gameObject);
            }
        }
    }

    // Coché → outils visibles ; décoché → outils masqués.
    void SetDevObjectsVisible(bool visible)
    {
        if (devObjectsCache.Count == 0) CacheDevObjects();
        foreach (var go in devObjectsCache)
            if (go) go.SetActive(visible);
        uiLog?.Append($"[DEV] Outils de debug {(visible ? "affichés" : "masqués")} ({devObjectsCache.Count} objets).");
    }

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
    // VISUEL UNIQUEMENT : dés d'un Flash non suivis par flashLockIndices — Flash de l'IA.
    // Sert au surlignage orange, n'influence aucune règle.
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
    // Le tour du joueur est perdu MAIS son score reste affiché (« en péril ») tant qu'il
    // n'a pas confirmé la fin du tour : un artefact peut encore le sauver.
    private bool turnScoreAtRisk = false;

    // Marque le score du tour du JOUEUR comme perdu-en-attente : il reste affiché et
    // n'est réellement remis à zéro qu'au changement de tour (StartNewTurn).
    //
    // ⚠️ « En péril » n'a de sens que si le joueur peut ENCORE sauver le tour. Sans artefact
    // de sauvetage utilisable, le tour est définitivement perdu : on solde le score tout de
    // suite plutôt que d'afficher un « (en péril) » trompeur.
    void MarkPlayerTurnScoreAtRisk()
    {
        if (!PlayerHasRescueArtifact())
        {
            wimpoutLostTurnScore = 0;
            turnScoreAtRisk = false;
            turnScore = 0;
            UpdateUI();
            return;
        }

        wimpoutLostTurnScore = turnScore;
        turnScoreAtRisk = turnScore > 0;
        UpdateUI();
    }

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
    // PiercedPurse : -25% sur la bank de l'IA pour CE tour (0.75). Guillotine : 0.
    private float aiBankMultiplier = 1f;
    // Nom du Contre-Jeu à afficher dans ScoreModifierText quand la bank IA est modifiée.
    private string aiBankModLabel = "Bourse percée";
    // Vrai UNIQUEMENT pendant la fenêtre de contre-jeu qui précède DIRECTEMENT une bank de l'IA
    // (score du tour validé mais pas encore banqué). La Guillotine n'est jouable qu'à ce moment.
    private bool aiPreBankCounterOpen = false;
    public bool IsAIPreBankCounterOpen() => awaitingCounterPlay && currentTurn == Turn.AI && aiPreBankCounterOpen;
    // Dés du Flash IA en cours (valide uniquement pendant la fenêtre qui suit un Flash).
    private readonly List<int> aiFlashIndices = new List<int>();
    // Un contre-jeu a touché un dé du Flash → le Flash est invalidé (pas de Clause).
    private bool aiFlashCancelled = false;
    // Un contre-jeu a modifié les dés → à la fermeture de la fenêtre, l'IA re-sélectionne
    // les dés marquants libres (elle ne les relance pas).
    private bool aiDiceChangedByCounter = false;
    // Dés relancés par un contre-jeu (Coup de table, Clocher…) : le résultat est DÉFINITIF,
    // l'IA ne peut pas les relancer une seconde fois ce tour-ci.
    private readonly HashSet<int> aiNoRerollIndices = new();

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
    [Tooltip("Au-delà de ce nombre de traits, les vignettes se chevauchent pour tenir dans la boîte.")]
    public int traitOverlapThreshold = 3;
    [Range(0f, 0.9f)]
    [Tooltip("Proportion de chevauchement (0.35 = les vignettes se recouvrent de 35% de leur largeur).")]
    public float traitOverlapRatio = 0.35f;

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

    // ===== Contour « trait actif » (l'effet est en train d'agir) — joueur ET IA =====
    // Registre des vignettes vivantes par clé, pour piloter le contour sans tout reconstruire.
    private readonly Dictionary<string, TraitIconView> playerTraitViews = new();
    private readonly Dictionary<string, TraitIconView> aiTraitViews = new();
    // Clés dont le contour est allumé : mémorisé pour survivre à une reconstruction des icônes.
    private readonly HashSet<string> playerActiveTraitOutlines = new();
    private readonly HashSet<string> aiActiveTraitOutlines = new();

    // Allume/éteint le contour « actif » d'un trait et mémorise l'état.
    void SetTraitActiveOutline(string key, bool isPlayerSide, bool on)
    {
        key = NormalizeTraitKey(key);
        var set = isPlayerSide ? playerActiveTraitOutlines : aiActiveTraitOutlines;
        if (on) set.Add(key); else set.Remove(key);

        var map = isPlayerSide ? playerTraitViews : aiTraitViews;
        if (map.TryGetValue(key, out var v) && v) v.SetActiveOutline(on);
    }

    // Marque un trait comme « actif » : le contour reste allumé AU MOINS jusqu'à la fin du tour
    // en cours (joueur ou IA). Il est effacé par ClearAllTraitActiveOutlines au StartNewTurn suivant.
    public void MarkTraitActiveForTurn(string key, bool isPlayerSide)
    {
        SetTraitActiveOutline(key, isPlayerSide, true);
    }

    // Éteint tous les contours (au changement de tour : rien ne traîne d'un tour à l'autre).
    void ClearAllTraitActiveOutlines()
    {
        playerActiveTraitOutlines.Clear();
        aiActiveTraitOutlines.Clear();
        foreach (var v in playerTraitViews.Values) if (v) v.SetActiveOutline(false);
        foreach (var v in aiTraitViews.Values) if (v) v.SetActiveOutline(false);
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
            default:              return false; // traits permanents ou à usage non limité
        }
    }

    // Signature du dernier rendu par côté : évite de DÉTRUIRE/recréer les vignettes quand rien
    // n'a changé (mêmes traits, mêmes états). Détruire un objet encore sélectionné dans
    // l'Inspector fait cracher l'Éditeur (SerializedObjectNotCreatableException) — on limite donc
    // ces destructions aux seuls vrais changements.
    private string lastPlayerTraitSig = null;
    private string lastAiTraitSig = null;

    string TraitIconsSignature(List<TraitDef> traits, bool isPlayerSide)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var t in traits)
        {
            if (t == null) continue;
            bool inactive = !isPlayerSide && !activeEnemyTraitKeys.Contains(t.key);
            bool consumed = IsTraitConsumed(t.key, isPlayerSide);
            bool hasIcon = GetTraitIcon(t) != null;
            sb.Append(t.key).Append(consumed ? '1' : '0').Append(inactive ? '1' : '0').Append(hasIcon ? '1' : '0').Append(';');
        }
        return sb.ToString();
    }

    void RebuildTraitIconsInto(RectTransform root, List<TraitDef> traits, bool isPlayerSide)
    {
        if (!root) return;

        // Rien n'a changé depuis le dernier rendu de ce côté → on NE détruit rien (les vignettes
        // existantes restent valides, le contour « actif » est piloté à part par SetTraitActiveOutline).
        string sig = TraitIconsSignature(traits, isPlayerSide);
        if (isPlayerSide) { if (sig == lastPlayerTraitSig) return; lastPlayerTraitSig = sig; }
        else              { if (sig == lastAiTraitSig)     return; lastAiTraitSig = sig; }

        // Les anciennes vignettes vont être détruites : on vide le registre de ce côté.
        (isPlayerSide ? playerTraitViews : aiTraitViews).Clear();

        for (int i = root.childCount - 1; i >= 0; i--)
            Destroy(root.GetChild(i).gameObject);

        // Layout horizontal ; au-delà de traitOverlapThreshold vignettes, on les fait
        // se CHEVAUCHER (spacing négatif) pour qu'elles tiennent dans la boîte.
        // Le survol met la vignette au premier plan avec un léger zoom (TraitIconView).
        var layout = root.GetComponent<HorizontalLayoutGroup>();
        if (!layout && !root.GetComponent<UnityEngine.UI.LayoutGroup>())
            layout = root.gameObject.AddComponent<HorizontalLayoutGroup>();

        if (layout)
        {
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.spacing = (traits.Count > traitOverlapThreshold) ? -traitIconSize.x * traitOverlapRatio : 4f;
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

            // Côté IA : un trait présent mais NON ACTIVÉ à ce palier est affiché atténué.
            bool inactive = !isPlayerSide && !activeEnemyTraitKeys.Contains(t.key);
            view.Setup(sprite, t.name, t.desc, tooltip, IsTraitConsumed(t.key, isPlayerSide), inactive);

            // Enregistre la vignette et ré-applique un éventuel contour « actif » en cours.
            string nkey = NormalizeTraitKey(t.key);
            (isPlayerSide ? playerTraitViews : aiTraitViews)[nkey] = view;
            if ((isPlayerSide ? playerActiveTraitOutlines : aiActiveTraitOutlines).Contains(nkey))
                view.SetActiveOutline(true);
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
    // « Marée montante » (boss) : effet permanent, points gagnés par le Boss à chaque relance du joueur.
    private const int TIDE_POINTS_PER_REROLL = 10;
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
    // Après un choix de trait affiché sur le Palier Panel : on attend Next avant de continuer.
    private bool awaitingPalierPanelNext = false;
    private Action palierPanelNextCallback = null;
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
        MarkTraitActiveForTurn("player.tax", isPlayerSide: true);
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
        MarkTraitActiveForTurn("boss.audit", isPlayerSide: false);
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
        palierHudText.text = $"Palier {palierIndex + 1}/{PalierCount()} ({GetWinThresholdForPalier(palierIndex)} pts)";
        tourHudText.text = $"Adversaire {enemyIndex + 1}/{EnemiesPerPalier(palierIndex)}";
    }


    void Start()
    {
        // Rend les journaux réellement défilables (ContentSizeFitter, ancrage haut, masque…).
        ConfigureJournalScrollView(playerJournalScroll, playerJournalText);
        ConfigureJournalScrollView(aiJournalScroll, aiJournalText);

        // ScoreTable : si le champ n'est pas assigné dans l'Inspector, on le retrouve par son
        // nom pour pouvoir le masquer pendant les phases de sélection d'artefact/trait.
        if (tableScoreObject == null)
        {
            var st = GameObject.Find("ScoreTable");
            if (st) tableScoreObject = st;
        }

        // Score du tour : mémorise sa couleur normale ET son contour d'origine (gris de la typo)
        // pour pouvoir les restaurer quand le score n'est plus en rouge.
        if (turnScoreText)
        {
            _turnScoreBaseColor = turnScoreText.color;
            var tsm = turnScoreText.fontMaterial; // material propre à ce texte (instancié)
            if (tsm)
            {
                _turnScoreBaseOutlineColor = tsm.GetColor(ShaderUtilities.ID_OutlineColor);
                _turnScoreBaseOutlineWidth = tsm.GetFloat(ShaderUtilities.ID_OutlineWidth);
            }
        }
        if (addScoreTurnText)
        {
            addScoreTurnText.outlineWidth = 0f; // jamais de contour sur le « +X »
            // Position verticale d'origine (celle que TU as choisie) : on la fige pour que le
            // « +X » ne redescende jamais sous le score. Seul son X sera ajusté ensuite.
            _addScoreBaseY = addScoreTurnText.rectTransform.anchoredPosition.y;
            // Empêche un éventuel Layout Group parent de le repositionner (cause du « retour dessous »).
            var le = addScoreTurnText.GetComponent<LayoutElement>();
            if (!le) le = addScoreTurnText.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            addScoreTurnText.gameObject.SetActive(false);
        }

        if (rollButton) rollButton.onClick.AddListener(OnPressRoll);
        if (bankButton) bankButton.onClick.AddListener(OnPressBank);
        if (endRoundButton) endRoundButton.onClick.AddListener(OnPressEndRound);

        // Panneau de palier : PalierCircle l'ouvre/ferme, il démarre FERMÉ.
        if (palierCircleButton)
        {
            palierCircleButton.onClick.AddListener(TogglePalierPanel);
            // Tutoriel : le clic du bouton de palier peut faire avancer une étape.
            palierCircleButton.onClick.AddListener(() => onActionPerformed?.Invoke(GameAction.PalierCircle));
        }
        if (palierPanelCloseButton)
        {
            palierPanelCloseButton.onClick.AddListener(ClosePalierPanel);
            // Tutoriel : le clic de fermeture du panneau de palier peut faire avancer une étape.
            palierPanelCloseButton.onClick.AddListener(() => onActionPerformed?.Invoke(GameAction.PalierClose));
        }
        // Le panneau riche (PalierPanelView) a son PROPRE bouton de fermeture : on y branche aussi l'événement.
        if (palierPanelView && palierPanelView.closeButton)
            palierPanelView.closeButton.onClick.AddListener(() => onActionPerformed?.Invoke(GameAction.PalierClose));
        if (palierPanel) palierPanel.SetActive(false);
        if (palierPanelView) palierPanelView.gameObject.SetActive(false);

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

        // ---- Dev Master Toggle : affiche/masque tous les objets "Dev*" ----
        if (devMasterToggle)
        {
            CacheDevObjects(); // recensés maintenant, avant tout masquage
            devMasterToggle.onValueChanged.AddListener(SetDevObjectsVisible);
            SetDevObjectsVisible(devMasterToggle.isOn); // applique l'état initial du Toggle
        }

        // Barre de temps du contre-jeu masquée par défaut
        if (counterPlayTimerBar) counterPlayTimerBar.gameObject.SetActive(false);



        // En tutoriel : on NE lance PAS de partie. Le plateau reste inerte jusqu'à ce qu'une
        // étape impose une phase via son "Setup Phase".
        if (tutorialMode) StartTutorialInert();
        else StartNewMatch();
    }

    // Démarrage neutre pour le tutoriel : aucun tirage au sort, aucune action possible.
    void StartTutorialInert()
    {
        CancelArtifactPickUI();
        if (palierPanelView) palierPanelView.Close();
        pendingForcedFaces.Clear();
        playerScore = aiScore = turnScore = 0;
        playerOpened = aiOpened = false;
        gameOver = false;
        awaitingNextMatch = false;
        awaitingArtifactPick = false;
        awaitingTraitPick = false;
        hintBanner?.Hide();
        ResetBannerState();
        ClearJournals();
        if (dice != null) foreach (var d in dice) if (d != null) d.ResetForNewTurn();
        HideDice(true);
        tutorialInert = true;
        UpdateUI();
        RefreshCampaignUI();
        ApplyButtonsState();
    }

    // ===================== CYCLE CAMPAGNE =====================
    void StartNewMatch()
    {
        // nettoyer tout résidu d’une éventuelle sélection d’artefact
        CancelArtifactPickUI();

        // Le Palier Panel ne reste pas affiché pendant une partie.
        if (palierPanelView) palierPanelView.Close();

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
        ClearJournals(); // nouveau match : les journaux repartent vides
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
        turnScoreAtRisk = false;
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
        traitTitheExtra = 0;
        traitTitheRollStreak = 0;
        aiHasRolledThisMatch = false;
        playerHasRolledThisMatch = false;
        aiTurnsThisMatch = 0;
        playerTurnsThisMatch = 0;
        estocUsedThisMatchPlayer = false;
        wellbornPlayerTurnsUsed = 0;   // Bien né : les 3 bonus de tour repartent à chaque match
        wellbornAITurnsUsed = 0;

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
        // Bien né : ne donne PLUS 35 points en banque + ouverture d'office (ce qui pouvait faire
        // « sauter » le tour). Le bonus est désormais appliqué au DÉBUT DE CHAQUE tour parmi les
        // 3 premiers, directement dans le SCORE DU TOUR (voir ApplyWellbornTurnBonus / StartNewTurn).
    }

    // Nombre de bonus « Bien né » déjà appliqués ce match, par camp (max 3).
    private int wellbornPlayerTurnsUsed = 0;
    private int wellbornAITurnsUsed = 0;
    private const int WELLBORN_TURN_BONUS = 35;
    private const int WELLBORN_MAX_TURNS = 3;

    // Applique le bonus « Bien né » du camp qui commence son tour : +35 dans le SCORE DU TOUR
    // (à risque, non versé en banque), pour ses 3 premiers tours seulement.
    void ApplyWellbornTurnBonus(Turn who)
    {
        bool has = (who == Turn.Player) ? PlayerHasTrait("wellborn") : EnemyTraitActive("wellborn");
        if (!has) return;

        if (who == Turn.Player)
        {
            if (wellbornPlayerTurnsUsed >= WELLBORN_MAX_TURNS) return;
            wellbornPlayerTurnsUsed++;
        }
        else
        {
            if (wellbornAITurnsUsed >= WELLBORN_MAX_TURNS) return;
            wellbornAITurnsUsed++;
        }

        turnScore += WELLBORN_TURN_BONUS;
        JournalCurrent($"Bien né : +{WELLBORN_TURN_BONUS} pts sur le tour");
        uiLog?.Append($"Bien né ({(who == Turn.Player ? "joueur" : "IA")}) : +{WELLBORN_TURN_BONUS} au score du tour.");
        MarkTraitActiveForTurn("wellborn", isPlayerSide: who == Turn.Player);
        UpdateUI();
    }

    void RefreshCampaignUI()
    {
        // Sécurités
        int palierCount = GetPalierCount();
        int enemiesThisPalier = GetEnemyCountInPalier(palierIndex);

        // ---------- Textes HUD ----------
        // Le palier affiche aussi le score à atteindre pour le remporter (ex : "Palier 1/5 (300 pts)").
        if (palierHudText)
            palierHudText.text = $"Palier {Mathf.Clamp(palierIndex + 1, 1, Mathf.Max(1, palierCount))}/{Mathf.Max(1, palierCount)}"
                               + $" ({GetWinThresholdForPalier(palierIndex)} pts)";

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

        // ---------- Panneau de Palier + vies ----------
        RefreshPalierPanel();
        RefreshLivesUI();

        // ---------- Map ----------
        if (mapView)
        {
            var ei = PickEnemy(palierIndex, enemyIndex);
            mapView.ShowEnemy(ei);
            UpdateOpponentPortrait(ei); // <-- portrait central
        }
    }

    // Chiffres romains (I, II, III, IV, V…) pour le numéro de palier.
    public static string ToRoman(int value)
    {
        if (value <= 0) return "";
        int[] nums    = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        string[] syms = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < nums.Length && value > 0; i++)
            while (value >= nums[i]) { sb.Append(syms[i]); value -= nums[i]; }
        return sb.ToString();
    }

    // Garde la liste des résultats alignée sur le nombre d'adversaires du palier courant.
    void EnsurePalierResultsSize()
    {
        int n = Mathf.Max(0, GetEnemyCountInPalier(palierIndex));
        while (palierEnemyResults.Count < n) palierEnemyResults.Add(0);
        while (palierEnemyResults.Count > n) palierEnemyResults.RemoveAt(palierEnemyResults.Count - 1);
    }

    // Enregistre l'issue du match qui vient de se terminer pour l'adversaire courant.
    void RecordPalierEnemyResult(bool won)
    {
        EnsurePalierResultsSize();
        if (enemyIndex >= 0 && enemyIndex < palierEnemyResults.Count)
            palierEnemyResults[enemyIndex] = won ? 1 : -1;

        // Historique sur toute la partie (pour le Palier Panel).
        while (palierResultsHistory.Count <= palierIndex) palierResultsHistory.Add(new List<int>());
        var row = palierResultsHistory[palierIndex];
        int ec = Mathf.Max(0, GetEnemyCountInPalier(palierIndex));
        while (row.Count < ec) row.Add(0);
        if (enemyIndex >= 0 && enemyIndex < row.Count) row[enemyIndex] = won ? 1 : -1;
    }

    // Construit l'instantané de progression pour le Palier Panel.
    PalierPanelView.Snapshot BuildPalierSnapshot()
    {
        int count = Mathf.Max(1, GetPalierCount());
        int segCount = Mathf.Max(0, count - 1);
        var snap = new PalierPanelView.Snapshot
        {
            palierCount = count,
            currentPalier = palierIndex,
            matchInProgress = !matchOver && !gameOver && !isGameOverScreen,
            winScores = new int[count],
            enemyCounts = new int[count],
            enemyResults = new int[count][],
            segmentFill = new float[segCount],
            segmentTraitSprites = new Sprite[segCount][],
            segmentTraitNames = new string[segCount][],
            segmentTraitDescs = new string[segCount][],
        };

        for (int p = 0; p < count; p++)
        {
            snap.winScores[p] = GetWinThresholdForPalier(p);
            int ec = Mathf.Max(0, GetEnemyCountInPalier(p));
            snap.enemyCounts[p] = ec;
            var res = new int[ec];
            var hist = (p < palierResultsHistory.Count) ? palierResultsHistory[p] : null;
            for (int e = 0; e < ec; e++) res[e] = (hist != null && e < hist.Count) ? hist[e] : 0;
            snap.enemyResults[p] = res;
        }

        for (int i = 0; i < segCount; i++)
        {
            // La ligne du segment i est pleine dès que le palier i est franchi.
            snap.segmentFill[i] = (palierIndex > i) ? 1f : 0f;
            if (segmentTraits.TryGetValue(i, out var list) && list != null && list.Count > 0)
            {
                int n = list.Count;
                snap.segmentTraitSprites[i] = new Sprite[n];
                snap.segmentTraitNames[i] = new string[n];
                snap.segmentTraitDescs[i] = new string[n];
                for (int t = 0; t < n; t++)
                {
                    snap.segmentTraitSprites[i][t] = GetTraitIcon(list[t]);
                    snap.segmentTraitNames[i][t] = list[t].name;
                    snap.segmentTraitDescs[i][t] = list[t].desc;
                }
            }
        }
        return snap;
    }

    // Numéro romain + état de chaque adversaire du palier (à venir / en cours / gagné / perdu).
    void RefreshPalierPanel()
    {
        if (palierNumberText) palierNumberText.text = ToRoman(palierIndex + 1);

        if (palierEnemyIcons == null || palierEnemyIcons.Count == 0) return;

        EnsurePalierResultsSize();
        int enemiesThisPalier = GetEnemyCountInPalier(palierIndex);

        for (int i = 0; i < palierEnemyIcons.Count; i++)
        {
            var img = palierEnemyIcons[i];
            if (!img) continue;

            // Emplacement au-delà du nombre d'adversaires du palier : on le masque.
            if (i >= enemiesThisPalier)
            {
                img.gameObject.SetActive(false);
                continue;
            }
            img.gameObject.SetActive(true);

            int result = (i < palierEnemyResults.Count) ? palierEnemyResults[i] : 0;
            bool isCurrent = (i == enemyIndex) && !matchOver;

            // Battu → icône victoire ; perdu → icône défaite ; sinon icône « en jeu ».
            Sprite s = result > 0 ? enemyIconWin
                     : result < 0 ? enemyIconLoose
                     : enemyIconInGame;
            if (s) img.sprite = s;
            img.color = Color.white;

            // Contour ROUGE uniquement sur l'adversaire de la partie en cours.
            SetIconOutline(img, isCurrent, GameColors.Points, currentEnemyOutlineWidth);
        }
    }

    // Ajoute/retire un contour coloré sur une Image (composant Outline créé à la demande).
    static void SetIconOutline(Image img, bool on, Color color, float width)
    {
        if (!img) return;
        var outline = img.GetComponent<Outline>();
        if (outline == null)
        {
            if (!on) return;            // rien à faire, on évite d'ajouter un composant inutile
            outline = img.gameObject.AddComponent<Outline>();
        }
        outline.effectColor = color;
        outline.effectDistance = new Vector2(width, width);
        outline.enabled = on;
    }

    // Une vie grisée par défaite, en partant de la DROITE.
    void RefreshLivesUI()
    {
        if (lifeIcons == null || lifeIcons.Count == 0) return;

        int lost = Mathf.Clamp(defeatsCount, 0, lifeIcons.Count);
        for (int i = 0; i < lifeIcons.Count; i++)
        {
            var img = lifeIcons[i];
            if (!img) continue;
            bool isLost = i >= (lifeIcons.Count - lost);
            img.color = isLost ? lifeLostTint : Color.white;
        }
    }

    // Ouvre/ferme le panneau de palier (branché sur PalierCircle).
    public void TogglePalierPanel()
    {
        // Panneau riche (PalierPanelView) prioritaire s'il est assigné.
        if (palierPanelView)
        {
            bool open = !palierPanelView.gameObject.activeSelf;
            if (open)
            {
                palierPanelView.Render(BuildPalierSnapshot(), animate: false); // ouverture manuelle : instantané
                palierPanelView.transform.SetAsLastSibling(); // passe AU-DESSUS du bouton de palier
                KeepHintBannerOnTop();
            }
            else palierPanelView.Close();
            return;
        }

        if (!palierPanel) return;
        bool willOpen = !palierPanel.activeSelf;
        if (willOpen) RefreshCampaignUI(); // contenu à jour à l'ouverture
        palierPanel.SetActive(willOpen);
        if (willOpen) { palierPanel.transform.SetAsLastSibling(); KeepHintBannerOnTop(); }
    }

    // Le bandeau d'indices partage le parent (Canvas) des panneaux qu'on remonte au premier
    // plan via SetAsLastSibling (ex : PalierPanel). Sans ça, le panneau passait DEVANT le
    // bandeau. On repasse donc le bandeau en dernier pour qu'il reste visible au-dessus.
    void KeepHintBannerOnTop()
    {
        if (hintBanner) hintBanner.transform.SetAsLastSibling();
    }

    public void ClosePalierPanel()
    {
        if (palierPanelView) palierPanelView.Close();
        if (palierPanel) palierPanel.SetActive(false);
    }

    // Affiche le Palier Panel pendant la phase de récompense (choix de trait PUIS d'artefact) :
    // il s'anime la première fois, puis reste affiché sans se réanimer entre trait et artefact.
    void ShowPalierPanelForReward()
    {
        // En tutoriel, le panneau de palier est piloté explicitement (étape PalierCircle) :
        // il ne doit PAS se rouvrir tout seul pendant un choix d'artefact/trait.
        if (tutorialMode) return;
        if (!palierPanelView) return;
        bool wasActive = palierPanelView.gameObject.activeSelf;
        palierPanelView.Render(BuildPalierSnapshot(), animate: !wasActive);
        palierPanelView.transform.SetAsLastSibling(); // au-dessus du bouton de palier
        KeepHintBannerOnTop();
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
        // Chemin boisé : ce match perdu est concédé, on n'enregistre AUCUNE défaite (pas de vie perdue).
        if (isDefeat && concedeNoLifeLoss)
        {
            concedeNoLifeLoss = false;
            uiLog?.Append("Chemin boisé : combat concédé — aucune vie perdue.");
            isDefeat = false; // on saute tout le bloc de comptage/défaite ci-dessous
        }
        if (isDefeat)
        {
            defeatsCount++;
            uiLog?.Append($"Défaite enregistrée ({defeatsCount}/3).");

            // Fin de campagne : 3 défaites OU défaite contre le Boss (dernier palier).
            bool lostToBoss = palierIndex >= GetPalierCount() - 1;
            if (defeatsCount >= 3 || lostToBoss)
            {
                // Trait Résurrection : lancer le dé SUN pour revenir dans la partie
                if (PlayerHasTrait("player.resurrection") && !resurrectionUsed)
                {
                    StartCoroutine(ResolveResurrection());
                    return;
                }

                ShowGameOver(lostToBoss ? "Vaincu par le Boss — Campagne perdue"
                                        : "3 défaites — Campagne perdue");
                return;
            }
        }

        bool crossedPalier = false;
        enemyIndex++;
        int enemiesInThisPalier = GetEnemyCountInPalier(palierIndex);
        if (enemyIndex >= enemiesInThisPalier)
        {
            // Nouveau palier : la liste d'états des adversaires repart vierge.
            palierEnemyResults.Clear();
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
            // On annule la défaite qui vient d'être enregistrée et on rejoue le match perdu.
            // (Utilise un décrément : une défaite contre le Boss peut survenir à 1 seule défaite.)
            defeatsCount = Mathf.Max(0, defeatsCount - 1);
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

        if (gameOverPanel)
        {
            gameOverPanel.SetActive(true);
            // Le panneau Game Over doit couvrir TOUT — y compris le PalierPanel/HintBanner qui
            // se remontent via SetAsLastSibling. On le repasse donc en dernier (même parent).
            gameOverPanel.transform.SetAsLastSibling();
        }
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
        palierEnemyResults.Clear(); // panneau de palier : états des adversaires remis à zéro
        palierResultsHistory.Clear();
        segmentTraits.Clear();
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
        HideAIBankModifierLabel(); // efface l'éventuel affichage Bourse percée (bank IA) du tour précédent
        ClearAllTraitActiveOutlines(); // les contours « trait actif » ne survivent pas au changement de tour

        currentTurn = who;

        // Nouveau tour de ce camp : son journal repart vide et se remplira au fil des actions.
        ResetJournalFor(who);
        HideAddScoreFx(); // le « +X » d'un tour précédent ne traîne pas sur le nouveau tour

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
        aiPreBankCounterOpen = false;
        aiBankMultiplier = 1f;
        aiBankModLabel = "Bourse percée";
        aiFlashIndices.Clear();
        aiFlashCancelled = false;
        aiDiceChangedByCounter = false;
        aiNoRerollIndices.Clear();

        turnScore = 0;
        flashPendingResolution = false;
        clauseClearedBySelection = false;
        wimpoutLostTurnScore = 0;
        turnScoreAtRisk = false;
        pendingWinCheckOnContinue = false;
        lastFlashValid = false; // pas de sauvetage de Clause hérité d'un tour précédent

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

        // Trait Bien né : +35 dans le SCORE DU TOUR au début de chacun des 3 premiers tours
        // du camp concerné (après le reset de turnScore, avant que le tour ne commence).
        ApplyWellbornTurnBonus(who);

        SetPhase(currentTurn == Turn.Player ? Phase.AwaitFirstRoll : Phase.AITurnPlaying);
        UpdateUI();

        // 👇 Force l’aperçu à se mettre à jour (masquera si IA)
        RefreshScoreModRuntimePreview();

        if (currentTurn == Turn.AI)
        {
            // En tutoriel, l'IA ne joue QUE si une Setup Phase l'a explicitement demandé.
            bool aiMayPlay = !tutorialMode || tutorialAIAuthorized;
            tutorialAIAuthorized = false; // consommé (autorisation à usage unique)
            if (aiMayPlay) StartCoroutine(RunAITurn());
        }
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

        // Le « +X » ne survit pas à la fin d'un tour (banque, wimpout, fin de tour IA).
        if (p == Phase.WaitEnd || p == Phase.AITurnWaitEnd) HideAddScoreFx();

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

        // Objectif affiché : seuil d'ouverture tant que le camp n'est pas ouvert.
        int playerGoalShown = playerOpened ? playerGoal : ENTRY_THRESHOLD;
        int aiGoalShown     = aiOpened     ? goal       : ENTRY_THRESHOLD;

        // Si un champ « objectif » est branché, on scinde : score d'un côté, objectif de l'autre.
        // Sinon on garde l'ancien affichage combiné (rien ne casse si un seul côté est câblé).
        if (playerScoreText)
            playerScoreText.text = playerScoreGoalText
                ? playerScore.ToString()
                : $"{playerScore} / {playerGoalShown} pts";
        if (playerScoreGoalText)
            playerScoreGoalText.text = playerGoalShown.ToString();

        if (aiScoreText)
            aiScoreText.text = aiScoreGoalText
                ? aiScore.ToString()
                : $"{aiScore} / {aiGoalShown} pts";
        if (aiScoreGoalText)
            aiScoreGoalText.text = aiGoalShown.ToString();

        // Score du tour : le libellé est porté par l'UI, on n'affiche que le nombre.
        // Mise en valeur ROUGE + contour blanc UNIQUEMENT tant que le joueur n'a pas encore
        // ouvert : c'est le repère « tu atteins les 35 pts pour ouvrir ». Une fois ouvert
        // (35 pts bankés), plus besoin de la couleur pour le reste de la partie.
        if (turnScoreText)
        {
            turnScoreText.text = turnScore.ToString();

            // Mise en valeur du seuil d'ouverture : côté JOUEUR (tant que non ouvert) ET côté IA
            // (tant qu'elle n'a pas ouvert) — le score du tour affiché est celui du camp qui joue.
            bool notOpened = (currentTurn == Turn.Player) ? !playerOpened : !aiOpened;
            bool highlightEntry = notOpened && turnScore >= ENTRY_THRESHOLD;
            turnScoreText.color = highlightEntry ? GameColors.Points : _turnScoreBaseColor;

            // Contour via le MATERIAL INSTANCE (fontMaterial) + ShaderUtilities : les propriétés
            // outlineWidth/outlineColor de TMP peuvent toucher le material PARTAGÉ et « rester
            // coincées » d'un tour à l'autre. On force width=0 à l'instance quand ce n'est plus
            // le cas, ce qui retire vraiment le contour.
            var tsMat = turnScoreText.fontMaterial; // instancie un material propre à ce texte
            if (tsMat)
            {
                // Rouge → contour blanc épais ; sinon → contour d'origine de la typo (gris).
                tsMat.SetColor(ShaderUtilities.ID_OutlineColor, highlightEntry ? Color.white : _turnScoreBaseOutlineColor);
                tsMat.SetFloat(ShaderUtilities.ID_OutlineWidth, highlightEntry ? turnScoreReachedOutlineWidth : _turnScoreBaseOutlineWidth);
            }
        }

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


    // Déverrouillage d'urgence posé par le filet anti-blocage (voir EnsurePlayerHasAnAction).
    private bool softlockRollOverride = false;

    // Vrai si le joueur peut relancer en phase Normal.
    // Miroir exact de la garde dans OnPressRoll : on doit avoir sélectionné un dé marquant
    // (sauf hot-dice où tous les dés scorent et la relance complète est imposée).
    bool CanPlayerRollInNormal()
    {
        if (dice == null || dice.Count == 0) return false;

        if (softlockRollOverride) return true;

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

    // Filet anti-blocage : si, au tour du joueur, ni ROLL ni BANK ni NEXT ne sont actifs
    // ET qu'aucun dé n'est sélectionnable, la partie est figée (aucune action possible).
    // On répare alors l'état (Flash fantôme) et on rouvre ROLL plutôt que de bloquer le jeu.
    void EnsurePlayerHasAnAction()
    {
        if (currentTurn != Turn.Player) return;
        if (gameOver || matchOver || isGameOverScreen) return;
        if (awaitingArtifactPick || awaitingTraitPick || awaitingCounterPlay) return;
        if (phase != Phase.Normal && phase != Phase.Clause) return;
        if (onExternalDiePicked != null || extPickActive) return; // un artefact attend une cible

        bool anyButton = (rollButton && rollButton.interactable)
                      || (bankButton && bankButton.interactable)
                      || (endRoundButton && endRoundButton.interactable);
        if (anyButton) return;

        if (eligibleLockIndices.Any(IsDieSelectableNow)) return; // le joueur peut encore cliquer un dé

        if (flashPendingResolution) { ClearClauseState(); clauseClearedBySelection = false; }
        softlockRollOverride = true;
        if (rollButton) rollButton.interactable = true;

        uiLog?.Append("Anti-blocage : plus aucune action possible — ROLL réactivé.");
        ShowAction("Aucun dé sélectionnable — appuie sur ROLL pour relancer.");
    }

    void ApplyButtonsState()
    {
        // Contour « actif » du trait Happy Hour (ennemi) : il agit dès que le joueur dépasse
        // 75 pts sur le tour (bank obligatoire). Une fois déclenché, on le VERROUILLE pour le
        // reste du tour (il ne s'éteint pas si le score varie) ; effacé au prochain StartNewTurn.
        if (currentTurn == Turn.Player && EnemyTraitActive("happyhour") && turnScore > 75)
            MarkTraitActiveForTurn("happyhour", isPlayerSide: false);

        // Tutoriel inerte : aucun bouton actionnable tant qu'aucune phase n'est mise en scène.
        if (tutorialInert)
        {
            if (rollButton) rollButton.interactable = false;
            if (bankButton) bankButton.interactable = false;
            if (endRoundButton) endRoundButton.interactable = false;
            return;
        }

        PurgeStaleLockSets();

        // Recalcul complet : le déverrouillage d'urgence est réévalué à chaque passage
        // (il disparaît dès que le joueur retrouve une action normale).
        softlockRollOverride = false;

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
                        bool canBank = !flashPendingResolution && selectionOk && !turnScoreAtRisk &&
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

        EnsurePlayerHasAnAction();
    }

    // ===== Extern: ciblage de dé pour artefacts =====
    private bool extPickActive = false;
    private System.Func<int, bool> extPickFilter = null;
    private System.Action<int> extOnPicked = null;
    void CancelExternalDiePick()
    {
        extPickActive = false; extPickFilter = null; extOnPicked = null;
        ClearDicePickHover();
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
                die.Bounce(); // effet de rebond sur le dé sélectionné
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
            JournalPlayerUnselectDie(idx); // action annulée : on retire sa ligne du journal
            HideAddScoreFx();              // et le « +X » disparaît aussitôt
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
                RemovePlayerClauseClearedLine(); // la Clause n'est plus réussie : on retire sa ligne
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
            JournalPlayerSelectDie(idx, die.GetFace(), add);
            ShowAddScoreFx(add); // « +X » éphémère à côté du score du tour
            UpdateUI();
            ApplyButtonsState();

            if (phase == Phase.Clause && flashPendingResolution && mutableLocks.Count > 0)
            {
                flashPendingResolution = false;
                clauseClearedBySelection = true; // annulable tant que la sélection n'est pas figée (ROLL/BANK)

                // Contour VERT sur le dé qui vient de dégager la Clause (couleur du journal).
                die.SetClauseSelected(true);

                // Journal : Clause réussie (le Flash est dégagé) + le dé qui l'a dégagée.
                // Reste annulable si le joueur ré-arme la Clause en désélectionnant.
                playerClauseClearedLine = $"Clause réussie — Flash {DieTag(currentFlashFace)} dégagé avec un {DieTag(die.GetFace())}";
                JournalPlayer(playerClauseClearedLine);

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

    void Update()
    {
        // Tutoriel : masque les actions interdites (ne fait que désactiver, jamais activer).
        ApplyTutorialButtonGate();

        // Raccourcis clavier des boutons d'action (touches configurables dans les réglages).
        if (IsTypingInInputField()) return;
        TryActionShortcut(rollButton, GameAction.Roll);
        TryActionShortcut(bankButton, GameAction.Bank);
        TryActionShortcut(endRoundButton, GameAction.EndRound);
    }

    // Désactive chaque bouton dont l'action est interdite, puis force-active ceux demandés.
    void ApplyTutorialButtonGate()
    {
        if (tutorialActionFilter != null)
        {
            if (rollButton && rollButton.interactable && !tutorialActionFilter(GameAction.Roll)) rollButton.interactable = false;
            if (bankButton && bankButton.interactable && !tutorialActionFilter(GameAction.Bank)) bankButton.interactable = false;
            if (endRoundButton && endRoundButton.interactable && !tutorialActionFilter(GameAction.EndRound)) endRoundButton.interactable = false;
        }
        if (tutorialForceEnable != null)
        {
            if (rollButton && tutorialForceEnable(GameAction.Roll)) rollButton.interactable = true;
            if (bankButton && tutorialForceEnable(GameAction.Bank)) bankButton.interactable = true;
            if (endRoundButton && tutorialForceEnable(GameAction.EndRound)) endRoundButton.interactable = true;
        }
    }

    // Déclenche le clic du bouton si sa touche est enfoncée et qu'il est réellement utilisable.
    void TryActionShortcut(Button b, GameAction a)
    {
        if (b && b.interactable && b.gameObject.activeInHierarchy && KeyBindings.WasPressedThisFrame(a))
            b.onClick.Invoke();
    }

    // Ignore les raccourcis pendant la saisie dans un champ de texte (outils DEV).
    bool IsTypingInInputField()
    {
        var es = EventSystem.current;
        var sel = es ? es.currentSelectedGameObject : null;
        if (!sel) return false;
        return sel.GetComponent<TMP_InputField>() != null || sel.GetComponent<InputField>() != null;
    }

    void OnPressRoll()
    {
        if (!IsActionAllowedByTutorial(GameAction.Roll)) return;
        onActionPerformed?.Invoke(GameAction.Roll);
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

        // Tutoriel : faces forcées (forceNextRoll) → affichées DIRECTEMENT, sans jet aléatoire
        // préalable. Ordre : face 1 = dé du joueur, face 2 = dé de l'IA.
        bool pForced = tutorialNextRollFaces.Count >= 1;
        bool aForced = tutorialNextRollFaces.Count >= 2;

        if (pForced) playerDie.SetFace(tutorialNextRollFaces[0]);
        else playerDie.Roll();
        yield return new WaitForSeconds(0.45f);

        if (aForced) aiDie.SetFace(tutorialNextRollFaces[1]);
        else aiDie.Roll();
        yield return new WaitForSeconds(0.6f);

        tutorialNextRollFaces.Clear();

        // Sinon, forçage optionnel du vainqueur (Tutorial_PlayerWinsRollOff / AIWinsRollOff).
        if (!pForced && !aForced && tutorialRollOffPlayerWins.HasValue)
        {
            playerDie.SetFace(tutorialRollOffPlayerWins.Value ? DieFace.Ten : DieFace.Two);
            aiDie.SetFace(tutorialRollOffPlayerWins.Value ? DieFace.Two : DieFace.Ten);
        }

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
        if (!IsActionAllowedByTutorial(GameAction.Bank)) return;
        onActionPerformed?.Invoke(GameAction.Bank);
        // Pendant la sélection d’artefact ou de trait, le choix se fait en cliquant les cartes
        if (awaitingArtifactPick || awaitingTraitPick) return;

        if (gameOver || matchOver || isGameOverScreen) return;
        if (currentTurn != Turn.Player || phase != Phase.Normal) return;
        if (turnScore <= 0) return;

        if (flashPendingResolution)
        { ShowAction("Un Flash doit être dégagé avant de banquer."); return; }

        // Score « en péril » (tour perdu, sauvable par artefact) : impossible de banquer.
        if (turnScoreAtRisk)
        { ShowAction("Ce tour est perdu : sauvez-le avec un artefact, sinon CONTINUE."); return; }

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

        CommitPlayerBankAndEndTurn();
    }

    // Crédite le score du tour au total du joueur (mods + traits) puis termine le tour.
    // Extrait de OnPressBank pour être réutilisé par le Cercueil maudit (bank forcée).
    void CommitPlayerBankAndEndTurn()
    {
        foreach (var idx in mutableLocks) frozenLocks.Add(idx);
        mutableLocks.Clear();
        mutablePoints.Clear();
        FreezePlayerSelectionJournal(); // sélections figées par la banque
        eligibleLockIndices.Clear();
        eligibleLockPoints.Clear();
        clauseClearedBySelection = false; // sélection figée par la banque
        wimpoutLostTurnScore = 0;
        turnScoreAtRisk = false;

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
            MarkTraitActiveForTurn("boss.obole", isPlayerSide: false);
        }

        // ==== Crédit du tour au score total du joueur ====
        playerScore += adjusted;
        turnScore = 0;
        JournalPlayer($"Banque {adjusted} pts (total {playerScore} pts)");
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
            MarkTraitActiveForTurn("poisonglass", isPlayerSide: false);
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

        // Sinon : BANK enchaîne directement sur le tour suivant (plus besoin d'appuyer sur Next).
        // Si la banque a clos le match / la partie, on reste en WaitEnd pour laisser
        // le joueur appuyer sur CONTINUE (artefacts, progression de campagne).
        if (gameOver || matchOver || isGameOverScreen) { SetPhase(Phase.WaitEnd); return; }

        ClearScoreModifier();
        StartNewTurn(Turn.AI);
    }




    void OnPressEndRound()
    {
        if (!IsActionAllowedByTutorial(GameAction.EndRound)) return;
        onActionPerformed?.Invoke(GameAction.EndRound);
        // Confirmation du Palier Panel après un choix de trait : Next enchaîne sur la suite.
        // La destination gère l'affichage : match suivant (StartNewMatch ferme le panneau) ou
        // sélection d'artefact (le panneau reste). Pas de fermeture explicite ici → pas de clignotement.
        if (awaitingPalierPanelNext)
        {
            awaitingPalierPanelNext = false;
            if (endRoundButton) endRoundButton.interactable = false;

            // On restaure les dés + le score du tour (masqués pendant le choix de trait), comme
            // le faisait ExitTraitPick. Si la suite est une sélection d'artefact, elle les
            // remasquera d'elle-même ; sinon (match suivant) l'objet Score réapparaît bien.
            HideDice(false);
            SetTurnScoreVisible(true);

            var cb = palierPanelNextCallback;
            palierPanelNextCallback = null;
            if (cb != null) cb.Invoke();
            else if (palierPanelView) palierPanelView.Close();
            return;
        }

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

    // ===================== FULL (5 dés identiques) =====================
    private enum FullKind { None, Points, WinInstant, Supernova }

    // Classe un jet de 5 dés identiques.
    // 6 → victoire instantanée · 10/SUN (si Supernova activée) → élimination ·
    // 2/3/4/5 (et 10 si Supernova désactivée) → 100 × valeur faciale.
    FullKind ClassifyFull(List<int> rolled, out DieFace face, out int points)
    {
        face = DieFace.Two; points = 0;
        if (rolled == null || rolled.Count != 5) return FullKind.None;

        var f0 = dice[rolled[0]].GetFace();
        for (int k = 1; k < 5; k++)
            if (dice[rolled[k]] == null || dice[rolled[k]].GetFace() != f0) return FullKind.None;

        face = f0;
        if (f0 == DieFace.Six) return FullKind.WinInstant;
        if ((f0 == DieFace.Ten || f0 == DieFace.Sun) && ENABLE_SUPERNOVA) return FullKind.Supernova;

        int faceValue = (f0 == DieFace.Sun) ? 10 : (int)f0;
        points = 100 * faceValue; // 2→200, 3→300, 4→400, 5→500, 10→1000
        return FullKind.Points;
    }

    // Résout un éventuel Full. Retourne true si un Full a été traité (le caller doit yield break).
    bool HandleFullIfAny(List<int> rolled, bool isAI)
    {
        var kind = ClassifyFull(rolled, out var face, out int pts);
        if (kind == FullKind.None) return false;

        string rollerName = (currentTurn == Turn.Player) ? PLAYER_NAME : AI_NAME;
        string opponentName = (currentTurn == Turn.Player) ? AI_NAME : PLAYER_NAME;

        if (kind == FullKind.WinInstant)
        {
            JournalCurrent("FULL de 6 — victoire instantanée !");
            ShowAction($"FULL de 6 ! Victoire instantanée de {rollerName} !");
            uiLog?.Append($"FULL de 6 — {rollerName} gagne le match.");
            EndMatch(rollerName);
            return true;
        }

        if (kind == FullKind.Supernova)
        {
            JournalCurrent($"SUPERNOVA (Full de {DieTag(face)}) — éliminé !");
            ShowAction($"SUPERNOVA (Full de {(face == DieFace.Sun ? "SUN" : ((int)face).ToString())}) ! Score trop élevé — {rollerName} est éliminé !");
            uiLog?.Append($"SUPERNOVA — {rollerName} éliminé (l'adversaire gagne).");
            EndMatch(opponentName);
            return true;
        }

        JournalCurrent($"FULL de {DieTag(face)} (+{pts} pts) — relance des 5 dés");

        // FULL de points : crédite 100 × la face, puis relance OBLIGATOIRE des 5 dés (hot dice).
        turnScore += pts;
        foreach (var i in rolled)
            if (i >= 0 && i < dice.Count && dice[i] != null) { dice[i].SetLocked(true); frozenLocks.Add(i); }

        ClearClauseState();
        flashVisualIndices.Clear();
        eligibleLockIndices.Clear();
        eligibleLockPoints.Clear();
        UpdateUI();

        ShowAction($"FULL de {(int)face} ! +{pts} points — les 5 dés sont chauds : relance OBLIGATOIRE.");
        uiLog?.Append($"FULL de {(int)face} : +{pts} points (relance des 5 imposée).");
        SetPhase(isAI ? Phase.AITurnPlaying : Phase.Normal);
        return true;
    }

    // ===================== ROLL / CLAUSE / IA =====================
    IEnumerator ResolvePlayerRoll()
    {
        bool isReroll = (phase == Phase.Normal); // relance (≠ premier jet du tour)

        foreach (var idx in mutableLocks) frozenLocks.Add(idx);
        mutableLocks.Clear();
        mutablePoints.Clear();
        FreezePlayerSelectionJournal(); // les sélections deviennent des actions définitives
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

        // Un dé relancé n'appartient plus à l'ancien Flash : on retire son contour orange.
        foreach (var idx in indicesToRoll) flashVisualIndices.Remove(idx);

        // Tuto : les faces forcées se mappent DANS L'ORDRE aux dés relancés (non-verrouillés).
        // Sinon dev/artefacts (pendingForcedFaces), sinon jet aléatoire — affichage direct dans tous les cas.
        int tf = 0;
        foreach (var idx in indicesToRoll)
        {
            if (tf < tutorialNextRollFaces.Count)
                dice[idx].SetFace(tutorialNextRollFaces[tf++]);
            else if (pendingForcedFaces.TryGetValue(dice[idx], out var forcedFace))
                dice[idx].SetFace(forcedFace);
            else
                dice[idx].Roll();
        }
        tutorialNextRollFaces.Clear();
        lastRolledIndices.Clear();
        lastRolledIndices.AddRange(indicesToRoll);
        yield return null;

        ApplyPendingFaceOverrides(lastRolledIndices); // nettoie/confirme les faces forcées

        // ---- TRAIT Pistage (joueur) : le premier jet du match a 2 dés à 10 ----
        if (!playerHasRolledThisMatch && PlayerHasTrait("tracking") && lastRolledIndices.Count >= 2)
        {
            dice[lastRolledIndices[0]].SetFace(DieFace.Ten);
            dice[lastRolledIndices[1]].SetFace(DieFace.Ten);
            ShowAction("Pistage : votre premier jet contient 2 dés de 10 !");
            MarkTraitActiveForTurn("tracking", isPlayerSide: true);
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
            MarkTraitActiveForTurn("runes", isPlayerSide: true);
        }

        // ---- TRAIT Boss « Marée montante » : chaque relance du joueur profite au Boss ----
        if (isReroll && EnemyTraitActive("boss.tide"))
        {
            aiScore += TIDE_POINTS_PER_REROLL;
            ShowAction($"Marée montante : votre relance fait monter les eaux — +{TIDE_POINTS_PER_REROLL} pts pour le Boss !");
            uiLog?.Append($"Trait Boss Marée montante : +{TIDE_POINTS_PER_REROLL} au Boss (relance du joueur).");
            MarkTraitActiveForTurn("boss.tide", isPlayerSide: false);
            UpdateUI();
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
                MarkTraitActiveForTurn("boss.tithe", isPlayerSide: false);
            }
        }

        // FULL (5 dés identiques) : victoire (6), supernova (10) ou 100 × la face.
        if (HandleFullIfAny(lastRolledIndices, isAI: false)) yield break;

        var eval = EvaluateRoll(lastRolledIndices, scoreSingles: false);

        if (eval.createdFlash)
        {
            foreach (var i in eval.flashLockIndices)
            {
                dice[i].SetLocked(true);
                frozenLocks.Add(i);
                flashLockIndices.Add(i);
                flashVisualIndices.Add(i); // surlignage orange persistant
            }
            turnScore += eval.pointsGained;
            ShowAddScoreFx(eval.pointsGained); // valeur du FLASH sur le « +X »
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
                MarkTraitActiveForTurn("boss.blacksun", isPlayerSide: false);
            }

            JournalPlayer($"FLASH {DieTag(currentFlashFace)} (+{eval.pointsGained} pts) — doit Clause");

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
                MarkTraitActiveForTurn("noose", isPlayerSide: true);
                yield return new WaitForSeconds(0.6f);
                int pick = lastRolledIndices[aiRng.Next(lastRolledIndices.Count)];
                dice[pick].Roll();
                dice[pick].Pulse(0.22f, 1.12f);
                yield return new WaitForSeconds(0.3f);
                FillEligibleFromIndices(lastRolledIndices);
            }

            if (eligibleLockIndices.Count == 0)
            {
                JournalPlayer("Lancé échoué, le joueur a raté ce tour.");
                MarkPlayerTurnScoreAtRisk(); // récupérable via un artefact de sauvetage

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

        // Un dé relancé n'appartient plus à l'ancien Flash : on retire son contour orange.
        foreach (var idx in indicesToRoll) flashVisualIndices.Remove(idx);

        // Tuto : faces forcées (dans l'ordre) sur les dés relancés de la Clause, sinon aléatoire.
        int tf = 0;
        foreach (var idx in indicesToRoll)
        {
            if (tf < tutorialNextRollFaces.Count) dice[idx].SetFace(tutorialNextRollFaces[tf++]);
            else dice[idx].Roll();
        }
        if (tf > 0) tutorialNextRollFaces.Clear();
        lastRolledIndices.Clear();
        lastRolledIndices.AddRange(indicesToRoll);
        yield return null;

        ApplyPendingFaceOverrides(lastRolledIndices); // faces forcées (dev / artefacts)

        // FULL (5 dés identiques) même pendant la Clause.
        if (HandleFullIfAny(lastRolledIndices, isAI: false)) yield break;

        var eval = EvaluateRoll(lastRolledIndices, scoreSingles: false);

        if (eval.createdFlash)
        {
            foreach (var i in eval.flashLockIndices)
            {
                dice[i].SetLocked(true);
                frozenLocks.Add(i);
                flashLockIndices.Add(i);
                flashVisualIndices.Add(i); // surlignage orange persistant
            }
            turnScore += eval.pointsGained;
            ShowAddScoreFx(eval.pointsGained); // valeur du FLASH sur le « +X »
            UpdateUI();

            currentFlashFace = eval.flashFace;
            flashPendingResolution = true;

            JournalPlayer($"Nouveau FLASH {DieTag(currentFlashFace)} (+{eval.pointsGained} pts) — doit Clause");

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
        {
            JournalPlayer($"Clause : la face {DieTag(currentFlashFace)} retombe — relance");
            ShowAction($"Clause : {(int)currentFlashFace} retombe — appuie sur ROLL pour continuer.");
            SetPhase(Phase.Clause);
            yield break;
        }

        // ---- TRAIT Coup d'estoc (joueur) : la Clause perdue offre une relance exceptionnelle
        //      (1×/match — le trait se réarme au prochain adversaire) ----
        if (PlayerHasTrait("estoc") && !estocUsedThisMatchPlayer)
        {
            estocUsedThisMatchPlayer = true;
            RebuildTraitIcons(); // marque le trait « (utilisé) » dans l'AddBox
            ShowAction("Coup d'estoc : le dé perdant vous accorde une relance — appuie sur ROLL !");
            uiLog?.Append("Trait Coup d'estoc (joueur) : relance exceptionnelle de la Clause.");
            MarkTraitActiveForTurn("estoc", isPlayerSide: true);
            SetPhase(Phase.Clause);
            yield break;
        }
        else if (!PlayerHasTrait("estoc"))
        {
            // Diagnostic : si le joueur pense posséder Coup d'estoc, la clé stockée diffère peut-être.
            uiLog?.Append($"Clause perdue — pas de Coup d'estoc (traits joueur : {(playerTraits.Count == 0 ? "aucun" : string.Join(", ", playerTraits.Select(t => t.key)))}).");
        }

        JournalPlayer("Clause échouée, le joueur a raté ce tour.");
        MarkPlayerTurnScoreAtRisk(); // récupérable via un artefact de sauvetage

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
        // En démo tuto la fenêtre s'ouvre quand même (l'artefact peut être donné juste après).
        if (!tutorialLoopCounterPlay && !PlayerHasCounterArtifact()) yield break;

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
                    if (tutorialLoopCounterPlay)
                    {
                        elapsed = 0f; // démo tuto : la barre recommence, la fenêtre ne se ferme pas
                    }
                    else
                    {
                        // Auto-Next : la fenêtre expire, l'IA reprend
                        CancelExternalDiePick();
                        ClearExternalDiePick();
                        awaitingCounterPlay = false;
                        uiLog?.Append("Contre-Jeu : temps écoulé — l'IA reprend.");
                        break;
                    }
                }
            }

            yield return null;
        }

        if (counterPlayTimerBar) counterPlayTimerBar.gameObject.SetActive(false);
        awaitingCounterPlay = false;
        tutorialLoopCounterPlay = false; // fin de la démo de contre-jeu
        // On remet l'état des boutons selon la phase : si un contre-jeu (Guillotine) a terminé le
        // tour de l'IA (AITurnWaitEnd), NEXT doit rester ACTIF — le désactiver ici bloquait le jeu.
        ApplyButtonsState();

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

            JournalAI($"Reforme un FLASH {DieTag(flashFace)} (+{FLASH_SCORE[flashFace]} pts)");
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
            },
            journalEachDie: true));
    }

    // ---- Effets de Contre-Jeu (appelés par les artefacts pendant la fenêtre) ----

    // Bourse percée : -25% sur la bank de l'IA pour ce tour.
    public void CounterPlay_PiercedPurse()
    {
        aiBankMultiplier = 0.75f;
        aiBankModLabel = "Bourse percée";
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
        flashVisualIndices.Clear();     // le Flash est annulé : plus de surlignage orange
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
        aiNoRerollIndices.Add(i);     // ⛔ l'IA ne pourra PAS le relancer ensuite
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

    // Clocher des Veilleurs : cible AUTOMATIQUEMENT le dé de plus HAUTE valeur qui COMPTE pour
    // l'IA — un dé déjà verrouillé (Flash / scoré) OU un single marquant (5/10/SUN) encore LIBRE
    // (ex: un 10 pas encore posé). Sans inclure ces singles libres, un 10 non verrouillé était
    // ignoré et le jeu proposait de relancer les 5 d'un Flash, alors que le 10 est plus fort.
    // Si plusieurs dés partagent cette valeur (ex: 3×10), le joueur choisit lequel.
    public void CounterPlay_WatchersTower()
    {
        bool Counts(int i)
        {
            if (dice[i] == null) return false;
            if (dice[i].isLocked) return true;               // Flash / dé déjà scoré
            var f = dice[i].GetFace();                        // single marquant encore libre
            return f == DieFace.Five || f == DieFace.Ten || f == DieFace.Sun;
        }

        var candidates = Enumerable.Range(0, dice.Count).Where(Counts).ToList();
        if (candidates.Count == 0)
        {
            hintBanner?.Show("Clocher des Veilleurs : l'IA n'a aucun dé à relancer.");
            return;
        }

        int maxVal = candidates.Max(i => HighValueOf(dice[i].GetFace()));
        var top = candidates.Where(i => HighValueOf(dice[i].GetFace()) == maxVal).ToList();

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
                // Le SUN vaut 10 : l'affaiblir (-1 en valeur) donne 6 (comme un 10 → 6), pas un 10.
                DieFace.Sun => DieFace.Six,
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
            JournalAI($"Utilise « {aiArtifact.displayName} » : bank doublée ({baseGain} → {boosted})");
            return boosted;
        }
        if (k == "score.funeral_ledger" && baseGain >= 70)
        {
            aiArtifactUsed = true;
            int boosted = Mathf.RoundToInt(baseGain * 1.5f);
            ShowAction($"L'adversaire utilise « {aiArtifact.displayName} » : sa bank gagne +50% ({baseGain} → {boosted}) !");
            uiLog?.Append($"Artefact IA : {aiArtifact.displayName} (+50%).");
            JournalAI($"Utilise « {aiArtifact.displayName} » : bank +50% ({baseGain} → {boosted})");
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
            JournalAI($"Utilise « {aiArtifact.displayName} » : +{ENTRY_THRESHOLD} pts, ouverture");
            UpdateUI();
            yield return new WaitForSeconds(0.9f);
        }

        // ⚠️ Pas de fenêtre de contre-jeu AVANT le premier lancé : l'IA lance d'abord.
        yield return StartCoroutine(ResolveAIRoll(allUnlocked: true));
        if (phase == Phase.AITurnWaitEnd || gameOver || matchOver || isGameOverScreen) yield break;

        while (phase == Phase.AITurnPlaying && !gameOver && !matchOver && !isGameOverScreen)
        {
            // Fenêtre de contre-jeu APRÈS le lancé précédent (avant que l'IA rejoue/banque).
            // Si l'IA va BANQUER juste après (score validé, pas encore banqué), on ouvre la
            // fenêtre en mode « pré-bank » → c'est le SEUL moment où la Guillotine est jouable.
            aiPreBankCounterOpen = AIWouldBankNow();
            yield return StartCoroutine(AICounterPlayWindow());
            aiPreBankCounterOpen = false;
            // Un contre-jeu (Guillotine) peut avoir terminé le tour de l'IA sur-le-champ.
            if (phase == Phase.AITurnWaitEnd || gameOver || matchOver || isGameOverScreen) yield break;

            yield return new WaitForSeconds(AI_DELAY);

            if (dice != null && dice.All(d => d != null && d.isLocked))
            {
                // HOT DICE : les 5 dés scorent → RELANCE OBLIGATOIRE des 5 (règle du jeu). L'IA ne
                // peut pas « confirmer » son score en gardant les 5 dés : elle doit tout relancer.
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

            // Décision de bank unifiée (même prédicat que la prévision « pré-bank » ci-dessus,
            // pour que la fenêtre Guillotine tombe pile au bon moment).
            bool shouldBank = AIWouldBankNow();

            // GARDE UNIQUEMENT CE if(shouldBank) — celui avec la fenêtre Contre-Jeu :
            if (shouldBank)
            {

                // ⛔ Empêche tout double banking dans le même tour
                if (aiHasBankedThisTurn) { SetPhase(Phase.AITurnWaitEnd); yield break; }
                aiHasBankedThisTurn = true;

                // ➜ ICI SEULEMENT on ajoute le tour au total IA (avec éventuel malus Contre-Jeu)
                int baseTurnForBank = turnScore;
                int aiGain = Mathf.RoundToInt(turnScore * aiBankMultiplier);
                if (aiBankMultiplier < 1f)
                {
                    int cutPct = Mathf.RoundToInt((1f - aiBankMultiplier) * 100f);
                    uiLog?.Append($"{aiBankModLabel} : bank IA {baseTurnForBank} → {aiGain} (−{cutPct}%).");
                    ShowAIBankModifierLabel($"{aiBankModLabel} : bank IA {baseTurnForBank} → {aiGain} (−{cutPct}%)");
                }
                aiGain = ApplyAIArtifactOnBank(aiGain); // Dot de mariage / Livre des Comptes de l'IA
                aiScore += aiGain;
                turnScore = 0;
                aiBankMultiplier = 1f; // consommé
                JournalAI($"Banque {aiGain} pts (total {aiScore} pts)");
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
                    MarkTraitActiveForTurn("poisonglass", isPlayerSide: true);
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


    // Mémoire de la dernière face de Flash (pour un éventuel « sauvetage de Clause » via
    // transformation quand la face du Flash est ramenée sur un dé libre — voir Miroir).
    private DieFace lastFlashFace = DieFace.Two;
    private bool lastFlashValid = false;

    void ClearClauseState()
    {
        if (flashPendingResolution) { lastFlashFace = currentFlashFace; lastFlashValid = true; }
        flashPendingResolution = false;
        currentFlashFace = DieFace.Two;
        flashLockIndices.Clear();
        // ⚠️ flashVisualIndices n'est PAS vidé ici : le surlignage orange des dés d'un Flash
        // doit SURVIVRE à la résolution de la Clause. Il n'est retiré que lorsque les dés
        // sont réellement relancés/annulés (nouveau jet complet, Corne de sommeil, Flash brisé).
    }

    // Prédicat central : dans l'état courant, l'IA banquerait-elle MAINTENANT (au lieu de
    // relancer) ? Sert à la fois à la décision réelle et à savoir si la fenêtre de contre-jeu
    // est « pré-bank » (score validé, pas encore banqué → Guillotine jouable).
    bool AIWouldBankNow()
    {
        if (dice == null || dice.Count == 0) return false;
        if (phase == Phase.Clause) return false; // une Clause n'est pas une bank directe

        bool iaCanBank = aiOpened || turnScore >= ENTRY_THRESHOLD;

        // Hot dice (les 5 dés scorent) : relance OBLIGATOIRE des 5 → ce n'est jamais une bank directe.
        if (dice.All(d => d != null && d.isLocked)) return false;

        if (!iaCanBank) return false;
        int palierWin = GetWinThresholdForPalier(palierIndex);
        if (finalPhase && challenger == Turn.Player)
            return (aiScore + turnScore > targetScore); // décisif : ne banque que si ça gagne
        return (aiScore + turnScore >= palierWin) || (turnScore >= 25);
    }

    // Verrouille une série de dés de l'IA UN PAR UN, avec un petit délai + pulse,
    // pour que le joueur puisse suivre la sélection. Ajoute optionnellement des points par dé.
    // journalEachDie : journalise chaque dé retenu « Garde un X (+n) » comme pour le joueur
    // (utilisé pour les singles ; PAS pour le trio d'un Flash, déjà résumé par sa ligne FLASH).
    IEnumerator AILockDiceOneByOne(List<int> indices, System.Func<int, int> pointsFor = null, int lumpSumAfter = 0, bool journalEachDie = false)
    {
        // Score « forfaitaire » (FLASH) crédité TOUT DE SUITE, AVANT de verrouiller les 3 dés,
        // pour qu'on ait le temps de voir le « +X » du Flash avant la résolution de la Clause.
        if (lumpSumAfter != 0)
        {
            turnScore += lumpSumAfter;
            ShowAddScoreFx(lumpSumAfter); // valeur du FLASH de l'IA sur le « +X »
            UpdateUI();
        }

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
                    if (pts != 0) { turnScore += pts; ShowAddScoreFx(pts); } // « +X » pendant le tour de l'IA
                    if (journalEachDie) JournalAI($"Garde un {DieTag(dice[i].GetFace())} (+{pts} pts)");
                }
                UpdateUI();
                yield return new WaitForSeconds(aiSelectDelay);
            }
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
            flashVisualIndices.Clear(); // tous les dés sont relancés : plus d'orange
            aiNoRerollIndices.Clear();  // relance complète : les blocages de contre-jeu tombent
            indicesToRoll = Enumerable.Range(0, dice.Count).ToList();
        }
        else
        {
            // ⛔ Les dés relancés par un contre-jeu ne sont PAS rejoués par l'IA.
            indicesToRoll = dice.Select((d, i) => (d, i))
                                .Where(t => t.d != null && !t.d.isLocked && !aiNoRerollIndices.Contains(t.i))
                                .Select(t => t.i).ToList();
            if (indicesToRoll.Count == 0)
            {
                foreach (var d in dice) if (d != null) d.SetLocked(false);
                frozenLocks.Clear();
                ClearClauseState();
                flashVisualIndices.Clear(); // tous les dés sont relancés : plus d'orange
                aiNoRerollIndices.Clear();
                indicesToRoll = Enumerable.Range(0, dice.Count).ToList();
            }
        }

        // Un dé neuf est relancé : il n'appartient plus à l'ancien Flash (contour orange).
        foreach (var idx in indicesToRoll) flashVisualIndices.Remove(idx);

        // Tuto : faces forcées (dans l'ordre) sur les dés relancés, sinon jet aléatoire.
        int aiTf = 0;
        foreach (var idx in indicesToRoll)
        {
            if (aiTf < tutorialNextRollFaces.Count) dice[idx].SetFace(tutorialNextRollFaces[aiTf++]);
            else dice[idx].Roll();
        }
        if (aiTf > 0) tutorialNextRollFaces.Clear();
        yield return new WaitForSeconds(0.1f);

        // ---- TRAIT Pistage (ennemi) : son premier jet de la partie a 2 dés de 10 ----
        if (!aiHasRolledThisMatch && EnemyTraitActive("tracking") && indicesToRoll.Count >= 2)
        {
            dice[indicesToRoll[0]].SetFace(DieFace.Ten);
            dice[indicesToRoll[1]].SetFace(DieFace.Ten);
            hintBanner?.Show("Pistage : le Chasseur ouvre avec 2 dés de 10 !");
            uiLog?.Append("Trait Pistage : premier jet IA avec 2×10.");
            MarkTraitActiveForTurn("tracking", isPlayerSide: false);
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
            MarkTraitActiveForTurn("runes", isPlayerSide: false);
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
                JournalAI($"Utilise « {aiArtifact.displayName} » : {balanceTargets} dés transformés (2→5, 6→10)");
                yield return new WaitForSeconds(0.7f);
            }
        }

        // FULL (5 dés identiques) côté IA : victoire (6), supernova (10 → l'IA est éliminée), ou points.
        if (HandleFullIfAny(indicesToRoll, isAI: true)) yield break;

        var evalFlashOnly = EvaluateRoll(indicesToRoll, scoreSingles: false);

        if (evalFlashOnly.createdFlash)
        {
            currentFlashFace = evalFlashOnly.flashFace;

            JournalAI($"FLASH {DieTag(evalFlashOnly.flashFace)} (+{evalFlashOnly.pointsGained} pts) — doit Clause");

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
            // Guillotine : le tour de l'IA peut avoir été terminé (score 0) pendant la fenêtre.
            if (phase == Phase.AITurnWaitEnd || gameOver || matchOver || isGameOverScreen) yield break;

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
            // AIReselectScoringDice a déjà re-verrouillé les singles marquants restants.
            if (!flashStillValid)
            {
                ClearClauseState();
                flashVisualIndices.Clear(); // le Flash n'existe plus : plus de surlignage orange
                hintBanner?.Show("Le Flash de l'IA a été brisé.");
                uiLog?.Append("Contre-Jeu : Flash IA brisé (dés conservés).");

                // Si le brisement ne laisse AUCUN dé marquant (rien de scoré), le tour de l'IA
                // est PERDU : elle ne relance PAS l'intégralité de ses dés (ce serait un relancé
                // gratuit offert par le contre-jeu). Si au contraire il reste un marquant
                // (ex: Flash de 10 → les 10 restants sont re-verrouillés), elle continue normalement.
                if (turnScore <= 0)
                {
                    JournalAI("Flash brisé — aucun dé marquant, l'IA a raté ce tour.");
                    uiLog?.Append("Flash IA brisé sans dé marquant restant : tour perdu.");
                    turnScore = 0;
                    UpdateUI();
                    CheckWinConditionOnTurnEnded();
                    if (gameOver || matchOver || isGameOverScreen) yield break;
                    SetPhase(Phase.AITurnWaitEnd);
                    yield break;
                }

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
                // Verrouille les singles un par un (journalisés « Garde un X » comme le joueur).
                yield return StartCoroutine(AILockDiceOneByOne(
                    singlesToLock,
                    pointsFor: i =>
                    {
                        var f = dice[i].GetFace();
                        if (f == DieFace.Five) return 5;
                        if (f == DieFace.Ten || f == DieFace.Sun) return 10;
                        return 0;
                    },
                    journalEachDie: true));

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
            MarkTraitActiveForTurn("noose", isPlayerSide: false);
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
                ShowAddScoreFx(evalSingles.pointsGained); // valeur du FLASH sur le « +X »
                UpdateUI();
                JournalAI($"FLASH {DieTag(evalSingles.flashFace)} (+{evalSingles.pointsGained} pts)");
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
            JournalAI($"Utilise « {aiArtifact.displayName} » : relance {(rerollAll ? "tout le jet" : "un dé")}");
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
                ShowAddScoreFx(evalSingles.pointsGained); // valeur du FLASH sur le « +X »
                UpdateUI();
                JournalAI($"FLASH {DieTag(evalSingles.flashFace)} (+{evalSingles.pointsGained} pts)");
                SetPhase(Phase.AITurnPlaying);
                yield break;
            }
        }

        if (!evalSingles.anyScoring)
        {
            JournalAI("Lancé échoué, l'IA a raté ce tour.");
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

        // Verrouille les dés marquants un par un (journalisés « Garde un X » comme le joueur).
        yield return StartCoroutine(AILockDiceOneByOne(
            evalSingles.lockedThisRoll,
            pointsFor: i =>
            {
                var f = dice[i].GetFace();
                if (f == DieFace.Five) return 5;
                if (f == DieFace.Ten || f == DieFace.Sun) return 10;
                return 0;
            },
            journalEachDie: true));
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
            // Un dé relancé n'appartient plus à l'ancien Flash : on retire son contour orange.
            foreach (var idx in indicesToRoll) flashVisualIndices.Remove(idx);

            foreach (var idx in indicesToRoll) dice[idx].Roll();
            yield return new WaitForSeconds(0.1f);

            // FULL (5 dés identiques) pendant une Clause auto-résolue.
            if (HandleFullIfAny(indicesToRoll, isAI)) yield break;

            if (isAI && indicesToRoll.Count == 1)
            {
                int onlyIdx = indicesToRoll[0];
                if (dice[onlyIdx].GetFace() == DieFace.Sun)
                {
                    dice[onlyIdx].SetLocked(true);
                    turnScore += 10;
                    ShowAddScoreFx(10); // valeur du dégagement de Clause sur le « +X »
                    UpdateUI();
                    JournalCurrent($"Clause réussie avec un {DieTag(DieFace.Sun)} (+10 pts)");
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
                    ShowAddScoreFx(eval.pointsGained); // valeur du FLASH sur le « +X »
                    JournalCurrent($"Nouveau FLASH {DieTag(currentFlashFace)} (+{eval.pointsGained} pts) — doit Clause");
                    yield return new WaitForSeconds(CLAUSE_START_DELAY);
                    indicesToRoll = dice.Select((d, i) => (d, i)).Where(t => t.d != null && !t.d.isLocked).Select(t => t.i).ToList();
                    if (indicesToRoll.Count == 0)
                    {
                        foreach (var d in dice) if (d != null) d.SetLocked(false);
                        indicesToRoll = Enumerable.Range(0, dice.Count).ToList();
                    }
                    continue;
                }

                // Une Clause n'est dégagée que par UN seul dé marquant : on cite ce dé-là.
                // Les éventuels autres dés marquants gardés ce jet sont journalisés à part
                // (« Garde un X »), comme pour le joueur.
                var marking = eval.lockedThisRoll;
                if (marking != null && marking.Count > 0)
                {
                    ShowAddScoreFx(eval.pointsGained); // valeur du dégagement de Clause sur le « +X »
                    var clearFace = dice[marking[0]].GetFace();
                    JournalCurrent($"Clause réussie — Flash {DieTag(currentFlashFace)} dégagé avec un {DieTag(clearFace)} (+{SinglePoints(clearFace)} pts)");
                    for (int k = 1; k < marking.Count; k++)
                    {
                        var f = dice[marking[k]].GetFace();
                        JournalCurrent($"Garde un {DieTag(f)} (+{SinglePoints(f)} pts)");
                    }
                }
                SetPhase(isAI ? Phase.AITurnPlaying : Phase.Normal);
                yield break;
            }

            bool matchedFlashFace = indicesToRoll.Any(idx => MapFlashableFace(dice[idx].GetFace()) == currentFlashFace);
            if (matchedFlashFace)
            {
                JournalCurrent($"Clause : la face {DieTag(currentFlashFace)} retombe — relance");
                if (isAI)
                {
                    hintBanner?.Show($"Clause de l'IA : le {(int)currentFlashFace} retombe — elle relance !");
                    uiLog?.Append($"Clause IA : {(int)currentFlashFace} retombe, relance.");
                }
                yield return new WaitForSeconds(CLAUSE_REPEAT_DELAY);
                continue;
            }

            // ---- TRAIT Coup d'estoc (ennemi) : la Chevalière relance le dé perdant (1×/Clause) ----
            if (isAI && EnemyTraitActive("estoc") && !estocRetryUsed)
            {
                estocRetryUsed = true;
                hintBanner?.Show("Coup d'estoc : la Chevalière relance le dé perdant !");
                uiLog?.Append("Trait Coup d'estoc : relance exceptionnelle de la Clause IA.");
                MarkTraitActiveForTurn("estoc", isPlayerSide: false);
                yield return new WaitForSeconds(CLAUSE_REPEAT_DELAY);
                continue;
            }

            // Wimpout
            // Joueur : le score reste affiché (« en péril ») et peut encore être sauvé.
            // IA : perte immédiate.
            if (!isAI) { JournalPlayer("Clause échouée, le joueur a raté ce tour."); MarkPlayerTurnScoreAtRisk(); }
            else { JournalAI("Clause échouée, l'IA a raté ce tour."); turnScore = 0; UpdateUI(); }
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

        // Panneau de palier : mémorise l'issue face à CET adversaire (avant que enemyIndex avance).
        RecordPalierEnemyResult(winner == PLAYER_NAME);

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
        // Pas de phase d'achat si la campagne s'arrête là :
        //  · 3e défaite, OU
        //  · défaite contre le BOSS (dernier palier) — la campagne se termine.
        // Dans les deux cas la Résurrection reste possible (gérée par AdvanceToNextOpponentOrPalier).
        bool isBossFight = palierIndex >= GetPalierCount() - 1;
        // Chemin boisé : concession sans perte de vie → jamais un game over (et jamais contre le Boss).
        bool willBeGameOver = !concedeNoLifeLoss && ((defeatsCount + 1) >= 3 || isBossFight);
        if (willBeGameOver)
        {
            awaitingVictoryNext = false;
            string cause = isBossFight ? "Le Boss vous terrasse" : "3e défaite";
            ShowAction($"Défaite ! {winner} remporte le match. {cause} — CONTINUE pour terminer.");
            uiLog?.Append($"{cause} — Game Over (pas de phase d’obtention).");
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

    // Un dé peut-il réellement être cliqué pour être sélectionné ?
    // Doit rester le MIROIR exact des gardes de OnDieClicked : sans ça, un dé annoncé
    // « marquant » mais refusé au clic grise ROLL/BANK/NEXT et fige la partie.
    bool IsDieSelectableNow(int idx)
    {
        if (dice == null || idx < 0 || idx >= dice.Count) return false;
        var d = dice[idx];
        if (d == null || d.isLocked) return false;
        if (flashLockIndices.Contains(idx)) return false;
        if (frozenLocks.Contains(idx) && !mutableLocks.Contains(idx)) return false;
        return true;
    }

    // Les ensembles de verrous peuvent contenir des index périmés quand un artefact
    // déverrouille/transforme des dés (Fortifiant, Miroir, Corne…). On les resynchronise
    // sur l'état réel des dés avant tout calcul d'éligibilité ou de boutons.
    void PurgeStaleLockSets()
    {
        if (dice == null) return;

        bool Stale(int i) => i < 0 || i >= dice.Count || dice[i] == null || !dice[i].isLocked;

        frozenLocks.RemoveWhere(Stale);
        flashLockIndices.RemoveWhere(Stale);
        foreach (var i in mutableLocks.Where(Stale).ToList())
        {
            mutableLocks.Remove(i);
            // Le dé n'est plus verrouillé : ses points ne font plus partie du tour.
            if (mutablePoints.TryGetValue(i, out int pts)) { turnScore = Mathf.Max(0, turnScore - pts); mutablePoints.Remove(i); }
        }
    }

    void FillEligibleFromIndices(List<int> indices)
    {
        PurgeStaleLockSets();

        eligibleLockIndices.Clear();
        eligibleLockPoints.Clear();

        // On ne retient que les dés réellement cliquables : « éligible » doit impliquer
        // « sélectionnable », sinon les boutons se grisent sans aucune action possible.
        var pickable = indices.Where(IsDieSelectableNow).ToList();

        var counts = new Dictionary<DieFace, int>();
        foreach (var idx in pickable)
        {
            var f = dice[idx].GetFace();
            if (f == DieFace.Sun) continue;
            if (!counts.ContainsKey(f)) counts[f] = 0;
            counts[f]++;
        }
        bool pairExists = counts.Values.Any(v => v >= 2);

        foreach (var idx in pickable)
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
        // Même filtre que FillEligibleFromIndices : seuls les dés réellement cliquables comptent.
        var unlocked = Enumerable.Range(0, dice?.Count ?? 0).Where(IsDieSelectableNow).ToList();

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

        artifactTooltip = FindAnyObjectByType<ArtifactTooltip>(FindObjectsInactive.Include);

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

    void EnterArtifactPick(int count = 3, List<Artifact> specific = null, bool selectable = true)
    {
        if (awaitingArtifactPick) return;

        count = Mathf.Max(1, count);

        // 1) Masquer les dés & le score du tour
        HideDice(true);
        SetTurnScoreVisible(false);

        // 2) Préparer l’offre (liste imposée pour le tutoriel, sinon aléatoire)
        offeredArtifacts.Clear();
        var picks = specific ?? (artifactLibrary ? artifactLibrary.GetRandomDistinct(count) : new List<Artifact>());
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

                // Choix au CLIC sur la carte… (désactivé si non sélectionnable : affichage seul)
                int captured = i;
                if (selectable)
                {
                    view.onClicked = _ => SelectArtifact(captured);
                    // …ou en glissant la carte sur le personnage (zone de dépôt de choix).
                    if (artifactPickDropZone)
                        view.EnablePickDrag(artifactPickDropZone, () => SelectArtifact(captured));
                }

                offeredCardViews.Add(view);
            }
        }

        // 4) État & UI
        awaitingArtifactPick = true;
        ApplyButtonsState();

        // Palier Panel : il apparaît pendant la phase de sélection (reste affiché depuis le
        // choix de trait s'il y en a eu un avant).
        ShowPalierPanelForReward();

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

        // Palier Panel : reste affiché pendant le choix de trait.
        ShowPalierPanelForReward();

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
                if (artifactPickDropZone)
                    view.EnablePickDrag(artifactPickDropZone, () => SelectTrait(captured));
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

        // Palier Panel : ce trait est obtenu au franchissement qui vient d'avoir lieu.
        // À cet instant palierIndex pointe déjà le NOUVEAU palier → segment = palierIndex - 1.
        // Un segment peut accueillir PLUSIEURS traits : on les accumule (sans doublon).
        int seg = Mathf.Max(0, palierIndex - 1);
        if (!segmentTraits.TryGetValue(seg, out var segList)) { segList = new List<TraitDef>(); segmentTraits[seg] = segList; }
        if (!segList.Exists(t => t.key == chosen.key)) segList.Add(chosen);

        hintBanner?.Show($"Trait acquis : « {chosen.name} ».");
        RebuildTraitIcons();

        // Tutoriel : une carte de trait a été choisie → peut faire avancer une étape.
        onActionPerformed?.Invoke(GameAction.CardPicked);

        // Le Palier Panel est affiché : le trait apparaît en animation, puis on attend Next
        // avant de passer à la suite (voir OnPressEndRound).
        if (palierPanelView && palierPanelView.gameObject.activeSelf)
        {
            palierPanelView.Render(BuildPalierSnapshot(), animate: false, revealNewTraits: true);

            // Démonte les cartes de trait mais NE lance PAS encore la suite.
            if (artifactTooltip) artifactTooltip.Hide();
            foreach (var v in offeredTraitCards) if (v) Destroy(v.gameObject);
            offeredTraitCards.Clear();
            offeredTraits.Clear();
            awaitingTraitPick = false;

            palierPanelNextCallback = afterTraitPick;
            afterTraitPick = null;
            awaitingPalierPanelNext = true;
            if (endRoundButton) endRoundButton.interactable = true;
            ShowAction("Trait acquis ! Appuyez sur Next pour continuer.");
            return;
        }

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
                JournalPlayer($"Clause : la face {DieTag(currentFlashFace)} retombe — relance");
                ShowAction($"Clause : {(int)currentFlashFace} retombe — appuie sur ROLL pour continuer.");
                SetPhase(Phase.Clause);
            }
            else
            {
                // Pas de points et pas de retombée sur la face du Flash → wimpout
                MarkPlayerTurnScoreAtRisk();
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
                MarkPlayerTurnScoreAtRisk();
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

        // Tutoriel : une carte a été choisie → peut faire avancer une étape (WaitForAction = CardPicked).
        onActionPerformed?.Invoke(GameAction.CardPicked);

        // Sortie de la phase (réactive TurnScoreText via CancelArtifactPickUI → SetTurnScoreVisible(true))
        ExitArtifactPickAndAdvance();
    }

    // (Le bouton USE a été retiré : l'activation d'un artefact passe uniquement
    //  par le drag & drop de la carte d'inventaire sur la table — voir InventoryUI/ArtifactDropZone.)


    void ExitArtifactPickAndAdvance()
    {
        // Palier Panel : il disparaît dès qu'un artefact est choisi (fin de la phase de sélection).
        if (palierPanelView) palierPanelView.Close();

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
        // L'image de fond du score (TableScore) suit le score du tour : masquée pendant les
        // phases de choix d'artefact/trait (mêmes appels false→enter / true→exit).
        if (tableScoreObject) tableScoreObject.SetActive(v);
    }

    // Affiche « +X » (rouge, sans contour) à côté du score du tour avec fondu, puis le masque.
    // Redémarre à chaque sélection : une nouvelle sélection relance le fondu et le compte à rebours.
    // skipIfFirst : n'affiche PAS le « +X » pour le tout premier score du tour (le score passe
    // de 0 à sa valeur → le nombre lui-même suffit). Les FLASH passent false : leur valeur est
    // toujours affichée. Sans état : on compare au score du tour COURANT (déjà incrémenté).
    void ShowAddScoreFx(int amount, bool skipIfFirst = true)
    {
        if (addScoreTurnText == null || amount == 0) return;
        if (skipIfFirst && (turnScore - amount) <= 0) return; // premier score du tour → pas de +X
        if (_addScoreFxCo != null) StopCoroutine(_addScoreFxCo);
        _addScoreFxCo = StartCoroutine(AddScoreFxRoutine(amount));
    }

    // Masque immédiatement le « +X » (désélection d'un dé, fin de tour…).
    void HideAddScoreFx()
    {
        if (_addScoreFxCo != null) { StopCoroutine(_addScoreFxCo); _addScoreFxCo = null; }
        if (addScoreTurnText)
        {
            addScoreTurnText.alpha = 1f;
            addScoreTurnText.gameObject.SetActive(false);
        }
    }

    // Recale le « +X » juste à droite du nombre du score du tour (suit sa largeur réelle),
    // pour qu'il reste proche quand le score est petit sans le recouvrir quand il grandit.
    void PositionAddScoreFx()
    {
        if (!addScoreAutoPosition || !addScoreTurnText || !turnScoreText) return;
        var a = addScoreTurnText.rectTransform;
        var s = turnScoreText.rectTransform;
        if (a.parent != s.parent) return; // repositionnement seulement si même parent

        turnScoreText.ForceMeshUpdate();
        float halfNumber = turnScoreText.preferredWidth * 0.5f; // score supposé centré
        float halfPlus   = addScoreTurnText.preferredWidth * 0.5f;
        // X : suit la largeur du nombre ; Y : FIGÉ sur la position d'origine (+ décalage réglable)
        // pour ne jamais redescendre sous le score au fil des tours.
        a.anchoredPosition = new Vector2(
            s.anchoredPosition.x + halfNumber + addScoreGapPx + halfPlus,
            _addScoreBaseY + addScoreOffsetYPx);
    }

    System.Collections.IEnumerator AddScoreFxRoutine(int amount)
    {
        addScoreTurnText.outlineWidth = 0f;            // jamais de contour sur le « +X »
        addScoreTurnText.color = GameColors.Points;    // rouge du jeu
        addScoreTurnText.text = $"+{amount}";
        addScoreTurnText.gameObject.SetActive(true);
        PositionAddScoreFx();

        float fade = Mathf.Max(0f, addScoreFadeDuration);
        float hold = Mathf.Max(0f, addScoreFxDuration - 2f * fade);

        yield return FadeText(addScoreTurnText, 0f, 1f, fade); // apparition
        if (hold > 0f) yield return new WaitForSeconds(hold);  // maintien
        yield return FadeText(addScoreTurnText, 1f, 0f, fade); // disparition

        addScoreTurnText.gameObject.SetActive(false);
        addScoreTurnText.alpha = 1f; // prêt pour la prochaine fois
        _addScoreFxCo = null;
    }

    System.Collections.IEnumerator FadeText(TMP_Text t, float from, float to, float dur)
    {
        if (!t) yield break;
        if (dur <= 0f) { t.alpha = to; yield break; }
        float e = 0f;
        while (e < dur)
        {
            e += Time.deltaTime;
            t.alpha = Mathf.Lerp(from, to, e / dur);
            yield return null;
        }
        t.alpha = to;
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
        JournalPlayerUnselectDie(idx); // sélection annulée par artefact : retire sa ligne
        HideAddScoreFx();              // et le « +X » disparaît aussitôt
        if (mutablePoints.TryGetValue(idx, out int pts))
        {
            turnScore -= pts;
            mutablePoints.Remove(idx);
        }

        if (clauseClearedBySelection && mutableLocks.Count == 0)
        {
            clauseClearedBySelection = false;
            flashPendingResolution = true;
            RemovePlayerClauseClearedLine(); // Clause ré-armée : retire la ligne « Clause réussie »
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
        SetDicePickHover(filter); // contour brillant au survol sur les dés ciblables
    }

    public void ClearExternalDiePick()
    {
        externalDiePickFilter = null;
        onExternalDiePicked = null;
        ClearDicePickHover();
    }

    // Active le survol « ciblable » (glow) sur les dés valides pour l'artefact en cours.
    void SetDicePickHover(Func<int, bool> filter)
    {
        if (dice == null) return;
        for (int i = 0; i < dice.Count; i++)
            if (dice[i] != null) dice[i].SetPickHoverEnabled(filter == null || filter(i));
    }

    void ClearDicePickHover()
    {
        if (dice == null) return;
        foreach (var d in dice) if (d != null) d.SetPickHoverEnabled(false);
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
            uiLog?.Append($"Os du Tricheur : score du tour restauré ({turnScore}).");
            UpdateUI();
        }
        wimpoutLostTurnScore = 0;

        // Le péril du lancé raté est levé : cette relance rejoue le coup. Un nouvel échec le
        // re-marquera (MarkPlayerTurnScoreAtRisk) ; un succès rend le score bankable.
        // (Même correctif que l'Artefact du Temps : sans ce reset, BANK restait grisé.)
        turnScoreAtRisk = false;

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
                foreach (var i in eval.flashLockIndices) { dice[i].SetLocked(true); frozenLocks.Add(i); flashLockIndices.Add(i); flashVisualIndices.Add(i); }
                turnScore += eval.pointsGained;
                ShowAddScoreFx(eval.pointsGained); // valeur du FLASH sur le « +X »
                currentFlashFace = eval.flashFace;
                flashPendingResolution = true;
                UpdateUI();
                JournalPlayer($"Dé relancé : FLASH {DieTag(currentFlashFace)} (+{eval.pointsGained} pts) — doit Clause");
                FillEligibleFromIndices(unlocked.Where(i => !eval.flashLockIndices.Contains(i)).ToList());
                ShowAction($"FLASH {(int)currentFlashFace} (via artefact).");
                SetPhase(Phase.Clause);
                yield break;
            }

            FillEligibleFromIndices(unlocked);

            if (eligibleLockIndices.Count > 0)
            {
                JournalPlayer("Dé relancé : Clause à dégager");
                ShowAction("Clause : sélectionne un 5/10/SUN pour dégager le Flash, ou ROLL pour retenter.");
                SetPhase(Phase.Clause);
            }
            else
            {
                bool matchedFlashFace = unlocked.Any(i => MapFlashableFace(dice[i].GetFace()) == currentFlashFace);
                if (matchedFlashFace)
                {
                    JournalPlayer($"Clause : la face {DieTag(currentFlashFace)} retombe — relance");
                    ShowAction($"Clause : {(int)currentFlashFace} retombe — appuie sur ROLL pour continuer.");
                    SetPhase(Phase.Clause);
                }
                else
                {
                    JournalPlayer("Clause échouée, le joueur a raté ce tour.");
                    MarkPlayerTurnScoreAtRisk();
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
                foreach (var i in eval.flashLockIndices) { dice[i].SetLocked(true); frozenLocks.Add(i); flashLockIndices.Add(i); flashVisualIndices.Add(i); }
                turnScore += eval.pointsGained;
                ShowAddScoreFx(eval.pointsGained); // valeur du FLASH sur le « +X »
                currentFlashFace = eval.flashFace;
                flashPendingResolution = true;
                UpdateUI();
                JournalPlayer($"Dé relancé : FLASH {DieTag(currentFlashFace)} (+{eval.pointsGained} pts) — doit Clause");
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
                JournalPlayer("Dé relancé : dés marquants à choisir");
                ShowAction("Sélectionne les dés marquants (5/10/SUN), puis BANK ou ROLL.");
                SetPhase(Phase.Normal);
            }
            else
            {
                // Aucun point nulle part → wimpout immédiat
                JournalPlayer("Dé relancé sans succès, le joueur a raté ce tour.");
                MarkPlayerTurnScoreAtRisk();
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

        // Le péril du lancé raté est levé : ce relancé rejoue le coup. S'il échoue à nouveau,
        // le wimpout re-marquera le score en péril ; s'il réussit, le score redevient bankable.
        // (Sans ce reset, turnScoreAtRisk restait vrai et BANK demeurait grisé après un succès.)
        turnScoreAtRisk = false;

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
        flashVisualIndices.Clear(); // lancé complet rejoué : aucun contour orange ne subsiste
        foreach (var i in rollableIndices)
            dice[i].Roll();

        lastRolledIndices.Clear();
        lastRolledIndices.AddRange(rollableIndices);
        yield return null;

        ApplyPendingFaceOverrides(lastRolledIndices);

        // FULL (5 dés identiques) après un relancé complet (Temps niv.1).
        if (HandleFullIfAny(lastRolledIndices, isAI: false)) yield break;

        var eval = EvaluateRoll(lastRolledIndices, scoreSingles: false);
        CheckLoveFilterBonus(eval.createdFlash, eval.flashFace, lastRolledIndices);

        if (eval.createdFlash)
        {
            foreach (var i in eval.flashLockIndices)
            {
                dice[i].SetLocked(true);
                frozenLocks.Add(i);
                flashLockIndices.Add(i);
                flashVisualIndices.Add(i); // surlignage orange persistant
            }
            turnScore += eval.pointsGained;
            ShowAddScoreFx(eval.pointsGained); // valeur du FLASH sur le « +X »
            currentFlashFace = eval.flashFace;
            flashPendingResolution = true;
            UpdateUI();

            JournalPlayer($"Coup rejoué : FLASH {DieTag(currentFlashFace)} (+{eval.pointsGained} pts) — doit Clause");

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
            JournalPlayer("Coup rejoué — dés marquants à choisir");
            ShowAction("Coup rejoué ! Sélectionne tes 5/10/SUN pour marquer, puis ROLL pour continuer.");
            SetPhase(Phase.Normal);
        }
        else
        {
            // Wimpout : le score conservé est perdu (risque du relancé), récupérable via Os du Tricheur
            JournalPlayer("Coup rejoué sans succès, le joueur a raté ce tour.");
            MarkPlayerTurnScoreAtRisk();
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
        {
            bool used = artifactPowers.TryUseFromInventory(inventoryIndex);
            // Tutoriel : un artefact a réellement été joué → peut faire avancer une étape.
            if (used) onActionPerformed?.Invoke(GameAction.ArtifactPlayed);
            return used;
        }

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

    // ===================== NOUVEAUX ARTEFACTS =====================

    // Artefact du Temps Niv2 : REJOUE tout le tour depuis le début — le score du tour est
    // REMIS À ZÉRO, tous les dés sont déverrouillés puis relancés comme au premier jet.
    // (Différent du Niv1 qui refait le lancé en CONSERVANT le score.)
    public System.Collections.IEnumerator Artifact_RestartTurn()
    {
        if (dice == null || dice.Count == 0) yield break;

        turnScore = 0;
        turnScoreAtRisk = false;
        wimpoutLostTurnScore = 0;
        flashPendingResolution = false;
        clauseClearedBySelection = false;

        foreach (var d in dice) if (d != null) d.SetLocked(false);
        frozenLocks.Clear();
        mutableLocks.Clear();
        mutablePoints.Clear();
        flashLockIndices.Clear();
        flashVisualIndices.Clear();
        eligibleLockIndices.Clear();
        eligibleLockPoints.Clear();
        UpdateUI();

        var rollable = Enumerable.Range(0, dice.Count).Where(i => dice[i] != null).ToList();
        foreach (var i in rollable) dice[i].Roll();
        lastRolledIndices.Clear();
        lastRolledIndices.AddRange(rollable);
        yield return null;
        ApplyPendingFaceOverrides(lastRolledIndices);

        if (HandleFullIfAny(lastRolledIndices, isAI: false)) yield break;

        var eval = EvaluateRoll(lastRolledIndices, scoreSingles: false);
        if (eval.createdFlash)
        {
            foreach (var i in eval.flashLockIndices)
            {
                dice[i].SetLocked(true);
                frozenLocks.Add(i); flashLockIndices.Add(i); flashVisualIndices.Add(i);
            }
            turnScore += eval.pointsGained;
            ShowAddScoreFx(eval.pointsGained);
            currentFlashFace = eval.flashFace;
            flashPendingResolution = true;
            UpdateUI();
            JournalPlayer($"Tour rejoué : FLASH {DieTag(currentFlashFace)} (+{eval.pointsGained} pts) — doit Clause");
            FillEligibleFromIndices(lastRolledIndices.Where(i => !eval.flashLockIndices.Contains(i)).ToList());
            SetPhase(Phase.Clause);
            ShowAction($"FLASH {(int)currentFlashFace} — Clause.");
            yield break;
        }

        FillEligibleFromIndices(lastRolledIndices);
        if (eligibleLockIndices.Count > 0)
        {
            JournalPlayer("Tour rejoué — dés marquants à choisir");
            ShowAction("Tour rejoué ! Sélectionne tes 5/10/SUN, puis ROLL pour continuer.");
            SetPhase(Phase.Normal);
        }
        else
        {
            JournalPlayer("Tour rejoué sans succès, le joueur a raté ce tour.");
            MarkPlayerTurnScoreAtRisk();
            ShowAction("Wimpout.");
            SetPhase(Phase.WaitEnd);
        }
        UpdateUI();
        ApplyButtonsState();
    }

    // Sacrifice humain : conditions pour créer un Flash de 10 (3 dés libres deviennent des 10).
    public bool CanCreateTenFlashNow()
    {
        if (gameOver || matchOver || isGameOverScreen) return false;
        if (currentTurn != Turn.Player) return false;
        if (flashPendingResolution) return false; // pas en pleine Clause
        if (phase != Phase.Normal && phase != Phase.WaitEnd) return false; // après un jet
        if (dice == null) return false;
        // Un jet a eu lieu (au moins une face affichée) et 3 dés libres pour former le Flash.
        bool hasFaces = dice.Any(d => d != null && d.faceImage && d.faceImage.sprite != null);
        return hasFaces && dice.Count(d => d != null && !d.isLocked) >= 3;
    }

    // Sacrifice humain : force un FLASH de 10 en posant 10 sur 3 dés libres.
    public void Artifact_CreateTenFlash()
    {
        if (!CanCreateTenFlashNow())
        { hintBanner?.Show("Sacrifice humain : impossible de créer un flash maintenant."); return; }

        var pick = Enumerable.Range(0, dice.Count)
            .Where(i => dice[i] != null && !dice[i].isLocked)
            .Take(3).ToList();
        foreach (var i in pick) dice[i].SetFace(DieFace.Ten);
        JournalPlayerAction("Sacrifice humain : un FLASH de 10 se matérialise !");
        Artifacts_CreateFlashFromIndices(pick, DieFace.Ten);
    }

    // Cercueil maudit : conditions d'usage (il faut un score à sécuriser).
    public bool CanUseCursedCoffinNow()
    {
        if (gameOver || matchOver || isGameOverScreen) return false;
        if (currentTurn != Turn.Player) return false;
        bool inPostRoll = phase == Phase.Normal || phase == Phase.Clause || phase == Phase.WaitEnd;
        if (!inPostRoll) return false;
        return turnScore > 0 || wimpoutLostTurnScore > 0 || turnScoreAtRisk;
    }

    // Cercueil maudit : banque le score courant (même en péril / en pleine Clause) et MET FIN au tour.
    public void Artifact_CursedCoffinBank()
    {
        // Récupère un éventuel score « en péril » / perdu au wimpout : le Cercueil le sécurise.
        RestoreWimpoutScoreIfAny();
        if (turnScore <= 0)
        { hintBanner?.Show("Cercueil maudit : aucun score à banquer."); return; }

        // Sécurise même en pleine Clause : les points du Flash sont déjà dans turnScore.
        flashPendingResolution = false;
        ClearClauseState();
        turnScoreAtRisk = false;
        wimpoutLostTurnScore = 0;

        int entry = (rules != null) ? rules.entryThreshold : 35;
        if (!playerOpened && GetEffectiveTurnScoreForEntry() >= entry) playerOpened = true;

        JournalPlayerAction("Cercueil maudit : score sécurisé, le tour prend fin.");
        hintBanner?.Show("Cercueil maudit : score banqué, fin du tour.");
        CommitPlayerBankAndEndTurn();
    }

    // Mystérieux colis : ajoute un artefact ALÉATOIRE de la bibliothèque à l'inventaire du joueur.
    public bool Artifact_AddRandomToInventory()
    {
        if (artifactLibrary == null || artifactLibrary.artifacts == null || playerInventory == null)
        { hintBanner?.Show("Mystérieux colis : aucune bibliothèque d'artefacts."); return false; }

        var pool = artifactLibrary.artifacts.FindAll(a => a != null);
        if (pool.Count == 0) return false;
        if (playerInventory.Count >= PlayerInventory.Capacity)
        { hintBanner?.Show("Mystérieux colis : inventaire plein !"); return false; }

        var pick = pool[UnityEngine.Random.Range(0, pool.Count)];
        if (!playerInventory.TryAdd(pick)) return false;

        hintBanner?.Show($"Mystérieux colis : « {pick.displayName} » rejoint votre inventaire !");
        uiLog?.Append($"Mystérieux colis : +1 artefact ({pick.displayName}).");
        RefreshInventoryUI();
        return true;
    }

    // Sacrifice humain : retire un artefact aléatoire de l'inventaire (le colis/sacrifice s'est
    // déjà retiré lui-même via onConsumed, donc tout ce qui reste est « un autre » artefact).
    public Artifact Artifact_SacrificeRandomArtifact()
    {
        if (playerInventory == null || playerInventory.Count == 0) return null;
        int idx = UnityEngine.Random.Range(0, playerInventory.Count);
        var victim = playerInventory.GetAt(idx);
        playerInventory.RemoveAt(idx);
        RefreshInventoryUI();
        if (victim)
        {
            hintBanner?.Show($"Sacrifice humain : « {victim.displayName} » est sacrifié !");
            uiLog?.Append($"Sacrifice humain : artefact sacrifié ({victim.displayName}).");
        }
        return victim;
    }

    void RefreshInventoryUI()
    {
        if (inventoryUI) inventoryUI.RefreshNow();
        inventoryDots?.Refresh();
    }

    // Guillotine flambant neuve (Contre-Jeu) : annule le score du tour de l'IA. On force le
    // multiplicateur de bank à 0 : quelle que soit la suite du tour, l'IA banquera 0 point.
    // (Réutilise la mécanique éprouvée de la Bourse percée, sans interrompre la coroutine IA.)
    public void CounterPlay_Guillotine()
    {
        // Annulation IMMÉDIATE : le score du tour de l'IA tombe à 0 tout de suite (pas de bank
        // ni de recompte). On nettoie les dés/verrous scorés et on termine le tour de l'IA.
        turnScore = 0;
        if (dice != null)
            foreach (var d in dice)
                if (d != null) { d.SetLocked(false); d.SetFlashHighlight(false); }

        frozenLocks.Clear();
        flashLockIndices.Clear();
        flashVisualIndices.Clear();
        mutableLocks.Clear();
        mutablePoints.Clear();
        aiFlashIndices.Clear();
        aiFlashCancelled = false;
        flashPendingResolution = false;

        aiHasBankedThisTurn = true;      // l'IA ne banquera rien de plus ce tour
        aiDiceChangedByCounter = false;  // pas de re-sélection après la fenêtre
        awaitingCounterPlay = false;     // ferme la fenêtre de contre-jeu

        UpdateUI();
        SetPhase(Phase.AITurnWaitEnd);   // le tour de l'IA se termine (le joueur appuie sur NEXT)

        hintBanner?.Show("Guillotine flambant neuve : le score du tour de l'IA est annulé (0) !");
        uiLog?.Append("Contre-Jeu : Guillotine — score du tour IA remis à 0, tour terminé.");
    }

    // ===================== ARTEFACTS LÉGENDAIRES =====================

    // Chemin boisé : concède le combat en cours (compté comme perdu) SANS perdre de vie.
    // Interdit contre le Boss (dernier palier).
    public bool CanConcedeMatchNow()
    {
        if (gameOver || matchOver || isGameOverScreen) return false;
        if (currentTurn != Turn.Player) return false;
        if (palierIndex >= GetPalierCount() - 1) return false; // pas contre le Boss
        return true;
    }

    public void Artifact_ConcedeMatchNoLife()
    {
        if (!CanConcedeMatchNow())
        { hintBanner?.Show("Chemin boisé : inutilisable ici (ou contre le Boss)."); return; }

        concedeNoLifeLoss = true;
        JournalPlayerAction("Chemin boisé : combat abandonné (sans perte de vie).");
        hintBanner?.Show("Chemin boisé : vous quittez ce combat sans perdre de vie.");
        uiLog?.Append("Chemin boisé : combat concédé, aucune vie perdue.");
        EndMatch(AI_NAME); // traité comme une défaite, mais concedeNoLifeLoss neutralise la perte de vie
    }

    // Cadeau royal : ajoute un artefact EPIC ou LÉGENDAIRE au hasard dans l'inventaire du joueur.
    public bool Artifact_AddRandomEpicOrLegendaryToInventory()
    {
        if (artifactLibrary == null || artifactLibrary.artifacts == null || playerInventory == null)
        { hintBanner?.Show("Cadeau royal : aucune bibliothèque d'artefacts."); return false; }

        var pool = artifactLibrary.artifacts.FindAll(a => a != null &&
            (a.rarity == ArtifactRarity.Epic || a.rarity == ArtifactRarity.Legendary));
        if (pool.Count == 0)
        { hintBanner?.Show("Cadeau royal : aucun artefact epic/légendaire disponible."); return false; }
        if (playerInventory.Count >= PlayerInventory.Capacity)
        { hintBanner?.Show("Cadeau royal : inventaire plein !"); return false; }

        var pick = pool[UnityEngine.Random.Range(0, pool.Count)];
        if (!playerInventory.TryAdd(pick)) return false;

        hintBanner?.Show($"Cadeau royal : « {pick.displayName} » rejoint votre inventaire !");
        uiLog?.Append($"Cadeau royal : +1 artefact epic/légendaire ({pick.displayName}).");
        RefreshInventoryUI();
        return true;
    }

    // Alignement cosmique : transforme aléatoirement TOUS les dés libres en 10 ou 5, avec un Sun.
    public void Artifact_TransformAllToHigh()
    {
        var free = Enumerable.Range(0, dice.Count).Where(i => dice[i] != null && !dice[i].isLocked).ToList();
        if (free.Count == 0)
        { hintBanner?.Show("Alignement cosmique : aucun dé à transformer."); return; }

        // Le Sun se pose de préférence sur le dé noir (isSunDie), sinon sur un dé au hasard.
        int sunIndex = -1;
        foreach (var i in free) if (dice[i].isSunDie) { sunIndex = i; break; }
        if (sunIndex < 0) sunIndex = free[UnityEngine.Random.Range(0, free.Count)];

        foreach (var i in free)
        {
            if (i == sunIndex) dice[i].SetFace(DieFace.Sun);
            else dice[i].SetFace(UnityEngine.Random.value < 0.5f ? DieFace.Ten : DieFace.Five);
        }

        Artifacts_ReevaluateAfterDiceChanged("Alignement cosmique : les dés deviennent 10, 5 et un Sun !");
    }

    // Loto post mortem : gain instantané de 150 points au score du joueur.
    public void Artifact_LotoPostMortem()
    {
        if (IsMatchOverOrScreen()) return;
        playerScore += 150;
        // Ce gain massif OUVRE le joueur : le seuil d'entrée (35 pts) ne s'applique plus,
        // il peut désormais banquer n'importe quel montant sur ce tour.
        playerOpened = true;
        JournalPlayerAction("Loto post mortem : +150 pts");
        hintBanner?.Show("Loto post mortem : +150 points !");
        uiLog?.Append("Loto post mortem : +150 pts (gain instantané, joueur ouvert).");
        UpdateUI();
        CheckWinConditionOnTurnEnded(); // un gain de 150 peut déclencher la phase finale
        ApplyButtonsState();
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
        //    Les index sont enregistrés dans flashLockIndices/frozenLocks comme pour un Flash
        //    « naturel » : sans ça le jeu considérait ces dés comme libres alors qu'ils sont
        //    verrouillés, et l'état incohérent finissait par bloquer la sélection.
        foreach (var i in tripleIndices)
            if (i >= 0 && i < dice.Count && dice[i] != null)
            {
                dice[i].SetLocked(true);
                // Un dé sélectionné qui rejoint le Flash cède ses points de single au Flash.
                if (mutableLocks.Remove(i) && mutablePoints.TryGetValue(i, out int single))
                { turnScore = Mathf.Max(0, turnScore - single); mutablePoints.Remove(i); }
                JournalPlayerUnselectDie(i); // sa ligne « Garde un X » laisse place à la ligne FLASH
                frozenLocks.Add(i);
                flashLockIndices.Add(i);
                flashVisualIndices.Add(i); // surlignage orange
            }

        if (FLASH_SCORE.TryGetValue(face, out var pts)) { turnScore += pts; ShowAddScoreFx(pts); }
        UpdateUI();

        // 2) état de Clause standard (mêmes variables que ton flow normal)
        currentFlashFace = face;
        flashPendingResolution = true;

        var remaining = Enumerable.Range(0, dice.Count)
            .Where(i => !tripleIndices.Contains(i) && dice[i] != null && !dice[i].isLocked)
            .ToList();

        FillEligibleFromIndices(remaining); // singles sur le reste des dés
        JournalPlayerAction($"FLASH {DieTag(currentFlashFace)} (+{pts} pts) via artefact — doit Clause");
        ShowAction($"FLASH {(int)currentFlashFace} — sélectionne 5/10/SUN ou ROLL pour continuer la Clause.");
        SetPhase(Phase.Clause);
    }

    // Restaure le score du tour perdu au wimpout (l'artefact vient de sauver le tour).
    void RestoreWimpoutScoreIfAny()
    {
        // Cas historique : le score avait ete remis a zero -> on le restaure.
        if (wimpoutLostTurnScore > 0 && turnScore == 0)
        {
            turnScore = wimpoutLostTurnScore;
            uiLog?.Append($"Artefact : tour sauvé, score du tour restauré ({turnScore}).");
        }
        // Cas courant : le score etait « en peril » et reste affiche -> on leve le peril.
        else if (turnScoreAtRisk && turnScore > 0)
        {
            uiLog?.Append($"Artefact : tour sauvé, score du tour conservé ({turnScore}).");
        }

        wimpoutLostTurnScore = 0;
        turnScoreAtRisk = false;
    }

    public void Artifacts_ReevaluateAfterDiceChanged(string banner = null)
    {
        // Journal du joueur : le banner décrit ce que l'artefact vient de faire (transformation…).
        if (!string.IsNullOrEmpty(banner)) JournalPlayerAction(banner);

        // Un artefact vient de déverrouiller/transformer des dés : on resynchronise d'abord
        // les ensembles de verrous, sinon un index périmé rend un dé « marquant » incliquable.
        PurgeStaleLockSets();

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

        // 1bis) Clause ACTIVE : si un dé libre correspond MAINTENANT à la face du Flash (ex :
        //       amené par le Miroir) sans être marquant, on confirme que la Clause peut se
        //       poursuivre — le joueur relance (ROLL) les dés restants.
        if (flashPendingResolution && phase == Phase.Clause && eligibleLockIndices.Count == 0)
        {
            bool matchesFlash = unlocked.Any(i => MapFlashableFace(dice[i].GetFace()) == currentFlashFace);
            if (matchesFlash)
                ShowAction($"La face {(int)currentFlashFace} est de retour — appuie sur ROLL pour continuer la Clause.");
        }

        // 1ter) Sauvetage de Clause : le tour a wimpouté en Clause, mais une transformation
        //       (Miroir…) vient de ramener la face du dernier Flash sur un dé libre → on RÉ-ARME
        //       la Clause pour permettre au joueur de RELANCER, comme si la face était retombée.
        if (phase == Phase.WaitEnd && !flashPendingResolution && lastFlashValid
            && eligibleLockIndices.Count == 0)
        {
            bool matchesFlash = unlocked.Any(i => MapFlashableFace(dice[i].GetFace()) == lastFlashFace);
            bool flashTrioLocked = dice.Count(d => d != null && d.isLocked) >= 3; // points du Flash encore en jeu
            if (matchesFlash && flashTrioLocked)
            {
                RestoreWimpoutScoreIfAny();
                currentFlashFace = lastFlashFace;
                flashPendingResolution = true;
                SetPhase(Phase.Clause);
                ShowAction($"La face {(int)currentFlashFace} est ramenée — appuie sur ROLL pour continuer la Clause !");
                JournalPlayer($"Clause relancée : la face {DieTag(currentFlashFace)} est ramenée");
                if (!string.IsNullOrEmpty(banner)) JournalPlayerAction(banner);
                ApplyButtonsState();
                UpdateUI();
                return;
            }
        }

        // Si on était bloqué en WaitEnd (tour perdu) et qu’on a maintenant du marquant,
        // on rend la main au joueur ET on RESTAURE le score perdu au wimpout.
        if (phase == Phase.WaitEnd && eligibleLockIndices.Count > 0)
        {
            RestoreWimpoutScoreIfAny();
            if (turnScore > 0)
                ShowAction($"Tour sauvé ! Score restauré ({turnScore} pts) — sélectionne un dé marquant.");
            // Un Flash reste à dégager → c'est une Clause, pas une phase Normale : en Normal
            // le Flash en attente grise BANK, ROLL et NEXT en même temps (partie figée).
            SetPhase(flashPendingResolution ? Phase.Clause : Phase.Normal);
            if (!string.IsNullOrEmpty(banner)) ShowAction(banner);
        }

        // Cohérence : un Flash en attente implique la phase Clause (seule phase où ROLL
        // reste ouvert pour le tenter). Sans ça, l'état Normal + Flash bloque tous les boutons.
        if (flashPendingResolution && phase == Phase.Normal)
        {
            uiLog?.Append("État corrigé : Flash en attente → retour en Clause.");
            SetPhase(Phase.Clause);
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
                flashVisualIndices.Add(i); // surlignage orange persistant
            }
            turnScore += eval.pointsGained;
            ShowAddScoreFx(eval.pointsGained); // valeur du FLASH sur le « +X »
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
            // Sauvetage « in extremis » : si le tour venait de wimpouter (Clause perdue) et que
            // ce dé d'ajout est marquant (SUN/10/5 — Soleil en bouteille), il DÉGAGE la Clause.
            // On restaure d'abord le score du wimpout (points du Flash) et on lève le péril,
            // puis on crédite le +10. Sans ça, le joueur perdait le Flash et devait relancer.
            bool rescuedClause = (phase == Phase.WaitEnd || turnScoreAtRisk);
            if (rescuedClause) RestoreWimpoutScoreIfAny();

            // Ajout immédiat des points
            turnScore += pointsSingle;
            UpdateUI();
            hintBanner?.Show($"+{pointsSingle} points.");

            if (rescuedClause)
            {
                JournalPlayer($"Clause réussie in extremis avec un {DieTag(face)} (+{pointsSingle} pts)");
                uiLog?.Append("Soleil en bouteille : Clause sauvée in extremis.");
            }

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

        // --- Le dé d'ajout n'est PAS marquant : "lancé nul" ---
        var allUnlocked = Enumerable.Range(0, dice.Count)
            .Where(i => dice[i] != null && !dice[i].isLocked).ToList();

        FillEligibleFromIndices(allUnlocked);

        // Exception : pendant une Clause, si le dé d'ajout tombe pile sur la FACE du Flash en
        // cours, il reste pertinent → on garde le déroulé normal de la Clause (ROLL autorisé).
        if (flashPendingResolution && dv.GetFace() == currentFlashFace)
        {
            SetPhase(Phase.Clause);
            hintBanner?.Show("Dé d’ajout sur la face du Flash — continue ta Clause ou ROLL.");
            RemoveAddedDieFromPool(dv, keepVisibleUntilNextAction: true, dimmedNotLocked: true);
            Artifacts_RefreshUI();
            yield break;
        }

        // Dé surprise nul : le tour est perdu. On NE laisse PLUS relancer le reste des dés
        // (auparavant le joueur récupérait une relance « gratuite », jugée injuste) : seul NEXT
        // reste disponible, exactement comme un wimpout normal (score en péril si un artefact
        // de sauvetage est disponible).
        if (!canCauseWimpout)
        {
            // Grisé (non marquant) et retiré du pool : il ne re-roulera pas et ne compte pas.
            RemoveAddedDieFromPool(dv, keepVisibleUntilNextAction: true, dimmedNotLocked: true);
            JournalPlayer("Dé surprise nul — tour raté.");
            uiLog?.Append("Dé surprise : lancé nul → wimpout.");

            MarkPlayerTurnScoreAtRisk(); // remet à 0 (ou laisse « en péril » si sauvetage possible)

            bool canStillRescue = PlayerHasRescueArtifact();
            hintBanner?.Show(canStillRescue
                ? "Dé surprise nul ! Un artefact peut encore sauver le tour — sinon NEXT."
                : "Dé surprise nul — Wimpout. NEXT pour finir le tour.");

            // Tant qu'un sauvetage reste possible, on ne clôt pas le match tout de suite.
            if (canStillRescue) pendingWinCheckOnContinue = true;
            else CheckWinConditionOnTurnEnded();

            SetPhase(Phase.WaitEnd); // seul NEXT est actif
            ApplyButtonsState();
            yield break;
        }

        // --- Chemin historique (canCauseWimpout = true, non utilisé par le Dé surprise) ---
        // Clause en cours ?
        if (flashPendingResolution)
        {
            SetPhase(Phase.Clause);
            hintBanner?.Show("Le dé d’ajout n’est pas marquant — continue ta Clause ou ROLL.");
            RemoveAddedDieFromPool(dv, keepVisibleUntilNextAction: true, dimmedNotLocked: true);
            Artifacts_RefreshUI();
            yield break;
        }

        // Singles ailleurs ?
        if (eligibleLockIndices.Count > 0)
        {
            SetPhase(Phase.Normal);
            hintBanner?.Show("Le dé d’ajout n’est pas marquant — tu peux sélectionner/banquer ou ROLL.");
            RemoveAddedDieFromPool(dv, keepVisibleUntilNextAction: true, dimmedNotLocked: true);
            Artifacts_RefreshUI();
            yield break;
        }

        // Rien de marquant sur le board → wimpout réel
        turnScore = 0;
        UpdateUI();
        SetPhase(Phase.WaitEnd);
        hintBanner?.Show("Wimpout.");

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



    void RemoveAddedDieFromPool(DieView dv, bool keepVisibleUntilNextAction, bool dimmedNotLocked = false)
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
                // Dé non marquant : grisé (pas de cadenas/contour bleu, il ne compte pas).
                // Dé marquant retiré après scoring : verrou visuel classique.
                if (dimmedNotLocked) dv.SetDimmed(true);
                else dv.SetLocked(true);
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

    // Bourse percée (Contre-Jeu) : réutilise ScoreModifierText (scoreModLabel) pour montrer
    // l'effet du malus sur la bank de l'IA. Aucun modificateur JOUEUR n'est actif pendant le
    // tour de l'IA, le label est donc libre. Il est effacé au prochain StartNewTurn.
    void ShowAIBankModifierLabel(string text)
    {
        if (!scoreModLabel) return;
        scoreModLabel.gameObject.SetActive(true);
        scoreModLabel.text = text;
    }

    void HideAIBankModifierLabel()
    {
        if (!scoreModLabel) return;
        scoreModLabel.text = "";
        scoreModLabel.gameObject.SetActive(false);
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

        // ⚠️ La prévisualisation était calculée mais jamais affichée : on l'écrit ici.
        // Le joueur voit ainsi en temps réel ce que vaudra sa bank (ex : Dot de mariage ×2).
        scoreModLabel.text = string.IsNullOrEmpty(perTurnPreview)
            ? $"Bank estimée : {previewAfterPerTurn}"
            : $"Bank estimée : {perTurnPreview}";
    }


    // Banque immédiatement le tour de l'IA et gère la logique de "chase".
    // À placer dans GameManager (même région que les coroutines IA).
    private IEnumerator AIBankNowAndHandleChase()
    {
        if (aiHasBankedThisTurn) yield break;
        aiHasBankedThisTurn = true;

        int baseTurnForChase = turnScore;
        int reducedChase = Mathf.RoundToInt(turnScore * aiBankMultiplier);
        if (aiBankMultiplier < 1f)
        {
            int cutPct = Mathf.RoundToInt((1f - aiBankMultiplier) * 100f);
            ShowAIBankModifierLabel($"{aiBankModLabel} : bank IA {baseTurnForChase} → {reducedChase} (−{cutPct}%)");
            uiLog?.Append($"{aiBankModLabel} : bank IA {baseTurnForChase} → {reducedChase} (−{cutPct}%).");
        }
        int aiChaseGain = ApplyAIArtifactOnBank(reducedChase);
        aiScore += aiChaseGain;     // ajoute UNE SEULE FOIS (malus Contre-Jeu + artefact IA)
        turnScore = 0;
        aiBankMultiplier = 1f;
        JournalAI($"Banque {aiChaseGain} pts (total {aiScore} pts)");
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
            MarkTraitActiveForTurn("poisonglass", isPlayerSide: true);
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
