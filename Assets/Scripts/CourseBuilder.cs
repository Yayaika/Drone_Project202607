using UnityEngine;
using System.Collections.Generic;

// 賽道生成器，負責在場景中動態生成起點、終點、檢查圈與樹木，並確保樹木不會阻擋賽道上的關鍵點。
public class CourseBuilder : MonoBehaviour
{
    public GameObject drone;
    public int checkpointCount = 7;
    public float courseLength = 150f;
    public int treeCount = 30;

    [Header("防擋道設定 (安全距離)")]
    [Tooltip("樹木與檢查圈、起終點平台的水平安全距離")]
    public float safetyRadius = 12f;

    // 用來儲存所有關鍵點的位置 (起點、終點、所有圈圈)
    private List<Vector3> criticalPositions = new List<Vector3>();

    void Start()
    {
        BuildCourse();
    }

    void BuildCourse()
    {
        // 記錄關鍵點：起點與終點
        criticalPositions.Add(new Vector3(0, 0, 0));
        criticalPositions.Add(new Vector3(0, 0, courseLength));

        CreatePlatform(new Vector3(0, 0, 0), Color.green, "START");
        CreatePlatform(new Vector3(0, 0, courseLength), Color.red, "FINISH");

        // 生成檢查點並記錄其位置
        for (int i = 0; i < checkpointCount; i++)
        {
            float z = courseLength * (i + 1f) / (checkpointCount + 1f);
            float x = Random.Range(-15f, 15f);
            float y = Random.Range(4f, 12f);

            Vector3 ringPos = new Vector3(x, y, z);
            CreateCheckpointRing(ringPos, i);
            criticalPositions.Add(ringPos);
        }

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

    void CreateCheckpointRing(Vector3 pos, int index)
    {
        GameObject ringParent = new GameObject("Checkpoint_" + index);
        ringParent.transform.position = pos;

        int segments = 12;
        float radius = 3.5f;

        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 segPos = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);

            GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            seg.transform.parent = ringParent.transform;
            seg.transform.localPosition = segPos;
            seg.transform.localScale = new Vector3(0.4f, 0.6f, 0.4f);
            seg.transform.LookAt(ringParent.transform.position);
            seg.transform.Rotate(90f, 0f, 0f);
            seg.GetComponent<Renderer>().material.color = new Color(1f, 0.6f, 0f); // 預設橘色
            Destroy(seg.GetComponent<Collider>());
        }

        SphereCollider col = ringParent.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = radius * 0.85f;

        // 掛載動態判定組件並綁定編號
        RingTrigger trigger = ringParent.AddComponent<RingTrigger>();
        trigger.checkpointIndex = index;
    }

    void CreateTree(Vector3 pos)
    {
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
    public int checkpointIndex;
    private bool isPassed = false;

    private void Start()
    {
        // 0 號目標圈開局自動設為黃色提示
        if (checkpointIndex == 0)
        {
            SetColor(Color.yellow);
        }
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

                // 1. 本身順序正確，變綠色
                SetColor(Color.green);

                // 2. 通知 GameManager 更新進度與最新重生點
                GameManager.Instance.CheckpointPassed(checkpointIndex);

                // 3. 搜尋並將下一個目標檢查點改為黃色高亮
                RingTrigger[] allTriggers = FindObjectsByType<RingTrigger>(FindObjectsSortMode.None);
                foreach (RingTrigger trigger in allTriggers)
                {
                    if (trigger.checkpointIndex == checkpointIndex + 1)
                    {
                        trigger.SetColor(Color.yellow);
                        break;
                    }
                }
            }
        }
    }

    public void SetColor(Color newColor)
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r != null) r.material.color = newColor;
        }
    }
}