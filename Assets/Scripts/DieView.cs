using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public enum DieFace { Two = 2, Three = 3, Four = 4, Five = 5, Six = 6, Ten = 10, Sun = 100 }

// Origine du dernier changement de face. Le rendu 3D s'en sert pour distinguer la culbute
// complète d'un jet (le hasard) du quart de tour bref d'une face imposée par un pouvoir :
// l'action des artefacts se lit alors d'un coup d'œil.
public enum DieMotion { None, Rolled, Forced }

// Faces d'un dé STANDARD : pas de SUN (cette face n'existe que sur le dé noir).
[Serializable]
public class DiceSprites {
    public Sprite two, three, four, five, six, ten;
}

// Faces du dé NOIR (Sun) : le SUN remplace le 3 (comme dans le jeu d'origine). Ce dé a donc
// ses propres images pour 2, 4, 5, 6, 10 et SUN — pas de face 3 (elle n'existe plus sur ce dé).
[Serializable]
public class BlackDiceSprites {
    public Sprite two, four, five, six, ten, sun;
}

public class DieView : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("Wiring")]
    public Image faceImage;         // Image enfant "Face" (affiche la face)
    public Image lockIcon;          // Image "LockIn" (cadenas)
    public Image holdHighlight;     // Image "HoldOutline" (outline sélection)

    [Header("Config")]
    public bool isSunDie = false;   // uniquement pour le 5e dé (noir)
    public DiceSprites sprites;

    [Tooltip("Sprites du dé NOIR (die_2_b, die_3_b, …). Utilisés uniquement si isSunDie est coché. " +
             "Dès qu'au moins un est assigné, ils remplacent le shader d'inversion.")]
    public BlackDiceSprites blackSprites;

    // Le dé noir a-t-il son propre jeu d'images ? (sinon on garde l'ancien rendu en négatif)
    bool HasBlackSprites => isSunDie && blackSprites != null &&
        (blackSprites.two || blackSprites.four ||
         blackSprites.five || blackSprites.six || blackSprites.ten || blackSprites.sun);

    // Image du SUN partagée par tous les dés : un dé de BASE peut afficher un Sun posé par un
    // artefact (Miroir, Soleil en bouteille) alors que son jeu de sprites n'en contient pas.
    // Le premier dé noir configuré la publie ici pour éviter une face vide.
    static Sprite _sharedSunSprite;

    [Header("Taille de la face")]
    [Tooltip("Normalise la face à sa taille de référence (celle du prefab), centrée sur le dé. " +
             "L'affichage ne dépend donc ni de la résolution du PNG (300px, 384px…) ni d'un " +
             "LayoutGroup qui redimensionnerait la racine du dé. Décocher pour gérer à la main.")]
    public bool fitFaceToDie = true;
    [Tooltip("Marge (px) entre le bord du dé et la face.")]
    public float faceInset = 0f;
    [Tooltip("Conserve les proportions du PNG. Laisser DÉCOCHÉ (comportement d'origine du prefab) " +
             "pour que TOUTES les faces occupent exactement la même boîte, même si un PNG n'a pas " +
             "le même ratio que les autres (die_5 et die_10 font 384x390 au lieu de 384x384).")]
    public bool preserveFaceAspect = false;

    [Header("Surlignage Flash")]
    // La couleur du cadre de FLASH vient de la palette partagée (GameColors.Flash) pour rester
    // identique au jaune « FLASH » du journal d'actions. Elle n'est donc plus réglable par dé.
    [Tooltip("Épaisseur (px) des barres du cadre intérieur de Flash.")]
    public float flashFrameThickness = 6f;
    [Tooltip("Marge (px) entre le bord du dé et le cadre intérieur.")]
    public float flashFrameInset = 5f;

    bool highlightIsFlash = false;
    GameObject flashFrame; // cadre intérieur généré à la volée (4 barres)

    [Header("Sun Die Visual")]
    [Tooltip("Si coché et isSunDie, affiche la face du dé en couleurs inversées (négatif).")]
    public bool invertSunDieColors = true;
    [Tooltip("Material d'inversion (optionnel). Laisser vide : créé automatiquement via le shader UI/Invert.")]
    public Material sunInvertMaterialOverride;

    static Material _sharedInvertMaterial;

    [Header("State (read-only)")]
    [SerializeField] private DieFace currentFace = DieFace.Two;
    public bool isLocked = false;           // verrou logique (score mis de côté)

    public Action<DieView> onClicked;       // assigne par GameManager

    // Compteur incrémenté à CHAQUE changement de face voulu, MÊME si le dé retombe sur la
    // même valeur. C'est le signal que suit le rendu 3D pour animer : comparer les faces
    // laisserait planté le dé qui refait un 5 après un 5, pendant que les autres culbutent.
    public int MotionTick { get; private set; }
    public DieMotion LastMotion { get; private set; } = DieMotion.None;

    // Instant (Time.unscaledTime) où la culbute en cours se termine, publié par le rendu 3D.
    // Le rendu plat n'anime rien : il y inscrit simplement l'instant du jet (culbute de durée
    // nulle). Ainsi `rollAnimationEndTime` est TOUJOURS renseigné après un jet, et le temps de
    // repos avant l'envoi des dés dans la zone s'applique quel que soit le rendu — sans lui,
    // les dés marquants filaient dans la zone à l'instant même du jet.
    [System.NonSerialized] public float rollAnimationEndTime;
    public bool IsRollAnimating => Time.unscaledTime < rollAnimationEndTime;

    // Instant du dernier jet, indépendant de toute animation. Sert de plancher au temps de
    // repos : un rendu qui n'anime pas ne doit pas supprimer la temporisation du jeu.
    [System.NonSerialized] public float lastRollTime;

    // Vrai tant qu'un déplacement reste DÛ au dé (il doit rejoindre la zone, ou la table, mais
    // le départ est encore retenu). Le dé est alors immobile sans être arrivé : lui seul ne
    // peut pas faire la différence, c'est le GameManager qui l'en informe.
    [System.NonSerialized] public bool placementPending;

    // Instant de fin de la TRANSITION DE PLATEAU en cours (sortie de fin de tour, rentrée au
    // ROLL). Deux usages :
    //   · le dé reste affiché même sans face — le tour est logiquement réinitialisé avant que
    //     les dés aient fini de quitter l'écran, sans quoi ils s'évaporeraient en plein vol ;
    //   · le rendu 3D y applique une inclinaison plus ample qu'au glisser : sur une traversée
    //     d'écran, l'amplitude réglée pour la souris passe inaperçue.
    [System.NonSerialized] public float stageTransitionUntil;
    public bool InStageTransition => Time.unscaledTime < stageTransitionUntil;

    void SignalMotion(DieMotion motion)
    {
        LastMotion = motion;
        MotionTick++;

        // Point de passage unique de TOUS les jets : on y date le lancer. Le rendu 3D écrasera
        // ensuite rollAnimationEndTime par la vraie fin de culbute (valeur plus grande) ; en
        // rendu plat, cette date suffit à faire respecter le temps de repos.
        if (motion == DieMotion.Rolled)
        {
            lastRollTime = Time.unscaledTime;
            if (rollAnimationEndTime < lastRollTime) rollAnimationEndTime = lastRollTime;
        }
    }

    private System.Random rng;

    void Awake()
    {
        rng = new System.Random();

        // Place d'origine sur la table : le dé y revient dès qu'il est désélectionné, et
        // `homeOrder` garde l'ordre gauche→droite quel que soit le conteneur (rangée ou zone).
        homeParent = transform.parent;
        homeOrder = transform.GetSiblingIndex();
        SlotContainer = homeParent;
        baseScale = transform.localScale.x <= 0f ? 1f : transform.localScale.x;

        // Nécessaire au glisser-déposer : on coupe les raycasts du dé pendant qu'il est porté,
        // sinon il se ferait détecter lui-même à la place de la zone qu'il survole.
        cg = GetComponent<CanvasGroup>();
        if (!cg) cg = gameObject.AddComponent<CanvasGroup>();

        // Retrouve l'enfant "Face" si non assigné
        if (faceImage == null)
        {
            var faceTr = transform.Find("Face");
            if (faceTr != null) faceImage = faceTr.GetComponent<Image>();
        }

        // Retrouve d’éventuels enfants nommés par convention si non assignés
        if (lockIcon == null)
        {
            var t = transform.Find("LockIn");
            if (t) lockIcon = t.GetComponent<Image>();
        }
        if (holdHighlight == null)
        {
            var t = transform.Find("HoldOutline");
            if (t) holdHighlight = t.GetComponent<Image>();
        }

        // S’assurer qu’un Graphic sur CE GameObject reçoit les clics
        var selfImg = GetComponent<Image>();
        if (selfImg == null) selfImg = gameObject.AddComponent<Image>();
        selfImg.color = new Color(0, 0, 0, 0); // invisible
        selfImg.raycastTarget = true;

        if (lockIcon)      lockIcon.enabled = false;
        if (holdHighlight)
        {
            holdHighlight.enabled = false;
            holdHighlight.raycastTarget = false;
        }

        // Publie l'image du SUN pour les dés de base (un artefact peut leur poser un Sun).
        if (blackSprites != null && blackSprites.sun) _sharedSunSprite = blackSprites.sun;

        ApplyFaceFit();
        ApplySunDieMaterial();
    }

    // Applique le material "négatif" sur la face du 5e dé (isSunDie), pour le différencier visuellement.
    void ApplySunDieMaterial()
    {
        if (faceImage == null) return;

        // Le dé noir a ses propres images (die_*_b) : plus besoin du négatif, et on retire
        // explicitement le material pour ne pas ré-inverser des sprites déjà noirs.
        if (HasBlackSprites) { faceImage.material = null; return; }

        if (!isSunDie || !invertSunDieColors) return;

        if (sunInvertMaterialOverride != null)
        {
            faceImage.material = sunInvertMaterialOverride;
            return;
        }

        if (_sharedInvertMaterial == null)
        {
            var sh = Shader.Find("UI/Invert");
            if (sh != null)
                _sharedInvertMaterial = new Material(sh) { name = "UIInvert (auto)" };
            else
                Debug.LogWarning("DieView: shader 'UI/Invert' introuvable. Ajoute-le à 'Always Included Shaders' (Project Settings > Graphics) pour les builds.");
        }

        if (_sharedInvertMaterial != null)
            faceImage.material = _sharedInvertMaterial;
    }

    public void ResetForNewTurn()
    {
        isLocked = false;
        SetFrozen(false);
        SetFlashHighlight(false);
        SetClauseSelected(false); // annule le contour vert de CLAUSE
        SetDimmed(false); // annule un éventuel grisage (dé d'ajout réutilisé)
        if (lockIcon)      lockIcon.enabled = false;
        if (holdHighlight) holdHighlight.enabled = false;
        SetFaceSprite(null); // efface l’affichage jusqu’au premier roll

        SetDragOnTop(false); // filet : un glisser interrompu par un changement de tour

        // StopAllCoroutines couperait un vol en cours en laissant le dé accroché au canvas
        // et son fantôme dans la zone : on le fait atterrir proprement avant.
        AbortFlight();
        StopAllCoroutines();
        _scaleCo = null; _bouncing = false;
        transform.localScale = Vector3.one * baseScale;
    }

    public void Roll()
    {
        // Dé noir (Sun) : comme dans le jeu d'origine, la face 3 est remplacée par un SUN.
        // Faces : 2, SUN, 4, 5, 6, 10 (pas de 3 ; le 10 existe, en noir via die_10_b).
        var pool = isSunDie
            ? new List<DieFace> { DieFace.Two, DieFace.Sun, DieFace.Four, DieFace.Five, DieFace.Six, DieFace.Ten }
            : new List<DieFace> { DieFace.Two, DieFace.Three, DieFace.Four, DieFace.Five, DieFace.Six, DieFace.Ten };

        int idx = rng.Next(0, pool.Count);
        currentFace = pool[idx];
        SetFaceSprite(currentFace);
        SignalMotion(DieMotion.Rolled);
        Pulse(0.28f, 1.10f);

        // Un signalement par dé lancé : DiceSfx additionne ceux de la frame et joue UN son
        // choisi sur le total (1 dé / 2-3 / 4-5). Ça couvre d'un coup les ~20 endroits où
        // GameManager lance des dés, sans avoir à les instrumenter un par un.
        DiceSfx.NotifyRoll();
    }

    public DieFace GetFace() => currentFace;
    
    // `asRoll` : ce changement de face TIENT LIEU de jet (faces forcées du tutoriel, du mode
    // dev ou d'un trait) plutôt que d'être un pouvoir qui retourne un dé déjà posé. La règle
    // sur les sites d'appel est simple : c'est un jet partout où DiceSfx.NotifyRoll() suit.
    public void SetFace(DieFace newFace, bool asRoll = false)
    {
        // public setter de face pour les pouvoirs
        // (DieView expose déjà SetFaceSprite et Pulse)
        // -> cf. structure actuelle, sprites & Pulse existent. 
        //    (tu as déjà Roll(), GetFace(), ResetForNewTurn(), etc.)
        //    On suit la même logique d’update visuelle. 
        //    :contentReference[oaicite:1]{index=1}
        var was = currentFace;
        currentFace = newFace;
        SetFaceSprite(currentFace);
        SignalMotion(asRoll ? DieMotion.Rolled : DieMotion.Forced);
        Pulse(0.22f, 1.08f);
    }

    public void SetLocked(bool locked)
    {
        isLocked = locked;
        // La sélection n'est PLUS signalée par un calque de couleur sur le dé : c'est sa
        // présence dans la zone de sélection qui la porte (voir DiceDropZone). Le contour
        // n'est donc plus qu'un support pour le vert de CLAUSE.
        if (!locked)
        {
            SetClauseSelected(false); // le vert de CLAUSE ne survit pas au déverrouillage
            SetFrozen(false);
            if (lockIcon) lockIcon.enabled = false;
            if (holdHighlight) holdHighlight.enabled = false;
        }
    }

    // Un dé verrouillé est-il DÉFINITIF (Flash, jet précédent) plutôt qu'annulable ?
    // État logique seulement : le cadenas ne s'affiche plus, la place du dé dans la zone de
    // sélection suffit à dire qu'il est gardé.
    bool frozenVisual;
    public bool IsFrozenVisual => frozenVisual;

    public void SetFrozen(bool frozen)
    {
        frozenVisual = frozen;
        if (lockIcon) lockIcon.enabled = false;
    }

    // Contour VERT sur le dé qui dégage une CLAUSE (même vert que « CLAUSE » dans le journal).
    // On teinte le contour de sélection existant : la couleur d'origine est restaurée ensuite.
    bool clauseSelected = false;
    Color holdOriginalColor;
    bool holdColorCached = false;

    // Le contour vert n'apparaît qu'une fois le dé POSÉ. Allumé à l'instant de la sélection,
    // il accompagnait le dé pendant tout son vol vers la zone et arrivait trop tôt.
    // L'extinction, elle, est toujours immédiate : attraper un dé doit le dépouiller net.
    bool clausePending;
    float clausePendingSince;
    const float ClauseSettleGrace = 0.18f; // laisse au vol le temps de démarrer

    public void SetClauseSelected(bool on)
    {
        if (holdHighlight == null) return;

        if (!on)
        {
            clausePending = false;
            if (!clauseSelected) return;
            clauseSelected = false;
            if (holdColorCached) holdHighlight.color = holdOriginalColor;
            holdHighlight.enabled = false;
            return;
        }

        if (clauseSelected || clausePending) return;
        clausePending = true;
        clausePendingSince = Time.unscaledTime;
    }

    // Appelé depuis Update et à l'atterrissage : le contour s'allume dès que le dé est
    // immobile — ni porté, ni en vol, ni en train de culbuter.
    void TryApplyClauseOutline()
    {
        if (!clausePending || holdHighlight == null) return;
        if (dragging || IsFlying || IsRollAnimating || placementPending) return;
        if (Time.unscaledTime - clausePendingSince < ClauseSettleGrace) return;

        clausePending = false;
        clauseSelected = true;
        if (!holdColorCached) { holdOriginalColor = holdHighlight.color; holdColorCached = true; }
        holdHighlight.color = GameColors.Clause;
        holdHighlight.enabled = true;
    }

    // Grise le dé SANS le verrouiller : ni cadenas, ni contour de sélection bleu — juste
    // une face assombrie. Sert au dé d'ajout non marquant, pour montrer qu'il ne compte
    // PAS dans le score (le contour bleu laissait croire qu'il était retenu).
    public void SetDimmed(bool dimmed)
    {
        if (lockIcon) lockIcon.enabled = false;
        if (holdHighlight) holdHighlight.enabled = false;
        // Grisé mais OPAQUE (alpha 1) : l'ancienne alpha 0.75 donnait un rendu « transparent »
        // qui semblait buggé sur le dé d'ajout. On assombrit sans laisser voir à travers.
        if (faceImage) faceImage.color = dimmed ? new Color(0.5f, 0.5f, 0.5f, 1f) : Color.white;

        // Un dé grisé n'est plus interactif.
        var selfImg = GetComponent<Image>();
        if (selfImg) selfImg.raycastTarget = !dimmed;
    }

    // Le dé fait-il partie d'un FLASH ? Un CADRE INTÉRIEUR orange épais s'affiche alors,
    // en plus du contour de sélection bleu (qui garde sa couleur de scène).
    // Piloté par GameManager (joueur ET adversaire).
    // L'allumage attend la fin de la culbute : posé pendant que le dé tourne encore, le cadre
    // annonçait le Flash avant même qu'on ait pu lire les faces. L'extinction reste immédiate.
    bool flashWanted;

    public void SetFlashHighlight(bool isFlash)
    {
        flashWanted = isFlash;
        if (!isFlash) ApplyFlashFrame(false);
        else TryApplyFlashFrame();
    }

    void TryApplyFlashFrame()
    {
        if (!flashWanted || highlightIsFlash || IsRollAnimating) return;
        ApplyFlashFrame(true);
    }

    void ApplyFlashFrame(bool on)
    {
        if (highlightIsFlash == on) return;
        highlightIsFlash = on;

        if (on && flashFrame == null) BuildFlashFrame();
        if (flashFrame) flashFrame.SetActive(on);
    }

    // Construit le cadre intérieur : 4 barres orange collées aux bords (avec marge),
    // rendues AU-DESSUS de la face du dé.
    void BuildFlashFrame()
    {
        flashFrame = new GameObject("FlashInnerFrame", typeof(RectTransform));
        var frt = (RectTransform)flashFrame.transform;
        frt.SetParent(transform, false);
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(flashFrameInset, flashFrameInset);
        frt.offsetMax = new Vector2(-flashFrameInset, -flashFrameInset);
        flashFrame.transform.SetAsLastSibling();

        float t = Mathf.Max(1f, flashFrameThickness);
        CreateFrameBar("Top",    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),   new Vector2(0f, t));
        CreateFrameBar("Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),   new Vector2(0f, t));
        CreateFrameBar("Left",   new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),   new Vector2(t, 0f));
        CreateFrameBar("Right",  new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),   new Vector2(t, 0f));
    }

    void CreateFrameBar(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size)
    {
        var bar = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)bar.transform;
        rt.SetParent(flashFrame.transform, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;

        var img = bar.AddComponent<Image>();
        img.color = GameColors.Flash; // même jaune que « FLASH » dans le journal
        img.raycastTarget = false;
    }

    // Face d'un dé standard (pas de SUN dans ce jeu).
    static Sprite FromNormal(DiceSprites set, DieFace face)
    {
        if (set == null) return null;
        switch (face)
        {
            case DieFace.Two:   return set.two;
            case DieFace.Three: return set.three;
            case DieFace.Four:  return set.four;
            case DieFace.Five:  return set.five;
            case DieFace.Six:   return set.six;
            case DieFace.Ten:   return set.ten;
        }
        return null;
    }

    // Face du dé noir (Sun) : 2, 4, 5, 6, 10, SUN (le SUN remplace le 3 ; pas de face 3 au tirage).
    static Sprite FromBlack(BlackDiceSprites set, DieFace face)
    {
        if (set == null) return null;
        switch (face)
        {
            case DieFace.Two:   return set.two;
            case DieFace.Four:  return set.four;
            case DieFace.Five:  return set.five;
            case DieFace.Six:   return set.six;
            case DieFace.Ten:   return set.ten;
            case DieFace.Sun:   return set.sun;
        }
        return null;
    }

    void SetFaceSprite(DieFace? face)
    {
        if (faceImage == null) return;
        if (!face.HasValue) { faceImage.sprite = null; return; }

        // 1) Dé noir → son jeu d'images dédié.
        Sprite s = null;
        if (HasBlackSprites) s = FromBlack(blackSprites, face.Value);
        // 2) Sinon (ou face absente du jeu noir, ex: un 10 posé par un artefact) → jeu standard.
        if (s == null) s = FromNormal(sprites, face.Value);
        // 3) Un SUN sur un dé de base (Miroir, Soleil en bouteille) : image partagée du dé noir.
        if (s == null && face.Value == DieFace.Sun) s = _sharedSunSprite;
        faceImage.sprite = s;

        // Assure l’affichage même si le parent a un Image transparent
        faceImage.enabled = true;
        ApplyFaceFit();
    }

    // Fait épouser à la face la taille du dé : l'affichage ne dépend plus de la résolution
    // du PNG (300px, 384px…), seule la boîte du dé compte. preserveAspect évite la déformation.
    // Taille de référence de la face, capturée au démarrage (avant toute mise en page).
    Vector2 _faceBaseSize;
    bool _faceBaseCaptured;

    void CaptureFaceBaseSize()
    {
        if (_faceBaseCaptured || faceImage == null) return;
        _faceBaseCaptured = true;

        _faceBaseSize = faceImage.rectTransform.rect.size;
        // Replis si la face n'est pas encore dimensionnée : taille du dé, puis 160 (valeur du prefab).
        if (_faceBaseSize.x <= 1f || _faceBaseSize.y <= 1f) _faceBaseSize = ((RectTransform)transform).rect.size;
        if (_faceBaseSize.x <= 1f || _faceBaseSize.y <= 1f) _faceBaseSize = new Vector2(160f, 160f);
    }

    void ApplyFaceFit()
    {
        if (faceImage == null) return;
        faceImage.type = Image.Type.Simple;
        faceImage.preserveAspect = preserveFaceAspect;

        if (!fitFaceToDie) return;
        CaptureFaceBaseSize();

        // Ancres CENTRÉES + taille explicite (et non un stretch sur la racine) : la face garde
        // sa taille de référence même si un LayoutGroup/ContentSizeFitter redimensionne la racine
        // du dé — sinon elle grossirait et déborderait du contour, qui lui reste fixe.
        var rt = faceImage.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = _faceBaseSize - new Vector2(faceInset * 2f, faceInset * 2f);
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
    }

    // --- petite anim pulse ---
    // Pulse et Bounce partagent un même handle : une seule animation d'échelle à la fois
    // (évite qu'un Pulse déclenché par SetFace/Roll et un rebond de sélection ne se combattent).
    Coroutine _scaleCo;
    bool _bouncing; // un rebond de sélection est en cours → prioritaire sur un Pulse

    // Coupé par le rendu 3D : le cube joue déjà son propre impact à l'atterrissage, et lui
    // le fait EN CASCADE. Garder les deux ferait sursauter les cinq dés d'un coup pendant
    // qu'ils culbutent l'un après l'autre — deux impacts pour un seul lancer.
    [System.NonSerialized] public bool suppressScalePulse;

    public void Pulse(float duration = 0.5f, float peakScale = 1.12f)
    {
        if (suppressScalePulse) return;
        if (!gameObject.activeInHierarchy) return;
        if (_bouncing) return; // laisse le rebond de sélection se jouer entièrement
        if (dragging || IsFlying) return; // pas d'animation d'échelle pendant un déplacement
        if (_scaleCo != null) StopCoroutine(_scaleCo);
        _scaleCo = StartCoroutine(PulseRoutine(duration, peakScale));
    }
    System.Collections.IEnumerator PulseRoutine(float duration, float peak)
    {
        var t = 0f;
        var half = duration * 0.5f;
        // Toutes les animations partent de baseScale : dans la zone de sélection les dés sont
        // réduits, un retour brutal à 1 les ferait « sauter » de taille.
        var start = Vector3.one * baseScale;
        var up = Vector3.one * (baseScale * peak);

        while (t < half) { t += Time.deltaTime; transform.localScale = Vector3.Lerp(start, up, t/half); yield return null; }
        t = 0f;
        while (t < half) { t += Time.deltaTime; transform.localScale = Vector3.Lerp(up, start, t/half); yield return null; }
        transform.localScale = start;
        _scaleCo = null;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        onClicked?.Invoke(this);
    }

    // ==== Ciblage de dé par un artefact : contour brillant au survol + rebond à la sélection ====
    // pickHoverEnabled est piloté par GameManager quand un artefact attend qu'on désigne un dé.
    [System.NonSerialized] public bool pickHoverEnabled;
    GameObject pickGlow;
    Image[] pickGlowBars;
    Coroutine pickGlowHandle;

    // Couleurs du dégradé du halo (haut → bas) : un bleu brillant vers cyan.
    static readonly Color GlowTop = new Color(0.42f, 0.85f, 1f);   // #6BD9FF
    static readonly Color GlowBottom = new Color(0.30f, 0.56f, 0.89f); // #4C90E3

    public void SetPickHoverEnabled(bool on)
    {
        pickHoverEnabled = on;
        if (!on) ShowPickGlow(false);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovering = true;
        if (pickHoverEnabled) ShowPickGlow(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovering = false;
        ShowPickGlow(false);
    }

    void ShowPickGlow(bool on)
    {
        if (on && pickGlow == null) BuildPickGlow();
        if (pickGlow) pickGlow.SetActive(on);

        if (on)
        {
            if (pickGlowHandle == null && gameObject.activeInHierarchy)
                pickGlowHandle = StartCoroutine(PickGlowPulse());
        }
        else if (pickGlowHandle != null)
        {
            StopCoroutine(pickGlowHandle);
            pickGlowHandle = null;
        }
    }

    void BuildPickGlow()
    {
        pickGlow = new GameObject("PickGlow", typeof(RectTransform));
        var rt = (RectTransform)pickGlow.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-6f, -6f); // déborde un peu → effet de halo
        rt.offsetMax = new Vector2(6f, 6f);
        pickGlow.transform.SetAsLastSibling();

        const float th = 5f;
        pickGlowBars = new Image[4];
        // Barres haut/bas prennent la couleur du dégradé aux extrémités ; gauche/droite une teinte médiane.
        var mid = Color.Lerp(GlowTop, GlowBottom, 0.5f);
        pickGlowBars[0] = MakeGlowBar(rt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, th), GlowTop);
        pickGlowBars[1] = MakeGlowBar(rt, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, th), GlowBottom);
        pickGlowBars[2] = MakeGlowBar(rt, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(th, 0f), mid);
        pickGlowBars[3] = MakeGlowBar(rt, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(th, 0f), mid);
    }

    static Image MakeGlowBar(RectTransform parent, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 size, Color col)
    {
        var bar = new GameObject("GlowBar", typeof(RectTransform));
        var rt = (RectTransform)bar.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
        rt.sizeDelta = size; rt.anchoredPosition = Vector2.zero;
        var img = bar.AddComponent<Image>();
        img.color = col;
        img.raycastTarget = false;
        return img;
    }

    // Pulse d'opacité du halo pour un effet « brillant ».
    System.Collections.IEnumerator PickGlowPulse()
    {
        while (true)
        {
            float a = Mathf.Lerp(0.4f, 1f, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4.5f));
            if (pickGlowBars != null)
                foreach (var b in pickGlowBars)
                    if (b) { var c = b.color; c.a = a; b.color = c; }
            yield return null;
        }
    }

    // Rebond joué quand le dé est SÉLECTIONNÉ par un artefact (gonfle puis revient en oscillant).
    public void Bounce()
    {
        if (!gameObject.activeInHierarchy) return;
        if (dragging || IsFlying) return;
        if (_scaleCo != null) StopCoroutine(_scaleCo);
        _bouncing = true;
        _scaleCo = StartCoroutine(BounceRoutine());
    }

    System.Collections.IEnumerator BounceRoutine()
    {
        const float dur = 0.4f;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / dur;
            // Oscillation amortie autour de baseScale : rebond visible qui se stabilise.
            float s = 1f + 0.30f * Mathf.Sin(t * Mathf.PI * 3f) * (1f - t);
            transform.localScale = Vector3.one * (baseScale * s);
            yield return null;
        }
        transform.localScale = Vector3.one * baseScale;
        _bouncing = false;
        _scaleCo = null;
    }

    // ==================================================================================
    // ============== GLISSER-DÉPOSER : sélection des dés par la drop-zone ===============
    // ==================================================================================
    // Le dé n'est plus « sélectionné » par un calque de couleur : on le TIRE physiquement
    // dans la DiceDropZone. L'inverse (le sortir de la zone) le désélectionne. Les dés que
    // le jeu sélectionne tout seul (Flash) y sont envoyés par le même vol animé.

    [Header("Glisser-déposer")]
    [Tooltip("Grossissement du dé pendant qu'on le porte.")]
    public float dragScaleBoost = 1.18f;
    [Tooltip("Inclinaison (degrés) prise par le dé selon la vitesse horizontale du curseur.")]
    public float dragTiltPerVelocity = 0.9f;
    public float dragMaxTilt = 16f;
    [Tooltip("Durée (s) du vol vers la zone de sélection ou vers la table.")]
    public float flightDuration = 0.28f;
    [Tooltip("Avance (s) du son de pose sur la FIN du vol. Le vol freine en fin de course : " +
             "l'œil considère le dé arrivé bien avant sa dernière frame, et un clac calé pile " +
             "sur l'atterrissage s'entend en retard. Bornée à la durée du vol — au-delà, le son " +
             "part au décollage (0 = pile à l'atterrissage).")]
    public float landSoundLead = 0.5f;
    [Tooltip("Grossissement au survol d'un dé attrapable.")]
    public float hoverScaleBoost = 1.07f;

    // Les sons de dés ne sont plus des clips posés sur chaque prefab : ils vivent dans
    // DiceSfxLibrary (un seul asset), et c'est DiceSfx qui choisit le bon selon le NOMBRE
    // de dés concernés — un dé attrapé et cinq dés attrapés ne sonnent pas pareil.

    // Éléments d'interface sous lesquels le dé doit rester quand il est soulevé sur le canvas
    // pour être transporté (portraits des joueurs, panneaux…). Vide : il passe au-dessus de
    // tout. C'est la MÊME liste que celle du GameManager, pas une copie : la modifier dans
    // l'Inspector en cours de partie prend effet immédiatement.
    [System.NonSerialized] public System.Collections.Generic.IList<RectTransform> renderBelow;

    // Câblage posé par GameManager (comme onClicked).
    [System.NonSerialized] public DiceDropZone selectionZone;
    // Ce dé peut-il être attrapé maintenant ? (tour du joueur, phase, dé non figé…)
    [System.NonSerialized] public System.Func<DieView, bool> canDragCheck;
    // Le dépôt dans la zone serait-il accepté ? Sert au retour visuel vert/rouge.
    [System.NonSerialized] public System.Func<DieView, bool> canDropInZoneCheck;
    // Dépôt effectif : (dé, déposé dans la zone ?). GameManager applique la sélection puis
    // resynchronise les placements — c'est lui qui décide où le dé atterrit.
    [System.NonSerialized] public System.Action<DieView, bool> onSelectionDrop;

    // Place d'origine sur la table + rang stable des slots.
    [System.NonSerialized] public Transform homeParent;
    [System.NonSerialized] public int homeOrder;

    // Le dé appartient-il au FLASH en cours ? (piloté par SetFlashHighlight)
    public bool IsFlashDie => highlightIsFlash;

    // Rang de tri du dé dans un conteneur (rangée ou zone de sélection).
    // Les dés d'un FLASH forment un BLOC CONTIGU en tête : sans ça, un dé marquant gardé
    // en plus du Flash pouvait s'intercaler entre deux dés du Flash (l'ordre suivait la
    // seule position d'origine sur la table). À l'intérieur d'un groupe, l'ordre d'origine
    // gauche→droite est conservé.
    public int SlotOrder => homeOrder + (highlightIsFlash ? 0 : DieSlotHolder.GroupStride);

    // Conteneur LOGIQUE du dé (rangée ou zone), y compris pendant un vol : sert à savoir
    // s'il est déjà à sa place sans être trompé par le reparentage temporaire au canvas.
    public Transform SlotContainer { get; private set; }
    public bool IsDragging => dragging;
    public bool IsFlying => _flightCo != null;

    CanvasGroup cg;
    bool _soundOnLand;
    float baseScale = 1f;
    bool dragging;
    bool hovering;
    Camera dragCam;
    float tiltTargetZ;
    DieSlotHolder slotHolder;      // fantôme réservant la place pendant le transport
    Coroutine _flightCo;

    Canvas RootCanvas()
    {
        var c = GetComponentInParent<Canvas>();
        return c ? c.rootCanvas : null;
    }

    // Sort le dé de sa rangée pour le transporter par-dessus l'interface, mais SOUS les
    // éléments déclarés dans renderBelow. Dans uGUI l'ordre de dessin suit l'ordre des
    // enfants : se placer juste avant un élément revient à passer dessous.
    //
    // `aboveEverything` : EXCEPTION assumée pour le dé PORTÉ à la souris. Un dé qu'on tient
    // doit rester visible quoi qu'il traverse — renderBelow est ignoré et le dé passe devant
    // tout le monde (voir aussi SetDragOnTop pour les panneaux à Canvas trié).
    void RaiseToTravelLayer(Canvas canvas, bool aboveEverything = false)
    {
        if (!canvas) return;
        if (transform.parent != canvas.transform) transform.SetParent(canvas.transform, true);

        if (aboveEverything) { transform.SetAsLastSibling(); return; }

        int ceiling = -1;
        if (renderBelow != null)
        {
            foreach (var target in renderBelow)
            {
                var top = TopLevelUnder(canvas.transform, target ? target.transform : null);
                if (!top || top == transform) continue;
                int idx = top.GetSiblingIndex();
                if (ceiling < 0 || idx < ceiling) ceiling = idx;
            }
        }

        if (ceiling < 0) transform.SetAsLastSibling();
        else transform.SetSiblingIndex(ceiling);
    }

    // Remonte jusqu'à l'enfant DIRECT du canvas : c'est lui qui porte l'ordre de dessin, quelle
    // que soit la profondeur à laquelle se trouve l'élément visé. On peut donc désigner un
    // objet imbriqué sans avoir à connaître la hiérarchie.
    static Transform TopLevelUnder(Transform canvasRoot, Transform t)
    {
        while (t && t.parent != canvasRoot) t = t.parent;
        return t;
    }

    [Header("Dé porté (glisser)")]
    [Tooltip("Ordre de tri appliqué au dé PENDANT qu'on le porte. Il doit passer au-dessus de " +
             "TOUTE l'interface — panneaux, inventaire, infobulles — y compris ceux qui ont " +
             "leur propre Canvas trié : l'ordre de fratrie ne suffit pas contre eux.")]
    public int dragSortingOrder = 32700;

    Canvas dragSortCanvas;

    // Un panneau qui se trie lui-même (Canvas + overrideSorting : inventaire, infobulle…)
    // passe devant le dé quel que soit son rang de fratrie. Le temps du transport, le dé
    // prend donc SON propre ordre de tri, volontairement plus élevé que tous les autres.
    void SetDragOnTop(bool on)
    {
        if (on)
        {
            if (!dragSortCanvas)
            {
                dragSortCanvas = GetComponent<Canvas>();
                if (!dragSortCanvas) dragSortCanvas = gameObject.AddComponent<Canvas>();
                // Un Canvas imbriqué a besoin de son propre raycaster pour rester survolable
                // et cliquable (même précaution que les vignettes de traits).
                if (!GetComponent<GraphicRaycaster>()) gameObject.AddComponent<GraphicRaycaster>();
            }
            dragSortCanvas.overrideSorting = true;
            dragSortCanvas.sortingOrder = dragSortingOrder;
        }
        else if (dragSortCanvas)
        {
            // On rend le dé à l'ordre de sa hiérarchie : le Canvas reste en place (inerte),
            // le retirer en cours de partie ferait clignoter le rendu.
            dragSortCanvas.overrideSorting = false;
            dragSortCanvas.sortingOrder = 0;
        }
    }

    bool CanDragNow() => canDragCheck != null && canDragCheck(this);

    // ---- Placement demandé par GameManager ------------------------------------------------
    // Envoie le dé vers un conteneur (la zone de sélection ou la table). `delay` permet
    // d'échelonner l'envol de plusieurs dés (Flash) pour un effet en cascade.
    // `durationOverride` : vol plus lent que la valeur du dé, pour les longues traversées
    // d'écran (entrée/sortie de plateau). Négatif = on garde flightDuration.
    // `easeIn` : le dé ACCÉLÈRE au lieu de freiner. Réservé aux sorties d'écran — un vol
    // classique freine en fin de course, si bien qu'un dé qui s'en va franchit la zone
    // visible en une fraction de seconde puis rampe hors champ. À l'inverse, il reste
    // visible presque tout le vol et son mouvement se lit.
    // `soundOnLand` : le dé joue DropDie en ARRIVANT. Réservé aux envois décidés par le jeu
    // (Flash, retour à la table) : l'échelonnement des vols fait alors la cascade tout seul,
    // un clac par dé. Un dé lâché par le joueur, lui, a déjà sonné au lâcher — le refaire
    // sonner à l'atterrissage doublerait le bruit d'un seul geste.
    public void MoveToContainer(Transform container, float targetScale, bool animate, float delay = 0f, float durationOverride = -1f, bool easeIn = false, bool soundOnLand = false)
    {
        if (!container) return;

        // Un dé qui QUITTE la zone de sélection se dépouille AUSSITÔT de ses contours : le cadre
        // jaune du Flash et le vert de CLAUSE marquent une sélection en cours, pas le dé
        // lui-même. Les garder pendant le trajet de retour les faisait traîner jusque sur la
        // table de départ, alors que le Flash n'existait plus.
        if (LeavingSelectionZone(container)) ClearSelectionOutlines();

        SlotContainer = container;
        _soundOnLand = soundOnLand && animate;

        if (!animate || !gameObject.activeInHierarchy)
        {
            AbortFlight();
            SnapInto(container, targetScale);
            return;
        }

        if (_flightCo != null) { StopCoroutine(_flightCo); _flightCo = null; }

        // Le fantôme réserve la case d'arrivée : la rangée s'ouvre pendant que le dé vole.
        if (slotHolder) slotHolder.MoveTo(container, SelfSize(), targetScale, SlotOrder);
        else slotHolder = DieSlotHolder.Create(container, SlotOrder, SelfSize(), targetScale);

        _flightCo = StartCoroutine(FlyRoutine(container, targetScale, delay, durationOverride, easeIn));
    }

    // Le dé est-il en train de SORTIR de la zone de sélection ? (il y est, et on l'envoie
    // ailleurs). Un dé qui se déplace À L'INTÉRIEUR de la zone garde évidemment ses contours.
    bool LeavingSelectionZone(Transform destination)
    {
        if (!selectionZone) return false;
        Transform zone = selectionZone.DiceContainer;
        return zone && SlotContainer == zone && destination != zone;
    }

    // Retire d'un coup tous les contours portés par la sélection : cadre jaune du Flash et
    // contour vert de la CLAUSE (y compris s'il était encore « en attente d'allumage »).
    public void ClearSelectionOutlines()
    {
        SetFlashHighlight(false);
        SetClauseSelected(false);
    }

    Vector2 SelfSize()
    {
        var r = ((RectTransform)transform).rect.size;
        return (r.x > 1f && r.y > 1f) ? r : new Vector2(160f, 160f);
    }

    System.Collections.IEnumerator FlyRoutine(Transform container, float targetScale, float delay, float durationOverride, bool easeIn)
    {
        var rt = (RectTransform)transform;

        // Le dé voyage par-dessus l'interface, mais sous les éléments déclarés (renderBelow).
        RaiseToTravelLayer(RootCanvas());

        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);

        // Force le calcul de la mise en page pour que le fantôme soit déjà à sa place :
        // sinon le dé viserait le centre du conteneur pendant la première frame.
        if (container is RectTransform crt) LayoutRebuilder.ForceRebuildLayoutImmediate(crt);

        Vector3 startPos = rt.position;
        float startScale = rt.localScale.x;
        float startTilt = rt.localEulerAngles.z;
        float t = 0f;
        float dur = Mathf.Max(0.05f, durationOverride > 0f ? durationOverride : flightDuration);

        // Instant du clac, compté depuis le début du vol. Une avance ≥ dur le ramène à 0 :
        // le son part avec le dé. On ne peut pas aller plus tôt sans devancer le mouvement.
        float soundAt = Mathf.Max(0f, dur - Mathf.Max(0f, landSoundLead));

        while (t < 1f && slotHolder)
        {
            if (_soundOnLand && t * dur >= soundAt) { _soundOnLand = false; DiceSfx.Drop(1); }
            t += Time.unscaledDeltaTime / dur;
            float k = Mathf.Clamp01(t);
            float e = easeIn ? EaseInCubic(k) : EaseOutBack(k);
            rt.position = Vector3.LerpUnclamped(startPos, slotHolder.transform.position, e);
            rt.localScale = Vector3.one * Mathf.Lerp(startScale, targetScale, EaseOutCubic(k));
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpAngle(startTilt, 0f, EaseOutCubic(k)));
            yield return null;
        }

        _flightCo = null;
        Land(container, targetScale);
    }

    // Pose le dé dans la case réservée par son fantôme, puis libère ce dernier.
    void Land(Transform container, float targetScale)
    {
        int idx = slotHolder ? slotHolder.transform.GetSiblingIndex()
                             : DieSlotHolder.SlotIndexFor(container, SlotOrder);
        if (slotHolder) { slotHolder.Release(); slotHolder = null; }

        SnapInto(container, targetScale, idx);
        if (_soundOnLand) { _soundOnLand = false; DiceSfx.Drop(1); } // filet : vol écourté
        Pulse(0.16f, 1.08f); // petit rebond d'arrivée
    }

    void SnapInto(Transform container, float targetScale, int siblingIndex = -1)
    {
        if (siblingIndex < 0) siblingIndex = DieSlotHolder.SlotIndexFor(container, SlotOrder);

        var rt = (RectTransform)transform;
        rt.SetParent(container, false);
        rt.SetSiblingIndex(siblingIndex);
        rt.anchoredPosition = Vector2.zero;
        rt.localRotation = Quaternion.identity;
        baseScale = targetScale;
        rt.localScale = Vector3.one * targetScale;
        SlotContainer = container;
    }

    // Interrompt un vol en cours sans laisser de fantôme orphelin ni de dé accroché au canvas.
    public void AbortFlight()
    {
        _soundOnLand = false; // vol interrompu : pas d'atterrissage, donc pas de clac
        if (_flightCo != null) { StopCoroutine(_flightCo); _flightCo = null; }
        if (slotHolder)
        {
            var container = slotHolder.transform.parent;
            int idx = slotHolder.transform.GetSiblingIndex();
            slotHolder.Release(); slotHolder = null;
            if (container) SnapInto(container, baseScale, idx);
        }
        else if (SlotContainer && transform.parent != SlotContainer)
        {
            SnapInto(SlotContainer, baseScale);
        }
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float p = x - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }

    static float EaseOutCubic(float x) => 1f - Mathf.Pow(1f - x, 3f);

    static float EaseInCubic(float x) { float k = Mathf.Clamp01(x); return k * k * k; }

    // ---- Refus : petite secousse pour dire « pas celui-là » --------------------------------
    public void ShakeRefusal()
    {
        if (!gameObject.activeInHierarchy) return;
        DiceSfx.Refuse();
        StartCoroutine(ShakeRoutine());
    }

    System.Collections.IEnumerator ShakeRoutine()
    {
        var rt = (RectTransform)transform;
        Vector2 origin = rt.anchoredPosition;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / 0.25f;
            if (dragging || IsFlying) yield break; // le joueur a repris la main
            rt.anchoredPosition = origin + new Vector2(Mathf.Sin(t * Mathf.PI * 6f) * 9f * (1f - t), 0f);
            yield return null;
        }
        rt.anchoredPosition = origin;
    }

    // ---- Handlers de drag ------------------------------------------------------------------
    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!CanDragNow()) return;

        AbortFlight();
        ShowPickGlow(false);
        SetClauseSelected(false); // un dé qu'on attrape se dépouille immédiatement

        var canvas = RootCanvas();
        dragCam = (canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

        // Le fantôme garde la place actuelle : sans lui, la rangée se refermerait sous le
        // curseur et le dé n'aurait plus de trou où revenir si le dépôt est refusé.
        var container = SlotContainer ? SlotContainer : transform.parent;
        slotHolder = DieSlotHolder.Create(container, SlotOrder, SelfSize(), baseScale);

        // Un dé PORTÉ passe au-dessus de toute l'interface (exception assumée).
        RaiseToTravelLayer(canvas, aboveEverything: true);
        SetDragOnTop(true);

        // Le LayoutGroup avait ancré le dé en haut-gauche : on recentre ancres et pivot,
        // sinon FollowPointer (qui raisonne au centre du canvas) le projetterait hors écran.
        var drt = (RectTransform)transform;
        drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 0.5f);
        drt.pivot = new Vector2(0.5f, 0.5f);

        if (cg) cg.blocksRaycasts = false;

        dragging = true;
        tiltTargetZ = 0f;
        // Le dé reprend sa taille de table (et un peu plus) le temps d'être porté : un dé
        // sorti de la zone, où il est réduit, ne reste pas minuscule dans la main.
        transform.localScale = Vector3.one * dragScaleBoost;
        DiceSfx.Take(1);

        if (selectionZone) selectionZone.SetMood(DiceDropZone.Mood.Armed);
        FollowPointer(eventData.position);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!dragging) return;
        FollowPointer(eventData.position);

        // Le dé penche à l'opposé du déplacement : il a l'air « trimbalé ».
        tiltTargetZ = Mathf.Clamp(-eventData.delta.x * dragTiltPerVelocity, -dragMaxTilt, dragMaxTilt);

        if (selectionZone)
        {
            bool over = selectionZone.Contains(eventData.position, eventData.pressEventCamera);
            bool ok = canDropInZoneCheck == null || canDropInZoneCheck(this);
            selectionZone.SetMood(!over ? DiceDropZone.Mood.Armed
                                        : (ok ? DiceDropZone.Mood.Accept : DiceDropZone.Mood.Deny));
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!dragging) return;
        dragging = false;
        if (cg) cg.blocksRaycasts = true;
        SetDragOnTop(false); // le dé lâché retrouve l'ordre de tri normal
        if (selectionZone) selectionZone.SetMood(DiceDropZone.Mood.Off);

        bool intoZone = selectionZone && selectionZone.Contains(eventData.position, eventData.pressEventCamera);

        // GameManager applique (ou refuse) la sélection, puis renvoie le dé au bon endroit
        // via MoveToContainer : c'est lui qui a la vérité sur ce qui est permis.
        if (onSelectionDrop != null) onSelectionDrop(this, intoZone);
        else AbortFlight();

        // Aucun vol déclenché (dépôt sans changement) → le dé reprend simplement sa case.
        if (!IsFlying && slotHolder) AbortFlight();
    }

    void Update()
    {
        // Retours visuels différés : ils attendent que le dé soit posé et immobile.
        TryApplyClauseOutline();
        TryApplyFlashFrame();

        if (dragging)
        {
            float z = Mathf.LerpAngle(transform.localEulerAngles.z, tiltTargetZ, Time.unscaledDeltaTime * 14f);
            transform.localRotation = Quaternion.Euler(0f, 0f, z);
            tiltTargetZ = Mathf.MoveTowards(tiltTargetZ, 0f, Time.unscaledDeltaTime * 90f);
            return;
        }

        // Survol : léger grossissement, uniquement sur la table (dans la zone, le layout
        // tient compte de l'échelle et les voisins se décaleraient à chaque survol).
        if (!IsFlying && !_bouncing && _scaleCo == null)
        {
            bool lift = hovering && CanDragNow() && SlotContainer == homeParent;
            float target = baseScale * (lift ? hoverScaleBoost : 1f);
            float cur = transform.localScale.x;
            if (!Mathf.Approximately(cur, target))
                transform.localScale = Vector3.one * Mathf.Lerp(cur, target, Time.unscaledDeltaTime * 14f);
        }
    }

    void FollowPointer(Vector2 screenPos)
    {
        var canvas = RootCanvas();
        if (!canvas) return;
        var canvasRT = (RectTransform)canvas.transform;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, screenPos, dragCam, out var local))
            ((RectTransform)transform).anchoredPosition = local;
    }
}
