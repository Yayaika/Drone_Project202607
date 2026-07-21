using UnityEngine;
using System.Collections;

public class DroneAnimator : MonoBehaviour
{
    Transform[] Props;
    public float propSpeed = 0;
    Transform trans;
    public float bobSpeed;
    public float bobHeight;
    public float wobble;
    public float wobbleSpeed;

    private DroneController1 controller;

    void Start()
    {
        trans = transform;

        controller = GetComponent<DroneController1>();
        if (controller == null)
        {
            controller = GetComponentInParent<DroneController1>();
        }

        int propCount = 0;
        Props = new Transform[4];
        foreach (Transform child in transform)
        {
            if (child.name.ToLower().Contains("motor"))
            {
                if (child.childCount > 0)
                {
                    Props[propCount] = child.GetChild(0);
                    propCount++;
                }
            }
        }
    }

    void Update()
    {
        // 如果綁定了 DroneController，則以 Controller 控制的實際 RPM 來決定動畫表現
        if (controller != null)
        {
            // 只有當引擎啟動且轉速足夠時，才執行機身的懸停浮動與擺動
            if (controller.IsEngineStarted && controller.CurrentRPM > 100f)
            {
                ApplyHoverAnimation();
            }
        }
        else
        {
            // 若沒有 DroneController，則執行原本的獨立測試動畫
            RotatePropsStandalone();
            ApplyHoverAnimation();
        }
    }

    // 原本的獨立槳葉旋轉（備用）
    private void RotatePropsStandalone()
    {
        foreach (Transform prop in Props)
        {
            if (prop != null)
            {
                prop.Rotate(0, 0, propSpeed * Time.deltaTime);
            }
        }
    }

    // 懸停與擺動動畫
    private void ApplyHoverAnimation()
    {
        Vector3 pos = trans.localPosition;
        pos.y = Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        trans.localPosition = pos;

        Vector3 rot = trans.localEulerAngles;
        rot.x = 270 + Mathf.Sin(Time.time * wobbleSpeed) * wobble;
        rot.z = Mathf.Cos(Time.time * wobbleSpeed) * wobble;
        rot.y = 0;
        trans.localEulerAngles = rot;
    }
}