using UnityEngine;
using System.Collections;
using TMPro;

public class TutorialStep_1_2 : MonoBehaviour
{
    [Header("UI Instructions")]
    public TextMeshProUGUI directiveText;

    [Header("References")]
    public LLMSemanticController controller; // 拖入场景中的 LLMSemanticController

    [Header("Next Stage Target")]
    public MonoBehaviour nextStageScript; // 在 Inspector 中拖入任意下一个 Stage 脚本

    // 状态硬锁
    private bool task1Completed = false;
    private bool isStepCompleted = false;
    private bool isHandlingIncorrectBinding = false;
    private float task1CompletedTime = 0f; // 记录 Task 1 完成的时间戳

    private const string TASK_1_INSTRUCTION = "Stage 1-2 (Task 1/2):\nHold Trigger and say:\n\"I want to use this to move that\"\n\n(Then use raycast to point and click Source and Target objects)";

    private void OnEnable()
    {
        if (controller == null)
        {
            controller = FindObjectsOfType<LLMSemanticController>(true)[0];
        }

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

        // 显示 1-2 初始指引
        UpdateDirectiveText(TASK_1_INSTRUCTION);

        // 3. 先取消订阅防重，再进行事件订阅
        LLMSemanticController.OnBindingCreated -= HandleBindingCreated;
        LLMSemanticController.OnBindingCleared -= HandleBindingCleared;

        LLMSemanticController.OnBindingCreated += HandleBindingCreated;
        LLMSemanticController.OnBindingCleared += HandleBindingCleared;
    }

    private void OnDisable()
    {
        // 取消订阅
        LLMSemanticController.OnBindingCreated -= HandleBindingCreated;
        LLMSemanticController.OnBindingCleared -= HandleBindingCleared;
    }

    /// <summary>
    /// 当成功建立 Binding 时触发
    /// </summary>
    public void HandleBindingCreated(string sourceObj, string targetObj)
    {
        if (isStepCompleted || isHandlingIncorrectBinding) return;

        // 【新增校验 1】利用 controller 的 LastBindingMethod 判定是否为点选代词方式 (PointAndSelect)，拒绝显式命名
        bool isUsingPointMethod = (controller != null && controller.LastBindingMethod == LLMSemanticController.BIND_METHOD_POINT);

        // 【新增校验 2】利用 controller 的 LastActiveAction 判定是否为移动/平移操作 (Translate 或 Move)
        bool isCorrectAction = (controller != null &&
            (controller.LastActiveAction.Equals("Translate", System.StringComparison.OrdinalIgnoreCase) ||
             controller.LastActiveAction.Equals("Move", System.StringComparison.OrdinalIgnoreCase)));

        // 错误情况拦截：未使用点选代词方式，或者动作类型不对
        if (!isUsingPointMethod || !isCorrectAction)
        {
            StartCoroutine(HandleIncorrectBinding());
            return;
        }

        // 正确通过 Task 1
        if (!task1Completed)
        {
            task1Completed = true;
            task1CompletedTime = Time.time; // 记录 Task 1 完成的时间
            Debug.Log("<color=green>[Stage 1-2]</color> Task 1 Completed with Point-and-Select and Translate Action!");

            // 切换为 Task 2/2 指引
            UpdateDirectiveText("✅ Demonstrative binding successful!\nYou can grab the can up to check how the connection works.\nStage 1-2 (Task 2/2):\nNow hold Trigger and say:\n\"Disconnect\" or \"Clear\" to unbind.");
        }
    }

    /// <summary>
    /// 处理绑定方式或动作错误的情况（保持 3 秒错误提示）
    /// </summary>
    private IEnumerator HandleIncorrectBinding()
    {
        isHandlingIncorrectBinding = true;
        task1Completed = false;

        Debug.LogWarning("[Stage 1-2] Incorrect binding method or action detected!");

        // 1. 弹出错误提示
        UpdateDirectiveText("❌ Incorrect Method or Action!\nPlease follow the instructions\n\nAuto-clearing...");

        // 2. 等待 1 秒
        yield return new WaitForSeconds(1.0f);

        // 3. 自动执行解绑重置
        if (controller != null)
        {
            controller.SendTextWithVisionPrompt("Clear");
        }

        // 4. 保持错误提示文本展示 3 秒钟后再恢复初始指引
        yield return new WaitForSeconds(3.0f);

        UpdateDirectiveText(TASK_1_INSTRUCTION);
        isHandlingIncorrectBinding = false;
    }

    /// <summary>
    /// 当解绑成功时触发
    /// </summary>
    public void HandleBindingCleared()
    {
        // 护栏 1：忽略错误自动化解绑触发的事件
        if (isHandlingIncorrectBinding) return;

        // 护栏 2：必须先完成 Task 1
        if (!task1Completed || isStepCompleted) return;

        // 护栏 3：防止 Task 1 刚建立时残留的 Clear 回调瞬间触发 Task 2（必须至少过 1.5 秒）
        if (Time.time - task1CompletedTime < 1.5f)
        {
            Debug.LogWarning("[Stage 1-2] Ignored premature Clear event triggered right after binding.");
            return;
        }

        isStepCompleted = true;
        Debug.Log("<color=green>[Stage 1-2]</color> Unbound successfully! Stage 1-2 complete.");

        UpdateDirectiveText("🎉 Great job! Stage 1-2 completed!\nProceeding to Stage 1-3...");

        // 2 秒后跳转进入 Stage 1-3
        Invoke(nameof(TransitionToStage1_3), 2.0f);
    }

    private void UpdateDirectiveText(string message)
    {
        if (directiveText != null)
        {
            directiveText.text = message;
        }
    }

    private void TransitionToStage1_3()
    {
        Debug.Log("Entering Stage 1-3...");
        if (nextStageScript != null)
        {
            nextStageScript.enabled = true;
        }
        else
        {
            Debug.LogWarning("[Stage 1-2] Next stage script is not assigned in the Inspector!");
        }
        this.enabled = false;
    }
}