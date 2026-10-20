using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 打擊專屬場景的環狀車道：子物件依序為路徑點 (封閉迴圈)，執行時生成車輛沿路徑等速行駛。
/// <see cref="StrikeMissionManager"/> 會從這些車輛中挑選移動目標；被炸毀的車原地停下燃燒，後方車輛排隊，
/// 一段時間後清除殘骸，車流恢復。
/// </summary>
public class StrikeVehicleRoute : MonoBehaviour
{
    [SerializeField] private GameObject[] carPrefabs;
    [SerializeField] private int vehicleCount = 5;
    [SerializeField] private float speed = 8f;
    [Tooltip("與前車 (殘骸) 保持的最小距離 (m)")]
    [SerializeField] private float minGap = 7f;
    [SerializeField] private float wreckClearSeconds = 35f;

    private class Vehicle
    {
        public Transform Root;
        public float S;
        public bool Wrecked;
    }

    private readonly List<Vehicle> vehicles = new List<Vehicle>();
    private readonly List<Vector3> points = new List<Vector3>();
    private readonly List<float> distances = new List<float>();
    private float length;

    /// <summary>目前仍在行駛的車輛</summary>
    public List<Transform> ActiveVehicles()
    {
        List<Transform> roots = new List<Transform>();
        foreach (Vehicle vehicle in vehicles)
        {
            if (vehicle.Root != null && !vehicle.Wrecked) roots.Add(vehicle.Root);
        }
        return roots;
    }

    private void Awake()
    {
        BuildPath();
        if (length < 1f || carPrefabs == null || carPrefabs.Length == 0) return;

        Transform holder = new GameObject("Vehicles").transform;
        holder.SetParent(transform, false);
        int count = Mathf.Max(0, vehicleCount);
        for (int i = 0; i < count; i++)
        {
            GameObject prefab = carPrefabs[i % carPrefabs.Length];
            if (prefab == null) continue;
            GameObject root = CityTraffic.CreateCarModel(prefab, holder, out _, out Vector3 size);
            BoxCollider box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, size.y * 0.5f, 0f);
            box.size = size;
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;

            Vehicle vehicle = new Vehicle { Root = root.transform, S = length * i / count };
            vehicles.Add(vehicle);
            Place(vehicle);
        }
    }

    private void BuildPath()
    {
        points.Clear();
        distances.Clear();
        foreach (Transform child in transform)
        {
            if (child.name.StartsWith("Waypoint")) points.Add(child.position);
        }
        if (points.Count < 2) return;

        length = 0f;
        for (int i = 0; i < points.Count; i++)
        {
            distances.Add(length);
            length += Vector3.Distance(points[i], points[(i + 1) % points.Count]);
        }
    }

    private void Update()
    {
        if (length < 1f) return;
        float step = speed * Time.deltaTime;
        foreach (Vehicle vehicle in vehicles)
        {
            if (vehicle.Root == null || vehicle.Wrecked) continue;
            if (GapToWreckAhead(vehicle) < minGap) continue; // 前方有殘骸：排隊等待
            vehicle.S = Mathf.Repeat(vehicle.S + step, length);
            Place(vehicle);
        }
    }

    private float GapToWreckAhead(Vehicle vehicle)
    {
        float best = float.MaxValue;
        foreach (Vehicle other in vehicles)
        {
            if (other == vehicle || !other.Wrecked || other.Root == null) continue;
            float gap = Mathf.Repeat(other.S - vehicle.S, length);
            if (gap < best) best = gap;
        }
        return best;
    }

    private void Place(Vehicle vehicle)
    {
        Vector3 position = PointAt(vehicle.S, out Vector3 forward);
        vehicle.Root.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
    }

    /// <summary>路徑上距離 s 的位置；轉角處以前後兩段的方向平滑過渡</summary>
    private Vector3 PointAt(float s, out Vector3 forward)
    {
        int index = 0;
        for (int i = points.Count - 1; i >= 0; i--)
        {
            if (s >= distances[i])
            {
                index = i;
                break;
            }
        }
        Vector3 a = points[index];
        Vector3 b = points[(index + 1) % points.Count];
        float segment = Vector3.Distance(a, b);
        float t = segment > 0.01f ? (s - distances[index]) / segment : 0f;

        Vector3 direction = (b - a).normalized;
        const float blend = 4f;
        float remaining = segment * (1f - t);
        if (remaining < blend)
        {
            Vector3 next = (points[(index + 2) % points.Count] - b).normalized;
            direction = Vector3.Slerp(direction, next, 0.5f * (1f - remaining / blend));
        }
        forward = direction.sqrMagnitude > 0.001f ? direction : Vector3.forward;
        return Vector3.Lerp(a, b, t);
    }

    /// <summary>炸彈命中：hit 屬於此路線的車輛時讓它停下並回傳 true</summary>
    public bool Wreck(Transform hit)
    {
        foreach (Vehicle vehicle in vehicles)
        {
            if (vehicle.Root == null || (hit != vehicle.Root && !hit.IsChildOf(vehicle.Root))) continue;
            if (!vehicle.Wrecked)
            {
                vehicle.Wrecked = true;
                StartCoroutine(ClearLater(vehicle));
            }
            return true;
        }
        return false;
    }

    private IEnumerator ClearLater(Vehicle vehicle)
    {
        yield return new WaitForSeconds(wreckClearSeconds);
        vehicles.Remove(vehicle);
        if (vehicle.Root != null) Destroy(vehicle.Root.gameObject);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = StrikeTarget.VehicleColor;
        Transform previous = null;
        Transform first = null;
        foreach (Transform child in transform)
        {
            if (!child.name.StartsWith("Waypoint")) continue;
            if (first == null) first = child;
            if (previous != null) Gizmos.DrawLine(previous.position, child.position);
            previous = child;
        }
        if (previous != null && first != null) Gizmos.DrawLine(previous.position, first.position);
    }
}
