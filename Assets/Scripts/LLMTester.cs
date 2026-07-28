using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class LLMTester : MonoBehaviour
{
    [Header("控制器引用")]
    public LLMSemanticController controller;

    // 用于记录上一帧的按键状态，实现 WasPressedThisFrame 效果
    private bool lastAPressed = false;
    private bool lastBPressed = false;

    void Update()
    {
        if (controller == null) return;

        bool triggerBind = false;
        bool triggerClear = false;

        // ---------------------------------------------------------
        // 1. 键盘测试 (1 / 2 键)
        // ---------------------------------------------------------
        if (Keyboard.current != null)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame) triggerBind = true;
            if (Keyboard.current.digit2Key.wasPressedThisFrame) triggerClear = true;
        }

        // ---------------------------------------------------------
        // 2. XR Device Simulator 模拟器测试 (A / B 键)
        // ---------------------------------------------------------
        if (Gamepad.current != null)
        {
            if (Gamepad.current.buttonSouth.wasPressedThisFrame) triggerBind = true;
            if (Gamepad.current.buttonEast.wasPressedThisFrame) triggerClear = true;
        }

        // ---------------------------------------------------------
        // 3. Quest 3 真实硬件检测 (右手柄 A 键 & B 键)
        // 显式指定 UnityEngine.XR 防止命名空间冲突
        // ---------------------------------------------------------
        var rightHandDevices = new List<UnityEngine.XR.InputDevice>();
        UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
            UnityEngine.XR.InputDeviceCharacteristics.Right | UnityEngine.XR.InputDeviceCharacteristics.Controller,
            rightHandDevices
        );

        if (rightHandDevices.Count > 0)
        {
            UnityEngine.XR.InputDevice rightController = rightHandDevices[0];

            // 检测 Quest 3 右手柄 A 键 (Primary Button)
            if (rightController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool aPressed))
            {
                if (aPressed && !lastAPressed) // 触发 PressedThisFrame
                {
                    triggerBind = true;
                }
                lastAPressed = aPressed;
            }

            // 检测 Quest 3 右手柄 B 键 (Secondary Button)
            if (rightController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool bPressed))
            {
                if (bPressed && !lastBPressed) // 触发 PressedThisFrame
                {
                    triggerClear = true;
                }
                lastBPressed = bPressed;
            }
        }

        // ---------------------------------------------------------
        // 执行逻辑调用
        // ---------------------------------------------------------
        if (triggerBind)
        {
            Debug.Log("[LLMTester] Quest 3 触发: 发送建立映射指令...");
            controller.SendUserPrompt("我想通过旋转易拉罐来控制浮空立方体的旋转");
        }

        if (triggerClear)
        {
            Debug.Log("[LLMTester] Quest 3 触发: 发送取消指令...");
            controller.SendUserPrompt("停止控制，取消绑定");
        }
    }
}