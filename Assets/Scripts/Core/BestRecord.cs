using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// The 무쌍 mode's best run on this machine (PlayerPrefs): the longest time survived, then the higher level.
    /// Saved when a run ends; shown on the title and flagged on the results screen when beaten.
    /// </summary>
    public static class BestRecord
    {
        const string Key = "KLP.Musou.Best.";

        public static bool Exists => PlayerPrefs.GetFloat(Key + "Time", 0f) > 0f;
        public static float Time => PlayerPrefs.GetFloat(Key + "Time", 0f);
        public static int Level => PlayerPrefs.GetInt(Key + "Level", 0);
        public static float Damage => PlayerPrefs.GetFloat(Key + "Damage", 0f);
        public static int Kills => PlayerPrefs.GetInt(Key + "Kills", 0);
        public static string Date => PlayerPrefs.GetString(Key + "Date", "");

        /// <summary>Records a finished run; true when it is the new best.</summary>
        public static bool Submit(float survived, int level, float damage, int kills)
        {
            // Longer survived, then more pieces taken (as the online ranking).
            bool better = !Exists || survived > Time + 0.5f || (Mathf.Abs(survived - Time) <= 0.5f && kills > Kills);
            if (!better) return false;
            PlayerPrefs.SetFloat(Key + "Time", survived);
            PlayerPrefs.SetInt(Key + "Level", level);
            PlayerPrefs.SetFloat(Key + "Damage", damage);
            PlayerPrefs.SetInt(Key + "Kills", kills);
            PlayerPrefs.SetString(Key + "Date", System.DateTime.Now.ToString("yyyy.MM.dd"));
            PlayerPrefs.Save();
            return true;
        }

        public static string FormatTime(float seconds) => $"{Mathf.FloorToInt(seconds / 60f):00}:{Mathf.FloorToInt(seconds % 60f):00}";
    }

    /// <summary>
    /// The normal mode's best clear on this machine (PlayerPrefs): the fewest deaths, then the shorter time. Saved when the
    /// ten rounds are won; shown on the title and flagged on the victory screen when beaten.
    /// </summary>
    public static class NormalRecord
    {
        const string Key = "KLP.Normal.Best.";

        public static bool Exists => PlayerPrefs.GetInt(Key + "Cleared", 0) > 0;
        public static int Deaths => PlayerPrefs.GetInt(Key + "Deaths", 0);
        public static float Time => PlayerPrefs.GetFloat(Key + "Time", 0f);
        public static string Date => PlayerPrefs.GetString(Key + "Date", "");

        /// <summary>Records a clear; true when it is the new best.</summary>
        public static bool Submit(int deaths, float time)
        {
            bool better = !Exists || deaths < Deaths || (deaths == Deaths && time < Time);
            if (!better) return false;
            PlayerPrefs.SetInt(Key + "Cleared", 1);
            PlayerPrefs.SetInt(Key + "Deaths", deaths);
            PlayerPrefs.SetFloat(Key + "Time", time);
            PlayerPrefs.SetString(Key + "Date", System.DateTime.Now.ToString("yyyy.MM.dd"));
            PlayerPrefs.Save();
            return true;
        }
    }
}
