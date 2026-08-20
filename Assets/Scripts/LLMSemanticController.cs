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
    public string action;   // Supports: "Rotate", "Scale", "Translate", "AskUser", "PointAndSelect_AskUser", "PointAndSelect_Rotate", "PointAndSelect_Scale", "PointAndSelect_Translate", "Clear", "Lock", "Unlock", "None"
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
    // ==========================================
    // Events for Tutorial & Stage Management
    // ==========================================
    public static Action<string, string> OnBindingCreated;
    public static Action<string> OnControlModeSwitched;
    public static Action OnBindingCleared;

    // ==========================================
    // Binding Method & Action Constants & Properties
    // ==========================================
    public const string BIND_METHOD_NAME = "Name";
    public const string BIND_METHOD_POINT = "PointAndSelect";
    public const string BIND_METHOD_NONE = "None";

    [Header("State & Tutorial Tracking")]
    public string LastBindingMethod { get; private set; } = BIND_METHOD_NONE;
    public string LastActiveAction { get; private set; } = "None";

    [Header("Lock Status & Physics Settings")]
    public bool isLocked = false;
    private Vector3 lockedPosition;
    private Quaternion lockedRotation;
    private Vector3 lockedScale;

    private bool targetHasRigidbody = false;
    private bool originalIsKinematic = false;

    [Header("Scene Reload Global Setting")]
    public static bool isSceneReloadEnabled = true;

    [Header("UI Feedback Settings")]
    public GameObject statusUIParent;
    private TMP_Text statusTextUI;

    [Header("UI Auto-Hide Settings")]
    public float uiAutoHideDelay = 5f;
    private Coroutine autoHideCoroutine;

    [Header("Hand Feedback Settings")]
    [Tooltip("拖入挂在右手手柄上的 HandFeedbackController 组件")]
    public HandFeedbackController handFeedback;

    [Header("Gemini API Configuration")]
    public string geminiApiKey = "YOUR_GEMINI_API_KEY_HERE";
    public string modelName = "gemini-1.5-flash";

    [Header("Vision & Scene Interaction")]
    public Camera mainVRCamera;

    [Header("XR Interaction Settings (Both Hands Supported)")]
    public XRRayInteractor leftRayInteractor;
    public XRRayInteractor rightRayInteractor;

    // Runtime state variables
    public bool isBound = false;
    public string activeAction = "None"; // Supports: "Rotate", "Scale", "Translate", "AskUser"
    public GameObject currentSource;
    public GameObject currentTarget;

    // Pending variables for AskUser mode
    private GameObject pendingSource;
    private GameObject pendingTarget;

    // Pose, Scale & Position tracking variables
    private Quaternion initialSourceRot;
    private Quaternion initialTargetRot;
    private Vector3 initialTargetScale;

    private Vector3 initialSourcePos;
    private Vector3 initialTargetPos;

    [Header("Scaling Sensitivity")]
    public float scaleSensitivity = 0.01f;
    public float minScale = 0.1f;
    public float maxScale = 5.0f;

    [Header("Translation Sensitivity")]
    public float translateSensitivity = 1.0f;

    // State Machine
    public enum ControllerState { Idle, SelectingSource, SelectingTarget, AwaitingControlMode }
    public ControllerState currentState = ControllerState.Idle;

    private bool mustReleaseTriggerFirst = false;
    private float lastSelectTime = 0f;
    private const float SELECT_COOLDOWN = 0.35f;

    // 请求版本锁：防止滞后的 API 返回扰乱当前操作
    private int currentRequestId = 0;

    // New Input System
    private InputAction leftTriggerAction;
    private InputAction rightTriggerAction;
    private InputAction leftXButtonAction;

    private const string VISION_SYSTEM_PROMPT =
        "You are a VR vision and semantic parser. Analyze user commands with screenshots to establish interaction bindings.\n" +
        "Extract object names or demonstrative pronouns, strictly returning JSON: {\"source\": \"...\", \"target\": \"...\", \"action\": \"...\"}\n" +
        "DEFINITIONS:\n" +
        "- 'source': The controller/tool object held or manipulated by the user to provide input (THIS/TOOL).\n" +
        "- 'target': The remote object being controlled or modified (THAT/CONTROLLED OBJECT).\n" +
        "SYNTAX RULES:\n" +
        "1. In patterns like 'Move/Rotate/Scale [Target] WITH/USING [Source]' (e.g., 'move cube with can', 'rotate that with this'):\n" +
        "   - The object after 'with'/'using' MUST BE the 'source'.\n" +
        "   - The object being manipulated MUST BE the 'target'.\n" +
        "2. In patterns like 'Use [Source] to move/rotate/scale/control [Target]' (e.g., 'use can to move cube'):\n" +
        "   - The object after 'use' is 'source'.\n" +
        "   - The object after 'to' is 'target'.\n" +
        "3. CRITICAL RULE FOR VAGUE COMMANDS ('CONTROL', 'CONNECT', 'LINK'):\n" +
        "   - If user says 'control', 'connect', or 'link' with specific object names: action MUST BE 'AskUser'.\n" +
        "   - If user uses demonstrative pronouns 'this' or 'that' with only 'control'/'connect'/'link' (e.g., 'control this with that'): action MUST BE 'PointAndSelect_AskUser'.\n" +
        "4. ACTION MAPPING:\n" +
        "   - Rotate/turn -> 'Rotate'\n" +
        "   - Scale/resize/size -> 'Scale'\n" +
        "   - Move/translate/position/follow -> 'Translate'\n" +
        "5. If demonstrative pronouns ('this', 'that') are used WITH a specific verb: use 'PointAndSelect_Rotate', 'PointAndSelect_Scale', or 'PointAndSelect_Translate'.\n" +
        "6. CRITICAL AUDIO RULE: If the user says 'disconnect', 'unbind', 'clear', or if the audio sounds phonetically like 'this connect', return 'Clear'.\n" +
        "7. If user explicitly requests to lock/freeze: action is 'Lock'. Unfreeze/unlock: action is 'Unlock'. Otherwise 'None'.\n" +
        "Example output: {\"source\": \"that\", \"target\": \"this\", \"action\": \"PointAndSelect_AskUser\"}";

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
        if (!isSceneReloadEnabled)
        {
            Debug.Log("<color=yellow>[Scene Reload]</color> Left X Button Reload is disabled.");
            return;
        }

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

        // 自动寻找场景中可能存在的 HandFeedbackController
        if (handFeedback == null)
        {
#if UNITY_2023_1_OR_NEWER
            handFeedback = FindFirstObjectByType<HandFeedbackController>();
#else
            handFeedback = FindObjectOfType<HandFeedbackController>();
#endif
        }
    }

    private void AutoDetectInteractors()
    {
#if UNITY_2023_1_OR_NEWER
        XRRayInteractor[] interactors = FindObjectsByType<XRRayInteractor>(FindObjectsSortMode.None);
#else
        XRRayInteractor[] interactors = FindObjectsOfType<XRRayInteractor>();
#endif

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
        // ==========================================
        // 💡 仅监听右手扳机按下，触发 Listening
        // ==========================================
        if (rightTriggerAction != null && rightTriggerAction.WasPressedThisFrame())
        {
            if (handFeedback != null)
            {
                handFeedback.StartListening();
            }
        }

        // ==========================================
        // 原有的锁定与跟随逻辑保持不变
        // ==========================================
        if (isLocked && currentTarget != null)
        {
            currentTarget.transform.position = lockedPosition;
            currentTarget.transform.rotation = lockedRotation;
            currentTarget.transform.localScale = lockedScale;
        }
        else if (isBound && currentSource != null && currentTarget != null)
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
                currentTarget.transform.position = initialTargetPos + (deltaPosition * translateSensitivity);
            }
        }

        HandlePointAndSelectInput();
    }

    public void RebindSelectableObjects()
    {
        ForceResetBinding();
        Debug.Log("<color=yellow>[LLM Controller]</color> Objects rebound and state cleared for new stage.");
    }

    public bool SwitchControlMode(string newAction)
    {
        if (!isBound || currentSource == null || currentTarget == null) return false;

        string normalizedAction = newAction;
        if (newAction.Equals("Move", StringComparison.OrdinalIgnoreCase))
        {
            normalizedAction = "Translate";
        }

        if (!normalizedAction.Equals("Rotate", StringComparison.OrdinalIgnoreCase) &&
            !normalizedAction.Equals("Scale", StringComparison.OrdinalIgnoreCase) &&
            !normalizedAction.Equals("Translate", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        UnlockTarget();

        activeAction = normalizedAction;
        LastActiveAction = normalizedAction;

        if (currentTarget.TryGetComponent<Rigidbody>(out Rigidbody rb))
        {
            targetHasRigidbody = true;
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector3.zero;
#else
            rb.velocity = Vector3.zero;
#endif
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        initialSourceRot = currentSource.transform.rotation;
        initialTargetRot = currentTarget.transform.rotation;
        initialTargetScale = currentTarget.transform.localScale;
        initialSourcePos = currentSource.transform.position;
        initialTargetPos = currentTarget.transform.position;

        SetStatusUI($"<color=#00FF00>[Mode Switched]</color> [{activeAction}]!\n<color=yellow>{currentSource.name}</color> -> <color=#FF8C00>{currentTarget.name}</color>", true, autoHide: true);
        Debug.Log($"<color=cyan>[LLM Controller]</color> Connection mode directly switched to: {activeAction}");

        OnBindingCreated?.Invoke(currentSource.name, currentTarget.name);
        OnControlModeSwitched?.Invoke(activeAction);

        return true;
    }

    public void LockTarget()
    {
        if (currentTarget == null)
        {
            SetStatusUI("<color=orange>[Lock Failed]</color>\nNo active target object to lock.", true, autoHide: true);
            return;
        }

        if (currentTarget.TryGetComponent<Rigidbody>(out Rigidbody rb))
        {
            targetHasRigidbody = true;
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector3.zero;
#else
            rb.velocity = Vector3.zero;
#endif
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        isLocked = true;
        lockedPosition = currentTarget.transform.position;
        lockedRotation = currentTarget.transform.rotation;
        lockedScale = currentTarget.transform.localScale;

        SetStatusUI($"<color=#FFD700>[Target Locked]</color>\n<color=yellow>{currentTarget.name}</color> position & scale frozen.", true, autoHide: true);
        Debug.Log($"[LLM Controller] Target [{currentTarget.name}] locked.");
    }

    public void UnlockTarget()
    {
        if (isLocked)
        {
            isLocked = false;

            if (isBound && currentSource != null && currentTarget != null)
            {
                initialTargetPos = currentTarget.transform.position;
                initialSourcePos = currentSource.transform.position;

                initialTargetRot = currentTarget.transform.rotation;
                initialSourceRot = currentSource.transform.rotation;

                initialTargetScale = currentTarget.transform.localScale;
            }

            SetStatusUI($"<color=#00FF00>🔓 Target Unlocked!</color>\n<color=yellow>{(currentTarget != null ? currentTarget.name : "Target")}</color> resumed.", true, autoHide: true);
            Debug.Log("[LLM Controller] Target unlocked. Baselines reset to current transform.");
        }
    }

    /// <summary>
    /// 核心修复：无条件恢复 Target 物体的原始物理刚体与重力状态
    /// </summary>
    private void RestoreTargetPhysics()
    {
        if (currentTarget != null && targetHasRigidbody && currentTarget.TryGetComponent<Rigidbody>(out Rigidbody rb))
        {
            rb.isKinematic = originalIsKinematic; // 恢复到绑定前的原始 isKinematic 状态
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector3.zero;
#else
            rb.velocity = Vector3.zero;
#endif
            rb.angularVelocity = Vector3.zero;
        }

        targetHasRigidbody = false;
        isLocked = false;
    }

    public void ForceResetBinding()
    {
        currentRequestId++;

        RestoreTargetPhysics();

        if (handFeedback != null)
        {
            handFeedback.StopFeedback(false, "");
        }

        isBound = false;
        currentSource = null;
        currentTarget = null;
        pendingSource = null;
        pendingTarget = null;
        activeAction = "None";
        LastBindingMethod = BIND_METHOD_NONE;
        LastActiveAction = "None";
        currentState = ControllerState.Idle;

        if (statusUIParent != null) statusUIParent.SetActive(false);
        Debug.Log("<color=yellow>[LLM Controller]</color> State force reset by Manager.");
    }

    private void SetStatusUI(string message, bool showUI = true, bool autoHide = true)
    {
        Debug.Log($"[Status] {message}");

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

        if (showUI && autoHide)
        {
            autoHideCoroutine = StartCoroutine(HideUIAfterDelay(uiAutoHideDelay));
        }
    }

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

        if (Time.time - lastSelectTime < SELECT_COOLDOWN) return;

        bool isLeftPressed = leftTriggerAction != null && leftTriggerAction.WasPressedThisFrame();
        bool isRightPressed = rightTriggerAction != null && rightTriggerAction.WasPressedThisFrame();

        if (isLeftPressed || isRightPressed)
        {
            XRRayInteractor activeInteractor = isLeftPressed ? leftRayInteractor : rightRayInteractor;
            GameObject hitGameObject = GetHoveredObjectFromInteractor(activeInteractor);

            if (hitGameObject != null)
            {
                SelectableObject selectedObj = hitGameObject.GetComponentInParent<SelectableObject>();
                if (selectedObj == null) selectedObj = hitGameObject.GetComponentInChildren<SelectableObject>();

                if (selectedObj == null)
                {
                    SetStatusUI("<color=orange>[Invalid Object]</color>\nPlease aim at an object with a [SelectableObject] script.", true, autoHide: true);
                    mustReleaseTriggerFirst = true;
                    return;
                }

                GameObject validTarget = selectedObj.gameObject;
                lastSelectTime = Time.time;

                if (currentState == ControllerState.SelectingSource)
                {
                    currentSource = validTarget;
                    SetStatusUI($"<color=#00FFFF>[Step 2/2]</color> Source: <color=yellow>{currentSource.name}</color>\nNow point at <color=#FF8C00>Target (THAT)</color> and press trigger.", true, autoHide: false);

                    currentState = ControllerState.SelectingTarget;
                    mustReleaseTriggerFirst = true;
                }
                else if (currentState == ControllerState.SelectingTarget)
                {
                    if (validTarget == currentSource)
                    {
                        SetStatusUI("<color=red>[Error]</color> Target cannot be the same as Source!\nPlease point at another object.", true, autoHide: true);
                        mustReleaseTriggerFirst = true;
                        return;
                    }

                    currentTarget = validTarget;

                    if (activeAction.Equals("AskUser", StringComparison.OrdinalIgnoreCase))
                    {
                        pendingSource = currentSource;
                        pendingTarget = currentTarget;
                        currentState = ControllerState.AwaitingControlMode;
                        LastBindingMethod = BIND_METHOD_POINT;

                        SetStatusUI($"<color=#FFFF00>[Action Required]</color>\nObjects selected:\n<color=yellow>{pendingSource.name}</color> -> <color=#FF8C00>{pendingTarget.name}</color>\nSay <color=#00FF00>'Rotate'</color>, <color=#00FF00>'Scale'</color> or <color=#00FF00>'Move'</color>", true, autoHide: false);
                        return;
                    }

                    ConfirmBinding(currentSource, currentTarget, activeAction);
                }
            }
            else
            {
                SetStatusUI("<color=orange>[Raycast Missed]</color>\nPlease aim at a valid object and press trigger.", true, autoHide: true);
                mustReleaseTriggerFirst = true;
            }
        }
    }

    private GameObject GetHoveredObjectFromInteractor(XRRayInteractor interactor)
    {
        if (interactor == null) return null;

        if (interactor.hasSelection && interactor.interactablesSelected.Count > 0)
        {
            var interactable = interactor.interactablesSelected[0];
            if (interactable is Component comp) return comp.gameObject;
        }

        if (interactor.hasHover && interactor.interactablesHovered.Count > 0)
        {
            var interactable = interactor.interactablesHovered[0];
            if (interactable is Component comp) return comp.gameObject;
        }

        if (interactor.TryGetCurrent3DRaycastHit(out RaycastHit xriHit))
        {
            if (xriHit.collider != null) return xriHit.collider.gameObject;
        }

        Transform rayOrigin = interactor.rayOriginTransform != null ? interactor.rayOriginTransform : interactor.transform;
        Ray fallbackRay = new Ray(rayOrigin.position, rayOrigin.forward);

        if (Physics.Raycast(fallbackRay, out RaycastHit fallbackHit, 100f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (fallbackHit.collider != null) return fallbackHit.collider.gameObject;
        }

        return null;
    }

    public string ConfirmBinding(GameObject sourceObj, GameObject targetObj, string actionType)
    {
        // 绑定新物体前，先恢复旧物体的物理状态
        RestoreTargetPhysics();

        currentSource = sourceObj;
        currentTarget = targetObj;
        activeAction = actionType;
        LastActiveAction = actionType;

        initialSourceRot = currentSource.transform.rotation;
        initialTargetRot = currentTarget.transform.rotation;
        initialTargetScale = currentTarget.transform.localScale;

        initialSourcePos = currentSource.transform.position;
        initialTargetPos = currentTarget.transform.position;

        if (currentTarget.TryGetComponent<Rigidbody>(out Rigidbody rb))
        {
            targetHasRigidbody = true;
            originalIsKinematic = rb.isKinematic;

#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector3.zero;
#else
            rb.velocity = Vector3.zero;
#endif
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }
        else
        {
            targetHasRigidbody = false;
        }

        isBound = true;

        string bindMethod = (LastBindingMethod == BIND_METHOD_POINT ||
                             currentState == ControllerState.SelectingTarget ||
                             currentState == ControllerState.SelectingSource) ? BIND_METHOD_POINT : BIND_METHOD_NAME;

        LastBindingMethod = bindMethod;
        currentState = ControllerState.Idle;

        SetStatusUI($"<color=#00FF00>[Bound ({activeAction})] [{bindMethod}]!</color>\n<color=yellow>{currentSource.name}</color> -> <color=#FF8C00>{currentTarget.name}</color>", true, autoHide: true);

        if (handFeedback != null)
        {
            handFeedback.StopFeedback(true, "Bound!");
        }

        OnBindingCreated?.Invoke(currentSource.name, currentTarget.name);

        return bindMethod;
    }

    public string SendTextWithVisionPrompt(string userInput)
    {
        string textLower = userInput.ToLower().Trim();

        if (textLower == "lock" || textLower == "freeze")
        {
            LockTarget();
            return "Lock";
        }
        if (textLower == "unlock" || textLower == "unfreeze")
        {
            UnlockTarget();
            return "Unlock";
        }

        if (currentState == ControllerState.AwaitingControlMode)
        {
            if (textLower.Contains("rotate") || textLower.Contains("turn") || textLower.Contains("旋转"))
            {
                return ConfirmBinding(pendingSource, pendingTarget, "Rotate");
            }
            else if (textLower.Contains("scale") || textLower.Contains("size") || textLower.Contains("bigger") || textLower.Contains("smaller") || textLower.Contains("缩放"))
            {
                return ConfirmBinding(pendingSource, pendingTarget, "Scale");
            }
            else if (textLower.Contains("move") || textLower.Contains("translate") || textLower.Contains("position") || textLower.Contains("移动"))
            {
                return ConfirmBinding(pendingSource, pendingTarget, "Translate");
            }
        }

        if (isBound && currentSource != null && currentTarget != null)
        {
            if (textLower == "rotate" || textLower == "turn" || textLower == "旋转")
            {
                if (SwitchControlMode("Rotate")) return "Rotate";
            }
            else if (textLower == "scale" || textLower == "size" || textLower == "resize" || textLower == "缩放")
            {
                if (SwitchControlMode("Scale")) return "Scale";
            }
            else if (textLower == "move" || textLower == "translate" || textLower == "position" || textLower == "移动")
            {
                if (SwitchControlMode("Translate")) return "Translate";
            }
        }

        if (!ValidateApiKey()) return BIND_METHOD_NONE;

        byte[] imageBytes = ScreenCaptureUtility.CaptureCameraView(mainVRCamera);
        if (imageBytes == null)
        {
            SetStatusUI("<color=red>[Error]</color> Screenshot capture failed!", true, autoHide: true);
            return BIND_METHOD_NONE;
        }
        string base64Image = Convert.ToBase64String(imageBytes);

        currentRequestId++;

        string fullPromptEscaped = EscapeJsonString(VISION_SYSTEM_PROMPT + "\n\nUser Instruction: " + userInput);
        string jsonPayload = $"{{\"contents\":[{{\"parts\":[{{\"text\":\"{fullPromptEscaped}\"}},{{\"inlineData\":{{\"mimeType\":\"image/jpeg\",\"data\":\"{base64Image}\"}}}}]}}]}}";

        StartCoroutine(SendGeminiApiRequest(jsonPayload, "Text+Vision", currentRequestId));
        return "PendingAPI";
    }

    public void SendAudioWithVisionPrompt(string base64Audio, string audioMime = "audio/wav")
    {
        if (!ValidateApiKey()) return;

        byte[] imageBytes = ScreenCaptureUtility.CaptureCameraView(mainVRCamera);
        if (imageBytes == null)
        {
            SetStatusUI("<color=red>[Error]</color> Screenshot capture failed!", true, autoHide: true);
            return;
        }
        string base64Image = Convert.ToBase64String(imageBytes);

        currentRequestId++;

        string promptEscaped = EscapeJsonString(VISION_SYSTEM_PROMPT);
        string jsonPayload = $"{{\"contents\":[{{\"parts\":[{{\"text\":\"{promptEscaped}\"}},{{\"inlineData\":{{\"mimeType\":\"image/jpeg\",\"data\":\"{base64Image}\"}}}},{{\"inlineData\":{{\"mimeType\":\"{audioMime}\",\"data\":\"{base64Audio}\"}}}}]}}]}}";

        StartCoroutine(SendGeminiApiRequest(jsonPayload, "Audio+Vision", currentRequestId));
    }

    private IEnumerator SendGeminiApiRequest(string jsonPayload, string requestTag, int requestId)
    {
        // 💡 开启手柄上的 Processing 动效
        if (handFeedback != null)
        {
            handFeedback.StartProcessing();
        }

        string cleanModelName = modelName.Trim();
        if (!cleanModelName.StartsWith("models/"))
        {
            cleanModelName = "models/" + cleanModelName;
        }

        string cleanApiKey = geminiApiKey.Trim();
        string url = $"https://generativelanguage.googleapis.com/v1beta/{cleanModelName}:generateContent?key={cleanApiKey}";

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (requestId != currentRequestId)
            {
                Debug.Log("<color=yellow>[LLM Controller]</color> Discarding stale Gemini response.");
                if (handFeedback != null) handFeedback.StopFeedback(false, "Stale");
                yield break;
            }

            if (request.result == UnityWebRequest.Result.Success)
            {
                string rawJsonResponse = request.downloadHandler.text;
                string jsonStringFromGemini = ExtractJsonFromGeminiResponse(rawJsonResponse);
                if (!string.IsNullOrEmpty(jsonStringFromGemini))
                {
                    ApplyDynamicVisionBinding(jsonStringFromGemini);
                }

                // 💡 成功返回：停止反馈并提示 Done
                if (handFeedback != null)
                {
                    handFeedback.StopFeedback(true, "Done!");
                }
            }
            else
            {
                SetStatusUI($"<color=red>[Request Failed]</color> ({request.responseCode})", true, autoHide: true);
                Debug.LogError($"[Gemini Controller] [{requestTag}] Request failed! Raw response: {request.downloadHandler.text}");

                // 💡 失败返回：停止反馈并提示 Failed
                if (handFeedback != null)
                {
                    handFeedback.StopFeedback(false, "Failed");
                }
            }
        }
    }

    private bool ValidateApiKey()
    {
        if (string.IsNullOrEmpty(geminiApiKey) || geminiApiKey == "YOUR_GEMINI_API_KEY_HERE")
        {
            SetStatusUI("<color=red>[API Key Missing]</color> Gemini API Key missing in Inspector!", true, autoHide: true);
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
                extractedText = extractedText.Replace("```json", "").Replace("```JSON", "").Replace("```", "").Trim();

                Debug.Log($"<color=#00FFFF>[Gemini JSON Output]</color> {extractedText}");
                return extractedText;
            }
        }
        catch (Exception e)
        {
            SetStatusUI("<color=red>[Error]</color> Failed to parse response JSON", true, autoHide: true);
            Debug.LogError($"[Gemini Controller] Json parsing error: {e.Message}");
        }
        return null;
    }

    public string ApplyDynamicVisionBinding(string jsonContent)
    {
        if (string.IsNullOrWhiteSpace(jsonContent)) return "None";

        string rawTextLower = jsonContent.ToLower().Trim();

        // 1. 优先处理纯文本断开
        if (rawTextLower.Contains("clear") || rawTextLower.Contains("disconnect") || rawTextLower.Contains("unbind") || rawTextLower.Contains("this connect"))
        {
            RestoreTargetPhysics();

            isBound = false;
            currentSource = null;
            currentTarget = null;
            activeAction = "None";
            LastBindingMethod = BIND_METHOD_NONE;
            LastActiveAction = "None";
            currentState = ControllerState.Idle;

            SetStatusUI("<color=yellow>[Binding Cleared]</color> Disconnected!", true, autoHide: true);

            if (handFeedback != null)
            {
                handFeedback.StopFeedback(true, "Disconnected");
            }

            OnBindingCleared?.Invoke();
            return "Clear";
        }

        try
        {
            BindingData data = JsonUtility.FromJson<BindingData>(jsonContent);
            if (data == null) throw new Exception("Parsed data is null");

            if (data.action.Equals("Lock", StringComparison.OrdinalIgnoreCase))
            {
                LockTarget();
                if (handFeedback != null) handFeedback.StopFeedback(true, "Locked");
                return "Lock";
            }
            if (data.action.Equals("Unlock", StringComparison.OrdinalIgnoreCase))
            {
                UnlockTarget();
                if (handFeedback != null) handFeedback.StopFeedback(true, "Unlocked");
                return "Unlock";
            }

            if (data.action.Equals("Clear", StringComparison.OrdinalIgnoreCase))
            {
                RestoreTargetPhysics();

                isBound = false;
                currentSource = null;
                currentTarget = null;
                activeAction = "None";
                LastBindingMethod = BIND_METHOD_NONE;
                LastActiveAction = "None";
                currentState = ControllerState.Idle;

                SetStatusUI("<color=yellow>[Binding Cleared]</color> Disconnected!", true, autoHide: true);

                if (handFeedback != null)
                {
                    handFeedback.StopFeedback(true, "Disconnected");
                }

                OnBindingCleared?.Invoke();
                return "Clear";
            }

            // 判断是否是真正的具体具名物体（排除 this, that, none 等占位符）
            string srcKey = string.IsNullOrEmpty(data.source) ? "" : data.source.ToLower().Trim();
            string tgtKey = string.IsNullOrEmpty(data.target) ? "" : data.target.ToLower().Trim();

            bool isSourcePronounOrEmpty = string.IsNullOrEmpty(srcKey) || srcKey == "this" || srcKey == "that" || srcKey == "none";
            bool isTargetPronounOrEmpty = string.IsNullOrEmpty(tgtKey) || tgtKey == "this" || tgtKey == "that" || tgtKey == "none";
            bool hasRealNamedObjects = (!isSourcePronounOrEmpty || !isTargetPronounOrEmpty) && (srcKey != "this" && srcKey != "that" && tgtKey != "this" && tgtKey != "that");

            // 若当前已建立连接，且没有指定全新的具名实体物体，则直接切换当前连接模式
            if (isBound && currentSource != null && currentTarget != null && !hasRealNamedObjects)
            {
                string rawAction = (data.action ?? "").ToLower();
                string targetMode = null;

                if (rawAction.Contains("rotate") || rawAction.Contains("turn"))
                {
                    targetMode = "Rotate";
                }
                else if (rawAction.Contains("scale") || rawAction.Contains("size") || rawAction.Contains("resize"))
                {
                    targetMode = "Scale";
                }
                else if (rawAction.Contains("translate") || rawAction.Contains("move") || rawAction.Contains("position"))
                {
                    targetMode = "Translate";
                }

                if (!string.IsNullOrEmpty(targetMode))
                {
                    SwitchControlMode(targetMode);
                    if (handFeedback != null) handFeedback.StopFeedback(true, $"Mode: {targetMode}");
                    return targetMode;
                }
            }

            if (data.action.Equals("RejectAmbiguous", StringComparison.OrdinalIgnoreCase) ||
               ((data.source.Equals("this", StringComparison.OrdinalIgnoreCase) || data.target.Equals("that", StringComparison.OrdinalIgnoreCase)) && data.action.Equals("AskUser", StringComparison.OrdinalIgnoreCase)))
            {
                isBound = false;
                currentState = ControllerState.Idle;

                SetStatusUI(
                    "<color=red>[Invalid Command]</color>\n" +
                    "When using <color=yellow>'this' / 'that'</color>, specify the action:\n" +
                    "- <i>'Rotate/Scale/Move this with that'</i>\n\n" +
                    "Or name the objects with 'Control':\n" +
                    "- <i>'Control the [Target] with the [Source]'</i>",
                    true,
                    autoHide: true
                );

                if (handFeedback != null) handFeedback.StopFeedback(false, "Ambiguous");
                return "RejectAmbiguous";
            }

            // 指向性代词 PointAndSelect 分支
            if (data.action.StartsWith("PointAndSelect", StringComparison.OrdinalIgnoreCase) ||
                data.source.Equals("this", StringComparison.OrdinalIgnoreCase) ||
                data.target.Equals("that", StringComparison.OrdinalIgnoreCase))
            {
                RestoreTargetPhysics();

                isBound = false;
                currentSource = null;
                currentTarget = null;

                if (data.action.Contains("_"))
                {
                    activeAction = data.action.Split('_')[1];
                }
                else if (data.action.Equals("Translate", StringComparison.OrdinalIgnoreCase) || data.action.Equals("Move", StringComparison.OrdinalIgnoreCase))
                {
                    activeAction = "Translate";
                }
                else if (data.action.Equals("Scale", StringComparison.OrdinalIgnoreCase))
                {
                    activeAction = "Scale";
                }
                else if (data.action.Equals("AskUser", StringComparison.OrdinalIgnoreCase))
                {
                    activeAction = "AskUser";
                }
                else
                {
                    activeAction = "Rotate";
                }

                LastActiveAction = activeAction;
                currentState = ControllerState.SelectingSource;
                mustReleaseTriggerFirst = true;

                SetStatusUI($"<color=#00FFFF>[Step 1/2]</color> Manual Selection\nPoint at the <color=yellow>Source (THIS)</color> object and press trigger.", true, autoHide: false);
                if (handFeedback != null) handFeedback.StopFeedback(true, "Select Source");
                return BIND_METHOD_POINT;
            }

#if UNITY_2023_1_OR_NEWER
            SelectableObject[] sceneObjects = FindObjectsByType<SelectableObject>(FindObjectsSortMode.None);
#else
            SelectableObject[] sceneObjects = FindObjectsOfType<SelectableObject>();
#endif

            GameObject foundSource = null;
            GameObject foundTarget = null;

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
                SetStatusUI($"<color=orange>[Match Failed]</color>\nSource: '{data.source}', Target: '{data.target}'", true, autoHide: true);
                if (handFeedback != null) handFeedback.StopFeedback(false, "No Match");
                return "MatchFailed";
            }

            // 具名物体的 AskUser
            if (data.action.Equals("AskUser", StringComparison.OrdinalIgnoreCase))
            {
                RestoreTargetPhysics();
                isBound = false;

                pendingSource = foundSource;
                pendingTarget = foundTarget;
                currentState = ControllerState.AwaitingControlMode;
                LastBindingMethod = BIND_METHOD_NAME;

                SetStatusUI($"<color=#FFFF00>[Action Required]</color>\nHow do you want to control?\n<color=yellow>{foundSource.name}</color> -> <color=#FF8C00>{foundTarget.name}</color>\nSay <color=#00FF00>'Rotate'</color>, <color=#00FF00>'Scale'</color> or <color=#00FF00>'Move'</color>", true, autoHide: false);
                if (handFeedback != null) handFeedback.StopFeedback(true, "Say Action");
                return "AskUser";
            }

            // 完整句子指令：直接确认绑定
            if (data.action == "Rotate" || data.action == "Scale" || data.action == "Translate" || data.action == "Move")
            {
                return ConfirmBinding(foundSource, foundTarget, data.action);
            }
        }
        catch (Exception e)
        {
            // 纯文本兜底
            if (isBound && currentSource != null && currentTarget != null)
            {
                if (rawTextLower.Contains("rotate") || rawTextLower.Contains("turn"))
                {
                    SwitchControlMode("Rotate");
                    if (handFeedback != null) handFeedback.StopFeedback(true, "Rotate");
                    return "Rotate";
                }
                if (rawTextLower.Contains("scale") || rawTextLower.Contains("size") || rawTextLower.Contains("resize"))
                {
                    SwitchControlMode("Scale");
                    if (handFeedback != null) handFeedback.StopFeedback(true, "Scale");
                    return "Scale";
                }
                if (rawTextLower.Contains("translate") || rawTextLower.Contains("move") || rawTextLower.Contains("position"))
                {
                    SwitchControlMode("Translate");
                    if (handFeedback != null) handFeedback.StopFeedback(true, "Translate");
                    return "Translate";
                }
            }

            SetStatusUI("<color=red>[Error]</color> JSON Deserialization Failed", true, autoHide: true);
            Debug.LogError($"[Gemini Controller] Deserialization error: {e.Message}\nRaw text was: {jsonContent}");
            if (handFeedback != null) handFeedback.StopFeedback(false, "Parse Error");
            return "Error";
        }

        return "None";
    }
}