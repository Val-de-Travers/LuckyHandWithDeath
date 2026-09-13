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

    [Tooltip("Ne fait apparaître/disparaître que le TEXTE : la boîte d'annonce (BoxAnnonce) reste " +
             "affichée en permanence, comme un élément de décor du HUD. Décocher pour retrouver " +
             "l'ancien comportement, où tout le bandeau clignotait à chaque message.")]
    public bool fadeTextOnly = true;

    // Groupe réellement animé. En mode « texte seul », il est posé sur le label (créé à la
    // demande) et la boîte est forcée à pleine opacité : c'est ce qui l'empêche de disparaître
    // puis réapparaître à chaque annonce.
    CanvasGroup textGroup;
    CanvasGroup FadeGroup
    {
        get
        {
            if (!fadeTextOnly) return canvasGroup;

            if (!textGroup && textLabel)
            {
                textGroup = textLabel.GetComponent<CanvasGroup>();
                if (!textGroup) textGroup = textLabel.gameObject.AddComponent<CanvasGroup>();
            }
            if (canvasGroup) canvasGroup.alpha = 1f;   // la boîte ne bouge plus
            return textGroup ? textGroup : canvasGroup;
        }
    }

    // Comportement : le bandeau reste affiché jusqu'à ce qu'on appelle Show() à nouveau
    // (ou Hide()). Pas d'auto-hide.
    Coroutine running;

    void Awake()
    {
        // En mode « texte seul », la boîte est laissée visible : seul le libellé démarre
        // transparent (FadeGroup s'en charge et remet la boîte à pleine opacité).
        var g = FadeGroup;
        if (g != null) g.alpha = 0f;
    }

    /// <summary>
    /// Affiche un message. Remplace l'annonce précédente :
    /// - si une annonce est visible, on la fait disparaître (fade out)
    /// - puis on affiche la nouvelle (fade in)
    /// Le bandeau reste visible jusqu'à un prochain Show(...) ou Hide().
    /// </summary>
    // Muselé pendant les phases où l'écran ne doit porter qu'une seule consigne (choix d'un
    // artefact ou d'un trait). Piloté par GameManager : le bandeau est appelé depuis une
    // trentaine d'endroits, filtrer ici est le seul moyen sûr de garantir qu'il ne resurgit pas.
    [System.NonSerialized] public bool suppressed;

    // Filtre appliqué à CHAQUE message avant affichage, posé par GameManager : il convertit les
    // tokens de dé « [[die:x]] » en images, exactement comme dans le journal. Passer par un seul
    // point d'entrée évite d'avoir à instrumenter la trentaine d'appels à Show().
    [System.NonSerialized] public System.Func<string, string> decorate;

    string Decorate(string s) => (decorate != null && !string.IsNullOrEmpty(s)) ? decorate(s) : s;

    public void Show(string message)
    {
        if (suppressed) return;
        if (!gameObject.activeInHierarchy) return;

        if (running != null) StopCoroutine(running);
        running = StartCoroutine(ShowStickyRoutine(message ?? string.Empty));
    }

    /// <summary>
    /// Affiche un message EN IGNORANT la mise en sourdine. Réservé à celui qui a posé le
    /// `suppressed` : pendant une phase de récompense, le bandeau est mis au silence pour que
    /// les messages de jeu n'écrasent pas la consigne — mais la consigne, elle, doit s'afficher.
    /// L'appelant ne doit l'invoquer QUE lorsque le message change (voir
    /// GameManager.rewardPhaseShownMessage) : relancer sur le même texte redémarrerait le fondu.
    /// </summary>
    public void ShowSuppressed(string message)
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
        if (suppressed) return;
        if (running != null) StopCoroutine(running);
        if (textLabel) textLabel.text = Decorate(message ?? string.Empty);
        var g = FadeGroup;
        if (g) g.alpha = 1f;
        running = null;
    }

    /// <summary>
    /// Masque immédiatement (sans fade).
    /// </summary>
    public void HideImmediate()
    {
        if (running != null) StopCoroutine(running);
        var g = FadeGroup;
        if (g) g.alpha = 0f;
        running = null;
    }

    IEnumerator ShowStickyRoutine(string msg)
    {
        var cg = FadeGroup;
        float current = cg ? cg.alpha : 0f;

        // 1) Si quelque chose est affiché, fade-out d'abord
        if (current > 0f && fadeOutDuration > 0f && cg)
        {
            float t = 0f;
            while (t < fadeOutDuration)
            {
                t += Time.deltaTime;
                cg.alpha = Mathf.Lerp(current, 0f, Mathf.Clamp01(t / Mathf.Max(0.0001f, fadeOutDuration)));
                yield return null;
            }
            cg.alpha = 0f;
        }
        else if (cg)
        {
            cg.alpha = 0f;
        }

        // 2) Changer le texte
        if (textLabel) textLabel.text = Decorate(msg);   // tokens de dé → images

        // 3) Fade-in
        if (cg)
        {
            if (fadeInDuration <= 0f)
            {
                cg.alpha = 1f;
            }
            else
            {
                float t = 0f;
                while (t < fadeInDuration)
                {
                    t += Time.deltaTime;
                    cg.alpha = Mathf.Lerp(0f, 1f, Mathf.Clamp01(t / Mathf.Max(0.0001f, fadeInDuration)));
                    yield return null;
                }
                cg.alpha = 1f;
            }
        }

        running = null; // on reste visible jusqu'à nouvel ordre
    }

    IEnumerator HideRoutine()
    {
        var cg = FadeGroup;
        if (!cg)
        {
            running = null;
            yield break;
        }

        float start = cg.alpha;
        if (fadeOutDuration <= 0f)
        {
            cg.alpha = 0f;
        }
        else
        {
            float t = 0f;
            while (t < fadeOutDuration)
            {
                t += Time.deltaTime;
                cg.alpha = Mathf.Lerp(start, 0f, Mathf.Clamp01(t / Mathf.Max(0.0001f, fadeOutDuration)));
                yield return null;
            }
            cg.alpha = 0f;
        }
        running = null;
    }
}
