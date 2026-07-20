using UnityEngine;

// Gestionnaire audio global : musique + effets sonores, volumes persistés (PlayerPrefs).
// À poser sur un GameObject de la scène MainMenu ; il survit aux changements de scène
// (DontDestroyOnLoad) pour que la musique continue entre le menu et la partie.
//
// Usage depuis n'importe quel script :
//   AudioManager.Instance?.PlaySfx(clip);
//   AudioManager.Instance?.PlayMusic(clip);
// Les sliders d'options appellent SetMusicVolume / SetSfxVolume (MenuUI s'en charge).
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Sources (créées automatiquement si vides)")]
    [Tooltip("Source dédiée à la musique (loop).")]
    public AudioSource musicSource;
    [Tooltip("Source dédiée aux effets sonores (one-shot).")]
    public AudioSource sfxSource;

    [Header("Musique")]
    [Tooltip("Musique lancée automatiquement au démarrage (optionnel).")]
    public AudioClip startupMusic;

    // Clés partagées avec MenuUI (sliders d'options)
    public const string PREF_MUSIC_VOLUME = "pref_vol_music";
    public const string PREF_SFX_VOLUME = "pref_vol_sfx";

    public static float SavedMusicVolume => PlayerPrefs.GetFloat(PREF_MUSIC_VOLUME, 0.8f);
    public static float SavedSfxVolume => PlayerPrefs.GetFloat(PREF_SFX_VOLUME, 1f);

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
            musicSource.loop = true;
            musicSource.playOnAwake = false;
        }
        if (!sfxSource)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.loop = false;
            sfxSource.playOnAwake = false;
        }

        // Volumes sauvegardés appliqués dès le démarrage
        musicSource.volume = SavedMusicVolume;
        sfxSource.volume = SavedSfxVolume;

        if (startupMusic) PlayMusic(startupMusic);
    }

    public void PlayMusic(AudioClip clip, bool loop = true)
    {
        if (!clip || !musicSource) return;
        if (musicSource.clip == clip && musicSource.isPlaying) return; // déjà en cours
        musicSource.clip = clip;
        musicSource.loop = loop;
        musicSource.Play();
    }

    public void StopMusic()
    {
        if (musicSource) musicSource.Stop();
    }

    public void PlaySfx(AudioClip clip, float volumeScale = 1f)
    {
        if (!clip || !sfxSource) return;
        sfxSource.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
    }

    public void SetMusicVolume(float v)
    {
        v = Mathf.Clamp01(v);
        if (musicSource) musicSource.volume = v;
        PlayerPrefs.SetFloat(PREF_MUSIC_VOLUME, v);
        PlayerPrefs.Save();
    }

    public void SetSfxVolume(float v)
    {
        v = Mathf.Clamp01(v);
        if (sfxSource) sfxSource.volume = v;
        PlayerPrefs.SetFloat(PREF_SFX_VOLUME, v);
        PlayerPrefs.Save();
    }
}
