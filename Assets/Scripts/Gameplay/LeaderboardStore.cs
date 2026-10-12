using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class LeaderboardEntry
{
    public string pilot;
    public float time;   // 完成時間 (秒)
    public int score;    // 分數 (搬運模式)
    public string date;
}

/// <summary>
/// 本機排行榜 (PlayerPrefs + JSON)。競速依時間由短到長、搬運依分數由高到低，各保留前 10 名。
/// </summary>
public static class Leaderboard
{
    public const int MaxEntries = 10;
    private const string KeyPrefix = "Leaderboard_";

    [Serializable]
    private class EntryList
    {
        public List<LeaderboardEntry> entries = new List<LeaderboardEntry>();
    }

    public static List<LeaderboardEntry> Get(string boardKey)
    {
        string json = PlayerPrefs.GetString(KeyPrefix + boardKey, string.Empty);
        if (string.IsNullOrEmpty(json)) return new List<LeaderboardEntry>();

        try
        {
            EntryList list = JsonUtility.FromJson<EntryList>(json);
            return list?.entries ?? new List<LeaderboardEntry>();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Leaderboard] 排行榜資料損毀，已重置：{exception.Message}");
            return new List<LeaderboardEntry>();
        }
    }

    /// <summary>提交成績，回傳名次 (1 起算)；未進榜回傳 -1</summary>
    public static int Submit(string boardKey, GameModeType mode, LeaderboardEntry entry)
    {
        entry.date = DateTime.Now.ToString("yyyy/MM/dd HH:mm");
        List<LeaderboardEntry> entries = Get(boardKey);
        entries.Add(entry);
        Sort(entries, mode);

        int rank = entries.IndexOf(entry) + 1;
        if (entries.Count > MaxEntries)
        {
            entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
        }

        EntryList list = new EntryList { entries = entries };
        PlayerPrefs.SetString(KeyPrefix + boardKey, JsonUtility.ToJson(list));
        PlayerPrefs.Save();
        return rank <= MaxEntries ? rank : -1;
    }

    public static void Sort(List<LeaderboardEntry> entries, GameModeType mode)
    {
        if (mode == GameModeType.Race)
        {
            entries.Sort((a, b) => a.time.CompareTo(b.time));
        }
        else
        {
            entries.Sort((a, b) =>
            {
                int byScore = b.score.CompareTo(a.score);
                return byScore != 0 ? byScore : a.time.CompareTo(b.time);
            });
        }
    }
}
