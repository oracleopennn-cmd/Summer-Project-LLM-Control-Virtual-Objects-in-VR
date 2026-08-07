using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
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
    public string action;   // 支持: "Rotate", "Scale", "Translate", "AskUser", "RejectAmbiguous", "PointAndSelect_Rotate", "PointAndSelect_Scale", "PointAndSelect_Translate", "Clear", "None"
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

    [Header("UI Auto-Hide Settings")]
    [Tooltip("UI 提示界面默认自动隐藏的时长（秒）")]
    public float uiAutoHideDelay = 5f; // 默认 5 秒
    private Coroutine autoHideCoroutine;

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

    // New Input System
    private InputAction leftTriggerAction;
    private InputAction rightTriggerAction;
    private InputAction leftXButtonAction;

    // System Prompt
    private const string VISION_SYSTEM_PROMPT =
        "You are a VR vision and semantic parser. Analyze user commands with screenshots to establish interaction bindings.\n" +
        "Extract object names or demonstrative pronouns, strictly returning JSON: {\"source\": \"...\", \"target\": \"...\", \"action\": \"...\"}\n" +
        "1. If user explicitly says rotate/turn (e.g., 'rotate can to turn cube'): action is 'Rotate'.\n" +
        "2. If user explicitly says scale/resize (e.g., 'rotate can to scale cube'): action is 'Scale'.\n" +
        "3. If user says move/translate/position/follow (e.g., 'move this to move that'): action is 'Translate'.\n" +
        "4. CRITICAL RULE FOR AMBIGUOUS DEMONSTRATIVES: If user uses 'this' or 'that' AND only vaguely says 'control', 'connect', or 'link' WITHOUT specifying the action mode (e.g., 'I want to control this with that', 'control this with that'): action MUST BE 'RejectAmbiguous'.\n" +
        "5. If demonstrative pronouns ('this', 'that') are used WITH a specific verb: use 'PointAndSelect_Rotate', 'PointAndSelect_Scale', or 'PointAndSelect_Translate'.\n" +
        "6. If user vaguely says 'control', 'connect', or 'link' with SPECIFIC object names (e.g., 'control the cube with the can'): action is 'AskUser'.\n" +
        "7. If user wants to unbind/disconnect (e.g., 'disconnect', 'unbind', 'clear'): action is 'Clear'. Otherwise 'None'.\n" +
        "Example output: {\"source\": \"this\", \"target\": \"that\", \"action\": \"RejectAmbiguous\"}";

    private void Awake()
    {
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

        if (leftXButtonAction != null)
        {
            leftXButtonAction.performed -= OnLeftXButtonPressed;
            leftXButtonAction.Disable();
        }
    }

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
        if (isBound && currentSource != null && currentTarget != null)
        {
            if (activeAction.Equals("Rotate", StringComparison.OrdinalIgnoreCase))
            {
                Quaternion sourceDeltaRot = currentSource.transform.rotation * Quaternion.Inverse(initialSourceRot);
                currentTarget.transform.rotation = sourceDeltaRot * initialTargetRot;
            }
            else if (activeAction.Equals("Scale", StringComparison.OrdinalIgnoreCase))
            {
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
                Vector3 deltaPosition = currentSource.transform.position - initialSourcePos;
                currentTarget.transform.position = initialTargetPos + deltaPosition;
            }
        }

        HandlePointAndSelectInput();
    }

    /// <summary>
    /// 设置 UI 文本。可以通过 autoHide 控制是否要在 5 秒后自动消失。
    /// </summary>
    private void SetStatusUI(string message, bool showUI = true, bool autoHide = true)
    {
        Debug.Log($"[Status] {message}");

        // 如果之前有正在倒计时的隐藏协程，先停止它
        if (autoHideCoroutine != null)
        {
            StopCoroutine(autoHideCoroutine);
            autoHideCoroutine = null;
        }

        if (statusUIParent != null)
        {
            statusUIParent.SetActive(showUI);
        }

        if (statusTextUI != null)
        {
            statusTextUI.text = message;
        }

        // 仅在 autoHide 为 true 时开启 5 秒倒计时自动隐藏
        if (showUI && autoHide)
        {
            autoHideCoroutine = StartCoroutine(HideUIAfterDelay(uiAutoHideDelay));
        }
    }

    /// <summary>
    /// 倒计时隐藏 UI 的协程
    /// </summary>
    private IEnumerator HideUIAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (statusUIParent != null)
        {
            statusUIParent.SetActive(false);
        }
        autoHideCoroutine = null;
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
                    SetStatusUI("<color=orange>⚠️ Invalid Object!</color>\nPlease aim at an object with a [SelectableObject] script.", true, autoHide: true);
                    mustReleaseTriggerFirst = true;
                    return;
                }

                GameObject validTarget = selectedObj.gameObject;

                if (currentState == ControllerState.SelectingSource)
                {
                    currentSource = validTarget;
                    // 【关键点】：Step 2 界面保持常亮，传入 autoHide: false
                    SetStatusUI($"<color=#00FFFF>[Step 2/2]</color> Source set to: <color=yellow>{currentSource.name}</color>\nPoint at the <color=#FF8C00>Target (THAT)</color> object and press trigger.", true, autoHide: false);

                    currentState = ControllerState.SelectingTarget;
                    mustReleaseTriggerFirst = true;
                }
                else if (currentState == ControllerState.SelectingTarget)
                {
                    if (validTarget == currentSource)
                    {
                        SetStatusUI("<color=red>⚠️ Target cannot be the same as Source!</color>\nPlease point at another object.", true, autoHide: true);
                        mustReleaseTriggerFirst = true;
                        return;
                    }

                    currentTarget = validTarget;
                    ConfirmBinding(currentSource, currentTarget, activeAction);
                }
            }
            else
            {
                SetStatusUI("<color=orange>⚠️ Raycast missed!</color>\nPlease aim at a valid object and press trigger.", true, autoHide: true);
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

        initialSourceRot = currentSource.transform.rotation;
        initialTargetRot = currentTarget.transform.rotation;
        initialTargetScale = currentTarget.transform.localScale;

        initialSourcePos = currentSource.transform.position;
        initialTargetPos = currentTarget.transform.position;

        isBound = true;
        currentState = ControllerState.Idle;

        // 成功绑定提示，5 秒后自动隐藏
        SetStatusUI($"<color=#00FF00>✅ Bound ({activeAction})!</color>\n<color=yellow>{currentSource.name}</color> ➔ <color=#FF8C00>{currentTarget.name}</color>", true, autoHide: true);
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
            SetStatusUI("<color=red>❌ Screenshot capture failed!</color>", true, autoHide: true);
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
            SetStatusUI("<color=red>❌ Screenshot capture failed!</color>", true, autoHide: true);
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
                SetStatusUI($"<color=red>❌ Request Failed ({request.responseCode})</color>", true, autoHide: true);
                Debug.LogError($"[Gemini Controller] [{requestTag}] Request failed! Raw response: {request.downloadHandler.text}");
            }
        }
    }

    private bool ValidateApiKey()
    {
        if (string.IsNullOrEmpty(geminiApiKey) || geminiApiKey == "YOUR_GEMINI_API_KEY_HERE")
        {
            SetStatusUI("<color=red>❌ Gemini API Key missing in Inspector!</color>", true, autoHide: true);
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
            SetStatusUI("<color=red>❌ Failed to parse response JSON</color>", true, autoHide: true);
            Debug.LogError($"[Gemini Controller] Json parsing error: {e.Message}");
        }
        return null;
    }

    private void ApplyDynamicVisionBinding(string jsonContent)
    {
        try
        {
            BindingData data = JsonUtility.FromJson<BindingData>(jsonContent);

            // 1. 处理 Clear / Disconnect
            if (data.action.Equals("Clear", StringComparison.OrdinalIgnoreCase))
            {
                isBound = false;
                currentSource = null;
                currentTarget = null;
                activeAction = "None";
                currentState = ControllerState.Idle;

                SetStatusUI("<color=yellow>🔓 Binding Cleared / Disconnected!</color>", true, autoHide: true);
                return;
            }

            // 2. 拒绝模糊指令逻辑（RejectAmbiguous）
            if (data.action.Equals("RejectAmbiguous", StringComparison.OrdinalIgnoreCase) ||
               ((data.source.Equals("this", StringComparison.OrdinalIgnoreCase) || data.target.Equals("that", StringComparison.OrdinalIgnoreCase)) && data.action.Equals("AskUser", StringComparison.OrdinalIgnoreCase)))
            {
                isBound = false;
                currentState = ControllerState.Idle;

                SetStatusUI(
                    "<color=red>⚠️ Invalid Command!</color>\n" +
                    "When using <color=yellow>'this' / 'that'</color>, specify the action:\n" +
                    "• <i>'Rotate/Scale/Move this with that'</i>\n\n" +
                    "Or name the objects with 'Control':\n" +
                    "• <i>'Control the [Target] with the [Source]'</i>",
                    true,
                    autoHide: true
                );
                return;
            }

            // 3. 处理具象操作的手动点选 (PointAndSelect)
            if (data.action.StartsWith("PointAndSelect", StringComparison.OrdinalIgnoreCase) ||
                data.source.Equals("this", StringComparison.OrdinalIgnoreCase) ||
                data.target.Equals("that", StringComparison.OrdinalIgnoreCase))
            {
                isBound = false;
                currentSource = null;
                currentTarget = null;

                if (data.action.Contains("_"))
                {
                    activeAction = data.action.Split('_')[1];
                }
                else
                {
                    activeAction = "Rotate";
                }

                currentState = ControllerState.SelectingSource;
                mustReleaseTriggerFirst = true;

                // 【关键点】：Step 1 界面保持常亮，传入 autoHide: false
                SetStatusUI($"<color=#00FFFF>[Step 1/2] Manual Selection ({activeAction})</color>\nPoint at the <color=yellow>Source (THIS)</color> object and press trigger.", true, autoHide: false);
                return;
            }

            // 4. 场景具体物体匹配
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
                SetStatusUI($"<color=orange>⚠️ Object Match Failed!</color>\nSource: '{data.source}', Target: '{data.target}'", true, autoHide: true);
                return;
            }

            // 5. 处理具名物体的 AskUser 交互 (等待用户语音回应，也保持常亮)
            if (data.action.Equals("AskUser", StringComparison.OrdinalIgnoreCase))
            {
                pendingSource = foundSource;
                pendingTarget = foundTarget;
                currentState = ControllerState.AwaitingControlMode;

                SetStatusUI($"<color=#FFFF00>❓ How do you want to control?</color>\n<color=yellow>{foundSource.name}</color> ➔ <color=#FF8C00>{foundTarget.name}</color>\nSay <color=#00FF00>'Rotate'</color>, <color=#00FF00>'Scale'</color> or <color=#00FF00>'Move'</color>", true, autoHide: false);
                return;
            }

            // 6. 执行确定操作绑定
            if (data.action == "Rotate" || data.action == "Scale" || data.action == "Translate")
            {
                ConfirmBinding(foundSource, foundTarget, data.action);
            }
        }
        catch (Exception e)
        {
            SetStatusUI("<color=red>❌ JSON Deserialization Failed</color>", true, autoHide: true);
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