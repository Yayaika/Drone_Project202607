using System.Collections.Generic;

public enum Medal
{
    None,
    Bronze,
    Silver,
    Gold
}

/// <summary>關卡結算資料 (競速與搬運共用)</summary>
public class LevelResult
{
    public GameModeType Mode;
    public bool Success;
    public string Title;
    public float Time;
    public int Score;
    public Medal Medal;
    /// <summary>排行榜名次 (1 起算)，未進榜或未提交為 -1</summary>
    public int Rank = -1;
    public LeaderboardEntry SubmittedEntry;
    /// <summary>結算明細 (每行一項，例如「準時送達 +500」)</summary>
    public readonly List<string> Details = new List<string>();
}
