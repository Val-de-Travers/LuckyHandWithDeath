using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// Petite vignette de trait (AddBoxP1 / AddBoxP2) : icône + tooltip de description au survol,
// comme les artefacts. Construite en code par GameManager.RebuildTraitIcons().
public class TraitIconView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
{
    public Image icon;
    public TMP_Text fallbackLabel;   // affiché si aucune icône n'est fournie

    ArtifactTooltip tooltip;
    string tipText;

    // Opacité appliquée à un trait dont l'effet unique a déjà été consommé.
    const float UsedAlpha = 0.45f;

    // used : le trait a un effet unique (1×/match) et il a déjà servi. La vignette est
    // atténuée et son titre préfixé de « (utilisé) ».
    // dimmed : le trait n'est PAS ACTIVÉ (palier trop bas) — même atténuation, sans préfixe
    // « (utilisé) » (le nom porte déjà « (Pas activé) »).
    // Note : Setup n'est appelée qu'une fois par vignette (les vues sont recréées à chaque
    // RebuildTraitIcons), l'atténuation ne se cumule donc pas.
    public void Setup(Sprite sprite, string traitName, string description, ArtifactTooltip sharedTooltip,
                      bool used = false, bool dimmed = false)
    {
        tooltip = sharedTooltip;

        string title = used ? $"(utilisé) {traitName}" : traitName;
        tipText = string.IsNullOrEmpty(description) ? title : $"{title}\n{description}";

        bool faded = used || dimmed;

        if (icon)
        {
            icon.sprite = sprite;
            icon.enabled = sprite != null;
            icon.preserveAspect = true;
            icon.raycastTarget = true;
            if (faded)
            {
                var c = icon.color;
                c.a *= UsedAlpha;   // conserve le fond sombre éventuel du mode "sans icône"
                icon.color = c;
            }
        }

        if (fallbackLabel)
        {
            bool showLabel = sprite == null;
            fallbackLabel.gameObject.SetActive(showLabel);
            if (showLabel)
            {
                fallbackLabel.text = title;
                fallbackLabel.raycastTarget = true;
                if (faded)
                {
                    var lc = fallbackLabel.color;
                    lc.a *= UsedAlpha;
                    fallbackLabel.color = lc;
                }
            }
        }
    }

    // ==== Mise en avant au survol (les vignettes se chevauchent quand elles sont nombreuses) ====
    [Header("Hover")]
    public float hoverScale = 1.25f;
    public float scaleLerpSpeed = 14f;

    bool isHover;
    Canvas sortingCanvas; // permet de passer devant les voisines SANS changer l'ordre du layout

    void Update()
    {
        float target = isHover ? hoverScale : 1f;
        transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * target,
                                            Time.unscaledDeltaTime * scaleLerpSpeed);
    }

    // Met la vignette au premier plan via un Canvas local trié.
    // ⚠️ On n'utilise PAS SetAsLastSibling : dans un LayoutGroup, l'ordre des enfants
    // définit la POSITION — la vignette sauterait en fin de rangée, quitterait le curseur
    // et provoquerait un clignotement enter/exit.
    void SetFront(bool front)
    {
        if (front && sortingCanvas == null)
        {
            sortingCanvas = gameObject.GetComponent<Canvas>();
            if (!sortingCanvas) sortingCanvas = gameObject.AddComponent<Canvas>();
            // Un Canvas imbriqué a besoin de son propre raycaster pour rester survolable.
            if (!gameObject.GetComponent<GraphicRaycaster>())
                gameObject.AddComponent<GraphicRaycaster>();
        }

        if (sortingCanvas)
        {
            sortingCanvas.overrideSorting = front;
            sortingCanvas.sortingOrder = front ? 10 : 0;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHover = true;
        SetFront(true);

        if (tooltip != null && !string.IsNullOrEmpty(tipText))
        {
            tooltip.Show(tipText, eventData.position);
            tooltip.FollowMouse(eventData.position);
        }
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        if (tooltip != null && tooltip.gameObject.activeSelf)
            tooltip.FollowMouse(eventData.position);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHover = false;
        SetFront(false);

        if (tooltip != null) tooltip.Hide();
    }
}
