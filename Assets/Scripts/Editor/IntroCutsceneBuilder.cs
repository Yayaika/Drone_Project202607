using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using UnityEngine.UI;

/// <summary>
/// 建立開場動畫 (選單：無人機遊戲/13)：在 StartMenu 建立「IntroCutscene」(PlayableDirector + <see cref="IntroCutscene"/>)，
/// Timeline 資產 Assets/Cutscenes/IntroCutscene.playable 含兩條軌道：
/// 1. Camera Rig：動畫軌道驅動 XR Origin 根物件 (只轉水平方向，VR 不會傾斜地平線)，從選單位置升空後繞行背景城市
/// 2. Dialogue：<see cref="DialogueTrack"/> 兩名角色的對話字幕
/// 開始按鈕原本呼叫 SceneChanger.LoadScene(下一個場景)，改為呼叫 IntroCutscene.Play，場景名稱移到 IntroCutscene。
/// 重新執行會覆蓋 Timeline 資產 (在 Timeline 視窗手動調整過的內容會被還原)。
/// </summary>
public static class IntroCutsceneBuilder
{
    private const string ScenePath = "Assets/Scenes/StartMenu.unity";
    private const string Folder = "Assets/Cutscenes";
    private const string TimelinePath = Folder + "/IntroCutscene.playable";
    private const string CameraClipPath = Folder + "/IntroCameraPath.anim";
    private const string RootName = "IntroCutscene";
    private const string RigName = "XR Origin (XR Rig)";
    private const string MenuCanvasName = "Canvas";
    /// <summary>對話彈出音效：沿用專案內 XR Interaction Toolkit 範例附的按鈕彈出音</summary>
    private const string PopSoundPath = "Assets/Samples/XR Interaction Toolkit/3.0.11/Starter Assets/DemoSceneAssets/Audio/Button Pop.wav";

    private const string Instructor = "林教官";
    private const string Rookie = "阿哲";

    /// <summary>(說話者, 台詞)：林教官在左、阿哲在右</summary>
    private static readonly (string speaker, string line)[] Script =
    {
        (Instructor, "看到下面塞住的救護車了嗎？現在，天空是這座城市唯一不塞車的路。"),
        (Rookie, "所以空中救援隊才要招募新飛手？我也想加入！"),
        (Instructor, "那就先通過三項考驗：大樓間穿圈的「競速」、準時送達的「搬運」，還有港區的「打擊演習」。"),
        (Rookie, "要快、要穩、還要準……聽起來超刺激！"),
        (Instructor, "去機庫挑一台你的無人機吧，菜鳥飛手。"),
        (Rookie, "收到，準備起飛！")
    };

    private const float FirstLineStart = 1.2f;
    private const float LineGap = 0.3f;
    private const float EndHold = 1f;

    [MenuItem("無人機遊戲/13. 建立開場動畫 (開始選單)", priority = 13)]
    private static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorUtility.DisplayDialog("開場動畫", Build(), "確定");
    }

    public static string Build()
    {
        if (!File.Exists(ScenePath)) return $"找不到 {ScenePath}";
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject[] roots = scene.GetRootGameObjects();

        GameObject rig = roots.FirstOrDefault(r => r.name == RigName);
        if (rig == null) return $"StartMenu 找不到 {RigName}";
        GameObject menuCanvas = roots.FirstOrDefault(r => r.name == MenuCanvasName);
        CityGenerator city = roots.SelectMany(r => r.GetComponentsInChildren<CityGenerator>(true)).FirstOrDefault();
        Vector3 cityCenter = city != null ? city.transform.position : new Vector3(0f, -18f, 140f);

        GameObject host = roots.FirstOrDefault(r => r.name == RootName) ?? new GameObject(RootName);
        // 編輯器中 GetComponent 找不到時回傳 Unity 的假 null，不能用 ??
        PlayableDirector director = host.GetComponent<PlayableDirector>();
        if (director == null) director = host.AddComponent<PlayableDirector>();
        IntroCutscene cutscene = host.GetComponent<IntroCutscene>();
        if (cutscene == null) cutscene = host.AddComponent<IntroCutscene>();

        string nextScene = RewireStartButton(cutscene, out string buttonReport);
        if (nextScene == null) return buttonReport;

        Animator animator = rig.GetComponent<Animator>();
        if (animator == null) animator = rig.AddComponent<Animator>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        EnsureFolder(Folder);
        float duration = DialogueEnd() + EndHold;
        AnimationClip cameraPath = BuildCameraPath(rig.transform, cityCenter, duration);
        TimelineAsset timeline = BuildTimeline(cameraPath, duration, out AnimationTrack cameraTrack, out DialogueTrack dialogueTrack);

        director.playableAsset = timeline;
        director.playOnAwake = false;
        director.extrapolationMode = DirectorWrapMode.None;
        director.timeUpdateMode = DirectorUpdateMode.GameTime;
        director.SetGenericBinding(cameraTrack, animator);
        director.SetGenericBinding(dialogueTrack, cutscene);

        SerializedObject so = new SerializedObject(cutscene);
        so.FindProperty("director").objectReferenceValue = director;
        so.FindProperty("nextScene").stringValue = nextScene;
        so.FindProperty("dialoguePopSound").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(PopSoundPath);
        // 只隱藏選單 Canvas；XR 手把保留，用射線點擊「跳過」
        GameObject[] hidden = new[] { menuCanvas }.Where(go => go != null).ToArray();
        SerializedProperty hide = so.FindProperty("hideWhilePlaying");
        hide.arraySize = hidden.Length;
        for (int i = 0; i < hidden.Length; i++) hide.GetArrayElementAtIndex(i).objectReferenceValue = hidden[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(director);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return $"開場動畫：{Script.Length} 句對話、長度 {duration:0.0} 秒；{buttonReport}；播完進入 {nextScene}";
    }

    /// <summary>開始按鈕：移除 SceneChanger.LoadScene，改呼叫 IntroCutscene.Play。回傳原本要載入的場景名稱</summary>
    private static string RewireStartButton(IntroCutscene cutscene, out string report)
    {
        Button start = null;
        string nextScene = null;
        foreach (Button button in Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            SerializedProperty calls = new SerializedObject(button).FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            for (int i = 0; i < calls.arraySize; i++)
            {
                SerializedProperty call = calls.GetArrayElementAtIndex(i);
                Object target = call.FindPropertyRelative("m_Target").objectReferenceValue;
                string method = call.FindPropertyRelative("m_MethodName").stringValue;
                if (target is SceneChanger && method == "LoadScene")
                {
                    start = button;
                    nextScene = call.FindPropertyRelative("m_Arguments.m_StringArgument").stringValue;
                }
                else if (target == cutscene && method == nameof(IntroCutscene.Play))
                {
                    start = button;
                }
            }
            if (start != null) break;
        }

        if (start == null)
        {
            report = "找不到開始按鈕 (呼叫 SceneChanger.LoadScene 的 Button)";
            return null;
        }

        if (nextScene == null)
        {
            // 已改接過：沿用 IntroCutscene 上的場景名稱
            report = $"開始按鈕「{start.name}」已接到開場動畫";
            return new SerializedObject(cutscene).FindProperty("nextScene").stringValue;
        }

        for (int i = start.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            if (start.onClick.GetPersistentTarget(i) is SceneChanger && start.onClick.GetPersistentMethodName(i) == "LoadScene")
            {
                UnityEventTools.RemovePersistentListener(start.onClick, i);
            }
        }
        UnityEventTools.AddVoidPersistentListener(start.onClick, new UnityAction(cutscene.Play));
        EditorUtility.SetDirty(start);
        report = $"開始按鈕「{start.name}」改為播放開場動畫";
        return nextScene;
    }

    private static float LineDuration(string line) => Mathf.Clamp(line.Length / 7f + 1.2f, 2.6f, 6.5f);

    private static float DialogueEnd()
    {
        float time = FirstLineStart;
        foreach (var entry in Script) time += LineDuration(entry.line) + LineGap;
        return time - LineGap;
    }

    /// <summary>
    /// 鏡頭路徑：選單位置 → 從主角無人機左側升空飛出平台 → 以城市中心為圓心逆時針繞行 (緩慢升降、視線略朝前進方向)。
    /// 只改變位置與水平朝向。
    /// </summary>
    private static AnimationClip BuildCameraPath(Transform rig, Vector3 cityCenter, float duration)
    {
        AnimationCurve x = new AnimationCurve(), y = new AnimationCurve(), z = new AnimationCurve(), yaw = new AnimationCurve();
        void Key(float time, Vector3 position, float heading)
        {
            x.AddKey(time, position.x);
            y.AddKey(time, position.y);
            z.AddKey(time, position.z);
            yaw.AddKey(time, heading);
        }

        Vector3 start = rig.position;
        float startYaw = rig.eulerAngles.y;
        Key(0f, start, startYaw);
        Key(2.5f, start + new Vector3(-4f, 3f, 6f), startYaw);

        const float radius = 125f;
        const float orbitStartTime = 7f;
        float orbitDegrees = Mathf.Clamp(duration * 4.5f, 90f, 290f); // 約每秒 4.5°，VR 中不會轉太快
        float orbitEnd = duration;
        float previousYaw = startYaw;
        for (float t = orbitStartTime; t <= orbitEnd + 0.01f; t += 3f)
        {
            float progress = Mathf.InverseLerp(orbitStartTime, orbitEnd, t);
            float angle = (-90f + orbitDegrees * progress) * Mathf.Deg2Rad;
            Vector3 position = new Vector3(cityCenter.x + Mathf.Cos(angle) * radius,
                cityCenter.y + 36f + Mathf.Sin(progress * Mathf.PI * 3f) * 6f,
                cityCenter.z + Mathf.Sin(angle) * radius);
            Vector3 toCenter = cityCenter - position;
            float heading = Mathf.Atan2(toCenter.x, toCenter.z) * Mathf.Rad2Deg + 18f;
            heading = previousYaw + Mathf.DeltaAngle(previousYaw, heading); // 連續角度，避免 360° 跳轉
            previousYaw = heading;
            Key(t, position, heading);
        }

        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(CameraClipPath);
        if (clip == null)
        {
            clip = new AnimationClip { name = "IntroCameraPath" };
            AssetDatabase.CreateAsset(clip, CameraClipPath);
        }
        clip.ClearCurves();
        foreach (AnimationCurve curve in new[] { x, y, z, yaw })
        {
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            }
        }
        SetCurve(clip, "localPosition.x", x);
        SetCurve(clip, "localPosition.y", y);
        SetCurve(clip, "localPosition.z", z);
        SetCurve(clip, "localEulerAnglesRaw.x", AnimationCurve.Constant(0f, duration, 0f));
        SetCurve(clip, "localEulerAnglesRaw.y", yaw);
        SetCurve(clip, "localEulerAnglesRaw.z", AnimationCurve.Constant(0f, duration, 0f));
        EditorUtility.SetDirty(clip);
        return clip;
    }

    private static void SetCurve(AnimationClip clip, string property, AnimationCurve curve)
    {
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Transform), property), curve);
    }

    private static TimelineAsset BuildTimeline(AnimationClip cameraPath, float duration, out AnimationTrack cameraTrack, out DialogueTrack dialogueTrack)
    {
        AssetDatabase.DeleteAsset(TimelinePath);
        TimelineAsset timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        timeline.name = "IntroCutscene";
        AssetDatabase.CreateAsset(timeline, TimelinePath);

        cameraTrack = timeline.CreateTrack<AnimationTrack>(null, "Camera Rig");
        cameraTrack.trackOffset = TrackOffset.ApplyTransformOffsets; // 位移/旋轉偏移為 0：曲線即世界座標
        TimelineClip cameraClip = cameraTrack.CreateClip(cameraPath);
        cameraClip.start = 0;
        cameraClip.duration = duration;
        cameraClip.displayName = "繞行城市";

        dialogueTrack = timeline.CreateTrack<DialogueTrack>(null, "Dialogue");
        float time = FirstLineStart;
        foreach (var entry in Script)
        {
            float length = LineDuration(entry.line);
            TimelineClip clip = dialogueTrack.CreateClip<DialogueClip>();
            clip.start = time;
            clip.duration = length;
            clip.displayName = entry.speaker;
            DialogueClip asset = (DialogueClip)clip.asset;
            asset.speaker = entry.speaker;
            asset.portrait = entry.speaker == Rookie ? "哲" : "林";
            asset.line = entry.line;
            asset.rightSide = entry.speaker == Rookie;
            EditorUtility.SetDirty(asset);
            time += length + LineGap;
        }

        timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
        timeline.fixedDuration = duration;
        EditorUtility.SetDirty(timeline);
        return timeline;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
