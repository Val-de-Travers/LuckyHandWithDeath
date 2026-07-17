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

    Canvas rootCanvas;
    Camera uiCam;

    void Awake()
    {
        if (!root) root = GetComponent<RectTransform>();
        rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;

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

            // clamp à l’écran pour pivot 0,1 (haut-gauche)
            var min = new Vector2(canvasRT.rect.xMin, canvasRT.rect.yMin + size.y);
            var max = new Vector2(canvasRT.rect.xMax - size.x, canvasRT.rect.yMax);
            localPoint.x = Mathf.Clamp(localPoint.x, min.x, max.x);
            localPoint.y = Mathf.Clamp(localPoint.y, min.y, max.y);
        }

        root.anchoredPosition = localPoint;
    }
}
