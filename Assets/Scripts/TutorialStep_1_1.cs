using UnityEngine;
using System.Collections;
using TMPro;

public class TutorialStep_1_1 : MonoBehaviour
{
    [Header("UI Instructions")]
    public TextMeshProUGUI directiveText;

    [Header("References")]
    public LLMSemanticController controller;

    [Header("Next Stage Target")]
    public MonoBehaviour nextStageScript;

    // 状态硬锁
    private bool task1Completed = false;      // 1. 建立绑定完成
    private bool taskLockCompleted = false;   // 2. Lock 锁定完成
    private bool isStepCompleted = false;     // 3. 整个 Stage 完成
    private bool isHandlingIncorrectBinding = false;
    private float taskLockCompletedTime = 0f; // 记录 Lock 完成的时间戳，用于防抖

    private const string TASK_1_INSTRUCTION = "Stage 1-1 (Task 1/3):\nHold Trigger on Right Hand and say:\n\"Move the cube with the can\"";

    private void OnEnable()
    {
        if (controller == null) controller = FindObjectsOfType<LLMSemanticController>(true)[0];

        // 1. 激活阶段时，强制给全局控制器清空历史状态
        if (controller != null)
        {
            controller.ForceResetBinding();
        }

        // 2. 初始化重置所有标志位
        task1Completed = false;
        taskLockCompleted = false;
        isStepCompleted = false;
        isHandlingIncorrectBinding = false;
        taskLockCompletedTime = 0f;

        UpdateDirectiveText(TASK_1_INSTRUCTION);

        // 3. 先取消订阅防重，再进行事件订阅
        LLMSemanticController.OnBindingCreated -= HandleBindingCreated;
        LLMSemanticController.OnBindingCleared -= HandleBindingCleared;

        LLMSemanticController.OnBindingCreated += HandleBindingCreated;
        LLMSemanticController.OnBindingCleared += HandleBindingCleared;
    }

    private void OnDisable()
    {
        LLMSemanticController.OnBindingCreated -= HandleBindingCreated;
        LLMSemanticController.OnBindingCleared -= HandleBindingCleared;
    }

    private void Update()
    {
        // 核心新增：监测用户建立绑定后是否成功触发了 Lock (controller.isLocked 为 true)
        if (task1Completed && !taskLockCompleted && controller != null && controller.isLocked)
        {
            taskLockCompleted = true;
            taskLockCompletedTime = Time.time; // 记录 Lock 完成的时间戳
            Debug.Log("<color=green>[Stage 1-1]</color> Task Lock Completed!");

            // 锁定成功后提示用户功能用途，并引导尝试解绑
            UpdateDirectiveText(
                "🔒 Target Locked!\n\n" +
                "You can use Lock to freeze the controlled object's position when needed.\n\n" +
                "Stage 1-1 (Task 3/3):\nNow try unbinding.\nHold Trigger and say:\n\"Disconnect\" or \"Clear\""
            );
        }
    }

    public void HandleBindingCreated(string sourceObj, string targetObj)
    {
        if (isStepCompleted || isHandlingIncorrectBinding) return;

        string src = string.IsNullOrEmpty(sourceObj) ? "" : sourceObj.ToLower();
        string tgt = string.IsNullOrEmpty(targetObj) ? "" : targetObj.ToLower();

        // 验证是否包含 Cube 和 Can (或 Container)
        bool hasCan = src.Contains("can") || tgt.Contains("can") || src.Contains("container") || tgt.Contains("container");
        bool hasCube = src.Contains("cube") || tgt.Contains("cube");

        // 校验是否为显式命名 (Name)，拒绝 PointAndSelect
        bool isUsingExplicitName = (controller != null && controller.LastBindingMethod == LLMSemanticController.BIND_METHOD_NAME);

        // 校验是否为移动/平移操作 (Translate 或 Move)
        bool isCorrectAction = (controller != null &&
            (controller.LastActiveAction.Equals("Translate", System.StringComparison.OrdinalIgnoreCase) ||
             controller.LastActiveAction.Equals("Move", System.StringComparison.OrdinalIgnoreCase)));

        // 错误情况拦截：未满足显式命名、动作不对、或者物料名称不匹配
        if (!isUsingExplicitName || !isCorrectAction || !hasCan || !hasCube)
        {
            StartCoroutine(HandleIncorrectBinding());
            return;
        }

        // 只有物料名称、绑定方式、控制动作全部精准匹配时，才算通过 Task 1
        if (!task1Completed)
        {
            task1Completed = true;
            Debug.Log("<color=green>[Stage 1-1]</color> Task 1 Completed with Explicit Names and Translate Action!");

            // 提示用户下一步执行 Lock 锁定操作
            UpdateDirectiveText(
                "✅ Bound successfully with object names!\nYou can grab the can up to check how the connection works.\n" +
                "When you are ready (Task 2/3):\nNow try locking the object.\nHold Trigger and say:\n\"Lock\""
            );
        }
    }

    private IEnumerator HandleIncorrectBinding()
    {
        isHandlingIncorrectBinding = true;
        task1Completed = false;
        taskLockCompleted = false;

        Debug.LogWarning("[Stage 1-1] Incorrect binding detected! Showing error & resetting...");

        UpdateDirectiveText("❌ No Spoilers!\n You are gonna learn it soonly later\n\nAuto-clearing...");

        yield return new WaitForSeconds(1.0f);

        if (controller != null)
        {
            controller.SendTextWithVisionPrompt("Clear");
        }

        // 保持错误提示文本展示 3 秒钟后再重置回初始指令
        yield return new WaitForSeconds(3.0f);

        UpdateDirectiveText(TASK_1_INSTRUCTION);
        isHandlingIncorrectBinding = false;
    }

    public void HandleBindingCleared()
    {
        // 防护 1：误操作自动清空时，不响应跳级
        if (isHandlingIncorrectBinding) return;

        // 防护 2：必须先依次完成 Task 1 (建立绑定) 与 Task 2 (成功 Lock)
        if (!task1Completed || !taskLockCompleted || isStepCompleted) return;

        // 防护 3：防止 Lock 完成后瞬间触发残留回调，需间隔至少 1.5 秒
        if (Time.time - taskLockCompletedTime < 1.5f)
        {
            Debug.LogWarning("[Stage 1-1] Ignored premature Clear event triggered right after locking.");
            return;
        }

        isStepCompleted = true;
        Debug.Log("<color=green>[Stage 1-1]</color> Stage 1-1 Complete!");

        UpdateDirectiveText("🎉 Great job! Stage 1-1 completed!\nProceeding to Stage 1-2...");

        Invoke(nameof(TransitionToStage1_2), 2.0f);
    }

    private void UpdateDirectiveText(string message)
    {
        if (directiveText != null) directiveText.text = message;
    }

    private void TransitionToStage1_2()
    {
        if (nextStageScript != null) nextStageScript.enabled = true;
        this.enabled = false;
    }
}