using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine.XR;

public class Stage2_Manager : MonoBehaviour
{
    [Header("UI & References")]
    [Tooltip("拖入 UI Canvas 或 UI 面板 GameObject（⚠️注意：本脚本挂在独立空物体上）")]
    public GameObject uiCanvas;

    [Tooltip("提示文字组件（可留空，代码会自动从 uiCanvas 中寻找）")]
    public TextMeshProUGUI hintText;

    public LLMSemanticController controller;

    [Tooltip("通关后需要激活的下一个阶段 GameObject（如 Stage 3 Manager 物体）")]
    public GameObject nextStageObject;

    [Header("Docking Objects")]
    [Tooltip("玩家操作的近处实体物体")]
    public GameObject sourceObject;

    [Header("Target Objects Pool")]
    [Tooltip("在场景中摆好的目标 GameObject 集合，脚本会自动切换显示与对齐")]
    public GameObject[] targetObjectsPool;

    [Header("Trial & Tolerances")]
    public int totalTrials = 3;
    public float positionTolerance = 0.08f;
    public float rotationTolerance = 15.0f;
    public float scaleTolerance = 0.15f;

    [Header("Runtime Status & Records")]
    public int currentTrialIndex = 0;

    [Tooltip("当前 Trial 激活的目标物体")]
    public GameObject currentTargetObject;

    [Tooltip("记录所有已经抽取并激活过的目标物体历史（实时更新）")]
    public List<GameObject> usedTargetObjects = new List<GameObject>();

    private bool isTrialActive = false;
    private float trialStartTime = 0f;
    private float holdTimer = 0f;
    private const float REQUIRED_HOLD_TIME = 1.0f;

    private List<int> shuffledIndices = new List<int>();

    // UI 隐藏控制
    private Coroutine hideUICoroutine;
    private const float UI_AUTO_HIDE_DELAY = 3.0f;

    // VR 手柄按键防刷
    private bool wasButtonPressedLastFrame = false;

    private void OnEnable()
    {
        if (controller == null) controller = FindObjectOfType<LLMSemanticController>();

        if (hintText == null && uiCanvas != null)
        {
            hintText = uiCanvas.GetComponentInChildren<TextMeshProUGUI>();
        }

        if (targetObjectsPool == null || targetObjectsPool.Length == 0)
        {
            Debug.LogError("<color=red>[Stage 2 Error]</color> Target Objects Pool 数组为空！");
            UpdateUI("<color=red>⚠️ Stage 2 Error:</color>\nNo Target Objects assigned!");
            return;
        }

        if (sourceObject == null)
        {
            Debug.LogError("<color=red>[Stage 2 Error]</color> Source Object 未赋值！");
            return;
        }

        HideAllTargets();
        usedTargetObjects.Clear();

        InitShuffledPool();
        currentTrialIndex = 0;
        StartNextTrial();
    }

    private void Update()
    {
        // Quest 手柄 Y (左) 或 B (右) 键开关 UI
        if (CheckQuestVRInput())
        {
            ToggleUI();
        }

        if (!isTrialActive || currentTrialIndex >= totalTrials) return;

        CheckDockingCondition();
    }

    private bool CheckQuestVRInput()
    {
        bool isPressedThisFrame = false;

        InputDevice leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (leftHand.isValid && leftHand.TryGetFeatureValue(CommonUsages.secondaryButton, out bool leftYPressed))
        {
            if (leftYPressed) isPressedThisFrame = true;
        }

        InputDevice rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (rightHand.isValid && rightHand.TryGetFeatureValue(CommonUsages.secondaryButton, out bool rightBPressed))
        {
            if (rightBPressed) isPressedThisFrame = true;
        }

        if (isPressedThisFrame && !wasButtonPressedLastFrame)
        {
            wasButtonPressedLastFrame = true;
            return true;
        }

        if (!isPressedThisFrame)
        {
            wasButtonPressedLastFrame = false;
        }

        return false;
    }

    private void ToggleUI()
    {
        if (uiCanvas == null) return;

        bool newState = !uiCanvas.activeSelf;
        uiCanvas.SetActive(newState);

        if (newState)
        {
            ResetHideTimer();
        }
        else if (hideUICoroutine != null)
        {
            StopCoroutine(hideUICoroutine);
            hideUICoroutine = null;
        }
    }

    private void UpdateUI(string message)
    {
        if (hintText == null && uiCanvas != null)
        {
            hintText = uiCanvas.GetComponentInChildren<TextMeshProUGUI>();
        }

        if (hintText != null)
        {
            hintText.text = message;
        }

        if (uiCanvas != null && !uiCanvas.activeSelf)
        {
            uiCanvas.SetActive(true);
        }

        ResetHideTimer();
    }

    private void ResetHideTimer()
    {
        if (hideUICoroutine != null)
        {
            StopCoroutine(hideUICoroutine);
        }
        hideUICoroutine = StartCoroutine(HideUIDelayed(UI_AUTO_HIDE_DELAY));
    }

    private IEnumerator HideUIDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (uiCanvas != null)
        {
            uiCanvas.SetActive(false);
        }
        hideUICoroutine = null;
    }

    private void HideAllTargets()
    {
        foreach (var obj in targetObjectsPool)
        {
            if (obj != null) obj.SetActive(false);
        }
    }

    private void InitShuffledPool()
    {
        shuffledIndices.Clear();
        for (int i = 0; i < targetObjectsPool.Length; i++)
        {
            shuffledIndices.Add(i);
        }

        for (int i = 0; i < shuffledIndices.Count; i++)
        {
            int randomIndex = Random.Range(i, shuffledIndices.Count);
            int temp = shuffledIndices[i];
            shuffledIndices[i] = shuffledIndices[randomIndex];
            shuffledIndices[randomIndex] = temp;
        }
    }

    private void StartNextTrial()
    {
        if (currentTrialIndex >= totalTrials || currentTrialIndex >= shuffledIndices.Count)
        {
            CompleteStage2();
            return;
        }

        HideAllTargets();

        int targetIndex = shuffledIndices[currentTrialIndex];
        currentTargetObject = targetObjectsPool[targetIndex];

        if (currentTargetObject != null)
        {
            currentTargetObject.SetActive(true);

            if (!usedTargetObjects.Contains(currentTargetObject))
            {
                usedTargetObjects.Add(currentTargetObject);
            }
        }

        if (controller != null)
        {
            controller.ForceResetBinding();
        }

        // 读取 SelectableObject 中的 Object Label 名称
        string sourceLabel = GetObjectLabel(sourceObject);
        string targetLabel = GetObjectLabel(currentTargetObject);

        UpdateUI($"Stage 2: Simple Docking ({currentTrialIndex + 1}/{totalTrials})\n\n" +
                 $"Bind <color=yellow>{sourceLabel}</color> and match its position, rotation & scale to <color=cyan>{targetLabel}</color>.");

        trialStartTime = Time.time;
        holdTimer = 0f;
        isTrialActive = true;

        Debug.Log($"<color=green>[Stage 2]</color> Trial {currentTrialIndex + 1} started. Target: [{targetLabel}]");
    }

    private void CheckDockingCondition()
    {
        if (sourceObject == null || currentTargetObject == null) return;

        float distance = Vector3.Distance(sourceObject.transform.position, currentTargetObject.transform.position);
        float angle = Quaternion.Angle(sourceObject.transform.rotation, currentTargetObject.transform.rotation);
        float scaleDiff = Vector3.Distance(sourceObject.transform.localScale, currentTargetObject.transform.localScale);

        if (distance <= positionTolerance && angle <= rotationTolerance && scaleDiff <= scaleTolerance)
        {
            holdTimer += Time.deltaTime;

            UpdateUI($"Stage 2: Simple Docking ({currentTrialIndex + 1}/{totalTrials})\n\n" +
                     $"<color=#00FF00>Holding alignment... ({holdTimer:F1}s / {REQUIRED_HOLD_TIME:F1}s)</color>");

            if (holdTimer >= REQUIRED_HOLD_TIME)
            {
                OnTrialCompleted();
            }
        }
        else
        {
            holdTimer = 0f;
        }
    }

    private void OnTrialCompleted()
    {
        isTrialActive = false;
        float duration = Time.time - trialStartTime;

        Debug.Log($"<color=green>[Stage 2]</color> Trial {currentTrialIndex + 1} Completed! Target: [{GetObjectLabel(currentTargetObject)}], Time: {duration:F2}s");

        UpdateUI($"🎉 Trial {currentTrialIndex + 1} Completed!\nTime: {duration:F1}s");

        currentTrialIndex++;
        Invoke(nameof(StartNextTrial), 1.5f);
    }

    private void CompleteStage2()
    {
        isTrialActive = false;
        HideAllTargets();

        UpdateUI("🎉 Stage 2 Complete!\nProceeding to Stage 3...");
        Debug.Log("<color=green>[Stage 2]</color> Stage 2 Fully Completed!");

        Invoke(nameof(TransitionToStage3), 2.0f);
    }

    private void TransitionToStage3()
    {
        if (nextStageObject != null)
        {
            nextStageObject.SetActive(true);
        }

        this.enabled = false;
    }

    /// <summary>
    /// 获取物体的显示的 Label 名称。
    /// 优先从 SelectableObject 组件中提取 objectLabel，若不存在则使用 GameObject 的 name。
    /// </summary>
    private string GetObjectLabel(GameObject obj)
    {
        if (obj == null) return "None";

        SelectableObject selectable = obj.GetComponent<SelectableObject>();
        if (selectable != null)
        {
            // 提示：如果你的 SelectableObject 脚本中变量名是首字母大写 (ObjectLabel)，把下一行的 objectLabel 改为 ObjectLabel 即可
            if (!string.IsNullOrEmpty(selectable.objectLabel))
            {
                return selectable.objectLabel;
            }
        }

        return obj.name;
    }
}