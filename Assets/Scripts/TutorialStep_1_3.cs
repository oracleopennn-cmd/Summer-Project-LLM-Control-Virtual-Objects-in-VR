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
    private string stage2SceneName;

    private bool hasRotated = false;
    private bool hasScaled = false;
    private bool hasMoved = false;

    private bool isStepCompleted = false;

    private void OnValidate()
    {
#if UNITY_EDITOR
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

        if (controller != null)
        {
            controller.ForceResetBinding();
        }

        hasRotated = false;
        hasScaled = false;
        hasMoved = false;
        isStepCompleted = false;

        if (stage2Button != null)
        {
            stage2Button.gameObject.SetActive(false);
            stage2Button.onClick.RemoveListener(OnStage2ButtonClicked);
            stage2Button.onClick.AddListener(OnStage2ButtonClicked);
        }

        UpdateDirectiveText("Stage 1-3: Practice all 3 Control Modes\nCheck out how Rotate, Move and Scale works.\n Hint: You can establish connections using complete sentences, or you can simply say \"rotate,\" \"scale,\" and \"move\" to change the\n connection if a connection has already been established. ");

        // 订阅绑定创建事件
        LLMSemanticController.OnBindingCreated -= HandleBindingCreated;
        LLMSemanticController.OnBindingCreated += HandleBindingCreated;

        // ➕ 核心新增：订阅模式直接切换事件
        LLMSemanticController.OnControlModeSwitched -= HandleControlModeSwitched;
        LLMSemanticController.OnControlModeSwitched += HandleControlModeSwitched;
    }

    private void OnDisable()
    {
        LLMSemanticController.OnBindingCreated -= HandleBindingCreated;

        // 取消订阅切换事件
        LLMSemanticController.OnControlModeSwitched -= HandleControlModeSwitched;

        if (stage2Button != null)
        {
            stage2Button.onClick.RemoveListener(OnStage2ButtonClicked);
        }
    }

    // 1. 处理新建立绑定时的回调
    public void HandleBindingCreated(string sourceObj, string targetObj)
    {
        if (controller != null)
        {
            RecordAction(controller.LastActiveAction);
        }
    }

    // 2. 核心新增：处理直说关键词切换模式时的回调
    public void HandleControlModeSwitched(string newMode)
    {
        RecordAction(newMode);
    }

    // 统一模式判定与关卡进度更新
    private void RecordAction(string action)
    {
        if (isStepCompleted || string.IsNullOrEmpty(action)) return;

        if (action.Equals("Rotate", System.StringComparison.OrdinalIgnoreCase))
        {
            hasRotated = true;
            Debug.Log("<color=green>[Stage 1-3 Tracker]</color> Rotate recorded. (Progress: " + GetProgressCount() + "/3)");
        }
        else if (action.Equals("Scale", System.StringComparison.OrdinalIgnoreCase))
        {
            hasScaled = true;
            Debug.Log("<color=green>[Stage 1-3 Tracker]</color> Scale recorded. (Progress: " + GetProgressCount() + "/3)");
        }
        else if (action.Equals("Translate", System.StringComparison.OrdinalIgnoreCase) ||
                 action.Equals("Move", System.StringComparison.OrdinalIgnoreCase))
        {
            hasMoved = true;
            Debug.Log("<color=green>[Stage 1-3 Tracker]</color> Move recorded. (Progress: " + GetProgressCount() + "/3)");
        }

        if (hasRotated && hasScaled && hasMoved)
        {
            CompleteStage();
        }
    }

    private int GetProgressCount()
    {
        int count = 0;
        if (hasRotated) count++;
        if (hasScaled) count++;
        if (hasMoved) count++;
        return count;
    }

    private void CompleteStage()
    {
        isStepCompleted = true;
        Debug.Log("<color=green>[Stage 1-3]</color> All 3 modes tested! Stage complete.");

        UpdateDirectiveText("🎉 Stage 1 Complete!\nClick the button below to proceed to Stage 2.");

        if (stage2Button != null)
        {
            stage2Button.gameObject.SetActive(true);
        }
        else
        {
            Debug.LogWarning("[Stage 1-3] Stage 2 Button is not assigned in the Inspector!");
        }
    }

    private void UpdateDirectiveText(string message)
    {
        if (directiveText != null)
        {
            directiveText.text = message;
        }
    }

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