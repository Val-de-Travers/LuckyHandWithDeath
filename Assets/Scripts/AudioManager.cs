using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Gestionnaire audio global : musique + effets sonores, volumes persistés (PlayerPrefs).
//
// Il n'a besoin d'être posé dans AUCUNE scène : s'il n'en existe pas au chargement, il se
// fabrique tout seul (voir Bootstrap plus bas) et survit aux changements de scène
// (DontDestroyOnLoad). Pose-le quand même à la main dans MainMenu si tu veux lui assigner
// une musique de démarrage depuis l'inspecteur.
//
// Usage depuis n'importe quel script :
//   AudioManager.Instance?.PlaySfx(clip);
//   AudioManager.Instance?.PlayMusic(clip);
// Pour les dés, passe plutôt par DiceSfx (qui compte les dés et choisit le bon clip).
// Les sliders d'options appellent SetMusicVolume / SetSfxVolume (MenuUI s'en charge).
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    // Noms des assets dans un dossier Resources (Assets/Resources/…)
    const string DICE_SFX_RESOURCE = "DiceSfxLibrary";
    const string MUSIC_RESOURCE = "MusicLibrary";

    [Header("Sources (créées automatiquement si vides)")]
    [Tooltip("Source dédiée à la musique. Les pistes s'y succèdent une à une : les morceaux " +
             "ne se recouvrent jamais, une seule source suffit donc.")]
    public AudioSource musicSource;
    [Tooltip("Source dédiée aux effets sonores (repli si le pool de voix est indisponible).")]
    public AudioSource sfxSource;

    [Header("Musique")]
    [Tooltip("Musique lancée automatiquement au démarrage (optionnel).")]
    public AudioClip startupMusic;

    [Header("Effets sonores")]
    [Tooltip("Nombre de sons pouvant se superposer. Chaque voix a sa propre hauteur : " +
             "c'est ce qui permet de varier le pitch d'un son à l'autre.")]
    [Range(2, 32)] public int sfxVoices = 8;

    [Tooltip("Banque des sons de dés. Laissée vide, elle est chargée depuis " +
             "Assets/Resources/DiceSfxLibrary.asset.")]
    public DiceSfxLibrary diceSfx;

    [Tooltip("Banque des musiques d'ambiance. Laissée vide, elle est chargée depuis " +
             "Assets/Resources/MusicLibrary.asset.")]
    public MusicLibrary music;

    // Clés partagées avec MenuUI (sliders d'options)
    public const string PREF_MUSIC_VOLUME = "pref_vol_music";
    public const string PREF_SFX_VOLUME = "pref_vol_sfx";

    public static float SavedMusicVolume => PlayerPrefs.GetFloat(PREF_MUSIC_VOLUME, 0.8f);
    public static float SavedSfxVolume => PlayerPrefs.GetFloat(PREF_SFX_VOLUME, 1f);

    AudioSource[] voices;
    int nextVoice;
    float sfxVolume = 1f;

    // Musique. `musicFade` (0 = muette, 1 = pleine) ne sert plus qu'à la sortie vers le
    // menu ; le volume réel est recalculé à partir de lui ET du curseur des options, pour
    // que déplacer le curseur pendant un fondu ne fige pas la source à mi-chemin.
    float musicVolume = 1f;
    float musicFade = 1f;
    Coroutine playlistCo;
    readonly List<int> trackBag = new List<int>();
    int lastTrack = -1;

    // Aucune scène du projet ne porte d'AudioManager : sans ça, AudioManager.Instance serait
    // toujours null et tous les PlaySfx du jeu partiraient dans le vide. On en crée un après
    // le chargement de la scène, mais seulement si personne n'en a déjà posé un (sinon on
    // perdrait les clips assignés dans l'inspecteur).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        if (FindAnyObjectByType<AudioManager>() != null) return;
        new GameObject("[AudioManager]").AddComponent<AudioManager>();
    }

    void Awake()
    {
        // Singleton persistant : une seule instance, la première gagne.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (!musicSource)
        {
            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
        }
        musicSource.loop = false;   // la playlist enchaîne elle-même : pas de boucle de piste
        musicSource.spatialBlend = 0f;
        if (!sfxSource)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.loop = false;
            sfxSource.playOnAwake = false;
        }

        // Pool de voix : PlayOneShot ne sait pas varier la hauteur son par son (le pitch est
        // porté par la source, pas par le clip). Une source par voix résout ça et permet
        // aussi de superposer proprement plusieurs sons de dés.
        voices = new AudioSource[Mathf.Max(2, sfxVoices)];
        for (int i = 0; i < voices.Length; i++)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.loop = false;
            s.playOnAwake = false;
            s.spatialBlend = 0f; // 2D : les dés sont dans l'UI, aucune position dans le monde
            voices[i] = s;
        }

        // Volumes sauvegardés appliqués dès le démarrage
        sfxVolume = SavedSfxVolume;
        musicVolume = SavedMusicVolume;
        sfxSource.volume = sfxVolume;
        ApplyMusicVolume();

        if (!diceSfx) diceSfx = Resources.Load<DiceSfxLibrary>(DICE_SFX_RESOURCE);
        if (!music) music = Resources.Load<MusicLibrary>(MUSIC_RESOURCE);

        if (startupMusic) PlayMusic(startupMusic);

        SceneManager.sceneLoaded += OnSceneLoaded;
        SyncMusicToScene(); // la scène de départ est déjà chargée : l'évènement ne la couvre pas
    }

    void OnDestroy()
    {
        if (Instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode) => SyncMusicToScene();

    // La musique d'ambiance suit la PRÉSENCE D'UN GameManager, pas un nom de scène : renommer
    // SampleScene ou ajouter une scène de jeu ne casse rien, et le menu reste silencieux sans
    // qu'on ait à l'énumérer.
    void SyncMusicToScene()
    {
        if (FindAnyObjectByType<GameManager>() != null) StartGameplayMusic();
        else StopGameplayMusic(music ? music.fadeOutOnLeave : 1f);
    }

    void LateUpdate()
    {
        // Tous les dés d'un même jet ont été lancés pendant cette frame : DiceSfx connaît
        // maintenant leur nombre et peut choisir le bon clip.
        DiceSfx.FlushFrame();
    }

    // Lecture d'un morceau isolé (jingle, musique de menu) : coupe la playlist en cours.
    public void PlayMusic(AudioClip clip, bool loop = true)
    {
        if (!clip || !musicSource) return;
        StopPlaylist();
        if (musicSource.clip == clip && musicSource.isPlaying) return; // déjà en cours
        musicSource.clip = clip;
        musicSource.loop = loop;
        musicFade = 1f;
        ApplyMusicVolume();
        musicSource.Play();
    }

    public void StopMusic()
    {
        StopPlaylist();
        if (musicSource) musicSource.Stop();
        musicFade = 0f;
    }

    // ---- Playlist d'ambiance ---------------------------------------------------------------

    public bool IsPlaylistPlaying => playlistCo != null;

    public void StartGameplayMusic()
    {
        if (playlistCo != null) return;             // déjà en cours : on ne relance pas au milieu
        if (music == null || !music.HasTracks) return;
        playlistCo = StartCoroutine(PlaylistRoutine());
    }

    public void StopGameplayMusic(float fadeDuration)
    {
        if (playlistCo == null && !(musicSource && musicSource.isPlaying)) return;
        StopPlaylist();
        StartCoroutine(FadeOutRoutine(fadeDuration));
    }

    void StopPlaylist()
    {
        if (playlistCo != null) { StopCoroutine(playlistCo); playlistCo = null; }
    }

    IEnumerator PlaylistRoutine()
    {
        while (true)
        {
            var clip = NextTrack();
            if (!clip) { playlistCo = null; yield break; }

            musicFade = 1f;
            ApplyMusicVolume();
            musicSource.clip = clip;
            musicSource.Play();

            // On suit la source plutôt que de décompter clip.length : une piste en streaming
            // ne démarre pas forcément dans la frame du Play, et un compte à rebours lancé
            // trop tôt raccourcirait le morceau. Le délai minimal est le filet qui évite de
            // sortir de la boucle avant que la lecture ait seulement commencé.
            float startedBy = Time.unscaledTime + 0.25f;
            while (Time.unscaledTime < startedBy || musicSource.isPlaying) yield return null;

            // Respiration entre deux morceaux : le silence fait partie de l'ambiance.
            float gap = Mathf.Max(0f, music.gapBetweenTracks);
            if (gap > 0f) yield return new WaitForSecondsRealtime(gap);
        }
    }

    IEnumerator FadeOutRoutine(float duration)
    {
        float start = musicFade;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.05f, duration);
            musicFade = start * (1f - Mathf.Clamp01(t));
            ApplyMusicVolume();
            yield return null;
        }
        if (musicSource) musicSource.Stop();
        musicFade = 0f;
    }

    // Tirage sans remise : chaque piste passe une fois avant qu'aucune ne repasse. Un simple
    // Random.Range rejouerait parfois trois fois de suite la même sur trois morceaux.
    AudioClip NextTrack()
    {
        var tracks = music.gameplayTracks;
        if (tracks == null || tracks.Length == 0) return null;
        if (tracks.Length == 1) return tracks[0];

        if (!music.shuffle)
        {
            lastTrack = (lastTrack + 1) % tracks.Length;
            return tracks[lastTrack];
        }

        if (trackBag.Count == 0)
        {
            for (int i = 0; i < tracks.Length; i++) trackBag.Add(i);
            for (int i = trackBag.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (trackBag[i], trackBag[j]) = (trackBag[j], trackBag[i]);
            }
            // Un sac neuf ne doit pas rouvrir sur la piste qui vient de finir : sans ça, la
            // jointure entre deux cycles est le seul endroit où un doublon peut passer.
            if (trackBag[0] == lastTrack)
            {
                int last = trackBag.Count - 1;
                (trackBag[0], trackBag[last]) = (trackBag[last], trackBag[0]);
            }
        }

        int idx = trackBag[0];
        trackBag.RemoveAt(0);
        lastTrack = idx;
        return tracks[idx];
    }

    // Volume réel = curseur des options × dosage de la banque × fondu de sortie.
    void ApplyMusicVolume()
    {
        float baseVol = musicVolume * (music ? music.trackVolume : 1f);
        if (musicSource) musicSource.volume = Mathf.Clamp01(baseVol * musicFade);
    }

    public void PlaySfx(AudioClip clip, float volumeScale = 1f, float pitch = 1f)
    {
        if (!clip) return;

        float vol = sfxVolume * Mathf.Clamp(volumeScale, 0f, 2f);
        if (vol <= 0.0001f) return;

        var src = NextVoice();
        if (!src) { sfxSource?.PlayOneShot(clip, Mathf.Clamp01(volumeScale)); return; }

        src.clip = clip;
        src.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
        src.volume = Mathf.Clamp01(vol);
        src.Play();
    }

    // Voix libre en priorité ; sinon on recycle la plus ancienne (round-robin).
    AudioSource NextVoice()
    {
        if (voices == null || voices.Length == 0) return null;
        for (int i = 0; i < voices.Length; i++)
        {
            var s = voices[(nextVoice + i) % voices.Length];
            if (s && !s.isPlaying) { nextVoice = (nextVoice + i + 1) % voices.Length; return s; }
        }
        var forced = voices[nextVoice];
        nextVoice = (nextVoice + 1) % voices.Length;
        return forced;
    }

    public void SetMusicVolume(float v)
    {
        v = Mathf.Clamp01(v);
        musicVolume = v;
        ApplyMusicVolume(); // suit le curseur en direct, fondu en cours ou non
        PlayerPrefs.SetFloat(PREF_MUSIC_VOLUME, v);
        PlayerPrefs.Save();
    }

    public void SetSfxVolume(float v)
    {
        v = Mathf.Clamp01(v);
        sfxVolume = v;
        if (sfxSource) sfxSource.volume = v;
        // Les voix déjà en cours gardent leur volume : chacune porte son propre dosage
        // (Entry.volume) qu'on ne peut pas recalculer ici, et les SFX durent moins d'une
        // seconde — le curseur s'entend dès le son suivant.
        PlayerPrefs.SetFloat(PREF_SFX_VOLUME, v);
        PlayerPrefs.Save();
    }
}
