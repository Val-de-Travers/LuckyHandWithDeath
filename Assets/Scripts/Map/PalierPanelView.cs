using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// Panneau récapitulatif de progression de la campagne (le « Palier Panel »).
//
// Piloté par GameManager via Render(...) : GameManager construit un instantané (Snapshot) et le
// panneau se contente d'AFFICHER — il ne connaît aucune règle du jeu.
//
// Câblage (Inspector) :
//  • paliers[i]  : un PalierRow par palier (label, points, liste d'icônes d'ennemis).
//  • segments[i] : un Segment ENTRE le palier i et i+1 :
//        - progressLine   : ligne rouge (Image Filled, Horizontal, Origin Left) sur la ligne blanche
//        - traitIconImages: 1..N indicateurs On/Off (autant que de traits possibles sur ce segment)
//        - traitImages    : 1..N images de traits obtenus (côte à côte via Layout Group)
//        - traitUnlockedTexts : 1..N textes « trait débloqué » (côte à côte)
public class PalierPanelView : MonoBehaviour
{
    [Serializable]
    public class PalierRow
    {
        public TMP_Text label;                 // « Palier 1 »
        public TMP_Text pointsText;            // « 300 pts »
        public List<Image> enemyIcons = new(); // P#_EnemyList
    }

    [Serializable]
    public class Segment // transition entre palier i et i+1
    {
        [Tooltip("Ligne de progression rouge (Image Filled, Horizontal, Origin Left).")]
        public Image progressLine;
        [Tooltip("Indicateurs d'état du trait (un par trait possible ici) : Off avant, On au déblocage.")]
        public List<Image> traitIconImages = new();
        [Tooltip("Images des traits obtenus (une par trait, côte à côte).")]
        public List<Image> traitImages = new();
        [Tooltip("Textes « trait débloqué » (un par trait, côte à côte).")]
        public List<TMP_Text> traitUnlockedTexts = new();
    }

    [Header("Paliers (un par palier)")]
    public List<PalierRow> paliers = new();
    [Header("Segments (entre chaque palier : count = paliers - 1)")]
    public List<Segment> segments = new();

    [Header("Sprites — indicateur de trait")]
    [Tooltip("État NON débloqué (IndTraitUnlOff) — c'est l'image de base.")]
    public Sprite traitIconOff;
    [Tooltip("État débloqué (IndTraitUnlOn).")]
    public Sprite traitIconOn;

    [Header("Sprites — état d'un ennemi")]
    public Sprite enemyInGame;
    public Sprite enemyWin;
    public Sprite enemyLoose;

    [Header("Contour de l'ennemi courant")]
    public Color currentEnemyOutlineColor = new Color(0.89f, 0.30f, 0.30f);
    public float currentEnemyOutlineWidth = 4f;

    [Header("Ligne de progression")]
    [Tooltip("Durée (s) pour remplir une ligne (~ le temps pour atteindre chaque point).")]
    public float lineFillDuration = 1f;

    [Header("Texte « trait débloqué »")]
    public float traitUnlockFadeDuration = 0.6f;

    [Header("Divers")]
    public Button closeButton;
    public ArtifactTooltip tooltip;

    public class Snapshot
    {
        public int palierCount;
        public int currentPalier;
        public int[] winScores;
        public int[] enemyCounts;
        public int[][] enemyResults;       // [palier][ennemi] : 0/1/-1
        public bool matchInProgress;
        public float[] segmentFill;        // remplissage cible par segment (0..1)
        public Sprite[][] segmentTraitSprites; // [segment][trait]
        public string[][] segmentTraitNames;
        public string[][] segmentTraitDescs;
    }

    Snapshot _last;
    Coroutine _animCo;
    readonly List<float> _shownFill = new();
    readonly List<int> _shownTraitCount = new();   // nb de traits déjà affichés par segment
    readonly Dictionary<int, (string name, string desc)[]> _segTooltips = new();

    void Awake()
    {
        for (int i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            if (seg == null) continue;

            if (seg.progressLine)
            {
                seg.progressLine.type = Image.Type.Filled;
                seg.progressLine.fillMethod = Image.FillMethod.Horizontal;
                seg.progressLine.fillOrigin = (int)Image.OriginHorizontal.Left;
                // Le sprite (PalierLineRed) est DÉJÀ rouge : on garde une teinte BLANCHE pour
                // l'afficher nativement (#E34C4C). Teinter avec GameColors.Points le multipliait
                // par lui-même → un rouge trop sombre.
                seg.progressLine.color = Color.white;
                seg.progressLine.fillAmount = 0f;
            }
            // État de base : TOUS les indicateurs sur Off, images/textes masqués.
            if (seg.traitIconImages != null)
                foreach (var ic in seg.traitIconImages)
                    if (ic && traitIconOff) ic.sprite = traitIconOff;
            if (seg.traitImages != null)
                for (int t = 0; t < seg.traitImages.Count; t++)
                {
                    if (seg.traitImages[t]) seg.traitImages[t].gameObject.SetActive(false);
                    EnsureTraitHover(i, t);
                }
            if (seg.traitUnlockedTexts != null)
                foreach (var txt in seg.traitUnlockedTexts) SetAlpha(txt, 0f);
        }
        while (_shownFill.Count < segments.Count) _shownFill.Add(0f);
        while (_shownTraitCount.Count < segments.Count) _shownTraitCount.Add(0);

        if (closeButton) closeButton.onClick.AddListener(Close);
    }

    public void Close() => gameObject.SetActive(false);

    // animate         : anime le remplissage des lignes qui progressent.
    // revealNewTraits : fait apparaître en fondu les traits nouvellement obtenus (sinon instantané).
    public void Render(Snapshot snap, bool animate, bool revealNewTraits = false)
    {
        if (snap == null) return;
        _last = snap;
        gameObject.SetActive(true);

        // ---------- Paliers : points + ennemis ----------
        for (int p = 0; p < paliers.Count; p++)
        {
            var row = paliers[p];
            if (row == null) continue;

            if (row.pointsText && snap.winScores != null && p < snap.winScores.Length)
                row.pointsText.text = $"{snap.winScores[p]} pts";

            int ecount = (snap.enemyCounts != null && p < snap.enemyCounts.Length) ? snap.enemyCounts[p] : 0;
            for (int e = 0; e < row.enemyIcons.Count; e++)
            {
                var img = row.enemyIcons[e];
                if (!img) continue;

                if (e >= ecount) { img.gameObject.SetActive(false); continue; }
                img.gameObject.SetActive(true);

                int result = 0;
                if (snap.enemyResults != null && p < snap.enemyResults.Length
                    && snap.enemyResults[p] != null && e < snap.enemyResults[p].Length)
                    result = snap.enemyResults[p][e];

                Sprite s = result > 0 ? enemyWin : result < 0 ? enemyLoose : enemyInGame;
                if (s) img.sprite = s;
                img.color = Color.white;

                bool isCurrent = snap.matchInProgress && p == snap.currentPalier && result == 0
                                 && IsFirstUnplayed(snap, p, e);
                SetOutline(img, isCurrent, currentEnemyOutlineColor, currentEnemyOutlineWidth);
            }
        }

        // Tooltips par segment.
        _segTooltips.Clear();
        for (int i = 0; i < segments.Count; i++)
        {
            var names = (snap.segmentTraitNames != null && i < snap.segmentTraitNames.Length) ? snap.segmentTraitNames[i] : null;
            var descs = (snap.segmentTraitDescs != null && i < snap.segmentTraitDescs.Length) ? snap.segmentTraitDescs[i] : null;
            int n = names?.Length ?? 0;
            var arr = new (string, string)[n];
            for (int t = 0; t < n; t++)
                arr[t] = (names != null && t < names.Length ? names[t] : "",
                          descs != null && t < descs.Length ? descs[t] : "");
            _segTooltips[i] = arr;
        }

        while (_shownFill.Count < segments.Count) _shownFill.Add(0f);
        while (_shownTraitCount.Count < segments.Count) _shownTraitCount.Add(0);

        if (_animCo != null) { StopCoroutine(_animCo); _animCo = null; }
        if (animate && isActiveAndEnabled) _animCo = StartCoroutine(AnimateSegments(snap, revealNewTraits));
        else ApplySegmentsInstant(snap, revealNewTraits);
    }

    static bool IsFirstUnplayed(Snapshot snap, int p, int e)
    {
        if (snap.enemyResults == null || p >= snap.enemyResults.Length || snap.enemyResults[p] == null) return e == 0;
        for (int k = 0; k < e; k++)
            if (snap.enemyResults[p][k] == 0) return false;
        return true;
    }

    void ApplySegmentsInstant(Snapshot snap, bool revealNewTraits)
    {
        for (int i = 0; i < segments.Count; i++)
        {
            float target = (snap.segmentFill != null && i < snap.segmentFill.Length) ? snap.segmentFill[i] : 0f;
            _shownFill[i] = target;
            if (segments[i]?.progressLine) segments[i].progressLine.fillAmount = target;
            SetSegmentTrait(i, snap, target >= 0.999f, revealNewTraits);
        }
    }

    IEnumerator AnimateSegments(Snapshot snap, bool revealNewTraits)
    {
        for (int i = 0; i < segments.Count; i++)
        {
            float from = _shownFill[i];
            float to = (snap.segmentFill != null && i < snap.segmentFill.Length) ? snap.segmentFill[i] : 0f;

            if (to > from + 0.001f && segments[i]?.progressLine)
            {
                float t = 0f, dur = Mathf.Max(0.05f, lineFillDuration);
                while (t < dur)
                {
                    t += Time.deltaTime;
                    float v = Mathf.Lerp(from, to, t / dur);
                    segments[i].progressLine.fillAmount = v;
                    _shownFill[i] = v;
                    yield return null;
                }
            }
            if (segments[i]?.progressLine) segments[i].progressLine.fillAmount = to;
            _shownFill[i] = to;

            SetSegmentTrait(i, snap, to >= 0.999f, revealNewTraits);
        }
        _animCo = null;
    }

    void SetSegmentTrait(int i, Snapshot snap, bool lineFull, bool revealNewTraits)
    {
        var seg = segments[i];
        if (seg == null) return;

        Sprite[] sprites = (snap.segmentTraitSprites != null && i < snap.segmentTraitSprites.Length)
            ? snap.segmentTraitSprites[i] : null;
        int traitCount = lineFull ? (sprites?.Length ?? 0) : 0;

        int prevShown = (i < _shownTraitCount.Count) ? _shownTraitCount[i] : 0;

        // Indicateurs On/Off : slot t On si le trait t est débloqué (base = Off).
        if (seg.traitIconImages != null)
            for (int t = 0; t < seg.traitIconImages.Count; t++)
            {
                var ic = seg.traitIconImages[t];
                if (!ic) continue;
                var spr = (t < traitCount) ? traitIconOn : traitIconOff;
                if (spr) ic.sprite = spr;
            }

        // Images de traits (une par trait), côte à côte.
        if (seg.traitImages != null)
            for (int t = 0; t < seg.traitImages.Count; t++)
            {
                var img = seg.traitImages[t];
                if (!img) continue;
                bool show = t < traitCount && sprites[t] != null;
                img.gameObject.SetActive(show);
                if (show)
                {
                    img.sprite = sprites[t];
                    img.preserveAspect = true;
                    img.raycastTarget = true;

                    bool isNew = t >= prevShown;
                    if (isNew && revealNewTraits) StartCoroutine(RevealImage(img)); // apparition animée
                    else { SetImageAlpha(img, 1f); img.rectTransform.localScale = Vector3.one; }
                }
            }

        // Textes « trait débloqué » : fade-in pour les traits NOUVELLEMENT obtenus.
        if (seg.traitUnlockedTexts != null)
            for (int t = 0; t < seg.traitUnlockedTexts.Count; t++)
            {
                var txt = seg.traitUnlockedTexts[t];
                if (!txt) continue;
                bool show = t < traitCount;
                if (!show) { SetAlpha(txt, 0f); continue; }
                bool isNew = t >= prevShown;
                if (isNew && revealNewTraits) StartCoroutine(FadeIn(txt));
                else SetAlpha(txt, 1f);
            }

        if (i < _shownTraitCount.Count) _shownTraitCount[i] = traitCount;
    }

    IEnumerator FadeIn(TMP_Text t)
    {
        if (!t) yield break;
        float e = 0f, dur = Mathf.Max(0.05f, traitUnlockFadeDuration);
        SetAlpha(t, 0f);
        while (e < dur) { e += Time.deltaTime; SetAlpha(t, Mathf.Clamp01(e / dur)); yield return null; }
        SetAlpha(t, 1f);
    }

    // Apparition de l'image d'un trait : fondu + léger « pop » de taille.
    IEnumerator RevealImage(Image img)
    {
        if (!img) yield break;
        var rt = img.rectTransform;
        float e = 0f, dur = Mathf.Max(0.05f, traitUnlockFadeDuration);
        SetImageAlpha(img, 0f);
        while (e < dur)
        {
            e += Time.deltaTime;
            float k = Mathf.Clamp01(e / dur);
            SetImageAlpha(img, k);
            rt.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, k);
            yield return null;
        }
        SetImageAlpha(img, 1f);
        rt.localScale = Vector3.one;
    }

    static void SetImageAlpha(Image img, float a)
    {
        if (!img) return;
        var c = img.color; c.a = a; img.color = c;
    }

    static void SetAlpha(TMP_Text t, float a)
    {
        if (!t) return;
        var c = t.color; c.a = a; t.color = c;
    }

    static void SetOutline(Image img, bool on, Color color, float width)
    {
        if (!img) return;
        var o = img.GetComponent<Outline>();
        if (o == null) { if (!on) return; o = img.gameObject.AddComponent<Outline>(); }
        o.effectColor = color;
        o.effectDistance = new Vector2(width, width);
        o.enabled = on;
    }

    // ---- Survol des images de trait → tooltip ----
    void EnsureTraitHover(int segIndex, int traitIndex)
    {
        if (segIndex < 0 || segIndex >= segments.Count) return;
        var seg = segments[segIndex];
        if (seg?.traitImages == null || traitIndex >= seg.traitImages.Count) return;
        var img = seg.traitImages[traitIndex];
        if (!img) return;
        var hover = img.GetComponent<PalierTraitHover>();
        if (!hover) hover = img.gameObject.AddComponent<PalierTraitHover>();
        hover.Init(this, segIndex, traitIndex);
    }

    public void ShowTraitTooltip(int segIndex, int traitIndex, Vector2 screenPos)
    {
        if (tooltip == null) return;
        if (!_segTooltips.TryGetValue(segIndex, out var arr) || traitIndex < 0 || traitIndex >= arr.Length) return;
        var info = arr[traitIndex];

        // Titre (en gras) PUIS description : le titre manquait auparavant.
        string txt;
        if (!string.IsNullOrEmpty(info.name) && !string.IsNullOrEmpty(info.desc))
            txt = $"<b>{info.name}</b>\n{info.desc}";
        else
            txt = string.IsNullOrEmpty(info.name) ? info.desc : info.name;
        if (string.IsNullOrEmpty(txt)) return;

        tooltip.Show(txt, screenPos);
        tooltip.FollowMouse(screenPos);
    }

    public void MoveTraitTooltip(Vector2 screenPos)
    {
        if (tooltip != null && tooltip.gameObject.activeSelf) tooltip.FollowMouse(screenPos);
    }

    public void HideTraitTooltip()
    {
        if (tooltip != null) tooltip.Hide();
    }
}
