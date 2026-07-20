using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public enum DieFace { Two = 2, Three = 3, Four = 4, Five = 5, Six = 6, Ten = 10, Sun = 100 }

[Serializable]
public class DiceSprites {
    public Sprite two, three, four, five, six, ten, sun;
}

public class DieView : MonoBehaviour, IPointerClickHandler
{
    [Header("Wiring")]
    public Image faceImage;         // Image enfant "Face" (affiche la face)
    public Image lockIcon;          // Image "LockIn" (cadenas)
    public Image holdHighlight;     // Image "HoldOutline" (outline sélection)

    [Header("Config")]
    public bool isSunDie = false;   // uniquement pour le 5e dé (noir)
    public DiceSprites sprites;

    [Header("Surlignage Flash")]
    [Tooltip("Couleur du CADRE INTÉRIEUR affiché quand le dé fait partie d'un FLASH. " +
             "Le contour de sélection (HoldOutline, bleu) reste inchangé et s'affiche en plus.")]
    public Color flashHighlightColor = new Color(1f, 0.55f, 0.05f); // orange
    [Tooltip("Épaisseur (px) des barres du cadre intérieur de Flash.")]
    public float flashFrameThickness = 6f;
    [Tooltip("Marge (px) entre le bord du dé et le cadre intérieur.")]
    public float flashFrameInset = 5f;

    bool highlightIsFlash = false;
    GameObject flashFrame; // cadre intérieur généré à la volée (4 barres)

    [Header("Sun Die Visual")]
    [Tooltip("Si coché et isSunDie, affiche la face du dé en couleurs inversées (négatif).")]
    public bool invertSunDieColors = true;
    [Tooltip("Material d'inversion (optionnel). Laisser vide : créé automatiquement via le shader UI/Invert.")]
    public Material sunInvertMaterialOverride;

    static Material _sharedInvertMaterial;

    [Header("State (read-only)")]
    [SerializeField] private DieFace currentFace = DieFace.Two;
    public bool isLocked = false;           // verrou logique (score mis de côté)

    public Action<DieView> onClicked;       // assigne par GameManager

    private System.Random rng;

    void Awake()
    {
        rng = new System.Random();

        // Retrouve l'enfant "Face" si non assigné
        if (faceImage == null)
        {
            var faceTr = transform.Find("Face");
            if (faceTr != null) faceImage = faceTr.GetComponent<Image>();
        }

        // Retrouve d’éventuels enfants nommés par convention si non assignés
        if (lockIcon == null)
        {
            var t = transform.Find("LockIn");
            if (t) lockIcon = t.GetComponent<Image>();
        }
        if (holdHighlight == null)
        {
            var t = transform.Find("HoldOutline");
            if (t) holdHighlight = t.GetComponent<Image>();
        }

        // S’assurer qu’un Graphic sur CE GameObject reçoit les clics
        var selfImg = GetComponent<Image>();
        if (selfImg == null) selfImg = gameObject.AddComponent<Image>();
        selfImg.color = new Color(0, 0, 0, 0); // invisible
        selfImg.raycastTarget = true;

        if (lockIcon)      lockIcon.enabled = false;
        if (holdHighlight)
        {
            holdHighlight.enabled = false;
            holdHighlight.raycastTarget = false;
        }

        ApplySunDieMaterial();
    }

    // Applique le material "négatif" sur la face du 5e dé (isSunDie), pour le différencier visuellement.
    void ApplySunDieMaterial()
    {
        if (faceImage == null) return;
        if (!isSunDie || !invertSunDieColors) return;

        if (sunInvertMaterialOverride != null)
        {
            faceImage.material = sunInvertMaterialOverride;
            return;
        }

        if (_sharedInvertMaterial == null)
        {
            var sh = Shader.Find("UI/Invert");
            if (sh != null)
                _sharedInvertMaterial = new Material(sh) { name = "UIInvert (auto)" };
            else
                Debug.LogWarning("DieView: shader 'UI/Invert' introuvable. Ajoute-le à 'Always Included Shaders' (Project Settings > Graphics) pour les builds.");
        }

        if (_sharedInvertMaterial != null)
            faceImage.material = _sharedInvertMaterial;
    }

    public void ResetForNewTurn()
    {
        isLocked = false;
        SetFlashHighlight(false);
        if (lockIcon)      lockIcon.enabled = false;
        if (holdHighlight) holdHighlight.enabled = false;
        SetFaceSprite(null); // efface l’affichage jusqu’au premier roll
        StopAllCoroutines();
    }

    public void Roll()
    {
        var pool = isSunDie
            ? new List<DieFace> { DieFace.Two, DieFace.Four, DieFace.Five, DieFace.Six, DieFace.Ten, DieFace.Sun } // pas de 3
            : new List<DieFace> { DieFace.Two, DieFace.Three, DieFace.Four, DieFace.Five, DieFace.Six, DieFace.Ten };

        int idx = rng.Next(0, pool.Count);
        currentFace = pool[idx];
        SetFaceSprite(currentFace);
        Pulse(0.28f, 1.10f);
    }

    public DieFace GetFace() => currentFace;
    
    public void SetFace(DieFace newFace)
    {
        // public setter de face pour les pouvoirs
        // (DieView expose déjà SetFaceSprite et Pulse)
        // -> cf. structure actuelle, sprites & Pulse existent. 
        //    (tu as déjà Roll(), GetFace(), ResetForNewTurn(), etc.)
        //    On suit la même logique d’update visuelle. 
        //    :contentReference[oaicite:1]{index=1}
        var was = currentFace;
        currentFace = newFace;
        SetFaceSprite(currentFace);
        Pulse(0.22f, 1.08f);
    }

    public void SetLocked(bool locked)
    {
        isLocked = locked;
        if (lockIcon) lockIcon.enabled = locked;
        if (holdHighlight) holdHighlight.enabled = locked; // outline = miroir du lock
    }

    // Le dé fait-il partie d'un FLASH ? Un CADRE INTÉRIEUR orange épais s'affiche alors,
    // en plus du contour de sélection bleu (qui garde sa couleur de scène).
    // Piloté par GameManager (joueur ET adversaire).
    public void SetFlashHighlight(bool isFlash)
    {
        if (highlightIsFlash == isFlash) return;
        highlightIsFlash = isFlash;

        if (isFlash && flashFrame == null) BuildFlashFrame();
        if (flashFrame) flashFrame.SetActive(isFlash);
    }

    // Construit le cadre intérieur : 4 barres orange collées aux bords (avec marge),
    // rendues AU-DESSUS de la face du dé.
    void BuildFlashFrame()
    {
        flashFrame = new GameObject("FlashInnerFrame", typeof(RectTransform));
        var frt = (RectTransform)flashFrame.transform;
        frt.SetParent(transform, false);
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(flashFrameInset, flashFrameInset);
        frt.offsetMax = new Vector2(-flashFrameInset, -flashFrameInset);
        flashFrame.transform.SetAsLastSibling();

        float t = Mathf.Max(1f, flashFrameThickness);
        CreateFrameBar("Top",    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),   new Vector2(0f, t));
        CreateFrameBar("Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),   new Vector2(0f, t));
        CreateFrameBar("Left",   new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),   new Vector2(t, 0f));
        CreateFrameBar("Right",  new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),   new Vector2(t, 0f));
    }

    void CreateFrameBar(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size)
    {
        var bar = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)bar.transform;
        rt.SetParent(flashFrame.transform, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;

        var img = bar.AddComponent<Image>();
        img.color = flashHighlightColor;
        img.raycastTarget = false;
    }

    void SetFaceSprite(DieFace? face)
    {
        if (faceImage == null) return;
        if (!face.HasValue) { faceImage.sprite = null; return; }

        switch (face.Value)
        {
            case DieFace.Two:   faceImage.sprite = sprites.two; break;
            case DieFace.Three: faceImage.sprite = sprites.three; break;
            case DieFace.Four:  faceImage.sprite = sprites.four; break;
            case DieFace.Five:  faceImage.sprite = sprites.five; break;
            case DieFace.Six:   faceImage.sprite = sprites.six; break;
            case DieFace.Ten:   faceImage.sprite = sprites.ten; break;
            case DieFace.Sun:   faceImage.sprite = sprites.sun; break;
        }
        // Assure l’affichage même si le parent a un Image transparent
        faceImage.enabled = true;
        faceImage.preserveAspect = true;
    }

    // --- petite anim pulse ---
    public void Pulse(float duration = 0.5f, float peakScale = 1.12f)
    {
        if (!gameObject.activeInHierarchy) return;
        StopCoroutine(nameof(PulseRoutine));
        StartCoroutine(PulseRoutine(duration, peakScale));
    }
    System.Collections.IEnumerator PulseRoutine(float duration, float peak)
    {
        var t = 0f;
        var half = duration * 0.5f;
        var start = Vector3.one;
        var up = Vector3.one * peak;

        while (t < half) { t += Time.deltaTime; transform.localScale = Vector3.Lerp(start, up, t/half); yield return null; }
        t = 0f;
        while (t < half) { t += Time.deltaTime; transform.localScale = Vector3.Lerp(up, start, t/half); yield return null; }
        transform.localScale = Vector3.one;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        onClicked?.Invoke(this);
    }
    
}
