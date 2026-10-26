using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Leaderboards;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Online rankings on Unity Gaming Services (anonymous sign-in + Leaderboards), one per game mode:
    /// <list type="bullet">
    /// <item><see cref="Musou"/> ("endless_best"): ranked by the time survived, then by the pieces taken —
    /// <c>10^11 + seconds × 100000 + kills</c>.</item>
    /// <item><see cref="Normal"/> ("normal_best"): ranked by the fewest deaths, then by the shorter clear time —
    /// <c>(999 − deaths) × 100000 + (99999 − seconds)</c>.</item>
    /// </list>
    /// Both scores are higher-is-better: each leaderboard must exist in the project's UGS dashboard sorted high to low,
    /// keeping each player's best. Without a linked project or a connection everything here just reports itself
    /// unavailable — the game never waits on it.
    /// </summary>
    public class OnlineBoard
    {
        public static readonly OnlineBoard Musou = new OnlineBoard("endless_best",
            (kills, seconds) => MusouBase + Mathf.Clamp(Mathf.FloorToInt(seconds), 0, 99999) * 100000L + Mathf.Clamp(kills, 0, 99999),
            score => score >= MusouBase ? (int)((score - MusouBase) % 100000d) : -1,
            score => score >= MusouBase ? (float)System.Math.Floor((score - MusouBase) / 100000d) : (float)System.Math.Floor(score / 1000d));

        /// <summary>
        /// 무쌍 scores carry this offset (time × 100000 + kills on top of it), so they rank above every score of the old
        /// time × 1000 + level packing still on the board; those still read their time, their kills unknown (−1).
        /// </summary>
        const long MusouBase = 100000000000L;

        public static readonly OnlineBoard Normal = new OnlineBoard("normal_best",
            (deaths, seconds) => (999L - Mathf.Clamp(deaths, 0, 999)) * 100000L + (99999L - Mathf.Clamp(Mathf.RoundToInt(seconds), 0, 99999)),
            score => 999 - (int)(score / 100000d), score => 99999f - (float)(score % 100000d));

        /// <summary>The ranking of a mode: the 무쌍 mode's, or the normal mode's.</summary>
        public static OnlineBoard For(bool musou) => musou ? Musou : Normal;

        const string NameKey = "KLP.Nickname";

        public enum Status { Idle, Loading, Ready, Unavailable }

        public struct Entry
        {
            public int rank;        // 1-based
            public string name;
            /// <summary>Pieces taken (무쌍; −1 for a score from before kills were ranked) or deaths (normal).</summary>
            public int value;
            public float time;
            public bool mine;
        }

        public readonly string LeaderboardId;
        readonly System.Func<int, float, long> pack;
        readonly System.Func<double, int> valueOf;
        readonly System.Func<double, float> timeOf;

        public Status State { get; private set; } = Status.Idle;
        public readonly List<Entry> Top = new List<Entry>();
        public Entry? Mine { get; private set; }
        public string Error { get; private set; }
        float loadedAt = -100f;

        OnlineBoard(string id, System.Func<int, float, long> pack, System.Func<double, int> valueOf, System.Func<double, float> timeOf)
        {
            LeaderboardId = id;
            this.pack = pack;
            this.valueOf = valueOf;
            this.timeOf = timeOf;
        }

        /// <summary>The nickname entered last time (offered again on the next results screen).</summary>
        public static string SavedName
        {
            get => PlayerPrefs.GetString(NameKey, "");
            set { PlayerPrefs.SetString(NameKey, value); PlayerPrefs.Save(); }
        }

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

        Entry ToEntry(Unity.Services.Leaderboards.Models.LeaderboardEntry e, string me) => new Entry
        {
            rank = e.Rank + 1, name = Clean(e.PlayerName), value = valueOf(e.Score), time = timeOf(e.Score), mine = e.PlayerId == me,
        };

        /// <summary>Loads the top 10 and this player's own entry (at most every 20 s unless forced).</summary>
        public async void Refresh(bool force = false)
        {
            if (State == Status.Loading || (!force && Time.unscaledTime - loadedAt < 20f && State == Status.Ready)) return;
            State = Status.Loading;
            try
            {
                if (!await Ready()) throw new System.Exception("sign-in failed");
                var page = await LeaderboardsService.Instance.GetScoresAsync(LeaderboardId, new GetScoresOptions { Limit = 10 });
                string me = AuthenticationService.Instance.PlayerId;
                Top.Clear();
                foreach (var e in page.Results) Top.Add(ToEntry(e, me));
                Mine = null;
                try
                {
                    var mine = await LeaderboardsService.Instance.GetPlayerScoreAsync(LeaderboardId);
                    var entry = ToEntry(mine, me);
                    entry.mine = true;
                    Mine = entry;
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
                Debug.LogWarning($"[OnlineBoard] {LeaderboardId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends a finished run under <paramref name="nickname"/>: <paramref name="value"/> is the level reached (무쌍) or
        /// the deaths (normal). True when it reached the server.
        /// </summary>
        public async Task<bool> Submit(string nickname, int value, float seconds, Dictionary<string, object> metadata)
        {
            try
            {
                if (!await Ready()) return false;
                nickname = Sanitize(nickname);
                if (nickname.Length > 0 && Clean(AuthenticationService.Instance.PlayerName) != nickname)
                    await AuthenticationService.Instance.UpdatePlayerNameAsync(nickname);
                await LeaderboardsService.Instance.AddPlayerScoreAsync(LeaderboardId, pack(value, seconds),
                    new AddPlayerScoreOptions { Metadata = metadata });
                loadedAt = -100f;   // the title reloads the ranking
                return true;
            }
            catch (System.Exception ex)
            {
                Error = ex.Message;
                Debug.LogWarning($"[OnlineBoard] {LeaderboardId} submit: {ex.Message}");
                return false;
            }
        }
    }
}
