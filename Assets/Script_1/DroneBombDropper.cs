using DG.Tweening;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 炸彈無人機 (Drone_Bomb) 的投彈功能：按 B / 手把右扳機，把機腹掛載的炸彈 (模型上的「Bomb」) 投下。
/// 投下後機腹炸彈隱藏，裝填時間到再重新顯示。炸彈本體為 Assets/Prefabs/Bomb.prefab。
/// </summary>
[RequireComponent(typeof(DroneController1))]
public class DroneBombDropper : MonoBehaviour
{
    [SerializeField] private GameObject bombPrefab;
    [Tooltip("機腹上掛載的炸彈模型 (留空自動尋找名為 Bomb 的子物件)")]
    [SerializeField] private Transform mountedBomb;
    [Tooltip("投彈後重新裝填所需秒數")]
    [SerializeField] private float reloadSeconds = 3f;
    [Tooltip("掛著炸彈時，撞擊速度超過此值 (m/s) 炸彈會直接在機腹引爆")]
    [SerializeField] private float crashDetonateSpeed = 6f;

    private DroneController1 drone;
    private Rigidbody body;
    private Renderer[] mountedRenderers;
    private Vector3 mountedScale = Vector3.one;
    private InputAction dropAction;
    private float reloadTimer;

    public bool IsLoaded => reloadTimer <= 0f;
    /// <summary>裝填進度 0~1 (1 = 可投彈)</summary>
    public float ReloadProgress => reloadSeconds > 0f ? 1f - Mathf.Clamp01(reloadTimer / reloadSeconds) : 1f;

    private void Awake()
    {
        drone = GetComponent<DroneController1>();
        body = GetComponent<Rigidbody>();
        if (mountedBomb == null) mountedBomb = FindDeepChild(transform, "Bomb");
        if (mountedBomb != null)
        {
            mountedRenderers = mountedBomb.GetComponentsInChildren<Renderer>(true);
            mountedScale = mountedBomb.localScale;
        }
        if (bombPrefab == null && GameplayAssets.Instance != null) bombPrefab = GameplayAssets.Instance.bombPrefab;

        dropAction = new InputAction("DropBomb", binding: "<Keyboard>/b");
        dropAction.AddBinding("<Gamepad>/rightTrigger");
    }

    private void OnEnable()
    {
        dropAction.Enable();
        drone.OnCrash += HandleCrash;
    }

    private void OnDisable()
    {
        dropAction.Disable();
        drone.OnCrash -= HandleCrash;
    }

    /// <summary>掛著炸彈高速撞擊：炸彈在機腹引爆 (無人機會被衝擊波推開)，之後照常重新裝填</summary>
    private void HandleCrash(float impactSpeed)
    {
        if (!IsLoaded || impactSpeed < crashDetonateSpeed || mountedBomb == null) return;

        foreach (Renderer mounted in mountedRenderers) mounted.enabled = false;
        reloadTimer = reloadSeconds;
        BombExplosion.Detonate(mountedBomb.position, BombExplosion.DefaultRadius, null);
    }

    private void Update()
    {
        if (reloadTimer > 0f)
        {
            reloadTimer -= Time.deltaTime;
            if (reloadTimer <= 0f) ShowMountedBomb();
        }

        if (!dropAction.WasPressedThisFrame() || Time.timeScale <= 0f) return;
        if (!drone.canControl || !drone.IsEngineStarted || !IsLoaded) return;
        Drop();
    }

    private void Drop()
    {
        if (bombPrefab == null || mountedBomb == null)
        {
            Debug.LogWarning("[DroneBombDropper] 缺少炸彈 Prefab 或機腹炸彈模型，無法投彈。");
            return;
        }

        GameObject bomb = Instantiate(bombPrefab, mountedBomb.position, mountedBomb.rotation);
        Vector3 velocity = body != null ? body.GetPointVelocity(mountedBomb.position) : Vector3.zero;
        DroppedBomb dropped = bomb.GetComponent<DroppedBomb>();
        if (dropped != null) dropped.Launch(transform, velocity);

        foreach (Renderer mounted in mountedRenderers) mounted.enabled = false;
        reloadTimer = reloadSeconds;
        GameAudio.Play(GameSfx.CargoDrop, 0.8f);
    }

    private void ShowMountedBomb()
    {
        if (mountedBomb == null) return;
        foreach (Renderer mounted in mountedRenderers) mounted.enabled = true;
        // 裝填完成：炸彈從機腹「長」回來
        mountedBomb.DOKill();
        mountedBomb.localScale = mountedScale * 0.2f;
        mountedBomb.DOScale(mountedScale, 0.3f).SetEase(Ease.OutBack);
    }

    private static Transform FindDeepChild(Transform root, string childName)
    {
        foreach (Transform child in root)
        {
            if (child.name == childName) return child;
            Transform result = FindDeepChild(child, childName);
            if (result != null) return result;
        }
        return null;
    }
}
