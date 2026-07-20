using System;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;

public class SurpriseDieEffect : IArtifactEffect
{
    public bool IsUsableNow(GameManager gm, out string reason)
    {
        reason = null;
        if (gm == null || !gm.CanUseRerollTurnNow()) { reason = "Utilisable après un jet."; return false; }
        return true;
    }

    public void BeginUse(GameManager gm, Action onConsumed)
    {
        gm.StartCoroutine(Use(gm, onConsumed));
    }

    private System.Collections.IEnumerator Use(GameManager gm, Action done)
    {
        // -> roll immédiat
        // -> autoBankSingles: true (ajoute 5/10/10 tout de suite)
        // -> ephemeral: false (on ne détruit pas si marquant)
        // -> lingerIfBanked: true (reste visible jusqu’à ROLL/NEXT, mais retiré du pool)
        // -> canCauseWimpout: false (ne déclenche jamais un wimpout “artefact”)
        yield return gm.StartCoroutine(
            gm.Artifact_RollAddDieNow(
                forcedFace: null,
                autoBankSingles: true,
                ephemeral: false,
                bonusIfFlash: false,
                bonusFlashTarget: DieFace.Ten,
                lingerIfBanked: true,
                canCauseWimpout: false
            )
        );
        done?.Invoke();
    }
}


public class BottleSunEffect : IArtifactEffect
{
    public bool IsUsableNow(GameManager gm, out string reason)
    {
        reason = null;
        if (gm == null || !gm.CanUseRerollTurnNow()) { reason = "Utilisable après un jet."; return false; }
        return true;
    }

    public void BeginUse(GameManager gm, Action onConsumed)
    {
        gm.StartCoroutine(Use(gm, onConsumed));
    }

    private System.Collections.IEnumerator Use(GameManager gm, Action done)
    {
        // forcedFace=Sun, autoBankSingles=true (ajoute 10 tout de suite), ephemeral=true (disparaît de suite)
        yield return gm.StartCoroutine(
            gm.Artifact_RollAddDieNow(
                forcedFace: DieFace.Sun,
                autoBankSingles: true,     // +10 immédiat
                ephemeral: false,          // on ne détruit pas tout de suite
                bonusIfFlash: false,
                bonusFlashTarget: DieFace.Ten,
                lingerIfBanked: true,      // ← reste visible jusqu’à ROLL/NEXT, mais retiré du pool
                canCauseWimpout: false
            )
        );
        done?.Invoke();
    }
}


public class LoveFilterEffect : IArtifactEffect
{
    static int Rank(DieFace f) => f switch
    {
        DieFace.Two => 0, DieFace.Three => 1, DieFace.Four => 2,
        DieFace.Five => 3, DieFace.Six => 4, DieFace.Ten => 5, DieFace.Sun => 5,
        _ => 5
    };
    static DieFace Normalize(DieFace f) => (f == DieFace.Sun) ? DieFace.Ten : f;

    public bool IsUsableNow(GameManager gm, out string reason)
    {
        reason = null;
        if (gm == null || !gm.CanUseRerollTurnNow()) { reason = "Utilisable après un jet."; return false; }
        if (gm.dice == null || gm.dice.All(d => d == null)) { reason = "Aucun dé sur la table."; return false; }
        return true;
    }

    public void BeginUse(GameManager gm, Action onConsumed)
    {
        gm.StartCoroutine(Use(gm, onConsumed));
    }

    private System.Collections.IEnumerator Use(GameManager gm, System.Action done)
    {
        // 1) S’assurer qu’on a un dé d’ajout présent (intégré au pool, affiché où tu veux)
        if (gm.currentAddedDie == null)
            yield return gm.StartCoroutine(gm.Artifact_AddSurpriseDie());

        var add = gm.currentAddedDie;
        if (!add) { done?.Invoke(); yield break; }

        int addIdx = gm.dice.IndexOf(add);

        // 2) Candidats = dés NON verrouillés du board, HORS dé d'ajout
        //    (les dés verrouillés ne peuvent pas participer à un flash,
        //     et le dé d'ajout fraîchement créé a une face interne factice)
        var candidates = Enumerable.Range(0, gm.dice.Count)
            .Where(i => i != addIdx && gm.dice[i] != null && !gm.dice[i].isLocked)
            .ToList();

        if (candidates.Count == 0)
        {
            gm.Artifacts_RemoveAddedDie(add);
            gm.hintBanner?.Show("Filtre d’amour : aucun dé disponible sur le board.");
            gm.Artifacts_RefreshUI();
            done?.Invoke();
            yield break;
        }

        // 3) Face la plus FAIBLE posée parmi les candidats (SUN compté comme 10)
        DieFace weakest = DieFace.Ten;
        bool any = false;
        foreach (var i in candidates)
        {
            var f = Normalize(gm.dice[i].GetFace());
            if (!any || Rank(f) < Rank(weakest)) { weakest = f; any = true; }
        }

        // 4) Appliquer IMMÉDIATEMENT la face au dé d’ajout
        add.SetFace(weakest);

        // 5) Tenter de CRÉER le FLASH immédiatement :
        //    (a) dé d'ajout + 2 dés de la même face
        //    (b) dé d'ajout + 1 dé de la même face + 1 SUN (le SUN complète la paire)
        var same = candidates.Where(i => gm.dice[i].GetFace() == weakest).ToList();
        var suns = candidates.Where(i => gm.dice[i].GetFace() == DieFace.Sun).ToList();

        List<int> triple = null;
        if (same.Count >= 2)
        {
            triple = new List<int> { addIdx, same[0], same[1] };
        }
        else if (same.Count == 1 && suns.Count > 0)
        {
            // chiffre + SUN : le SUN est bien pris en compte pour compléter le flash
            triple = new List<int> { addIdx, same[0], suns[0] };
        }

        if (triple != null)
        {
            // FLASH immédiat : lock + points + Clause. Le bonus du Filtre est désormais
            // +30% sur la bank de ce tour (modificateur de score) au lieu du +10 fixe.
            gm.Artifacts_CreateFlashFromIndices(triple, weakest);
            gm.Artifacts_ActivateLoveFilterBankBonus(1.3f);

            // Le dé d’ajout fait partie du flash (verrouillé) → on NE le retire pas maintenant.
            gm.Artifacts_RefreshUI();
            done?.Invoke();
            yield break;
        }

        // 6) Pas de flash immédiat → gérer le single ou rien
        int singlePts = (weakest == DieFace.Five) ? 5 : (weakest == DieFace.Ten ? 10 : 0);
        if (singlePts > 0)
        {
            // Crédite tout de suite et retire le dé d’ajout
            gm.Artifacts_AddTurnBonus(singlePts, $"Filtre d’amour : +{singlePts} points.");
            gm.Artifacts_RemoveAddedDie(add);
            gm.Artifacts_SetPhaseNormalIfWaitEnd();
            gm.Artifacts_RefreshUI();
            done?.Invoke();
            yield break;
        }
        else
        {
            // Aucun point : retirer le dé d’ajout et laisser l’état tel quel
            gm.Artifacts_RemoveAddedDie(add);
            gm.hintBanner?.Show("Filtre d’amour : aucune combinaison immédiate.");
            gm.Artifacts_RefreshUI();
            done?.Invoke();
            yield break;
        }
    }

}

