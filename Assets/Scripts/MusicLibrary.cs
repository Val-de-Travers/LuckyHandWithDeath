using UnityEngine;

// Banque des musiques d'ambiance, sur le même principe que DiceSfxLibrary : un seul asset
// dans Assets/Resources/MusicLibrary.asset, chargé tout seul par AudioManager. Aucune scène
// à recâbler.
//
// AudioManager enchaîne ces pistes en boucle pendant la partie, dans un ordre mélangé :
// chaque morceau va jusqu'à son terme, puis un silence, puis le suivant. Ajouter une
// 4ᵉ musique = la glisser dans la liste, rien d'autre.
[CreateAssetMenu(fileName = "MusicLibrary", menuName = "Cosmic Wimpout/Music Library")]
public class MusicLibrary : ScriptableObject
{
    [Header("Musiques de partie")]
    [Tooltip("Jouées en boucle pendant une partie (toute scène contenant un GameManager). " +
             "L'ordre de la liste n'a pas d'importance si le mélange est actif.")]
    public AudioClip[] gameplayTracks;

    [Header("Enchaînement")]
    [Tooltip("Ordre mélangé plutôt que l'ordre de la liste. Le mélange évite aussi qu'une " +
             "piste soit rejouée juste après elle-même au changement de cycle.")]
    public bool shuffle = true;

    [Range(0f, 30f)]
    [Tooltip("Silence (s) entre la fin d'un morceau et le début du suivant. Le morceau va " +
             "toujours jusqu'à son terme : ce silence est une respiration, pas une coupure.")]
    public float gapBetweenTracks = 5f;

    [Range(0f, 10f)]
    [Tooltip("Durée (s) du fondu de sortie quand on quitte la partie pour le menu.")]
    public float fadeOutOnLeave = 1.5f;

    [Range(0f, 1f)]
    [Tooltip("Dosage de la musique PAR RAPPORT aux effets sonores. Le curseur Musique des " +
             "options s'applique par-dessus. C'est ici qu'on fait de la place aux dés : une " +
             "ambiance de fond vit très bien à 0,5–0,7.")]
    public float trackVolume = 0.65f;

    public bool HasTracks => gameplayTracks != null && gameplayTracks.Length > 0;
}
