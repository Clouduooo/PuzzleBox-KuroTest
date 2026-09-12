using UnityEngine;

namespace PuzzleBox.View
{
    [CreateAssetMenu(menuName = "Puzzle Box/Audio Settings", fileName = "AudioSettings")]
    public sealed class PuzzleBoxAudioSettings : ScriptableObject
    {
        public const string DefaultResource = "PuzzleBox/AudioSettings";
        [Header("Background music")]
        public AudioClip music;
        [Range(0f, 1f)] public float musicVolume = .18f;
        [Header("Soft cat footsteps (alternate variations)")]
        public AudioClip[] catSteps;
        [Range(0f, 1f)] public float catVolume = .55f;
        [Header("Parcel landing (once per falling stack)")]
        public AudioClip[] boxLandings;
        [Range(0f, 1f)] public float boxVolume = .65f;
        [Header("Overall effects volume")]
        [Range(0f, 1f)] public float effectsVolume = .8f;
    }
}
