using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// ============================== DÉS EN 3D ==============================
// Remplace le rendu plat des dés par un vrai cube : les six faces existent en même temps,
// et changer de face fait TOURNER le dé au lieu d'échanger une image. Les faces voisines
// que l'on aperçoit quand le dé s'incline sont donc les vraies voisines du dé réel.
//
// INSTALLATION
//   1. Un GameObject vide dans la scène de jeu (SampleScene), n'importe où.
//   2. Ajoute-lui ce composant. C'est tout : les cubes, les caméras et les textures sont
//      construits au lancement à partir des dés déjà présents.
//   3. Les cinq dispositions sont pré-remplies (relevés du Cosmic Wimpout, 10 face au joueur)
//      et attribuées dans l'ordre : les dés blancs prennent les dispositions blanches, le dé
//      noir (isSunDie) prend celle marquée « Black Die ».
//
// POURQUOI UNE RENDERTEXTURE
//   Le canvas du jeu est en ScreenSpaceOverlay : il est dessiné APRÈS toutes les caméras, donc
//   un cube posé « devant » l'UI passerait derrière toute l'interface. Chaque cube est donc
//   rendu dans une RenderTexture affichée par un RawImage, qui reste un élément d'UI normal.
//   Conséquence : mise en page, glisser-déposer, vols vers la drop-zone, cadre de FLASH et
//   contour de CLAUSE continuent de fonctionner sans qu'une ligne de leur code ne change.
//
// CE QUI EST DÉLIBÉRÉMENT LAISSÉ DE CÔTÉ
//   · une caméra et une RenderTexture par dé — un atlas partagé serait plus économe, mais
//     illisible tant que les réglages de cadrage ne sont pas figés ;
//   · le lancer qui culbute : la mécanique est là (une face = une rotation), il ne manque
//     qu'une interpolation entre l'ancienne et la nouvelle rotation ;
//   · SetDimmed (grisage du dé d'ajout) n'agit plus, la face n'étant plus rendue par l'Image.
// =======================================================================
public class Dice3DRenderer : MonoBehaviour
{
    [Header("Activation")]
    [Tooltip("Décoche pour revenir instantanément aux faces plates d'origine, sans rien " +
             "désinstaller. Utile pour comparer les deux rendus.")]
    public bool enable3D = true;

    [Header("Dispositions des dés")]
    [Tooltip("Une disposition par dé, relevée le 10 face au joueur. Les dés blancs consomment " +
             "les dispositions non marquées « Black Die » dans l'ordre ; le dé noir prend la " +
             "première marquée. S'il manque des dispositions, elles sont réutilisées en boucle.")]
    public DieCubeLayout[] layouts = DieCubeLayout.CosmicWimpout();

    [Header("Inclinaison au déplacement")]
    [Tooltip("Degrés d'inclinaison par unité de vitesse du dé à l'écran.")]
    public float tiltPerVelocity = 0.018f;
    [Tooltip("Inclinaison maximale (degrés) au glisser. Au-delà de ~25°, les faces voisines " +
             "mangent la face principale et la valeur devient dure à lire.")]
    [Range(0f, 45f)] public float maxTilt = 16f;
    [Tooltip("Inclinaison maximale (degrés) pendant les TRAVERSÉES D'ÉCRAN — sortie de fin de " +
             "tour, rentrée au ROLL. Nettement plus ample : sur un déplacement aussi long et " +
             "rapide, l'amplitude réglée pour la souris ne se voit pas, et la lisibilité de la " +
             "face importe peu puisque le dé est de passage.")]
    [Range(0f, 70f)] public float maxSweepTilt = 34f;
    [Tooltip("Raideur du RETOUR AU REPOS, une fois le dé immobile. Plus haut = redressement " +
             "sec, plus bas = flottant.")]
    public float tiltResponse = 12f;
    [Tooltip("Raideur de la MISE EN INCLINAISON quand le dé se met à bouger. Doit rester bien " +
             "plus élevée que le retour au repos : avec un ressort symétrique, l'inclinaison " +
             "met ~0,3 s à s'établir, soit plus longtemps qu'un dé ne met à quitter l'écran — " +
             "l'effet n'était visible qu'à l'arrivée, où il avait le temps de se former hors " +
             "champ. Monter/descendre ce réglage change à quel point le dé « accroche ».")]
    public float tiltAttackResponse = 45f;
    [Tooltip("Décoché : le dé TRAÎNE derrière le mouvement (inertie, on voit la face opposée " +
             "au déplacement). Coché : il se penche VERS le déplacement.")]
    public bool tiltTowardsMovement = false;

    [Header("Animation du jet")]
    [Tooltip("Durée (s) de la culbute d'un jet.")]
    public float rollSpinDuration = 0.55f;
    [Tooltip("Nombre de tours complets de la culbute (tiré au sort entre les deux bornes).")]
    public int minTurns = 2;
    public int maxTurns = 4;
    [Tooltip("Décalage (s) entre l'atterrissage de deux dés d'un même jet. C'est ce qui " +
             "transforme un changement d'état en lancer. Purement VISUEL : la logique et le " +
             "son restent groupés dans la même frame (voir DiceSfx).")]
    public float rollStagger = 0.05f;

    [Tooltip("Durée (s) du quart de tour joué quand un POUVOIR impose une face (artefact, " +
             "trait). Plus court et sans tours complets : l'effet se distingue d'un jet.")]
    public float forcedSpinDuration = 0.26f;
    [Tooltip("Angle (degrés) de ce quart de tour.")]
    public float forcedSpinAngle = 200f;

    [Header("Atterrissage")]
    [Tooltip("Durée (s) de l'atterrissage : c'est lui qui donne l'impression que le dé se " +
             "POSE au lieu d'apparaître.")]
    public float settleDuration = 0.22f;
    [Tooltip("Amplitude (degrés) du balancement. À 0 (défaut) il n'y en a pas : l'oscillation " +
             "se lisait comme un tremblement parasite juste après le jet. Monte-la si tu veux " +
             "retrouver un dé qui se balance avant de s'immobiliser.")]
    public float settleAngle = 0f;
    [Tooltip("Nombre d'oscillations du balancement (sans effet tant que Settle Angle vaut 0).")]
    public float settleOscillations = 2.5f;
    [Tooltip("Écrasement à l'impact (0 = aucun). Appliqué sur un pivot SANS rotation, donc " +
             "toujours vraiment vertical à l'écran, quelle que soit la face présentée.")]
    [Range(0f, 0.5f)] public float settleSquash = 0.07f;
    [Tooltip("Part de l'atterrissage consacrée à la MISE EN COMPRESSION (le reste sert au " +
             "relâchement). Trop bas, l'écrasement tombe d'un bloc et se ressent comme un " +
             "à-coup ; trop haut, l'impact devient mou.")]
    [Range(0.02f, 0.6f)] public float settleAttack = 0.18f;

    [Header("Rendu")]
    [Tooltip("Résolution de la RenderTexture de chaque dé.")]
    public int renderTextureSize = 512;
    [Tooltip("Anti-aliasing (1, 2, 4 ou 8). Sans lui les arêtes du cube crénellent, là où les " +
             "sprites plats étaient parfaitement nets.")]
    public int msaa = 4;
    [Tooltip("Angle de champ. Petit = presque orthographique, grand = arêtes très fuyantes.")]
    [Range(10f, 60f)] public float fieldOfView = 26f;
    [Tooltip("Hauteur cadrée par la caméra, en unités de cube (le cube fait 1). Au-dessus de 1, " +
             "il reste de la marge pour que l'inclinaison ne rogne pas les coins.")]
    public float frameHeight = 1.4f;
    [Tooltip("Taille du RawImage posé sur le dé. La caméra gardant de la marge, prends-le plus " +
             "grand que la face d'origine (128 px) pour retrouver la même taille apparente.")]
    public Vector2 displaySize = new Vector2(190f, 190f);
    [Tooltip("Fond de la RenderTexture. Alpha à 0 = transparent. Si un carré opaque apparaît " +
             "derrière les dés, c'est qu'URP n'a pas préservé l'alpha : mets ici la couleur de " +
             "la table et le problème disparaît.")]
    public Color backgroundColor = new Color(0f, 0f, 0f, 0f);

    [Header("Aspect du cube")]
    [Tooltip("Corps du dé, visible sur les arêtes et derrière les coins arrondis des vignettes.")]
    public Color bodyColor = new Color(0.98f, 0.98f, 0.98f, 1f);
    [Tooltip("Corps du dé noir.")]
    public Color blackBodyColor = new Color(0.13f, 0.13f, 0.13f, 1f);
    [Tooltip("Direction d'où vient la lumière, dans le repère de la scène du dé. La caméra " +
             "regarde vers +Z : un Z négatif éclaire donc la face tournée vers le joueur.")]
    public Vector3 lightDirection = new Vector3(-0.30f, 0.45f, -0.85f);
    [Tooltip("Luminosité de la face la plus sombre. À 1, plus aucun relief.")]
    [Range(0f, 1f)] public float shadeMin = 0.55f;

    [Header("Avancé")]
    [Tooltip("Laisser vide : trouvé par son nom (Dice3D/DieCubeUnlit). À assigner à la main " +
             "pour un build, où Shader.Find échoue sur un shader qu'aucun objet ne référence.")]
    public Shader cubeShader;
    [Tooltip("Où les cubes vivent dans le monde : très loin, hors du champ de la caméra du jeu. " +
             "Évite d'avoir à réserver un Layer dans les réglages du projet.")]
    public Vector3 stageOrigin = new Vector3(10000f, 10000f, 0f);

    // ---- Un « rig » par dé : sa scène 3D, son cube, sa caméra, sa texture, son RawImage. ----
    class Rig
    {
        public DieView die;
        public DieCubeLayout layout;
        public Image originalFace;      // réactivée au démontage
        public RawImage raw;
        public Transform stage;         // racine fixe : porte la caméra
        public Transform pivot;         // échelle seule (écrasement) — jamais de rotation
        public Transform cube;          // corps + vignettes : c'est lui qui tourne
        public Camera cam;
        public RenderTexture rt;
        public Material bodyMat;
        public readonly Material[] faceMats = new Material[DieCubeGeometry.SlotCount];
        public readonly Mesh[] faceMeshes = new Mesh[DieCubeGeometry.SlotCount];

        public DieFace lastFace = (DieFace)(-1);
        public Quaternion facing = Quaternion.identity;  // rotation présentant la face courante
        public Quaternion tilt = Quaternion.identity;    // inclinaison due au déplacement
        public Vector3 lastPos;

        // ---- Culbute en cours ----
        public int lastMotionTick = -1;
        public bool animating;
        public float animElapsed;
        public float animDelay;      // attente avant de partir (cascade entre dés)
        public float animSpin;       // durée de la rotation
        public float animExtra;      // angle à dérouler jusqu'à la face cible
        public Vector3 animAxis;
        public Quaternion fromFacing;  // face affichée pendant l'attente
        public Quaternion toFacing;    // face à présenter à l'arrivée
    }

    readonly List<Rig> rigs = new();
    Sprite sharedSun;   // image du SUN, pour un dé blanc sur lequel un artefact en pose un
    bool built;

    void Start()
    {
        if (!enable3D) return;
        Build();
    }

    void Build()
    {
        if (built) return;

        if (!cubeShader) cubeShader = Shader.Find("Dice3D/DieCubeUnlit");
        if (!cubeShader)
        {
            Debug.LogError("Dice3DRenderer : shader « Dice3D/DieCubeUnlit » introuvable.", this);
            enabled = false;
            return;
        }
        if (layouts == null || layouts.Length == 0) layouts = DieCubeLayout.CosmicWimpout();

        var dice = CollectDice();
        foreach (var d in dice)
            if (!sharedSun && d.blackSprites != null && d.blackSprites.sun) sharedSun = d.blackSprites.sun;

        int whiteCount = 0;
        for (int i = 0; i < dice.Count; i++)
        {
            var rig = BuildRig(dice[i], i, LayoutFor(dice[i], ref whiteCount));
            if (rig != null) rigs.Add(rig);
        }

        built = rigs.Count > 0;
        if (!built)
            Debug.LogWarning("Dice3DRenderer : aucun dé trouvé. Le composant doit vivre dans la scène de jeu.", this);
    }

    List<DieView> CollectDice()
    {
        var list = new List<DieView>();
        var gm = FindAnyObjectByType<GameManager>();
        if (gm != null && gm.dice != null)
            foreach (var d in gm.dice) if (d && !list.Contains(d)) list.Add(d);
        if (list.Count == 0) list.AddRange(FindObjectsByType<DieView>());
        return list;
    }

    // Le dé noir prend la disposition marquée « Black Die », les autres consomment les
    // dispositions blanches dans l'ordre. Le compteur est passé par référence pour que le dé
    // noir, où qu'il soit dans la liste, ne décale pas l'attribution des dés blancs.
    DieCubeLayout LayoutFor(DieView die, ref int whiteIndex)
    {
        bool black = die.isSunDie;
        var pool = new List<DieCubeLayout>();
        foreach (var l in layouts) if (l != null && l.blackDie == black) pool.Add(l);
        if (pool.Count == 0) pool.AddRange(layouts);

        return black ? pool[0] : pool[whiteIndex++ % pool.Count];
    }

    // ---- Construction d'un rig ----------------------------------------------------------
    Rig BuildRig(DieView die, int index, DieCubeLayout layout)
    {
        if (!die || !die.faceImage) return null;

        var rig = new Rig { die = die, layout = layout, originalFace = die.faceImage };

        // 1) La scène 3D, très loin du jeu pour rester hors du champ de la caméra principale.
        //    La caméra est accrochée à CETTE racine et le cube est un enfant à part : s'ils
        //    partageaient le même transform, la caméra tournerait avec le cube et ni la
        //    rotation de face ni l'inclinaison ne se verraient.
        var stage = new GameObject($"DieCube[{index}] {die.name}").transform;
        stage.position = stageOrigin + new Vector3(index * 20f, 0f, 0f);
        rig.stage = stage;

        // Un pivot SANS rotation s'intercale entre la scène et le cube : l'écrasement
        // d'atterrissage s'y applique, donc il reste vertical à l'écran quelle que soit la
        // face présentée. Écrasé sur le cube lui-même, il suivrait la rotation de la face
        // et comprimerait le dé selon un axe arbitraire.
        var pivot = new GameObject("Pivot").transform;
        pivot.SetParent(stage, false);
        rig.pivot = pivot;

        var cube = new GameObject("Cube").transform;
        cube.SetParent(pivot, false);
        rig.cube = cube;

        // 2) Le corps : un cube plein, opaque, qui écrit la profondeur. C'est lui qui masque
        //    les vignettes des faces arrière — sans quoi on verrait le dé par transparence.
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(cube, false);
        body.transform.localScale = Vector3.one * 0.995f;

        rig.bodyMat = NewMaterial($"DieBody[{index}]", zWrite: 1f, queue: 2000);
        rig.bodyMat.color = layout.blackDie ? blackBodyColor : bodyColor;
        body.GetComponent<MeshRenderer>().sharedMaterial = rig.bodyMat;

        // 3) Les six vignettes. Elles sont posées UNE FOIS pour toutes : changer de face
        //    fera tourner le cube, plus jamais changer une texture.
        for (int s = 0; s < DieCubeGeometry.SlotCount; s++)
        {
            var quad = new GameObject($"Face_{DieCubeGeometry.SlotNames[s]}", typeof(MeshFilter), typeof(MeshRenderer));
            quad.transform.SetParent(cube, false);
            quad.transform.localPosition = DieCubeGeometry.Dir[s] * 0.502f;
            quad.transform.localRotation = DieCubeGeometry.QuadRotation(s);

            var sprite = SpriteFor(die, layout.ValueAt(s));
            rig.faceMeshes[s] = BuildQuadMesh(sprite);
            quad.GetComponent<MeshFilter>().sharedMesh = rig.faceMeshes[s];

            rig.faceMats[s] = NewMaterial($"DieFace[{index}]_{DieCubeGeometry.SlotNames[s]}", zWrite: 0f, queue: 3000);
            rig.faceMats[s].mainTexture = sprite ? sprite.texture : null;
            quad.GetComponent<MeshRenderer>().sharedMaterial = rig.faceMats[s];
        }

        // 4) La caméra et sa RenderTexture. Posée en -Z sans rotation : la droite de l'écran
        //    reste +X et le haut +Y, donc les « gauche/droite » des relevés se lisent tels quels.
        rig.rt = new RenderTexture(Mathf.Max(64, renderTextureSize), Mathf.Max(64, renderTextureSize), 24, RenderTextureFormat.ARGB32)
        {
            name = $"DieRT[{index}]",
            antiAliasing = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(1, msaa)), 1, 8),
            filterMode = FilterMode.Bilinear,
            useMipMap = false,
            autoGenerateMips = false,
        };
        rig.rt.Create();

        var camGo = new GameObject("Camera");
        camGo.transform.SetParent(stage, false);
        rig.cam = camGo.AddComponent<Camera>();
        rig.cam.clearFlags = CameraClearFlags.SolidColor;
        rig.cam.backgroundColor = backgroundColor;
        rig.cam.orthographic = false;
        rig.cam.nearClipPlane = 0.05f;
        rig.cam.farClipPlane = 40f;
        rig.cam.targetTexture = rig.rt;
        rig.cam.allowHDR = false;
        rig.cam.allowMSAA = true;

        // 5) Côté UI : on désactive le COMPOSANT Image sans toucher à son sprite — le
        //    GameManager teste `faceImage.sprite != null` pour savoir si un jet a eu lieu.
        rig.originalFace.enabled = false;

        var rawGo = new GameObject("Face3D", typeof(RectTransform));
        var rrt = (RectTransform)rawGo.transform;
        rrt.SetParent(die.transform, false);
        rrt.SetSiblingIndex(rig.originalFace.rectTransform.GetSiblingIndex());
        rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.5f);
        rrt.pivot = new Vector2(0.5f, 0.5f);
        rrt.anchoredPosition = Vector2.zero;

        rig.raw = rawGo.AddComponent<RawImage>();
        rig.raw.texture = rig.rt;
        rig.raw.raycastTarget = false; // clic et glisser doivent atteindre la racine du dé

        rig.lastPos = die.transform.position;
        rig.raw.enabled = rig.originalFace.sprite != null; // vierge tant qu'aucun jet n'a eu lieu
        ApplyCamera(rig);
        ApplyShading(rig);

        die.suppressScalePulse = true; // l'impact est joué par le cube, en cascade

        rig.lastMotionTick = die.MotionTick;
        rig.lastFace = die.GetFace();
        rig.facing = ResolveFacing(rig, rig.lastFace);
        return rig;
    }

    Material NewMaterial(string name, float zWrite, int queue)
    {
        var m = new Material(cubeShader) { name = name };
        m.SetFloat("_ZWrite", zWrite);
        m.renderQueue = queue;
        return m;
    }

    // Quad 1×1 dans le plan XY, face vers +Z. Les UV sont écrites DANS le mesh plutôt que via
    // mainTextureOffset/Scale : c'est la seule méthode qui reste juste quand le sprite provient
    // d'un atlas, quel que soit le shader.
    static Mesh BuildQuadMesh(Sprite sprite)
    {
        var m = new Mesh { name = "DieFaceQuad" };
        m.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f), new Vector3(0.5f,  0.5f, 0f),
        };
        m.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        m.uv = SpriteUV(sprite);
        return m;
    }

    // Sommets dans l'ordre (-X,-Y), (+X,-Y), (-X,+Y), (+X,+Y).
    //
    // L'axe U est INVERSÉ volontairement. La vignette est regardée depuis le côté vers lequel
    // pointe sa normale : la direction du regard est donc -Z local, ce qui place le +X local à
    // la GAUCHE de l'écran. C'est l'inverse du Quad standard d'Unity, dont la normale pointe
    // vers la caméra par défaut et pour lequel les UV sont pensées. Sans cette inversion, tous
    // les symboles s'affichent en miroir. L'axe V, lui, n'est pas concerné : le haut reste
    // le haut quel que soit le côté d'où l'on regarde.
    static Vector2[] SpriteUV(Sprite sprite)
    {
        if (!sprite || !sprite.texture)
            return new[] { Vector2.right, Vector2.zero, Vector2.one, Vector2.up };

        var r = sprite.textureRect;
        float tw = sprite.texture.width, th = sprite.texture.height;
        float x0 = r.xMin / tw, x1 = r.xMax / tw;
        float y0 = r.yMin / th, y1 = r.yMax / th;
        return new[]
        {
            new Vector2(x1, y0), new Vector2(x0, y0),
            new Vector2(x1, y1), new Vector2(x0, y1),
        };
    }

    void Update()
    {
        // Bascule à chaud entre les deux rendus, sans redémarrer la partie.
        if (enable3D && !built) { Build(); return; }
        if (!enable3D && built) { Teardown(); return; }
        if (!built) return;

        for (int i = rigs.Count - 1; i >= 0; i--)
            if (!rigs[i].die || !rigs[i].stage || !rigs[i].cube) rigs.RemoveAt(i);

        // Rang de chaque dé dans la cascade du jet. Tous les dés d'un même jet changent de
        // face dans la MÊME frame (c'est aussi ce qui permet à DiceSfx de jouer un seul son
        // pour cinq dés) : les compter ici suffit à les faire atterrir l'un après l'autre.
        // Parcours vers l'AVANT pour que la cascade aille de gauche à droite.
        int staggerSlot = 0;

        for (int i = 0; i < rigs.Count; i++)
        {
            var rig = rigs[i];

            ApplyCamera(rig);
            ApplyShading(rig);
            UpdateMotion(rig, ref staggerSlot);
            UpdateTilt(rig);

            // L'inclinaison s'applique dans le repère de l'écran, PAR-DESSUS la rotation qui
            // présente la face : l'ordre inverse ferait tourner l'inclinaison avec le dé.
            rig.cube.localRotation = rig.tilt * rig.facing;

            rig.raw.rectTransform.sizeDelta = displaySize;
            rig.cam.backgroundColor = backgroundColor;

            // DieView.SetFaceSprite réactive le composant Image à chaque jet : sans ce rappel,
            // la face plate réapparaîtrait derrière le cube dès le premier ROLL.
            rig.originalFace.enabled = false;

            // Avant le premier jet, le dé doit rester VIERGE. Le signal existe déjà et sert
            // aussi au GameManager : pas de sprite sur la face = aucun jet n'a eu lieu.
            bool rolled = rig.originalFace.sprite != null
                       || rig.die.InStageTransition; // entrée/sortie de plateau
            if (rig.raw.enabled != rolled) rig.raw.enabled = rolled;

            // Dé remis à zéro en cours de culbute (nouveau tour) : on coupe net, il n'y a
            // plus rien à montrer et l'animation reprendrait au prochain jet.
            if (!rolled && rig.animating) EndMotion(rig);
        }
    }

    void ApplyCamera(Rig rig)
    {
        rig.cam.fieldOfView = fieldOfView;
        float dist = (Mathf.Max(0.2f, frameHeight) * 0.5f) / Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
        rig.cam.transform.localPosition = new Vector3(0f, 0f, -dist);
        rig.cam.transform.localRotation = Quaternion.identity; // regarde vers +Z
    }

    void ApplyShading(Rig rig)
    {
        var dir = lightDirection.sqrMagnitude < 0.0001f ? Vector3.back : lightDirection.normalized;
        SetShading(rig.bodyMat, dir);
        foreach (var m in rig.faceMats) SetShading(m, dir);
        rig.bodyMat.color = rig.layout.blackDie ? blackBodyColor : bodyColor;
    }

    void SetShading(Material m, Vector3 dir)
    {
        if (!m) return;
        m.SetVector("_LightDir", dir);
        m.SetFloat("_ShadeMin", shadeMin);
    }

    // ---- Présentation de la face courante -------------------------------------------------
    // ---- Mouvement : culbute d'un jet, quart de tour d'un pouvoir, atterrissage -----------
    void UpdateMotion(Rig rig, ref int staggerSlot)
    {
        // Un nouveau mouvement est signalé par le COMPTEUR de DieView, pas par un changement
        // de valeur : un dé qui refait un 5 après un 5 doit culbuter comme les autres.
        if (rig.die.MotionTick != rig.lastMotionTick)
        {
            rig.lastMotionTick = rig.die.MotionTick;
            bool isRoll = rig.die.LastMotion != DieMotion.Forced;
            StartMotion(rig, isRoll, isRoll ? staggerSlot++ : 0);
        }
        else if (!rig.animating && rig.die.GetFace() != rig.lastFace)
        {
            // Face changée sans signal (outils de dev, écriture directe par réflexion) :
            // on se recale sans rien animer plutôt que de rester sur l'ancienne face.
            rig.lastFace = rig.die.GetFace();
            rig.facing = ResolveFacing(rig, rig.lastFace);
        }

        if (!rig.animating) return;

        rig.animElapsed += Time.unscaledDeltaTime;
        float t = rig.animElapsed - rig.animDelay;

        // Attente de son rang dans la cascade : le dé montre encore son ancienne face.
        if (t <= 0f) { rig.facing = rig.fromFacing; return; }

        if (t < rig.animSpin)
        {
            // Le dé part avec plusieurs tours d'avance et les DÉROULE. À la fin, l'angle
            // supplémentaire vaut exactement zéro : la face atterrit toujours d'équerre,
            // sans dérive possible. Un Slerp de l'ancienne vers la nouvelle rotation aurait
            // pris le plus court chemin — un demi-quart de tour mou, jamais une culbute.
            float k = t / rig.animSpin;
            rig.facing = Quaternion.AngleAxis(rig.animExtra * (1f - EaseOutQuart(k)), rig.animAxis) * rig.toFacing;
            return;
        }

        float s = (t - rig.animSpin) / Mathf.Max(0.01f, settleDuration);
        if (s >= 1f) { EndMotion(rig); return; }

        // Atterrissage. L'écrasement est MONOTONE : compression maximale au contact, puis
        // relâchement. Une version oscillante donnait un frémissement juste après le jet.
        float damp = (1f - s) * (1f - s);
        rig.facing = settleAngle > 0f
            ? Quaternion.AngleAxis(settleAngle * Mathf.Sin(s * Mathf.PI * settleOscillations) * damp, rig.animAxis) * rig.toFacing
            : rig.toFacing;

        // L'écrasement monte vite mais CONTINÛMENT, puis se relâche. La version précédente
        // l'appliquait d'un bloc dès le contact : cette discontinuité se lisait comme un
        // tremblement sec juste après le jet, alors qu'aucune oscillation n'était en jeu.
        float attack = Mathf.Clamp(settleAttack, 0.02f, 0.9f);
        float squash = s < attack
            ? settleSquash * (s / attack)
            : settleSquash * Mathf.Pow(1f - (s - attack) / (1f - attack), 2f);
        rig.pivot.localScale = new Vector3(1f + squash, 1f - squash, 1f + squash);
    }

    void StartMotion(Rig rig, bool isRoll, int staggerSlot)
    {
        rig.lastFace = rig.die.GetFace();
        rig.fromFacing = rig.facing;
        rig.toFacing = ResolveFacing(rig, rig.lastFace);

        rig.animating = true;
        rig.animElapsed = 0f;
        rig.animDelay = isRoll ? staggerSlot * Mathf.Max(0f, rollStagger) : 0f;
        // Un jet qui tombe pendant l'écrasement du précédent repartirait d'un dé aplati.
        if (rig.pivot) rig.pivot.localScale = Vector3.one;

        // Publie l'instant d'arrivée : cadre de FLASH, contour de CLAUSE et déplacements
        // automatiques vers la zone s'y accrochent pour ne pas devancer l'animation.
        rig.die.rollAnimationEndTime = Time.unscaledTime + rig.animDelay + rig.animSpin + Mathf.Max(0f, settleDuration);

        if (isRoll)
        {
            rig.animSpin = Mathf.Max(0.05f, rollSpinDuration) * Random.Range(0.9f, 1.1f);
            int turns = Random.Range(Mathf.Min(minTurns, maxTurns), Mathf.Max(minTurns, maxTurns) + 1);
            rig.animExtra = 360f * Mathf.Max(1, turns) + Random.Range(-40f, 40f);
            rig.animAxis = RandomTumbleAxis();
        }
        else
        {
            // Face imposée par un pouvoir : un pivot bref et net, sans tour complet ni
            // hasard. On le lit immédiatement comme « quelque chose a retourné ce dé ».
            rig.animSpin = Mathf.Max(0.05f, forcedSpinDuration);
            rig.animExtra = forcedSpinAngle;
            rig.animAxis = Vector3.up;
        }
    }

    void EndMotion(Rig rig)
    {
        rig.animating = false;
        rig.facing = rig.toFacing;
        if (rig.pivot) rig.pivot.localScale = Vector3.one;
    }

    // Axe de culbute tiré au sort, mais jamais purement vertical : une rotation autour du seul
    // axe Y donne un plateau tournant, pas un dé qui bascule. On garantit donc du basculement.
    static Vector3 RandomTumbleAxis()
    {
        var a = Random.onUnitSphere;
        float sign = a.x < 0f ? -1f : 1f;
        a.x = sign * Mathf.Max(0.45f, Mathf.Abs(a.x));
        return a.normalized;
    }

    static float EaseOutQuart(float x)
    {
        float p = 1f - Mathf.Clamp01(x);
        return 1f - p * p * p * p;
    }

    // Rotation présentant une valeur donnée, et repeinte de la face avant quand le dé ne
    // porte pas cette valeur.
    Quaternion ResolveFacing(Rig rig, DieFace face)
    {
        int slot = rig.layout.SlotOf(face);
        if (slot >= 0)
        {
            // Le dé porte peut-être encore une vignette de secours d'un SUN posé plus tôt.
            RestoreSlotSprite(rig, DieCubeGeometry.Front);
            return DieCubeGeometry.FacingRotation(slot);
        }

        // Valeur absente du dé : cas réel, un artefact (Miroir, Soleil en bouteille) pose un
        // SUN sur un dé blanc, qui n'en porte pas. On ne triche que sur la face avant, en y
        // peignant l'image demandée — les voisines restent celles du dé.
        var sprite = SpriteFor(rig.die, face);
        if (sprite)
        {
            rig.faceMats[DieCubeGeometry.Front].mainTexture = sprite.texture;
            rig.faceMeshes[DieCubeGeometry.Front].uv = SpriteUV(sprite);
        }
        return DieCubeGeometry.FacingRotation(DieCubeGeometry.Front);
    }

    void RestoreSlotSprite(Rig rig, int slot)
    {
        var sprite = SpriteFor(rig.die, rig.layout.ValueAt(slot));
        rig.faceMats[slot].mainTexture = sprite ? sprite.texture : null;
        rig.faceMeshes[slot].uv = SpriteUV(sprite);
    }

    // ---- Inclinaison, dérivée du déplacement RÉEL du dé à l'écran -------------------------
    // On mesure la position du RectTransform plutôt que le delta du pointeur : ça couvre aussi
    // les vols vers la drop-zone, et ça ne demande aucune modification de DieView.
    void UpdateTilt(Rig rig)
    {
        float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
        Vector3 pos = rig.die.transform.position;
        Vector3 v = (pos - rig.lastPos) / dt;
        rig.lastPos = pos;

        // Le dé ne s'incline que lorsqu'il est RÉELLEMENT déplacé : porté, ou en vol vers la
        // zone. Sans ce filtre, le moindre réagencement de la rangée — un dé qui la quitte et
        // les autres qui se recentrent d'un coup — se lisait comme un lancer violent et
        // secouait tous les dés restants.
        bool carried = rig.die.IsDragging || rig.die.IsFlying;

        // Une traversée d'écran s'incline plus franchement qu'un simple glisser : même code,
        // même vol, seule l'amplitude change.
        float limit = rig.die.InStageTransition ? maxSweepTilt : maxTilt;

        float sign = tiltTowardsMovement ? -1f : 1f;
        float yaw   = carried ? Mathf.Clamp(sign * -v.x * tiltPerVelocity, -limit, limit) : 0f;
        float pitch = carried ? Mathf.Clamp(sign *  v.y * tiltPerVelocity, -limit, limit) : 0f;

        var target = Quaternion.Euler(pitch, yaw, 0f);

        // Ressort ASYMÉTRIQUE : le dé s'incline presque instantanément dès qu'il bouge, mais
        // se redresse doucement. Un ressort symétrique donnait une montée trop lente pour être
        // vue sur un déplacement bref, et surtout pour être vue AVANT que le dé n'ait quitté
        // l'écran — d'où un effet qui semblait n'exister qu'à l'entrée des dés.
        float toRest = Quaternion.Angle(Quaternion.identity, target)
                     < Quaternion.Angle(Quaternion.identity, rig.tilt) ? tiltResponse : tiltAttackResponse;
        rig.tilt = Quaternion.Slerp(rig.tilt, target, 1f - Mathf.Exp(-toRest * dt));
    }

    // ---- Images des faces (reprend la règle de DieView) -----------------------------------
    Sprite SpriteFor(DieView d, DieFace f)
    {
        Sprite s = null;
        if (IsBlackDie(d)) s = FromBlack(d.blackSprites, f);
        if (!s) s = FromNormal(d.sprites, f);
        if (!s && f == DieFace.Sun) s = sharedSun;
        return s;
    }

    static bool IsBlackDie(DieView d) =>
        d.isSunDie && d.blackSprites != null &&
        (d.blackSprites.two || d.blackSprites.four || d.blackSprites.five ||
         d.blackSprites.six || d.blackSprites.ten  || d.blackSprites.sun);

    static Sprite FromNormal(DiceSprites set, DieFace f)
    {
        if (set == null) return null;
        return f switch
        {
            DieFace.Two => set.two,   DieFace.Three => set.three, DieFace.Four => set.four,
            DieFace.Five => set.five, DieFace.Six => set.six,     DieFace.Ten => set.ten,
            _ => null
        };
    }

    // Le dé noir n'a pas de face 3 : le SUN l'a remplacée.
    static Sprite FromBlack(BlackDiceSprites set, DieFace f)
    {
        if (set == null) return null;
        return f switch
        {
            DieFace.Two => set.two, DieFace.Four => set.four, DieFace.Five => set.five,
            DieFace.Six => set.six, DieFace.Ten  => set.ten,  DieFace.Sun  => set.sun,
            _ => null
        };
    }

    // ---- Démontage : les dés retrouvent exactement leur rendu plat -------------------------
    void OnDisable() => Teardown();
    void OnDestroy() => Teardown();

    void Teardown()
    {
        foreach (var rig in rigs)
        {
            if (rig.die) { rig.die.suppressScalePulse = false; rig.die.rollAnimationEndTime = 0f; }
            if (rig.originalFace) rig.originalFace.enabled = true;
            if (rig.raw) SafeDestroy.Now(rig.raw);
            if (rig.cam) rig.cam.targetTexture = null;
            if (rig.stage) SafeDestroy.Now(rig.stage); // emporte cube, vignettes et caméra
            if (rig.rt) { rig.rt.Release(); Destroy(rig.rt); }
            if (rig.bodyMat) Destroy(rig.bodyMat);
            foreach (var m in rig.faceMats) if (m) Destroy(m);
            foreach (var m in rig.faceMeshes) if (m) Destroy(m);
        }
        rigs.Clear();
        built = false;
    }
}
