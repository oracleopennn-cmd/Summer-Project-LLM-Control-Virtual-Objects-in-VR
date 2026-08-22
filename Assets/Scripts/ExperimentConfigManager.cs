using UnityEngine;
using TMPro;

public class ExperimentConfigManager : MonoBehaviour
{
    [Header("Active Stage Dropdowns (LLM / Voice)")]
    [Tooltip("Stage 2 Trials 下拉选单 (选项 1, 2, 3...)")]
    public TMP_Dropdown stage2TrialsDropdown;

    [Tooltip("Stage 3 Trials 下拉选单 (选项 1, 2, 3...)")]
    public TMP_Dropdown stage3TrialsDropdown;

    [Header("Traditional UI Stage Dropdowns")]
    [Tooltip("Traditional UI Stage 2 Trials 下拉选单")]
    public TMP_Dropdown stage2TrialsDropdownT;

    [Tooltip("Traditional UI Stage 3 Trials 下拉选单")]
    public TMP_Dropdown stage3TrialsDropdownT;

    [Header("Feature Toggle Dropdowns")]
    [Tooltip("左手 X 键重载场景开关 (选项 0: Enabled, 选项 1: Disabled)")]
    public TMP_Dropdown sceneReloadDropdown;

    // 全局静态变量，跨场景直接读取
    public static int GlobalStage2Trials = 3;
    public static int GlobalStage3Trials = 3;
    public static int GlobalStage2TrialsT = 3; // Traditional UI Stage 2
    public static int GlobalStage3TrialsT = 3; // Traditional UI Stage 3

    private void Awake()
    {
        if (stage2TrialsDropdown != null)
            stage2TrialsDropdown.onValueChanged.AddListener(SetStage2TrialsFromDropdown);

        if (stage3TrialsDropdown != null)
            stage3TrialsDropdown.onValueChanged.AddListener(SetStage3TrialsFromDropdown);

        if (stage2TrialsDropdownT != null)
            stage2TrialsDropdownT.onValueChanged.AddListener(SetStage2TrialsTFromDropdown);

        if (stage3TrialsDropdownT != null)
            stage3TrialsDropdownT.onValueChanged.AddListener(SetStage3TrialsTFromDropdown);

        if (sceneReloadDropdown != null)
            sceneReloadDropdown.onValueChanged.AddListener(SetSceneReloadFromDropdown);
    }

    public void SetStage2TrialsFromDropdown(int index)
    {
        GlobalStage2Trials = index + 1;
        Debug.Log($"<color=cyan>[Config]</color> Stage 2 Trials set to: {GlobalStage2Trials}");
    }

    public void SetStage3TrialsFromDropdown(int index)
    {
        GlobalStage3Trials = index + 1;
        Debug.Log($"<color=cyan>[Config]</color> Stage 3 Trials set to: {GlobalStage3Trials}");
    }

    public void SetStage2TrialsTFromDropdown(int index)
    {
        GlobalStage2TrialsT = index + 1;
        Debug.Log($"<color=cyan>[Config]</color> Traditional Stage 2 Trials set to: {GlobalStage2TrialsT}");
    }

    public void SetStage3TrialsTFromDropdown(int index)
    {
        GlobalStage3TrialsT = index + 1;
        Debug.Log($"<color=cyan>[Config]</color> Traditional Stage 3 Trials set to: {GlobalStage3TrialsT}");
    }

    public void SetSceneReloadFromDropdown(int index)
    {
        LLMSemanticController.isSceneReloadEnabled = (index == 0);
        Debug.Log($"<color=cyan>[Config]</color> Scene Reload Feature Enabled: {LLMSemanticController.isSceneReloadEnabled}");
    }
}