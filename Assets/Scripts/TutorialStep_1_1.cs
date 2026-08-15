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
    private bool task1Completed = false;
    private bool isStepCompleted = false;
    private bool isHandlingIncorrectBinding = false;
    private float task1CompletedTime = 0f; // 记录 Task 1 完成的时间戳

    private const string TASK_1_INSTRUCTION = "Stage 1-1 (Task 1/2):\nHold Trigger on Right Hand and say:\n\"Move the cube with the can\"";

    private void OnEnable()
    {
        if (controller == null) controller = FindObjectsOfType<LLMSemanticController>(true)[0];

        // 1. 激活阶段时，强制给全局控制器清空历史状态
        if (controller != null)
        {
            controller.ForceResetBinding();
        }

        // 2. 初始化重置所有锁变量
        task1Completed = false;
        isStepCompleted = false;
        isHandlingIncorrectBinding = false;
        task1CompletedTime = 0f;

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
            task1CompletedTime = Time.time; // 记录 Task 1 完成的时间
            Debug.Log("<color=green>[Stage 1-1]</color> Task 1 Completed with Explicit Names and Translate Action!");

            UpdateDirectiveText("✅ Bound successfully with object names!\n\nStage 1-1 (Task 2/2):\nNow try unbinding.\nHold Trigger and say:\n\"Disconnect\" or \"Clear\"");
        }
    }

    private IEnumerator HandleIncorrectBinding()
    {
        isHandlingIncorrectBinding = true;
        task1Completed = false;

        Debug.LogWarning("[Stage 1-1] Incorrect binding detected! Showing error & resetting...");

        UpdateDirectiveText("❌ Incorrect Action or Method!\n\nIn Stage 1-1, please explicitly name the objects and use move:\n\"Move the cube with the can\"\n\nAuto-clearing...");

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

        // 防护 2：必须先完成 Task 1
        if (!task1Completed || isStepCompleted) return;

        // 核心修复防护 3：防止 Task 1 刚建立时残留的/同一次请求返回的 Clear 回调瞬间触发 Task 2
        // 解绑指令必须在 Task 1 建立成功至少 1.5 秒后收到才有效（保证是受试者第二次说 Clear 触发的）
        if (Time.time - task1CompletedTime < 1.5f)
        {
            Debug.LogWarning("[Stage 1-1] Ignored premature Clear event triggered right after binding.");
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