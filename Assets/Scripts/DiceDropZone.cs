using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Zone de SÉLECTION des dés : on y glisse le ou les dés que l'on veut garder.
// Elle remplace l'ancien mode de sélection (un simple calque de couleur posé sur le dé) :
// désormais un dé est sélectionné SI ET SEULEMENT SI il se trouve dans cette zone.
// L'adversaire l'utilise aussi : ses dés retenus y atterrissent pendant son tour.
//
// ===================== COMMENT CONSTRUIRE TA ZONE DANS LA SCÈNE =====================
// Le composant s'adapte à l'objet que TU crées ; rien n'est imposé.
//
//  1. Crée un UI > Image (ou un Panel) sous le Canvas, place-le et dimensionne-le librement.
//  2. Ajoute-lui ce composant DiceDropZone.
//  3. Assigne-le au champ « Dice Drop Zone » du GameManager.
//     (Si tu ne le fais pas, le GameManager en fabrique une par défaut au lancement.)
//
// Champs à renseigner selon ton habillage :
//  · Dice Container  : le RectTransform qui reçoit les dés. Laisse VIDE si les dés doivent
//                      s'aligner directement dans la zone. Renseigne-le si ta zone a un décor
//                      autour (titre, cadre, ornements) et une sous-zone dédiée aux dés.
//  · Background      : le Graphic (Image) teinté selon l'état — laisse vide pour qu'un voile
//                      soit créé, ou assigne TON image : sa couleur devient l'état « repos ».
//  · Frame Graphics  : les images de bordure teintées avec le fond (facultatif).
//  · Empty Hint      : l'objet affiché tant que la zone est vide (ton texte, ta flèche…).
//
// Décoche « Auto Build Visuals » pour que RIEN ne soit généré : seuls tes objets servent.
// Décoche « Auto Configure Layout » si tu gères toi-même le rangement des dés (voir le
// tooltip : le composant a besoin d'un LayoutGroup qui ne redimensionne PAS ses enfants).
//
// ⚠ Tout enfant du Dice Container qui n'est pas un dé sera rangé par le LayoutGroup comme
//   s'il en était un : place ton décor HORS du Dice Container, ou coche « Ignore Layout »
//   sur son LayoutElement.
// ====================================================================================
[RequireComponent(typeof(RectTransform))]
public class DiceDropZone : MonoBehaviour
{
    // État d'illumination de la zone pendant un glisser-déposer.
    public enum Mood
    {
        Off,     // repos
        Armed,   // un dé est saisi quelque part → la zone s'annonce
        Accept,  // le dé survole la zone et le dépôt sera accepté
        Deny     // le dé survole la zone mais ce dé n'est pas sélectionnable
    }

    [Header("Conteneur des dés")]
    [Tooltip("RectTransform qui reçoit les dés. Vide = la zone elle-même. À renseigner si ta " +
             "zone a un décor autour et une sous-zone dédiée à l'alignement des dés.")]
    public RectTransform diceContainer;

    [Header("Visuels (vide = généré automatiquement)")]
    [Tooltip("Image de fond teintée selon l'état. Si tu assignes la tienne, SA couleur devient " +
             "l'état « repos » : ton habillage reste intact hors glisser-déposer.")]
    public Graphic background;
    [Tooltip("Images de bordure teintées en même temps que le fond (facultatif).")]
    public Graphic[] frameGraphics;
    [Tooltip("Objet affiché tant que la zone est vide (invite du type « glisse tes dés ici »). " +
             "Il est masqué dès qu'un dé est posé, et pendant le tour de l'adversaire.")]
    public GameObject emptyHint;

    [Header("Génération automatique")]
    [Tooltip("Crée un voile, un cadre et un libellé pour CHAQUE référence laissée vide ci-dessus. " +
             "Décoche pour n'utiliser que tes propres objets.")]
    public bool autoBuildVisuals = true;
    [Tooltip("Met en place le rangement des dés dans le Dice Container. S'il n'a aucun LayoutGroup, " +
             "un HorizontalLayoutGroup est ajouté. S'il en a déjà un (le tien), sa disposition est " +
             "respectée : seuls « Control Child Size » (désactivé, sinon les dés seraient écrasés) " +
             "et « Use Child Scale » (activé, sinon les dés réduits laisseraient des trous) sont forcés.")]
    public bool autoConfigureLayout = true;

    [Header("Mise en page des dés")]
    [Tooltip("Espacement (px) entre deux dés. Appliqué uniquement au LayoutGroup créé par ce " +
             "composant : si tu poses le tien, c'est SON espacement qui compte.")]
    public float spacing = 18f;
    [Tooltip("Échelle des dés une fois dans la zone (ils y sont plus compacts que sur la table).")]
    [Range(0.3f, 1.5f)] public float dieScale = 0.65f;

    [Header("Couleurs d'état")]
    [Tooltip("Teinte le fond et les bordures selon l'état. Décoche si ton habillage ne doit " +
             "jamais changer de couleur (la pulsation d'opacité reste, si un CanvasGroup est fourni).")]
    public bool tintVisuals = true;
    [Tooltip("Couleur au repos, utilisée seulement pour les visuels générés automatiquement. " +
             "Un fond que TU assignes garde la couleur que tu lui as réglée.")]
    public Color idleColor   = new Color(1f, 1f, 1f, 0.10f);
    public Color armedColor  = new Color(1f, 1f, 1f, 0.22f);
    public Color acceptColor = new Color(0.30f, 0.89f, 0.47f, 0.34f); // vert CLAUSE
    public Color denyColor   = new Color(0.89f, 0.30f, 0.30f, 0.34f); // rouge points

    [Header("Pulsation")]
    [Tooltip("CanvasGroup dont l'opacité pulse pendant qu'un dé est porté (facultatif : sert si " +
             "tu veux faire respirer tout un habillage plutôt que la seule couleur du fond).")]
    public CanvasGroup pulseGroup;
    public float pulseSpeed = 3.5f;
    [Range(0f, 1f)] public float pulseMin = 0.55f;
    [Range(0f, 1f)] public float pulseMax = 1f;

    [Header("Cadre généré")]
    public float frameThickness = 3f;

    [Header("Libellé généré")]
    public string emptyHintText = "Glisse ici les dés à garder";
    public float emptyHintFontSize = 24f;

    [Header("Tolérance de dépôt")]
    [Tooltip("Marge (px) ajoutée autour de la zone pour tester le dépôt : viser juste devient " +
             "moins pénible sans agrandir le visuel.")]
    public float dropPadding = 40f;

    RectTransform rt;
    public RectTransform Rt => rt ? rt : (rt = (RectTransform)transform);

    // Conteneur réel des dés : c'est LUI que le GameManager vise, pas forcément la zone.
    public RectTransform DiceContainer => diceContainer ? diceContainer : Rt;

    // L'invite peut-elle s'afficher ? Piloté par le GameManager : inutile de proposer un
    // geste au joueur pendant le tour de l'adversaire, alors que la zone sert quand même.
    [System.NonSerialized] public bool hintAllowed = true;

    Mood mood = Mood.Off;
    bool built;

    // Couleurs d'origine des visuels (les tiennes si tu les as assignés) : elles sont
    // restaurées telles quelles au repos.
    Color authoredBackground;
    Color[] authoredFrame;
    float authoredPulseAlpha = 1f;

    void Awake() => Build();

    // Construction idempotente : le GameManager peut l'appeler juste après un AddComponent
    // (zone créée à la volée) sans attendre le Awake d'Unity.
    public void Build()
    {
        if (built) return;
        built = true;

        if (!diceContainer) diceContainer = Rt;

        if (autoBuildVisuals)
        {
            if (!background) background = CreateFill();
            if (frameGraphics == null || frameGraphics.Length == 0) frameGraphics = CreateFrame();
            if (!emptyHint) emptyHint = CreateHint();
        }

        CaptureAuthoredColors();
        if (autoConfigureLayout) ConfigureLayout();
        ApplyMood();
    }

    void CaptureAuthoredColors()
    {
        if (background) authoredBackground = background.color;
        if (frameGraphics != null)
        {
            authoredFrame = new Color[frameGraphics.Length];
            for (int i = 0; i < frameGraphics.Length; i++)
                if (frameGraphics[i]) authoredFrame[i] = frameGraphics[i].color;
        }
        if (pulseGroup) authoredPulseAlpha = pulseGroup.alpha;
    }

    void Update()
    {
        // Pulsation tant qu'un dé est porté : la zone « respire » pour attirer l'œil.
        if (mood != Mood.Off)
        {
            float k = (Mathf.Sin(Time.unscaledTime * pulseSpeed) + 1f) * 0.5f;
            float a = Mathf.Lerp(pulseMin, pulseMax, k);

            if (tintVisuals)
            {
                var c = MoodColor(mood);
                if (background) background.color = new Color(c.r, c.g, c.b, c.a * a);
                if (frameGraphics != null)
                    foreach (var g in frameGraphics)
                        if (g) g.color = new Color(c.r, c.g, c.b, Mathf.Lerp(0.55f, 1f, a));
            }
            if (pulseGroup) pulseGroup.alpha = authoredPulseAlpha * a;
        }

        RefreshHint();
    }

    // ==== Visuels générés (uniquement pour les références laissées vides) ====
    Graphic CreateFill()
    {
        var go = NewDecor("ZoneFill");
        var img = go.AddComponent<Image>();
        img.color = idleColor;
        img.raycastTarget = false;
        return img;
    }

    Graphic[] CreateFrame()
    {
        var holder = NewDecor("ZoneFrame");
        var h = (RectTransform)holder.transform;
        float t = Mathf.Max(1f, frameThickness);
        return new[]
        {
            Bar(h, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, t)),
            Bar(h, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, t)),
            Bar(h, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(t, 0f)),
            Bar(h, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(t, 0f)),
        };
    }

    GameObject CreateHint()
    {
        var go = NewDecor("ZoneHint");
        var label = go.AddComponent<TextMeshProUGUI>();
        label.text = emptyHintText;
        label.fontSize = emptyHintFontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(1f, 1f, 1f, 0.45f);
        label.raycastTarget = false;
        return go;
    }

    // Décor généré : étiré sur la zone et marqué hors mise en page, pour ne jamais occuper
    // une case de dé même quand le conteneur est la zone elle-même.
    GameObject NewDecor(string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var r = (RectTransform)go.transform;
        r.SetParent(transform, false);
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;

        var le = go.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
        return go;
    }

    static Graphic Bar(RectTransform parent, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 size)
    {
        var go = new GameObject("Bar", typeof(RectTransform));
        var r = (RectTransform)go.transform;
        r.SetParent(parent, false);
        r.anchorMin = aMin; r.anchorMax = aMax; r.pivot = pivot;
        r.sizeDelta = size; r.anchoredPosition = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    // ==== Rangement des dés ====
    // Un dé a une taille fixe (160×160 dans le prefab) et pas de LayoutElement : un LayoutGroup
    // qui « contrôle » la taille de ses enfants les réduirait à zéro. On force donc ce réglage,
    // ainsi que la prise en compte de l'échelle (sans elle, des dés réduits par dieScale
    // occuperaient la largeur du dé à taille réelle et laisseraient des trous).
    void ConfigureLayout()
    {
        var container = DiceContainer;
        var group = container.GetComponent<LayoutGroup>();

        if (!group)
        {
            var h = container.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleCenter;
            h.spacing = spacing;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            h.childControlWidth = h.childControlHeight = false;
            h.childScaleWidth = h.childScaleHeight = true;
            return;
        }

        // Ton LayoutGroup : on garde ta disposition (sens, alignement, espacement, marges)
        // et on ne corrige que les deux réglages incompatibles avec des dés à taille fixe.
        if (group is HorizontalOrVerticalLayoutGroup hv)
        {
            hv.childControlWidth = hv.childControlHeight = false;
            hv.childScaleWidth = hv.childScaleHeight = true;
        }
        else if (group is GridLayoutGroup grid)
        {
            // La grille impose sa cellSize : on l'aligne sur la taille affichée d'un dé,
            // sinon les dés déborderaient de leur case.
            Debug.LogWarning($"DiceDropZone ({name}) : un GridLayoutGroup impose la taille des " +
                             "cellules. Règle cellSize sur la taille d'un dé × Die Scale, ou " +
                             "préfère un Horizontal/VerticalLayoutGroup.", this);
        }
    }

    // ==== API utilisée par GameManager / DieView ====

    // Nombre de dés réellement posés (décors et fantômes de réservation exclus).
    public int DiceCount
    {
        get
        {
            var c = DiceContainer;
            int n = 0;
            for (int i = 0; i < c.childCount; i++)
                if (c.GetChild(i).GetComponent<DieView>()) n++;
            return n;
        }
    }

    // Vrai si le point écran tombe dans la zone (marge de tolérance comprise).
    public bool Contains(Vector2 screenPos, Camera cam)
    {
        if (!Rt) return false;
        if (RectTransformUtility.RectangleContainsScreenPoint(Rt, screenPos, cam)) return true;
        if (dropPadding <= 0f) return false;

        // Zone élargie : on repasse par le repère local pour appliquer la marge.
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Rt, screenPos, cam, out var local)) return false;
        var r = Rt.rect;
        return local.x >= r.xMin - dropPadding && local.x <= r.xMax + dropPadding
            && local.y >= r.yMin - dropPadding && local.y <= r.yMax + dropPadding;
    }

    public void SetMood(Mood m)
    {
        if (mood == m) return;
        mood = m;
        ApplyMood();
    }

    Color MoodColor(Mood m) => m switch
    {
        Mood.Armed  => armedColor,
        Mood.Accept => acceptColor,
        Mood.Deny   => denyColor,
        _ => idleColor
    };

    // Au repos on RESTAURE les couleurs d'origine : ton habillage reprend exactement
    // l'apparence que tu lui as donnée dans l'éditeur.
    void ApplyMood()
    {
        if (mood == Mood.Off)
        {
            if (tintVisuals)
            {
                if (background) background.color = authoredBackground;
                if (frameGraphics != null && authoredFrame != null)
                    for (int i = 0; i < frameGraphics.Length && i < authoredFrame.Length; i++)
                        if (frameGraphics[i]) frameGraphics[i].color = authoredFrame[i];
            }
            if (pulseGroup) pulseGroup.alpha = authoredPulseAlpha;
            return;
        }

        if (!tintVisuals) return;
        var c = MoodColor(mood);
        if (background) background.color = c;
        if (frameGraphics != null)
            foreach (var g in frameGraphics)
                if (g) g.color = new Color(c.r, c.g, c.b, 1f);
    }

    // L'invite ne s'affiche que quand la zone est vide ET que le joueur a la main :
    // sinon elle passerait sous les dés, ou proposerait un geste impossible.
    void RefreshHint()
    {
        if (!emptyHint) return;
        bool show = hintAllowed && DiceCount == 0;
        if (emptyHint.activeSelf != show) emptyHint.SetActive(show);
    }
}
