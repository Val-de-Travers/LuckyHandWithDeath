using UnityEngine.SceneManagement;

// Point unique pour savoir si l'on est dans la scène de tutoriel.
//
// Certains panneaux (inventaire, tooltip d'artefact) reçoivent un ordre d'affichage
// très élevé UNIQUEMENT pour passer au-dessus du voile du tutoriel (5000+). En scène
// principale, ces priorités géantes faisaient remonter ces éléments par-dessus d'autres
// panneaux qui doivent rester au-dessus d'eux. On ne les applique donc qu'ici.
public static class TutorialScene
{
    public const string SceneName = "TutoScene";

    public static bool IsActive => SceneManager.GetActiveScene().name == SceneName;
}
