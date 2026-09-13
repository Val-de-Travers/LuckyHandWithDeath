using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

public enum ArtifactCardMode { Selection, Inventory }

public class ArtifactCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler, IPointerClickHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("UI Refs")]
    public Image icon;
    public TMP_Text nameLabel;
    public TMP_Text descLabel;     // tu peux le laisser vide si tu veux la desc uniquement en tooltip
    public TMP_Text badgeLabel;    // optionnel : affiche "1", "2", "3" en mode Selection
    public TMP_Text typeLabel;     // affiche le Type de l'artefact (Ajout, Relance, Score, ...)
    public TMP_Text rarityLabel;   // "Rarete" : affiche le nom de la rareté, coloré selon celle-ci

    [Header("Hover FX")]
    public float hoverScale = 1.04f;
    public float normalScale = 1.0f;
    public float scaleLerpSpeed = 12f;
    public float hoverAlphaBoost = 0.15f;

    [Header("Tooltip")]
    public ArtifactTooltip tooltip; // assigné par le parent (GameManager / InventoryUI)

    [Header("Behavior")]
    public bool showInlineDescriptionInInventory = true;

    [Header("Contour de rareté")]
    [Tooltip("Épaisseur (px) du cadre coloré indiquant la rareté de l'artefact.")]
    public float rarityOutlineThickness = 4f;
    GameObject rarityOutline;
    Image[] rarityBars;

    [HideInInspector] public Artifact artifact;
    [HideInInspector] public ArtifactCardMode mode = ArtifactCardMode.Selection;

    CanvasGroup cg;
    bool isHover;
    int indexZeroBased = 0; // pour le badge ("1", "2", "3")

    public System.Action<ArtifactCardView> onClicked;

    // ==== Drag & drop (mode Inventaire) : glisser la carte sur la table pour activer l'artefact ====
    [HideInInspector] public bool dragEnabled = false;
    ArtifactDropZone dropZone;
    // Retourne true si l'artefact a réellement été joué (l'animation de destruction
    // ne doit pas se jouer quand l'usage est refusé).
    System.Func<bool> onDroppedOnTable;
    Camera dragCam;

    // Sauvegarde de la position d'origine (on déplace la carte elle-même, pas une copie)
    bool dragging;
    Transform dragOrigParent;
    int dragOrigSibling;
    Vector2 dragOrigAnchoredPos;
    Quaternion dragOrigRotation;
    // Ancres/pivot d'origine : pendant le drag on les recentre pour que la carte suive
    // exactement la souris (sinon un ancrage non centré crée un décalage énorme).
    Vector2 dragOrigAnchorMin, dragOrigAnchorMax, dragOrigPivot;

    [Header("Drag FX")]
    [Tooltip("Amplitude du balancement (degrés) selon la vitesse horizontale de la souris.")]
    public float swayPerVelocity = 1.4f;
    public float swayMaxAngle = 22f;
    public float swayLerpSpeed = 14f;
    public float swayReturnSpeed = 90f; // vitesse de retour du sway vers 0 (deg/s)
    float swayTargetZ;

    // Active/désactive le drag pour cette carte (appelé par InventoryUI).
    public void EnableDrag(ArtifactDropZone zone, System.Func<bool> onDropped)
    {
        dropZone = zone;
        onDroppedOnTable = onDropped;
        dragEnabled = (zone != null);
    }

    // ==== Drag & drop (mode Sélection) : glisser la carte sur le PERSONNAGE pour la choisir ====
    [HideInInspector] public bool pickDragEnabled = false;
    ArtifactDropZone pickZone;
    System.Action onDroppedOnCharacter;

    // Active le glisser-déposer de CHOIX : déposer la carte sur la zone (le personnage) la sélectionne.
    public void EnablePickDrag(ArtifactDropZone zone, System.Action onPicked)
    {
        pickZone = zone;
        onDroppedOnCharacter = onPicked;
        pickDragEnabled = (zone != null);
    }

    void Awake()
    {
        cg = GetComponent<CanvasGroup>();
        if (!cg) cg = gameObject.AddComponent<CanvasGroup>();
        transform.localScale = Vector3.one * normalScale;
        if (descLabel) descLabel.textWrappingMode = TextWrappingModes.NoWrap;

        // Retrouve le label de rareté ("Rarete") s'il n'est pas assigné dans l'Inspector.
        if (rarityLabel == null)
        {
            var t = transform.Find("Rarete");
            if (t) rarityLabel = t.GetComponent<TMP_Text>();
        }
    }

    // ==== Apparition en cascade (effet de découverte) ====
    // La carte surgit réduite et de travers, puis se déploie jusqu'à sa taille. Tant que
    // l'animation joue, le hover est court-circuité : son lissage d'échelle, qui tourne
    // chaque frame, écraserait l'effet dès la première image.
    bool revealing;
    float revealElapsed, revealDelay, revealDuration, revealTilt;
    const float RevealStartScale = 0.55f;

    // `delay` échelonne les cartes ; `tiltDegrees` donne à chacune son inclinaison de départ.
    public void PlayReveal(float delay, float tiltDegrees = -8f, float duration = 0.34f)
    {
        if (!cg) cg = GetComponent<CanvasGroup>();
        if (!cg) cg = gameObject.AddComponent<CanvasGroup>();

        revealing = true;
        revealElapsed = 0f;
        revealDelay = Mathf.Max(0f, delay);
        revealDuration = Mathf.Max(0.05f, duration);
        revealTilt = tiltDegrees;

        cg.alpha = 0f;
        cg.blocksRaycasts = false; // pas de clic sur une carte pas encore arrivée
        transform.localScale = Vector3.one * (normalScale * RevealStartScale);
        transform.localRotation = Quaternion.Euler(0f, 0f, revealTilt);
    }

    void UpdateReveal()
    {
        revealElapsed += Time.unscaledDeltaTime;
        float t = (revealElapsed - revealDelay) / revealDuration;

        if (t <= 0f) { if (cg) cg.alpha = 0f; return; } // son tour n'est pas encore venu

        if (t >= 1f)
        {
            revealing = false;
            transform.localScale = Vector3.one * normalScale;
            transform.localRotation = Quaternion.identity;
            if (cg) { cg.alpha = 1f; cg.blocksRaycasts = true; }
            return;
        }

        // Léger dépassement à l'arrivée : la carte se pose au lieu d'apparaître.
        float e = 1f + 2.70158f * Mathf.Pow(t - 1f, 3f) + 1.70158f * Mathf.Pow(t - 1f, 2f);
        transform.localScale = Vector3.one * Mathf.LerpUnclamped(normalScale * RevealStartScale, normalScale, e);
        transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpAngle(revealTilt, 0f, 1f - Mathf.Pow(1f - t, 3f)));
        if (cg) cg.alpha = Mathf.Clamp01(t / 0.4f); // opacité acquise bien avant la fin du mouvement
    }

    void Update()
    {
        if (revealing) { UpdateReveal(); return; }

        // Pendant le drag : balancement (sway) selon la vitesse horizontale de la souris.
        if (dragging)
        {
            swayTargetZ = Mathf.MoveTowards(swayTargetZ, 0f, Time.unscaledDeltaTime * swayReturnSpeed);
            float z = Mathf.LerpAngle(transform.localEulerAngles.z, swayTargetZ, Time.unscaledDeltaTime * swayLerpSpeed);
            transform.localRotation = Quaternion.Euler(0f, 0f, z);
            return; // on ne fait pas le hover-scale pendant le drag
        }

        float targetScale = isHover ? hoverScale : normalScale;
        transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * targetScale, Time.unscaledDeltaTime * scaleLerpSpeed);

        if (cg)
        {
            float targetAlpha = isHover ? (1f + hoverAlphaBoost) : 1f;
            cg.alpha = Mathf.Lerp(cg.alpha, targetAlpha, Time.unscaledDeltaTime * scaleLerpSpeed);
        }
    }

    // ==== API publique ====
    public void Setup(Artifact a, ArtifactTooltip sharedTooltip, ArtifactCardMode m, int idxZeroBased = 0)
    {
        tooltip = sharedTooltip;
        UpdateMode(m);
        SetIndex(idxZeroBased);
        Bind(a);
    }

    public void SetIndex(int idxZeroBased)
    {
        this.indexZeroBased = idxZeroBased;
        if (badgeLabel) badgeLabel.text = (this.indexZeroBased + 1).ToString();
    }

    public void UpdateMode(ArtifactCardMode m)
    {
        mode = m;
        // Le choix se fait désormais au clic sur la carte : plus de badge 1/2/3
        bool showDesc = (mode == ArtifactCardMode.Inventory) && showInlineDescriptionInInventory;

        if (badgeLabel) badgeLabel.gameObject.SetActive(false);
        if (descLabel) descLabel.gameObject.SetActive(showDesc);
    }

    public void SetTooltip(ArtifactTooltip t) => tooltip = t;

    public void Bind(Artifact a)
    {
        artifact = a;

        if (!a)
        {
            if (icon) { icon.sprite = null; icon.enabled = false; }
            if (nameLabel) nameLabel.text = "—";
            if (typeLabel) typeLabel.text = "";
            if (descLabel && descLabel.gameObject.activeSelf) descLabel.text = "Inventaire vide.";
            HideRarityOutline();
            return;
        }

        if (icon)
        {
            icon.sprite = a.icon;
            icon.enabled = a.icon != null;
            icon.preserveAspect = true;
            icon.color = Color.white;
        }
        if (nameLabel) nameLabel.text = a.displayName;
        if (typeLabel) typeLabel.text = TypeToLabel(a.type);
        if (descLabel && descLabel.gameObject.activeSelf) descLabel.text = a.description;

        ApplyRarity(a.rarity); // label de rareté (coloré) + prépare le contour (affiché au survol)
    }

    // ==== Rareté : couleur + nom (Common vert · Rare bleu · Epic jaune · Legendary rouge) ====
    public static Color RarityColor(ArtifactRarity r) => r switch
    {
        ArtifactRarity.Common    => new Color(0x4C / 255f, 0xE3 / 255f, 0x77 / 255f), // #4CE377
        ArtifactRarity.Rare      => new Color(0x4C / 255f, 0x90 / 255f, 0xE3 / 255f), // #4C90E3
        ArtifactRarity.Epic      => new Color(0xD9 / 255f, 0xB8 / 255f, 0x3E / 255f), // #D9B83E
        ArtifactRarity.Legendary => new Color(0xE3 / 255f, 0x4C / 255f, 0x4C / 255f), // #E34C4C
        _ => Color.white
    };

    public static string RarityName(ArtifactRarity r) => r switch
    {
        ArtifactRarity.Common    => "Commun",
        ArtifactRarity.Rare      => "Rare",
        ArtifactRarity.Epic      => "Épique",
        ArtifactRarity.Legendary => "Légendaire",
        _ => r.ToString()
    };

    bool hasRarity; // un artefact est lié → le contour peut s'afficher au survol

    void ApplyRarity(ArtifactRarity r)
    {
        var c = RarityColor(r);

        // 1) Label "Rarete" : texte + couleur de la rareté.
        if (rarityLabel)
        {
            rarityLabel.text = RarityName(r);
            rarityLabel.color = c;
            rarityLabel.gameObject.SetActive(true);
        }

        // 2) Contour : on prépare/colore les barres, mais on ne l'affiche qu'au survol (voir RefreshOutline).
        if (rarityOutline == null) BuildRarityOutline();
        if (rarityBars != null) foreach (var b in rarityBars) if (b) b.color = c;
        hasRarity = true;
        RefreshOutline();
    }

    // Le contour n'apparaît qu'au survol, uniquement en mode Sélection (jamais dans l'inventaire).
    void RefreshOutline()
    {
        if (rarityOutline)
            rarityOutline.SetActive(hasRarity && isHover && mode == ArtifactCardMode.Selection);
    }

    void HideRarityOutline()
    {
        hasRarity = false;
        if (rarityOutline) rarityOutline.SetActive(false);
        if (rarityLabel) { rarityLabel.text = ""; rarityLabel.gameObject.SetActive(false); }
    }

    void BuildRarityOutline()
    {
        rarityOutline = new GameObject("RarityOutline", typeof(RectTransform));
        var rt = (RectTransform)rarityOutline.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        // Premier enfant : le cadre rend AU-DESSUS du fond de carte mais DERRIÈRE les labels
        // (Name, Type, Rarete...) — sinon la barre du haut recouvrait le Name.
        rarityOutline.transform.SetAsFirstSibling();

        float t = Mathf.Max(1f, rarityOutlineThickness);
        rarityBars = new Image[4];
        rarityBars[0] = MakeBar(rt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, t)); // haut
        rarityBars[1] = MakeBar(rt, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, t)); // bas
        rarityBars[2] = MakeBar(rt, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(t, 0f)); // gauche
        rarityBars[3] = MakeBar(rt, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(t, 0f)); // droite
    }

    static Image MakeBar(RectTransform parent, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 size)
    {
        var bar = new GameObject("Bar", typeof(RectTransform));
        var rt = (RectTransform)bar.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
        rt.sizeDelta = size; rt.anchoredPosition = Vector2.zero;
        var img = bar.AddComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    // Affiche un contenu "libre" (ex: un Trait) sur la carte, sans Artifact sous-jacent.
    string rawTooltip;
    public void BindRaw(string title, string desc, Sprite iconSprite = null, string typeText = "Trait")
    {
        artifact = null;
        // Titre en gras dans le tooltip (cohérent avec les autres survols de traits).
        rawTooltip = string.IsNullOrEmpty(desc) ? $"<b>{title}</b>" : $"<b>{title}</b>\n{desc}";

        if (icon)
        {
            icon.sprite = iconSprite;
            icon.enabled = iconSprite != null;
            icon.preserveAspect = true;
            icon.color = Color.white;
        }
        if (nameLabel) nameLabel.text = title;
        if (typeLabel) typeLabel.text = typeText;
        if (descLabel && descLabel.gameObject.activeSelf) descLabel.text = desc;

        HideRarityOutline(); // un contenu brut (Trait) n'a pas de rareté
    }

    static string TypeToLabel(ArtifactType t) => t switch
    {
        ArtifactType.Relance => "Relance",
        ArtifactType.Ajout => "Ajout",
        ArtifactType.Transformation => "Transformation",
        ArtifactType.Score => "Score",
        ArtifactType.ContreJeu => "Contre-Jeu",
        _ => t.ToString()
    };

    // ==== Hover / Tooltip ====
    public void OnPointerEnter(PointerEventData eventData)
    {
        isHover = true;
        RefreshOutline(); // le contour de rareté n'apparaît qu'au survol
        string tip = artifact != null ? artifact.description : rawTooltip;
        if (tooltip != null && !string.IsNullOrEmpty(tip))
        {
            tooltip.Show(tip, eventData.position);
            tooltip.FollowMouse(eventData.position);
        }
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        if (tooltip != null && tooltip.gameObject.activeSelf)
        {
            tooltip.FollowMouse(eventData.position);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHover = false;
        RefreshOutline(); // masque le contour de rareté quand on quitte la carte
        if (tooltip != null) tooltip.Hide();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // Mode Selection : cliquer la carte choisit l'artefact (câblé par GameManager).
        // Mode Inventory : pas de clic direct (glisser-déposer sur la table pour activer).
        if (mode == ArtifactCardMode.Selection)
            onClicked?.Invoke(this);
    }

    // ==== Drag & drop ====
    // Inventaire : glisser sur la table (usage). Sélection : glisser sur le personnage (choix).
    bool CanDrag => artifact != null && (
        (dragEnabled && mode == ArtifactCardMode.Inventory) ||
        (pickDragEnabled && mode == ArtifactCardMode.Selection));

    // Zone de dépôt active selon le mode courant.
    ArtifactDropZone ActiveZone => (mode == ArtifactCardMode.Selection) ? pickZone : dropZone;

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!CanDrag) return;
        if (tooltip != null) tooltip.Hide();

        var rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;
        dragCam = (rootCanvas && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay) ? rootCanvas.worldCamera : null;

        // On déplace LA CARTE elle-même : on mémorise sa place pour la remettre ensuite.
        var selfRT = (RectTransform)transform;
        dragOrigParent = transform.parent;
        dragOrigSibling = transform.GetSiblingIndex();
        dragOrigAnchoredPos = selfRT.anchoredPosition;
        dragOrigRotation = transform.localRotation;
        dragOrigAnchorMin = selfRT.anchorMin;
        dragOrigAnchorMax = selfRT.anchorMax;
        dragOrigPivot = selfRT.pivot;

        if (rootCanvas)
        {
            transform.SetParent(rootCanvas.transform, true); // sort du layout (inventaire ou choix)
            transform.SetAsLastSibling();                    // au-dessus de tout
        }

        // Recentre ancres + pivot : MoveSelf place la carte via anchoredPosition = point local du
        // canvas (repère centré). Sans ce recentrage, un ancrage en coin/étiré (layout des cartes
        // de choix) provoque un décalage énorme et la carte part hors écran.
        selfRT.anchorMin = selfRT.anchorMax = new Vector2(0.5f, 0.5f);
        selfRT.pivot = new Vector2(0.5f, 0.5f);

        if (cg) cg.blocksRaycasts = false;                   // laisse passer le raycast vers la table

        dragging = true;
        swayTargetZ = 0f;
        if (ActiveZone) ActiveZone.SetHighlight(true);
        MoveSelf(eventData.position);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!dragging) return;
        MoveSelf(eventData.position);

        // Balancement : la carte penche à l'opposé du déplacement horizontal (effet "trimbalé").
        swayTargetZ = Mathf.Clamp(-eventData.delta.x * swayPerVelocity, -swayMaxAngle, swayMaxAngle);

        // La zone s'illumine uniquement quand la carte est réellement au-dessus.
        var z = ActiveZone;
        if (z) z.SetHighlight(z.Contains(eventData.position, eventData.pressEventCamera));
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!dragging) return;
        dragging = false;

        var zone = ActiveZone;
        bool droppedOnZone = CanDrag && zone && zone.Contains(eventData.position, eventData.pressEventCamera);
        bool isPickMode = (mode == ArtifactCardMode.Selection);

        // Remet la carte à sa place d'origine (ancres/pivot recentrés → restaurés, puis position).
        var rt = (RectTransform)transform;
        if (dragOrigParent)
        {
            transform.SetParent(dragOrigParent, true);
            transform.SetSiblingIndex(dragOrigSibling);
        }
        rt.anchorMin = dragOrigAnchorMin;
        rt.anchorMax = dragOrigAnchorMax;
        rt.pivot = dragOrigPivot;
        rt.anchoredPosition = dragOrigAnchoredPos;
        transform.localRotation = dragOrigRotation;
        swayTargetZ = 0f;
        if (cg) cg.blocksRaycasts = true;
        if (zone) zone.SetHighlight(false);

        if (!droppedOnZone) return;

        if (isPickMode)
        {
            // Déposé sur le personnage → on CHOISIT cet artefact. Le callback détruit toute
            // l'UI de choix (dont CETTE carte) : on n'accède plus à `this` après l'appel.
            onDroppedOnCharacter?.Invoke();
            return;
        }

        // Mode Inventaire : déposé sur la table → on tente l'activation. L'effet de destruction
        // n'est joué QUE si l'artefact a réellement été consommé (un usage refusé laisse la carte).
        // ⚠️ On capture l'image AVANT l'usage : consommer l'artefact re-lie aussitôt la carte
        // à l'artefact suivant (sinon l'animation afficherait la mauvaise icône).
        Sprite fxSprite = (icon && icon.sprite) ? icon.sprite : (artifact ? artifact.icon : null);
        bool used = onDroppedOnTable != null && onDroppedOnTable.Invoke();
        if (used) SpawnDestructionFx(eventData.position, fxSprite);
    }

    // Vignette "détruite" (tremblement + rotation + rétrécissement + fondu) à l'endroit du dépôt.
    void SpawnDestructionFx(Vector2 screenPos, Sprite fxSprite)
    {
        var rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;
        if (!rootCanvas) return;

        // Sans sprite, on ne spawne rien (une Image sans sprite = carré blanc).
        if (fxSprite == null) return;

        var go = new GameObject("ArtifactDestroyFx", typeof(RectTransform));
        go.transform.SetParent(rootCanvas.transform, false);
        go.transform.SetAsLastSibling();

        var img = go.AddComponent<Image>();
        img.sprite = fxSprite;
        img.preserveAspect = true;
        img.raycastTarget = false;
        ((RectTransform)go.transform).sizeDelta = ((RectTransform)transform).sizeDelta;

        var cam = (rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay) ? rootCanvas.worldCamera : null;
        var canvasRT = (RectTransform)rootCanvas.transform;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, screenPos, cam, out var local))
            ((RectTransform)go.transform).anchoredPosition = local;

        go.AddComponent<DragDestroyFx>();
    }

    void MoveSelf(Vector2 screenPos)
    {
        var rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;
        if (!rootCanvas) return;
        var canvasRT = (RectTransform)rootCanvas.transform;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, screenPos, dragCam, out var local))
            ((RectTransform)transform).anchoredPosition = local;
    }

}
