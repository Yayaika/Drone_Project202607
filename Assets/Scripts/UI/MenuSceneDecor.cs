using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 選單場景 (開始選單 / 選擇無人機) 的動態背景：
/// 1. 天空盒與遠景霧，背景使用台灣街景城市 (同一場景放一個 CityGenerator，含車流與路人)。
/// 2. 主角無人機懸浮擺動、螺旋槳轉動。
/// 3. 數架帶光軌的無人機在城市上空巡航。
/// 4. (選擇場景) 屋頂停機坪平台與發光展示台，選中的無人機在展示台上緩慢旋轉。
/// 只新增裝飾物件，不修改既有 UI 與選單邏輯。
/// </summary>
public class MenuSceneDecor : MonoBehaviour
{
    [Header("天空與霧")]
    [SerializeField] private Material skybox;
    [SerializeField] private bool enableFog = true;
    [SerializeField] private Color fogColor = new Color(0.72f, 0.82f, 0.9f);
    [SerializeField] private float fogStart = 80f;
    [SerializeField] private float fogEnd = 420f;

    [Header("主角無人機 (懸浮擺動)")]
    [SerializeField] private Transform heroDrone;
    [SerializeField] private float heroBobHeight = 0.12f;
    [SerializeField] private float heroBobSpeed = 1.6f;
    [SerializeField] private float heroSway = 6f;

    [Header("巡航無人機 (帶光軌)")]
    [Tooltip("複製來源的無人機 (只複製網格外觀，不含腳本、碰撞體、相機)；其 +Z 視為機頭")]
    [SerializeField] private Transform ambientDroneTemplate;
    [SerializeField] private int ambientDroneCount = 3;
    [SerializeField] private Vector3 flightCenter = new Vector3(0f, 5f, 22f);
    [SerializeField] private Vector3 flightRadius = new Vector3(16f, 2.5f, 9f);
    [Tooltip("相對於來源無人機的縮放 (來源本身可能已放大，例如開始選單的主角無人機為 10 倍)")]
    [SerializeField] private float ambientDroneScale = 0.35f;
    [SerializeField] private Color[] trailColors =
    {
        new Color(0.2f, 1f, 0.75f, 1f), new Color(1f, 0.55f, 0.15f, 1f), new Color(0.35f, 0.6f, 1f, 1f)
    };

    [Header("選擇無人機場景 (展示台)")]
    [SerializeField] private UIPlayerSelectCtrl selectController;
    [SerializeField] private Transform previewAnchor;
    [SerializeField] private float turntableSpeed = 22f;
    [Tooltip("建立屋頂停機坪平台 (取代原本的地形)")]
    [SerializeField] private bool buildRooftop;
    [SerializeField] private GameObject hideObject;
    [SerializeField] private Vector3 rooftopCenter = new Vector3(0f, 0f, 3f);
    [SerializeField] private Vector2 rooftopSize = new Vector2(16f, 16f);

    private class AmbientDrone
    {
        public Transform Root;
        public List<Transform> Props;
        public float Phase;
        public float Speed;
        public Vector3 Radius;
    }

    private readonly List<AmbientDrone> ambientDrones = new List<AmbientDrone>();
    private readonly List<Transform> heroProps = new List<Transform>();
    private Vector3 heroBasePosition;
    private Quaternion heroBaseRotation;
    private Transform pedestal;
    private Transform pedestalBeam;
    private GameObject lastShowcased;
    private float turntableAngle;

    private void Awake()
    {
        if (skybox != null)
        {
            RenderSettings.skybox = skybox;
            DynamicGI.UpdateEnvironment();
        }
        if (enableFog)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
        }

        if (hideObject != null) hideObject.SetActive(false);
        if (buildRooftop) BuildRooftop();
    }

    private void Start()
    {
        if (heroDrone != null)
        {
            heroBasePosition = heroDrone.position;
            heroBaseRotation = heroDrone.rotation;
            CollectProps(heroDrone, heroProps);
        }

        if (ambientDroneTemplate != null)
        {
            for (int i = 0; i < ambientDroneCount; i++)
            {
                SpawnAmbientDrone(i);
            }
        }
    }

    private void Update()
    {
        float t = Time.time;
        float dt = Time.deltaTime;

        if (heroDrone != null)
        {
            heroDrone.position = heroBasePosition + Vector3.up * (Mathf.Sin(t * heroBobSpeed) * heroBobHeight);
            heroDrone.rotation = heroBaseRotation * Quaternion.Euler(
                Mathf.Sin(t * 0.9f) * heroSway * 0.5f, Mathf.Sin(t * 0.35f) * heroSway * 2f, Mathf.Cos(t * 0.8f) * heroSway * 0.5f);
            SpinProps(heroProps, dt);
        }

        foreach (AmbientDrone drone in ambientDrones)
        {
            UpdateAmbientDrone(drone, t, dt);
        }

        UpdateShowcase(t, dt);
    }

    #region 螺旋槳

    /// <summary>找出常見的螺旋槳命名 (Prop / rotor / fan)</summary>
    private static void CollectProps(Transform root, List<Transform> props)
    {
        props.Clear();
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            string lower = child.name.ToLowerInvariant();
            if (lower == "prop" || lower.StartsWith("rotor_") || lower.StartsWith("fan.") || lower.Contains("prop0"))
            {
                props.Add(child);
            }
        }
    }

    private static void SpinProps(List<Transform> props, float dt)
    {
        for (int i = 0; i < props.Count; i++)
        {
            if (props[i] == null) continue;
            // 不同模型的螺旋槳旋轉軸不同：以本地 Z 為主 (Racing Drone)，rotor/fan 以本地 Y
            string lower = props[i].name.ToLowerInvariant();
            Vector3 axis = lower == "prop" ? Vector3.forward : Vector3.up;
            props[i].Rotate(axis, (i % 2 == 0 ? 1f : -1f) * 2400f * dt, Space.Self);
        }
    }

    #endregion

    #region 巡航無人機

    private void SpawnAmbientDrone(int index)
    {
        Transform clone = new GameObject("AmbientDrone_" + index).transform;
        clone.SetParent(transform, false);
        clone.localScale = ambientDroneTemplate.lossyScale * ambientDroneScale;
        CloneVisual(ambientDroneTemplate, clone);

        Color color = trailColors.Length > 0 ? trailColors[index % trailColors.Length] : Color.white;
        TrailRenderer trail = clone.gameObject.AddComponent<TrailRenderer>();
        trail.time = 1.4f;
        trail.minVertexDistance = 0.2f;
        trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.22f), new Keyframe(1f, 0f));
        trail.material = RuntimeMaterials.Glow(new Color(color.r, color.g, color.b, 0.75f));
        trail.startColor = color;
        trail.endColor = new Color(color.r, color.g, color.b, 0f);
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        AmbientDrone drone = new AmbientDrone
        {
            Root = clone,
            Props = new List<Transform>(),
            Phase = index * Mathf.PI * 2f / Mathf.Max(1, ambientDroneCount),
            Speed = 0.32f + index * 0.07f,
            Radius = new Vector3(flightRadius.x * (1f - index * 0.15f), flightRadius.y, flightRadius.z * (1f + index * 0.2f))
        };
        CollectProps(clone, drone.Props);
        ambientDrones.Add(drone);
        UpdateAmbientDrone(drone, 0f, 0f);
        trail.Clear();
    }

    /// <summary>8 字形 (Lissajous) 航線，機身朝前進方向並依轉彎傾斜</summary>
    private void UpdateAmbientDrone(AmbientDrone drone, float t, float dt)
    {
        float a = t * drone.Speed + drone.Phase;
        Vector3 Position(float angle) => flightCenter + new Vector3(
            Mathf.Sin(angle) * drone.Radius.x,
            Mathf.Sin(angle * 2f + drone.Phase) * drone.Radius.y,
            Mathf.Sin(angle * 2f) * drone.Radius.z);

        Vector3 position = Position(a);
        Vector3 ahead = Position(a + 0.05f);
        Vector3 further = Position(a + 0.1f);
        Vector3 forward = (ahead - position).normalized;
        Vector3 nextForward = (further - ahead).normalized;
        float turn = Vector3.SignedAngle(forward, nextForward, Vector3.up);

        Quaternion heading = Quaternion.LookRotation(forward, Vector3.up);
        // 前傾 15° 加上轉彎側傾
        drone.Root.position = position;
        drone.Root.rotation = heading * Quaternion.Euler(15f, 0f, Mathf.Clamp(-turn * 6f, -35f, 35f));
        SpinProps(drone.Props, dt);
    }

    /// <summary>只複製有網格的節點 (保留區域變換)，略過沒有任何 Renderer 的子樹 (相機、XR Origin、UI)</summary>
    private static void CloneVisual(Transform source, Transform target)
    {
        foreach (Transform child in source)
        {
            if (!child.gameObject.activeSelf || child.GetComponentInChildren<MeshRenderer>(false) == null) continue;

            Transform copy = new GameObject(child.name).transform;
            copy.SetParent(target, false);
            copy.localPosition = child.localPosition;
            copy.localRotation = child.localRotation;
            copy.localScale = child.localScale;

            MeshFilter filter = child.GetComponent<MeshFilter>();
            MeshRenderer meshRenderer = child.GetComponent<MeshRenderer>();
            if (filter != null && meshRenderer != null)
            {
                copy.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                MeshRenderer copyRenderer = copy.gameObject.AddComponent<MeshRenderer>();
                copyRenderer.sharedMaterials = meshRenderer.sharedMaterials;
            }
            CloneVisual(child, copy);
        }
    }

    #endregion

    #region 選擇場景：屋頂與展示台

    private void BuildRooftop()
    {
        Material concrete = RuntimeMaterials.Opaque(new Color(0.56f, 0.57f, 0.58f));
        Material pad = RuntimeMaterials.Opaque(new Color(0.22f, 0.24f, 0.27f));
        Material white = RuntimeMaterials.Opaque(new Color(0.93f, 0.93f, 0.9f));
        Material yellow = RuntimeMaterials.Opaque(new Color(0.95f, 0.78f, 0.15f));
        Material rail = RuntimeMaterials.Opaque(new Color(0.75f, 0.77f, 0.8f));
        Material tank = RuntimeMaterials.Opaque(new Color(0.2f, 0.42f, 0.78f));
        Material unit = RuntimeMaterials.Opaque(new Color(0.86f, 0.86f, 0.84f));

        CityMeshBuilder builder = new CityMeshBuilder();
        Vector3 c = rooftopCenter;
        float hx = rooftopSize.x * 0.5f;
        float hz = rooftopSize.y * 0.5f;

        // 屋頂樓板 + 女兒牆 + 欄杆
        builder.AddBox(c + new Vector3(0f, -0.25f, 0f), new Vector3(rooftopSize.x, 0.5f, rooftopSize.y), concrete);
        // 大樓本體往下延伸，從欄杆往外看不會看到懸空的平台
        builder.AddBox(c + new Vector3(0f, -20.5f, 0f), new Vector3(rooftopSize.x - 0.2f, 40f, rooftopSize.y - 0.2f), concrete);
        for (int side = -1; side <= 1; side += 2)
        {
            builder.AddBox(c + new Vector3(0f, 0.35f, side * (hz - 0.1f)), new Vector3(rooftopSize.x, 0.7f, 0.2f), concrete);
            builder.AddBox(c + new Vector3(side * (hx - 0.1f), 0.35f, 0f), new Vector3(0.2f, 0.7f, rooftopSize.y), concrete);
            builder.AddBox(c + new Vector3(0f, 1.1f, side * (hz - 0.1f)), new Vector3(rooftopSize.x, 0.06f, 0.06f), rail);
            builder.AddBox(c + new Vector3(side * (hx - 0.1f), 1.1f, 0f), new Vector3(0.06f, 0.06f, rooftopSize.y), rail);
            for (float u = -hx; u <= hx + 0.01f; u += 1.6f)
            {
                builder.AddBox(c + new Vector3(u, 0.9f, side * (hz - 0.1f)), new Vector3(0.05f, 0.4f, 0.05f), rail);
            }
            for (float u = -hz; u <= hz + 0.01f; u += 1.6f)
            {
                builder.AddBox(c + new Vector3(side * (hx - 0.1f), 0.9f, u), new Vector3(0.05f, 0.4f, 0.05f), rail);
            }
        }

        // 停機坪：黃色外圈 + H 標誌
        Vector3 padCenter = previewAnchor != null ? new Vector3(previewAnchor.position.x, c.y, previewAnchor.position.z) : c;
        builder.AddCylinder(Matrix4x4.identity, padCenter + Vector3.up * 0.001f, Vector3.up, 3.2f, 0.02f, 32, yellow);
        builder.AddCylinder(Matrix4x4.identity, padCenter + Vector3.up * 0.002f, Vector3.up, 2.95f, 0.02f, 32, pad);
        builder.AddBox(padCenter + new Vector3(-0.8f, 0.03f, 0f), new Vector3(0.3f, 0.01f, 2.2f), white);
        builder.AddBox(padCenter + new Vector3(0.8f, 0.03f, 0f), new Vector3(0.3f, 0.01f, 2.2f), white);
        builder.AddBox(padCenter + new Vector3(0f, 0.03f, 0f), new Vector3(1.6f, 0.01f, 0.3f), white);

        // 台灣頂樓常見的水塔與冷氣室外機
        Vector3 tankPos = c + new Vector3(hx - 1.6f, 0f, hz - 1.6f);
        builder.AddBox(tankPos + Vector3.up * 0.3f, new Vector3(1.6f, 0.6f, 1.6f), concrete);
        builder.AddCylinder(Matrix4x4.identity, tankPos + Vector3.up * 0.6f, Vector3.up, 0.7f, 1.5f, 16, tank);
        builder.AddBox(c + new Vector3(-hx + 1.2f, 0.35f, hz - 1f), new Vector3(1f, 0.7f, 0.45f), unit);
        builder.AddBox(c + new Vector3(-hx + 2.5f, 0.35f, hz - 1f), new Vector3(1f, 0.7f, 0.45f), unit);

        builder.Build("MenuRooftop", transform);

        // 展示台光圈與光柱
        pedestal = CreateDisc("Pedestal", padCenter + Vector3.up * 0.04f, 1.1f, RuntimeMaterials.Glow(new Color(0.2f, 1f, 0.75f, 0.55f)));
        // 淡淡的光柱 (高 1.4m，停在無人機下方)
        pedestalBeam = CreateDisc("PedestalBeam", padCenter + Vector3.up * 0.72f, 0.7f, RuntimeMaterials.Glow(new Color(0.2f, 1f, 0.75f, 0.06f)));
        pedestalBeam.localScale = new Vector3(1.4f, 0.7f, 1.4f);
    }

    private Transform CreateDisc(string discName, Vector3 position, float radius, Material material)
    {
        GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = discName;
        Destroy(disc.GetComponent<Collider>());
        disc.transform.SetParent(transform, false);
        disc.transform.position = position;
        disc.transform.localScale = new Vector3(radius * 2f, 0.01f, radius * 2f);
        Renderer discRenderer = disc.GetComponent<Renderer>();
        discRenderer.sharedMaterial = material;
        discRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return disc.transform;
    }

    /// <summary>選中的無人機在展示台上緩慢旋轉並懸浮 (螺旋槳保持靜止)</summary>
    private void UpdateShowcase(float t, float dt)
    {
        if (selectController == null || selectController.charOptions == null) return;

        GameObject active = null;
        foreach (UIPlayerSelectCtrl.CharOption option in selectController.charOptions)
        {
            if (option.droneModel != null && option.droneModel.activeInHierarchy)
            {
                active = option.droneModel;
                break;
            }
        }
        if (active == null) return;

        if (active != lastShowcased)
        {
            lastShowcased = active;
            turntableAngle = 0f;
            // 只保留航向，水平旋轉 (原本的前傾角在轉盤上會變成大幅側傾)
            showcaseBaseRotation = Quaternion.Euler(0f, active.transform.eulerAngles.y, 0f);
            showcaseBasePosition = active.transform.position;
            // 部分模型 (drone.prefab) 的槳葉 fan.00x 掛有循環旋轉動畫，展示時停住
            foreach (Animator animator in active.GetComponentsInChildren<Animator>(true))
            {
                if (animator.name.StartsWith("fan", System.StringComparison.OrdinalIgnoreCase)) animator.enabled = false;
            }
        }

        turntableAngle += turntableSpeed * dt;
        active.transform.position = showcaseBasePosition + Vector3.up * (Mathf.Sin(t * 1.5f) * 0.08f);
        active.transform.rotation = Quaternion.AngleAxis(turntableAngle, Vector3.up) * showcaseBaseRotation;

        if (pedestal != null)
        {
            float pulse = 1f + Mathf.Sin(t * 2.4f) * 0.06f;
            pedestal.localScale = new Vector3(2.2f * pulse, 0.01f, 2.2f * pulse);
        }
    }

    private Quaternion showcaseBaseRotation;
    private Vector3 showcaseBasePosition;

    #endregion
}
