using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// The endless mode's best run on this machine (PlayerPrefs): the highest round reached, then the shorter time.
    /// Saved when a run ends; shown on the title and flagged on the results screen when beaten.
    /// </summary>
    public static class BestRecord
    {
        const string Key = "KLP.Endless.Best.";

        public static bool Exists => PlayerPrefs.GetInt(Key + "Round", 0) > 0;
        public static int Round => PlayerPrefs.GetInt(Key + "Round", 0);
        public static float Time => PlayerPrefs.GetFloat(Key + "Time", 0f);
        public static float Damage => PlayerPrefs.GetFloat(Key + "Damage", 0f);
        public static int Kills => PlayerPrefs.GetInt(Key + "Kills", 0);
        public static string Date => PlayerPrefs.GetString(Key + "Date", "");

        /// <summary>Records a finished run; true when it is the new best.</summary>
        public static bool Submit(int round, float time, float damage, int kills)
        {
            bool better = !Exists || round > Round || (round == Round && time < Time);
            if (!better) return false;
            PlayerPrefs.SetInt(Key + "Round", round);
            PlayerPrefs.SetFloat(Key + "Time", time);
            PlayerPrefs.SetFloat(Key + "Damage", damage);
            PlayerPrefs.SetInt(Key + "Kills", kills);
            PlayerPrefs.SetString(Key + "Date", System.DateTime.Now.ToString("yyyy.MM.dd"));
            PlayerPrefs.Save();
            return true;
        }

        public static string FormatTime(float seconds) => $"{Mathf.FloorToInt(seconds / 60f):00}:{Mathf.FloorToInt(seconds % 60f):00}";
    }
}
