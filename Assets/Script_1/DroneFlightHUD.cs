using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DroneFlightHUD : MonoBehaviour
{
    [Header("HUD References")]
    [SerializeField] private DroneController1 droneController;
    [SerializeField] private TMP_FontAsset chineseFont;
    [SerializeField] private Color horizonColor = new Color(0.2f, 1f, 0.75f, 0.9f);
    [SerializeField] private float horizonWidth = 420f;
    [SerializeField] private float pitchPixelsPerDegree = 5f;
    [SerializeField] private float maxPitchOffset = 120f;

    private GameObject firstPersonRoot;
    private GameObject flightHudLayer;
    private RectTransform horizonLine;

    private void Awake()
    {
        if (droneController == null)
        {
            droneController = GetComponentInParent<DroneController1>();
        }

        if (!BindExistingHudElements())
        {
            CreateHudElements();
        }
        CreatePickupInstructions();
    }

    private void Update()
    {
        if (droneController == null)
        {
            droneController = GetComponentInParent<DroneController1>();
            if (droneController == null) return;
        }

        bool isFirstPerson = droneController.IsFirstPerson;
        firstPersonRoot.SetActive(isFirstPerson);

        Vector3 attitude = droneController.transform.eulerAngles;
        float pitch = NormalizeAngle(attitude.x);
        float roll = NormalizeAngle(attitude.z);
        float pitchOffset = Mathf.Clamp(-pitch * pitchPixelsPerDegree, -maxPitchOffset, maxPitchOffset);
        horizonLine.anchoredPosition = new Vector2(0f, pitchOffset);
        horizonLine.localRotation = Quaternion.Euler(0f, 0f, -roll);
    }

    private void CreateHudElements()
    {
        flightHudLayer = CreateRectObject("FlightHudLayer", transform);
        flightHudLayer.transform.SetAsFirstSibling();

        firstPersonRoot = CreateRectObject("FPV_HorizonHUD", flightHudLayer.transform);
        firstPersonRoot.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 0.5f);
        firstPersonRoot.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.5f);
        firstPersonRoot.GetComponent<RectTransform>().sizeDelta = new Vector2(600f, 300f);

        GameObject horizon = new GameObject("ArtificialHorizon", typeof(RectTransform), typeof(Image));
        horizon.transform.SetParent(firstPersonRoot.transform, false);
        horizonLine = horizon.GetComponent<RectTransform>();
        horizonLine.anchorMin = new Vector2(0.5f, 0.5f);
        horizonLine.anchorMax = new Vector2(0.5f, 0.5f);
        horizonLine.sizeDelta = new Vector2(horizonWidth, 3f);
        horizon.GetComponent<Image>().color = horizonColor;

        TextMeshProUGUI centerMarker = CreateText("CenterMarker", firstPersonRoot.transform, "+", 34f);
        centerMarker.alignment = TextAlignmentOptions.Center;
        centerMarker.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        centerMarker.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        centerMarker.rectTransform.sizeDelta = new Vector2(80f, 60f);
        centerMarker.color = horizonColor;

    }

    private bool BindExistingHudElements()
    {
        Transform layer = transform.Find("FlightHudLayer");
        Transform firstPerson = layer != null ? layer.Find("FPV_HorizonHUD") : null;
        Transform horizon = firstPerson != null ? firstPerson.Find("ArtificialHorizon") : null;

        if (layer == null || firstPerson == null || horizon == null)
        {
            return false;
        }

        flightHudLayer = layer.gameObject;
        firstPersonRoot = firstPerson.gameObject;
        horizonLine = horizon.GetComponent<RectTransform>();
        return horizonLine != null;
    }

    private void CreatePickupInstructions()
    {
        Transform settingsPanel = FindDeepChild(transform, "[Panel]設置");
        if (settingsPanel == null || settingsPanel.Find("PickupInstructions") != null)
        {
            return;
        }

        TextMeshProUGUI instructions = CreateText(
            "PickupInstructions",
            settingsPanel,
            "抓取／放下：Z／Xbox A",
            24f);
        instructions.enableWordWrapping = true;
        instructions.color = Color.white;
        instructions.alignment = TextAlignmentOptions.Left;
        instructions.margin = new Vector4(8f, 4f, 8f, 4f);
        instructions.rectTransform.anchorMin = new Vector2(0.08f, 0.08f);
        instructions.rectTransform.anchorMax = new Vector2(0.92f, 0.25f);
        instructions.rectTransform.offsetMin = Vector2.zero;
        instructions.rectTransform.offsetMax = Vector2.zero;
    }

    private static GameObject CreateRectObject(string objectName, Transform parent)
    {
        GameObject created = new GameObject(objectName, typeof(RectTransform));
        created.transform.SetParent(parent, false);
        return created;
    }

    private TextMeshProUGUI CreateText(string objectName, Transform parent, string content, float fontSize)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform));
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = fontSize;
        text.font = ResolveFont();
        text.raycastTarget = false;
        return text;
    }

    private TMP_FontAsset ResolveFont()
    {
        if (chineseFont != null)
        {
            return chineseFont;
        }

        TextMeshProUGUI existingText = GetComponentInChildren<TextMeshProUGUI>(true);
        return existingText != null && existingText.font != null
            ? existingText.font
            : TMP_Settings.defaultFontAsset;
    }

    private static Transform FindDeepChild(Transform root, string childName)
    {
        foreach (Transform child in root)
        {
            if (child.name == childName)
            {
                return child;
            }

            Transform result = FindDeepChild(child, childName);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        return angle > 180f ? angle - 360f : angle;
    }
}