using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum WeatherType
{
    Clear,  // 晴
    Cloudy, // 多雲
    Rain,   // 下雨
    Storm,  // 暴風雨
    Fog     // 濃霧
}

/// <summary>
/// 晝夜與天氣系統：自動加入遊戲關卡 (有 GameManager 的場景；選單場景不會加入)。
/// - 太陽隨時間移動，光色/環境光/霧色/天空盒曝光跟著變化，夜晚改為月光與夜空，並開啟無人機前燈。
/// - 天氣 (晴/多雲/雨/暴風雨/濃霧) 定時隨機轉換並平滑過渡；雨天有雨滴、暴風雨有閃電與陣風。
/// - <see cref="Wind"/> 為目前風力加速度，由 DroneController1 套用。
/// - F6：時間快轉 3 小時；F7：切換下一種天氣。
/// 只修改執行時的副本 (天空盒材質實例、RenderSettings)，不改動任何資產。
/// </summary>
public class EnvironmentController : MonoBehaviour
{
    public static EnvironmentController Instance { get; private set; }

    /// <summary>目前風力 (m/s² 加速度)，晴天為零</summary>
    public static Vector3 Wind => Instance != null ? Instance.wind : Vector3.zero;

    public float Hour => hour;
    public WeatherType Weather => targetWeather;
    public bool IsNight => daylight < 0.15f;

    private struct WeatherLook
    {
        public float Cloud;      // 0~1：減弱陽光、天空變灰
        public float FogStart;
        public float FogEnd;
        public float Rain;       // 0~1：雨量
        public float Wind;       // 平均風力 (m/s²)
        public float Gust;       // 陣風幅度
        public float Lightning;  // 0/1
    }

    private static readonly WeatherLook[] Looks =
    {
        new WeatherLook { Cloud = 0f,    FogStart = 160f, FogEnd = 750f, Rain = 0f,   Wind = 0f,   Gust = 0.2f },
        new WeatherLook { Cloud = 0.55f, FogStart = 110f, FogEnd = 480f, Rain = 0f,   Wind = 0.4f, Gust = 0.5f },
        new WeatherLook { Cloud = 0.8f,  FogStart = 50f,  FogEnd = 280f, Rain = 0.6f, Wind = 0.9f, Gust = 0.9f },
        new WeatherLook { Cloud = 0.95f, FogStart = 30f,  FogEnd = 190f, Rain = 1f,   Wind = 1.5f, Gust = 1.4f, Lightning = 1f },
        new WeatherLook { Cloud = 0.6f,  FogStart = 6f,   FogEnd = 85f,  Rain = 0f,   Wind = 0.1f, Gust = 0.1f }
    };

    private static readonly string[] WeatherNames = { "晴朗", "多雲", "下雨", "暴風雨", "濃霧" };

    private EnvironmentProfile profile;
    private Light sun;
    private float baseSunIntensity = 1.2f;
    private Material dayMaterial;
    /// <summary>天空盒畫有太陽時白天的平行光旋轉 (對準畫中太陽)；沒有對照時為 null，太陽依時間移動</summary>
    private Quaternion? skyboxSunRotation;
    private Material nightMaterial;
    private float hour;
    private float daylight;
    private WeatherType currentWeather;
    private WeatherType targetWeather;
    private float weatherBlend = 1f;
    private float nextWeatherChange;
    private WeatherLook look;
    private Vector3 wind;
    private float lightningFlash;
    private float nextLightning;
    private float noiseSeed;

    private ParticleSystem rain;
    private Light headlight;
    private Canvas toastCanvas;
    private TextMeshProUGUI toastText;
    private float toastTimer;
    private InputAction timeSkipAction;
    private InputAction weatherAction;

    #region 自動加入關卡

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryCreate(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single) TryCreate(scene);
    }

    private static void TryCreate(Scene scene)
    {
        if (Instance != null && Instance.gameObject.scene == scene) return;
        // 只在遊戲關卡啟用 (選單場景有自己的天空)
        if (FindFirstObjectByType<GameManager>() == null) return;
        if (FindFirstObjectByType<EnvironmentController>() != null) return;
        // 室內關卡 (工廠) 不跑晝夜與天氣：不會在廠房內下雨或變成黑夜
        if (FindFirstObjectByType<IndoorLighting>() != null) return;
        new GameObject("[EnvironmentController]").AddComponent<EnvironmentController>();
    }

    #endregion

    private void Awake()
    {
        Instance = this;
        profile = Resources.Load<EnvironmentProfile>("EnvironmentProfile");
        if (profile == null) profile = ScriptableObject.CreateInstance<EnvironmentProfile>();
        noiseSeed = Random.Range(0f, 100f);

        sun = RenderSettings.sun;
        if (sun == null)
        {
            foreach (Light candidate in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (candidate.type == LightType.Directional) { sun = candidate; break; }
            }
        }
        if (sun != null) baseSunIntensity = Mathf.Max(0.8f, sun.intensity);

        // 天空盒使用材質副本，避免改到資產
        if (RenderSettings.skybox != null)
        {
            dayMaterial = new Material(RenderSettings.skybox);
            foreach (EnvironmentProfile.SkyboxSun entry in profile.skyboxSuns)
            {
                if (entry.skybox != RenderSettings.skybox) continue;
                // 天空盒的 _Rotation 會把整張天空繞 Y 軸轉動，太陽方位跟著轉
                float rotation = entry.skybox.HasProperty("_Rotation") ? entry.skybox.GetFloat("_Rotation") : 0f;
                skyboxSunRotation = Quaternion.Euler(entry.elevation, entry.yaw - rotation + 180f, 0f);
                break;
            }
        }
        if (profile.nightSkybox != null) nightMaterial = new Material(profile.nightSkybox);
        if (dayMaterial != null) RenderSettings.skybox = dayMaterial;

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;

        hour = profile.startHour >= 0f ? profile.startHour : Random.Range(6f, 20f);
        currentWeather = targetWeather = WeatherType.Clear;
        look = Looks[0];
        ScheduleNextWeather();

        timeSkipAction = new InputAction("TimeSkip", binding: "<Keyboard>/f6");
        weatherAction = new InputAction("NextWeather", binding: "<Keyboard>/f7");
        timeSkipAction.Enable();
        weatherAction.Enable();

        BuildRain();
        BuildToast();
    }

    private void OnDestroy()
    {
        timeSkipAction?.Dispose();
        weatherAction?.Dispose();
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        if (timeSkipAction.WasPressedThisFrame())
        {
            hour = Mathf.Repeat(hour + 3f, 24f);
            ShowToast($"時間 {FormatHour(hour)}");
        }
        if (weatherAction.WasPressedThisFrame())
        {
            SetWeather((WeatherType)(((int)targetWeather + 1) % Looks.Length));
        }

        hour = Mathf.Repeat(hour + dt * 24f / (profile.dayLengthMinutes * 60f), 24f);
        if (Time.time >= nextWeatherChange) SetWeather(PickWeather());

        // 天氣平滑過渡
        if (weatherBlend < 1f)
        {
            weatherBlend = Mathf.MoveTowards(weatherBlend, 1f, dt / Mathf.Max(1f, profile.weatherBlendSeconds));
            look = Lerp(Looks[(int)currentWeather], Looks[(int)targetWeather], Mathf.SmoothStep(0f, 1f, weatherBlend));
            if (weatherBlend >= 1f) currentWeather = targetWeather;
        }

        UpdateLightning(dt);
        UpdateSunAndSky();
        UpdateWind();
        UpdateRain();
        UpdateHeadlight();
        UpdateToast(dt);
    }

    #region 天氣切換

    /// <summary>設定時間 (0~24 時)</summary>
    public void SetTime(float newHour)
    {
        hour = Mathf.Repeat(newHour, 24f);
    }

    /// <summary>立即切換天氣 (不經過漸變)</summary>
    public void SetWeatherInstant(WeatherType weather)
    {
        currentWeather = targetWeather = weather;
        weatherBlend = 1f;
        look = Looks[(int)weather];
        ScheduleNextWeather();
    }

    public void SetWeather(WeatherType weather)
    {
        if (weatherBlend < 1f) currentWeather = targetWeather;
        if (weather == targetWeather && weatherBlend >= 1f)
        {
            ScheduleNextWeather();
            return;
        }
        targetWeather = weather;
        weatherBlend = 0f;
        ScheduleNextWeather();
        ShowToast($"天氣轉為：{WeatherNames[(int)weather]}");
    }

    private void ScheduleNextWeather()
    {
        nextWeatherChange = Time.time + Random.Range(profile.weatherChangeInterval.x, profile.weatherChangeInterval.y);
    }

    private WeatherType PickWeather()
    {
        float[] weights = { profile.clearWeight, profile.cloudyWeight, profile.rainWeight, profile.stormWeight, profile.fogWeight };
        float total = 0f;
        foreach (float w in weights) total += w;
        float roll = Random.value * Mathf.Max(0.0001f, total);
        for (int i = 0; i < weights.Length; i++)
        {
            roll -= weights[i];
            if (roll <= 0f) return (WeatherType)i;
        }
        return WeatherType.Clear;
    }

    private static WeatherLook Lerp(WeatherLook a, WeatherLook b, float t)
    {
        return new WeatherLook
        {
            Cloud = Mathf.Lerp(a.Cloud, b.Cloud, t),
            FogStart = Mathf.Lerp(a.FogStart, b.FogStart, t),
            FogEnd = Mathf.Lerp(a.FogEnd, b.FogEnd, t),
            Rain = Mathf.Lerp(a.Rain, b.Rain, t),
            Wind = Mathf.Lerp(a.Wind, b.Wind, t),
            Gust = Mathf.Lerp(a.Gust, b.Gust, t),
            Lightning = t > 0.5f ? b.Lightning : a.Lightning
        };
    }

    #endregion

    #region 太陽、天空、環境光、霧

    private void UpdateSunAndSky()
    {
        // 6 點日出、18 點日落；太陽高度角 = sin
        float sunAngle = (hour - 6f) / 12f * 180f;
        float elevation = Mathf.Sin(Mathf.Clamp(sunAngle, -180f, 360f) * Mathf.Deg2Rad);
        daylight = Mathf.Clamp01(elevation * 2.2f + 0.15f);
        float dusk = Mathf.Clamp01(1f - Mathf.Abs(elevation) * 4f) * (elevation > -0.25f ? 1f : 0f); // 日出日落的暖色
        float cloud = look.Cloud;

        Color noon = new Color(1f, 0.97f, 0.9f);
        Color sunset = new Color(1f, 0.55f, 0.3f);
        Color moon = new Color(0.55f, 0.65f, 0.95f);
        Color sunColor = Color.Lerp(Color.Lerp(noon, sunset, dusk), moon, 1f - daylight);
        sunColor = Color.Lerp(sunColor, new Color(0.72f, 0.75f, 0.8f), cloud * 0.6f);

        float flash = lightningFlash;
        if (sun != null)
        {
            bool isDay = elevation > -0.05f;
            // 白天為太陽 (由東升到西落)；夜晚把同一盞燈轉成斜照的月光
            float pitch = isDay ? Mathf.Clamp(sunAngle, 4f, 176f) : 40f;
            // 白天且天空盒畫有太陽：光從畫中太陽的方向照下來 (陰影與天空一致)
            sun.transform.rotation = isDay && skyboxSunRotation.HasValue && RenderSettings.skybox == dayMaterial
                ? skyboxSunRotation.Value
                : Quaternion.Euler(pitch, 330f, 0f);
            float dayIntensity = baseSunIntensity * Mathf.Clamp01(elevation * 3f) * (1f - cloud * 0.7f);
            float nightIntensity = 0.12f * (1f - cloud * 0.6f);
            sun.intensity = Mathf.Max(dayIntensity, isDay ? 0f : nightIntensity) + flash * 1.5f;
            sun.color = flash > 0.01f ? Color.Lerp(sunColor, new Color(0.85f, 0.9f, 1f), flash) : sunColor;
            sun.shadowStrength = Mathf.Lerp(0.35f, 0.9f, daylight * (1f - cloud * 0.6f));
        }

        // 環境光 (三色)：白天藍天、黃昏偏暖、夜晚深藍
        Color daySky = new Color(0.62f, 0.72f, 0.88f);
        Color nightSky = new Color(0.07f, 0.09f, 0.16f);
        Color grey = new Color(0.55f, 0.57f, 0.6f);
        Color sky = Color.Lerp(nightSky, Color.Lerp(daySky, new Color(0.9f, 0.62f, 0.45f), dusk * 0.6f), daylight);
        sky = Color.Lerp(sky, grey * Mathf.Lerp(0.25f, 1f, daylight), cloud * 0.7f);
        sky += Color.white * flash * 0.6f;
        RenderSettings.ambientSkyColor = sky;
        RenderSettings.ambientEquatorColor = sky * 0.8f;
        RenderSettings.ambientGroundColor = sky * 0.45f;

        RenderSettings.fogColor = Color.Lerp(sky * 1.05f, grey * Mathf.Lerp(0.2f, 0.95f, daylight), Mathf.Max(cloud, look.Rain) * 0.8f);
        RenderSettings.fogStartDistance = look.FogStart * Mathf.Lerp(0.6f, 1f, daylight);
        RenderSettings.fogEndDistance = look.FogEnd * Mathf.Lerp(0.55f, 1f, daylight);

        // 天空盒：白天/夜晚切換 + 曝光與色調
        bool useNight = nightMaterial != null && daylight < 0.12f;
        Material active = useNight ? nightMaterial : dayMaterial;
        if (active != null)
        {
            if (RenderSettings.skybox != active) RenderSettings.skybox = active;
            float exposure = useNight ? Mathf.Lerp(0.55f, 0.9f, Mathf.InverseLerp(0.12f, 0f, daylight))
                                      : Mathf.Lerp(0.25f, 1f, daylight) * Mathf.Lerp(1f, 0.4f, cloud);
            if (active.HasProperty("_Exposure")) active.SetFloat("_Exposure", exposure + flash * 0.8f);
            if (active.HasProperty("_Tint"))
            {
                Color tint = Color.Lerp(new Color(0.5f, 0.5f, 0.5f), new Color(0.55f, 0.42f, 0.36f), dusk * 0.8f);
                // 陰雨/霧天：天空褪色成灰 (降低藍天的飽和度)
                tint = Color.Lerp(tint, new Color(0.34f, 0.35f, 0.36f), cloud);
                active.SetColor("_Tint", tint);
            }
        }
    }

    private void UpdateLightning(float dt)
    {
        lightningFlash = Mathf.MoveTowards(lightningFlash, 0f, dt * 6f);
        if (look.Lightning < 0.5f) return;
        if (Time.time >= nextLightning)
        {
            lightningFlash = 1f;
            nextLightning = Time.time + Random.Range(5f, 14f);
            // 二次閃光
            Invoke(nameof(SecondFlash), Random.Range(0.12f, 0.25f));
        }
    }

    private void SecondFlash()
    {
        lightningFlash = Mathf.Max(lightningFlash, 0.7f);
    }

    #endregion

    #region 風、雨、前燈

    private void UpdateWind()
    {
        float t = Time.time * 0.25f;
        float gust = (Mathf.PerlinNoise(noiseSeed, t) - 0.35f) * 2f;
        Vector3 direction = Quaternion.Euler(0f, Mathf.PerlinNoise(noiseSeed + 10f, t * 0.2f) * 360f, 0f) * Vector3.forward;
        wind = direction * Mathf.Max(0f, look.Wind + gust * look.Gust) * profile.windEffect;
    }

    private void BuildRain()
    {
        GameObject rainObject = new GameObject("Rain");
        rainObject.transform.SetParent(transform, false);
        rain = rainObject.AddComponent<ParticleSystem>();
        rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = rain.main;
        main.loop = true;
        main.startLifetime = 1.1f;
        main.startSpeed = 0f;
        main.startSize3D = true;
        main.startSizeX = 0.012f;
        main.startSizeY = 0.5f;
        main.startSizeZ = 0.012f;
        main.startColor = new Color(0.78f, 0.82f, 0.9f, 0.22f);
        main.maxParticles = 6000;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        ParticleSystem.EmissionModule emission = rain.emission;
        emission.rateOverTime = 0f;

        ParticleSystem.ShapeModule shape = rain.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(45f, 1f, 45f);

        ParticleSystem.VelocityOverLifetimeModule velocity = rain.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(0f);
        velocity.y = new ParticleSystem.MinMaxCurve(-22f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f);

        ParticleSystemRenderer particleRenderer = rain.GetComponent<ParticleSystemRenderer>();
        particleRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        particleRenderer.velocityScale = 0.03f;
        particleRenderer.lengthScale = 1f;
        particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Material material = shader != null ? new Material(shader) : RuntimeMaterials.Glow(new Color(0.8f, 0.85f, 0.95f, 0.4f));
        if (shader != null)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetColor("_BaseColor", Color.white);
        }
        particleRenderer.sharedMaterial = material;
        rain.Play();
    }

    private void UpdateRain()
    {
        Camera camera = Camera.main;
        if (rain == null) return;
        if (camera != null)
        {
            rain.transform.position = camera.transform.position + Vector3.up * 14f + camera.transform.forward * 8f;
        }

        ParticleSystem.EmissionModule emission = rain.emission;
        emission.rateOverTime = look.Rain * 4500f;

        ParticleSystem.VelocityOverLifetimeModule velocity = rain.velocityOverLifetime;
        velocity.x = new ParticleSystem.MinMaxCurve(wind.x * 1.5f);
        velocity.z = new ParticleSystem.MinMaxCurve(wind.z * 1.5f);
    }

    /// <summary>夜間或濃霧時，在玩家相機上開一盞前燈 (FPV 夜飛照明)</summary>
    private void UpdateHeadlight()
    {
        Camera camera = Camera.main;
        float need = Mathf.Clamp01((0.35f - daylight) / 0.3f);
        need = Mathf.Max(need, look.FogEnd < 120f ? 0.5f : 0f);

        if (headlight == null)
        {
            if (need <= 0f || camera == null) return;
            GameObject lightObject = new GameObject("DroneHeadlight");
            headlight = lightObject.AddComponent<Light>();
            headlight.type = LightType.Spot;
            headlight.spotAngle = 70f;
            headlight.innerSpotAngle = 35f;
            headlight.range = 70f;
            headlight.color = new Color(1f, 0.96f, 0.88f);
            headlight.shadows = LightShadows.None;
        }

        if (camera != null && headlight.transform.parent != camera.transform)
        {
            headlight.transform.SetParent(camera.transform, false);
            headlight.transform.localPosition = new Vector3(0f, -0.1f, 0.2f);
            headlight.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);
        }
        headlight.enabled = need > 0.01f;
        headlight.intensity = need * 6f;
    }

    #endregion

    #region 提示文字

    private void BuildToast()
    {
        GameObject canvasObject = new GameObject("EnvironmentToast", typeof(RectTransform));
        canvasObject.transform.SetParent(transform, false);
        toastCanvas = canvasObject.AddComponent<Canvas>();
        toastCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        toastCanvas.sortingOrder = 31000;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        toastText = RuntimeUI.CreateText("Toast", canvasObject.transform, string.Empty, 34f, Vector2.zero, new Vector2(900f, 60f));
        RectTransform rect = toastText.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -140f);
        toastText.color = new Color(1f, 1f, 1f, 0f);
        toastText.outlineWidth = 0.2f;
        toastText.outlineColor = new Color32(0, 0, 0, 200);
    }

    private void ShowToast(string message)
    {
        if (toastText == null) return;
        toastText.text = message;
        toastTimer = 3f;
    }

    private void UpdateToast(float dt)
    {
        if (toastText == null) return;
        // VR 中 Overlay 不可見時改綁定相機
        Camera camera = Camera.main;
        if (camera != null && toastCanvas.renderMode != RenderMode.ScreenSpaceCamera)
        {
            toastCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            toastCanvas.worldCamera = camera;
            toastCanvas.planeDistance = 0.5f;
        }
        toastTimer = Mathf.Max(0f, toastTimer - dt);
        Color color = toastText.color;
        color.a = Mathf.Clamp01(toastTimer / 0.5f);
        toastText.color = color;
    }

    public static string FormatHour(float value)
    {
        int h = Mathf.FloorToInt(value) % 24;
        int m = Mathf.FloorToInt((value - Mathf.Floor(value)) * 60f);
        return $"{h:00}:{m:00}";
    }

    public string WeatherLabel => WeatherNames[(int)targetWeather];

    #endregion

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
    }
}
