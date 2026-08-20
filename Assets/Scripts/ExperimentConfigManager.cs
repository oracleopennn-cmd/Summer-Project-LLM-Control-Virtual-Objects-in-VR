using UnityEngine;
using TMPro;

public class ExperimentConfigManager : MonoBehaviour
{
    [Header("Active Stage Dropdowns")]
    [Tooltip("Stage 2 Trials 下拉选单 (选项 1, 2, 3...)")]
    public TMP_Dropdown stage2TrialsDropdown;

    [Tooltip("Stage 3 Trials 下拉选单 (选项 1, 2, 3...)")]
    public TMP_Dropdown stage3TrialsDropdown;

    [Header("Reserved Slot Dropdowns (备用槽位)")]
    [Tooltip("备用槽位 1 下拉选单")]
    public TMP_Dropdown reservedSlot1Dropdown;

    [Tooltip("备用槽位 2 下拉选单")]
    public TMP_Dropdown reservedSlot2Dropdown;

    [Header("Feature Toggle Dropdowns")]
    [Tooltip("左手 X 键重载场景开关 (选项 0: Enabled, 选项 1: Disabled)")]
    public TMP_Dropdown sceneReloadDropdown;

    // 全局静态变量，跨场景直接读取
    public static int GlobalStage2Trials = 3;
    public static int GlobalStage3Trials = 3;
    public static int GlobalReservedSlot1 = 3;
    public static int GlobalReservedSlot2 = 3;

    private void Awake()
    {
        // 绑定各个 Dropdown 的更改监听
        if (stage2TrialsDropdown != null)
        {
            stage2TrialsDropdown.onValueChanged.AddListener(SetStage2TrialsFromDropdown);
        }

        if (stage3TrialsDropdown != null)
        {
            stage3TrialsDropdown.onValueChanged.AddListener(SetStage3TrialsFromDropdown);
        }

        if (reservedSlot1Dropdown != null)
        {
            reservedSlot1Dropdown.onValueChanged.AddListener(SetReservedSlot1FromDropdown);
        }

        if (reservedSlot2Dropdown != null)
        {
            reservedSlot2Dropdown.onValueChanged.AddListener(SetReservedSlot2FromDropdown);
        }

        if (sceneReloadDropdown != null)
        {
            sceneReloadDropdown.onValueChanged.AddListener(SetSceneReloadFromDropdown);
        }
    }

    public void SetStage2TrialsFromDropdown(int index)
    {
        // 选项索引 0 -> 1 轮, 1 -> 2 轮, 2 -> 3 轮...
        GlobalStage2Trials = index + 1;
        Debug.Log($"<color=cyan>[Config]</color> Stage 2 Trials set to: {GlobalStage2Trials}");
    }

    public void SetStage3TrialsFromDropdown(int index)
    {
        GlobalStage3Trials = index + 1;
        Debug.Log($"<color=cyan>[Config]</color> Stage 3 Trials set to: {GlobalStage3Trials}");
    }

    public void SetReservedSlot1FromDropdown(int index)
    {
        GlobalReservedSlot1 = index + 1;
        Debug.Log($"<color=cyan>[Config]</color> Reserved Slot 1 set to: {GlobalReservedSlot1}");
    }

    public void SetReservedSlot2FromDropdown(int index)
    {
        GlobalReservedSlot2 = index + 1;
        Debug.Log($"<color=cyan>[Config]</color> Reserved Slot 2 set to: {GlobalReservedSlot2}");
    }

    public void SetSceneReloadFromDropdown(int index)
    {
        // 选项 0: Enabled (true), 选项 1: Disabled (false)
        LLMSemanticController.isSceneReloadEnabled = (index == 0);
        Debug.Log($"<color=cyan>[Config]</color> Scene Reload Feature Enabled: {LLMSemanticController.isSceneReloadEnabled}");
    }
}