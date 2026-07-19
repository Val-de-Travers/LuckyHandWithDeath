using UnityEngine;
using UnityEngine.UI;

// Petit effet de "destruction" d'un artefact utilisé : la vignette tremble, tourne,
// rétrécit et s'efface. Auto-porté (aucune dépendance à la carte) et s'auto-détruit.
[RequireComponent(typeof(RectTransform))]
public class DragDestroyFx : MonoBehaviour
{
    public float duration = 0.55f;
    public float shakeAmplitude = 6f;
    public float maxTilt = 28f;

    Image img;
    float t;
    Vector2 basePos;
    Vector3 baseScale;

    void Awake()
    {
        img = GetComponent<Image>();
        var rt = (RectTransform)transform;
        basePos = rt.anchoredPosition;
        baseScale = transform.localScale;
    }

    void Update()
    {
        t += Time.unscaledDeltaTime;
        float p = Mathf.Clamp01(t / Mathf.Max(0.01f, duration));

        var rt = (RectTransform)transform;
        // Tremblement décroissant
        float decay = 1f - p;
        Vector2 shake = new Vector2(
            Mathf.Sin(t * 55f) * shakeAmplitude,
            Mathf.Cos(t * 47f) * shakeAmplitude) * decay;
        rt.anchoredPosition = basePos + shake;

        // Bascule + rétrécissement
        transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 35f) * maxTilt * decay);
        transform.localScale = baseScale * Mathf.Lerp(1f, 0.25f, p);

        // Fondu
        if (img)
        {
            var c = img.color;
            c.a = Mathf.Lerp(1f, 0f, p);
            img.color = c;
        }

        if (p >= 1f) Destroy(gameObject);
    }
}
