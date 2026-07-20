using UnityEngine;

// Zone d'activation d'artefact : à poser sur un RectTransform couvrant la table de jeu.
// Le drag & drop d'une carte d'inventaire au-dessus de cette zone déclenche l'usage de l'artefact.
[RequireComponent(typeof(RectTransform))]
public class ArtifactDropZone : MonoBehaviour
{
    [Tooltip("Surbrillance affichée quand un artefact glissé survole la zone (facultatif).")]
    public GameObject highlight;

    [Header("Pulsation d'illumination")]
    public float pulseSpeed = 3.5f;
    [Range(0f, 1f)] public float pulseMin = 0.35f;
    [Range(0f, 1f)] public float pulseMax = 0.85f;

    RectTransform rt;
    RectTransform Rt => rt ? rt : (rt = (RectTransform)transform);

    CanvasGroup hlCg;
    bool on;

    void Awake()
    {
        if (highlight)
        {
            hlCg = highlight.GetComponent<CanvasGroup>();
            if (!hlCg) hlCg = highlight.AddComponent<CanvasGroup>();
            highlight.SetActive(false);
        }
    }

    void Update()
    {
        // Légère illumination pulsée tant qu'un objet survole la zone.
        if (on && hlCg)
        {
            float k = (Mathf.Sin(Time.unscaledTime * pulseSpeed) + 1f) * 0.5f;
            hlCg.alpha = Mathf.Lerp(pulseMin, pulseMax, k);
        }
    }

    // Vrai si le point écran est au-dessus de la zone.
    public bool Contains(Vector2 screenPos, Camera cam)
        => Rt && RectTransformUtility.RectangleContainsScreenPoint(Rt, screenPos, cam);

    public void SetHighlight(bool value)
    {
        on = value;
        if (highlight) highlight.SetActive(value);
        if (hlCg) hlCg.alpha = value ? pulseMax : 0f;
    }
}
