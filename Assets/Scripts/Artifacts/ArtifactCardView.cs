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

    [Header("Hover FX")]
    public float hoverScale = 1.04f;
    public float normalScale = 1.0f;
    public float scaleLerpSpeed = 12f;
    public float hoverAlphaBoost = 0.15f;

    [Header("Tooltip")]
    public ArtifactTooltip tooltip; // assigné par le parent (GameManager / InventoryUI)

    [Header("Behavior")]
    public bool showInlineDescriptionInInventory = true;

    [HideInInspector] public Artifact artifact;
    [HideInInspector] public ArtifactCardMode mode = ArtifactCardMode.Selection;

    CanvasGroup cg;
    bool isHover;
    int indexZeroBased = 0; // pour le badge ("1", "2", "3")

    public System.Action<ArtifactCardView> onClicked;

    // ==== Drag & drop (mode Inventaire) : glisser la carte sur la table pour activer l'artefact ====
    [HideInInspector] public bool dragEnabled = false;
    ArtifactDropZone dropZone;
    System.Action onDroppedOnTable;
    Camera dragCam;

    // Sauvegarde de la position d'origine (on déplace la carte elle-même, pas une copie)
    bool dragging;
    Transform dragOrigParent;
    int dragOrigSibling;
    Vector2 dragOrigAnchoredPos;
    Quaternion dragOrigRotation;

    [Header("Drag FX")]
    [Tooltip("Amplitude du balancement (degrés) selon la vitesse horizontale de la souris.")]
    public float swayPerVelocity = 1.4f;
    public float swayMaxAngle = 22f;
    public float swayLerpSpeed = 14f;
    public float swayReturnSpeed = 90f; // vitesse de retour du sway vers 0 (deg/s)
    float swayTargetZ;

    // Active/désactive le drag pour cette carte (appelé par InventoryUI).
    public void EnableDrag(ArtifactDropZone zone, System.Action onDropped)
    {
        dropZone = zone;
        onDroppedOnTable = onDropped;
        dragEnabled = (zone != null);
    }

    void Awake()
    {
        cg = GetComponent<CanvasGroup>();
        if (!cg) cg = gameObject.AddComponent<CanvasGroup>();
        transform.localScale = Vector3.one * normalScale;
        if (descLabel) descLabel.textWrappingMode = TextWrappingModes.NoWrap;
    }

    void Update()
    {
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
    }

    // Affiche un contenu "libre" (ex: un Trait) sur la carte, sans Artifact sous-jacent.
    string rawTooltip;
    public void BindRaw(string title, string desc, Sprite iconSprite = null, string typeText = "Trait")
    {
        artifact = null;
        rawTooltip = desc;

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
    bool CanDrag => dragEnabled && mode == ArtifactCardMode.Inventory && artifact != null;

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

        if (rootCanvas)
        {
            transform.SetParent(rootCanvas.transform, true); // sort du layout de l'inventaire
            transform.SetAsLastSibling();                    // au-dessus de tout
        }
        if (cg) cg.blocksRaycasts = false;                   // laisse passer le raycast vers la table

        dragging = true;
        swayTargetZ = 0f;
        if (dropZone) dropZone.SetHighlight(true);
        MoveSelf(eventData.position);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!dragging) return;
        MoveSelf(eventData.position);

        // Balancement : la carte penche à l'opposé du déplacement horizontal (effet "trimbalé").
        swayTargetZ = Mathf.Clamp(-eventData.delta.x * swayPerVelocity, -swayMaxAngle, swayMaxAngle);

        // La table s'illumine uniquement quand la carte est réellement au-dessus.
        if (dropZone) dropZone.SetHighlight(dropZone.Contains(eventData.position, eventData.pressEventCamera));
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!dragging) return;
        dragging = false;

        bool droppedOnTable = CanDrag && dropZone && dropZone.Contains(eventData.position, eventData.pressEventCamera);

        // Remet la carte à sa place d'origine dans l'inventaire (rotation comprise).
        if (dragOrigParent)
        {
            transform.SetParent(dragOrigParent, true);
            transform.SetSiblingIndex(dragOrigSibling);
            ((RectTransform)transform).anchoredPosition = dragOrigAnchoredPos;
        }
        transform.localRotation = dragOrigRotation;
        swayTargetZ = 0f;
        if (cg) cg.blocksRaycasts = true;
        if (dropZone) dropZone.SetHighlight(false);

        // Déposé sur la table → effet de destruction + activation de l'artefact.
        if (droppedOnTable)
        {
            SpawnDestructionFx(eventData.position);
            onDroppedOnTable?.Invoke();
        }
    }

    // Vignette "détruite" (tremblement + rotation + rétrécissement + fondu) à l'endroit du dépôt.
    void SpawnDestructionFx(Vector2 screenPos)
    {
        var rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;
        if (!rootCanvas) return;

        var go = new GameObject("ArtifactDestroyFx", typeof(RectTransform));
        go.transform.SetParent(rootCanvas.transform, false);
        go.transform.SetAsLastSibling();

        var img = go.AddComponent<Image>();
        img.sprite = icon ? icon.sprite : null;
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
