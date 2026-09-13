using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

// ============================ PORTRAIT DE L'ADVERSAIRE EN 3D ============================
// Le portrait adverse (Portrait_P2) cesse d'être une image fixe : un personnage modélisé y
// joue une animation choisie par la situation de jeu.
//
// MÊME RECETTE QUE Dice3DRenderer
//   Le canvas est en ScreenSpaceOverlay, dessiné APRÈS toutes les caméras : un personnage posé
//   « devant » l'UI passerait derrière toute l'interface. On le fait donc vivre TRÈS LOIN du
//   monde de jeu, on le filme avec une caméra dédiée, et on affiche sa RenderTexture dans un
//   RawImage — qui reste un élément d'UI parfaitement normal.
//
// POURQUOI LE RawImage EST UN ENFANT DE Portrait_P2, ET PAS SON REMPLAÇANT
//   GameManager laisse volontairement l'Image de Portrait_P2 ACTIVÉE avec une couleur
//   transparente : un Graphic désactivé ne reçoit aucun raycast, et le survol d'information
//   ennemi (nom, description, trait) cesserait de sortir. On glisse donc le RawImage DEDANS,
//   en raycastTarget = false. Résultat : le survol, la mise en page et les contours de tour
//   continuent de fonctionner sans qu'une ligne de leur code ne change.
//
// INSTALLATION
//   1. Un GameObject vide dans SampleScene, n'importe où. Ajoute-lui ce composant.
//   2. Renseigne « Test Character » avec le FBX du personnage (le fichier lui-même fait
//      l'affaire, pas besoin de prefab) — il s'affichera pour TOUS les adversaires.
//   3. Crée un Animator Controller avec un état nommé « Idle », glisse-le dans « Controller ».
//      Les états manquants (Wait, Roll, Win, Lose) retombent automatiquement sur Idle : tu peux
//      donc commencer avec une seule animation et compléter au fur et à mesure.
//   4. Glisse ce composant dans le champ « Portrait 3D » du GameManager.
//
// ÉCLAIRAGE
//   Les lampes sont des POINT LIGHTS créées à l'exécution et parentées à la scène du portrait,
//   à 10 000 unités du jeu. Leur portée limitée les rend physiquement incapables d'atteindre
//   quoi que ce soit d'autre : aucun réglage global (ambiance, lumière directionnelle) n'est
//   touché, donc rien n'empiète sur le reste du jeu. Elles sont réglables EN COURS DE PARTIE —
//   cadrage compris — pour pouvoir composer l'image sans relancer.
//
// POST-PROCESSING
//   Délibérément laissé désactivé sur cette caméra (c'est le défaut d'une caméra créée par
//   script sous URP). Le blit final du post-processing écrit alpha = 1 et ferait apparaître un
//   rectangle opaque derrière le personnage — exactement le symptôme documenté sur les dés.
// =======================================================================================

public enum PortraitState { Idle, Wait, Roll, Win, Lose }

[System.Serializable]
public class PortraitLamp
{
    public string name = "Lampe";
    public bool enabled = true;
    [Tooltip("Position par rapport aux PIEDS du personnage.")]
    public Vector3 position = new Vector3(1.2f, 2.2f, -1.6f);
    public Color color = Color.white;
    [Min(0f)] public float intensity = 8f;
    [Min(0.1f)] public float range = 12f;
}

[ExecuteAlways]
public class Portrait3DRenderer : MonoBehaviour
{
    [Header("Aperçu dans l'éditeur")]
    [Tooltip("Monte le décor et rend le portrait SANS lancer le jeu. Les lampes et la caméra " +
             "apparaissent dans la Hierarchy : tu les sélectionnes, tu les déplaces à la souris " +
             "dans la Scene view, et le portrait se met à jour en direct dans le Canvas.\n\n" +
             "Une lampe déplacée à la main est relue et rangée dans la liste ci-dessous : tes " +
             "réglages survivent donc à la reconstruction du décor, contrairement à ce qu'on " +
             "bricole en Play Mode.")]
    public bool previewInEditor = true;

    [Header("Emplacement dans l'UI")]
    [Tooltip("Portrait_P2. Laisser vide : le GameManager y met son opponentPortraitImage au " +
             "démarrage, et à défaut on le retrouve par son nom.")]
    public Image slot;
    [Tooltip("Nom de l'objet cherché dans la scène si « Slot » est vide.")]
    public string slotObjectName = "Portrait_P2";

    [Header("Personnage")]
    [Tooltip("PHASE DE TEST : ce modèle s'affiche pour TOUS les adversaires, quel que soit " +
             "l'ennemi en face. Le laisser vide pour revenir au portrait 2D d'origine.")]
    public GameObject testCharacter;
    [Tooltip("Animator Controller partagé. Ses états doivent s'appeler Idle, Wait, Roll, Win, " +
             "Lose. Assigné automatiquement au personnage instancié s'il n'en a pas.")]
    public RuntimeAnimatorController controller;

    [Header("Cadrage")]
    [Tooltip("Angle de champ. Petit = presque orthographique (portrait posé), grand = " +
             "perspective marquée (portrait qui vient vers le joueur).")]
    [Range(10f, 60f)] public float fieldOfView = 28f;
    [Tooltip("Le cadrage est calculé sur la TAILLE RÉELLE du modèle, mesurée à l'instanciation. " +
             "Un personnage de 0,2 ou de 20 unités se cadre donc pareil, sans rien régler. " +
             "Décoche pour revenir à un placement de caméra manuel (les deux champs du bas).")]
    public bool autoFrame = true;
    [Tooltip("Part de la HAUTEUR du personnage tenue dans l'image. 1 = corps entier, " +
             "0,45 = buste, 0,20 = gros plan visage.")]
    [Range(0.05f, 1.2f)] public float frameHeight01 = 0.45f;
    [Tooltip("Où se situe le centre du cadre sur le personnage. 0 = pieds, 1 = sommet du crâne.")]
    [Range(0f, 1.2f)] public float frameCenter01 = 0.82f;
    [Tooltip("Tour de la caméra AUTOUR du personnage, en degrés. 0 = de face.")]
    [Range(-180f, 180f)] public float orbitYaw = 0f;
    [Tooltip("Hauteur du regard de la caméra. Positif = vue légèrement plongeante.")]
    [Range(-60f, 60f)] public float orbitPitch = 6f;

    [Header("Cadrage manuel (si Auto Frame est décoché)")]
    [Tooltip("Position de la caméra par rapport aux PIEDS du personnage.")]
    public Vector3 cameraOffset = new Vector3(0f, 1.55f, -3.2f);
    [Tooltip("Point visé par la caméra, par rapport aux pieds. Monte-le pour cadrer le visage.")]
    public Vector3 lookAtOffset = new Vector3(0f, 1.45f, 0f);

    [Header("Personnage : orientation")]
    [Tooltip("Rotation du personnage sur lui-même, en degrés. Évite de ré-exporter depuis " +
             "Blender pour un simple trois-quarts.")]
    [Range(-180f, 180f)] public float characterYaw = 0f;
    [Min(0.01f)] public float characterScale = 1f;

    [Header("Éclairage")]
    [Tooltip("Lampes locales à la scène du portrait. Réglables en cours de partie. Positions et " +
             "portées sont exprimées pour un personnage d'1,8 unité, et remises à l'échelle du " +
             "tien automatiquement : tu n'as pas à les retoucher si ton modèle est plus petit.")]
    public PortraitLamp[] lamps =
    {
        new PortraitLamp { name = "Key",  position = new Vector3( 1.3f, 2.3f, -1.7f),
                           color = new Color(1f, 0.96f, 0.90f), intensity = 9f,  range = 12f },
        new PortraitLamp { name = "Fill", position = new Vector3(-1.7f, 1.5f, -1.5f),
                           color = new Color(0.82f, 0.88f, 1f),  intensity = 3.5f, range = 12f },
        new PortraitLamp { name = "Rim",  position = new Vector3(-0.7f, 2.5f,  2.1f),
                           color = new Color(0.90f, 0.94f, 1f),  intensity = 7f,  range = 10f },
    };
    [Tooltip("Les ombres portées sur du vide donnent des artefacts. À n'activer que si tu " +
             "ajoutes un sol au personnage.")]
    public bool castShadows = false;

    [Header("États")]
    [Tooltip("Secondes d'inaction du joueur avant que l'adversaire ne s'impatiente (Wait).")]
    [Min(0.5f)] public float waitDelay = 5f;
    [Tooltip("Durée pendant laquelle Roll garde la main avant de rendre au repos. Règle-la sur " +
             "la longueur réelle de ton animation de lancer.")]
    [Min(0.1f)] public float rollHold = 1.7f;
    [Tooltip("Durée du fondu entre deux états.")]
    [Range(0f, 1f)] public float crossFade = 0.15f;

    [Header("Rendu")]
    [Tooltip("Taille la RenderTexture sur le nombre RÉEL de pixels qu'occupe Portrait_P2 à " +
             "l'écran. Le RawImage n'a alors plus rien à rééchantillonner — c'est ce qui " +
             "supprime le scintillement des détails fins. Décoche pour imposer une hauteur fixe.")]
    public bool matchScreenPixels = true;
    [Tooltip("Rendu en 2× puis réduit : silhouette et texture nettement plus stables, pour " +
             "quatre fois plus de pixels à calculer. Sur un portrait, ça reste dérisoire.")]
    [Range(1, 2)] public int supersampling = 2;
    [Tooltip("Hauteur imposée si « Match Screen Pixels » est décoché. La largeur suit les " +
             "proportions de Portrait_P2.")]
    [Min(64)] public int renderTextureHeight = 900;
    [Tooltip("Anti-aliasing (1, 2, 4 ou 8). Sans lui, la silhouette du personnage crénelle.")]
    public int msaa = 4;
    [Tooltip("Alpha à 0 = fond transparent. Si un rectangle opaque apparaît derrière le " +
             "personnage, mets ici la couleur de la table.")]
    public Color backgroundColor = new Color(0f, 0f, 0f, 0f);
    [Tooltip("Index, dans la « Renderer List » de ton asset URP, du renderer que doit utiliser " +
             "la caméra du portrait.\n\n" +
             "Ton projet est bâti sur le renderer 2D (template URP 2D) : il ignore purement et " +
             "simplement les Light classiques, il ne connaît que les Light2D. Aucun réglage " +
             "d'intensité ne pouvait donc éclairer le personnage.\n\n" +
             "Ajoute un « URP Universal Renderer » à la liste de UniversalRP.asset — il prendra " +
             "l'index 1, le renderer 2D restant à l'index 0 par défaut pour tout le reste du " +
             "jeu — et laisse ce champ à 1. Mets -1 pour ne rien imposer.")]
    public int rendererIndex = 1;
    [Tooltip("Où vit le personnage : hors du champ de la caméra du jeu. Évite d'avoir à " +
             "réserver un Layer dans les réglages du projet.\n\n" +
             "NE MONTE PAS CETTE VALEUR. Un float perd sa précision à mesure qu'on s'éloigne " +
             "de l'origine : à 13 000 unités (l'ancien réglage), le plus petit écart " +
             "représentable vaut déjà ~1,6 mm, soit près d'un pixel de frémissement sur un " +
             "personnage cadré en gros plan. À 500, il tombe à 0,06 mm — invisible. " +
             "500 unités suffisent largement à sortir du champ de la caméra du jeu.")]
    public Vector3 stageOrigin = new Vector3(0f, 500f, 0f);

    [Header("Diagnostic")]
    [Tooltip("Écrit UNE ligne dans la console une seconde après le démarrage : emplacement " +
             "trouvé, personnage instancié, taille mesurée, texture, caméra. C'est ce qui dit " +
             "en un coup d'œil pourquoi rien ne s'affiche.")]
    public bool logDiagnostic = true;

    // ---- Construction interne ----
    Transform stage;    // racine, très loin du jeu : porte la caméra et les lampes
    Transform mount;    // pieds du personnage : c'est ici qu'on l'instancie
    Camera cam;
    RenderTexture rt;
    RawImage raw;
    Light[] lights;
    Vector3?[] lastLampPos;     // dernière position ÉCRITE : sert à détecter un déplacement manuel

    GameObject current;         // instance à l'écran
    GameObject currentSource;   // asset dont elle provient (évite de reconstruire pour rien)
    Animator animator;
    bool warnedNoController;

    // ---- Mesure du modèle (à l'échelle 1) : base de tout le cadrage ----
    float modelHeight = 1.8f;
    float modelBottomLocal;     // distance des pieds au pivot du personnage
    int modelRendererCount;
    float diagnosticAt = -1f;
    bool diagnosticDone;

    // ---- État courant ----
    PortraitState state = PortraitState.Idle;
    int appliedHash;            // état réellement envoyé à l'Animator (≠ state en cas de repli)
    float lastActionTime;       // dernière action du joueur : sert au déclenchement de Wait
    float ambientBlockedUntil;  // un one-shot court (Roll) garde la main jusque-là
    bool frozen;                // Win / Lose : plus rien ne reprend la main
    bool contextInPlay = true;
    bool contextPlayersTurn = true;

    public bool IsShowingCharacter => current != null;

    // =============================== CYCLE DE VIE ===============================
    //
    // Tout est monté dans OnEnable et démonté dans OnDisable, et non plus dans Awake/Start.
    // C'est ce qui permet à [ExecuteAlways] de fonctionner : hors Play Mode, l'éditeur
    // appelle OnEnable/OnDisable à chaque recompilation et à chaque ouverture de scène, là
    // où Awake ne serait pas rejoué. Rien n'est sérialisé (HideFlags.DontSave) : la scène
    // ne garde aucune trace du décor, il se reconstruit tout seul.

    void OnEnable()
    {
        lastActionTime = Time.unscaledTime;
        if (!Application.isPlaying && !previewInEditor) return;

        BuildStage();
        if (testCharacter) SetCharacter(testCharacter);
    }

    void OnDisable()
    {
        Teardown();
    }

    void Teardown()
    {
        if (cam) cam.targetTexture = null;
        if (rt) { rt.Release(); DestroyAnyMode(rt); rt = null; }
        if (raw) { DestroyAnyMode(raw.gameObject); raw = null; }
        // La scène du portrait est un objet racine : elle ne disparaît pas d'elle-même.
        if (stage) { DestroyAnyMode(stage.gameObject); stage = null; }

        cam = null; mount = null; lights = null;
        current = null; currentSource = null; animator = null;
        appliedHash = 0;
    }

    // Destroy est interdit hors Play Mode : l'éditeur exige DestroyImmediate. Sans cette
    // bascule, tout le montage fuiterait dès la première recompilation.
    static void DestroyAnyMode(Object o)
    {
        if (!o) return;
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }

    void Update()
    {
        // Hors Play Mode, on ne tourne que si l'aperçu est demandé — et la case peut être
        // décochée à tout moment, d'où le démontage ici plutôt qu'au seul OnDisable.
        if (!Application.isPlaying)
        {
            if (!previewInEditor) { if (stage) Teardown(); return; }
            if (!stage) { BuildStage(); if (testCharacter) SetCharacter(testCharacter); }
        }

        ReportDiagnosticOnce();

        if (!ResolveSlot()) return;

        EnsureRenderTextureMatchesSlot();

        // Visibilité synchronisée ICI et pas dans SetCharacter : le personnage est posé au
        // Start, alors que le RawImage n'existe qu'à la première frame où Portrait_P2 a été
        // retrouvé. Régler la visibilité au moment du chargement la perdrait.
        // La RenderTexture est exigée pour allumer la caméra : une caméra active SANS cible
        // rendrait la scène du portrait par-dessus tout l'écran de jeu.
        bool visible = current != null && rt != null;
        if (raw.enabled != visible) raw.enabled = visible;
        if (cam && cam.enabled != visible) cam.enabled = visible;

        ApplyFraming();   // caméra + lampes appliquées chaque frame : réglable en cours de partie
        RefreshAmbientState();
    }

    // Une seule ligne de console, une seconde après le démarrage : de quoi voir immédiatement
    // lequel des maillons manque quand le portrait reste vide.
    void ReportDiagnosticOnce()
    {
        if (!logDiagnostic || diagnosticDone) return;
        if (diagnosticAt < 0f) { diagnosticAt = Time.unscaledTime + 1f; return; }
        if (Time.unscaledTime < diagnosticAt) return;
        diagnosticDone = true;

        string emplacement = slot ? $"OK ({slot.name})" : $"INTROUVABLE — assigne « Slot », ou vérifie qu'un objet s'appelle « {slotObjectName} »";
        string perso = current ? $"OK ({current.name}, {modelRendererCount} renderer(s), hauteur mesurée {modelHeight:0.###} u)"
                               : (testCharacter ? "instanciation ÉCHOUÉE" : "AUCUN — renseigne « Test Character »");
        string texture = rt ? $"{rt.width}×{rt.height}" : "NON CRÉÉE (Portrait_P2 a-t-il une taille non nulle ?)";
        string anim = !animator ? "aucun Animator sur le modèle"
                    : (animator.runtimeAnimatorController ? $"controller « {animator.runtimeAnimatorController.name} »"
                                                          : "PAS DE CONTROLLER — pose de repos");
        // Le shader dit si la lumière peut avoir le moindre effet : un Unlit ou un matériau
        // rendu par le renderer 2D ne réagira à aucune lampe, quelle qu'en soit l'intensité.
        string shaders = "—";
        if (current)
        {
            var vus = new System.Collections.Generic.HashSet<string>();
            foreach (var r in current.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    vus.Add(m ? (m.shader ? m.shader.name : "matériau sans shader") : "slot vide");
            if (vus.Count > 0) shaders = string.Join(" + ", vus);
        }

        string cadrage = !cam ? "pas de caméra"
                       : autoFrame ? $"auto, caméra à {Vector3.Distance(cam.transform.position, mount.position):0.##} u"
                                   : "manuel";

        // Éloignement de la scène : c'est un tremblement de PRÉCISION FLOTTANTE, invisible à
        // l'arrêt et très visible au FOV serré d'un portrait. On le chiffre en pixels plutôt
        // que de laisser chercher.
        string precision = "";
        float d = stageOrigin.magnitude;
        if (d > 2000f && cam && rt)
        {
            float ulp = d * 1.1920929e-7f;                       // pas d'un float à cette distance
            float dist = Vector3.Distance(cam.transform.position, mount.position);
            float px = (dist > 0.0001f)
                     ? (Mathf.Atan2(ulp, dist) * Mathf.Rad2Deg) * (rt.height / Mathf.Max(1f, fieldOfView))
                     : 0f;
            precision = $" | ⚠ Stage Origin à {d:0} u : ~{px:0.0} px de frémissement à ce FOV — " +
                        $"mets (0, 500, 0)";
        }

        Debug.Log($"[Portrait3D] emplacement : {emplacement} | personnage : {perso} | " +
                  $"texture : {texture} | caméra : {(cam && cam.enabled ? "active" : "éteinte")} ({cadrage}) | " +
                  $"animation : {anim} | shader(s) : {shaders} | renderer imposé : " +
                  $"{(rendererIndex >= 0 ? rendererIndex.ToString() : "aucun")}{precision}", this);
    }

    // =============================== CONSTRUCTION ===============================

    void BuildStage()
    {
        // La scène 3D, très loin du jeu. La caméra est accrochée à la RACINE et le personnage
        // à un enfant séparé : s'ils partageaient le même transform, faire tourner le
        // personnage ferait tourner la caméra avec lui.
        stage = new GameObject("Portrait3D Stage").transform;
        stage.position = stageOrigin;
        // Visible et sélectionnable dans la Hierarchy — c'est ce qui rend les lampes
        // manipulables — mais jamais sérialisé : la scène ne garde aucune trace du décor.
        stage.gameObject.hideFlags = HideFlags.DontSave;

        mount = new GameObject("Mount").transform;
        mount.SetParent(stage, false);

        var camGo = new GameObject("Camera");
        camGo.transform.SetParent(stage, false);
        cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = backgroundColor;
        cam.orthographic = false;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 60f;
        cam.allowHDR = false;    // HDR + post-processing = alpha écrasé à 1
        cam.allowMSAA = true;
        cam.enabled = false;     // activée seulement quand un personnage est à l'écran

        // Le renderer de CETTE caméra seulement. Le jeu garde le renderer 2D par défaut :
        // l'UI, les dés et tout le reste sont rendus exactement comme avant.
        var camData = cam.GetUniversalAdditionalCameraData();
        if (camData != null)
        {
            if (rendererIndex >= 0) camData.SetRenderer(rendererIndex);
            // Explicite : le blit final du post-processing écrit alpha = 1 et ferait
            // apparaître un rectangle opaque derrière le personnage.
            camData.renderPostProcessing = false;
        }

        BuildLights();
    }

    void BuildLights()
    {
        int n = (lamps != null) ? lamps.Length : 0;
        lights = new Light[n];
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject($"Lamp_{lamps[i].name}");
            go.transform.SetParent(stage, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.renderMode = LightRenderMode.ForcePixel;
            lights[i] = l;
        }
    }

    // Retrouve Portrait_P2 et y installe le RawImage. Fait à la première frame utile plutôt
    // qu'au Awake : le GameManager peut nous passer son opponentPortraitImage entre-temps.
    bool ResolveSlot()
    {
        if (raw) return true;
        if (!slot && !string.IsNullOrEmpty(slotObjectName))
        {
            foreach (var img in FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (img.gameObject.name == slotObjectName) { slot = img; break; }
        }
        if (!slot) return false;

        var go = new GameObject("Portrait3D", typeof(RectTransform));
        var rrt = (RectTransform)go.transform;
        rrt.SetParent(slot.rectTransform, false);
        rrt.anchorMin = Vector2.zero;      // épouse exactement le cadre du portrait 2D
        rrt.anchorMax = Vector2.one;
        rrt.offsetMin = Vector2.zero;
        rrt.offsetMax = Vector2.zero;

        go.hideFlags = HideFlags.DontSave; // ne pas accumuler un « Portrait3D » par recompilation
        raw = go.AddComponent<RawImage>();
        raw.raycastTarget = false;         // le survol doit atteindre Portrait_P2, en dessous
        raw.enabled = false;
        return true;
    }

    // La RenderTexture prend les PROPORTIONS de Portrait_P2 : sans ça, un portrait plus haut
    // que large écraserait le personnage.
    void EnsureRenderTextureMatchesSlot()
    {
        var rect = slot.rectTransform.rect;
        if (rect.height < 1f || rect.width < 1f) return;

        int w, h;
        if (matchScreenPixels)
        {
            // Le Canvas Scaler du jeu est en 1920×1080 « Expand » : la taille du rect n'est
            // PAS la taille à l'écran. On multiplie donc par le facteur d'échelle du canvas,
            // sinon la RenderTexture est rééchantillonnée dans un rapport arbitraire — et un
            // rapport arbitraire, sur des détails fins, se voit comme un frémissement.
            float k = (slot.canvas ? slot.canvas.scaleFactor : 1f) * Mathf.Clamp(supersampling, 1, 2);
            w = Mathf.RoundToInt(rect.width * k);
            h = Mathf.RoundToInt(rect.height * k);
        }
        else
        {
            h = renderTextureHeight;
            w = Mathf.RoundToInt(h * (rect.width / rect.height));
        }
        w = Mathf.Clamp(w, 64, 4096);
        h = Mathf.Clamp(h, 64, 4096);

        // Hystérésis. Un layout qui respire d'un demi-pixel ferait basculer l'arrondi d'une
        // frame à l'autre, donc reconstruirait la texture en boucle : le tremblement serait
        // causé par le correctif lui-même.
        const int seuil = 4;
        if (rt && Mathf.Abs(rt.width - w) <= seuil && Mathf.Abs(rt.height - h) <= seuil) return;

        if (cam) cam.targetTexture = null;
        if (rt) { rt.Release(); DestroyAnyMode(rt); }

        rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
        {
            name = "Portrait3D RT",
            antiAliasing = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(1, msaa)), 1, 8),
            filterMode = FilterMode.Bilinear,
            useMipMap = false,
            autoGenerateMips = false,
        };
        rt.Create();

        cam.targetTexture = rt;
        raw.texture = rt;
    }

    void ApplyFraming()
    {
        if (!cam) return;

        cam.fieldOfView = fieldOfView;
        cam.backgroundColor = backgroundColor;

        mount.localRotation = Quaternion.Euler(0f, characterYaw, 0f);
        mount.localScale = Vector3.one * Mathf.Max(0.01f, characterScale);

        // Taille du personnage À L'ÉCRAN : la mesure faite à l'instanciation, remise à l'échelle
        // courante. Tout le cadrage et l'éclairage en découlent, ce qui rend le composant
        // insensible à l'unité choisie dans Blender.
        float h = Mathf.Max(0.0001f, modelHeight * Mathf.Max(0.01f, characterScale));
        Vector3 feet = mount.position + Vector3.up * (modelBottomLocal * Mathf.Max(0.01f, characterScale));

        if (autoFrame)
        {
            // Le cadre tient `frameHeight01` de la hauteur du personnage, centré à la hauteur
            // `frameCenter01`. La distance s'en déduit par la trigonométrie du champ de vision :
            // changer le FOV recule ou avance la caméra au lieu de recadrer.
            Vector3 target = new Vector3(mount.position.x,
                                         feet.y + h * frameCenter01,
                                         mount.position.z);
            float fit = Mathf.Max(0.001f, h * frameHeight01);
            float dist = (fit * 0.5f) / Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);

            Quaternion rot = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            cam.transform.SetPositionAndRotation(target - rot * Vector3.forward * dist, rot);

            // Les plans de coupe suivent aussi la taille du modèle : à l'échelle Blender
            // (centimètres, par exemple), un near à 5 cm mangerait le personnage entier.
            cam.nearClipPlane = Mathf.Max(0.001f, dist * 0.02f);
            cam.farClipPlane = dist * 8f + h * 4f;
        }
        else
        {
            cam.transform.position = feet + cameraOffset;
            cam.transform.LookAt(feet + lookAtOffset);
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 60f;
        }

        if (lights == null || lamps == null) return;

        // Les lampes sont écrites pour un personnage d'1,8 unité : on les met à SON échelle.
        float k = Mathf.Max(0.0001f, h / 1.8f);
        if (lastLampPos == null || lastLampPos.Length != lights.Length)
            lastLampPos = new Vector3?[lights.Length];

        for (int i = 0; i < lights.Length && i < lamps.Length; i++)
        {
            var l = lights[i];
            var d = lamps[i];
            if (!l) continue;

            Vector3 voulue = feet + d.position * k;

            // ÉDITION À LA SOURIS. Si la lampe n'est plus là où on l'avait posée, c'est que
            // quelqu'un l'a déplacée dans la Scene view : on relit sa position et on la range
            // dans les données. Sans ça, la ligne suivante la remettrait en place à chaque
            // frame et le gizmo serait inutilisable.
            if (lastLampPos[i].HasValue &&
                (l.transform.position - lastLampPos[i].Value).sqrMagnitude > 1e-8f)
            {
                d.position = (l.transform.position - feet) / k;
                voulue = l.transform.position;
#if UNITY_EDITOR
                if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
#endif
            }

            l.transform.position = voulue;
            lastLampPos[i] = voulue;

            l.enabled = d.enabled;
            l.color = d.color;
            l.intensity = d.intensity;
            l.range = Mathf.Max(0.01f, d.range * k);
            l.shadows = castShadows ? LightShadows.Soft : LightShadows.None;
        }
    }

    // Mesure le personnage instancié. On prend l'union des Renderers plutôt que la seule
    // hiérarchie d'os : c'est la géométrie visible qu'on veut cadrer, pas le squelette (dont
    // un os racine peut traîner très loin sous les pieds).
    void MeasureModel()
    {
        modelHeight = 1.8f;
        modelBottomLocal = 0f;
        modelRendererCount = 0;
        if (!current) return;

        var rends = current.GetComponentsInChildren<Renderer>(true);
        modelRendererCount = rends.Length;
        if (rends.Length == 0) return;

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

        // Ramené à l'échelle 1 : ApplyFraming réappliquera characterScale, qui peut changer
        // après coup dans l'Inspector sans qu'on remesure.
        float s = Mathf.Max(0.0001f, mount.lossyScale.y);
        modelHeight = Mathf.Max(0.0001f, b.size.y / s);
        modelBottomLocal = (b.min.y - mount.position.y) / s;
    }

    // =============================== PERSONNAGE ===============================

    // Le modèle à afficher. En phase de test, « Test Character » écrase ce qu'on lui passe :
    // c'est ce qui fait apparaître le même personnage face à tous les adversaires.
    // (Plus tard : un modèle par ennemi, via un champ dans PalierConfig.EnemyInfo.)
    public void SetCharacterFor(GameObject perEnemyModel)
    {
        SetCharacter(testCharacter ? testCharacter : perEnemyModel);
    }

    public void SetCharacter(GameObject source)
    {
        if (source == currentSource) return;
        currentSource = source;

        if (current) DestroyAnyMode(current);
        current = null;
        animator = null;
        appliedHash = 0;

        if (source)
        {
            current = Instantiate(source, mount);
            current.hideFlags = HideFlags.DontSave;
            current.transform.localPosition = Vector3.zero;
            current.transform.localRotation = Quaternion.identity;

            MeasureModel();

            animator = current.GetComponentInChildren<Animator>();
            if (animator)
            {
                // Hors du champ de la caméra principale, un Animator peut être suspendu : il
                // faut lui dire explicitement de continuer à tourner.
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                // Le portrait est FIXE : le personnage ne doit jamais se déplacer lui-même. Si
                // les Root Transform du clip ne sont pas « Bake Into Pose », le root motion
                // pousse le transform à chaque frame — dérive lente, ou vibration sur place
                // quand la courbe de racine oscille.
                animator.applyRootMotion = false;
                if (!animator.runtimeAnimatorController && controller)
                    animator.runtimeAnimatorController = controller;
            }

            if (animator && !animator.runtimeAnimatorController && !warnedNoController)
            {
                warnedNoController = true;
                Debug.LogWarning("[Portrait3D] Le personnage n'a aucun Animator Controller : il " +
                                 "restera en pose de repos. Crée un Animator Controller avec un " +
                                 "état « Idle » et glisse-le dans le champ « Controller ».", this);
            }
        }

        bool visible = current != null;
        if (raw) raw.enabled = visible;
        if (cam) cam.enabled = visible;

        frozen = false;
        ambientBlockedUntil = 0f;
        state = PortraitState.Idle;
        lastActionTime = Time.unscaledTime;
        ApplyState(PortraitState.Idle, restart: true);
    }

    // =============================== ÉTATS ===============================

    // Contexte de jeu, poussé par le GameManager à chaque frame.
    public void SetContext(bool inPlay, bool playersTurn)
    {
        contextInPlay = inPlay;
        contextPlayersTurn = playersTurn;
    }

    // Le joueur vient d'agir : le compteur d'impatience repart de zéro.
    public void NotifyPlayerActed()
    {
        lastActionTime = Time.unscaledTime;
    }

    public void Play(PortraitState s, bool restart = false)
    {
        switch (s)
        {
            case PortraitState.Roll:
                // One-shot court : garde la main le temps de l'animation, puis rend au repos.
                frozen = false;
                ambientBlockedUntil = Time.unscaledTime + rollHold;
                restart = true; // deux lancers d'affilée doivent rejouer l'animation
                break;

            case PortraitState.Win:
            case PortraitState.Lose:
                // Fin de match : plus rien ne reprend la main tant qu'on ne le dit pas.
                frozen = true;
                ambientBlockedUntil = 0f;
                break;

            default:
                frozen = false;
                ambientBlockedUntil = 0f;
                lastActionTime = Time.unscaledTime;
                break;
        }
        ApplyState(s, restart);
    }

    // Arbitrage du repos : Idle, ou Wait si le joueur laisse traîner son tour.
    void RefreshAmbientState()
    {
        if (frozen || !current) return;
        if (Time.unscaledTime < ambientBlockedUntil) return;

        bool bored = contextInPlay
                     && contextPlayersTurn
                     && (Time.unscaledTime - lastActionTime) >= waitDelay;

        var want = bored ? PortraitState.Wait : PortraitState.Idle;
        if (want != state) ApplyState(want, restart: false);
    }

    void ApplyState(PortraitState s, bool restart)
    {
        state = s;
        if (!animator || !animator.runtimeAnimatorController) return;

        int hash = Animator.StringToHash(s.ToString());

        // État pas encore animé (tu n'as que Idle pour l'instant) : on retombe sur Idle plutôt
        // que de laisser Unity se plaindre à chaque frame.
        if (!animator.HasState(0, hash))
        {
            hash = Animator.StringToHash(PortraitState.Idle.ToString());
            if (!animator.HasState(0, hash)) return;
        }

        if (hash == appliedHash && !restart) return;

        if (restart) animator.CrossFadeInFixedTime(hash, crossFade, 0, 0f);
        else         animator.CrossFadeInFixedTime(hash, crossFade);

        appliedHash = hash;
    }
}
