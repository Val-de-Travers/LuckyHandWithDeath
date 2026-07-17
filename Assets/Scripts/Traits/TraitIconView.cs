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

    public void Setup(Sprite sprite, string traitName, string description, ArtifactTooltip sharedTooltip)
    {
        tooltip = sharedTooltip;
        tipText = string.IsNullOrEmpty(description) ? traitName : $"{traitName}\n{description}";

        if (icon)
        {
            icon.sprite = sprite;
            icon.enabled = sprite != null;
            icon.preserveAspect = true;
            icon.raycastTarget = true;
        }

        if (fallbackLabel)
        {
            bool showLabel = sprite == null;
            fallbackLabel.gameObject.SetActive(showLabel);
            if (showLabel)
            {
                fallbackLabel.text = traitName;
                fallbackLabel.raycastTarget = true;
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
