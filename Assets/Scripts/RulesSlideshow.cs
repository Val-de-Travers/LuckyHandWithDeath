using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Diaporama des règles : un bouton ouvre un panneau plein écran affichant une série
// d'images, parcourues avec deux flèches (précédent / suivant).
//
// Câblage minimal dans la scène :
//   - openButton   : le bouton "Règles" visible en jeu
//   - panel        : le panneau (désactivé au départ, activé à l'ouverture)
//   - slideImage   : l'Image qui affiche la diapo courante
//   - prevButton / nextButton : les flèches
//   - closeButton  : bouton de fermeture (Échap fonctionne aussi)
//   - slides       : les images des règles, dans l'ordre d'affichage
//
// Le compteur ("2 / 7") et le titre de diapo sont optionnels.
public class RulesSlideshow : MonoBehaviour
{
    [Header("Ouverture / fermeture")]
    public Button openButton;
    public GameObject panel;
    public Button closeButton;

    [Header("Diapositives")]
    [Tooltip("Images des règles, dans l'ordre d'affichage.")]
    public Sprite[] slides;
    [Tooltip("Image qui affiche la diapo courante.")]
    public Image slideImage;

    [Header("Navigation")]
    public Button prevButton;
    public Button nextButton;
    [Tooltip("Coché : après la dernière diapo, on revient à la première (et inversement).")]
    public bool loop = false;

    [Header("Affichage (optionnel)")]
    [Tooltip("Compteur de progression, ex. « 2 / 7 ».")]
    public TMP_Text counterLabel;
    [Tooltip("Légendes affichées sous chaque diapo (facultatif, même ordre que slides).")]
    public string[] captions;
    public TMP_Text captionLabel;

    [Header("Comportement")]
    [Tooltip("Coché : le jeu est mis en pause (Time.timeScale = 0) pendant la lecture.")]
    public bool pauseGameWhileOpen = false;

    int index = 0;
    float timeScaleBeforeOpen = 1f;

    public bool IsOpen => panel && panel.activeSelf;

    void Awake()
    {
        if (openButton) openButton.onClick.AddListener(Open);
        if (closeButton) closeButton.onClick.AddListener(Close);
        if (prevButton) prevButton.onClick.AddListener(Previous);
        if (nextButton) nextButton.onClick.AddListener(Next);

        if (panel) panel.SetActive(false);
    }

    void Update()
    {
        if (!IsOpen) return;

        // Échap ferme ; flèches gauche/droite naviguent.
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null)
        {
            if (kb.escapeKey.wasPressedThisFrame) Close();
            else if (kb.leftArrowKey.wasPressedThisFrame) Previous();
            else if (kb.rightArrowKey.wasPressedThisFrame) Next();
        }
#else
        if (Input.GetKeyDown(KeyCode.Escape)) Close();
        else if (Input.GetKeyDown(KeyCode.LeftArrow)) Previous();
        else if (Input.GetKeyDown(KeyCode.RightArrow)) Next();
#endif
    }

    public void Open()
    {
        if (!panel) return;
        index = 0;
        panel.SetActive(true);
        panel.transform.SetAsLastSibling(); // au-dessus du reste de l'UI

        if (pauseGameWhileOpen)
        {
            timeScaleBeforeOpen = Time.timeScale;
            Time.timeScale = 0f;
        }

        ShowSlide(index);
    }

    public void Close()
    {
        if (panel) panel.SetActive(false);
        if (pauseGameWhileOpen) Time.timeScale = timeScaleBeforeOpen;
    }

    public void Next()
    {
        if (slides == null || slides.Length == 0) return;
        if (index >= slides.Length - 1 && !loop) return;
        index = (index + 1) % slides.Length;
        ShowSlide(index);
    }

    public void Previous()
    {
        if (slides == null || slides.Length == 0) return;
        if (index <= 0 && !loop) return;
        index = (index - 1 + slides.Length) % slides.Length;
        ShowSlide(index);
    }

    void ShowSlide(int i)
    {
        int count = (slides != null) ? slides.Length : 0;

        if (slideImage)
        {
            bool valid = count > 0 && i >= 0 && i < count && slides[i] != null;
            slideImage.sprite = valid ? slides[i] : null;
            slideImage.enabled = valid;
            slideImage.preserveAspect = true;
        }

        if (counterLabel)
            counterLabel.text = (count > 0) ? $"{i + 1} / {count}" : "";

        if (captionLabel)
        {
            bool hasCaption = captions != null && i >= 0 && i < captions.Length
                              && !string.IsNullOrEmpty(captions[i]);
            captionLabel.gameObject.SetActive(hasCaption);
            if (hasCaption) captionLabel.text = captions[i];
        }

        // Flèches grisées aux extrémités (sauf en mode boucle)
        if (prevButton) prevButton.interactable = loop ? count > 1 : i > 0;
        if (nextButton) nextButton.interactable = loop ? count > 1 : i < count - 1;
    }
}
