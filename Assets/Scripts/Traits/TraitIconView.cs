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

    public void OnPointerEnter(PointerEventData eventData)
    {
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
        if (tooltip != null) tooltip.Hide();
    }
}
