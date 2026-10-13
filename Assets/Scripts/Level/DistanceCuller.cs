using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 依與相機的距離分批切換路人/車輛的外觀 (Renderer 與 Animator)。
/// 遠處的物件仍持續移動，只是不繪製、不播動畫，降低大量角色/車輛的 CPU 與繪製成本。
/// 每幀只檢查一部分物件，避免單幀尖峰。
/// </summary>
public class DistanceCuller
{
    private class Entry
    {
        public Transform Target;
        public Renderer[] Renderers;
        public Animator Animator;
        public bool Visible = true;
    }

    private readonly List<Entry> entries = new List<Entry>();
    private readonly float hideDistanceSqr;
    private readonly float showDistanceSqr;
    private int cursor;

    /// <param name="hideDistance">超過此距離隱藏</param>
    /// <param name="hysteresis">回到 (hideDistance - hysteresis) 內才重新顯示，避免邊界閃爍</param>
    public DistanceCuller(float hideDistance, float hysteresis = 10f)
    {
        hideDistanceSqr = hideDistance * hideDistance;
        float show = Mathf.Max(1f, hideDistance - hysteresis);
        showDistanceSqr = show * show;
    }

    public void Add(Transform target, Animator animator = null)
    {
        entries.Add(new Entry
        {
            Target = target,
            Renderers = target.GetComponentsInChildren<Renderer>(true),
            Animator = animator
        });
    }

    /// <summary>每幀呼叫，checksPerFrame 決定每幀檢查的物件數</summary>
    public void Update(int checksPerFrame = 24)
    {
        if (entries.Count == 0) return;
        Camera camera = Camera.main;
        if (camera == null) return;
        Vector3 eye = camera.transform.position;

        int checks = Mathf.Min(checksPerFrame, entries.Count);
        for (int i = 0; i < checks; i++)
        {
            cursor = (cursor + 1) % entries.Count;
            Entry entry = entries[cursor];
            if (entry.Target == null) continue;

            float distanceSqr = (entry.Target.position - eye).sqrMagnitude;
            bool visible = entry.Visible ? distanceSqr < hideDistanceSqr : distanceSqr < showDistanceSqr;
            if (visible == entry.Visible) continue;

            entry.Visible = visible;
            foreach (Renderer renderer in entry.Renderers)
            {
                if (renderer != null) renderer.enabled = visible;
            }
            if (entry.Animator != null) entry.Animator.enabled = visible;
        }
    }
}
