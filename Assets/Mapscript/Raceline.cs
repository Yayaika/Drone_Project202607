using UnityEngine;
using UnityEngine.Splines;

[RequireComponent(typeof(LineRenderer))]
public class SplineToLine : MonoBehaviour
{
    public SplineContainer trackSpline;
    public int resolution = 100; // 數字越大，線條越圓滑

    void Start()
    {
        LineRenderer lr = GetComponent<LineRenderer>();
        lr.positionCount = resolution;

        if (trackSpline != null)
        {
            for (int i = 0; i < resolution; i++)
            {
                float t = (float)i / (resolution - 1);
                lr.SetPosition(i, (Vector3)trackSpline.EvaluatePosition(t));
            }
        }
    }
}