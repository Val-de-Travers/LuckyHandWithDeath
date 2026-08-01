using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>Comment une étape se termine.</summary>
public enum TutorialAdvance
{
    ClickToContinue, // le joueur clique n'importe où pour passer à la suite (actions bloquées)
    WaitForAction,   // le joueur doit effectuer l'action de jeu attendue (requiredAction)
    AutoDelay        // avance automatiquement après autoDelay secondes
}

/// <summary>Position de la boîte de dialogue à l'écran.</summary>
public enum TutorialDialoguePosition { Top, Center, Bottom }

/// <summary>État de jeu à imposer à l'entrée d'une étape (None = ne change rien).</summary>
public enum TutorialPhaseSetup
{
    None,
    RollOff,        // Lancé d'ordre (tirage au sort d'ouverture)
    PlayerRoll,     // Lancé des dés : joueur (début d'un tour joueur)
    AIRoll,         // Jet IA + sélection des dés marquants (faces forcées)
    ArtifactPick,   // Choix d'artefact
    TraitPick,      // Choix de trait
    EmptyBoard,     // Plateau de jeu vide
    SelectDicePlayer, // Sélection des dés marquants : joueur (score visible)
    SelectDiceAI,     // Tour IA complet automatique (jet + sélection + banque)
    AIStartTurn,    // Ouvre un tour IA : dés vierges affichés (aucun jet)
    AIReroll,       // Relance IA des dés non sélectionnés (faces forcées) ; wimpout si rien ne marque
    AIBank,         // L'IA banque le score du tour
    CounterPlayDemo // Jet IA forcé (FLASH) + fenêtre de Contre-Jeu en boucle (démo)
}

/// <summary>Interrupteur à 3 états pour un réglage optionnel par étape.</summary>
public enum TutorialToggle { Leave, Show, Hide }

/// <summary>
/// Une étape de tutoriel, entièrement éditable dans l'Inspector.
/// Ajouter une étape = ajouter un élément à la liste de TutorialManager.
/// </summary>
[System.Serializable]
public class TutorialStep
{
    [Header("Texte")]
    public string title;
    [TextArea(2, 6)] public string text;
    public TutorialDialoguePosition dialoguePosition = TutorialDialoguePosition.Top;
    [Tooltip("Coché : NE PAS assombrir l'arrière-plan pour cette étape (le voile est actif par défaut).")]
    public bool disableDim = false;

    [Header("Avancement")]
    public TutorialAdvance advance = TutorialAdvance.ClickToContinue;
    [Tooltip("Action attendue si 'advance' = WaitForAction.")]
    public GameAction requiredAction = GameAction.Roll;
    [Tooltip("Délai si 'advance' = AutoDelay (secondes).")]
    public float autoDelay = 2f;
    [Tooltip("Petit délai après l'action réussie avant d'afficher l'étape suivante (laisse voir le résultat).")]
    public float postActionDelay = 0.6f;
    [Tooltip("Texte d'invite en bas de la boîte (vide = valeur par défaut selon le mode).")]
    public string hintOverride = "";

    [Header("Blocage des actions")]
    [Tooltip("Actions du jeu AUTORISÉES pendant l'étape. Vide = tout est bloqué. " +
             "En mode WaitForAction, 'requiredAction' est autorisée automatiquement.")]
    public List<GameAction> allowedActions = new();
    [Tooltip("Force-active les boutons autorisés même si le jeu les grise (ex. NEXT pour une " +
             "étape de navigation hors phase de fin de tour).")]
    public bool forceEnableAction = false;

    [Header("Mise en scène (optionnel)")]
    [Tooltip("Coché : réinitialise les faces des dés à l'entrée de l'étape (repart d'un plateau propre).")]
    public bool resetDice = false;
    [Tooltip("Force l'état du jeu à l'entrée de l'étape (None = ne change rien).")]
    public TutorialPhaseSetup setupPhase = TutorialPhaseSetup.None;
    [Tooltip("Si Setup Phase = ArtifactPick : glisse ici les assets Artifact à proposer. " +
             "Vide = 3 artefacts aléatoires.")]
    public List<Artifact> artifactChoices = new();
    [Tooltip("Si Setup Phase = ArtifactPick : décoché = cartes affichées mais NON sélectionnables " +
             "(juste pour montrer). Coché (défaut) = le joueur peut choisir.")]
    public bool artifactSelectable = true;
    [Tooltip("Assets Artifact à ajouter DIRECTEMENT à l'inventaire du joueur à l'entrée de l'étape.")]
    public List<Artifact> giveToInventory = new();
    [Tooltip("Affiche ces faces sur les dés immédiatement, SANS lancer (pour montrer les faces).")]
    public List<DieFace> showDiceFaces = new();
    [Tooltip("Affichage du bandeau d'aide du jeu pendant l'étape (Leave = inchangé).")]
    public TutorialToggle hintBanner = TutorialToggle.Leave;
    [Tooltip("Affichage de tout le contenu de Score pendant l'étape (Leave = inchangé).")]
    public TutorialToggle score = TutorialToggle.Leave;

    [Header("Dés scriptés (optionnel)")]
    [Tooltip("Faces forcées au prochain jet, dans l'ordre des dés (vide = aléatoire).")]
    public List<DieFace> forceNextRoll = new();

    [Header("Surbrillance (optionnel)")]
    [Tooltip("Élément d'UI à mettre en avant pendant l'étape.")]
    public RectTransform highlightTarget;
    [Tooltip("Éléments qui NE sont PAS assombris (restent en pleine lumière au-dessus du voile). " +
             "Ex : les dés, la barre de boutons… selon l'étape.")]
    public List<RectTransform> keepBright = new();
    [Tooltip("Coché : l'inventaire (bouton + panneau) est utilisable — remonté au-dessus du voile.")]
    public bool allowInventory = false;

    [Header("Événements (câblables dans l'Inspector)")]
    public UnityEvent onEnter;
    public UnityEvent onExit;
}

/// <summary>
/// Séquenceur de tutoriel : joue une partie scriptée en affichant des fenêtres de
/// dialogue, en bloquant les actions non pertinentes et en guidant le joueur.
///
/// Utilisation : poser ce composant dans la scène du tutoriel, remplir la liste
/// <see cref="steps"/>. La boîte de dialogue est générée automatiquement si aucune
/// référence d'UI n'est fournie — rien d'autre à câbler pour démarrer.
/// </summary>
public class TutorialManager : MonoBehaviour
{
    [Header("Références")]
    [Tooltip("Laisser vide : trouvé automatiquement dans la scène.")]
    public GameManager game;
    [Tooltip("Conteneur 'Score' de la scène. Laisser vide = repli sur le score du tour + la table.")]
    public GameObject scoreRoot;
    [Tooltip("Bouton d'ouverture de l'inventaire (remonté au-dessus du voile si 'Allow Inventory'). " +
             "Le panneau, lui, se remonte tout seul à l'ouverture.")]
    public RectTransform inventoryButton;

    [Header("Étapes du tutoriel")]
    public List<TutorialStep> steps = new();

    [Header("Démarrage")]
    public bool autoStart = true;
    public float startDelay = 0.3f;

    [Header("UI de dialogue (laisser vide = génération auto)")]
    public Canvas dialogueCanvas;
    public RectTransform dialoguePanel;
    public TMP_Text titleLabel;
    public TMP_Text bodyLabel;
    public TMP_Text continueHint;
    public Image inputBlocker;
    public RectTransform highlightFrame;

    [Header("Polices (optionnel — laisser vide = police TMP par défaut)")]
    [Tooltip("Police du titre des panneaux du tutoriel.")]
    public TMP_FontAsset titleFont;
    [Tooltip("Police du texte (corps + invite) des panneaux du tutoriel.")]
    public TMP_FontAsset bodyFont;

    [Header("Apparence (génération auto)")]
    public Color blockerColor = new(0f, 0f, 0f, 0.45f);
    public Color panelColor = new(0.09f, 0.10f, 0.16f, 0.96f);
    public Color textColor = Color.white;
    public Color highlightColor = new(1f, 0.86f, 0.25f, 0.28f);
    public int sortingOrder = 5000;

    int index = -1;
    bool running;
    Coroutine pending;
    RectTransform activeHighlight;

    // Éléments remontés au-dessus du voile (pleine lumière + cliquables). Les Canvas/Raycaster
    // que l'on ajoute sont MÉMORISÉS et réutilisés (jamais détruits en cours de tuto : un Destroy
    // est différé et casserait la réutilisation dans la même frame → clignotement 1 étape sur 2).
    class BrightRec
    {
        public Canvas canvas; public bool weAddedCanvas; public bool prevOverride; public int prevOrder;
        public GraphicRaycaster raycaster; public bool weAddedRaycaster;
    }
    readonly Dictionary<RectTransform, BrightRec> brightRecs = new();
    readonly List<BrightRec> brightActive = new();

    TutorialStep Current => (index >= 0 && index < steps.Count) ? steps[index] : null;

    // Appelé par l'éditeur quand on ajoute le composant (ou via clic droit ▸ Reset).
    // Pré-remplit un MODÈLE d'étapes, entièrement éditable ensuite dans l'Inspector.
    void Reset()
    {
        if (steps != null && steps.Count > 0) return;
        steps = BuildExampleSteps();
    }

    void Awake()
    {
        // Doit s'exécuter avant le Start() de GameManager (tous les Awake précèdent les Start)
        // pour empêcher le lancement automatique d'une partie : le tutoriel pilote les phases.
        if (!game) game = FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        if (game) game.tutorialMode = true;
    }

    void Start()
    {
        if (!game) game = FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        EnsureEventSystem();
        BuildUiIfNeeded();
        ApplyFonts();
        EnsurePanelClickable();

        if (game) game.tutorialActionFilter = ActionFilter;
        if (game) game.tutorialForceEnable = ForceEnableFilter;
        if (game) game.onActionPerformed += OnGameAction;

        HideUi();
        if (autoStart) StartCoroutine(StartAfterDelay());
    }

    void OnDestroy()
    {
        if (game)
        {
            if (game.tutorialActionFilter == ActionFilter) game.tutorialActionFilter = null;
            if (game.tutorialForceEnable == ForceEnableFilter) game.tutorialForceEnable = null;
            game.onActionPerformed -= OnGameAction;
        }
        DestroyBrightAdds();
    }

    IEnumerator StartAfterDelay()
    {
        yield return new WaitForSecondsRealtime(startDelay);
        Begin();
    }

    // ===================== Séquence =====================

    public void Begin()
    {
        if (steps == null || steps.Count == 0) return;
        running = true;
        ShowStep(0);
    }

    public void Next()
    {
        if (!running) return;
        var leaving = Current;
        if (leaving != null) leaving.onExit?.Invoke();

        if (index + 1 >= steps.Count) { Finish(); return; }
        ShowStep(index + 1);
    }

    void ShowStep(int i)
    {
        if (pending != null) { StopCoroutine(pending); pending = null; }

        index = i;
        var step = Current;
        if (step == null) { Finish(); return; }

        // Mise en scène : état du jeu, faces montrées, bandeau d'aide.
        if (game)
        {
            if (step.resetDice) game.Tutorial_ResetDice();

            // Faces forcées AVANT la mise en phase : une phase qui lance (SelectDice) doit les utiliser.
            if (step.forceNextRoll != null && step.forceNextRoll.Count > 0)
                game.Tutorial_ForceNextRoll(step.forceNextRoll.ToArray());

            ApplyPhaseSetup(step);

            if (step.giveToInventory != null)
                foreach (var a in step.giveToInventory) if (a) game.Tutorial_GiveArtifactAsset(a);

            if (step.showDiceFaces != null && step.showDiceFaces.Count > 0)
                game.Tutorial_ShowDiceFaces(step.showDiceFaces.ToArray());

            if (step.hintBanner == TutorialToggle.Show) game.Tutorial_SetHintBannerVisible(true);
            else if (step.hintBanner == TutorialToggle.Hide) game.Tutorial_SetHintBannerVisible(false);
        }

        // Affichage du contenu de Score (indépendant de la phase).
        if (step.score == TutorialToggle.Show) SetScoreVisible(true);
        else if (step.score == TutorialToggle.Hide) SetScoreVisible(false);

        step.onEnter?.Invoke();

        // Contenu.
        if (titleLabel)
        {
            titleLabel.text = Colorize(step.title ?? "");
            titleLabel.gameObject.SetActive(!string.IsNullOrEmpty(step.title));
        }
        if (bodyLabel) bodyLabel.text = Colorize(step.text ?? "");
        PositionDialogue(step.dialoguePosition);
        SetHighlight(step.highlightTarget);
        ApplyKeepBright(BuildBrightList(step));
        ShowUi();

        // Mode d'avancement.
        switch (step.advance)
        {
            case TutorialAdvance.ClickToContinue:
                SetVeil(!step.disableDim, true);
                SetHint(step, "▼ Cliquez sur cette fenêtre pour continuer");
                break;

            case TutorialAdvance.AutoDelay:
                SetVeil(!step.disableDim, true);
                SetHint(step, "");
                pending = StartCoroutine(AutoAdvance(step.autoDelay));
                break;

            case TutorialAdvance.WaitForAction:
                // Voile visuel selon le réglage, mais on NE bloque PAS les clics (le joueur agit).
                SetVeil(!step.disableDim, false);
                SetHint(step, ActionHint(step.requiredAction));
                break;
        }

        // Recalcule l'état des boutons pour la nouvelle étape : réactive ceux qui
        // redeviennent autorisés (le filtre du jeu ne fait, lui, que désactiver).
        if (game) game.Tutorial_RefreshButtons();
    }

    void Finish()
    {
        running = false;
        index = -1;
        if (game)
        {
            game.tutorialActionFilter = null;   // rend la main au jeu
            game.tutorialForceEnable = null;
            game.Tutorial_RefreshButtons();     // restaure aussitôt l'état normal des boutons
        }
        HideUi();
        SetHighlight(null);
    }

    IEnumerator AutoAdvance(float delay)
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, delay));
        Next();
    }

    // ===================== Entrées =====================

    // Appelé par le bouton plein écran (mode ClickToContinue).
    public void OnBlockerClicked()
    {
        if (!running) return;
        if (Current != null && Current.advance == TutorialAdvance.ClickToContinue)
            Next();
    }

    void OnGameAction(GameAction a)
    {
        var step = Current;
        if (!running || step == null) return;
        if (step.advance == TutorialAdvance.WaitForAction && a == step.requiredAction)
        {
            if (pending != null) StopCoroutine(pending);
            pending = StartCoroutine(AdvanceAfterAction(step.postActionDelay));
        }
    }

    IEnumerator AdvanceAfterAction(float delay)
    {
        // Bloque toute nouvelle action le temps que le résultat s'affiche (voile selon l'étape).
        var s = Current;
        SetVeil(s == null || !s.disableDim, true);
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, delay));
        Next();
    }

    // Affiche/masque tout le contenu de Score : le conteneur assigné ET les éléments connus
    // (un enfant a pu être désactivé individuellement par une phase — on force les deux).
    void SetScoreVisible(bool visible)
    {
        if (scoreRoot) scoreRoot.SetActive(visible);
        if (game) game.Tutorial_SetScoreVisible(visible);
    }

    // Colorise les mots FLASH / CLAUSE / FULL avec le code couleur partagé (GameColors).
    static string Colorize(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bFLASH\b",
            m => $"<color=#{GameColors.FlashHex}>{m.Value}</color>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bCLAUSE\b",
            m => $"<color=#{GameColors.ClauseHex}>{m.Value}</color>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bFULL\b",
            m => $"<color=#{GameColors.FullHex}>{m.Value}</color>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return s;
    }

    // Impose l'état de jeu demandé par l'étape.
    void ApplyPhaseSetup(TutorialStep step)
    {
        if (!game || step == null) return;
        switch (step.setupPhase)
        {
            case TutorialPhaseSetup.RollOff:      game.Tutorial_EnterRollOff();     break;
            case TutorialPhaseSetup.PlayerRoll:   game.Tutorial_StartPlayerTurn();  break;
            case TutorialPhaseSetup.ArtifactPick: game.Tutorial_EnterArtifactPick(step.artifactChoices, step.artifactSelectable); break;
            case TutorialPhaseSetup.TraitPick:    game.Tutorial_EnterTraitPick();   break;
            case TutorialPhaseSetup.EmptyBoard:   game.Tutorial_ClearBoard();       break;
            case TutorialPhaseSetup.SelectDicePlayer: game.Tutorial_SelectDicePlayer(); break;
            case TutorialPhaseSetup.SelectDiceAI:     game.Tutorial_SelectDiceAI();     break;
            case TutorialPhaseSetup.AIRoll:       game.Tutorial_AIRoll();           break;
            case TutorialPhaseSetup.AIStartTurn:  game.Tutorial_AIStartTurn();      break;
            case TutorialPhaseSetup.AIReroll:     game.Tutorial_AIReroll();         break;
            case TutorialPhaseSetup.AIBank:       game.Tutorial_AIBank();           break;
            case TutorialPhaseSetup.CounterPlayDemo: game.Tutorial_CounterPlayDemo(); break;
        }
    }

    // Filtre consulté par GameManager : quelles actions sont autorisées maintenant ?
    bool ActionFilter(GameAction a)
    {
        var step = Current;
        if (!running || step == null) return true; // hors tutoriel : tout est permis
        if (step.advance == TutorialAdvance.WaitForAction && a == step.requiredAction) return true;
        return step.allowedActions != null && step.allowedActions.Contains(a);
    }

    // Actions à FORCE-activer (bouton cliquable même si le jeu le grise), pour l'étape courante.
    bool ForceEnableFilter(GameAction a)
    {
        var step = Current;
        if (!running || step == null) return false;

        // NAVIGATION : une étape SANS phase, en WaitForAction=EndRound, est forcément une étape
        // de navigation → NEXT est toujours cliquable (le roll-off, lui, a une Setup Phase).
        if (step.setupPhase == TutorialPhaseSetup.None
            && step.advance == TutorialAdvance.WaitForAction
            && step.requiredAction == GameAction.EndRound
            && a == GameAction.EndRound)
            return true;

        // Sinon, force-enable explicite via le toggle.
        if (!step.forceEnableAction) return false;
        if (step.advance == TutorialAdvance.WaitForAction && a == step.requiredAction) return true;
        return step.allowedActions != null && step.allowedActions.Contains(a);
    }

    // ===================== UI : état =====================

    // dim = voile visuel (assombrissement) ; block = bloque les clics du jeu.
    // Le voile ne fait QUE bloquer (il n'avance pas). C'est la boîte de dialogue qui,
    // quand elle est "cliquable" (block), capte le clic "continuer".
    void SetVeil(bool dim, bool block)
    {
        if (inputBlocker)
        {
            inputBlocker.color = dim ? blockerColor : new Color(0, 0, 0, 0);
            inputBlocker.raycastTarget = block; // empêche d'interagir avec le jeu, sans avancer
        }
        if (dialoguePanel)
        {
            var g = dialoguePanel.GetComponent<Graphic>();
            if (g) g.raycastTarget = block;      // seule la fenêtre capte le clic "continuer"
            var btn = dialoguePanel.GetComponent<Button>();
            if (btn) btn.interactable = block;
        }
    }

    // Assure que la boîte de dialogue capte le clic "continuer" (bouton câblé sur l'avance).
    void EnsurePanelClickable()
    {
        if (!dialoguePanel) return;
        var btn = dialoguePanel.GetComponent<Button>();
        if (!btn) btn = dialoguePanel.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.RemoveListener(OnBlockerClicked);
        btn.onClick.AddListener(OnBlockerClicked);
    }

    void SetHint(TutorialStep step, string fallback)
    {
        if (!continueHint) return;
        string h = (step != null && !string.IsNullOrEmpty(step.hintOverride)) ? step.hintOverride : fallback;
        continueHint.text = h;
        continueHint.gameObject.SetActive(!string.IsNullOrEmpty(h));
    }

    string ActionHint(GameAction a) => a switch
    {
        GameAction.Roll => "À vous : appuyez sur Roll",
        GameAction.Bank => "À vous : appuyez sur Bank",
        GameAction.EndRound => "À vous : appuyez sur Next",
        GameAction.PalierCircle => "Cliquez sur le cercle de palier",
        GameAction.PalierClose => "Fermez le panneau de palier",
        GameAction.CardPicked => "Choisissez une carte",
        GameAction.ArtifactPlayed => "Glissez un artefact sur la table pour le jouer",
        _ => "À vous de jouer"
    };

    void ShowUi()
    {
        if (dialogueCanvas) dialogueCanvas.gameObject.SetActive(true);
        if (dialoguePanel) dialoguePanel.gameObject.SetActive(true);
    }

    void HideUi()
    {
        if (dialoguePanel) dialoguePanel.gameObject.SetActive(false);
        SetVeil(false, false);
        if (highlightFrame) highlightFrame.gameObject.SetActive(false);
        ClearKeepBright();
    }

    // Liste finale des éléments à garder en lumière : keepBright de l'étape + inventaire si autorisé.
    List<RectTransform> BuildBrightList(TutorialStep step)
    {
        var list = new List<RectTransform>();
        if (step.keepBright != null) list.AddRange(step.keepBright);
        if (step.allowInventory && inventoryButton) list.Add(inventoryButton);
        return list;
    }

    // Remonte les éléments choisis au-dessus du voile (pleine lumière + cliquables).
    void ApplyKeepBright(List<RectTransform> targets)
    {
        ClearKeepBright();
        if (targets == null) return;
        int order = sortingOrder + 1; // juste au-dessus du voile du tutoriel
        foreach (var t in targets)
        {
            if (!t) continue;

            if (!brightRecs.TryGetValue(t, out var rec))
            {
                // Première fois pour cet élément : on prépare (et mémorise) Canvas + Raycaster.
                rec = new BrightRec();
                var c = t.GetComponent<Canvas>();
                if (!c) { c = t.gameObject.AddComponent<Canvas>(); rec.weAddedCanvas = true; }
                rec.canvas = c;
                rec.prevOverride = c.overrideSorting;
                rec.prevOrder = c.sortingOrder;

                // Sans GraphicRaycaster propre, l'élément sous un Canvas imbriqué n'est plus cliquable.
                var gr = t.GetComponent<GraphicRaycaster>();
                if (!gr) { gr = t.gameObject.AddComponent<GraphicRaycaster>(); rec.weAddedRaycaster = true; }
                rec.raycaster = gr;

                brightRecs[t] = rec;
            }

            if (rec.canvas)
            {
                rec.canvas.overrideSorting = true;
                rec.canvas.sortingOrder = order;
            }
            brightActive.Add(rec);
        }
    }

    void ClearKeepBright()
    {
        foreach (var rec in brightActive)
        {
            if (!rec.canvas) continue;
            if (rec.weAddedCanvas) rec.canvas.overrideSorting = false; // on garde le composant, repasse sous le voile
            else { rec.canvas.overrideSorting = rec.prevOverride; rec.canvas.sortingOrder = rec.prevOrder; }
        }
        brightActive.Clear();
    }

    // Retire définitivement les composants qu'on a ajoutés (raycaster avant canvas, car il en dépend).
    void DestroyBrightAdds()
    {
        foreach (var kv in brightRecs)
        {
            var rec = kv.Value;
            if (rec.weAddedRaycaster && rec.raycaster) Destroy(rec.raycaster);
            if (rec.weAddedCanvas && rec.canvas) Destroy(rec.canvas);
        }
        brightRecs.Clear();
    }

    void PositionDialogue(TutorialDialoguePosition pos)
    {
        if (!dialoguePanel) return;
        Vector2 anchor, pivot, offset;
        switch (pos)
        {
            case TutorialDialoguePosition.Center:
                anchor = new Vector2(0.5f, 0.5f); pivot = new Vector2(0.5f, 0.5f); offset = Vector2.zero; break;
            case TutorialDialoguePosition.Bottom:
                anchor = new Vector2(0.5f, 0f); pivot = new Vector2(0.5f, 0f); offset = new Vector2(0f, 140f); break;
            default: // Top
                anchor = new Vector2(0.5f, 1f); pivot = new Vector2(0.5f, 1f); offset = new Vector2(0f, -40f); break;
        }
        dialoguePanel.anchorMin = dialoguePanel.anchorMax = anchor;
        dialoguePanel.pivot = pivot;
        dialoguePanel.anchoredPosition = offset;
    }

    // ===================== UI : surbrillance =====================

    void SetHighlight(RectTransform target)
    {
        activeHighlight = target;
        if (highlightFrame)
            highlightFrame.gameObject.SetActive(target != null);
    }

    void LateUpdate()
    {
        if (!highlightFrame || activeHighlight == null || !highlightFrame.gameObject.activeSelf) return;

        var canvasRT = dialogueCanvas ? dialogueCanvas.transform as RectTransform : null;
        if (!canvasRT) return;

        Camera uiCam = (dialogueCanvas.renderMode == RenderMode.ScreenSpaceOverlay) ? null : dialogueCanvas.worldCamera;
        Camera targetCam = CanvasCameraOf(activeHighlight);

        var corners = new Vector3[4];
        activeHighlight.GetWorldCorners(corners);
        Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
        foreach (var c in corners)
        {
            Vector2 sp = RectTransformUtility.WorldToScreenPoint(targetCam, c);
            min = Vector2.Min(min, sp); max = Vector2.Max(max, sp);
        }

        Vector2 centerSp = (min + max) * 0.5f;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, centerSp, uiCam, out var local))
        {
            highlightFrame.localPosition = local;
            highlightFrame.sizeDelta = (max - min) + new Vector2(16f, 16f);
        }
    }

    static Camera CanvasCameraOf(RectTransform rt)
    {
        var c = rt ? rt.GetComponentInParent<Canvas>() : null;
        if (!c) return null;
        return (c.renderMode == RenderMode.ScreenSpaceOverlay) ? null : c.worldCamera;
    }

    // ===================== Génération auto de l'UI =====================

    // ===================== Modèle d'exemple (à adapter) =====================

    // Petit constructeur d'étape pour garder le modèle lisible.
    static TutorialStep Step(string title, string text,
        TutorialDialoguePosition pos = TutorialDialoguePosition.Top,
        TutorialAdvance advance = TutorialAdvance.ClickToContinue)
        => new() { title = title, text = text, dialoguePosition = pos, advance = advance };

    // Squelette couvrant les 3 sections demandées. Le texte est volontairement
    // synthétique : à enrichir / corriger dans l'Inspector.
    List<TutorialStep> BuildExampleSteps()
    {
        var s = new List<TutorialStep>();

        // ---------- 1) Les dés et les combinaisons ----------
        s.Add(Step("Bienvenue",
            "Bienvenue dans Cosmic Wimpout ! Ce tutoriel t'explique le jeu pas à pas. " +
            "Clique n'importe où pour passer à l'étape suivante."));
        s.Add(Step("Les dés",
            "Le jeu se joue avec des dés spéciaux. Leurs faces valent 2, 3, 4, 5, 6, 10 et le Soleil (☀). " +
            "Certaines faces rapportent des points, d'autres non."));
        s.Add(Step("Les faces qui marquent",
            "Le 5 rapporte 5 points, le 10 en rapporte 10, et le Soleil est la face la plus précieuse. " +
            "Les autres faces ne marquent que si elles forment une combinaison."));
        s.Add(Step("Les combinaisons",
            "Trois faces identiques forment un FLASH : une combinaison qui rapporte gros. " +
            "Nous verrons les Flash, la Clause et le Full en jouant."));

        // ---------- 2) Le déroulement d'un tour ----------
        s.Add(Step("Ton tour",
            "À ton tour, tu lances les dés, tu mets de côté les faces qui marquent, " +
            "puis tu choisis de relancer (pour marquer plus) ou de mettre tes points en banque.",
            TutorialDialoguePosition.Top));

        // Étape interactive : on autorise uniquement RELANCER et on attend l'action.
        var roll = Step("Relance les dés",
            "Appuie sur RELANCER pour lancer les dés une première fois.",
            TutorialDialoguePosition.Bottom, TutorialAdvance.WaitForAction);
        roll.requiredAction = GameAction.Roll;
        s.Add(roll);

        s.Add(Step("La contrainte des 35 points",
            "Pour « entrer » dans la partie, ton premier tour banqué doit atteindre au moins 35 points. " +
            "En dessous, tu ne marques rien : il faut donc parfois oser relancer.",
            TutorialDialoguePosition.Top));
        s.Add(Step("Le risque",
            "Si une relance ne produit aucune face qui marque, tu perds tous les points non banqués du tour. " +
            "Savoir s'arrêter à temps est le cœur du jeu."));
        s.Add(Step("La Clause",
            "La Clause est une règle spéciale liée aux Flash : garde un œil sur les messages en jeu, " +
            "ils t'indiquent quand elle s'applique."));

        // ---------- 3) La campagne ----------
        s.Add(Step("La campagne",
            "En dehors du tutoriel, tu affrontes une série d'adversaires : c'est le mode campagne, " +
            "organisé en paliers de difficulté croissante."));
        s.Add(Step("Les paliers",
            "Chaque palier propose un nouvel adversaire. Le vaincre te fait progresser vers le Boss final."));
        s.Add(Step("Artefacts & Traits",
            "En progressant, tu gagnes des Artefacts (objets aux pouvoirs variés) et des Traits " +
            "(bonus passifs) qui modifient ta façon de jouer."));
        s.Add(Step("Les défaites",
            "Tu peux perdre un certain nombre de manches avant que la campagne ne s'arrête. " +
            "Gère tes risques pour aller le plus loin possible !"));
        s.Add(Step("À toi de jouer",
            "Voilà l'essentiel ! Tu peux relire les règles depuis le menu à tout moment. Bonne partie !",
            TutorialDialoguePosition.Center));

        return s;
    }

    void EnsureEventSystem()
    {
        // La scène de jeu possède déjà un EventSystem ; ceci n'est qu'un filet de sécurité.
        if (FindAnyObjectByType<EventSystem>() != null) return;

        var es = new GameObject("EventSystem", typeof(EventSystem));
        es.transform.SetParent(transform, false);
#if ENABLE_INPUT_SYSTEM
        es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        es.AddComponent<StandaloneInputModule>();
#endif
    }

    void BuildUiIfNeeded()
    {
        if (dialogueCanvas && dialoguePanel && bodyLabel && inputBlocker) return; // déjà câblé à la main

        // Canvas dédié au-dessus de tout.
        var canvasGo = new GameObject("TutorialCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        dialogueCanvas = canvasGo.GetComponent<Canvas>();
        dialogueCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        dialogueCanvas.sortingOrder = sortingOrder;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // Bloqueur plein écran : il BLOQUE le jeu (raycast target) mais ne fait PAS avancer.
        // Seule la boîte de dialogue capte le clic "continuer".
        var blockerGo = new GameObject("InputBlocker", typeof(Image));
        blockerGo.transform.SetParent(canvasGo.transform, false);
        inputBlocker = blockerGo.GetComponent<Image>();
        inputBlocker.color = blockerColor;
        StretchFull((RectTransform)blockerGo.transform);

        // Cadre de surbrillance : TOUJOURS au-dessus de tout (y compris des éléments "keep bright").
        var hlGo = new GameObject("Highlight", typeof(Image));
        hlGo.transform.SetParent(canvasGo.transform, false);
        highlightFrame = (RectTransform)hlGo.transform;
        var hlImg = hlGo.GetComponent<Image>();
        hlImg.color = highlightColor;
        hlImg.raycastTarget = false;
        highlightFrame.anchorMin = highlightFrame.anchorMax = new Vector2(0.5f, 0.5f);
        highlightFrame.pivot = new Vector2(0.5f, 0.5f);
        var hlCanvas = hlGo.AddComponent<Canvas>();
        hlCanvas.overrideSorting = true;
        hlCanvas.sortingOrder = sortingOrder + 2; // > voile (0) et > keep bright (+1)
        hlGo.SetActive(false);

        // Boîte de dialogue.
        var panelGo = new GameObject("DialoguePanel", typeof(Image));
        panelGo.transform.SetParent(canvasGo.transform, false);
        dialoguePanel = (RectTransform)panelGo.transform;
        var panelImg = panelGo.GetComponent<Image>();
        panelImg.color = panelColor;
        panelImg.raycastTarget = false;
        dialoguePanel.sizeDelta = new Vector2(960f, 240f);
        PositionDialogue(TutorialDialoguePosition.Top);

        // Titre : bandeau supérieur (hauteur fixe, ancré en haut).
        titleLabel = MakeText(panelGo.transform, "Title", 34, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        var trt = (RectTransform)titleLabel.transform;
        trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.offsetMin = new Vector2(24, -60); trt.offsetMax = new Vector2(-24, -14);

        // Corps : remplit le centre.
        bodyLabel = MakeText(panelGo.transform, "Body", 26, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        var brt = (RectTransform)bodyLabel.transform;
        brt.anchorMin = new Vector2(0, 0); brt.anchorMax = new Vector2(1, 1);
        brt.offsetMin = new Vector2(24, 40); brt.offsetMax = new Vector2(-24, -64);

        // Invite : coin inférieur droit.
        continueHint = MakeText(panelGo.transform, "Hint", 20, FontStyles.Italic, TextAlignmentOptions.BottomRight);
        var hrt = (RectTransform)continueHint.transform;
        hrt.anchorMin = new Vector2(0, 0); hrt.anchorMax = new Vector2(1, 0);
        hrt.pivot = new Vector2(0.5f, 0f);
        hrt.offsetMin = new Vector2(24, 10); hrt.offsetMax = new Vector2(-16, 40);
        continueHint.color = new Color(textColor.r, textColor.g, textColor.b, 0.75f);
    }

    // Applique les polices choisies (si renseignées) sur les libellés de dialogue.
    void ApplyFonts()
    {
        if (titleFont && titleLabel) titleLabel.font = titleFont;
        if (bodyFont && bodyLabel) bodyLabel.font = bodyFont;
        if (bodyFont && continueHint) continueHint.font = bodyFont;
    }

    // Crée un TMP enfant (ancrage réglé par l'appelant).
    TMP_Text MakeText(Transform parent, string name, float size, FontStyles style, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = align;
        t.color = textColor;
        t.raycastTarget = false;
        return t;
    }

    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
