using UnityEngine;

/// <summary>
/// 地圖外圍空氣牆：四面牆 + 天花板，皆為無外觀的 BoxCollider，讓無人機無法飛出可遊玩範圍。
/// 由關卡生成器在執行時建立 (程序化城市、訓練場)；City2 場景已手動放置 BoundWall。
/// </summary>
public static class MapBoundary
{
    public const string RootName = "MapBoundary";
    private const float Thickness = 5f;

    /// <summary>
    /// 依可遊玩範圍 (XZ 最小/最大值、地面高度、天花板高度) 建立空氣牆。
    /// 牆的內側面剛好落在範圍邊緣，往下多延伸一段避免從地面縫隙穿出。
    /// </summary>
    public static GameObject Build(Transform parent, Vector2 min, Vector2 max, float groundY, float ceilingY)
    {
        Transform existing = parent != null ? parent.Find(RootName) : null;
        if (existing != null) Object.Destroy(existing.gameObject);

        GameObject root = new GameObject(RootName);
        if (parent != null) root.transform.SetParent(parent, false);
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        float bottom = groundY - 10f;
        float height = ceilingY - bottom;
        float midY = bottom + height * 0.5f;
        float sizeX = max.x - min.x + Thickness * 2f;
        float sizeZ = max.y - min.y + Thickness * 2f;
        float centerX = (min.x + max.x) * 0.5f;
        float centerZ = (min.y + max.y) * 0.5f;

        AddWall(root.transform, "Wall_West", new Vector3(min.x - Thickness * 0.5f, midY, centerZ), new Vector3(Thickness, height, sizeZ));
        AddWall(root.transform, "Wall_East", new Vector3(max.x + Thickness * 0.5f, midY, centerZ), new Vector3(Thickness, height, sizeZ));
        AddWall(root.transform, "Wall_South", new Vector3(centerX, midY, min.y - Thickness * 0.5f), new Vector3(sizeX, height, Thickness));
        AddWall(root.transform, "Wall_North", new Vector3(centerX, midY, max.y + Thickness * 0.5f), new Vector3(sizeX, height, Thickness));
        AddWall(root.transform, "Ceiling", new Vector3(centerX, ceilingY + Thickness * 0.5f, centerZ), new Vector3(sizeX, Thickness, sizeZ));
        return root;
    }

    private static void AddWall(Transform root, string wallName, Vector3 center, Vector3 size)
    {
        GameObject wall = new GameObject(wallName);
        wall.transform.SetParent(root, false);
        wall.transform.position = center;
        BoxCollider box = wall.AddComponent<BoxCollider>();
        box.size = size;
        wall.isStatic = true;
    }
}
