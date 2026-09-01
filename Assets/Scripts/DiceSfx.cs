using UnityEngine;

// Aiguillage des sons de dés : traduit « ce qui vient de se passer » en « quel clip jouer ».
//
// Le point délicat, c'est le NOMBRE. Un jet de 5 dés, ce n'est pas cinq fois le son
// d'un dé : c'est UN son de 5 dés. Or GameManager lance ses dés dé par dé, dans une
// vingtaine d'endroits différents (jet normal, Clause, hot-dice, artefacts, IA…).
// Plutôt que d'instrumenter chaque appel, DieView.Roll() signale simplement « moi
// aussi » : on additionne les signalements de la frame, et en fin de frame (AudioManager
// appelle FlushFrame en LateUpdate) on joue UN seul son choisi selon le total.
// Tous les dés d'un même jet partant dans la même frame, le compte est toujours juste.
//
// Attraper / poser, eux, sont déjà groupés à l'appel (GameManager connaît le lot) :
// on leur passe directement le nombre. Ces sons partent AVEC le geste, sans attendre la
// fin du vol du dé : un bruit calé sur l'atterrissage se décolle de l'action à l'oreille.
public static class DiceSfx
{
    static int pendingRolls;

    static DiceSfxLibrary Library
    {
        get
        {
            var am = AudioManager.Instance;
            return am ? am.diceSfx : null;
        }
    }

    // ---- Jets -----------------------------------------------------------------------------

    // À appeler UNE FOIS PAR DÉ lancé. Le son est choisi en fin de frame sur le total.
    // Sans AudioManager, personne ne viderait le compteur : on n'accumule pas, sinon le
    // premier son joué après coup croirait qu'on vient de lancer cinquante dés.
    public static void NotifyRoll()
    {
        if (!AudioManager.Instance) return;
        pendingRolls++;
    }

    // Appelé par AudioManager en LateUpdate : la frame est finie, on connaît le total.
    public static void FlushFrame()
    {
        if (pendingRolls <= 0) return;
        int n = pendingRolls;
        pendingRolls = 0;

        var lib = Library;
        if (lib != null) Play(lib.ForRoll(n));
    }

    // ---- Attraper / poser -----------------------------------------------------------------

    // `count` : nombre de dés attrapés d'un coup (1 = le joueur en soulève un ; 2+ = le jeu
    // en ramasse plusieurs, Flash ou Full).
    public static void Take(int count)
    {
        if (count <= 0) return;
        var lib = Library;
        if (lib != null) Play(lib.ForTake(count));
    }

    public static void Drop(int count)
    {
        if (count <= 0) return;
        var lib = Library;
        if (lib != null) Play(lib.ForDrop(count));
    }

    // Plusieurs dés qui se posent EN MÊME TEMPS : un clac par dé, espacés de `spacing`.
    // Nécessaire parce que le retour groupé depuis la zone de sélection lance tous les dés
    // dans la même frame, sans échelonnement. Sans écart artificiel, le garde-fou
    // anti-répétition de la banque n'en laisserait passer qu'un seul — et cinq clacs
    // rigoureusement simultanés ne s'entendraient de toute façon que comme un seul, en
    // plus fort. Ailleurs (Flash, relance) les vols sont déjà échelonnés : Drop(1) par dé
    // au décollage suffit, la cascade sort du mouvement lui-même.
    public static void DropCascade(int count, float spacing = 0.06f)
    {
        if (count <= 0) return;
        var am = AudioManager.Instance;
        if (count == 1 || spacing <= 0f || !am) { Drop(1); return; }
        am.StartCoroutine(CascadeRoutine(count, spacing));
    }

    static System.Collections.IEnumerator CascadeRoutine(int count, float spacing)
    {
        for (int i = 0; i < count; i++)
        {
            Drop(1);
            // Temps réel : la rentrée des dés s'anime en unscaled, le son doit suivre même
            // si le jeu tourne au ralenti ou en pause.
            if (i < count - 1) yield return new WaitForSecondsRealtime(spacing);
        }
    }

    public static void Refuse()
    {
        var lib = Library;
        if (lib != null) Play(lib.refuseDie);
    }

    // ---- Lecture ---------------------------------------------------------------------------

    static void Play(DiceSfxLibrary.Entry entry)
    {
        if (entry == null) return;
        var clip = entry.Pick();          // null si vide, ou si le garde-fou anti-répétition bloque
        if (!clip) return;

        var am = AudioManager.Instance;
        if (!am) return;

        am.PlaySfx(clip, entry.volume, entry.RandomPitch());
    }
}
