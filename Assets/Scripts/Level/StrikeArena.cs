using UnityEngine;

/// <summary>打擊專屬場景的可遊玩範圍：執行時建立外圍空氣牆與天花板 (<see cref="MapBoundary"/>)</summary>
public class StrikeArena : MonoBehaviour
{
    [SerializeField] private Vector2 min = new Vector2(-100f, -100f);
    [SerializeField] private Vector2 max = new Vector2(100f, 100f);
    [SerializeField] private float groundY;
    [SerializeField] private float ceilingY = 60f;

    public void Configure(Vector2 areaMin, Vector2 areaMax, float ground, float ceiling)
    {
        min = areaMin;
        max = areaMax;
        groundY = ground;
        ceilingY = ceiling;
    }

    private void Awake()
    {
        MapBoundary.Build(transform, min, max, groundY, ceilingY);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
        Vector3 center = new Vector3((min.x + max.x) * 0.5f, (groundY + ceilingY) * 0.5f, (min.y + max.y) * 0.5f);
        Gizmos.DrawWireCube(center, new Vector3(max.x - min.x, ceilingY - groundY, max.y - min.y));
    }
}
