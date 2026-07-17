using System;
using UnityEngine;

public class FuneralLedgerEffect : IArtifactEffect
{
    public int threshold = 70;  // Seuil de 70 pts
    public float bonusPct = 0.50f;  // +50%
    public int penaltyFlat = -20;

    public bool IsUsableNow(GameManager gm, out string reason)
    {
        reason = null;
        if (gm == null) { reason = "Contexte manquant."; return false; }

        // Refuse si un autre mod de score est déjà armé
        if (gm.HasActiveScoreModifier()) { reason = "Un modificateur de score est déjà actif."; return false; }

        // ✅ Autoriser AVANT le premier jet / en phase normale
        // ✅ ET juste après un FLASH, AVANT de sélectionner/roll pour Clause
        bool allow =
            gm.CanActivateScorePreRollNow()     // AwaitFirstRoll ou Normal, hors clause bloquante
            || gm.CanActivateScorePreClauseNow(); // Nouvelle fenêtre “pré-clause”

        if (!allow)
        {
            reason = "Utilisable avant la Clause (ou avant/juste après le 1er jet).";
            return false;
        }

        return true;
    }


    public void BeginUse(GameManager gm, Action onConsumed)
    {
        var mod = new GameManager.ActiveScoreMod
        {
            mode = GameManager.ActiveScoreMod.Mode.FuneralLedger,
            threshold = threshold,
            bonusPct = bonusPct,
            penaltyFlat = penaltyFlat
        };

        gm.ActivateScoreModifier(mod);

        // Feedback via HintBanner / UILog (pas de LogUX)
        gm.hintBanner?.Show($"Livre des Comptes Funèbres activé : +{Mathf.RoundToInt(bonusPct * 100)}% si ≥ {threshold}, sinon {penaltyFlat} pts.");
        gm.uiLog?.Append("Artefact: Livre des Comptes Funèbres activé.");

        gm.hintBanner?.Show(
            gm.CanActivateScorePreClauseNow()
            ? $"Livre des Comptes Funèbres armé : le FLASH comptera dans le seuil (puis ×{Mathf.RoundToInt((1f+bonusPct)*100)}% si atteint)."
            : $"Livre des Comptes Funèbres activé : +{Mathf.RoundToInt(bonusPct*100)}% si le seuil est atteint, sinon {penaltyFlat}."
        );


        onConsumed?.Invoke();
    }
}

/// <summary>
/// SCORE (Pré-jet) — "Dot de mariage"
/// Multiplie par 2 le score bancable de CE tour (banque du joueur).
/// </summary>
public class MarriageDotEffect : IArtifactEffect
{
    public float multiplier = 2f; // ×2

    public bool IsUsableNow(GameManager gm, out string reason)
    {
        reason = null;
        if (gm == null) { reason = "Contexte manquant."; return false; }

        // Même fenêtre que les autres SCORE pré-jet
        if (!gm.CanActivateScorePreRollNow()) { reason = "Utilisable avant (ou juste après) le premier jet."; return false; }
        if (gm.HasActiveScoreModifier()) { reason = "Un modificateur de score est déjà actif."; return false; }

        return true;
    }

    public void BeginUse(GameManager gm, Action onConsumed)
    {
        var mod = new GameManager.ActiveScoreMod
        {
            mode = GameManager.ActiveScoreMod.Mode.MarriageDot,
            multiplier = multiplier,
            threshold = 0,
            bonusPct = 0f,
            penaltyFlat = 0
        };

        gm.ActivateScoreModifier(mod);

        gm.hintBanner?.Show($"Dot de mariage activé : ×{multiplier:0.##} sur la banque de ce tour.");
        gm.uiLog?.Append("Artefact: Dot de mariage (×2) activé.");

        onConsumed?.Invoke(); // consommation immédiate
    }
}

/// <summary>
/// SCORE — "Ticket d’entrée"
/// Ouvre immédiatement le joueur pour ce match (comme si le seuil d’entrée était atteint).
/// Optionnellement, peut aussi créditer 35 pts (voir GameManager.Artifact_GrantEntryNow).
/// </summary>
public class EntryTicketEffect : IArtifactEffect
{
    public bool grantPoints = true; // <- désormais TRUE par défaut

    public bool IsUsableNow(GameManager gm, out string reason)
    {
        reason = null;
        if (gm == null) { reason = "Contexte manquant."; return false; }
        if (!gm.IsPlayerTurn || gm.IsMatchOverOrScreen())
        { reason = "Utilisable pendant ton tour."; return false; }
        if (gm.IsPlayerAlreadyOpened())
        { reason = "Tu es déjà ouvert."; return false; }
        return true;
    }

    public void BeginUse(GameManager gm, Action onConsumed)
    {
        gm.Artifact_GrantEntryNow(grantPoints);

        gm.hintBanner?.Show(
            grantPoints
                ? $"Ticket d’entrée : +{gm.EntryThreshold} pts et ouverture immédiate."
                : "Ticket d’entrée : ouverture immédiate."
        );
        gm.uiLog?.Append("Artefact: Ticket d’entrée utilisé.");

        onConsumed?.Invoke();
    }
}


