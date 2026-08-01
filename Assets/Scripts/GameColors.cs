using UnityEngine;

// Palette partagée du jeu : une seule source de vérité pour que le journal d'actions et les
// éléments de la table (contour de FLASH, contour de CLAUSE, vies…) restent cohérents.
// Modifier une couleur ici la change PARTOUT.
public static class GameColors
{
    // Codes hexa utilisés par le rich text TMP des journaux.
    public const string FlashHex  = "D9B83E"; // jaune  — FLASH
    public const string ClauseHex = "4CE377"; // vert   — CLAUSE
    public const string FullHex   = "E3902E"; // orange — FULL (ajuste si besoin)
    public const string PointsHex = "E34C4C"; // rouge  — points

    // Mêmes couleurs pour l'UI / les contours de dés.
    public static readonly Color Flash  = new Color(0.851f, 0.722f, 0.243f); // #D9B83E
    public static readonly Color Clause = new Color(0.298f, 0.890f, 0.467f); // #4CE377
    public static readonly Color Points = new Color(0.890f, 0.298f, 0.298f); // #E34C4C
}
