using UnityEngine;
using System.Collections.Generic;

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
            criticalPositions.Add(ringPos); // 記錄此檢查點
        }

        // 生成樹木（加入避開邏輯）
        for (int i = 0; i < treeCount; i++)
        {
            Vector3 treePos = Vector3.zero;
            bool validPos = false;
            int attempts = 0;

            // 嘗試尋找不會擋到路的位置（最多嘗試 100 次防死循環）
            while (!validPos && attempts < 100)
            {
                attempts++;
                float x = Random.Range(-40f, 40f);
                float z = Random.Range(-10f, courseLength + 10f);
                treePos = new Vector3(x, 0, z);

                validPos = true;

                // 比對所有關鍵點
                foreach (Vector3 critPos in criticalPositions)
                {
                    // 僅比對水平面 (XZ)，因為高處的檢查圈投影到地面依然不能有樹幹擋住
                    float distanceXZ = Vector2.Distance(
                        new Vector2(treePos.x, treePos.z),
                        new Vector2(critPos.x, critPos.z)
                    );

                    if (distanceXZ < safetyRadius)
                    {
                        validPos = false; // 離起終點或圈圈太近，此位置失效
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
        GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
        platform.name = label + "_Platform";
        platform.transform.position = pos + Vector3.up * 0.25f;
        platform.transform.localScale = new Vector3(8f, 0.5f, 8f);
        platform.GetComponent<Renderer>().material.color = color;

        GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pole.name = label + "_Pole";
        pole.transform.position = pos + Vector3.up * 2f;
        pole.transform.localScale = new Vector3(0.3f, 2f, 0.3f);
        pole.GetComponent<Renderer>().material.color = Color.gray;

        GameObject sign = GameObject.CreatePrimitive(PrimitiveType.Cube);
        sign.name = label + "_Sign";
        sign.transform.position = pos + Vector3.up * 4.5f;
        sign.transform.localScale = new Vector3(4f, 1f, 0.2f);
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
            seg.GetComponent<Renderer>().material.color = new Color(1f, 0.6f, 0f);
            Destroy(seg.GetComponent<Collider>());
        }

        SphereCollider col = ringParent.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = radius * 0.85f;

        CheckpointRing cp = ringParent.AddComponent<CheckpointRing>();
        cp.checkpointIndex = index;
    }

    void CreateTree(Vector3 pos)
    {
        GameObject tree = new GameObject("Tree");
        tree.transform.position = pos;

        float height = Random.Range(9f, 20f);

        GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        trunk.transform.parent = tree.transform;
        trunk.transform.localPosition = Vector3.up * height * 0.5f;
        float trunkThickness = Random.Range(0.8f, 1.5f);
        trunk.transform.localScale = new Vector3(0.4f, height * 0.5f, trunkThickness);
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