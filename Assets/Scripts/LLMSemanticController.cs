using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.SceneManagement; // 引入场景管理
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// ==========================================
// 1. Data Structure Definitions
// ==========================================

[Serializable]
public class BindingData
{
    public string source;
    public string target;
    public string action;   // 支持: "Rotate", "Scale", "Translate", "AskUser", "PointAndSelect_Rotate", "PointAndSelect_Scale", "PointAndSelect_Translate", "Clear", "None"
}

[Serializable]
public class GeminiPartResponse
{
    public string text;
}

[Serializable]
public class GeminiContentResponse
{
    public GeminiPartResponse[] parts;
}

[Serializable]
public class GeminiCandidate
{
    public GeminiContentResponse content;
}

[Serializable]
public class GeminiResponse
{
    public GeminiCandidate[] candidates;
}


// ==========================================
// 2. Main Semantic Controller Script
// ==========================================

public class LLMSemanticController : MonoBehaviour
{
    [Header("UI Feedback Settings")]
    public GameObject statusUIParent;
    private TMP_Text statusTextUI;

    [Header("Gemini API Configuration")]
    public string geminiApiKey = "YOUR_GEMINI_API_KEY_HERE";
    public string modelName = "gemini-1.5-flash";

    [Header("Vision & Scene Interaction")]
    public Camera mainVRCamera;

    [Header("XR Interaction Settings (Both Hands Supported)")]
    [Tooltip("Drag Left Hand XR Ray Interactor here")]
    public XRRayInteractor leftRayInteractor;

    [Tooltip("Drag Right Hand XR Ray Interactor here")]
    public XRRayInteractor rightRayInteractor;

    // Runtime state variables
    public bool isBound = false;
    public string activeAction = "None"; // 支持: "Rotate", "Scale", "Translate"
    public GameObject currentSource;
    public GameObject currentTarget;

    // AskUser 暂存变量
    private GameObject pendingSource;
    private GameObject pendingTarget;

    // Pose, Scale & Position tracking variables
    private Quaternion initialSourceRot;
    private Quaternion initialTargetRot;
    private Vector3 initialTargetScale;

    private Vector3 initialSourcePos;
    private Vector3 initialTargetPos;

    [Header("Scaling Sensitivity")]
    [Tooltip("How sensitive the scale change is when rotating the source object.")]
    public float scaleSensitivity = 0.01f;
    [Tooltip("Minimum allowed scale for the target object.")]
    public float minScale = 0.1f;
    [Tooltip("Maximum allowed scale for the target object.")]
    public float maxScale = 5.0f;

    // State Machine
    private enum ControllerState { Idle, SelectingSource, SelectingTarget, AwaitingControlMode }
    private ControllerState currentState = ControllerState.Idle;

    private bool mustReleaseTriggerFirst = false;

    // New Input System: 支持左右手 Trigger 及 左手 X 键
    private InputAction leftTriggerAction;
    private InputAction rightTriggerAction;
    private InputAction leftXButtonAction; // 左手 X 键 Action

    // 升级版 System Prompt：区分点选模式下的动作类型 (PointAndSelect_Scale, PointAndSelect_Translate, PointAndSelect_Rotate)
    private const string VISION_SYSTEM_PROMPT =
        "You are a VR vision and semantic parser. Analyze user commands with screenshots to establish interaction bindings.\n" +
        "Extract object names or demonstrative pronouns, strictly returning JSON: {\"source\": \"...\", \"target\": \"...\", \"action\": \"...\"}\n" +
        "1. If user explicitly says rotate/turn (e.g., 'rotate can to turn cube'): action is 'Rotate'.\n" +
        "2. If user explicitly says scale/resize (e.g., 'rotate can to scale cube'): action is 'Scale'.\n" +
        "3. If user says move/translate/position/follow (e.g., 'move this to move that', 'when I move can move cube'): action is 'Translate'.\n" +
        "4. If user vaguely says 'control', 'connect', or 'link' without specifying how (e.g., 'I want to control the cube with the can'): action is 'AskUser'.\n" +
        "5. If demonstrative pronouns are used ('this', 'that'): use 'PointAndSelect_Rotate', 'PointAndSelect_Scale', or 'PointAndSelect_Translate' based on the requested verb (default to 'PointAndSelect_Rotate' if no verb is given).\n" +
        "6. If user wants to unbind, disconnect, remove connection, or stop controlling (e.g., 'disconnect', 'unbind', 'stop', 'clear', 'break link', '取消绑定', '断开'): action is 'Clear'. Otherwise 'None'.\n" +
        "Example output: {\"source\": \"this\", \"target\": \"that\", \"action\": \"PointAndSelect_Scale\"}";

    private void Awake()
    {
        // 1. Trigger 按键绑定
        leftTriggerAction = new InputAction(
            name: "LeftTriggerSelect",
            type: InputActionType.Button,
            binding: "<XRController>{LeftHand}/triggerButton"
        );
        leftTriggerAction.AddBinding("<XRController>{LeftHand}/activate");

        rightTriggerAction = new InputAction(
            name: "RightTriggerSelect",
            type: InputActionType.Button,
            binding: "<XRController>{RightHand}/triggerButton"
        );
        rightTriggerAction.AddBinding("<XRController>{RightHand}/activate");

        // 2. 左手 X 键绑定 (PrimaryButton)
        leftXButtonAction = new InputAction(
            name: "LeftXButton",
            type: InputActionType.Button,
            binding: "<XRController>{LeftHand}/primaryButton"
        );
    }

    private void OnEnable()
    {
        leftTriggerAction?.Enable();
        rightTriggerAction?.Enable();

        // 启用 X 键监听并绑定重载事件
        if (leftXButtonAction != null)
        {
            leftXButtonAction.Enable();
            leftXButtonAction.performed += OnLeftXButtonPressed;
        }
    }

    private void OnDisable()
    {
        leftTriggerAction?.Disable();
        rightTriggerAction?.Disable();

        // 禁用 X 键并解绑事件
        if (leftXButtonAction != null)
        {
            leftXButtonAction.performed -= OnLeftXButtonPressed;
            leftXButtonAction.Disable();
        }
    }

    // 左手 X 键触发：重新加载当前场景
    private void OnLeftXButtonPressed(InputAction.CallbackContext context)
    {
        Debug.Log("<color=yellow>[Scene Reload]</color> Left X button pressed. Reloading active scene...");
        int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
        SceneManager.LoadScene(currentSceneIndex);
    }

    private void Start()
    {
        if (mainVRCamera == null)
        {
            mainVRCamera = Camera.main;
        }

        AutoDetectInteractors();

        if (statusUIParent != null)
        {
            statusTextUI = statusUIParent.GetComponentInChildren<TMP_Text>();
            statusUIParent.SetActive(false);
        }
    }

    private void AutoDetectInteractors()
    {
        XRRayInteractor[] interactors = FindObjectsOfType<XRRayInteractor>();
        foreach (var interactor in interactors)
        {
            string objName = interactor.gameObject.name.ToLower();
            if (leftRayInteractor == null && (objName.Contains("left") || objName.Contains("l_")))
            {
                leftRayInteractor = interactor;
            }
            else if (rightRayInteractor == null && (objName.Contains("right") || objName.Contains("r_")))
            {
                rightRayInteractor = interactor;
            }
        }
    }

    private void Update()
    {
        // 1. 执行生效中的绑定逻辑
        if (isBound && currentSource != null && currentTarget != null)
        {
            if (activeAction.Equals("Rotate", StringComparison.OrdinalIgnoreCase))
            {
                // 旋转控制旋转
                Quaternion sourceDeltaRot = currentSource.transform.rotation * Quaternion.Inverse(initialSourceRot);
                currentTarget.transform.rotation = sourceDeltaRot * initialTargetRot;
            }
            else if (activeAction.Equals("Scale", StringComparison.OrdinalIgnoreCase))
            {
                // 旋转控制缩放
                Quaternion sourceDeltaRot = currentSource.transform.rotation * Quaternion.Inverse(initialSourceRot);
                sourceDeltaRot.ToAngleAxis(out float angle, out Vector3 axis);

                float angleSign = Vector3.Dot(axis, currentSource.transform.up) >= 0 ? 1f : -1f;
                float angleChange = angle * angleSign;

                if (angleChange > 180f) angleChange -= 360f;

                float scaleFactor = 1.0f + (angleChange * scaleSensitivity);
                Vector3 newScale = initialTargetScale * scaleFactor;

                newScale.x = Mathf.Clamp(newScale.x, minScale, maxScale);
                newScale.y = Mathf.Clamp(newScale.y, minScale, maxScale);
                newScale.z = Mathf.Clamp(newScale.z, minScale, maxScale);

                currentTarget.transform.localScale = newScale;
            }
            else if (activeAction.Equals("Translate", StringComparison.OrdinalIgnoreCase) ||
                     activeAction.Equals("Move", StringComparison.OrdinalIgnoreCase))
            {
                // 位移控制位移 (Move -> Move)
                Vector3 deltaPosition = currentSource.transform.position - initialSourcePos;
                currentTarget.transform.position = initialTargetPos + deltaPosition;
            }
        }

        // 2. 处理手动点选逻辑
        HandlePointAndSelectInput();
    }

    private void SetStatusUI(string message, bool showUI = true)
    {
        Debug.Log($"[Status] {message}");

        if (statusUIParent != null)
        {
            statusUIParent.SetActive(showUI);
        }

        if (statusTextUI != null)
        {
            statusTextUI.text = message;
        }
    }

    private void HandlePointAndSelectInput()
    {
        if (currentState != ControllerState.SelectingSource && currentState != ControllerState.SelectingTarget) return;

        bool isLeftHolding = leftTriggerAction != null && leftTriggerAction.IsPressed();
        bool isRightHolding = rightTriggerAction != null && rightTriggerAction.IsPressed();

        if (mustReleaseTriggerFirst)
        {
            if (!isLeftHolding && !isRightHolding)
            {
                mustReleaseTriggerFirst = false;
            }
            return;
        }

        bool isLeftPressed = leftTriggerAction != null && leftTriggerAction.WasPressedThisFrame();
        bool isRightPressed = rightTriggerAction != null && rightTriggerAction.WasPressedThisFrame();

        if (isLeftPressed || isRightPressed)
        {
            XRRayInteractor activeInteractor = isLeftPressed ? leftRayInteractor : rightRayInteractor;
            GameObject hitGameObject = GetHoveredObjectFromInteractor(activeInteractor);

            if (hitGameObject != null)
            {
                SelectableObject selectedObj = hitGameObject.GetComponent<SelectableObject>();
                if (selectedObj == null) selectedObj = hitGameObject.GetComponentInParent<SelectableObject>();

                if (selectedObj == null)
                {
                    SetStatusUI("<color=orange>⚠️ Invalid Object!</color>\nPlease aim at an object with a [SelectableObject] script.", true);
                    mustReleaseTriggerFirst = true;
                    return;
                }

                GameObject validTarget = selectedObj.gameObject;

                if (currentState == ControllerState.SelectingSource)
                {
                    currentSource = validTarget;
                    SetStatusUI($"<color=#00FFFF>[Step 2/2]</color> Source set to: <color=yellow>{currentSource.name}</color>\nPoint at the <color=#FF8C00>Target (THAT)</color> object and press trigger.", true);

                    currentState = ControllerState.SelectingTarget;
                    mustReleaseTriggerFirst = true;
                }
                else if (currentState == ControllerState.SelectingTarget)
                {
                    if (validTarget == currentSource)
                    {
                        SetStatusUI("<color=red>⚠️ Target cannot be the same as Source!</color>\nPlease point at another object.", true);
                        mustReleaseTriggerFirst = true;
                        return;
                    }

                    currentTarget = validTarget;

                    // 【修复关键点】：直接采用点选模式预设解析出的 activeAction（Scale / Translate / Rotate），不强制降级为 Rotate
                    ConfirmBinding(currentSource, currentTarget, activeAction);
                }
            }
            else
            {
                SetStatusUI("<color=orange>⚠️ Raycast missed!</color>\nPlease aim at a valid object and press trigger.", true);
                mustReleaseTriggerFirst = true;
            }
        }
    }

    private GameObject GetHoveredObjectFromInteractor(XRRayInteractor interactor)
    {
        if (interactor == null) return null;

        if (interactor.hasHover && interactor.interactablesHovered.Count > 0)
        {
            var interactable = interactor.interactablesHovered[0];
            if (interactable != null) return interactable.transform.gameObject;
        }

        if (interactor.TryGetCurrent3DRaycastHit(out RaycastHit xriHit))
        {
            if (xriHit.collider != null) return xriHit.collider.gameObject;
        }

        Transform rayOrigin = interactor.rayOriginTransform != null ? interactor.rayOriginTransform : interactor.transform;
        Ray fallbackRay = new Ray(rayOrigin.position, rayOrigin.forward);

        if (Physics.Raycast(fallbackRay, out RaycastHit fallbackHit, 100f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            if (fallbackHit.collider != null) return fallbackHit.collider.gameObject;
        }

        return null;
    }

    private void ConfirmBinding(GameObject sourceObj, GameObject targetObj, string actionType)
    {
        currentSource = sourceObj;
        currentTarget = targetObj;
        activeAction = actionType;

        // 记录旋转与缩放初始值
        initialSourceRot = currentSource.transform.rotation;
        initialTargetRot = currentTarget.transform.rotation;
        initialTargetScale = currentTarget.transform.localScale;

        // 记录位置初始值
        initialSourcePos = currentSource.transform.position;
        initialTargetPos = currentTarget.transform.position;

        isBound = true;
        currentState = ControllerState.Idle;

        StartCoroutine(ShowTempStatusAndHide($"<color=#00FF00>✅ Bound ({activeAction})!</color>\n<color=yellow>{currentSource.name}</color> ➔ <color=#FF8C00>{currentTarget.name}</color>", 2.5f));
    }

    private IEnumerator ShowTempStatusAndHide(string message, float delay)
    {
        SetStatusUI(message, true);
        yield return new WaitForSeconds(delay);
        if (statusUIParent != null)
        {
            statusUIParent.SetActive(false);
        }
    }

    public void SendTextWithVisionPrompt(string userInput)
    {
        if (currentState == ControllerState.AwaitingControlMode)
        {
            string textLower = userInput.ToLower().Trim();
            if (textLower.Contains("rotate") || textLower.Contains("turn"))
            {
                ConfirmBinding(pendingSource, pendingTarget, "Rotate");
                return;
            }
            else if (textLower.Contains("scale") || textLower.Contains("size") || textLower.Contains("bigger") || textLower.Contains("smaller"))
            {
                ConfirmBinding(pendingSource, pendingTarget, "Scale");
                return;
            }
            else if (textLower.Contains("move") || textLower.Contains("translate") || textLower.Contains("position"))
            {
                ConfirmBinding(pendingSource, pendingTarget, "Translate");
                return;
            }
        }

        if (!ValidateApiKey()) return;

        byte[] imageBytes = ScreenCaptureUtility.CaptureCameraView(mainVRCamera);
        if (imageBytes == null)
        {
            SetStatusUI("<color=red>❌ Screenshot capture failed!</color>", true);
            return;
        }
        string base64Image = Convert.ToBase64String(imageBytes);

        string fullPromptEscaped = EscapeJsonString(VISION_SYSTEM_PROMPT + "\n\nUser Instruction: " + userInput);
        string jsonPayload = $"{{\"contents\":[{{\"parts\":[{{\"text\":\"{fullPromptEscaped}\"}},{{\"inlineData\":{{\"mimeType\":\"image/jpeg\",\"data\":\"{base64Image}\"}}}}]}}]}}";

        StartCoroutine(SendGeminiApiRequest(jsonPayload, "Text+Vision"));
    }

    public void SendAudioWithVisionPrompt(string base64Audio, string audioMime = "audio/wav")
    {
        if (!ValidateApiKey()) return;

        byte[] imageBytes = ScreenCaptureUtility.CaptureCameraView(mainVRCamera);
        if (imageBytes == null)
        {
            SetStatusUI("<color=red>❌ Screenshot capture failed!</color>", true);
            return;
        }
        string base64Image = Convert.ToBase64String(imageBytes);

        string promptEscaped = EscapeJsonString(VISION_SYSTEM_PROMPT);
        string jsonPayload = $"{{\"contents\":[{{\"parts\":[{{\"text\":\"{promptEscaped}\"}},{{\"inlineData\":{{\"mimeType\":\"image/jpeg\",\"data\":\"{base64Image}\"}}}},{{\"inlineData\":{{\"mimeType\":\"{audioMime}\",\"data\":\"{base64Audio}\"}}}}]}}]}}";

        StartCoroutine(SendGeminiApiRequest(jsonPayload, "Audio+Vision"));
    }

    private IEnumerator SendGeminiApiRequest(string jsonPayload, string requestTag)
    {
        string cleanModelName = modelName.Trim();
        if (!cleanModelName.StartsWith("models/"))
        {
            cleanModelName = "models/" + cleanModelName;
        }

        string cleanApiKey = geminiApiKey.Trim();
        string url = $"https://generativelanguage.googleapis.com/v1beta/{cleanModelName}:generateContent?key={cleanApiKey}";

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.certificateHandler = new BypassCertificate();

            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string rawJsonResponse = request.downloadHandler.text;
                string jsonStringFromGemini = ExtractJsonFromGeminiResponse(rawJsonResponse);
                if (!string.IsNullOrEmpty(jsonStringFromGemini))
                {
                    ApplyDynamicVisionBinding(jsonStringFromGemini);
                }
            }
            else
            {
                SetStatusUI($"<color=red>❌ Request Failed ({request.responseCode})</color>", true);
                Debug.LogError($"[Gemini Controller] [{requestTag}] Request failed! Raw response: {request.downloadHandler.text}");
            }
        }
    }

    private bool ValidateApiKey()
    {
        if (string.IsNullOrEmpty(geminiApiKey) || geminiApiKey == "YOUR_GEMINI_API_KEY_HERE")
        {
            SetStatusUI("<color=red>❌ Gemini API Key missing in Inspector!</color>", true);
            return false;
        }
        return true;
    }

    private string EscapeJsonString(string str)
    {
        if (string.IsNullOrEmpty(str)) return "";
        return str.Replace("\\", "\\\\")
                  .Replace("\"", "\\\"")
                  .Replace("\n", "\\n")
                  .Replace("\r", "\\r")
                  .Replace("\t", "\\t");
    }

    private string ExtractJsonFromGeminiResponse(string rawResponse)
    {
        try
        {
            GeminiResponse responseObj = JsonUtility.FromJson<GeminiResponse>(rawResponse);
            if (responseObj != null && responseObj.candidates != null && responseObj.candidates.Length > 0)
            {
                string extractedText = responseObj.candidates[0].content.parts[0].text;
                extractedText = extractedText.Replace("```json", "").Replace("```", "").Trim();
                return extractedText;
            }
        }
        catch (Exception e)
        {
            SetStatusUI("<color=red>❌ Failed to parse response JSON</color>", true);
            Debug.LogError($"[Gemini Controller] Json parsing error: {e.Message}");
        }
        return null;
    }

    private void ApplyDynamicVisionBinding(string jsonContent)
    {
        try
        {
            BindingData data = JsonUtility.FromJson<BindingData>(jsonContent);

            // 1. 最高优先级：处理 Clear / Disconnect（直接跳过物体匹配）
            if (data.action.Equals("Clear", StringComparison.OrdinalIgnoreCase))
            {
                isBound = false;
                currentSource = null;
                currentTarget = null;
                activeAction = "None";
                currentState = ControllerState.Idle;

                if (statusUIParent != null) statusUIParent.SetActive(false);

                StartCoroutine(ShowTempStatusAndHide("<color=yellow>🔓 Binding Cleared / Disconnected!</color>", 2.0f));
                Debug.Log("<color=yellow>[Binding Cleared]</color> Interaction successfully disconnected.");
                return;
            }

            // 2. 处理手动点选 (PointAndSelect)
            if (data.action.StartsWith("PointAndSelect", StringComparison.OrdinalIgnoreCase) ||
                data.source.Equals("this", StringComparison.OrdinalIgnoreCase) ||
                data.target.Equals("that", StringComparison.OrdinalIgnoreCase))
            {
                isBound = false;
                currentSource = null;
                currentTarget = null;

                // 【关键修复】：准确解析具体的操控意图 (Scale / Translate / Rotate)
                if (data.action.Contains("_"))
                {
                    activeAction = data.action.Split('_')[1];
                }
                else
                {
                    activeAction = "Rotate"; // 兜底处理
                }

                currentState = ControllerState.SelectingSource;
                mustReleaseTriggerFirst = true;

                SetStatusUI($"<color=#00FFFF>[Step 1/2] Manual Selection ({activeAction})</color>\nPoint at the <color=yellow>Source (THIS)</color> object and press trigger.", true);
                return;
            }

            // 3. 场景物体匹配
            SelectableObject[] sceneObjects = FindObjectsOfType<SelectableObject>();
            GameObject foundSource = null;
            GameObject foundTarget = null;

            string srcKey = string.IsNullOrEmpty(data.source) ? "" : data.source.ToLower().Trim();
            string tgtKey = string.IsNullOrEmpty(data.target) ? "" : data.target.ToLower().Trim();

            foreach (var obj in sceneObjects)
            {
                string label = string.IsNullOrEmpty(obj.objectLabel) ? "" : obj.objectLabel.ToLower();
                string gameObjectName = obj.gameObject.name.ToLower();

                if (foundSource == null && !string.IsNullOrEmpty(srcKey))
                {
                    if (label.Equals(srcKey) || gameObjectName.Equals(srcKey) || label.Contains(srcKey) || gameObjectName.Contains(srcKey))
                        foundSource = obj.gameObject;
                }

                if (foundTarget == null && !string.IsNullOrEmpty(tgtKey))
                {
                    if (label.Equals(tgtKey) || gameObjectName.Equals(tgtKey) || label.Contains(tgtKey) || gameObjectName.Contains(tgtKey))
                        foundTarget = obj.gameObject;
                }
            }

            if (foundSource == null || foundTarget == null)
            {
                SetStatusUI($"<color=orange>⚠️ Object Match Failed!</color>\nSource: '{data.source}', Target: '{data.target}'", true);
                return;
            }

            // 4. 处理模糊意图 AskUser
            if (data.action.Equals("AskUser", StringComparison.OrdinalIgnoreCase))
            {
                pendingSource = foundSource;
                pendingTarget = foundTarget;
                currentState = ControllerState.AwaitingControlMode;

                SetStatusUI($"<color=#FFFF00>❓ How do you want to control?</color>\n<color=yellow>{foundSource.name}</color> ➔ <color=#FF8C00>{foundTarget.name}</color>\nSay <color=#00FF00>'Rotate'</color>, <color=#00FF00>'Scale'</color> or <color=#00FF00>'Move'</color>", true);
                return;
            }

            // 5. 确认最终绑定策略
            if (data.action == "Rotate" || data.action == "Scale" || data.action == "Translate")
            {
                ConfirmBinding(foundSource, foundTarget, data.action);
            }
        }
        catch (Exception e)
        {
            SetStatusUI("<color=red>❌ JSON Deserialization Failed</color>", true);
            Debug.LogError($"[Gemini Controller] Deserialization error: {e.Message}");
        }
    }
}

public class BypassCertificate : CertificateHandler
{
    protected override bool ValidateCertificate(byte[] certificateData)
    {
        return true;
    }
}