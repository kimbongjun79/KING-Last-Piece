using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Leaderboards;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Online ranking of the endless mode on Unity Gaming Services (anonymous sign-in + Leaderboards).
    /// Ranked by round, then by the shorter time: both are packed into one score (higher is better),
    /// <c>round × 100000 + (99999 − seconds)</c>. The leaderboard (id <see cref="LeaderboardId"/>) must exist in the
    /// project's UGS dashboard, sorted high to low, keeping each player's best. Without a linked project or a
    /// connection everything here just reports itself unavailable — the game never waits on it.
    /// </summary>
    public static class OnlineBoard
    {
        public const string LeaderboardId = "endless_best";
        const string NameKey = "KLP.Nickname";

        public enum Status { Idle, Loading, Ready, Unavailable }

        public struct Entry
        {
            public int rank;        // 1-based
            public string name;
            public int round;
            public float time;
            public bool mine;
        }

        public static Status State { get; private set; } = Status.Idle;
        public static readonly List<Entry> Top = new List<Entry>();
        public static Entry? Mine { get; private set; }
        public static string Error { get; private set; }
        static float loadedAt = -100f;

        /// <summary>The nickname entered last time (offered again on the next results screen).</summary>
        public static string SavedName
        {
            get => PlayerPrefs.GetString(NameKey, "");
            set { PlayerPrefs.SetString(NameKey, value); PlayerPrefs.Save(); }
        }

        public static long Pack(int round, float seconds) => round * 100000L + (99999L - Mathf.Clamp(Mathf.RoundToInt(seconds), 0, 99999));
        public static int RoundOf(double score) => (int)(score / 100000d);
        public static float TimeOf(double score) => 99999f - (float)(score % 100000d);

        /// <summary>UGS names come back as "name#1234": the tag is dropped for display.</summary>
        static string Clean(string playerName)
        {
            if (string.IsNullOrEmpty(playerName)) return "???";
            int hash = playerName.LastIndexOf('#');
            return hash > 0 ? playerName.Substring(0, hash) : playerName;
        }

        /// <summary>A nickname UGS accepts: trimmed, no spaces, at most 12 characters.</summary>
        public static string Sanitize(string name)
        {
            name = (name ?? "").Trim().Replace(' ', '_');
            return name.Length > 12 ? name.Substring(0, 12) : name;
        }

        static async Task<bool> Ready()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
            return AuthenticationService.Instance.IsSignedIn;
        }

        /// <summary>Loads the top 10 and this player's own entry (at most every 20 s unless forced).</summary>
        public static async void Refresh(bool force = false)
        {
            if (State == Status.Loading || (!force && Time.unscaledTime - loadedAt < 20f && State == Status.Ready)) return;
            State = Status.Loading;
            try
            {
                if (!await Ready()) throw new System.Exception("sign-in failed");
                var page = await LeaderboardsService.Instance.GetScoresAsync(LeaderboardId, new GetScoresOptions { Limit = 10 });
                string me = AuthenticationService.Instance.PlayerId;
                Top.Clear();
                foreach (var e in page.Results)
                    Top.Add(new Entry { rank = e.Rank + 1, name = Clean(e.PlayerName), round = RoundOf(e.Score), time = TimeOf(e.Score), mine = e.PlayerId == me });
                Mine = null;
                try
                {
                    var mine = await LeaderboardsService.Instance.GetPlayerScoreAsync(LeaderboardId);
                    Mine = new Entry { rank = mine.Rank + 1, name = Clean(mine.PlayerName), round = RoundOf(mine.Score), time = TimeOf(mine.Score), mine = true };
                }
                catch (System.Exception) { /* no score yet */ }
                loadedAt = Time.unscaledTime;
                State = Status.Ready;
                Error = null;
            }
            catch (System.Exception ex)
            {
                Error = ex.Message;
                State = Status.Unavailable;
                Debug.LogWarning($"[OnlineBoard] {ex.Message}");
            }
        }

        /// <summary>Sends a finished run under <paramref name="nickname"/>; true when it reached the server.</summary>
        public static async Task<bool> Submit(string nickname, int round, float seconds, int kills, float damage)
        {
            try
            {
                if (!await Ready()) return false;
                nickname = Sanitize(nickname);
                if (nickname.Length > 0 && Clean(AuthenticationService.Instance.PlayerName) != nickname)
                    await AuthenticationService.Instance.UpdatePlayerNameAsync(nickname);
                await LeaderboardsService.Instance.AddPlayerScoreAsync(LeaderboardId, Pack(round, seconds),
                    new AddPlayerScoreOptions { Metadata = new Dictionary<string, object> { { "round", round }, { "time", Mathf.RoundToInt(seconds) }, { "kills", kills }, { "damage", Mathf.RoundToInt(damage) } } });
                loadedAt = -100f;   // the title reloads the ranking
                return true;
            }
            catch (System.Exception ex)
            {
                Error = ex.Message;
                Debug.LogWarning($"[OnlineBoard] submit: {ex.Message}");
                return false;
            }
        }
    }
}
