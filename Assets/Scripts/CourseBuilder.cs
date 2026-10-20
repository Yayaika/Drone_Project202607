using UnityEngine;
using System.Collections.Generic;
using DG.Tweening;

// 賽道生成器，負責在場景中動態生成起點、終點、檢查圈與樹木，並確保樹木不會阻擋賽道上的關鍵點。
public class CourseBuilder : MonoBehaviour
{
    public GameObject drone;
    public int checkpointCount = 7;
    public float courseLength = 150f;
    public int treeCount = 30;

    [Header("植物 (Anime Nature，由選單 8 填入；留空則使用簡易樹)")]
    public GameObject[] treePrefabs;
    public GameObject[] bushPrefabs;
    public GameObject[] grassPrefabs;
    public Vector2 treeHeightRange = new Vector2(9f, 16f);
    public int bushCount = 40;
    public int grassCount = 160;

    [Header("防擋道設定 (安全距離)")]
    [Tooltip("樹木與檢查圈、起終點平台的水平安全距離")]
    public float safetyRadius = 12f;

    [Header("地圖邊界 (空氣牆)")]
    [Tooltip("左右邊界 (距賽道中線，m)")]
    public float boundaryHalfWidth = 60f;
    [Tooltip("起點後方與終點前方的緩衝距離 (m)")]
    public float boundaryEndMargin = 40f;
    public float boundaryCeiling = 45f;

    // 用來儲存所有關鍵點的位置 (起點、終點、所有圈圈)
    private List<Vector3> criticalPositions = new List<Vector3>();

    void Start()
    {
        BuildCourse();
        MapBoundary.Build(transform, new Vector2(-boundaryHalfWidth, -boundaryEndMargin),
            new Vector2(boundaryHalfWidth, courseLength + boundaryEndMargin), 0f, boundaryCeiling);

        // 讓 GameManager 的總檢查點數與實際生成數量一致，並把第一個圈設為導航目標
        if (GameManager.Instance != null)
        {
            GameManager.Instance.totalCheckpoints = checkpointCount;
        }
        RingTrigger.SetNavigationTo(0);
    }

    void BuildCourse()
    {
        // 記錄關鍵點：起點與終點
        criticalPositions.Add(new Vector3(0, 0, 0));
        criticalPositions.Add(new Vector3(0, 0, courseLength));

        CreatePlatform(new Vector3(0, 0, 0), Color.green, "START");
        CreatePlatform(new Vector3(0, 0, courseLength), Color.red, "FINISH");

        // 生成連貫的蜿蜒路線 (控制點左右/高低漸變)，再沿路線連續擺放檢查圈
        List<Vector3> controls = new List<Vector3> { new Vector3(0f, 3f, 0f) };
        float lateral = 0f;
        float height = 6f;
        for (int i = 1; i <= checkpointCount; i++)
        {
            lateral = Mathf.Clamp(lateral + Random.Range(-9f, 9f), -15f, 15f);
            height = Mathf.Clamp(height + Random.Range(-3f, 3f), 4f, 12f);
            controls.Add(new Vector3(lateral, height, courseLength * i / (checkpointCount + 1f)));
        }
        controls.Add(new Vector3(0f, 4f, courseLength));

        List<Vector3> route = RaceCourseLayout.RoundCorners(controls, 10f, 4f, 0.5f);
        List<RaceCourseLayout.Gate> gates = RaceCourseLayout.PlaceGates(route, 12f, courseLength / (checkpointCount + 1f), 30f, 3f * RingSizeScale, Random.Range(0, 100000));
        for (int i = 0; i < gates.Count; i++)
        {
            CreateCheckpointRing(gates[i].Position, i, gates[i].Rotation, 3f);
            criticalPositions.Add(gates[i].Position);
        }
        checkpointCount = gates.Count;

        // 生成樹木（加入避開邏輯）
        for (int i = 0; i < treeCount; i++)
        {
            Vector3 treePos = Vector3.zero;
            bool validPos = false;
            int attempts = 0;

            while (!validPos && attempts < 100)
            {
                attempts++;
                float x = Random.Range(-40f, 40f);
                float z = Random.Range(-10f, courseLength + 10f);
                treePos = new Vector3(x, 0, z);

                validPos = true;

                foreach (Vector3 critPos in criticalPositions)
                {
                    float distanceXZ = Vector2.Distance(
                        new Vector2(treePos.x, treePos.z),
                        new Vector2(critPos.x, critPos.z)
                    );

                    if (distanceXZ < safetyRadius)
                    {
                        validPos = false;
                        break;
                    }
                }
            }

            if (validPos)
            {
                CreateTree(treePos);
            }
        }

        ScatterGroundCover();
    }

    /// <summary>灌木與草叢 (避開賽道中央，讓場地不再是單調的平面)</summary>
    void ScatterGroundCover()
    {
        for (int i = 0; i < bushCount && bushPrefabs != null && bushPrefabs.Length > 0; i++)
        {
            Vector3 p = new Vector3(Random.Range(-45f, 45f), 0f, Random.Range(-15f, courseLength + 15f));
            if (Mathf.Abs(p.x) < 8f || OnPlatform(p, 2f)) continue;
            PlaceVegetation(bushPrefabs[Random.Range(0, bushPrefabs.Length)], p, Random.Range(1.2f, 2.5f), false, 0.05f, true);
        }
        for (int i = 0; i < grassCount && grassPrefabs != null && grassPrefabs.Length > 0; i++)
        {
            Vector3 p = new Vector3(Random.Range(-50f, 50f), 0f, Random.Range(-20f, courseLength + 20f));
            if (OnPlatform(p, 1f)) continue;
            GameObject grass = PlaceVegetation(grassPrefabs[Random.Range(0, grassPrefabs.Length)], p, Random.Range(0.6f, 1f), false, 0.08f, true);
            if (grass == null) continue;
            foreach (Renderer r in grass.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    /// <summary>點是否落在起點/終點平台 (8×8m) 外擴 margin 的範圍內</summary>
    bool OnPlatform(Vector3 p, float margin)
    {
        float reach = 4f + margin;
        foreach (float z in new[] { 0f, courseLength })
        {
            if (Mathf.Abs(p.x) < reach && Mathf.Abs(p.z - z) < reach) return true;
        }
        return false;
    }

    /// <summary>放置植物並縮放 (size = 樹高，或 byWidth 時為寬度)；沒有 LOD 的模型加上遠距剔除</summary>
    GameObject PlaceVegetation(GameObject prefab, Vector3 pos, float size, bool keepCollider, float cullScreenHeight, bool byWidth = false)
    {
        if (prefab == null) return null;
        GameObject plant = Instantiate(prefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * prefab.transform.rotation);
        Renderer[] renderers = plant.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return plant;
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
        float measured = byWidth ? Mathf.Max(bounds.size.x, bounds.size.z) : bounds.size.y;
        if (measured > 0.01f) plant.transform.localScale *= size / measured;
        if (!keepCollider)
        {
            foreach (Collider c in plant.GetComponentsInChildren<Collider>()) Destroy(c);
        }
        if (plant.GetComponent<LODGroup>() == null)
        {
            LODGroup group = plant.AddComponent<LODGroup>();
            group.SetLODs(new[] { new LOD(cullScreenHeight, renderers) });
            group.RecalculateBounds();
        }
        return plant;
    }

    void CreatePlatform(Vector3 pos, Color color, string label)
    {
        // 1. 平台本體
        GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
        platform.name = label + "_Platform";
        platform.transform.position = pos + Vector3.up * 0.25f;
        platform.transform.localScale = new Vector3(8f, 0.5f, 8f);
        platform.GetComponent<Renderer>().material.color = color;

        // 【修正核心問題】將原本卡在平台中央 (0,0,0) 的單一柱子，移至左右兩側做成拱門 (Gate)
        // 徹底清空起點中央區域，讓無人機重生時絕不會再撞到柱子模型內！
        float poleXOffset = 3.8f;

        // 左側門柱
        GameObject poleL = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        poleL.name = label + "_Pole_L";
        poleL.transform.position = pos + new Vector3(-poleXOffset, 2f, 0f);
        poleL.transform.localScale = new Vector3(0.3f, 2f, 0.3f);
        poleL.GetComponent<Renderer>().material.color = Color.gray;

        // 右側門柱
        GameObject poleR = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        poleR.name = label + "_Pole_R";
        poleR.transform.position = pos + new Vector3(poleXOffset, 2f, 0f);
        poleR.transform.localScale = new Vector3(0.3f, 2f, 0.3f);
        poleR.GetComponent<Renderer>().material.color = Color.gray;

        // 拱門頂部橫樑標示牌
        GameObject sign = GameObject.CreatePrimitive(PrimitiveType.Cube);
        sign.name = label + "_Sign";
        sign.transform.position = pos + Vector3.up * 4.2f;
        sign.transform.localScale = new Vector3(8f, 0.6f, 0.3f);
        sign.GetComponent<Renderer>().material.color = color;
    }

    /// <summary>所有地圖檢查圈的整體縮放 (各場景設定的半徑 × 此值)</summary>
    public const float RingSizeScale = 0.65f;

    /// <summary>
    /// 建立一個檢查圈 (亦供程序化城市與 City2 賽道使用)。
    /// rotation 的 forward = 飛行穿越方向 (圈面法線)；外觀使用 GameplayAssets 的 ring target 20，缺少時退回圓柱拼接。
    /// </summary>
    public static GameObject CreateCheckpointRing(Vector3 pos, int index, Quaternion rotation, float radius)
    {
        radius *= RingSizeScale;
        GameObject ringParent = new GameObject("Checkpoint_" + index);
        ringParent.transform.SetPositionAndRotation(pos, rotation);

        GameplayAssets assets = GameplayAssets.Instance;
        GameObject ringPrefab = assets != null ? assets.checkpointRing : null;
        if (ringPrefab != null)
        {
            GameObject ring = Instantiate(ringPrefab, ringParent.transform);
            ring.name = "Ring";
            // Prefab 本身的旋轉讓圈面法線朝 +Z，保留它再整體套用檢查點朝向
            ring.transform.localPosition = Vector3.zero;
            ring.transform.localRotation = ringPrefab.transform.localRotation;
            ring.transform.localScale = ringPrefab.transform.localScale * (radius * 2f);
            // 原 Prefab 的 GateTrigger 是舊版 City2 單一閘門用的，這裡由 RingTrigger 負責判定
            foreach (GateTrigger legacy in ring.GetComponentsInChildren<GateTrigger>(true))
            {
                legacy.enabled = false;
                Destroy(legacy);
            }
            foreach (Collider modelCollider in ring.GetComponentsInChildren<Collider>(true))
            {
                Destroy(modelCollider);
            }
        }
        else
        {
            int segments = 12;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                seg.transform.SetParent(ringParent.transform, false);
                seg.transform.localPosition = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
                seg.transform.localScale = new Vector3(0.4f, 0.6f, 0.4f);
                seg.transform.LookAt(ringParent.transform.position);
                seg.transform.Rotate(90f, 0f, 0f);
                Destroy(seg.GetComponent<Collider>());
            }
        }

        // 薄盒狀觸發區：必須「穿過」圈面才算通過，不會因為從旁邊擦過而觸發
        BoxCollider col = ringParent.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(radius * 1.7f, radius * 1.7f, 2f);

        // 掛載動態判定組件並綁定編號
        RingTrigger trigger = ringParent.AddComponent<RingTrigger>();
        trigger.checkpointIndex = index;
        return ringParent;
    }

    void CreateTree(Vector3 pos)
    {
        if (treePrefabs != null && treePrefabs.Length > 0)
        {
            GameObject prefab = treePrefabs[Random.Range(0, treePrefabs.Length)];
            if (prefab != null)
            {
                PlaceVegetation(prefab, pos, Random.Range(treeHeightRange.x, treeHeightRange.y), true, 0.03f);
                return;
            }
        }

        GameObject tree = new GameObject("Tree");
        tree.transform.position = pos;

        float height = Random.Range(9f, 20f);
        float trunkThickness = Random.Range(0.8f, 1.5f);

        GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        trunk.transform.parent = tree.transform;
        trunk.transform.localPosition = Vector3.up * height * 0.5f;

        // 【修正】將樹幹粗細鎖定為正比例 (trunkThickness)，消除非對稱縮放產生的幾何異常
        trunk.transform.localScale = new Vector3(trunkThickness, height * 0.5f, trunkThickness);
        trunk.GetComponent<Renderer>().material.color = new Color(0.35f, 0.2f, 0.08f);

        GameObject leaves = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        leaves.transform.parent = tree.transform;
        leaves.transform.localPosition = Vector3.up * (height + 0.5f);
        float leafSize = Random.Range(6f, 12f);
        leaves.transform.localScale = new Vector3(leafSize, leafSize * 1.3f, leafSize);
        leaves.GetComponent<Renderer>().material.color = new Color(
            Random.Range(0.1f, 0.3f),
            Random.Range(0.4f, 0.7f),
            Random.Range(0.1f, 0.2f)
        );
    }
}

// 檢查點碰撞觸發邏輯，直接包含於同檔案內
public class RingTrigger : MonoBehaviour
{
    public static readonly Color NextColor = new Color(1f, 0.85f, 0.1f);
    public static readonly Color PendingColor = new Color(1f, 0.45f, 0.05f);
    private static readonly Color PassedColor = new Color(0.3f, 1f, 0.45f);

    /// <summary>場景中所有檢查圈 (含已通過而隱藏的)，依編號查詢</summary>
    private static readonly List<RingTrigger> all = new List<RingTrigger>();

    public int checkpointIndex;
    private bool isPassed = false;

    public bool IsPassed => isPassed;
    public static IReadOnlyList<RingTrigger> All => all;

    private void Awake()
    {
        all.Add(this);
    }

    private void OnDestroy()
    {
        all.Remove(this);
    }

    private void Start()
    {
        // 0 號目標圈開局自動設為黃色提示，其餘為橘色
        SetColor(checkpointIndex == 0 ? NextColor : PendingColor);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isPassed) return;

        // 判斷是否為無人機
        if (other.GetComponentInParent<DroneController1>() != null || other.CompareTag("Player"))
        {
            // 向 GameManager 驗證當前穿越順序
            if (GameManager.Instance != null && checkpointIndex == GameManager.Instance.passedCheckpoints)
            {
                isPassed = true;

                // 1. 通知 GameManager 更新進度與最新重生點
                GameManager.Instance.CheckpointPassed(checkpointIndex, transform);

                // 2. 通過的圈閃綠後縮小消失
                SetColor(PassedColor);
                GetComponent<Collider>().enabled = false;
                transform.DOKill();
                transform.DOScale(Vector3.zero, 0.35f).SetEase(Ease.InBack)
                    .OnComplete(() => { if (this != null) gameObject.SetActive(false); });

                // 3. 下一個目標檢查點改為黃色高亮，並更新 HUD 導航
                RingTrigger next = SetNavigationTo(checkpointIndex + 1);
                if (next != null)
                {
                    next.SetColor(NextColor);
                }
            }
        }
    }

    /// <summary>把 HUD 導航目標設為指定編號的檢查圈，找不到則清除</summary>
    public static RingTrigger SetNavigationTo(int index)
    {
        foreach (RingTrigger trigger in all)
        {
            if (trigger != null && trigger.checkpointIndex == index)
            {
                NavigationTarget.Set(trigger.transform.position, $"CP {index + 1}/{all.Count}", NextColor);
                return trigger;
            }
        }

        NavigationTarget.Clear();
        return null;
    }

    public void SetColor(Color newColor)
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r == null) continue;
            Material material = r.material;
            material.color = newColor;
            if (material.HasProperty("_EmissionColor"))
            {
                // 讓圈在陰影/夜晚也看得清楚
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", newColor * 0.8f);
            }
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        all.Clear();
    }
}
