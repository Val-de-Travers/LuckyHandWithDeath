using System;
using UnityEngine;

// Base commune : un artefact de Contre-Jeu n'est jouable QUE pendant la fenêtre
// ouverte au tour de l'IA (avant/après un lancé).
public abstract class CounterPlayEffectBase : IArtifactEffect
{
    public bool IsUsableNow(GameManager gm, out string reason)
    {
        reason = null;
        if (gm == null) { reason = "Contexte manquant."; return false; }
        if (!gm.IsCounterPlayWindowOpen())
        {
            reason = "Contre-Jeu : jouable uniquement pendant le tour de l'IA (avant/après un lancé).";
            return false;
        }
        return true;
    }

    public abstract void BeginUse(GameManager gm, Action onConsumed);
}

// Bourse percée : -25% sur la bank de l'IA ce tour.
public class PiercedPurseEffect : CounterPlayEffectBase
{
    public override void BeginUse(GameManager gm, Action onConsumed)
    {
        gm.CounterPlay_PiercedPurse();
        onConsumed?.Invoke();
    }
}

// Corne de sommeil : annule la prochaine Clause de l'IA.
public class SleepingHornCounterEffect : CounterPlayEffectBase
{
    public override void BeginUse(GameManager gm, Action onConsumed)
    {
        gm.CounterPlay_SleepingHorn();
        onConsumed?.Invoke();
    }
}

// Coup de table : relance un dé aléatoire (non verrouillé) de l'IA.
public class SlamTableEffect : CounterPlayEffectBase
{
    public override void BeginUse(GameManager gm, Action onConsumed)
    {
        gm.CounterPlay_SlamTable();
        onConsumed?.Invoke();
    }
}

// Clocher des Veilleurs : le joueur désigne un dé de l'IA à relancer.
public class WatchersTowerEffect : CounterPlayEffectBase
{
    public override void BeginUse(GameManager gm, Action onConsumed)
    {
        gm.CounterPlay_WatchersTower();
        onConsumed?.Invoke();
    }
}

// Poison douteux : le joueur désigne un dé de l'IA et lui enlève 1 (face précédente).
public class QuestionablePoisonCounterEffect : CounterPlayEffectBase
{
    public override void BeginUse(GameManager gm, Action onConsumed)
    {
        gm.CounterPlay_QuestionablePoison();
        onConsumed?.Invoke();
    }
}
