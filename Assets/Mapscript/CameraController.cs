using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;


public class CameraController : MonoBehaviour
{
    [Header("跟隨目標")]
    public Transform target;        // 把主角拖到這裡
    public Vector3 targetOffset = new Vector3(0, 1.5f, 0); // 鎖定主角頭部上方一點，避免看著腳

    [Header("視角設定")]
    public float distance = 5f;     // 攝影機距離主角多遠
    public float mouseSensitivity = 2f; // 滑鼠靈敏度

    // 紀錄目前的旋轉角度
    private float rotationY = 0f;   // 左右旋轉
    private float rotationX = 0f;   // 上下旋轉

    public float turnSpeed = 0.1f;

    void Start()
    {
        // 遊戲開始時，隱藏並鎖定滑鼠游標在螢幕正中央 (按 Esc 可以叫出游標)
        Cursor.lockState = CursorLockMode.Locked;
    }

    void LateUpdate()
    {
        if (target == null) return;

        // 1. 讀取滑鼠的移動量 (Mouse X 是左右，Mouse Y 是上下)
        float mouseX = 0f;
        float mouseY = 0f;

        if (Mouse.current != null)
        {
    // 讀取滑鼠瞬間的 X 與 Y 軸位移量
          mouseX = Mouse.current.delta.x.ReadValue() * turnSpeed; 
          mouseY = Mouse.current.delta.y.ReadValue() * turnSpeed;
        }

        // 2. 累加旋轉角度
        rotationY += mouseX; // 左右看
        rotationX -= mouseY; // 上下看 (減號是因為滑鼠往上推，我們通常希望畫面往上看)

        // 3. 限制上下看的角度，避免攝影機「翻跟斗」 (例如只能看天上 60 度到腳下 40 度)
        rotationX = Mathf.Clamp(rotationX, -40f, 60f);

        // 4. 計算出攝影機的 3D 旋轉角度
        Quaternion rotation = Quaternion.Euler(rotationX, rotationY, 0);

        // 5. 計算攝影機的位置：目標位置 - (攝影機面向的方向 * 距離)
        Vector3 focusPosition = target.position + targetOffset;
        Vector3 position = focusPosition - (rotation * Vector3.forward * distance);

        // 6. 套用位置與旋轉到攝影機上
        transform.rotation = rotation;
        transform.position = position;
    }
}