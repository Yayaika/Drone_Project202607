using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// 豐富 City2 場景 (選單：無人機遊戲/10)：自動辨識道路、人行道與空地，擺放
/// 路燈、行道樹、街道設施 (長椅/消防栓/郵筒…)、路邊停車、停車場與小綠地。
/// - 不擋賽道：與所有可抽中的賽道 Spline 保持水平距離，不放在起飛點附近
/// - 不穿模：放置後以 OverlapBox 檢查，碰到建築或其他物件就移除
/// - 可重複執行：每次先刪除舊的「City2_Decor」再重新生成 (固定亂數種子，結果相同)
/// </summary>
public static class City2Decorator
{
    private const string ScenePath = "Assets/Scenes/City2.unity";
    private const string RootName = "City2_Decor";
    private const float Cell = 1f;
    private const float RouteClearance = 3.5f;

    private enum Ground : byte { Blocked, Open, Road }

    private static Ground[,] ground;
    private static int[,] roadDistance;
    private static bool[,] routeMask;
    private static Vector3 origin;
    private static int nx, nz;
    private static System.Random random;
    private static Transform root;
    private static readonly List<(Vector3 position, float radius)> occupied = new List<(Vector3, float)>();
    private static Dictionary<Material, Material> urpRemap;

    [MenuItem("無人機遊戲/10. 豐富 City2 場景物件", priority = 10)]
    private static void DecorateFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorUtility.DisplayDialog("City2 場景物件", Decorate(), "確定");
    }

    public static string Decorate()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        GameObject old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);
        Physics.SyncTransforms();

        random = new System.Random(2026);
        occupied.Clear();
        urpRemap = CityLevelBaker.StreetPackUrpRemap();
        root = new GameObject(RootName).transform;
        root.gameObject.isStatic = true;

        if (!BuildGroundMap(out string error)) return error;

        int lamps = PlaceLamps();
        int trees = PlaceStreetTrees();
        int props = PlaceSidewalkProps();
        int curbCars = PlaceCurbCars();
        (int lotCars, int lotPlants, int parks) = DecorateLots();

        Scene();
        return $"City2 新增：路燈 {lamps}、行道樹 {trees}、街道設施 {props}、路邊停車 {curbCars}、停車場車輛 {lotCars}、空地植物 {lotPlants}（{parks} 個小綠地）";
    }

    private static void Scene()
    {
        UnityEngine.SceneManagement.Scene scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    #region 地面辨識

    private static bool BuildGroundMap(out string error)
    {
        error = null;
        GameObject walls = GameObject.Find("BoundWall");
        if (walls == null)
        {
            error = "找不到 BoundWall，無法判斷 City2 範圍";
            return false;
        }

        Collider[] wallColliders = walls.GetComponentsInChildren<Collider>(true);
        Bounds bounds = wallColliders[0].bounds;
        foreach (Collider wall in wallColliders) bounds.Encapsulate(wall.bounds);
        // 牆厚約 10m，往內縮
        Vector3 min = bounds.min + new Vector3(11f, 0f, 11f);
        Vector3 max = bounds.max - new Vector3(11f, 0f, 11f);
        origin = new Vector3(min.x, 0f, min.z);
        nx = Mathf.CeilToInt((max.x - min.x) / Cell);
        nz = Mathf.CeilToInt((max.z - min.z) / Cell);

        ground = new Ground[nx, nz];
        List<Bounds> roads = RoadTiles();
        for (int x = 0; x < nx; x++)
        {
            for (int z = 0; z < nz; z++)
            {
                Vector3 p = CellCenter(x, z);
                if (!GroundHit(p, walls.transform, out RaycastHit hit) || hit.point.y > 0.35f)
                {
                    ground[x, z] = Ground.Blocked;
                    continue;
                }
                bool isRoad = roads.Any(r => p.x >= r.min.x && p.x <= r.max.x && p.z >= r.min.z && p.z <= r.max.z);
                ground[x, z] = isRoad ? Ground.Road : Ground.Open;
            }
        }

        MarkExistingVegetation();
        ComputeRoadDistance();
        ComputeRouteMask();
        return true;
    }

    /// <summary>由上往下第一個「不是邊界牆」的碰撞 (City2 邊界牆含一片天花板)</summary>
    private static bool GroundHit(Vector3 p, Transform walls, out RaycastHit result)
    {
        RaycastHit[] hits = Physics.RaycastAll(p + Vector3.up * 150f, Vector3.down, 300f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(walls)) continue;
            result = hit;
            return true;
        }
        result = default;
        return false;
    }

    /// <summary>道路 = Street 7 Prefab 與路口 (Intersection) 底下的路面貼片</summary>
    private static List<Bounds> RoadTiles()
    {
        List<Bounds> tiles = new List<Bounds>();
        foreach (GameObject top in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (!top.name.StartsWith("Street 7 Prefab") && !top.name.StartsWith("Intersection")) continue;
            foreach (Renderer renderer in top.GetComponentsInChildren<Renderer>())
            {
                if (renderer.bounds.size.y < 0.5f) tiles.Add(renderer.bounds);
            }
        }
        return tiles;
    }

    /// <summary>既有植物多半沒有碰撞體，射線偵測不到，依外觀範圍標成不可放置</summary>
    private static void MarkExistingVegetation()
    {
        GameObject vegetation = GameObject.Find("AnimeVegetation");
        if (vegetation == null) return;
        foreach (Renderer renderer in vegetation.GetComponentsInChildren<Renderer>())
        {
            Bounds b = renderer.bounds;
            for (float x = b.min.x; x <= b.max.x; x += Cell)
            {
                for (float z = b.min.z; z <= b.max.z; z += Cell)
                {
                    if (ToCell(new Vector3(x, 0f, z), out int cx, out int cz)) ground[cx, cz] = Ground.Blocked;
                }
            }
        }
    }

    private static void ComputeRoadDistance()
    {
        roadDistance = new int[nx, nz];
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        for (int x = 0; x < nx; x++)
        {
            for (int z = 0; z < nz; z++)
            {
                roadDistance[x, z] = ground[x, z] == Ground.Road ? 0 : int.MaxValue;
                if (ground[x, z] == Ground.Road) queue.Enqueue(new Vector2Int(x, z));
            }
        }
        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            foreach (Vector2Int n in Neighbours(c))
            {
                if (roadDistance[n.x, n.y] != int.MaxValue) continue;
                roadDistance[n.x, n.y] = roadDistance[c.x, c.y] + 1;
                queue.Enqueue(n);
            }
        }
    }

    /// <summary>所有可抽中的賽道路線附近 (水平 3.5m) 與起飛點附近不放東西</summary>
    private static void ComputeRouteMask()
    {
        routeMask = new bool[nx, nz];
        List<Vector3> keepOut = new List<Vector3>();
        RaceManager race = Object.FindFirstObjectByType<RaceManager>();
        if (race != null && race.availableSplines != null)
        {
            foreach (SplineContainer spline in race.availableSplines)
            {
                if (spline == null || spline.CalculateLength() < race.minRouteLength) continue;
                float length = spline.CalculateLength();
                for (float d = 0f; d <= length; d += 1f) keepOut.Add((Vector3)spline.EvaluatePosition(d / length));
            }
        }
        foreach (GameObject top in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (top.name.StartsWith("StarPoint") || top.name == "Spawner")
            {
                for (float a = 0f; a < 360f; a += 30f)
                {
                    for (float r = 0f; r <= 6f; r += 1.5f) keepOut.Add(top.transform.position + Quaternion.Euler(0f, a, 0f) * Vector3.forward * r);
                }
            }
        }

        int reach = Mathf.CeilToInt(RouteClearance / Cell);
        foreach (Vector3 point in keepOut)
        {
            if (!ToCell(point, out int cx, out int cz)) continue;
            for (int x = cx - reach; x <= cx + reach; x++)
            {
                for (int z = cz - reach; z <= cz + reach; z++)
                {
                    if (!InGrid(x, z)) continue;
                    if (Vector2.Distance(new Vector2(x, z), new Vector2(cx, cz)) * Cell <= RouteClearance) routeMask[x, z] = true;
                }
            }
        }
    }

    #endregion

    #region 人行道

    private static int PlaceLamps()
    {
        GameObject[] prefabs = Prefabs(CityLevelBaker.StreetPrefabs("StreetProps/TraficLights", "LampPost_D", "LampPost_J"));
        int count = 0;
        foreach (Vector2Int c in Shuffled(Cells(Ground.Open, 1, 1)))
        {
            Vector3 p = CellCenter(c.x, c.y);
            if (routeMask[c.x, c.y] || NearOccupied(p, 16f)) continue;
            Vector3 toRoad = DirectionToRoad(c);
            if (toRoad == Vector3.zero) continue;
            GameObject prefab = Pick(prefabs);
            if (prefab == null) break;
            Quaternion rotation = Quaternion.LookRotation(Vector3.Cross(toRoad, Vector3.up), Vector3.up) * prefab.transform.rotation;
            if (PlaceStreetProp(prefab, p, rotation, "Lamps") != null)
            {
                occupied.Add((p, 1.5f));
                count++;
            }
        }
        return count;
    }

    private static int PlaceStreetTrees()
    {
        GameObject[] prefabs = Prefabs(CityLevelBaker.NaturePrefabs("Tree_02", "Tree_03", "Tree_04", "Tree_05", "Tree_06", "Tree_07", "Tree_08"));
        int count = 0;
        foreach (Vector2Int c in Shuffled(Cells(Ground.Open, 2, 3)))
        {
            Vector3 p = CellCenter(c.x, c.y);
            if (routeMask[c.x, c.y] || NearOccupied(p, 9f)) continue;
            if (PlaceTree(Pick(prefabs), p, Range(6f, 8.5f), "StreetTrees") != null)
            {
                occupied.Add((p, 2f));
                count++;
            }
        }
        return count;
    }

    private static int PlaceSidewalkProps()
    {
        GameObject[] prefabs = Prefabs(CityLevelBaker.StreetPrefabs("StreetProps", "Hidrant", "Bench_", "MailBox", "FuseBox", "NewsBoard", "Phone"));
        int count = 0;
        foreach (Vector2Int c in Shuffled(Cells(Ground.Open, 1, 2)))
        {
            Vector3 p = CellCenter(c.x, c.y);
            if (NearOccupied(p, 7f)) continue;
            Vector3 toRoad = DirectionToRoad(c);
            if (toRoad == Vector3.zero) continue;
            GameObject prefab = Pick(prefabs);
            // 長椅/郵筒等正面朝向馬路
            Quaternion rotation = Quaternion.LookRotation(toRoad, Vector3.up) * prefab.transform.rotation;
            if (PlaceStreetProp(prefab, p, rotation, "SidewalkProps") != null)
            {
                occupied.Add((p, 1f));
                count++;
            }
        }
        return count;
    }

    /// <summary>沿路緣停車：車道最外側、與賽道保持距離，車頭順著道路方向</summary>
    private static int PlaceCurbCars()
    {
        GameObject[] cars = CarPrefabs();
        int count = 0;
        foreach (Vector2Int c in Shuffled(CurbCells().ToList()))
        {
            Vector3 p = CellCenter(c.x, c.y);
            if (routeMask[c.x, c.y] || NearOccupied(p, 14f) || random.NextDouble() < 0.45) continue;
            Vector3 toSidewalk = DirectionToOpen(c);
            if (toSidewalk == Vector3.zero) continue;
            Vector3 along = Vector3.Cross(toSidewalk, Vector3.up);
            Vector3 position = p - toSidewalk * 0.6f;
            if (PlaceCar(Pick(cars), position, Quaternion.LookRotation(random.NextDouble() < 0.5 ? along : -along), "CurbCars"))
            {
                occupied.Add((position, 3f));
                count++;
            }
        }
        return count;
    }

    private static IEnumerable<Vector2Int> CurbCells()
    {
        for (int x = 0; x < nx; x++)
        {
            for (int z = 0; z < nz; z++)
            {
                if (ground[x, z] != Ground.Road) continue;
                if (Neighbours(new Vector2Int(x, z)).Any(n => ground[n.x, n.y] == Ground.Open)) yield return new Vector2Int(x, z);
            }
        }
    }

    #endregion

    #region 空地：停車場 / 小綠地

    private static (int cars, int plants, int parks) DecorateLots()
    {
        GameObject[] carPrefabs = CarPrefabs();
        GameObject[] trees = Prefabs(CityLevelBaker.NaturePrefabs("Tree_02", "Tree_03", "Tree_04", "Tree_05", "Tree_06", "Tree_07", "Tree_08"));
        GameObject[] bushes = Prefabs(CityLevelBaker.NaturePrefabs("Bush_"));
        GameObject[] benches = Prefabs(CityLevelBaker.StreetPrefabs("StreetProps/Bench", "Bench_"));
        GameObject[] parkLamps = Prefabs(CityLevelBaker.StreetPrefabs("StreetProps/ParkLamp", "ParkLamp"));

        int cars = 0, plants = 0, parks = 0;
        bool[,] visited = new bool[nx, nz];
        for (int x = 0; x < nx; x++)
        {
            for (int z = 0; z < nz; z++)
            {
                if (visited[x, z] || !IsLot(x, z)) continue;
                List<Vector2Int> region = FloodLot(x, z, visited);
                if (region.Count < 60) continue;

                bool parking = region.Count >= 350 && random.NextDouble() < 0.7;
                if (parking)
                {
                    cars += FillParking(region, carPrefabs);
                    plants += PlantEdges(region, bushes, 8f, 1.2f, 2f);
                }
                else
                {
                    parks++;
                    plants += PlantEdges(region, bushes, 5f, 1.2f, 2.2f);
                    plants += ScatterInRegion(region, trees, Mathf.Clamp(region.Count / 45, 2, 14), 7f, tree: true);
                    plants += ScatterBushes(region, bushes, Mathf.Clamp(region.Count / 35, 2, 18));
                    ScatterInRegion(region, benches, Mathf.Clamp(region.Count / 150, 1, 3), 6f, tree: false);
                    ScatterInRegion(region, parkLamps, Mathf.Clamp(region.Count / 200, 0, 2), 10f, tree: false);
                }
            }
        }
        return (cars, plants, parks);
    }

    private static bool IsLot(int x, int z) => ground[x, z] == Ground.Open && roadDistance[x, z] >= 5;

    private static List<Vector2Int> FloodLot(int sx, int sz, bool[,] visited)
    {
        List<Vector2Int> region = new List<Vector2Int>();
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(new Vector2Int(sx, sz));
        visited[sx, sz] = true;
        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            region.Add(c);
            foreach (Vector2Int n in Neighbours(c))
            {
                if (visited[n.x, n.y] || !IsLot(n.x, n.y)) continue;
                visited[n.x, n.y] = true;
                queue.Enqueue(n);
            }
        }
        return region;
    }

    /// <summary>停車場：沿區域較長的方向排成一列列車位 (3m × 5.5m)，約七成車位停車</summary>
    private static int FillParking(List<Vector2Int> region, GameObject[] carPrefabs)
    {
        HashSet<Vector2Int> cells = new HashSet<Vector2Int>(region);
        int minX = region.Min(c => c.x), maxX = region.Max(c => c.x), minZ = region.Min(c => c.y), maxZ = region.Max(c => c.y);
        bool rowsAlongX = maxX - minX >= maxZ - minZ;
        int count = 0;

        for (int a = (rowsAlongX ? minZ : minX) + 2; a <= (rowsAlongX ? maxZ : maxX) - 3; a += 7)
        {
            for (int b = (rowsAlongX ? minX : minZ) + 2; b <= (rowsAlongX ? maxX : maxZ) - 2; b += 3)
            {
                Vector2Int c = rowsAlongX ? new Vector2Int(b, a) : new Vector2Int(a, b);
                if (!FootprintInside(cells, c, rowsAlongX) || routeMask[c.x, c.y] || random.NextDouble() < 0.3) continue;
                Vector3 p = CellCenter(c.x, c.y) + (rowsAlongX ? Vector3.forward : Vector3.right) * 2.25f;
                Vector3 facing = rowsAlongX ? Vector3.forward : Vector3.right;
                if (random.NextDouble() < 0.5) facing = -facing;
                if (PlaceCar(Pick(carPrefabs), p, Quaternion.LookRotation(facing), "ParkingLots"))
                {
                    occupied.Add((p, 2.5f));
                    count++;
                }
            }
        }
        return count;
    }

    private static bool FootprintInside(HashSet<Vector2Int> cells, Vector2Int c, bool rowsAlongX)
    {
        for (int i = -1; i <= 1; i++)
        {
            for (int j = 0; j <= 5; j++)
            {
                Vector2Int test = rowsAlongX ? new Vector2Int(c.x + i, c.y + j) : new Vector2Int(c.x + j, c.y + i);
                if (!cells.Contains(test)) return false;
            }
        }
        return true;
    }

    /// <summary>沿空地邊緣種一排灌木 (像樹籬)</summary>
    private static int PlantEdges(List<Vector2Int> region, GameObject[] prefabs, float spacing, float minWidth, float maxWidth)
    {
        HashSet<Vector2Int> cells = new HashSet<Vector2Int>(region);
        int count = 0;
        foreach (Vector2Int c in Shuffled(region.Where(c => Neighbours(c).Any(n => !cells.Contains(n))).ToList()))
        {
            Vector3 p = CellCenter(c.x, c.y);
            if (NearOccupied(p, spacing)) continue;
            if (PlaceBush(Pick(prefabs), p, Range(minWidth, maxWidth), "LotPlants") != null)
            {
                occupied.Add((p, 1f));
                count++;
            }
        }
        return count;
    }

    private static int ScatterInRegion(List<Vector2Int> region, GameObject[] prefabs, int target, float spacing, bool tree)
    {
        int count = 0;
        foreach (Vector2Int c in Shuffled(region.ToList()))
        {
            if (count >= target) break;
            Vector3 p = CellCenter(c.x, c.y);
            if (NearOccupied(p, spacing) || (tree && routeMask[c.x, c.y])) continue;
            GameObject prefab = Pick(prefabs);
            if (prefab == null) break;
            GameObject placed = tree
                ? PlaceTree(prefab, p, Range(6f, 9f), "Parks")
                : PlaceStreetProp(prefab, p, Quaternion.Euler(0f, random.Next(4) * 90f, 0f) * prefab.transform.rotation, "Parks");
            if (placed == null) continue;
            occupied.Add((p, tree ? 2f : 1f));
            count++;
        }
        return count;
    }

    private static int ScatterBushes(List<Vector2Int> region, GameObject[] prefabs, int target)
    {
        int count = 0;
        foreach (Vector2Int c in Shuffled(region.ToList()))
        {
            if (count >= target) break;
            Vector3 p = CellCenter(c.x, c.y);
            if (NearOccupied(p, 3.5f)) continue;
            if (PlaceBush(Pick(prefabs), p, Range(1.2f, 2.6f), "Parks") == null) continue;
            occupied.Add((p, 1f));
            count++;
        }
        return count;
    }

    #endregion

    #region 放置

    private static Transform Group(string name)
    {
        Transform group = root.Find(name);
        if (group == null)
        {
            group = new GameObject(name).transform;
            group.SetParent(root, false);
            group.gameObject.isStatic = true;
        }
        return group;
    }

    /// <summary>LowpolyStreetPack 設施：URP 材質、移除燈光與光錐、碰撞體與模型同形</summary>
    private static GameObject PlaceStreetProp(GameObject prefab, Vector3 position, Quaternion rotation, string group)
    {
        if (prefab == null) return null;
        GameObject prop = (GameObject)PrefabUtility.InstantiatePrefab(prefab, Group(group));
        prop.transform.SetPositionAndRotation(position, rotation);

        foreach (Light light in prop.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(light);
        foreach (Renderer renderer in prop.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            bool cone = materials.Length > 0;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null) continue;
                if (urpRemap.TryGetValue(materials[i], out Material converted)) materials[i] = converted;
                if (!materials[i].name.Contains("LightShape")) cone = false;
            }
            renderer.sharedMaterials = materials;
            if (cone) renderer.enabled = false;
        }
        CityGenerator.FitPropColliders(prop);
        return Validate(prop);
    }

    private static GameObject PlaceTree(GameObject prefab, Vector3 position, float height, string group)
    {
        GameObject tree = PlacePlant(prefab, position, height, false, group);
        if (tree == null) return null;
        // 樹幹碰撞 (樹冠不擋無人機穿越太多空間)
        if (tree.GetComponentInChildren<Collider>() == null)
        {
            CapsuleCollider trunk = tree.AddComponent<CapsuleCollider>();
            float scale = Mathf.Max(tree.transform.lossyScale.y, 0.0001f);
            trunk.radius = 0.35f / scale;
            trunk.height = height * 0.6f / scale;
            trunk.center = new Vector3(0f, trunk.height * 0.5f, 0f);
        }
        return Validate(tree);
    }

    private static GameObject PlaceBush(GameObject prefab, Vector3 position, float width, string group)
    {
        GameObject bush = PlacePlant(prefab, position, width, true, group);
        return bush != null ? Validate(bush) : null;
    }

    private static GameObject PlacePlant(GameObject prefab, Vector3 position, float size, bool byWidth, string group)
    {
        if (prefab == null) return null;
        GameObject plant = (GameObject)PrefabUtility.InstantiatePrefab(prefab, Group(group));
        plant.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f) * prefab.transform.rotation);
        Bounds bounds = RendererBounds(plant);
        float measured = byWidth ? Mathf.Max(bounds.size.x, bounds.size.z) : bounds.size.y;
        if (measured > 0.01f) plant.transform.localScale = prefab.transform.localScale * (size / measured);
        return plant;
    }

    private static bool PlaceCar(GameObject prefab, Vector3 position, Quaternion rotation, string group)
    {
        if (prefab == null) return false;
        GameObject car = CityTraffic.CreateCarModel(prefab, Group(group), out Transform _, out Vector3 size);
        car.transform.SetPositionAndRotation(position, rotation);
        BoxCollider box = car.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, size.y * 0.5f, 0f);
        box.size = size;
        return Validate(car) != null;
    }

    /// <summary>放置後檢查：模型範圍 (略縮、離地 0.3m 以上) 內有其他碰撞體就移除，避免穿模</summary>
    private static GameObject Validate(GameObject placed)
    {
        Physics.SyncTransforms();
        Bounds bounds = RendererBounds(placed);
        Vector3 extents = new Vector3(bounds.extents.x * 0.8f, Mathf.Max(0.05f, bounds.extents.y - 0.3f), bounds.extents.z * 0.8f);
        Vector3 center = bounds.center + Vector3.up * 0.3f;
        HashSet<Collider> own = new HashSet<Collider>(placed.GetComponentsInChildren<Collider>(true));
        foreach (Collider hit in Physics.OverlapBox(center, extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            if (own.Contains(hit)) continue;
            Object.DestroyImmediate(placed);
            return null;
        }
        foreach (Transform part in placed.GetComponentsInChildren<Transform>(true)) part.gameObject.isStatic = true;
        return placed;
    }

    #endregion

    #region 工具

    private static Vector3 CellCenter(int x, int z) => origin + new Vector3((x + 0.5f) * Cell, 0f, (z + 0.5f) * Cell);

    private static bool ToCell(Vector3 p, out int x, out int z)
    {
        x = Mathf.FloorToInt((p.x - origin.x) / Cell);
        z = Mathf.FloorToInt((p.z - origin.z) / Cell);
        return InGrid(x, z);
    }

    private static bool InGrid(int x, int z) => x >= 0 && z >= 0 && x < nx && z < nz;

    private static IEnumerable<Vector2Int> Neighbours(Vector2Int c)
    {
        if (c.x > 0) yield return new Vector2Int(c.x - 1, c.y);
        if (c.x < nx - 1) yield return new Vector2Int(c.x + 1, c.y);
        if (c.y > 0) yield return new Vector2Int(c.x, c.y - 1);
        if (c.y < nz - 1) yield return new Vector2Int(c.x, c.y + 1);
    }

    private static List<Vector2Int> Cells(Ground type, int minRoadDistance, int maxRoadDistance)
    {
        List<Vector2Int> cells = new List<Vector2Int>();
        for (int x = 0; x < nx; x++)
        {
            for (int z = 0; z < nz; z++)
            {
                int d = roadDistance[x, z];
                if (ground[x, z] == type && d >= minRoadDistance && d <= maxRoadDistance) cells.Add(new Vector2Int(x, z));
            }
        }
        return cells;
    }

    private static Vector3 DirectionToRoad(Vector2Int c)
    {
        Vector2Int best = c;
        foreach (Vector2Int n in Neighbours(c))
        {
            if (roadDistance[n.x, n.y] < roadDistance[best.x, best.y]) best = n;
        }
        return best == c ? Vector3.zero : new Vector3(best.x - c.x, 0f, best.y - c.y);
    }

    private static Vector3 DirectionToOpen(Vector2Int c)
    {
        foreach (Vector2Int n in Neighbours(c))
        {
            if (ground[n.x, n.y] == Ground.Open) return new Vector3(n.x - c.x, 0f, n.y - c.y);
        }
        return Vector3.zero;
    }

    private static bool NearOccupied(Vector3 p, float distance)
    {
        foreach ((Vector3 position, float radius) in occupied)
        {
            if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(position.x, position.z)) < Mathf.Max(distance, radius + 1f)) return true;
        }
        return false;
    }

    private static List<Vector2Int> Shuffled(List<Vector2Int> cells)
    {
        for (int i = cells.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (cells[i], cells[j]) = (cells[j], cells[i]);
        }
        return cells;
    }

    private static GameObject[] Prefabs(Object[] assets) => assets.OfType<GameObject>().ToArray();

    private static GameObject[] CarPrefabs()
    {
        const string folder = "Assets/Download/Awbmecreations/Mobile Optimize-Free Low Poly Cars/Prefabs";
        string[] names = { "Classic Car_9", "Hatchback Car_15", "N Van_10", "N_Muscle Car_10", "Pick Up_11", "Police Car N_4", "Sport Car_39" };
        return names.Select(n => AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/{n}.prefab")).Where(p => p != null).ToArray();
    }

    private static GameObject Pick(GameObject[] prefabs) => prefabs.Length > 0 ? prefabs[random.Next(prefabs.Length)] : null;

    private static float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

    private static Bounds RendererBounds(GameObject target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(target.transform.position, Vector3.one);
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers)
        {
            if (renderer.enabled) bounds.Encapsulate(renderer.bounds);
        }
        return bounds;
    }

    #endregion
}
