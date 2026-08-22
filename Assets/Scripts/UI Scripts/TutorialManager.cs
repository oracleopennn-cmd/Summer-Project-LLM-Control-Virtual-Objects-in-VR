using UnityEngine;
using TMPro;

public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance { get; private set; }

    public enum TutorialStep
    {
        Step1_SelectCan,
        Step2_SelectMoveMode,
        Step3_SelectCube,
        Step4_PracticeLockAndUI,
        Step5_PracticeUnlockAndHotkey,
        Step6_Disconnect,
        Completed
    }

    [Header("Current Tutorial State")]
    public TutorialStep currentStep = TutorialStep.Step1_SelectCan;

    [Header("UI References")]
    public TMP_Text tutorialText; // 用于展示教程提示的固定文本框
    public GameObject completionUI; // 💡 新增：教程完成时需要激活的 UI 物体

    private float stepEnterTime = 0f; // 时间锁，防止事件瞬时穿透连跳

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    private void Start()
    {
        if (completionUI != null) completionUI.SetActive(false); // 初始时先隐藏该 UI
        UpdateTutorialPrompt();
    }

    private void OnEnable()
    {
        TraditionalUIController.OnBindingCreated += OnBindingCreatedHandler;
        TraditionalUIController.OnControlModeSwitched += OnControlModeSwitchedHandler;
        TraditionalUIController.OnBindingCleared += OnBindingClearedHandler;
    }

    private void OnDisable()
    {
        TraditionalUIController.OnBindingCreated -= OnBindingCreatedHandler;
        TraditionalUIController.OnControlModeSwitched -= OnControlModeSwitchedHandler;
        TraditionalUIController.OnBindingCleared -= OnBindingClearedHandler;
    }

    private void Update()
    {
        CheckStepProgress();
    }

    private void CheckStepProgress()
    {
        if (TraditionalUIController.Instance == null) return;

        var controller = TraditionalUIController.Instance;

        switch (currentStep)
        {
            case TutorialStep.Step1_SelectCan:
                if (controller.currentState == TraditionalUIController.UIWorkflowState.AwaitingModeClick)
                {
                    AdvanceStep(TutorialStep.Step2_SelectMoveMode);
                }
                break;

            case TutorialStep.Step2_SelectMoveMode:
                if (controller.currentState == TraditionalUIController.UIWorkflowState.AwaitingTargetPoint)
                {
                    if (controller.activeAction.Equals("Move", System.StringComparison.OrdinalIgnoreCase))
                    {
                        AdvanceStep(TutorialStep.Step3_SelectCube);
                    }
                    else
                    {
                        ShowPrompt("Wrong mode! This step requires [Move]. Reloading scene...");
                        ReloadScene();
                    }
                }
                break;

            default:
                break;
        }
    }

    private void OnBindingCreatedHandler(string sourceName, string targetName)
    {
        if (Time.time - stepEnterTime < 0.6f) return;

        if (currentStep == TutorialStep.Step3_SelectCube)
        {
            AdvanceStep(TutorialStep.Step4_PracticeLockAndUI);
        }
    }

    private void OnControlModeSwitchedHandler(string newMode)
    {
        if (Time.time - stepEnterTime < 0.6f) return;

        if (currentStep == TutorialStep.Step4_PracticeLockAndUI)
        {
            AdvanceStep(TutorialStep.Step5_PracticeUnlockAndHotkey);
        }
        else if (currentStep == TutorialStep.Step5_PracticeUnlockAndHotkey)
        {
            AdvanceStep(TutorialStep.Step6_Disconnect);
        }
    }

    private void OnBindingClearedHandler()
    {
        if (Time.time - stepEnterTime < 0.6f) return;

        if (currentStep == TutorialStep.Step6_Disconnect)
        {
            AdvanceStep(TutorialStep.Completed);
        }
    }

    private void AdvanceStep(TutorialStep nextStep)
    {
        currentStep = nextStep;
        stepEnterTime = Time.time;
        UpdateTutorialPrompt();

        // 💡 当进入 Completed 状态时，自动激活指定的 UI
        if (currentStep == TutorialStep.Completed && completionUI != null)
        {
            completionUI.SetActive(true);
        }
    }

    private void UpdateTutorialPrompt()
    {
        if (tutorialText == null) return;

        switch (currentStep)
        {
            case TutorialStep.Step1_SelectCan:
                tutorialText.text = "Welcome! Please use the ray to click the Can object to open the interaction menu.";
                break;
            case TutorialStep.Step2_SelectMoveMode:
                tutorialText.text = "Please click the [Move] mode in the menu. (Note: This step requires Move; choosing other modes will reload).";
                break;
            case TutorialStep.Step3_SelectCube:
                tutorialText.text = "Please use the ray to click the Cube object to establish the connection.";
                break;
            case TutorialStep.Step4_PracticeLockAndUI:
                tutorialText.text = "Connection established! Try pressing the B/Y button to lock the transformation. Then double-click the Can to open the UI and switch connection mode.";
                break;
            case TutorialStep.Step5_PracticeUnlockAndHotkey:
                tutorialText.text = "Great! Try pressing B/Y again to unlock, and use the A/X shortcut button on your controller to switch connection modes.";
                break;
            case TutorialStep.Step6_Disconnect:
                tutorialText.text = "Final step: Click the Can again to open the UI, and click [Disconnect] to clear the binding.";
                break;
            case TutorialStep.Completed:
                tutorialText.text = "Congratulations! You have successfully mastered all core interaction workflows.";
                break;
        }
    }

    private void ShowPrompt(string msg)
    {
        if (tutorialText != null) tutorialText.text = msg;
    }

    private void ReloadScene()
    {
#if UNITY_2023_1_OR_NEWER
        SceneReloader reloader = FindFirstObjectByType<SceneReloader>();
#else
        SceneReloader reloader = FindObjectOfType<SceneReloader>();
#endif
        if (reloader != null)
        {
            reloader.ReloadCurrentScene();
        }
    }
}