using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ArtifactTooltip : MonoBehaviour
{
    [Header("Assign in Inspector")]
    public RectTransform root;   // TooltipPanel (ce GameObject)
    public Image background;     // Image sur le Panel
    public TMP_Text text;        // TMP_Text enfant

    [Header("Settings")]
    public Vector2 offset = new Vector2(18f, -18f);
    public float maxWidth = 420f;
    [Tooltip("Ordre de tri du tooltip : doit rester supérieur à celui des éléments mis au premier plan au survol (vignettes de traits = 10).")]
    public int tooltipSortingOrder = 500;

    Canvas rootCanvas;
    Camera uiCam;

    void Awake()
    {
        if (!root) root = GetComponent<RectTransform>();
        rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;

        // Le tooltip ne doit JAMAIS intercepter la souris : sinon, quand il est recalé
        // sous le curseur (bord bas de l'écran), il vole le survol à l'élément source
        // et provoque un clignotement enter/exit en boucle.
        var cg = GetComponent<CanvasGroup>();
        if (!cg) cg = gameObject.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.interactable = false;
        foreach (var g in GetComponentsInChildren<Graphic>(true))
            g.raycastTarget = false;

        // Le tooltip doit s'afficher AU-DESSUS de tout — y compris des éléments qui se
        // mettent eux-mêmes au premier plan via un Canvas trié (ex : vignettes de traits
        // survolées). Un simple SetAsLastSibling ne suffit pas dans ce cas.
        var sortCanvas = GetComponent<Canvas>();
        if (!sortCanvas) sortCanvas = gameObject.AddComponent<Canvas>();
        sortCanvas.overrideSorting = true;
        sortCanvas.sortingOrder = tooltipSortingOrder;

        // ✅ Anchors centrés + pivot en haut-gauche
        if (root)
        {
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0f, 1f);
        }

        if (rootCanvas && rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            uiCam = null;
        else
            uiCam = rootCanvas ? (rootCanvas.worldCamera ? rootCanvas.worldCamera : Camera.main) : Camera.main;

        Hide();
    }

    public void Show(string content, Vector2 screenPos)
    {
        if (!root) return;
        if (text) text.text = content ?? "";

        root.gameObject.SetActive(true);
        root.SetAsLastSibling();
        if (text) text.ForceMeshUpdate();

        // place immédiatement le tooltip à la position écran fournie
        FollowMouse(screenPos);
    }

    public void Hide()
    {
        if (root) root.gameObject.SetActive(false);
    }

    public void FollowMouse(Vector2 screenPos)
    {
        if (!rootCanvas || !root) return;

        var canvasRT = (RectTransform)rootCanvas.transform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, screenPos, uiCam, out var localPoint);

        // pivot 0,1 => on pousse vers la droite et vers le bas
        localPoint += offset;

        // auto-size
        float maxW = Mathf.Min(maxWidth, canvasRT.rect.width * 0.8f);
        if (text)
        {
            var pref = text.GetPreferredValues(text.text, maxW - 20f, 0f);
            var size = new Vector2(
                Mathf.Clamp(pref.x + 20f, 120f, maxW),
                Mathf.Clamp(pref.y + 16f, 40f, canvasRT.rect.height * 0.6f)
            );
            root.sizeDelta = size;

            // Si le tooltip déborderait en bas, on le bascule AU-DESSUS du curseur
            // plutôt que de le recaler par-dessus (évite qu'il masque l'élément survolé).
            if (localPoint.y - size.y < canvasRT.rect.yMin)
                localPoint.y = localPoint.y - offset.y + size.y;

            // clamp à l’écran pour pivot 0,1 (haut-gauche)
            var min = new Vector2(canvasRT.rect.xMin, canvasRT.rect.yMin + size.y);
            var max = new Vector2(canvasRT.rect.xMax - size.x, canvasRT.rect.yMax);
            localPoint.x = Mathf.Clamp(localPoint.x, min.x, max.x);
            localPoint.y = Mathf.Clamp(localPoint.y, min.y, max.y);
        }

        root.anchoredPosition = localPoint;
    }
}
