using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class EnemyPanel : MonoBehaviour
{
    [Header("Refs")]
    public RectTransform root;
    public Image portrait;
    public TMP_Text nameText;
    public TMP_Text descText;
    public TMP_Text traitText;   // onglet "Trait" : affiche l'affixe propre de l'ennemi

    [Header("Layout")]
    public Vector2 offset = new Vector2(16f, -16f);
    public float maxWidth = 420f;

    Canvas rootCanvas;
    Camera uiCam;

    void Awake()
    {
        if (!root) root = GetComponent<RectTransform>();
        rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;

        // Le panneau ne doit JAMAIS intercepter la souris : sinon il passe sous le curseur,
        // vole le hover au EnemyMarker (PointerExit) et se met à clignoter.
        var cg = GetComponent<CanvasGroup>();
        if (!cg) cg = gameObject.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.interactable = false;
        foreach (var g in GetComponentsInChildren<Graphic>(true))
            g.raycastTarget = false;

        // Pivot en haut-gauche pour un placement “à côté de la souris”
        if (root)
        {
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0f, 1f);
        }

        if (rootCanvas && rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay) uiCam = null;
        else uiCam = rootCanvas ? (rootCanvas.worldCamera ? rootCanvas.worldCamera : Camera.main) : Camera.main;

        Hide();
    }

    public void Show(PalierConfig.EnemyInfo info, Vector2 screenPos)
    {
        if (!root) return;
        if (portrait) // image optionnelle dans le prefab
        {
            portrait.sprite = (info != null) ? info.icon : null;
            portrait.enabled = (portrait.sprite != null);
        }

        if (nameText)  nameText.text = info != null ? info.name : "—";
        if (descText)  descText.text = info != null ? (string.IsNullOrEmpty(info.description) ? "…" : info.description) : "";

        if (traitText)
        {
            bool hasTrait = info != null && !string.IsNullOrEmpty(info.afflictionName);
            traitText.gameObject.SetActive(hasTrait);
            if (hasTrait)
                traitText.text = $"Trait : {info.afflictionName}\n{info.afflictionDescription}";
        }

        root.gameObject.SetActive(true);
        root.SetAsLastSibling();
        Follow(screenPos);
    }

    public void Follow(Vector2 screenPos)
    {
        if (!rootCanvas || !root) return;

        var canvasRT = (RectTransform)rootCanvas.transform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, screenPos, uiCam, out var local);
        local += offset;

        float maxW = Mathf.Min(maxWidth, canvasRT.rect.width * 0.9f);
        var size = root.sizeDelta;
        if (descText)
        {
            var pref = descText.GetPreferredValues(descText.text, maxW - 20f, 0f);
            size = new Vector2(Mathf.Clamp(pref.x + 40f, 160f, maxW), Mathf.Clamp(pref.y + 64f, 80f, canvasRT.rect.height * 0.66f));
            root.sizeDelta = size;
        }

        var min = new Vector2(canvasRT.rect.xMin, canvasRT.rect.yMin + size.y);
        var max = new Vector2(canvasRT.rect.xMax - size.x, canvasRT.rect.yMax);
        local.x = Mathf.Clamp(local.x, min.x, max.x);
        local.y = Mathf.Clamp(local.y, min.y, max.y);

        root.anchoredPosition = local;
    }

    public void Hide()
    {
        if (root) root.gameObject.SetActive(false);
    }
}
