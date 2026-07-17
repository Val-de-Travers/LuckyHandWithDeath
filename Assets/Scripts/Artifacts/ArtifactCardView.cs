using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

public enum ArtifactCardMode { Selection, Inventory }

public class ArtifactCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler, IPointerClickHandler
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

    void Awake()
    {
        cg = GetComponent<CanvasGroup>();
        if (!cg) cg = gameObject.AddComponent<CanvasGroup>();
        transform.localScale = Vector3.one * normalScale;
        if (descLabel) descLabel.textWrappingMode = TextWrappingModes.NoWrap;
    }

    void Update()
    {
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
        // Mode Inventory : pas de clic direct (on passe par le bouton USE) pour éviter
        // toute utilisation accidentelle d'un artefact.
        if (mode == ArtifactCardMode.Selection)
            onClicked?.Invoke(this);
    }

}
