using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 路燈夜間照明：所有關卡的 LowpolyStreetPack 路燈都保留了隱藏的光錐網格 (材質名稱含 LightShape)，
/// 這裡在每個光錐頂端 (燈頭) 加一盞向下的暖色聚光燈，照射角度與距離依光錐大小計算。
/// <see cref="EnvironmentController.IsNight"/> 時淡入點亮、白天熄滅；為了效能只點亮離攝影機最近的 maxActive 盞 (不投影)。
/// 場景中預先擺好、名稱以 "NightLamp" 開頭的燈 (例如港區高桿照明) 也一併管理，亮度沿用該燈設定的強度。
/// 車燈：城市車流 (<see cref="CityTraffic"/>) 與打擊場景環狀車道 (<see cref="StrikeVehicleRoute"/>) 行駛中的車輛，
/// 夜間亮起左右兩盞白色頭燈、紅色尾燈 (發光燈泡)，最近的 maxActiveVehicles 輛的兩盞頭燈另有向前照路的聚光燈。
/// 有 GameManager 的關卡自動建立；室內關卡 (<see cref="IndoorLighting"/>) 不建立。
/// </summary>
public class StreetLampLights : MonoBehaviour
{
    [SerializeField] private int maxActive = 48;
    [SerializeField] private float maxDistance = 160f;
    [Tooltip("路燈聚光燈強度 (URP 照度隨距離平方衰減，燈高約 8m)")]
    [SerializeField] private float intensity = 60f;
    [SerializeField] private Color color = new Color(1f, 0.82f, 0.58f);
    [SerializeField] private float fadeSeconds = 1.5f;
    [SerializeField] private int maxActiveVehicles = 16;
    [SerializeField] private float headlightIntensity = 40f;

    private class Lamp
    {
        public Light Light;
        public float BaseIntensity;
        public GameObject Bulb;
        public float Distance;
    }

    private readonly List<Lamp> lamps = new List<Lamp>();
    private readonly List<Lamp> vehicles = new List<Lamp>();
    private readonly HashSet<Transform> litVehicles = new HashSet<Transform>();
    private float nextVehicleScan;
    private readonly HashSet<Light> wantedLamps = new HashSet<Light>();
    private readonly HashSet<Light> wantedVehicles = new HashSet<Light>();
    private float level;
    private float nextCull;

    #region 自動建立

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryCreate();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single) TryCreate();
    }

    private static void TryCreate()
    {
        if (FindFirstObjectByType<GameManager>() == null || FindFirstObjectByType<StreetLampLights>() != null) return;
        if (FindFirstObjectByType<IndoorLighting>() != null) return;
        new GameObject("[StreetLampLights]").AddComponent<StreetLampLights>();
    }

    #endregion

    private IEnumerator Start()
    {
        // 程序化城市在 Start 才生成路燈，等兩幀再收集
        yield return null;
        yield return null;
        Collect();
    }

    private void Collect()
    {
        foreach (MeshRenderer cone in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (cone.enabled || !IsCone(cone)) continue;
            MeshFilter filter = cone.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;

            // 光錐的世界包圍盒：頂端 = 燈頭，底面半徑決定照射角度
            Bounds local = filter.sharedMesh.bounds;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 world = cone.transform.TransformPoint(corner);
                min = Vector3.Min(min, world);
                max = Vector3.Max(max, world);
            }
            float height = Mathf.Max(1f, max.y - min.y);
            float radius = Mathf.Max(1f, Mathf.Max(max.x - min.x, max.z - min.z) * 0.5f);

            Light light = new GameObject("LampSpot").AddComponent<Light>();
            light.transform.SetParent(cone.transform.parent != null ? cone.transform.parent : cone.transform, true);
            light.transform.SetPositionAndRotation(new Vector3((min.x + max.x) * 0.5f, max.y - 0.15f, (min.z + max.z) * 0.5f), Quaternion.Euler(90f, 0f, 0f));
            light.type = LightType.Spot;
            light.spotAngle = Mathf.Clamp(2f * Mathf.Atan(radius * 1.4f / height) * Mathf.Rad2Deg, 60f, 140f);
            light.innerSpotAngle = light.spotAngle * 0.55f;
            light.range = height * 2f + 4f;
            light.color = color;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            light.enabled = false;
            lamps.Add(new Lamp { Light = light, BaseIntensity = intensity, Bulb = CreateBulb(light.transform, 0.22f) });
        }

        foreach (Light authored in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (!authored.name.StartsWith("NightLamp")) continue;
            lamps.Add(new Lamp { Light = authored, BaseIntensity = authored.intensity, Bulb = CreateBulb(authored.transform, 0.6f) });
            authored.enabled = false;
        }
    }

    /// <summary>燈頭的發光燈泡 (夜間亮燈時顯示，看得出路燈被點亮)</summary>
    private GameObject CreateBulb(Transform light, float size)
    {
        GameObject bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(bulb.GetComponent<Collider>());
        bulb.name = "LampBulb";
        bulb.transform.SetParent(light, false);
        bulb.transform.localPosition = Vector3.zero;
        bulb.transform.localScale = Vector3.one * size;
        Renderer bulbRenderer = bulb.GetComponent<Renderer>();
        bulbRenderer.sharedMaterial = RuntimeMaterials.Unlit(new Color(1f, 0.93f, 0.75f));
        bulbRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        bulb.SetActive(false);
        return bulb;
    }

    private static bool IsCone(Renderer renderer)
    {
        foreach (Material material in renderer.sharedMaterials)
        {
            if (material == null || !material.name.Contains("LightShape")) return false;
        }
        return renderer.sharedMaterials.Length > 0;
    }

    private void Update()
    {
        if (Time.time >= nextVehicleScan)
        {
            nextVehicleScan = Time.time + 1f;
            ScanVehicles();
        }

        EnvironmentController environment = EnvironmentController.Instance;
        float target = environment != null && environment.IsNight ? 1f : 0f;
        level = Mathf.MoveTowards(level, target, Time.deltaTime / fadeSeconds);

        if (Time.time >= nextCull)
        {
            nextCull = Time.time + 0.5f;
            SelectNearest(lamps, maxActive, wantedLamps);
            SelectNearest(vehicles, maxActiveVehicles * 2, wantedVehicles);   // 每輛車兩盞頭燈
        }

        UpdateLights(lamps, wantedLamps);
        UpdateLights(vehicles, wantedVehicles);
    }

    private void UpdateLights(List<Lamp> list, HashSet<Light> wanted)
    {
        foreach (Lamp lamp in list)
        {
            if (lamp.Light == null) continue;
            bool on = level > 0.001f && wanted.Contains(lamp.Light);
            lamp.Light.enabled = on;
            if (lamp.Bulb != null && lamp.Bulb.activeSelf != (level > 0.5f)) lamp.Bulb.SetActive(level > 0.5f);
            if (on) lamp.Light.intensity = lamp.BaseIntensity * level;
        }
    }

    /// <summary>只點亮離攝影機最近、且在 maxDistance 內的 count 盞</summary>
    private void SelectNearest(List<Lamp> list, int count, HashSet<Light> wanted)
    {
        wanted.Clear();
        Camera view = Camera.main;
        if (view == null) return;
        Vector3 eye = view.transform.position;
        list.RemoveAll(lamp => lamp.Light == null);
        foreach (Lamp lamp in list) lamp.Distance = (lamp.Light.transform.position - eye).sqrMagnitude;
        list.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        for (int i = 0; i < list.Count && i < count; i++)
        {
            if (list[i].Distance > maxDistance * maxDistance) break;
            wanted.Add(list[i].Light);
        }
    }

    /// <summary>新出現的行駛車輛加上車燈 (車輛根物件 +Z 為車頭，BoxCollider 為車身大小)</summary>
    private void ScanVehicles()
    {
        List<Transform> roots = new List<Transform>();
        if (CityTraffic.Instance != null) roots.AddRange(CityTraffic.Instance.ActiveVehicleRoots());
        foreach (StrikeVehicleRoute route in FindObjectsByType<StrikeVehicleRoute>(FindObjectsSortMode.None)) roots.AddRange(route.ActiveVehicles());
        foreach (Transform root in roots)
        {
            if (root == null || !litVehicles.Add(root)) continue;
            BoxCollider body = root.GetComponent<BoxCollider>();
            if (body == null) continue;
            Vector3 size = body.size;
            float front = body.center.z + size.z * 0.5f, rear = body.center.z - size.z * 0.5f;
            float lampY = body.center.y - size.y * 0.15f;

            GameObject bulbs = new GameObject("CarLampBulbs");
            bulbs.transform.SetParent(root, false);
            Material white = RuntimeMaterials.Unlit(new Color(1f, 0.97f, 0.85f));
            Material red = RuntimeMaterials.Unlit(new Color(0.9f, 0.08f, 0.05f));
            foreach (float side in new[] { -1f, 1f })
            {
                AddBulb(bulbs.transform, new Vector3(side * size.x * 0.34f, lampY, front), white);
                AddBulb(bulbs.transform, new Vector3(side * size.x * 0.36f, lampY + 0.1f, rear), red);
            }
            bulbs.SetActive(false);

            // 左右兩盞頭燈各自一個聚光燈光源 (燈泡顯示由左燈負責)
            foreach (float side in new[] { -1f, 1f })
            {
                Light headlight = new GameObject("Headlight").AddComponent<Light>();
                headlight.transform.SetParent(root, false);
                headlight.transform.localPosition = new Vector3(side * size.x * 0.34f, lampY, front + 0.05f);
                headlight.transform.localRotation = Quaternion.Euler(8f, side * 3f, 0f);
                headlight.type = LightType.Spot;
                headlight.spotAngle = 60f;
                headlight.innerSpotAngle = 35f;
                headlight.range = 28f;
                headlight.color = new Color(1f, 0.95f, 0.85f);
                headlight.shadows = LightShadows.None;
                headlight.enabled = false;
                vehicles.Add(new Lamp { Light = headlight, BaseIntensity = headlightIntensity * 0.6f, Bulb = side < 0f ? bulbs : null });
            }
        }
    }

    private static void AddBulb(Transform parent, Vector3 localPosition, Material material)
    {
        GameObject bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(bulb.GetComponent<Collider>());
        bulb.transform.SetParent(parent, false);
        bulb.transform.localPosition = localPosition;
        bulb.transform.localScale = new Vector3(0.22f, 0.12f, 0.08f);
        Renderer bulbRenderer = bulb.GetComponent<Renderer>();
        bulbRenderer.sharedMaterial = material;
        bulbRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}
