using System;
using UnityEngine;

public class CheaterBoneEffect : IArtifactEffect
{
    public bool IsUsableNow(GameManager gm, out string reason)
    {
        reason = null;
        if (gm == null) { reason = "Contexte manquant."; return false; }

        // Fenêtre post-jet (Normal/Clause/WaitEnd) + au moins 1 dé relançable
        if (!gm.CanUseRerollTurnNow()) { reason = "Utilisable après un jet."; return false; }
        if (gm.dice == null) { reason = "Aucun dé."; return false; }

        for (int i = 0; i < gm.dice.Count; i++)
        {
            var d = gm.dice[i];
            if (d != null && !d.isLocked) return true;
        }
        reason = "Aucun dé à relancer.";
        return false;
    }

    public void BeginUse(GameManager gm, Action onConsumed)
    {
        bool Filter(int i)
        {
            if (i < 0 || i >= gm.dice.Count) return false;
            var d = gm.dice[i];
            return d != null && !d.isLocked;
        }

        gm.BeginExternalDiePick(Filter, dieIndex =>
        {
            gm.StartCoroutine(gm.Artifact_RerollOneDie(dieIndex));
            onConsumed?.Invoke();
        }, "Os du Tricheur : choisis un dé à relancer.");
    }
}

public class TimeRerollTurnEffect : IArtifactEffect
{
    public bool IsUsableNow(GameManager gm, out string reason)
    {
        reason = null;
        if (gm == null) { reason = "Contexte manquant."; return false; }
        // Relance TOUS les dés (même sélectionnés/verrouillés) → il suffit d'être après un jet.
        if (!gm.CanUseRelanceNow())
        { reason = "Utilisable après un jet."; return false; }
        return true;
    }

    public void BeginUse(GameManager gm, Action onConsumed)
    {
        gm.StartCoroutine(gm.Artifact_RerollTurn());
        onConsumed?.Invoke();
    }
}

public class TimeRerollMatchEffect : IArtifactEffect
{
    public bool IsUsableNow(GameManager gm, out string reason)
    {
        reason = null;
        if (gm == null) { reason = "Contexte manquant."; return false; }
        if (!gm.CanRestartMatchNow())
        { reason = "Utilisable uniquement avant une défaite."; return false; }
        return true;
    }

    public void BeginUse(GameManager gm, Action onConsumed)
    {
        gm.Artifact_RestartMatch();
        onConsumed?.Invoke();
    }
}
