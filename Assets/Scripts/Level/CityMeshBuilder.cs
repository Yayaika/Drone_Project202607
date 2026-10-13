using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 把大量方塊/柱體合併成少數網格 (每種材質一個子網格)，用於程序化街景：
/// 道路標線、騎樓透天厝、停放機車等。避免每個小零件各自成為 GameObject，生成快且繪製呼叫少。
/// 座標一律以世界座標加入，<see cref="Build"/> 產生的物件放在世界原點。
/// UV 以世界座標依面法線投影 (類似三平面映射)，平鋪尺寸可依材質用 <see cref="SetTiling"/> 設定，貼圖不會被拉伸。
/// </summary>
public class CityMeshBuilder
{
    private class Part
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<int> Triangles = new List<int>();
        public float Tiling = 1f;
    }

    private readonly Dictionary<Material, Part> parts = new Dictionary<Material, Part>();
    private readonly List<Material> order = new List<Material>();

    private readonly Dictionary<Material, float> tilings = new Dictionary<Material, float>();

    public bool IsEmpty => order.Count == 0;

    /// <summary>設定材質的貼圖平鋪尺寸 (一張貼圖對應幾公尺)</summary>
    public void SetTiling(Material material, float metersPerTile)
    {
        if (material == null) return;
        tilings[material] = Mathf.Max(0.01f, metersPerTile);
        if (parts.TryGetValue(material, out Part part)) part.Tiling = tilings[material];
    }

    private static Vector2 ProjectUv(Vector3 position, Vector3 normal, float tiling)
    {
        Vector3 n = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
        Vector2 uv = n.y >= n.x && n.y >= n.z ? new Vector2(position.x, position.z)
            : n.x >= n.z ? new Vector2(position.z, position.y)
            : new Vector2(position.x, position.y);
        return uv / tiling;
    }

    private Part PartFor(Material material)
    {
        if (!parts.TryGetValue(material, out Part part))
        {
            part = new Part();
            if (tilings.TryGetValue(material, out float tiling)) part.Tiling = tiling;
            parts[material] = part;
            order.Add(material);
        }
        return part;
    }

    /// <summary>在 frame 座標系中加入一個方塊 (center/size 為 frame 的區域座標)</summary>
    public void AddBox(Matrix4x4 frame, Vector3 center, Vector3 size, Material material)
    {
        if (material == null) return;
        Part part = PartFor(material);
        Vector3 h = size * 0.5f;

        AddQuad(part, frame, center, Vector3.forward, Vector3.right, Vector3.up, h.z, h.x, h.y);
        AddQuad(part, frame, center, Vector3.back, Vector3.left, Vector3.up, h.z, h.x, h.y);
        AddQuad(part, frame, center, Vector3.right, Vector3.back, Vector3.up, h.x, h.z, h.y);
        AddQuad(part, frame, center, Vector3.left, Vector3.forward, Vector3.up, h.x, h.z, h.y);
        AddQuad(part, frame, center, Vector3.up, Vector3.right, Vector3.back, h.y, h.x, h.z);
        AddQuad(part, frame, center, Vector3.down, Vector3.right, Vector3.forward, h.y, h.x, h.z);
    }

    /// <summary>世界座標、無旋轉的方塊</summary>
    public void AddBox(Vector3 center, Vector3 size, Material material)
    {
        AddBox(Matrix4x4.identity, center, size, material);
    }

    /// <summary>
    /// 加入一個柱體：frame 區域座標中，以 baseCenter 為底面中心、沿 axis (區域座標，須為單位向量) 延伸 height。
    /// </summary>
    public void AddCylinder(Matrix4x4 frame, Vector3 baseCenter, Vector3 axis, float radius, float height, int sides, Material material)
    {
        if (material == null || sides < 3) return;
        Part part = PartFor(material);

        axis = axis.normalized;
        Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
        Vector3 side2 = Vector3.Cross(axis, side);
        Vector3 top = baseCenter + axis * height;

        for (int i = 0; i < sides; i++)
        {
            float a0 = i * Mathf.PI * 2f / sides;
            float a1 = (i + 1) * Mathf.PI * 2f / sides;
            Vector3 d0 = side * Mathf.Cos(a0) + side2 * Mathf.Sin(a0);
            Vector3 d1 = side * Mathf.Cos(a1) + side2 * Mathf.Sin(a1);
            Vector3 n = (d0 + d1).normalized;

            // 側面
            AddRawQuad(part, frame,
                baseCenter + d0 * radius, baseCenter + d1 * radius, top + d1 * radius, top + d0 * radius, n);
            // 頂面 / 底面三角形
            AddRawTriangle(part, frame, top, top + d0 * radius, top + d1 * radius, axis);
            AddRawTriangle(part, frame, baseCenter, baseCenter + d1 * radius, baseCenter + d0 * radius, -axis);
        }
    }

    private static void AddQuad(Part part, Matrix4x4 frame, Vector3 center, Vector3 normal, Vector3 tangent, Vector3 bitangent,
        float normalExtent, float tangentExtent, float bitangentExtent)
    {
        Vector3 c = center + normal * normalExtent;
        Vector3 t = tangent * tangentExtent;
        Vector3 b = bitangent * bitangentExtent;
        AddRawQuad(part, frame, c - t - b, c + t - b, c + t + b, c - t + b, normal);
    }

    /// <summary>四點依序環繞即可，繞序會依法線自動校正 (Unity 正面 = Cross(b-a, c-a) 朝向法線)</summary>
    private static void AddRawQuad(Part part, Matrix4x4 frame, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
    {
        int start = part.Vertices.Count;
        Vector3 worldNormal = frame.MultiplyVector(normal).normalized;
        part.Vertices.Add(frame.MultiplyPoint3x4(a));
        part.Vertices.Add(frame.MultiplyPoint3x4(b));
        part.Vertices.Add(frame.MultiplyPoint3x4(c));
        part.Vertices.Add(frame.MultiplyPoint3x4(d));
        for (int i = 0; i < 4; i++)
        {
            part.Normals.Add(worldNormal);
            part.Uvs.Add(ProjectUv(part.Vertices[start + i], worldNormal, part.Tiling));
        }

        // 依實際幾何決定繞序，確保正面朝向法線
        Vector3 cross = Vector3.Cross(part.Vertices[start + 1] - part.Vertices[start], part.Vertices[start + 2] - part.Vertices[start]);
        if (Vector3.Dot(cross, worldNormal) > 0f)
        {
            part.Triangles.Add(start); part.Triangles.Add(start + 1); part.Triangles.Add(start + 2);
            part.Triangles.Add(start); part.Triangles.Add(start + 2); part.Triangles.Add(start + 3);
        }
        else
        {
            part.Triangles.Add(start); part.Triangles.Add(start + 2); part.Triangles.Add(start + 1);
            part.Triangles.Add(start); part.Triangles.Add(start + 3); part.Triangles.Add(start + 2);
        }
    }

    private static void AddRawTriangle(Part part, Matrix4x4 frame, Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
    {
        int start = part.Vertices.Count;
        Vector3 worldNormal = frame.MultiplyVector(normal).normalized;
        part.Vertices.Add(frame.MultiplyPoint3x4(a));
        part.Vertices.Add(frame.MultiplyPoint3x4(b));
        part.Vertices.Add(frame.MultiplyPoint3x4(c));
        for (int i = 0; i < 3; i++)
        {
            part.Normals.Add(worldNormal);
            part.Uvs.Add(ProjectUv(part.Vertices[start + i], worldNormal, part.Tiling));
        }

        Vector3 cross = Vector3.Cross(part.Vertices[start + 1] - part.Vertices[start], part.Vertices[start + 2] - part.Vertices[start]);
        if (Vector3.Dot(cross, worldNormal) > 0f)
        {
            part.Triangles.Add(start); part.Triangles.Add(start + 1); part.Triangles.Add(start + 2);
        }
        else
        {
            part.Triangles.Add(start); part.Triangles.Add(start + 2); part.Triangles.Add(start + 1);
        }
    }

    /// <summary>產生合併網格 (每種材質一個子網格)。materials 依子網格順序回傳。</summary>
    public Mesh BuildMesh(string meshName, out Material[] materials)
    {
        Mesh mesh = new Mesh { name = meshName, indexFormat = IndexFormat.UInt32 };
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        foreach (Material material in order)
        {
            vertices.AddRange(parts[material].Vertices);
            normals.AddRange(parts[material].Normals);
            uvs.AddRange(parts[material].Uvs);
        }
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = order.Count;

        int offset = 0;
        for (int i = 0; i < order.Count; i++)
        {
            Part part = parts[order[i]];
            int[] triangles = new int[part.Triangles.Count];
            for (int t = 0; t < triangles.Length; t++) triangles[t] = part.Triangles[t] + offset;
            mesh.SetTriangles(triangles, i, false);
            offset += part.Vertices.Count;
        }
        mesh.RecalculateBounds();
        materials = order.ToArray();
        return mesh;
    }

    /// <summary>建立網格物件並放到 parent 底下 (物件位於世界原點)</summary>
    public GameObject Build(string objectName, Transform parent, bool castShadows = true)
    {
        if (IsEmpty) return null;

        Mesh mesh = BuildMesh(objectName, out Material[] materials);
        GameObject target = new GameObject(objectName, typeof(MeshFilter), typeof(MeshRenderer));
        target.transform.SetParent(parent, false);
        target.transform.position = Vector3.zero;
        target.transform.rotation = Quaternion.identity;
        target.transform.localScale = Vector3.one;
        target.GetComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer meshRenderer = target.GetComponent<MeshRenderer>();
        meshRenderer.sharedMaterials = materials;
        meshRenderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        target.isStatic = true;
        return target;
    }

    public void Clear()
    {
        parts.Clear();
        order.Clear();
    }
}
