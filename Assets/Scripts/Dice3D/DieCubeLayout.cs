using UnityEngine;

// Disposition des six faces d'un dé, telle qu'on la lit sur le dé réel en tenant le 10 vers soi.
// C'est la seule source de vérité : elle sert À LA FOIS à coller les vignettes sur le cube et à
// calculer la rotation qui amène une face donnée vers le joueur. Les deux ne peuvent donc pas
// se contredire, quelle que soit la convention choisie.
[System.Serializable]
public class DieCubeLayout
{
    public string name = "Dé";
    [Tooltip("Coché pour le dé noir (le SUN y remplace le 3).")]
    public bool blackDie = false;

    [Tooltip("Face tournée vers le joueur au repos — le 10 sur les relevés.")]
    public DieFace front = DieFace.Ten;
    [Tooltip("Face opposée à celle du repos (A sur les relevés).")]
    public DieFace back = DieFace.Two;
    [Tooltip("À GAUCHE du joueur quand la face de repos lui fait face (G).")]
    public DieFace left = DieFace.Three;
    [Tooltip("À DROITE du joueur (D).")]
    public DieFace right = DieFace.Five;
    [Tooltip("En HAUT (H).")]
    public DieFace top = DieFace.Four;
    [Tooltip("En BAS (B).")]
    public DieFace bottom = DieFace.Six;

    public DieFace ValueAt(int slot) => slot switch
    {
        DieCubeGeometry.Front  => front,
        DieCubeGeometry.Back   => back,
        DieCubeGeometry.Left   => left,
        DieCubeGeometry.Right  => right,
        DieCubeGeometry.Top    => top,
        DieCubeGeometry.Bottom => bottom,
        _ => front
    };

    // Sur quelle face se trouve cette valeur ? -1 si le dé ne la porte pas — cas réel :
    // un artefact (Miroir, Soleil en bouteille) pose un SUN sur un dé blanc, qui n'en a pas.
    public int SlotOf(DieFace value)
    {
        for (int s = 0; s < DieCubeGeometry.SlotCount; s++)
            if (ValueAt(s) == value) return s;
        return -1;
    }

    // Les cinq dés du Cosmic Wimpout, relevés le 10 face au joueur.
    // 10-3 et 10-4 sont le même dé physique noté sous deux orientations (180° autour de
    // l'axe 10↔6) : conservé tel quel, ça évite que tous les dés reposent identiquement.
    public static DieCubeLayout[] CosmicWimpout() => new[]
    {
        new DieCubeLayout { name = "Dé 1", top = DieFace.Four,  bottom = DieFace.Six,
                            left = DieFace.Three, right = DieFace.Five, back = DieFace.Two },

        new DieCubeLayout { name = "Dé 2", top = DieFace.Two,   bottom = DieFace.Four,
                            left = DieFace.Five,  right = DieFace.Six,  back = DieFace.Three },

        new DieCubeLayout { name = "Dé 3", top = DieFace.Two,   bottom = DieFace.Three,
                            left = DieFace.Five,  right = DieFace.Four, back = DieFace.Six },

        new DieCubeLayout { name = "Dé 4", top = DieFace.Three, bottom = DieFace.Two,
                            left = DieFace.Four,  right = DieFace.Five, back = DieFace.Six },

        new DieCubeLayout { name = "Dé noir", blackDie = true,
                            top = DieFace.Six,   bottom = DieFace.Two,
                            left = DieFace.Five, right = DieFace.Sun,  back = DieFace.Four },
    };
}

// Géométrie du cube : où se trouve chaque face et comment son image est orientée dessus.
//
// Repère : la caméra du dé est posée en -Z et regarde vers +Z, sans rotation. La droite de
// l'écran est donc +X et le haut +Y — les « gauche » et « droite » des relevés se lisent
// directement, sans inversion piégeuse. La face vue par le joueur est celle qui pointe vers -Z.
public static class DieCubeGeometry
{
    public const int Front = 0, Back = 1, Left = 2, Right = 3, Top = 4, Bottom = 5;
    public const int SlotCount = 6;

    public static readonly string[] SlotNames = { "Avant", "Arrière", "Gauche", "Droite", "Haut", "Bas" };

    // Normale sortante de chaque face.
    public static readonly Vector3[] Dir =
    {
        Vector3.back, Vector3.forward, Vector3.left, Vector3.right, Vector3.up, Vector3.down
    };

    // Sens du « haut » de l'image sur chaque face. Pour le dessus et le dessous, le choix
    // reproduit le geste réel : on bascule le dé vers soi et le symbole se lit à l'endroit.
    // Si un symbole apparaît à l'envers lors d'une inclinaison, inverser la valeur ici suffit.
    public static readonly Vector3[] Up =
    {
        Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.forward, Vector3.back
    };

    // Rotation à donner à la vignette pour qu'elle se plaque sur sa face, image à l'endroit.
    public static Quaternion QuadRotation(int slot) => Quaternion.LookRotation(Dir[slot], Up[slot]);

    // Rotation à donner au CUBE pour amener cette face vers le joueur, image à l'endroit.
    // Construite à partir de la même paire (direction, haut) que la vignette : les deux
    // restent donc cohérentes par construction, sans convention à retenir de tête.
    public static Quaternion FacingRotation(int slot)
        => Quaternion.Euler(0f, 180f, 0f) * Quaternion.Inverse(QuadRotation(slot));
}
