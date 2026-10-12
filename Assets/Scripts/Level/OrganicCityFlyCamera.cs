using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 有機城市測試場景用的自由飛行相機：
/// WASD 移動、Q/E 下降/上升、按住滑鼠右鍵轉視角、Shift 加速、滾輪調整速度、
/// R 換新種子重新生成、F 回到城市上空。
/// </summary>
public class OrganicCityFlyCamera : MonoBehaviour
{
    [SerializeField] private OrganicCityGenerator generator;
    [SerializeField] private float moveSpeed = 40f;
    [SerializeField] private float boostMultiplier = 3f;
    [SerializeField] private float lookSensitivity = 0.15f;
    [SerializeField] private bool showHelp = true;

    private float yaw;
    private float pitch;

    private void Start()
    {
        Vector3 euler = transform.eulerAngles;
        yaw = euler.y;
        pitch = euler.x > 180f ? euler.x - 360f : euler.x;
        if (generator == null) generator = FindFirstObjectByType<OrganicCityGenerator>();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard == null) return;

        if (mouse != null && mouse.rightButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue();
            yaw += delta.x * lookSensitivity;
            pitch = Mathf.Clamp(pitch - delta.y * lookSensitivity, -89f, 89f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f) moveSpeed = Mathf.Clamp(moveSpeed * (scroll > 0f ? 1.15f : 0.87f), 5f, 400f);
        }

        Vector3 input = Vector3.zero;
        if (keyboard.wKey.isPressed) input.z += 1f;
        if (keyboard.sKey.isPressed) input.z -= 1f;
        if (keyboard.dKey.isPressed) input.x += 1f;
        if (keyboard.aKey.isPressed) input.x -= 1f;
        if (keyboard.eKey.isPressed) input.y += 1f;
        if (keyboard.qKey.isPressed) input.y -= 1f;

        float speed = moveSpeed * (keyboard.leftShiftKey.isPressed ? boostMultiplier : 1f);
        transform.position += (transform.rotation * new Vector3(input.x, 0f, input.z) + Vector3.up * input.y) * speed * Time.deltaTime;

        if (generator != null && keyboard.rKey.wasPressedThisFrame)
        {
            generator.Seed = Random.Range(1, 999999);
            generator.RequestRegenerate();
        }

        if (keyboard.fKey.wasPressedThisFrame) ResetView();
        if (keyboard.hKey.wasPressedThisFrame) showHelp = !showHelp;
    }

    private void ResetView()
    {
        float radius = generator != null ? generator.CityRadius : 250f;
        Vector3 center = generator != null ? generator.transform.position : Vector3.zero;
        transform.position = center + new Vector3(0f, radius * 0.8f, -radius * 1.1f);
        transform.LookAt(center);
        Vector3 euler = transform.eulerAngles;
        yaw = euler.y;
        pitch = euler.x > 180f ? euler.x - 360f : euler.x;
    }

    private void OnGUI()
    {
        if (!showHelp) return;

        string stats = generator != null ? $"種子 {generator.Seed}　{generator.LastStats}\n" : "";
        GUI.Box(new Rect(10f, 10f, 520f, 82f),
            stats +
            "WASD 移動　Q/E 下降/上升　右鍵拖曳 轉視角　Shift 加速　滾輪 調整速度\n" +
            "R 換種子重新生成　F 回到城市上空　H 隱藏說明\n" +
            "Play 中在 Inspector 調整 OrganicCityGenerator 參數也會即時重建");
    }
}
