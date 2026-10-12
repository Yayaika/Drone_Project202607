using DG.Tweening;
using UnityEngine;

/// <summary>
/// 搬運任務的取貨/送貨區域：地面光圈 + 高空可見的光柱 (DOTween 呼吸動畫)。
/// 判定採幾何計算 (水平半徑 + 高度範圍)，不需要觸發器或特定 Layer。
/// </summary>
public class DeliveryZone : MonoBehaviour
{
    public enum ZoneKind
    {
        Pickup,
        DropOff
    }

    [SerializeField] private float radius = 2.5f;
    [SerializeField] private float height = 4f;
    [SerializeField] private float beamHeight = 60f;

    public ZoneKind Kind { get; private set; }
    public float Radius => radius;

    private Transform ring;
    private Transform beam;

    public static Color PickupColor => new Color(1f, 0.8f, 0.15f);
    public static Color DropColor => new Color(0.25f, 1f, 0.55f);

    public static DeliveryZone Create(Vector3 groundPosition, ZoneKind kind, float zoneRadius = 2.5f)
    {
        GameObject zoneObject = new GameObject(kind == ZoneKind.Pickup ? "PickupZone" : "DropOffZone");
        zoneObject.transform.position = groundPosition;
        DeliveryZone zone = zoneObject.AddComponent<DeliveryZone>();
        zone.radius = zoneRadius;
        zone.Kind = kind;
        zone.BuildVisuals(kind == ZoneKind.Pickup ? PickupColor : DropColor);
        return zone;
    }

    private void BuildVisuals(Color color)
    {
        ring = CreateVisual("GroundRing", new Vector3(radius * 2f, 0.03f, radius * 2f), new Vector3(0f, 0.05f, 0f),
            RuntimeMaterials.Glow(new Color(color.r, color.g, color.b, 0.45f)));
        beam = CreateVisual("Beam", new Vector3(0.6f, beamHeight * 0.5f, 0.6f), new Vector3(0f, beamHeight * 0.5f, 0f),
            RuntimeMaterials.Glow(new Color(color.r, color.g, color.b, 0.25f)));

        ring.DOScale(new Vector3(radius * 2.2f, 0.03f, radius * 2.2f), 0.9f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine);
        beam.DOScaleX(0.9f, 0.9f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine);
    }

    private Transform CreateVisual(string visualName, Vector3 scale, Vector3 localPosition, Material material)
    {
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        visual.name = visualName;
        Destroy(visual.GetComponent<Collider>());
        visual.transform.SetParent(transform, false);
        visual.transform.localPosition = localPosition;
        visual.transform.localScale = scale;
        Renderer visualRenderer = visual.GetComponent<Renderer>();
        visualRenderer.sharedMaterial = material;
        visualRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        visualRenderer.receiveShadows = false;
        return visual.transform;
    }

    public bool Contains(Vector3 worldPosition)
    {
        Vector3 offset = worldPosition - transform.position;
        float horizontal = new Vector2(offset.x, offset.z).magnitude;
        return horizontal <= radius && offset.y >= -1f && offset.y <= height;
    }

    public void Hide()
    {
        transform.DOKill(true);
        if (ring != null) ring.DOKill();
        if (beam != null) beam.DOKill();
        transform.DOScale(0f, 0.3f).SetEase(Ease.InBack).OnComplete(() =>
        {
            if (this != null) Destroy(gameObject);
        });
    }

    private void OnDestroy()
    {
        if (ring != null) ring.DOKill();
        if (beam != null) beam.DOKill();
        transform.DOKill();
    }
}
