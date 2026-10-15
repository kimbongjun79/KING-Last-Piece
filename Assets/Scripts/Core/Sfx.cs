using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Sound event hub. Gameplay code calls <see cref="Play(string, Vector3)"/> at every moment a sound belongs;
    /// ids match Docs/Sound/sounds.json. There is no audio playback yet — listeners subscribe to <see cref="Played"/>
    /// (the future sound player, and the sound-reference capture in the labs).
    /// </summary>
    public static class Sfx
    {
        /// <summary>id, world position (Vector3.zero with <c>is2D</c> = true for UI / global sounds).</summary>
        public static event System.Action<string, Vector3, bool> Played;

        /// <summary>3D sound at a world position.</summary>
        public static void Play(string id, Vector3 position)
        {
            if (!string.IsNullOrEmpty(id)) Played?.Invoke(id, position, false);
        }

        /// <summary>2D sound (UI, music, global stingers).</summary>
        public static void Play(string id)
        {
            if (!string.IsNullOrEmpty(id)) Played?.Invoke(id, Vector3.zero, true);
        }
    }
}
