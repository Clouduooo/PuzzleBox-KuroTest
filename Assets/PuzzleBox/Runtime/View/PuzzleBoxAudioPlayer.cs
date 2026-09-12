using UnityEngine;

namespace PuzzleBox.View
{
    /// <summary>One persistent music player. Scene previews never create it or emit sound.</summary>
    [DisallowMultipleComponent]
    public sealed class PuzzleBoxAudioPlayer : MonoBehaviour
    {
        private static PuzzleBoxAudioPlayer instance;
        [SerializeField] private PuzzleBoxAudioSettings settings;
        private AudioSource musicSource;
        private AudioSource catSource;
        private AudioSource boxSource;
        private int catVariation;
        private int boxVariation;
        public PuzzleBoxAudioSettings Settings => settings;
        public AudioSource MusicSource => musicSource;
        public int CatCueCount { get; private set; }
        public int BoxCueCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { instance = null; }

        public static PuzzleBoxAudioPlayer Ensure(PuzzleBoxAudioSettings configuration = null)
        {
            if (!Application.isPlaying) return null;
            if (instance == null) instance = FindObjectOfType<PuzzleBoxAudioPlayer>();
            if (instance == null)
            {
                var root = new GameObject("Puzzle Box Audio");
                instance = root.AddComponent<PuzzleBoxAudioPlayer>();
                DontDestroyOnLoad(root);
            }
            instance.Configure(configuration != null ? configuration : Resources.Load<PuzzleBoxAudioSettings>(PuzzleBoxAudioSettings.DefaultResource));
            return instance;
        }

        public void Configure(PuzzleBoxAudioSettings configuration)
        {
            settings = configuration;
            if (musicSource == null)
            {
                musicSource = AddSource("BGM · looping", true);
                catSource = AddSource("Cat · soft paws", false);
                boxSource = AddSource("Parcel · landing", false);
            }
            ApplySettings();
        }

        private AudioSource AddSource(string label, bool loop)
        {
            var child = new GameObject(label);
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f; // Independent of zoom, camera quarter and illusory depth.
            source.dopplerLevel = 0f;
            source.priority = loop ? 128 : 80;
            return source;
        }

        private void Update() { ApplySettings(); }

        private void ApplySettings()
        {
            if (musicSource == null) return;
            var clip = settings != null ? settings.music : null;
            if (musicSource.clip != clip)
            {
                musicSource.Stop();
                musicSource.clip = clip;
                if (clip != null && Application.isPlaying) musicSource.Play();
            }
            musicSource.volume = settings != null ? settings.musicVolume : 0f;
            catSource.volume = boxSource.volume = settings != null ? settings.effectsVolume : 0f;
        }

        public void PlayCatStep()
        {
            if (settings == null || catSource == null || !Application.isPlaying) return;
            var clip = NextClip(settings.catSteps, ref catVariation);
            if (clip == null) return;
            catSource.pitch = 1.08f + (catVariation % 3 - 1) * .035f;
            catSource.PlayOneShot(clip, settings.catVolume);
            CatCueCount++;
        }

        public void PlayBoxLanding(float dropCells)
        {
            if (settings == null || boxSource == null || dropCells <= .001f || !Application.isPlaying) return;
            var clip = NextClip(settings.boxLandings, ref boxVariation);
            if (clip == null) return;
            boxSource.pitch = .92f + (boxVariation % 3 - 1) * .025f;
            // A high drop has a little more weight, never a deafening stack of one-shots.
            var weight = Mathf.Lerp(.72f, 1f, Mathf.Clamp01(dropCells / 4f));
            boxSource.PlayOneShot(clip, settings.boxVolume * weight);
            BoxCueCount++;
        }

        private static AudioClip NextClip(AudioClip[] clips, ref int cursor)
        {
            if (clips == null || clips.Length == 0) return null;
            for (var i = 0; i < clips.Length; i++)
            {
                var clip = clips[cursor % clips.Length];
                cursor = (cursor + 1) % clips.Length;
                if (clip != null) return clip;
            }
            return null;
        }

        private void OnDestroy() { if (instance == this) instance = null; }
    }
}
