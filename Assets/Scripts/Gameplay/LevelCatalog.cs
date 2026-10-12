using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum GameModeType
{
    Race,     // 競速：依序穿越檢查點，比時間
    Delivery  // 搬運：取貨並送到指定地點，比分數
}

/// <summary>
/// 一個可遊玩的關卡設定。同一個場景可以用不同參數 (城市種子、規模、檢查點數) 組成多個關卡。
/// </summary>
public class LevelDefinition
{
    public string Id;
    public string DisplayName;
    public string Description;
    public string SceneName;
    public GameModeType[] SupportedModes;

    // 程序化城市參數 (僅 CityProcedural 場景使用)
    public int CitySeed;
    public int CityBlocks = 5;
    public float BuildingDensity = 0.85f;
    public float MaxBuildingHeight = 40f;

    // 競速參數
    public int CheckpointCount = 8;
    public float GoldTime = 60f;
    public float SilverTime = 90f;

    // 搬運參數
    public int DeliveryOrders = 3;
    public float DeliveryTimeLimit = 300f;

    public bool Supports(GameModeType mode)
    {
        foreach (GameModeType supported in SupportedModes)
        {
            if (supported == mode) return true;
        }
        return false;
    }

    public string LeaderboardKey(GameModeType mode) => $"{Id}_{mode}";
}

/// <summary>
/// 關卡目錄與目前選擇 (跨場景保存)。
/// 新增關卡時在 <see cref="All"/> 加一筆，並確認場景已加入 Build Settings。
/// </summary>
public static class LevelCatalog
{
    public const string ProceduralCityScene = "CityProcedural";
    public const string DroneSelectScene = "PlayerSelectSettings";

    public static readonly List<LevelDefinition> All = new List<LevelDefinition>
    {
        new LevelDefinition
        {
            Id = "training_field",
            DisplayName = "訓練場",
            Description = "開闊草地與樹林，熟悉操控與穿圈。",
            SceneName = "SampleScene",
            SupportedModes = new[] { GameModeType.Race },
            CheckpointCount = 7,
            GoldTime = 45f,
            SilverTime = 70f
        },
        new LevelDefinition
        {
            Id = "city_classic",
            DisplayName = "城市賽道",
            Description = "手工搭建的城市與曲線賽道。",
            SceneName = "City2",
            SupportedModes = new[] { GameModeType.Race },
            CheckpointCount = 15,
            GoldTime = 90f,
            SilverTime = 130f
        },
        new LevelDefinition
        {
            Id = "city_suburb",
            DisplayName = "新城區・住宅區",
            Description = "程序生成的低密度住宅區，建築較矮、街道寬闊。",
            SceneName = ProceduralCityScene,
            SupportedModes = new[] { GameModeType.Race, GameModeType.Delivery },
            CitySeed = 1207,
            CityBlocks = 4,
            BuildingDensity = 0.7f,
            MaxBuildingHeight = 25f,
            CheckpointCount = 8,
            GoldTime = 60f,
            SilverTime = 90f,
            DeliveryOrders = 3,
            DeliveryTimeLimit = 300f
        },
        new LevelDefinition
        {
            Id = "city_downtown",
            DisplayName = "新城區・市中心",
            Description = "程序生成的高樓密集市中心，穿梭大樓之間。",
            SceneName = ProceduralCityScene,
            SupportedModes = new[] { GameModeType.Race, GameModeType.Delivery },
            CitySeed = 4242,
            CityBlocks = 6,
            BuildingDensity = 0.95f,
            MaxBuildingHeight = 60f,
            CheckpointCount = 12,
            GoldTime = 85f,
            SilverTime = 120f,
            DeliveryOrders = 5,
            DeliveryTimeLimit = 420f
        }
    };

    /// <summary>目前選擇的遊戲模式 (預設競速，讓直接開啟關卡場景時維持原本玩法)</summary>
    public static GameModeType SelectedMode { get; set; } = GameModeType.Race;

    /// <summary>目前選擇的關卡；直接從編輯器開啟場景時為 null，會依場景名稱自動推斷</summary>
    public static LevelDefinition SelectedLevel { get; set; }

    public static bool HasExplicitSelection => SelectedLevel != null;

    /// <summary>取得目前場景對應的關卡設定 (找不到時回傳該場景的第一個設定或 null)</summary>
    public static LevelDefinition CurrentLevel
    {
        get
        {
            string sceneName = SceneManager.GetActiveScene().name;
            if (SelectedLevel != null && SelectedLevel.SceneName == sceneName)
            {
                return SelectedLevel;
            }

            foreach (LevelDefinition level in All)
            {
                if (level.SceneName == sceneName) return level;
            }
            return null;
        }
    }

    public static List<LevelDefinition> LevelsFor(GameModeType mode)
    {
        List<LevelDefinition> result = new List<LevelDefinition>();
        foreach (LevelDefinition level in All)
        {
            if (level.Supports(mode)) result.Add(level);
        }
        return result;
    }

    public static bool IsSceneInBuild(string sceneName)
    {
        return Application.CanStreamedLevelBeLoaded(sceneName);
    }

    public static LevelDefinition NextLevel(LevelDefinition current, GameModeType mode)
    {
        List<LevelDefinition> levels = LevelsFor(mode);
        int index = levels.IndexOf(current);
        for (int i = index + 1; i < levels.Count; i++)
        {
            if (IsSceneInBuild(levels[i].SceneName)) return levels[i];
        }
        return null;
    }

    public static void Load(LevelDefinition level, GameModeType mode)
    {
        if (level == null) return;

        SelectedLevel = level;
        SelectedMode = mode;
        SceneLoader.Load(level.SceneName);
    }

    public static string ModeDisplayName(GameModeType mode) => mode == GameModeType.Race ? "競速" : "搬運";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        SelectedMode = GameModeType.Race;
        SelectedLevel = null;
    }
}
