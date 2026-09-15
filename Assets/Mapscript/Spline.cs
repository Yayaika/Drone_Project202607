using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

public class SplinePointGenerator : MonoBehaviour
{
    [Header("基礎設定")]
    public SplineContainer splineContainer; // 拖入畫好的 Spline
    public GameObject gatePrefab;           // 拖入剛剛做好的計分點 Prefab
    public int numberOfPoints = 10;         // 想要產生幾個計分點

    [Header("微調設定")]
    public Vector3 rotationOffset;          // 如果圓圈方向歪了，可以在這裡調 (例如 90, 0, 0)

    [ContextMenu("Generate Gates")] // 讓你在 Inspector 點右鍵就能直接生成
    public void GenerateGates()
    {
        if (splineContainer == null || gatePrefab == null) return;

        // 移除舊的點 (方便重複測試)
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(transform.GetChild(i).gameObject);
        }

        // 開始生成
        for (int i = 0; i < numberOfPoints; i++)
        {
            // 計算在 Spline 上的進度比例 t (0.0 到 1.0)
            float t = (float)i / (numberOfPoints - 1);

            // 2. 取得該點的位置 把剛剛算出來的百分比 $t$，轉換成真實的 3D 空間座標 $(x, y, z)$
            float3 position = splineContainer.EvaluatePosition(t);

            // 3. 取得該點的切線 (Tangent) 與 向上向量 (Up Vector) 取得該位置的「前進方向」與「正上方方向」
            float3 tangent = splineContainer.EvaluateTangent(t);
            float3 upVector = splineContainer.EvaluateUpVector(t);

            // 4. 計算旋轉：讓圓圈正面對著飛行路徑
            Quaternion rotation = Quaternion.LookRotation(tangent, upVector);
            // 強制將圓圈的 Z 軸 (正面) 對齊 tangent 的方向，並盡量讓 Y 軸 (上方) 對齊 upVector
            rotation *= Quaternion.Euler(rotationOffset); // 加入微調偏移量
            // *疊加旋轉
            // 5. 生成並設定父物件
            GameObject newGate = Instantiate(gatePrefab, position, rotation, transform);
            newGate.name = $"Gate_{i}";
            
            // 如果你有計分腳本，可以在這裡順便幫它編號
            // var scoreScript = newGate.GetComponent<ScoreGate>();
            // if(scoreScript != null) scoreScript.gateIndex = i;
        }
        
        Debug.Log($"成功沿著路徑生成了 {numberOfPoints} 個計分點！");
    }
}