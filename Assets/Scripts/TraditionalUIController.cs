using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class TraditionalUIController : MonoBehaviour
{
    public static TraditionalUIController Instance { get; private set; }

    public static Action<string, string> OnBindingCreated;
    public static Action<string> OnControlModeSwitched;
    public static Action OnBindingCleared;

    public enum UIWorkflowState { Idle, AwaitingModeClick, AwaitingTargetPoint, Bound }

    [Header("Current State")]
    public UIWorkflowState currentState = UIWorkflowState.Idle;
    public bool isBound = false;
    public bool isLocked = false;
    public string activeAction = "None";

    [Header("Bound Objects")]
    public GameObject currentSource;
    public GameObject currentTarget;

    private bool targetHasRigidbody = false;
    private bool originalIsKinematic = false;

    private Quaternion initialSourceRot;
    private Quaternion initialTargetRot;
    private Vector3 initialTargetScale;
    private Vector3 initialSourcePos;
    private Vector3 initialTargetPos;

    private Vector3 lockedPosition;
    private Quaternion lockedRotation;
    private Vector3 lockedScale;

    [Header("Sensitivity Settings")]
    public float scaleSensitivity = 0.01f;
    public float minScale = 0.1f;
    public float maxScale = 5.0f;
    public float translateSensitivity = 1.0f;

    [Header("XR Interactors (Compatible with Near-Far Interactor)")]
    public MonoBehaviour rightRayInteractor;
    public MonoBehaviour leftRayInteractor;

    [Header("HUD Feedback Settings")]
    public GameObject hudUIParent;
    public TMP_Text hudText;
    public float hudAutoHideDelay = 2.5f;
    private Coroutine hudAutoHideCoroutine;

    private float lastSelectTime = 0f;
    private const float SELECT_COOLDOWN = 0.35f;

    private InputAction rightTriggerAction;
    private InputAction leftTriggerAction;
    private InputAction primaryButtonAction;   // A / X 键
    private InputAction secondaryButtonAction; // B / Y 键
    private InputAction reloadSceneAction;     // 左摇杆按下

    private readonly string[] availableActions = new string[] { "Move", "Rotate", "Scale" };

    private class UIContext
    {
        public GameObject sourceOwner;
        public GameObject uiRoot;
    }
    private readonly Dictionary<GameObject, UIContext> uiCache = new Dictionary<GameObject, UIContext>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        rightTriggerAction = new InputAction("RightTrigger", InputActionType.Button, "<XRController>{RightHand}/triggerButton");
        rightTriggerAction.AddBinding("<XRController>{RightHand}/activate");

        leftTriggerAction = new InputAction("LeftTrigger", InputActionType.Button, "<XRController>{LeftHand}/triggerButton");
        leftTriggerAction.AddBinding("<XRController>{LeftHand}/activate");

        primaryButtonAction = new InputAction("PrimaryBtn", InputActionType.Button, "<XRController>{RightHand}/primaryButton");
        primaryButtonAction.AddBinding("<XRController>{LeftHand}/primaryButton");

        secondaryButtonAction = new InputAction("SecondaryBtn", InputActionType.Button);
        secondaryButtonAction.AddBinding("<XRController>{RightHand}/secondaryButton"); // 右手 B 键
        secondaryButtonAction.AddBinding("<XRController>{LeftHand}/secondaryButton");  // 左手 Y 键
        secondaryButtonAction.AddBinding("<XRController>/secondaryButton");            // 兜底通用绑定

        reloadSceneAction = new InputAction("ReloadScene", InputActionType.Button, "<XRController>{LeftHand}/thumbstickClicked");
        reloadSceneAction.AddBinding("<XRController>{LeftHand}/primary2DAxisClick");
    }

    private void OnEnable()
    {
        rightTriggerAction?.Enable();
        leftTriggerAction?.Enable();

        if (primaryButtonAction != null) { primaryButtonAction.Enable(); primaryButtonAction.performed += OnPrimaryButtonPressed; }
        if (secondaryButtonAction != null) { secondaryButtonAction.Enable(); secondaryButtonAction.performed += OnSecondaryButtonPressed; }
        if (reloadSceneAction != null) { reloadSceneAction.Enable(); reloadSceneAction.performed += OnReloadScenePressed; }
    }

    private void OnDisable()
    {
        rightTriggerAction?.Disable();
        leftTriggerAction?.Disable();

        if (primaryButtonAction != null) { primaryButtonAction.performed -= OnPrimaryButtonPressed; primaryButtonAction.Disable(); }
        if (secondaryButtonAction != null) { secondaryButtonAction.performed -= OnSecondaryButtonPressed; secondaryButtonAction.Disable(); }
        if (reloadSceneAction != null) { reloadSceneAction.performed -= OnReloadScenePressed; reloadSceneAction.Disable(); }
    }

    private void Start()
    {
        if (hudUIParent != null) hudUIParent.SetActive(false);
        ValidateAndFilterInteractors();
        AutoCacheAllUIs();
    }

    private void OnReloadScenePressed(InputAction.CallbackContext context)
    {
#if UNITY_2023_1_OR_NEWER
        SceneReloader reloader = FindFirstObjectByType<SceneReloader>();
#else
        SceneReloader reloader = FindObjectOfType<SceneReloader>();
#endif
        if (reloader != null)
        {
            ShowHUD("<color=red>Requesting Scene Reload...</color>");
            reloader.ReloadCurrentScene();
        }
    }

    private void ValidateAndFilterInteractors()
    {
        if (rightRayInteractor != null && leftRayInteractor != null) return;

        MonoBehaviour[] allInteractors = FindObjectsOfType<MonoBehaviour>();
        foreach (var interactor in allInteractors)
        {
            string typeName = interactor.GetType().Name;
            if (!typeName.Contains("Interactor")) continue;

            string objName = interactor.gameObject.name.ToLower();
            if (objName.Contains("teleport")) continue;

            if (rightRayInteractor == null && (objName.Contains("right") || objName.Contains("r_")))
            {
                if (typeName.Contains("NearFar") || typeName.Contains("Ray") || typeName.Contains("Controller"))
                    rightRayInteractor = interactor;
            }
            else if (leftRayInteractor == null && (objName.Contains("left") || objName.Contains("l_")))
            {
                if (typeName.Contains("NearFar") || typeName.Contains("Ray") || typeName.Contains("Controller"))
                    leftRayInteractor = interactor;
            }
        }
    }

    private void AutoCacheAllUIs()
    {
        uiCache.Clear();
#if UNITY_2023_1_OR_NEWER
        SelectableObject[] allSelectables = FindObjectsByType<SelectableObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        SelectableObject[] allSelectables = FindObjectsOfType<SelectableObject>(true);
#endif
        foreach (var selectable in allSelectables)
        {
            GameObject owner = selectable.gameObject;
            DistributedSourceUI uiScript = owner.GetComponentInChildren<DistributedSourceUI>(true);
            if (uiScript != null)
            {
                uiCache[owner] = new UIContext { sourceOwner = owner, uiRoot = uiScript.gameObject };
                uiScript.gameObject.SetActive(false);
            }
        }
    }

    public void OnSourceTriggered(GameObject sourceObj)
    {
        if (sourceObj == null || !uiCache.ContainsKey(sourceObj)) return;
        UIContext ctx = uiCache[sourceObj];

        // 状态 1：空闲状态，或者当前已有连接但点的是“另一个”不同的物体
        if (currentState == UIWorkflowState.Idle || (currentState == UIWorkflowState.Bound && currentSource != sourceObj))
        {
            HideAllUIs();
            currentSource = sourceObj;
            currentState = UIWorkflowState.AwaitingModeClick;

            ctx.uiRoot.SetActive(true);
            ctx.uiRoot.GetComponent<DistributedSourceUI>()?.RefreshUIState();
        }
        // 状态 2：正在等待模式点击
        else if (currentState == UIWorkflowState.AwaitingModeClick)
        {
            if (currentSource == sourceObj)
            {
                HideAllUIs();
                currentSource = null;
                currentState = UIWorkflowState.Idle;
            }
            else
            {
                HideAllUIs();
                currentSource = sourceObj;
                ctx.uiRoot.SetActive(true);
                ctx.uiRoot.GetComponent<DistributedSourceUI>()?.RefreshUIState();
            }
        }
        // 状态 3：已经建立连接（Bound），点击当前的 Source 呼出或切换 UI
        else if (currentState == UIWorkflowState.Bound && currentSource == sourceObj)
        {
            // 💡 核心修复：直接取反当前 UI 的显隐状态，或者直接强制打开。
            // 这样无论点第几次，只要点一下就能立刻切换显隐，告别双击！
            bool willBeActive = !ctx.uiRoot.activeSelf;
            HideAllUIs(); // 先关掉所有其他可能的UI
            ctx.uiRoot.SetActive(willBeActive); // 单击立刻生效

            if (willBeActive)
            {
                ctx.uiRoot.GetComponent<DistributedSourceUI>()?.RefreshUIState();
            }
        }
    }

    public void HideAllUIs()
    {
        foreach (var kvp in uiCache)
        {
            if (kvp.Value.uiRoot != null) kvp.Value.uiRoot.SetActive(false);
        }
    }

    public void OnButtonClicked(GameObject sourceObj, string mode)
    {
        if (isBound && currentSource == sourceObj && currentTarget != null)
        {
            SwitchControlMode(mode);
            HideAllUIs();
            return;
        }

        currentSource = sourceObj;
        activeAction = mode;
        currentState = UIWorkflowState.AwaitingTargetPoint;

        lastSelectTime = Time.time + 0.25f;

        HideAllUIs();
        ShowHUD($"Mode: <color=yellow>{activeAction}</color>\nPoint ray at <color=#FF8C00>Target Object</color> and pull trigger.");
    }

    public void OnDisconnectClicked()
    {
        ForceResetBinding();
        ShowHUD("<color=yellow>Binding Disconnected.</color>");
        OnBindingCleared?.Invoke();
    }

    private void Update()
    {
        HandleTransformTracking();
        HandleRaycastInteraction();
    }

    private void HandleRaycastInteraction()
    {
        if (Time.time < lastSelectTime) return;

        bool isRightPressed = rightTriggerAction != null && rightTriggerAction.WasPressedThisFrame();
        bool isLeftPressed = leftTriggerAction != null && leftTriggerAction.WasPressedThisFrame();

        if (!isRightPressed && !isLeftPressed) return;

        MonoBehaviour activeInteractor = isRightPressed ? rightRayInteractor : leftRayInteractor;
        if (activeInteractor == null) return;

        SelectableObject selectable = GetSelectableFromRay(activeInteractor);

        if (currentState == UIWorkflowState.AwaitingTargetPoint)
        {
            lastSelectTime = Time.time + SELECT_COOLDOWN;

            if (selectable != null)
            {
                if (selectable.gameObject == currentSource)
                {
                    ShowHUD("<color=red>Target cannot be the same as Source!</color>");
                    return;
                }
                ConfirmBinding(currentSource, selectable.gameObject, activeAction);
            }
            else
            {
                currentSource = null;
                activeAction = "None";
                currentState = UIWorkflowState.Idle;
                ShowHUD("<color=yellow>Action Cancelled.</color>");
            }
            return;
        }

        if (currentState == UIWorkflowState.Idle || currentState == UIWorkflowState.AwaitingModeClick || currentState == UIWorkflowState.Bound)
        {
            if (selectable != null && uiCache.ContainsKey(selectable.gameObject))
            {
                lastSelectTime = Time.time + SELECT_COOLDOWN;
                OnSourceTriggered(selectable.gameObject);
            }
        }
    }

    private SelectableObject GetSelectableFromRay(MonoBehaviour interactor)
    {
        if (interactor == null) return null;

        Transform origin = interactor.transform;

        RaycastHit[] hits = Physics.SphereCastAll(origin.position, 0.08f, origin.forward, 100f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            if (hit.collider.GetComponentInParent<Canvas>() != null) continue;

            SelectableObject sel = hit.collider.GetComponentInParent<SelectableObject>();
            if (sel == null) sel = hit.collider.GetComponentInChildren<SelectableObject>();

            if (sel != null) return sel;
        }

        return null;
    }

    public void ConfirmBinding(GameObject src, GameObject tgt, string action)
    {
        RestoreTargetPhysics();
        currentSource = src;
        currentTarget = tgt;
        activeAction = action;

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

        isBound = true;
        isLocked = false;
        currentState = UIWorkflowState.Bound;
        HideAllUIs();
        ShowHUD($"<color=#00FF00>✔ Connected [{activeAction}]!</color>\n{currentSource.name} -> {currentTarget.name}");

        OnBindingCreated?.Invoke(currentSource.name, currentTarget.name);
        OnControlModeSwitched?.Invoke(activeAction);
    }

    public void ForceResetBinding()
    {
        RestoreTargetPhysics();
        HideAllUIs();
        isBound = false;
        isLocked = false;
        currentSource = null;
        currentTarget = null;
        activeAction = "None";
        currentState = UIWorkflowState.Idle;
    }

    private void RestoreTargetPhysics()
    {
        if (currentTarget != null && targetHasRigidbody && currentTarget.TryGetComponent<Rigidbody>(out Rigidbody rb))
        {
            rb.isKinematic = originalIsKinematic;
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

    private void HandleTransformTracking()
    {
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
            else if (activeAction.Equals("Move", StringComparison.OrdinalIgnoreCase) ||
                     activeAction.Equals("Translate", StringComparison.OrdinalIgnoreCase))
            {
                Vector3 deltaPosition = currentSource.transform.position - initialSourcePos;
                currentTarget.transform.position = initialTargetPos + (deltaPosition * translateSensitivity);
            }
        }
    }

    private void OnSecondaryButtonPressed(InputAction.CallbackContext context)
    {
        if (!isBound || currentSource == null || currentTarget == null) return;

        isLocked = !isLocked;
        if (isLocked)
        {
            lockedPosition = currentTarget.transform.position;
            lockedRotation = currentTarget.transform.rotation;
            lockedScale = currentTarget.transform.localScale;
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
            ShowHUD("<color=yellow>🔒 Locked</color>");
        }
        else
        {
            initialTargetPos = currentTarget.transform.position;
            initialSourcePos = currentSource.transform.position;
            initialTargetRot = currentTarget.transform.rotation;
            initialSourceRot = currentSource.transform.rotation;
            initialTargetScale = currentTarget.transform.localScale;
            ShowHUD("<color=#00FF00>🔓 Unlocked</color>");
        }

        RefreshCurrentUI();
    }

    private void OnPrimaryButtonPressed(InputAction.CallbackContext context)
    {
        if (!isBound || currentSource == null || currentTarget == null) return;

        int currentIndex = Array.IndexOf(availableActions, activeAction);
        if (currentIndex < 0) currentIndex = 0;
        int nextIndex = (currentIndex + 1) % availableActions.Length;
        SwitchControlMode(availableActions[nextIndex]);
    }

    public void SwitchControlMode(string newAction)
    {
        if (!isBound || currentSource == null || currentTarget == null) return;

        activeAction = newAction;

        initialSourceRot = currentSource.transform.rotation;
        initialTargetRot = currentTarget.transform.rotation;
        initialTargetScale = currentTarget.transform.localScale;
        initialSourcePos = currentSource.transform.position;
        initialTargetPos = currentTarget.transform.position;

        ShowHUD($"Switched to: <color=cyan>{activeAction}</color>");
        OnControlModeSwitched?.Invoke(activeAction);

        RefreshCurrentUI();
    }

    private void RefreshCurrentUI()
    {
        if (currentSource != null && uiCache.TryGetValue(currentSource, out UIContext ctx))
        {
            if (ctx.uiRoot.activeSelf)
            {
                DistributedSourceUI uiScript = ctx.uiRoot.GetComponent<DistributedSourceUI>();
                if (uiScript != null) uiScript.RefreshUIState();
            }
        }
    }

    private void ShowHUD(string message)
    {
        if (hudAutoHideCoroutine != null) StopCoroutine(hudAutoHideCoroutine);
        if (hudUIParent != null) hudUIParent.SetActive(true);
        if (hudText != null) hudText.text = message;
        hudAutoHideCoroutine = StartCoroutine(HideHUDDelayed(hudAutoHideDelay));
    }

    private System.Collections.IEnumerator HideHUDDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (hudUIParent != null) hudUIParent.SetActive(false);
    }
}