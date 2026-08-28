using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public enum DieFace { Two = 2, Three = 3, Four = 4, Five = 5, Six = 6, Ten = 10, Sun = 100 }

// Faces d'un dé STANDARD : pas de SUN (cette face n'existe que sur le dé noir).
[Serializable]
public class DiceSprites {
    public Sprite two, three, four, five, six, ten;
}

// Faces du dé NOIR (Sun) : le SUN remplace le 3 (comme dans le jeu d'origine). Ce dé a donc
// ses propres images pour 2, 4, 5, 6, 10 et SUN — pas de face 3 (l'image `three` n'est plus
// utilisée au tirage, conservée par compatibilité).
[Serializable]
public class BlackDiceSprites {
    public Sprite two, three, four, five, six, ten, sun;
}

public class DieView : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Wiring")]
    public Image faceImage;         // Image enfant "Face" (affiche la face)
    public Image lockIcon;          // Image "LockIn" (cadenas)
    public Image holdHighlight;     // Image "HoldOutline" (outline sélection)

    [Header("Config")]
    public bool isSunDie = false;   // uniquement pour le 5e dé (noir)
    public DiceSprites sprites;

    [Tooltip("Sprites du dé NOIR (die_2_b, die_3_b, …). Utilisés uniquement si isSunDie est coché. " +
             "Dès qu'au moins un est assigné, ils remplacent le shader d'inversion.")]
    public BlackDiceSprites blackSprites;

    // Le dé noir a-t-il son propre jeu d'images ? (sinon on garde l'ancien rendu en négatif)
    bool HasBlackSprites => isSunDie && blackSprites != null &&
        (blackSprites.two || blackSprites.three || blackSprites.four ||
         blackSprites.five || blackSprites.six || blackSprites.ten || blackSprites.sun);

    // Image du SUN partagée par tous les dés : un dé de BASE peut afficher un Sun posé par un
    // artefact (Miroir, Soleil en bouteille) alors que son jeu de sprites n'en contient pas.
    // Le premier dé noir configuré la publie ici pour éviter une face vide.
    static Sprite _sharedSunSprite;

    [Header("Taille de la face")]
    [Tooltip("Normalise la face à sa taille de référence (celle du prefab), centrée sur le dé. " +
             "L'affichage ne dépend donc ni de la résolution du PNG (300px, 384px…) ni d'un " +
             "LayoutGroup qui redimensionnerait la racine du dé. Décocher pour gérer à la main.")]
    public bool fitFaceToDie = true;
    [Tooltip("Marge (px) entre le bord du dé et la face.")]
    public float faceInset = 0f;
    [Tooltip("Conserve les proportions du PNG. Laisser DÉCOCHÉ (comportement d'origine du prefab) " +
             "pour que TOUTES les faces occupent exactement la même boîte, même si un PNG n'a pas " +
             "le même ratio que les autres (die_5 et die_10 font 384x390 au lieu de 384x384).")]
    public bool preserveFaceAspect = false;

    [Header("Surlignage Flash")]
    // La couleur du cadre de FLASH vient de la palette partagée (GameColors.Flash) pour rester
    // identique au jaune « FLASH » du journal d'actions. Elle n'est donc plus réglable par dé.
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

        // Publie l'image du SUN pour les dés de base (un artefact peut leur poser un Sun).
        if (blackSprites != null && blackSprites.sun) _sharedSunSprite = blackSprites.sun;

        ApplyFaceFit();
        ApplySunDieMaterial();
    }

    // Applique le material "négatif" sur la face du 5e dé (isSunDie), pour le différencier visuellement.
    void ApplySunDieMaterial()
    {
        if (faceImage == null) return;

        // Le dé noir a ses propres images (die_*_b) : plus besoin du négatif, et on retire
        // explicitement le material pour ne pas ré-inverser des sprites déjà noirs.
        if (HasBlackSprites) { faceImage.material = null; return; }

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
        SetClauseSelected(false); // annule le contour vert de CLAUSE
        SetDimmed(false); // annule un éventuel grisage (dé d'ajout réutilisé)
        if (lockIcon)      lockIcon.enabled = false;
        if (holdHighlight) holdHighlight.enabled = false;
        SetFaceSprite(null); // efface l’affichage jusqu’au premier roll
        StopAllCoroutines();
    }

    public void Roll()
    {
        // Dé noir (Sun) : comme dans le jeu d'origine, la face 3 est remplacée par un SUN.
        // Faces : 2, SUN, 4, 5, 6, 10 (pas de 3 ; le 10 existe, en noir via die_10_b).
        var pool = isSunDie
            ? new List<DieFace> { DieFace.Two, DieFace.Sun, DieFace.Four, DieFace.Five, DieFace.Six, DieFace.Ten }
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
        // Le contour vert de CLAUSE ne survit pas au déverrouillage (désélection, relance).
        if (!locked) SetClauseSelected(false);
    }

    // Contour VERT sur le dé qui dégage une CLAUSE (même vert que « CLAUSE » dans le journal).
    // On teinte le contour de sélection existant : la couleur d'origine est restaurée ensuite.
    bool clauseSelected = false;
    Color holdOriginalColor;
    bool holdColorCached = false;

    public void SetClauseSelected(bool on)
    {
        if (holdHighlight == null) return;
        if (clauseSelected == on) return;
        clauseSelected = on;

        if (!holdColorCached) { holdOriginalColor = holdHighlight.color; holdColorCached = true; }
        holdHighlight.color = on ? GameColors.Clause : holdOriginalColor;
    }

    // Grise le dé SANS le verrouiller : ni cadenas, ni contour de sélection bleu — juste
    // une face assombrie. Sert au dé d'ajout non marquant, pour montrer qu'il ne compte
    // PAS dans le score (le contour bleu laissait croire qu'il était retenu).
    public void SetDimmed(bool dimmed)
    {
        if (lockIcon) lockIcon.enabled = false;
        if (holdHighlight) holdHighlight.enabled = false;
        // Grisé mais OPAQUE (alpha 1) : l'ancienne alpha 0.75 donnait un rendu « transparent »
        // qui semblait buggé sur le dé d'ajout. On assombrit sans laisser voir à travers.
        if (faceImage) faceImage.color = dimmed ? new Color(0.5f, 0.5f, 0.5f, 1f) : Color.white;

        // Un dé grisé n'est plus interactif.
        var selfImg = GetComponent<Image>();
        if (selfImg) selfImg.raycastTarget = !dimmed;
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
        img.color = GameColors.Flash; // même jaune que « FLASH » dans le journal
        img.raycastTarget = false;
    }

    // Face d'un dé standard (pas de SUN dans ce jeu).
    static Sprite FromNormal(DiceSprites set, DieFace face)
    {
        if (set == null) return null;
        switch (face)
        {
            case DieFace.Two:   return set.two;
            case DieFace.Three: return set.three;
            case DieFace.Four:  return set.four;
            case DieFace.Five:  return set.five;
            case DieFace.Six:   return set.six;
            case DieFace.Ten:   return set.ten;
        }
        return null;
    }

    // Face du dé noir (Sun) : 2, 4, 5, 6, 10, SUN (le SUN remplace le 3 ; pas de face 3 au tirage).
    static Sprite FromBlack(BlackDiceSprites set, DieFace face)
    {
        if (set == null) return null;
        switch (face)
        {
            case DieFace.Two:   return set.two;
            case DieFace.Three: return set.three;
            case DieFace.Four:  return set.four;
            case DieFace.Five:  return set.five;
            case DieFace.Six:   return set.six;
            case DieFace.Ten:   return set.ten;
            case DieFace.Sun:   return set.sun;
        }
        return null;
    }

    void SetFaceSprite(DieFace? face)
    {
        if (faceImage == null) return;
        if (!face.HasValue) { faceImage.sprite = null; return; }

        // 1) Dé noir → son jeu d'images dédié.
        Sprite s = null;
        if (HasBlackSprites) s = FromBlack(blackSprites, face.Value);
        // 2) Sinon (ou face absente du jeu noir, ex: un 10 posé par un artefact) → jeu standard.
        if (s == null) s = FromNormal(sprites, face.Value);
        // 3) Un SUN sur un dé de base (Miroir, Soleil en bouteille) : image partagée du dé noir.
        if (s == null && face.Value == DieFace.Sun) s = _sharedSunSprite;
        faceImage.sprite = s;

        // Assure l’affichage même si le parent a un Image transparent
        faceImage.enabled = true;
        ApplyFaceFit();
    }

    // Fait épouser à la face la taille du dé : l'affichage ne dépend plus de la résolution
    // du PNG (300px, 384px…), seule la boîte du dé compte. preserveAspect évite la déformation.
    // Taille de référence de la face, capturée au démarrage (avant toute mise en page).
    Vector2 _faceBaseSize;
    bool _faceBaseCaptured;

    void CaptureFaceBaseSize()
    {
        if (_faceBaseCaptured || faceImage == null) return;
        _faceBaseCaptured = true;

        _faceBaseSize = faceImage.rectTransform.rect.size;
        // Replis si la face n'est pas encore dimensionnée : taille du dé, puis 160 (valeur du prefab).
        if (_faceBaseSize.x <= 1f || _faceBaseSize.y <= 1f) _faceBaseSize = ((RectTransform)transform).rect.size;
        if (_faceBaseSize.x <= 1f || _faceBaseSize.y <= 1f) _faceBaseSize = new Vector2(160f, 160f);
    }

    void ApplyFaceFit()
    {
        if (faceImage == null) return;
        faceImage.type = Image.Type.Simple;
        faceImage.preserveAspect = preserveFaceAspect;

        if (!fitFaceToDie) return;
        CaptureFaceBaseSize();

        // Ancres CENTRÉES + taille explicite (et non un stretch sur la racine) : la face garde
        // sa taille de référence même si un LayoutGroup/ContentSizeFitter redimensionne la racine
        // du dé — sinon elle grossirait et déborderait du contour, qui lui reste fixe.
        var rt = faceImage.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = _faceBaseSize - new Vector2(faceInset * 2f, faceInset * 2f);
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
    }

    // --- petite anim pulse ---
    // Pulse et Bounce partagent un même handle : une seule animation d'échelle à la fois
    // (évite qu'un Pulse déclenché par SetFace/Roll et un rebond de sélection ne se combattent).
    Coroutine _scaleCo;
    bool _bouncing; // un rebond de sélection est en cours → prioritaire sur un Pulse

    public void Pulse(float duration = 0.5f, float peakScale = 1.12f)
    {
        if (!gameObject.activeInHierarchy) return;
        if (_bouncing) return; // laisse le rebond de sélection se jouer entièrement
        if (_scaleCo != null) StopCoroutine(_scaleCo);
        _scaleCo = StartCoroutine(PulseRoutine(duration, peakScale));
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

    // ==== Ciblage de dé par un artefact : contour brillant au survol + rebond à la sélection ====
    // pickHoverEnabled est piloté par GameManager quand un artefact attend qu'on désigne un dé.
    [System.NonSerialized] public bool pickHoverEnabled;
    GameObject pickGlow;
    Image[] pickGlowBars;
    Coroutine pickGlowHandle;

    // Couleurs du dégradé du halo (haut → bas) : un bleu brillant vers cyan.
    static readonly Color GlowTop = new Color(0.42f, 0.85f, 1f);   // #6BD9FF
    static readonly Color GlowBottom = new Color(0.30f, 0.56f, 0.89f); // #4C90E3

    public void SetPickHoverEnabled(bool on)
    {
        pickHoverEnabled = on;
        if (!on) ShowPickGlow(false);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (pickHoverEnabled) ShowPickGlow(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        ShowPickGlow(false);
    }

    void ShowPickGlow(bool on)
    {
        if (on && pickGlow == null) BuildPickGlow();
        if (pickGlow) pickGlow.SetActive(on);

        if (on)
        {
            if (pickGlowHandle == null && gameObject.activeInHierarchy)
                pickGlowHandle = StartCoroutine(PickGlowPulse());
        }
        else if (pickGlowHandle != null)
        {
            StopCoroutine(pickGlowHandle);
            pickGlowHandle = null;
        }
    }

    void BuildPickGlow()
    {
        pickGlow = new GameObject("PickGlow", typeof(RectTransform));
        var rt = (RectTransform)pickGlow.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-6f, -6f); // déborde un peu → effet de halo
        rt.offsetMax = new Vector2(6f, 6f);
        pickGlow.transform.SetAsLastSibling();

        const float th = 5f;
        pickGlowBars = new Image[4];
        // Barres haut/bas prennent la couleur du dégradé aux extrémités ; gauche/droite une teinte médiane.
        var mid = Color.Lerp(GlowTop, GlowBottom, 0.5f);
        pickGlowBars[0] = MakeGlowBar(rt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, th), GlowTop);
        pickGlowBars[1] = MakeGlowBar(rt, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, th), GlowBottom);
        pickGlowBars[2] = MakeGlowBar(rt, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(th, 0f), mid);
        pickGlowBars[3] = MakeGlowBar(rt, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(th, 0f), mid);
    }

    static Image MakeGlowBar(RectTransform parent, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 size, Color col)
    {
        var bar = new GameObject("GlowBar", typeof(RectTransform));
        var rt = (RectTransform)bar.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
        rt.sizeDelta = size; rt.anchoredPosition = Vector2.zero;
        var img = bar.AddComponent<Image>();
        img.color = col;
        img.raycastTarget = false;
        return img;
    }

    // Pulse d'opacité du halo pour un effet « brillant ».
    System.Collections.IEnumerator PickGlowPulse()
    {
        while (true)
        {
            float a = Mathf.Lerp(0.4f, 1f, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4.5f));
            if (pickGlowBars != null)
                foreach (var b in pickGlowBars)
                    if (b) { var c = b.color; c.a = a; b.color = c; }
            yield return null;
        }
    }

    // Rebond joué quand le dé est SÉLECTIONNÉ par un artefact (gonfle puis revient en oscillant).
    public void Bounce()
    {
        if (!gameObject.activeInHierarchy) return;
        if (_scaleCo != null) StopCoroutine(_scaleCo);
        _bouncing = true;
        _scaleCo = StartCoroutine(BounceRoutine());
    }

    System.Collections.IEnumerator BounceRoutine()
    {
        const float dur = 0.4f;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / dur;
            // Oscillation amortie autour de 1 : rebond visible qui se stabilise.
            float s = 1f + 0.30f * Mathf.Sin(t * Mathf.PI * 3f) * (1f - t);
            transform.localScale = Vector3.one * s;
            yield return null;
        }
        transform.localScale = Vector3.one;
        _bouncing = false;
    }
    
}
