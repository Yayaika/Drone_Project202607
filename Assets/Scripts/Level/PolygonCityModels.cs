using UnityEngine;

/// <summary>
/// POLYGON city pack 建築模型的共用處理 (CityGenerator 與 OrganicCityGenerator 共用)：
/// 1. 模型根節點帶 X 270° 旋轉 (FBX 匯出慣例)，量測與擺放時必須保留，否則模型會倒地、縮放算錯。
/// 2. 依「門」子網格法線判斷模型正面。
/// 3. 素材包 FBX 匯入後材質皆為預設灰色，依子網格材質名稱改套素材包配色 (只改實例，不改素材包資產)。
/// </summary>
public static class PolygonCityModels
{
    public struct Measurement
    {
        public Vector3 Size;
        public Vector3 Pivot;   // 底部中心 (含模型根節點旋轉的座標)
        public float FrontYaw;  // 模型正面方向，0 = +Z，為 90 的倍數
        public bool Valid;
    }

    /// <summary>依子網格名稱分類後要套用的材質；欄位為 null 時保留原材質</summary>
    public class Palette
    {
        public Material Facade;
        public Material Roof;
        public Material Window;
        public Material Door;
        public Material Trim;
        public Material Accent;
        public Material Grass;
        public Material Pavement;
        /// <summary>招牌子網格 (名稱以 Texture 開頭) 使用的模型專屬貼圖材質</summary>
        public Material Sign;
    }

    public enum Slot { Facade, Window, Roof, Door, Trim, Accent, Grass, Pavement }

    /// <summary>在原點暫時生成模型 (保留根節點旋轉) 以量測尺寸、底部中心與正面方向</summary>
    public static Measurement Measure(GameObject prefab)
    {
        GameObject probe = Object.Instantiate(prefab);
        probe.transform.position = Vector3.zero;

        Measurement result = default;
        Renderer[] renderers = probe.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            result.Size = bounds.size;
            result.Pivot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            result.FrontYaw = DetectFrontYaw(probe);
            result.Valid = bounds.size.x > 0.5f && bounds.size.y > 0.5f && bounds.size.z > 0.5f;
        }

        Object.DestroyImmediate(probe);
        return result;
    }

    /// <summary>以「門」子網格的法線平均方向判斷模型正面 (沒有門時改用招牌)，取最接近的 90 度</summary>
    public static float DetectFrontYaw(GameObject probe)
    {
        foreach (string keyword in new[] { "door", "texture", "sign" })
        {
            Vector3 sum = Vector3.zero;
            foreach (MeshFilter filter in probe.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = filter.sharedMesh;
                Renderer renderer = filter.GetComponent<Renderer>();
                if (mesh == null || renderer == null || (!mesh.isReadable && !Application.isEditor)) continue;

                Vector3[] normals = mesh.normals;
                if (normals.Length == 0) continue;
                Material[] materials = renderer.sharedMaterials;
                for (int sub = 0; sub < mesh.subMeshCount && sub < materials.Length; sub++)
                {
                    if (materials[sub] == null || !materials[sub].name.ToLowerInvariant().Contains(keyword)) continue;
                    foreach (int index in mesh.GetTriangles(sub))
                    {
                        sum += filter.transform.TransformDirection(normals[index]);
                    }
                }
            }

            sum.y = 0f;
            if (sum.sqrMagnitude > 1e-4f)
            {
                return Mathf.Round(Mathf.Atan2(sum.x, sum.z) * Mathf.Rad2Deg / 90f) * 90f;
            }
        }
        return 0f;
    }

    /// <summary>依子網格材質名稱換上調色盤材質</summary>
    public static void ApplyMaterials(GameObject model, Palette palette)
    {
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null) continue;

                // 素材包只有名為 Texture 的子網格 (招牌、看板) 使用模型專屬貼圖，其他面為純色
                if (palette.Sign != null && materials[i].name.StartsWith("Texture", System.StringComparison.OrdinalIgnoreCase))
                {
                    materials[i] = palette.Sign;
                    continue;
                }

                Material replacement;
                switch (Classify(materials[i].name))
                {
                    case Slot.Grass: replacement = palette.Grass; break;
                    case Slot.Pavement: replacement = palette.Pavement; break;
                    case Slot.Window: replacement = palette.Window; break;
                    case Slot.Roof: replacement = palette.Roof; break;
                    case Slot.Door: replacement = palette.Door; break;
                    case Slot.Trim: replacement = palette.Trim; break;
                    case Slot.Accent: replacement = palette.Accent; break;
                    default: replacement = palette.Facade; break;
                }
                if (replacement != null) materials[i] = replacement;
            }
            renderer.sharedMaterials = materials;
        }
    }

    public static Slot Classify(string materialName)
    {
        string name = materialName.ToLowerInvariant();
        if (name.Contains("grass")) return Slot.Grass;
        if (name.Contains("street") || name.Contains("sideway") || name.Contains("sidewalk")) return Slot.Pavement;
        if (name.Contains("frame") || name.Contains("board") || name.Contains("metal") || name.Contains("pole") ||
            name.Contains("rail") || name.Contains("fire") || name.Contains("stair") || name.Contains("grid") ||
            name.Contains("holder") || name.Contains("lines") || name.Contains("filter") || name.Contains("black") ||
            name.Contains("platt") || name.Contains("box") || name.Contains("goals") || name.Contains("sign"))
        {
            return Slot.Trim;
        }
        if (name.Contains("window")) return Slot.Window;
        if (name.Contains("roof") || name.Contains("floor")) return Slot.Roof;
        if (name.Contains("door") || name.Contains("wood") || name.Contains("garage") || name.Contains("drive")) return Slot.Door;
        if (name.Contains("red") || name.Contains("blue") || name.Contains("green") || name.Contains("brown")) return Slot.Accent;
        return Slot.Facade;
    }

    /// <summary>兩個角度相差約 90/270 度 (模型側向擺放，寬深互換)</summary>
    public static bool IsSideways(float yaw)
    {
        return Mathf.Abs(Mathf.DeltaAngle(yaw, 90f)) < 45f || Mathf.Abs(Mathf.DeltaAngle(yaw, 270f)) < 45f;
    }
}
