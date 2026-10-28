using System.Collections.Generic;
using UnityEngine;

public enum GameSfx
{
    UIHover,
    UIClick,
    PanelOpen,
    CountdownBeep,
    CountdownGo,
    Checkpoint,
    Finish,
    NewRecord,
    CargoPickup,
    CargoDrop,
    Delivered,
    CargoBroken,
    Crash,
    Warning,
    DialoguePop,
    Gunshot,
    BulletHit
}

public enum GameMusic
{
    None,
    Menu,
    Race,
    Delivery,
    Result
}

/// <summary>
/// 全域音效服務。音效檔放在 Assets/Resources/Audio/ 下並以固定檔名命名即可自動載入
/// (檔名對照見 <see cref="SfxFileName"/>，或 Documentation/ASSET_REQUESTS.md)。
/// 缺少的音效會靜音略過，並在 Console 列出一次缺檔清單，方便後續補齊。
/// </summary>
public static class GameAudio
{
    private const string ResourceFolder = "Audio/";

    private static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
    private static readonly HashSet<string> missing = new HashSet<string>();
    private static AudioSource sfxSource;
    private static AudioSource musicSource;
    private static GameMusic currentMusic = GameMusic.None;
    private static float musicVolume = 0.35f;

    public static string SfxFileName(GameSfx sfx) => sfx switch
    {
        GameSfx.UIHover => "ui_hover",
        GameSfx.UIClick => "ui_click",
        GameSfx.PanelOpen => "ui_panel_open",
        GameSfx.CountdownBeep => "countdown_beep",
        GameSfx.CountdownGo => "countdown_go",
        GameSfx.Checkpoint => "checkpoint",
        GameSfx.Finish => "finish",
        GameSfx.NewRecord => "new_record",
        GameSfx.CargoPickup => "cargo_pickup",
        GameSfx.CargoDrop => "cargo_drop",
        GameSfx.Delivered => "cargo_delivered",
        GameSfx.CargoBroken => "cargo_broken",
        GameSfx.Crash => "drone_crash",
        GameSfx.Warning => "warning_beep",
        GameSfx.DialoguePop => "ui_dialogue_pop",
        GameSfx.Gunshot => "gunshot",
        GameSfx.BulletHit => "bullet_hit",
        _ => sfx.ToString()
    };

    public static string MusicFileName(GameMusic music) => music switch
    {
        GameMusic.Menu => "bgm_menu",
        GameMusic.Race => "bgm_race",
        GameMusic.Delivery => "bgm_delivery",
        GameMusic.Result => "bgm_result",
        _ => string.Empty
    };

    public static void Play(GameSfx sfx, float volumeScale = 1f)
    {
        AudioClip clip = LoadSfx(sfx);
        if (clip == null) return;

        EnsureSources();
        sfxSource.PlayOneShot(clip, volumeScale);
    }

    /// <summary>播放指定的音效檔 (與其他音效共用同一個 2D 音源)</summary>
    public static void PlayClip(AudioClip clip, float volumeScale = 1f)
    {
        if (clip == null) return;

        EnsureSources();
        sfxSource.PlayOneShot(clip, volumeScale);
    }

    /// <summary>在世界座標播放 3D 音效 (撞擊、貨物落地等)</summary>
    public static void PlayAt(GameSfx sfx, Vector3 position, float volumeScale = 1f)
    {
        AudioClip clip = LoadSfx(sfx);
        if (clip == null) return;

        AudioSource.PlayClipAtPoint(clip, position, volumeScale);
    }

    public static void PlayMusic(GameMusic music)
    {
        if (music == currentMusic) return;
        currentMusic = music;

        EnsureSources();
        AudioClip clip = music == GameMusic.None ? null : Load(MusicFileName(music));
        if (clip == null)
        {
            musicSource.Stop();
            return;
        }

        musicSource.clip = clip;
        musicSource.volume = musicVolume;
        musicSource.Play();
    }

    public static void SetMusicVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);
        if (musicSource != null) musicSource.volume = musicVolume;
    }

    /// <summary>
    /// 優先使用 Resources/Audio 的正式音效檔；缺檔時改用程式合成的 placeholder 音效，
    /// 讓按鈕、倒數、提示等在補齊素材前也有聲音回饋。
    /// </summary>
    private static AudioClip LoadSfx(GameSfx sfx)
    {
        AudioClip clip = Load(SfxFileName(sfx));
        if (clip != null || !UseSynthFallback) return clip;

        string key = "synth_" + sfx;
        if (cache.TryGetValue(key, out AudioClip synth) && synth != null) return synth;
        synth = SynthSfx.Create(sfx);
        cache[key] = synth;
        return synth;
    }

    /// <summary>缺少音效檔時是否使用合成音效 (設為 false 則缺檔靜音)</summary>
    public static bool UseSynthFallback = true;

    private static AudioClip Load(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return null;
        if (cache.TryGetValue(fileName, out AudioClip cached)) return cached;

        AudioClip clip = Resources.Load<AudioClip>(ResourceFolder + fileName);
        cache[fileName] = clip;
        if (clip == null && missing.Add(fileName))
        {
            Debug.Log($"[GameAudio] 尚未提供音效「{fileName}」，暫用合成音效 (BGM 則靜音)。請放入 Assets/Resources/Audio/{fileName}.wav (或 .mp3/.ogg)。目前缺少：{string.Join(", ", missing)}");
        }
        return clip;
    }

    private static void EnsureSources()
    {
        if (sfxSource != null && musicSource != null) return;

        GameObject host = new GameObject("[GameAudio]");
        Object.DontDestroyOnLoad(host);

        sfxSource = host.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.spatialBlend = 0f;
        sfxSource.ignoreListenerPause = true;

        musicSource = host.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.loop = true;
        musicSource.spatialBlend = 0f;
        musicSource.volume = musicVolume;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        cache.Clear();
        missing.Clear();
        sfxSource = null;
        musicSource = null;
        currentMusic = GameMusic.None;
    }
}
