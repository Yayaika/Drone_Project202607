using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 競速檢查圈的共用配置 (City2 Spline 賽道、城市生成器街道賽道、SampleScene 測試賽道)：
/// - <see cref="RoundCorners"/>：把路線的直角轉彎改成不會撞到建築的圓弧，避免 90° 急轉
/// - <see cref="PlaceGates"/>：沿路線連續擺放檢查圈，轉彎處自動加密，相鄰檢查圈的方向變化不超過 maxTurnDegrees；
///   位置左右/高低錯落，部分檢查圈刻意貼近建築、路燈或地面，增加飛行技巧要求
/// </summary>
public static class RaceCourseLayout
{
    public struct Gate
    {
        public Vector3 Position;
        public Quaternion Rotation;
    }

    /// <summary>
    /// 在每個轉角以二次貝茲曲線圓角，半徑從 maxRadius 往下試，直到曲線周圍 clearRadius 內沒有碰撞體
    /// (城市街道路口的圓角會留在路口範圍內，不會切過街角建築)。
    /// </summary>
    public static List<Vector3> RoundCorners(IList<Vector3> points, float maxRadius, float minRadius, float clearRadius)
    {
        List<Vector3> result = new List<Vector3>();
        if (points.Count < 3)
        {
            result.AddRange(points);
            return result;
        }

        result.Add(points[0]);
        for (int i = 1; i < points.Count - 1; i++)
        {
            Vector3 a = points[i - 1];
            Vector3 b = points[i];
            Vector3 c = points[i + 1];
            Vector3 inDir = Flat(b - a);
            Vector3 outDir = Flat(c - b);
            float turn = inDir.sqrMagnitude < 0.01f || outDir.sqrMagnitude < 0.01f ? 0f : Vector3.Angle(inDir, outDir);
            // 折返的尖刺 (例如手繪 Spline 結尾的小勾) 直接略過這個點
            if (turn > 120f) continue;
            if (turn < 10f)
            {
                result.Add(b);
                continue;
            }

            float limit = Mathf.Min(maxRadius, Vector3.Distance(a, b) * 0.45f, Vector3.Distance(b, c) * 0.45f);
            bool placed = false;
            for (float radius = limit; radius >= minRadius && !placed; radius -= 1.5f)
            {
                Vector3 p1 = b + (a - b).normalized * radius;
                Vector3 p2 = b + (c - b).normalized * radius;
                List<Vector3> curve = new List<Vector3>();
                bool clear = true;
                for (int s = 0; s <= 10; s++)
                {
                    float t = s / 10f;
                    Vector3 point = (1f - t) * (1f - t) * p1 + 2f * (1f - t) * t * b + t * t * p2;
                    if (Physics.CheckSphere(point, clearRadius, ~0, QueryTriggerInteraction.Ignore))
                    {
                        clear = false;
                        break;
                    }
                    curve.Add(point);
                }
                if (!clear) continue;
                result.AddRange(curve);
                placed = true;
            }
            if (!placed) result.Add(b);
        }
        result.Add(points[points.Count - 1]);
        return result;
    }

    /// <summary>
    /// Douglas-Peucker 簡化：把密集取樣的路線還原成「轉折點」，
    /// 手繪 Spline 的尖角才能交給 <see cref="RoundCorners"/> 圓角 (每 1m 一點的路線無法直接圓角)。
    /// </summary>
    public static List<Vector3> Simplify(IList<Vector3> points, float tolerance)
    {
        List<Vector3> result = new List<Vector3>();
        if (points.Count < 3)
        {
            result.AddRange(points);
            return result;
        }

        bool[] keep = new bool[points.Count];
        keep[0] = keep[points.Count - 1] = true;
        Stack<(int start, int end)> ranges = new Stack<(int, int)>();
        ranges.Push((0, points.Count - 1));
        while (ranges.Count > 0)
        {
            (int start, int end) = ranges.Pop();
            float maxDistance = 0f;
            int index = -1;
            for (int i = start + 1; i < end; i++)
            {
                float distance = DistanceToSegment(points[i], points[start], points[end]);
                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    index = i;
                }
            }
            if (index < 0 || maxDistance <= tolerance) continue;
            keep[index] = true;
            ranges.Push((start, index));
            ranges.Push((index, end));
        }

        for (int i = 0; i < points.Count; i++)
        {
            if (keep[i]) result.Add(points[i]);
        }
        return result;
    }

    private static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude) : 0f;
        return Vector3.Distance(point, a + ab * t);
    }

    /// <param name="path">世界座標路線 (會先重新取樣為每 1m 一點)</param>
    /// <param name="firstDistance">起點到第一個檢查圈的距離</param>
    /// <param name="spacing">直線路段上相鄰檢查圈的距離</param>
    /// <param name="maxTurnDegrees">相鄰檢查圈之間最大方向變化 (超過就在中間加一個檢查圈)</param>
    public static List<Gate> PlaceGates(IList<Vector3> path, float firstDistance, float spacing, float maxTurnDegrees, float radius, int seed)
    {
        List<Gate> gates = new List<Gate>();
        List<Vector3> samples = Resample(path, 1f);
        if (samples.Count < 3) return gates;

        float length = samples.Count - 1;
        // 急彎處允許較密的檢查圈 (直線段仍依 spacing)
        float minSpacing = Mathf.Max(radius * 2f, 3.5f);
        System.Random random = new System.Random(seed);

        List<float> distances = new List<float>();
        float last = -1f;
        Vector3 lastHeading = Vector3.zero;
        for (float d = firstDistance; d <= length - 1f; d += 1f)
        {
            Vector3 heading = Flat(TangentAt(samples, d));
            if (last < 0f)
            {
                distances.Add(d);
                last = d;
                lastHeading = heading;
                continue;
            }

            float gap = d - last;
            bool turned = gap >= minSpacing && Vector3.Angle(lastHeading, heading) >= maxTurnDegrees;
            if (gap >= spacing || turned)
            {
                distances.Add(d);
                last = d;
                lastHeading = heading;
            }
        }
        // 終點圈
        if (distances.Count == 0 || length - 1f - distances[distances.Count - 1] > minSpacing * 0.6f) distances.Add(length - 1f);

        // 先決定位置 (錯落/貼近障礙)，再決定朝向
        List<Vector3> positions = new List<Vector3>(distances.Count);
        for (int i = 0; i < distances.Count; i++)
        {
            float d = distances[i];
            Vector3 flat = Flat(TangentAt(samples, d));
            if (flat.sqrMagnitude < 0.0001f) flat = Vector3.forward;

            Vector3 position = Unblock(ClampAboveGround(PointAt(samples, d), radius), flat, radius);
            bool isEndpoint = i == 0 || i == distances.Count - 1;
            if (!isEndpoint)
            {
                // 偏移量隨相鄰檢查圈距離縮放：急彎處檢查圈很密，不能左右亂跳造成之字形
                float room = Mathf.Min(d - distances[i - 1], distances[i + 1] - d);
                position = Stagger(position, flat, radius, i, Mathf.Clamp01(room / 15f), random);
            }
            positions.Add(position);
        }

        // 朝向 = 「上一個 → 下一個」檢查圈的連線方向：飛行路線平順，相鄰檢查圈的朝向變化約為轉角的一半
        for (int i = 0; i < positions.Count; i++)
        {
            Vector3 from = i > 0 ? positions[i - 1] : PointAt(samples, Mathf.Max(0f, distances[i] - 4f));
            Vector3 to = i + 1 < positions.Count ? positions[i + 1] : PointAt(samples, distances[i] + 4f);
            Vector3 chord = to - from;
            Vector3 flat = Flat(chord);
            if (flat.sqrMagnitude < 0.0001f) flat = Flat(TangentAt(samples, distances[i]));
            if (flat.sqrMagnitude < 0.0001f) flat = Vector3.forward;
            float pitch = Mathf.Clamp(Mathf.Atan2(chord.y, flat.magnitude) * Mathf.Rad2Deg, -20f, 20f);
            Quaternion rotation = Quaternion.LookRotation(flat.normalized) * Quaternion.Euler(-pitch, 0f, 0f);
            gates.Add(new Gate { Position = positions[i], Rotation = rotation });
        }
        return gates;
    }

    #region 錯落配置

    /// <summary>
    /// 每三個檢查圈中有一個貼近障礙 (側邊建築/路燈，或低空貼近地面)，其餘左右、高低交錯偏移。
    /// 偏移後若和任何碰撞體重疊就退回較小的偏移。
    /// </summary>
    private static Vector3 Stagger(Vector3 position, Vector3 forward, float radius, int index, float amount, System.Random random)
    {
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 candidate;

        // 貼近障礙只用在檢查圈間距夠大的路段，急彎處的密集檢查圈維持在路線上
        if (index % 3 == 2 && amount >= 0.8f)
        {
            candidate = Vector3.Lerp(position, HugObstacle(position, right, radius, random), Mathf.Max(amount, 0.5f));
        }
        else
        {
            float side = index % 2 == 0 ? 1f : -1f;
            float lateral = side * Mathf.Lerp(0.8f, 2f, (float)random.NextDouble()) * amount;
            float vertical = Mathf.Lerp(-1.5f, 1.5f, (float)random.NextDouble()) * amount;
            candidate = position + right * lateral + Vector3.up * vertical;
        }

        candidate = ClampAboveGround(candidate, radius);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (!Physics.CheckSphere(candidate, radius * 1.1f, ~0, QueryTriggerInteraction.Ignore)) return candidate;
            candidate = Vector3.Lerp(position, candidate, 0.5f);
        }
        return position;
    }

    private static Vector3 HugObstacle(Vector3 position, Vector3 right, float radius, System.Random random)
    {
        const float search = 9f;
        float margin = radius + 0.7f;
        float best = float.MaxValue;
        Vector3 bestDirection = Vector3.zero;
        foreach (Vector3 direction in new[] { right, -right })
        {
            if (Physics.Raycast(position, direction, out RaycastHit hit, search, ~0, QueryTriggerInteraction.Ignore) && hit.distance < best)
            {
                best = hit.distance;
                bestDirection = direction;
            }
        }

        if (bestDirection != Vector3.zero && best > margin)
        {
            // 側邊有建築/路燈：貼過去，只留一點點空間
            return position + bestDirection * Mathf.Min(best - margin, 5f);
        }

        // 兩側都很空：改為壓低貼近地面
        if (Physics.Raycast(position + Vector3.up, Vector3.down, out RaycastHit ground, 40f, ~0, QueryTriggerInteraction.Ignore))
        {
            float low = ground.point.y + radius + 0.9f + (float)random.NextDouble() * 0.6f;
            if (low < position.y) return new Vector3(position.x, low, position.z);
        }
        return position;
    }

    /// <summary>檢查圈若碰到路燈/號誌/建築，往左右或上方微調到淨空的位置 (轉角圓弧靠近街角設施時使用)</summary>
    private static Vector3 Unblock(Vector3 position, Vector3 forward, float radius)
    {
        float check = radius * 1.05f;
        if (!Physics.CheckSphere(position, check, ~0, QueryTriggerInteraction.Ignore)) return position;

        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        for (float offset = 0.5f; offset <= 4f; offset += 0.5f)
        {
            foreach (Vector3 direction in new[] { right, -right, Vector3.up })
            {
                Vector3 candidate = position + direction * offset;
                if (!Physics.CheckSphere(candidate, check, ~0, QueryTriggerInteraction.Ignore)) return candidate;
            }
        }
        return position;
    }

    private static Vector3 ClampAboveGround(Vector3 position, float radius)
    {
        if (Physics.Raycast(position + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 60f, ~0, QueryTriggerInteraction.Ignore))
        {
            position.y = Mathf.Max(position.y, hit.point.y + radius + 0.8f);
        }
        return position;
    }

    #endregion

    #region 路線取樣

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    private static List<Vector3> Resample(IList<Vector3> path, float step)
    {
        List<Vector3> result = new List<Vector3>();
        if (path.Count == 0) return result;
        result.Add(path[0]);
        float carry = 0f;
        for (int i = 1; i < path.Count; i++)
        {
            Vector3 a = path[i - 1];
            Vector3 b = path[i];
            float segment = Vector3.Distance(a, b);
            if (segment < 0.0001f) continue;
            float d = step - carry;
            while (d <= segment)
            {
                result.Add(Vector3.Lerp(a, b, d / segment));
                d += step;
            }
            carry = segment - (d - step);
        }
        if ((result[result.Count - 1] - path[path.Count - 1]).sqrMagnitude > 0.01f) result.Add(path[path.Count - 1]);
        return result;
    }

    private static Vector3 PointAt(List<Vector3> samples, float d)
    {
        d = Mathf.Clamp(d, 0f, samples.Count - 1);
        int i = Mathf.Min(Mathf.FloorToInt(d), samples.Count - 2);
        return Vector3.Lerp(samples[i], samples[i + 1], d - i);
    }

    private static Vector3 TangentAt(List<Vector3> samples, float d)
    {
        Vector3 tangent = PointAt(samples, d + 2f) - PointAt(samples, d - 2f);
        return tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector3.forward;
    }

    #endregion
}
