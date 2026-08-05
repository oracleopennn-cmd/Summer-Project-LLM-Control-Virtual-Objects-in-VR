using System.Collections;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.InputSystem;

public class VoiceInputController : MonoBehaviour
{
    [Header("绑定控制器引用")]
    public LLMSemanticController semanticController;

    [Header("录音设置")]
    public int recordingFrequency = 16000; // 16kHz 适合语音识别
    public int maxRecordingTimeSeconds = 10;

    private AudioClip recordedClip;
    private string microphoneDevice;
    private bool isRecording = false;

    void Start()
    {
        // 检查是否有可用麦克风设备
        if (Microphone.devices.Length > 0)
        {
            microphoneDevice = Microphone.devices[0];
            Debug.Log($"[VoiceInput] 已找到麦克风设备: {microphoneDevice}");
        }
        else
        {
            Debug.LogError("[VoiceInput] 未检测到麦克风设备！");
        }

        // 自动获取脚本组件引用
        if (semanticController == null)
        {
            semanticController = GetComponent<LLMSemanticController>();
        }
    }

    void Update()
    {
        // 1. 电脑端键盘测试 (使用 New Input System 避免报错)
        if (Keyboard.current != null)
        {
            if (Keyboard.current.vKey.wasPressedThisFrame)
            {
                StartRecording();
            }
            else if (Keyboard.current.vKey.wasReleasedThisFrame)
            {
                StopAndProcessRecording();
            }
        }

        // 2. Quest 3 手柄输入检测
        CheckVRTriggerInput();
    }

    private void CheckVRTriggerInput()
    {
        var rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (rightHand.isValid)
        {
            // 使用完整的命名空间 UnityEngine.XR.CommonUsages 解决与 InputSystem 的命名冲突
            if (rightHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool triggerPressed))
            {
                if (triggerPressed && !isRecording)
                {
                    StartRecording();
                }
                else if (!triggerPressed && isRecording)
                {
                    StopAndProcessRecording();
                }
            }
        }
    }

    public void StartRecording()
    {
        if (isRecording || string.IsNullOrEmpty(microphoneDevice)) return;

        isRecording = true;
        Debug.Log("[VoiceInput] 开始录音... (请说话)");
        recordedClip = Microphone.Start(microphoneDevice, false, maxRecordingTimeSeconds, recordingFrequency);
    }

    public void StopAndProcessRecording()
    {
        if (!isRecording) return;

        isRecording = false;
        int recordingPosition = Microphone.GetPosition(microphoneDevice);
        Microphone.End(microphoneDevice);
        Debug.Log("[VoiceInput] 停止录音，开始处理音频...");

        if (recordingPosition <= 0 || recordedClip == null)
        {
            Debug.LogWarning("[VoiceInput] 录音无效或时间太短！");
            return;
        }

        // 将音频转为 WAV 格式字节数组
        byte[] wavBytes = WavUtility.FromAudioClip(recordedClip);

        // 触发发送流程
        SendAudioToGemini(wavBytes);
    }

    private void SendAudioToGemini(byte[] wavData)
    {
        string base64Audio = System.Convert.ToBase64String(wavData);
        Debug.Log($"[VoiceInput] 音频数据打包完成，大小: {wavData.Length} bytes, Base64 长度: {base64Audio.Length}");

        if (semanticController != null)
        {
            semanticController.SendAudioWithVisionPrompt(base64Audio, "audio/wav");
        }
        else
        {
            Debug.LogError("[VoiceInput] 未挂载 LLMSemanticController 引用！");
        }
    }
}