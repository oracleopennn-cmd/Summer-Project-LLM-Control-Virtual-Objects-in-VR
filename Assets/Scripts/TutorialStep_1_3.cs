using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class TutorialStep_1_3 : MonoBehaviour
{
    [Header("UI Instructions")]
    public TextMeshProUGUI directiveText;

    [Header("References")]
    public LLMSemanticController controller;

    [Header("Stage 2 Scene Transition")]
    [Tooltip("拖入层级中的 Stage 2 触发按钮")]
    public Button stage2Button;

#if UNITY_EDITOR
    [Tooltip("直接把 Project 窗口里的 .unity 场景文件拖进来")]
    public SceneAsset stage2SceneAsset;
#endif

    [HideInInspector]
    [SerializeField]
    private string stage2SceneName; // 用于在运行时保存 Scene 名字

    // 三种控制方式的完成状态
    private bool hasRotated = false;
    private bool hasScaled = false;
    private bool hasMoved = false;

    private bool isStepCompleted = false;

    private void OnValidate()
    {
#if UNITY_EDITOR
        // 当你在 Inspector 中拖入或更改 Scene 文件时，自动提取其名称
        if (stage2SceneAsset != null)
        {
            stage2SceneName = stage2SceneAsset.name;
        }
#endif
    }

    private void OnEnable()
    {
        if (controller == null)
        {
            controller = FindObjectsOfType<LLMSemanticController>(true)[0];
        }

        // 1. 进入 1-3 阶段时，重置控制器状态与本地进度
        if (controller != null)
        {
            controller.ForceResetBinding();
        }

        hasRotated = false;
        hasScaled = false;
        hasMoved = false;
        isStepCompleted = false;

        // 2. 初始化按钮状态：先隐藏按钮，并绑定点击监听事件
        if (stage2Button != null)
        {
            stage2Button.gameObject.SetActive(false);
            stage2Button.onClick.RemoveListener(OnStage2ButtonClicked);
            stage2Button.onClick.AddListener(OnStage2ButtonClicked);
        }

        // 3. 更新初始 UI 指引
        UpdateInstructionUI();

        // 4. 取消订阅防重，并订阅绑定事件
        LLMSemanticController.OnBindingCreated -= HandleBindingCreated;
        LLMSemanticController.OnBindingCreated += HandleBindingCreated;
    }

    private void OnDisable()
    {
        LLMSemanticController.OnBindingCreated -= HandleBindingCreated;

        if (stage2Button != null)
        {
            stage2Button.onClick.RemoveListener(OnStage2ButtonClicked);
        }
    }

    /// <summary>
    /// 当每次建立 Binding 时触发，匹配动作类型并更新打勾进度
    /// </summary>
    public void HandleBindingCreated(string sourceObj, string targetObj)
    {
        if (isStepCompleted || controller == null) return;

        string currentAction = controller.LastActiveAction;

        // 记录对应的控制模式
        if (currentAction.Equals("Rotate", System.StringComparison.OrdinalIgnoreCase))
        {
            hasRotated = true;
            Debug.Log("<color=green>[Stage 1-3]</color> Rotate connection created!");
        }
        else if (currentAction.Equals("Scale", System.StringComparison.OrdinalIgnoreCase))
        {
            hasScaled = true;
            Debug.Log("<color=green>[Stage 1-3]</color> Scale connection created!");
        }
        else if (currentAction.Equals("Translate", System.StringComparison.OrdinalIgnoreCase) ||
                 currentAction.Equals("Move", System.StringComparison.OrdinalIgnoreCase))
        {
            hasMoved = true;
            Debug.Log("<color=green>[Stage 1-3]</color> Move connection created!");
        }

        // 刷新 UI 打勾进度
        UpdateInstructionUI();

        // 校验是否集齐全部 3 种控制模式
        if (hasRotated && hasScaled && hasMoved)
        {
            CompleteStage();
        }
    }

    /// <summary>
    /// 实时渲染 3 种模式的打勾进度 UI
    /// </summary>
    private void UpdateInstructionUI()
    {
        string text = "Stage 1-3: Try all 3 Control Modes\n" +
                      "Create bindings using Rotate, Scale, and Move:\n\n" +
                      $"{(hasRotated ? "✅" : "⬜")} Rotate (e.g., \"Rotate the cube with the can\")\n" +
                      $"{(hasScaled ? "✅" : "⬜")} Scale  (e.g., \"Scale the cube with the can\")\n" +
                      $"{(hasMoved ? "✅" : "⬜")} Move   (e.g., \"Move the cube with the can\")";

        UpdateDirectiveText(text);
    }

    private void UpdateDirectiveText(string message)
    {
        if (directiveText != null)
        {
            directiveText.text = message;
        }
    }

    private void CompleteStage()
    {
        isStepCompleted = true;
        Debug.Log("<color=green>[Stage 1-3]</color> All 3 connection types established! Activating Stage 2 Button.");

        // 1. 提示用户点击按钮
        UpdateDirectiveText("🎉 Tutorial 1 Complete!\n\nPlease click the button below to load Stage 2.");

        // 2. 激活 Stage 2 按钮
        if (stage2Button != null)
        {
            stage2Button.gameObject.SetActive(true);
        }
        else
        {
            Debug.LogWarning("[Stage 1-3] Stage 2 Button is not assigned in the Inspector!");
        }
    }

    /// <summary>
    /// 按钮点击事件：加载 Stage 2 Scene
    /// </summary>
    private void OnStage2ButtonClicked()
    {
        Debug.Log($"<color=yellow>[Stage 1-3]</color> Loading Scene: {stage2SceneName}");
        if (!string.IsNullOrEmpty(stage2SceneName))
        {
            SceneManager.LoadScene(stage2SceneName);
        }
        else
        {
            Debug.LogError("[Stage 1-3] Stage 2 Scene is not assigned!");
        }
    }
}