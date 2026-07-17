using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

public class JusticeBalanceEffect : IArtifactEffect
{
    public bool IsUsableNow(GameManager gm, out string reason)
    {
        reason = null;
        if (gm == null) { reason = "Pas de GameManager."; return false; }

        // Utilisable en post-jet (Normal/Clause/WaitEnd) et s’il reste au moins un 2/6 non verrouillé
        if (!gm.CanUseRerollTurnNow()) { reason = "Utilisable après un jet."; return false; }

        bool ok = gm.dice != null && gm.dice.Any(d => d != null && !d.isLocked &&
                                                      (d.GetFace() == DieFace.Two || d.GetFace() == DieFace.Six));
        if (!ok) reason = "Aucun 2/6 disponible à transformer.";
        return ok;
    }

    public void BeginUse(GameManager gm, Action onConsumed)
    {
        // Transformation AUTOMATIQUE : tous les 2→5 et 6→10 non verrouillés, sans clic.
        int transformed = 0;
        foreach (var d in gm.dice)
        {
            if (d == null || d.isLocked) continue;
            var f = d.GetFace();
            if (f == DieFace.Two) { d.SetFace(DieFace.Five); transformed++; }
            else if (f == DieFace.Six) { d.SetFace(DieFace.Ten); transformed++; }
        }

        if (transformed > 0)
        {
            // Si on était en WaitEnd et qu’on crée du marquant, repasser en Normal pour autoriser le clic
            gm.Artifacts_ReevaluateAfterDiceChanged(
                $"Balance de la justice : {transformed} dé{(transformed > 1 ? "s" : "")} transformé{(transformed > 1 ? "s" : "")} (2→5, 6→10).");
            gm.uiLog?.Append($"Balance de la justice : {transformed} dé(s) transformé(s).");
        }

        onConsumed?.Invoke();
    }
}


public class ApothecaryFortifierEffect : IArtifactEffect
{
    // Éligible : face 2..6, dé libre OU sélectionné ce jet (la sélection sera annulée avant transformation)
    static bool IsTargetable(GameManager gm, int i)
    {
        if (i < 0 || i >= gm.dice.Count) return false;
        var d = gm.dice[i];
        if (d == null) return false;
        if (d.isLocked && !gm.IsDieSelectedThisRoll(i)) return false; // verrous figés (Flash / jets précédents) interdits
        var f = d.GetFace();
        return f == DieFace.Two || f == DieFace.Three || f == DieFace.Four || f == DieFace.Five || f == DieFace.Six;
    }

    public bool IsUsableNow(GameManager gm, out string reason)
    {
        reason = null;
        if (gm == null || !gm.CanUseRelanceNow()) { reason = "Utilisable après un jet."; return false; }

        bool ok = gm.dice != null && Enumerable.Range(0, gm.dice.Count).Any(i => IsTargetable(gm, i));
        if (!ok) { reason = "Aucun dé eligible (2/3/4/5/6)."; return false; }
        return true;
    }

    public void BeginUse(GameManager gm, Action onConsumed)
    {
        gm.BeginExternalDiePick(i => IsTargetable(gm, i), dieIndex =>
        {
            // Si le dé avait été sélectionné ce jet, on annule d'abord sa sélection (points rendus)
            gm.Artifact_TryUnselectDie(dieIndex);

            var f = gm.dice[dieIndex].GetFace();
            var newF = f switch
            {
                DieFace.Two => DieFace.Three,
                DieFace.Three => DieFace.Four,
                DieFace.Four => DieFace.Five,
                DieFace.Five => DieFace.Six,
                DieFace.Six => DieFace.Ten,
                _ => f
            };
            gm.dice[dieIndex].SetFace(newF);

            gm.Artifacts_RefreshEligibilityAndUI();
            onConsumed?.Invoke();
        }, "Fortifiant : choisis un dé à améliorer (+1).");
    }
}

public class FlashScrollEffect : IArtifactEffect
{
    public bool IsUsableNow(GameManager gm, out string reason)
    {
        reason = null;
        if (gm == null || !gm.CanUseRerollTurnNow()) { reason = "Utilisable après un jet."; return false; }

        // besoin d'au moins 3 dés non verrouillés
        int free = gm.dice?.Count(d => d != null && !d.isLocked) ?? 0;
        if (free < 3) { reason = "Pas assez de dés disponibles (3 nécessaires)."; return false; }
        return true;
    }

    public void BeginUse(GameManager gm, Action onConsumed)
    {
        var candidates = Enumerable.Range(0, gm.dice.Count)
            .Where(i => gm.dice[i] != null && !gm.dice[i].isLocked).ToList();

        bool Filter(int i) => candidates.Contains(i);

        gm.BeginExternalDiePick(Filter, idx =>
        {
            var pivot = gm.dice[idx].GetFace();
            var target = ChooseFlashFace(pivot, gm, idx, candidates);

            // choisir 2 autres dés : d'abord ceux qui ont déjà la face cible
            var others = candidates.Where(i => i != idx).ToList();
            var chosen = others.Where(i => gm.dice[i].GetFace() == target).Take(2).ToList();
            if (chosen.Count < 2)
            {
                // complète avec d’autres dés et force leur face
                var missing = 2 - chosen.Count;
                var extra = others.Where(i => gm.dice[i].GetFace() != target).Take(missing).ToList();
                chosen.AddRange(extra);
            }

            if (chosen.Count < 2)
            {
                gm.hintBanner?.Show("Pas assez de dés pour compléter un FLASH."); return;
            }

            // set faces (pivot + 2 autres), puis créer le flash standard
            gm.dice[idx].SetFace(target);
            gm.dice[chosen[0]].SetFace(target);
            gm.dice[chosen[1]].SetFace(target);

            gm.Artifacts_CreateFlashFromIndices(new List<int> { idx, chosen[0], chosen[1] }, target);
            onConsumed?.Invoke();
        }, "Parchemin de Flash : choisis le dé pivot.");
    }

    // Si pivot = Sun, on choisit une face numérique qui “colle” aux dés restants (Ten > Six > Five > Four > Three > Two)
    DieFace ChooseFlashFace(DieFace pivot, GameManager gm, int pivotIdx, List<int> free)
    {
        if (pivot != DieFace.Sun) return pivot;

        var order = new[] { DieFace.Ten, DieFace.Six, DieFace.Five, DieFace.Four, DieFace.Three, DieFace.Two };
        foreach (var f in order)
            if (free.Any(i => i != pivotIdx && gm.dice[i].GetFace() == f)) return f;
        return DieFace.Ten;
    }
}

public class MirrorEffect : IArtifactEffect
{
    public bool IsUsableNow(GameManager gm, out string reason)
    {
        reason = null;
        if (gm == null) { reason = "Contexte manquant."; return false; }
        if (!gm.CanUseRerollTurnNow()) { reason = "Utilisable après un jet."; return false; }
        // au moins 2 dés pour copier
        int count = gm.dice != null ? gm.dice.Count(d => d != null) : 0;
        if (count < 2) { reason = "Pas assez de dés."; return false; }
        return true;
    }

    public void BeginUse(GameManager gm, System.Action onConsumed)
    {
        // 1) choisir la source
        gm.BeginExternalDiePick(
            i => i >= 0 && i < gm.dice.Count && gm.dice[i] != null,
            srcIdx =>
            {
                var src = gm.dice[srcIdx];
                var srcFace = src.GetFace();

                gm.hintBanner?.Show("Miroir : choisis la cible.");
                // 2) choisir la cible (≠ source)
                gm.BeginExternalDiePick(
                    j => j >= 0 && j < gm.dice.Count && gm.dice[j] != null && j != srcIdx,
                    dstIdx =>
                    {
                        var dst = gm.dice[dstIdx];
                        dst.SetFace(srcFace); // copie immédiate

                        // Recalcule les éligibilités et, si on était en WaitEnd et que ça crée du marquant, repasse en Normal
                        gm.Artifacts_ReevaluateAfterDiceChanged("Miroir : face copiée — tu peux sélectionner/continuer.");
                        onConsumed?.Invoke();
                    },
                    "Miroir : clique la cible."
                );
            },
            "Miroir : clique la source."
        );
    }
}
