using System.Collections;
using UnityEngine;
using TMPro;

public class HintBanner : MonoBehaviour
{
    [Header("Refs")]
    public CanvasGroup canvasGroup;   // assigner le CanvasGroup du panneau
    public TMP_Text textLabel;        // assigner le TMP_Text du bandeau

    [Header("Transitions")]
    [Tooltip("Durée du fondu d'apparition")]
    public float fadeInDuration = 0.15f;
    [Tooltip("Durée du fondu de disparition")]
    public float fadeOutDuration = 0.15f;

    // Comportement : le bandeau reste affiché jusqu'à ce qu'on appelle Show() à nouveau
    // (ou Hide()). Pas d'auto-hide.
    Coroutine running;

    void Awake()
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
        }
    }

    /// <summary>
    /// Affiche un message. Remplace l'annonce précédente :
    /// - si une annonce est visible, on la fait disparaître (fade out)
    /// - puis on affiche la nouvelle (fade in)
    /// Le bandeau reste visible jusqu'à un prochain Show(...) ou Hide().
    /// </summary>
    public void Show(string message)
    {
        if (!gameObject.activeInHierarchy) return;

        if (running != null) StopCoroutine(running);
        running = StartCoroutine(ShowStickyRoutine(message ?? string.Empty));
    }

    /// <summary>
    /// Masque le bandeau avec un fade-out.
    /// </summary>
    public void Hide()
    {
        if (!gameObject.activeInHierarchy) return;

        if (running != null) StopCoroutine(running);
        running = StartCoroutine(HideRoutine());
    }

    /// <summary>
    /// Affiche immédiatement (sans fade) et reste visible.
    /// </summary>
    public void ShowImmediate(string message)
    {
        if (running != null) StopCoroutine(running);
        if (textLabel) textLabel.text = message ?? string.Empty;
        if (canvasGroup) canvasGroup.alpha = 1f;
        running = null;
    }

    /// <summary>
    /// Masque immédiatement (sans fade).
    /// </summary>
    public void HideImmediate()
    {
        if (running != null) StopCoroutine(running);
        if (canvasGroup) canvasGroup.alpha = 0f;
        running = null;
    }

    IEnumerator ShowStickyRoutine(string msg)
    {
        float current = canvasGroup ? canvasGroup.alpha : 0f;

        // 1) Si quelque chose est affiché, fade-out d'abord
        if (current > 0f && fadeOutDuration > 0f && canvasGroup)
        {
            float t = 0f;
            while (t < fadeOutDuration)
            {
                t += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(current, 0f, Mathf.Clamp01(t / Mathf.Max(0.0001f, fadeOutDuration)));
                yield return null;
            }
            canvasGroup.alpha = 0f;
        }
        else if (canvasGroup)
        {
            canvasGroup.alpha = 0f;
        }

        // 2) Changer le texte
        if (textLabel) textLabel.text = msg;

        // 3) Fade-in
        if (canvasGroup)
        {
            if (fadeInDuration <= 0f)
            {
                canvasGroup.alpha = 1f;
            }
            else
            {
                float t = 0f;
                while (t < fadeInDuration)
                {
                    t += Time.deltaTime;
                    canvasGroup.alpha = Mathf.Lerp(0f, 1f, Mathf.Clamp01(t / Mathf.Max(0.0001f, fadeInDuration)));
                    yield return null;
                }
                canvasGroup.alpha = 1f;
            }
        }

        running = null; // on reste visible jusqu'à nouvel ordre
    }

    IEnumerator HideRoutine()
    {
        if (!canvasGroup)
        {
            running = null;
            yield break;
        }

        float start = canvasGroup.alpha;
        if (fadeOutDuration <= 0f)
        {
            canvasGroup.alpha = 0f;
        }
        else
        {
            float t = 0f;
            while (t < fadeOutDuration)
            {
                t += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(start, 0f, Mathf.Clamp01(t / Mathf.Max(0.0001f, fadeOutDuration)));
                yield return null;
            }
            canvasGroup.alpha = 0f;
        }
        running = null;
    }
}
