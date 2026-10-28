using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 敵方軍人 (選單「無人機遊戲/16」)：
/// 1. 動畫控制器 (Idle 預設、Run 依 Speed、Shoot 由觸發，播完回 Idle)，使用 LowPolySoldiers_demo 的三段動畫。
/// 2. 四種兵種 Prefab (Assets/Prefabs/Soldier)：模型縮放到身高 1.8m，槍械與射擊參數不同 ——
///    步槍兵 (內建步槍，單發)、卡賓槍兵 (War FX 的 M4 綁在右手，3 發點放)、
///    精確射手 (內建步槍 + 瞄準鏡，慢速高傷害、射程遠)、機槍兵 (M4 + 彈鼓，6 發連射、散布大)。
///    使用 M4 的兵種改用移除內建步槍子網格的網格，不會有兩把槍重疊。
/// 3. 曳光彈拖尾顏色 (複製 Bullet FX Pack 的 BulletTrailMat)、GameplayAssets 的預設兵種 / 子彈 / 槍口火光 / 彈著點。
/// 4. 港區場景擺放軍人出生點 (守在目標附近、與起飛點保持 45m 以上)。工廠由 <see cref="FactorySceneBuilder"/> 擺放。
/// </summary>
public static class SoldierSetup
{
    private const string SoldierFolder = "Assets/Download/LowPolySoldiers_demo";
    private const string PrefabFolder = "Assets/Prefabs/Soldier";
    private const string ControllerPath = "Assets/Animations/Soldier/Soldier.controller";
    private const string AssetsPath = "Assets/Resources/GameplayAssets.asset";
    private const string BulletFolder = "Assets/Download/Bullet FX Pack Trail, Fire, Energy";
    private const string WarFxFolder = "Assets/Download/JMO Assets/WarFX";
    private const string M4Path = WarFxFolder + "/Demo/Assets/WFX_Demo_M4.fbx";
    private const string TracerFolder = "Assets/Materials/Weapons";
    private const string RootName = "Soldiers";
    private const string PeopleAnimations = "Assets/Download/DenysAlmaral/CityPeople/Animations";
    private const string WorkerControllerPath = "Assets/Animations/Worker/Worker.controller";

    private class WeaponSpec
    {
        public string Name, Display, Flash, Tracer;
        public bool M4, Scope, Drum;
        public float GunLength = 0.84f;
        public int Burst = 1;
        public float BurstInterval = 0.1f;
        public Vector2 Interval;
        public float Spread, Speed, Range;
        public int Damage;
        public int Magazine = 20;
        public Color TracerColor;
    }

    private static readonly WeaponSpec[] Specs =
    {
        new WeaponSpec { Name = "Rifle", Display = "步槍", Flash = "WFX_MF 4P RIFLE1", Tracer = "Orange", TracerColor = new Color(1f, 0.62f, 0.2f),
            Interval = new Vector2(0.7f, 1.2f), Spread = 2.5f, Damage = 8, Speed = 90f, Range = 45f, Magazine = 20 },
        new WeaponSpec { Name = "Carbine", Display = "卡賓槍 (M4)", Flash = "WFX_MF 4P RIFLE2", Tracer = "Yellow", TracerColor = new Color(1f, 0.88f, 0.35f),
            M4 = true, Burst = 3, BurstInterval = 0.09f, Interval = new Vector2(1.3f, 1.9f), Spread = 3.2f, Damage = 6, Speed = 95f, Range = 42f, Magazine = 30 },
        new WeaponSpec { Name = "Marksman", Display = "精確射手步槍", Flash = "WFX_MF 4P RIFLE3", Tracer = "Red", TracerColor = new Color(1f, 0.3f, 0.2f),
            Scope = true, Interval = new Vector2(2f, 2.8f), Spread = 0.8f, Damage = 18, Speed = 140f, Range = 65f, Magazine = 10 },
        new WeaponSpec { Name = "MachineGunner", Display = "輕機槍", Flash = "WFX_MF 4P RIFLE1", Tracer = "Red", TracerColor = new Color(1f, 0.3f, 0.2f),
            M4 = true, Drum = true, GunLength = 1.0f, Burst = 6, BurstInterval = 0.075f, Interval = new Vector2(2.2f, 3f), Spread = 4.2f, Damage = 5, Speed = 100f, Range = 50f, Magazine = 100 }
    };

    [MenuItem("無人機遊戲/16. 建立敵方軍人 (Prefab、動畫、港區擺放)", priority = 16)]
    private static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorUtility.DisplayDialog("敵方軍人", Build(), "確定");
    }

    public static string Build()
    {
        AnimatorController controller = BuildController();
        foreach (WeaponSpec spec in Specs) BuildPrefab(spec, controller);

        GameplayAssets assets = AssetDatabase.LoadAssetAtPath<GameplayAssets>(AssetsPath);
        SerializedObject so = new SerializedObject(assets);
        so.FindProperty("soldierPrefab").objectReferenceValue = Prefab("Rifle");
        so.FindProperty("soldierAnimator").objectReferenceValue = controller;
        so.FindProperty("workerAnimator").objectReferenceValue = BuildWorkerController();
        so.FindProperty("bulletPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>($"{BulletFolder}/Prefabs/Bullet.prefab");
        so.FindProperty("muzzleFlashEffect").objectReferenceValue = Flash("WFX_MF 4P RIFLE1");
        so.FindProperty("bulletImpactEffect").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>($"{WarFxFolder}/_Effects/Bullet Impacts/WFX_BImpact Concrete.prefab");
        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();

        int placed = PlacePortSoldiers();
        return $"兵種 Prefab：{string.Join("、", Specs.Select(s => s.Display))}\n港區軍人出生點 {placed} 個";
    }

    internal static GameObject Prefab(string variant) => AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/Soldier_{variant}.prefab");

    private static GameObject Flash(string name) =>
        AssetDatabase.LoadAssetAtPath<GameObject>($"{WarFxFolder}/_Effects/MuzzleFlashes/4Planes/{name}.prefab");

    #region 動畫控制器

    private static AnimationClip Clip(string name)
    {
        return AssetDatabase.LoadAllAssetsAtPath($"{SoldierFolder}/animation/{name}.FBX")
            .OfType<AnimationClip>()
            .First(clip => !clip.name.StartsWith("__"));
    }

    /// <summary>使用者補充的軍人動畫放這裡 (檔名見 <see cref="SoldierStates"/>)，Generic 骨架須與 LowPolySoldiers_demo 相同 (3ds Max Biped：Bip001)</summary>
    public const string ExtraClipFolder = "Assets/Animations/Soldier/Clips";

    /// <summary>
    /// 行為樹各狀態的動畫：(狀態, 使用者補充的檔名, 素材現有的替代動作, 替代時的播放速度)。
    /// 補充檔不存在時用替代動作 (瞄準 = 射擊動作第一格定格)；陣亡沒有替代 (改用程式倒地)。
    /// </summary>
    public static readonly (string state, string extraFile, string fallback, float fallbackSpeed)[] SoldierStates =
    {
        ("Idle", null, "demo_combat_idle", 1f),
        ("Run", null, "demo_combat_run", 1f),
        ("Shoot", null, "demo_combat_shoot", 1f),
        ("Walk", "soldier_walk", "demo_combat_run", 0.5f),
        ("Aim", "soldier_aim", "demo_combat_shoot", 0f),
        ("Reload", "soldier_reload", "demo_combat_idle", 1f),
        ("Alert", "soldier_alert", "demo_combat_idle", 1f),
        ("Hit", "soldier_hit", "demo_combat_idle", 1.6f),
        ("Death", "soldier_death", null, 1f)
    };

    /// <summary>
    /// 軍人控制器：由 <see cref="SoldierUnit"/> 以狀態名稱切換 (不需要轉場條件)。
    /// 控制器資產只建立一次、之後清空重建內容 (刪除重建會讓已載入的 Prefab 失去引用)。
    /// </summary>
    private static AnimatorController BuildController()
    {
        StrikeSceneBuilder.EnsureFolder(ExtraClipFolder);
        AnimatorController controller = LoadOrCreateController(ControllerPath);
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        foreach ((string state, string extraFile, string fallback, float fallbackSpeed) in SoldierStates)
        {
            AnimationClip clip = extraFile != null ? ExtraClip(extraFile) : null;
            float speed = 1f;
            if (clip == null && fallback != null)
            {
                clip = Clip(fallback);
                speed = fallbackSpeed;
            }
            if (clip == null) continue;   // 陣亡沒有替代動作：不建立狀態，SoldierUnit 改用程式倒地
            AnimatorState added = machine.AddState(state);
            added.motion = clip;
            added.speed = speed;
            if (state == "Idle") machine.defaultState = added;
        }
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    private static AnimationClip ExtraClip(string name)
    {
        foreach (string extension in new[] { "fbx", "FBX", "anim" })
        {
            string path = $"{ExtraClipFolder}/{name}.{extension}";
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__"));
            if (clip != null) return clip;
        }
        return null;
    }

    /// <summary>載入既有控制器並清空狀態與參數 (保持資產 GUID 與物件不變)，不存在時建立</summary>
    private static AnimatorController LoadOrCreateController(string path)
    {
        StrikeSceneBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (controller == null) return AnimatorController.CreateAnimatorControllerAtPath(path);
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        foreach (ChildAnimatorState child in machine.states) machine.RemoveState(child.state);
        foreach (AnimatorStateTransition transition in machine.anyStateTransitions) machine.RemoveAnyStateTransition(transition);
        controller.parameters = new AnimatorControllerParameter[0];
        return controller;
    }

    /// <summary>
    /// 平民工人控制器 (CityPeople 的 Humanoid 動畫，全部循環)：由 <see cref="FactoryWorker"/> 以狀態名稱切換，不需要轉場條件。
    /// </summary>
    private static AnimatorController BuildWorkerController()
    {
        AnimatorController controller = LoadOrCreateController(WorkerControllerPath);
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        (string state, string file)[] states =
        {
            ("Idle", "idle_m_2_220f"), ("Walk", "locom_m_basicWalk_30f"), ("Run", "locom_m_running_20f"),
            ("LookAround", "idle_selfcheck_1_300f"), ("Phone", "idle_phoneTalking_180f"),
            ("Hammer", "construction/ani_wtool_hammerUse"), ("Drill", "construction/ani_wtool_drillUse"),
            ("Wrench", "construction/ani_wtool_wrenchUse"), ("PipeWrench", "construction/ani_wtool_pipewrenchUse")
        };
        foreach ((string state, string file) in states)
        {
            AnimatorState added = machine.AddState(state);
            added.motion = AssetDatabase.LoadAllAssetsAtPath($"{PeopleAnimations}/{file}.fbx").OfType<AnimationClip>().First(clip => !clip.name.StartsWith("__"));
            if (state == "Idle") machine.defaultState = added;
        }
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    #endregion

    #region 兵種 Prefab

    private static void BuildPrefab(WeaponSpec spec, AnimatorController controller)
    {
        StrikeSceneBuilder.EnsureFolder(PrefabFolder);
        GameObject demo = AssetDatabase.LoadAssetAtPath<GameObject>($"{SoldierFolder}/Soldier_demo_Prefab.prefab");
        GameObject root = new GameObject($"Soldier_{spec.Name}");
        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(demo, root.transform);
        model.name = "Model";
        model.transform.localScale = Vector3.one * SoldierUnit.ModelScale;
        Animator animator = model.GetComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;

        SkinnedMeshRenderer skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
        Transform hand = model.GetComponentsInChildren<Transform>().First(t => t.name == "Bip001 R Hand");
        // 以待機姿勢量測內建步槍：槍管方向 (槍托 → 槍口) 與槍的上方 (世界上方)，都換算到右手座標
        Clip("demo_combat_idle").SampleAnimation(model, 0.1f);
        RifleAxis(skin, hand, out Vector3 muzzleLocal, out Vector3 axis);
        Vector3 up = Vector3.ProjectOnPlane(hand.InverseTransformDirection(Vector3.up), axis).normalized;
        Quaternion barrel = Quaternion.LookRotation(axis, up);
        float handScale = hand.lossyScale.x;

        Transform muzzle = new GameObject("Muzzle").transform;
        Material dark = WeaponMaterial();
        if (spec.M4)
        {
            skin.sharedMesh = MeshWithoutRifle(skin.sharedMesh);
            skin.sharedMaterials = new[] { skin.sharedMaterials[0] };

            GameObject gun = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(M4Path), hand);
            gun.name = "Weapon_M4";
            foreach (Collider collider in gun.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            Bounds bounds = gun.GetComponentInChildren<MeshFilter>().sharedMesh.bounds;
            gun.transform.localRotation = barrel;
            gun.transform.localScale = Vector3.one * (spec.GunLength / bounds.size.z / handScale);
            gun.transform.localPosition = Vector3.zero;
            muzzle.SetParent(gun.transform, false);
            muzzle.localPosition = new Vector3(0f, bounds.center.y, bounds.max.z);
            if (spec.Drum)
            {
                Transform drum = Part(PrimitiveType.Cylinder, gun.transform, dark);
                drum.localPosition = new Vector3(0f, bounds.min.y + 0.02f, bounds.center.z - 0.05f);
                drum.localRotation = Quaternion.Euler(0f, 0f, 90f);
                drum.localScale = new Vector3(0.2f, 0.05f, 0.2f);
                Transform bipod = Part(PrimitiveType.Cube, gun.transform, dark);
                bipod.localPosition = new Vector3(0f, bounds.min.y + 0.05f, bounds.max.z - 0.15f);
                bipod.localScale = new Vector3(0.12f, 0.02f, 0.02f);
            }
        }
        else
        {
            muzzle.SetParent(hand, false);
            muzzle.localPosition = muzzleLocal;
            muzzle.localRotation = barrel;
            if (spec.Scope)
            {
                Transform scope = Part(PrimitiveType.Cylinder, hand, dark);
                scope.localPosition = axis * (0.12f / handScale) + up * (0.1f / handScale);
                scope.localRotation = Quaternion.FromToRotation(Vector3.up, axis);
                scope.localScale = new Vector3(0.045f, 0.13f, 0.045f) / handScale;
                Transform lens = Part(PrimitiveType.Cylinder, hand, WeaponLens());
                lens.localPosition = axis * (0.26f / handScale) + up * (0.1f / handScale);
                lens.localRotation = Quaternion.FromToRotation(Vector3.up, axis);
                lens.localScale = new Vector3(0.05f, 0.005f, 0.05f) / handScale;
            }
        }
        if (spec.M4) muzzle.localRotation = Quaternion.identity;

        CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 0.9f, 0f);
        capsule.height = 1.8f;
        capsule.radius = 0.35f;
        Rigidbody body = root.AddComponent<Rigidbody>();
        body.isKinematic = true;

        SoldierUnit unit = root.AddComponent<SoldierUnit>();
        SerializedObject so = new SerializedObject(unit);
        so.FindProperty("weaponName").stringValue = spec.Display;
        so.FindProperty("muzzle").objectReferenceValue = muzzle;
        so.FindProperty("muzzleFlash").objectReferenceValue = Flash(spec.Flash);
        so.FindProperty("tracerMaterial").objectReferenceValue = TracerMaterial(spec.Tracer, spec.TracerColor);
        so.FindProperty("tracerColor").colorValue = spec.TracerColor;
        so.FindProperty("detectRange").floatValue = spec.Range;
        so.FindProperty("burstCount").intValue = spec.Burst;
        so.FindProperty("burstInterval").floatValue = spec.BurstInterval;
        so.FindProperty("fireInterval").vector2Value = spec.Interval;
        so.FindProperty("spreadDegrees").floatValue = spec.Spread;
        so.FindProperty("bulletSpeed").floatValue = spec.Speed;
        so.FindProperty("damagePerHit").intValue = spec.Damage;
        so.FindProperty("magazineSize").intValue = spec.Magazine;
        so.ApplyModifiedPropertiesWithoutUndo();

        string prefabPath = $"{PrefabFolder}/Soldier_{spec.Name}.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
        Object.DestroyImmediate(root);
    }

    /// <summary>內建步槍 (第 2 個子網格) 在右手座標的槍口 (離手最遠的頂點) 與槍管方向 (槍托 → 槍口)</summary>
    private static void RifleAxis(SkinnedMeshRenderer skin, Transform hand, out Vector3 muzzle, out Vector3 axis)
    {
        Mesh baked = new Mesh();
        skin.BakeMesh(baked, false);
        Vector3[] vertices = baked.vertices;
        int[] rifle = baked.GetTriangles(1);
        muzzle = Vector3.zero;
        float far = -1f;
        foreach (int index in rifle)
        {
            Vector3 local = hand.InverseTransformPoint(skin.transform.TransformPoint(vertices[index]));
            if (local.sqrMagnitude <= far) continue;
            far = local.sqrMagnitude;
            muzzle = local;
        }
        Vector3 stock = muzzle;
        float back = float.MaxValue;
        foreach (int index in rifle)
        {
            Vector3 local = hand.InverseTransformPoint(skin.transform.TransformPoint(vertices[index]));
            float along = Vector3.Dot(local, muzzle.normalized);
            if (along >= back) continue;
            back = along;
            stock = local;
        }
        axis = (muzzle - stock).normalized;
        Object.DestroyImmediate(baked);
    }

    private static Mesh MeshWithoutRifle(Mesh source)
    {
        string path = $"{PrefabFolder}/Soldier_NoRifle.asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;
        Mesh copy = Object.Instantiate(source);
        copy.name = "Soldier_NoRifle";
        copy.subMeshCount = 1;
        AssetDatabase.CreateAsset(copy, path);
        return copy;
    }

    private static Transform Part(PrimitiveType type, Transform parent, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        Object.DestroyImmediate(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.GetComponent<Renderer>().sharedMaterial = material;
        return part.transform;
    }

    private static Material WeaponMaterial() => LitMaterial("Weapon_Dark", new Color(0.12f, 0.12f, 0.13f), 0.45f);
    private static Material WeaponLens() => LitMaterial("Weapon_Lens", new Color(0.15f, 0.35f, 0.5f), 0.95f);

    private static Material LitMaterial(string name, Color color, float smoothness)
    {
        StrikeSceneBuilder.EnsureFolder(TracerFolder);
        string path = $"{TracerFolder}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>曳光拖尾：複製 Bullet FX Pack 的 BulletTrailMat 改顏色</summary>
    private static Material TracerMaterial(string name, Color color)
    {
        StrikeSceneBuilder.EnsureFolder(TracerFolder);
        string path = $"{TracerFolder}/Tracer_{name}.mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(path) == null)
        {
            AssetDatabase.CopyAsset($"{BulletFolder}/Materials/BulletTrailMat.mat", path);
        }
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        material.SetColor("_Color", color);
        material.SetColor("_EmissionColor", color);
        EditorUtility.SetDirty(material);
        return material;
    }

    #endregion

    #region 擺放

    /// <summary>港區：守在目標附近 (起飛點在 (0, -86))，兵種混合</summary>
    private static int PlacePortSoldiers()
    {
        Scene scene = EditorSceneManager.OpenScene(StrikeSceneBuilder.ScenePath, OpenSceneMode.Single);
        GameObject old = scene.GetRootGameObjects().FirstOrDefault(root => root.name == RootName);
        if (old != null) Object.DestroyImmediate(old);
        Transform root = new GameObject(RootName).transform;

        int count = 0;
        // 制高點：辦公樓屋頂 (射界涵蓋南側入口與卸貨頂棚)、碼頭吊車機房頂 (俯瞰碼頭與港池)
        AddPoint(root, "Office_Marksman", Prefab("Marksman"), new Vector3(38f, 12.4f, -48f), 300f, null, ref count);
        AddPoint(root, "Crane_Marksman", Prefab("Marksman"), new Vector3(25f, 25.5f, 66f), 180f, null, ref count);
        // 要點：倉庫 A 卸貨口前的機槍陣地 (守撞擊目標)、倉庫 B 開口處、卸貨頂棚的貨車之間
        AddPoint(root, "Warehouse_MG", Prefab("MachineGunner"), new Vector3(-30f, 0f, 6f), 200f, null, ref count);
        AddPoint(root, "WarehouseB_Guard", Prefab("Rifle"), new Vector3(24f, 0f, 22f), 270f, null, ref count);
        AddPoint(root, "Canopy_Guard", Prefab("Carbine"), new Vector3(28f, 0f, -30f), 180f, null, ref count);
        // 巡邏：貨櫃場中央走道 (避開走道中的撞擊目標)、高架管廊下方、碼頭沿岸
        AddPoint(root, "Yard_Patrol", Prefab("Carbine"), new Vector3(-50f, 0f, -43.5f), 90f, new Vector3(-22f, 0f, -43.5f), ref count);
        AddPoint(root, "PipeRack_Patrol", Prefab("Rifle"), new Vector3(-14f, 0f, 34f), 90f, new Vector3(2f, 0f, 34f), ref count);
        AddPoint(root, "Quay_Patrol", Prefab("Rifle"), new Vector3(-20f, 0f, 70f), 90f, new Vector3(10f, 0f, 70f), ref count);
        // 油槽之間 (守油槽頂投彈目標)
        AddPoint(root, "Tank_Guard", Prefab("Carbine"), new Vector3(32f, 0f, 38f), 0f, null, ref count);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return count;
    }

    internal static void AddPoint(Transform root, string name, GameObject prefab, Vector3 position, float yaw, Vector3? patrolTo, ref int count)
    {
        SoldierPoint point = new GameObject(name).AddComponent<SoldierPoint>();
        point.transform.SetParent(root, false);
        point.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        point.soldierPrefab = prefab;
        if (patrolTo.HasValue)
        {
            Transform end = new GameObject("PatrolTo").transform;
            end.SetParent(point.transform, false);
            end.position = patrolTo.Value;
            point.patrolTo = end;
        }
        count++;
    }

    #endregion
}
