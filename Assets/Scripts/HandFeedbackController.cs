using UnityEngine;
using TMPro;
using System.Collections;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class HandFeedbackController : MonoBehaviour
{
    [Header("Tracking Target & Offsets")]
    [Tooltip("手柄目标 Transform (如果为空则自动获取父级或 Right Controller)")]
    public Transform targetController;

    [Tooltip("相对于手柄的相对位置偏移 (X: 左右, Y: 上下, Z: 前后)")]
    public Vector3 localPositionOffset = new Vector3(0f, 0.08f, 0.05f);

    [Tooltip("相对于手柄的相对旋转角度偏移 (X, Y, Z 欧拉角)")]
    public Vector3 localRotationOffset = new Vector3(45f, 0f, 0f);

    [Tooltip("跟随平滑度 (0 代表完全硬性吸附无延迟，数值越大越平滑)")]
    public float smoothSpeed = 0f;

    [Header("UI References")]
    [Tooltip("手柄上跟随的 Canvas 或状态根物体")]
    public GameObject feedbackRoot;

    [Tooltip("显示状态文字 (如: Listening... / Processing...)")]
    public TextMeshProUGUI statusText;

    [Header("Icon / Spinner (可选)")]
    [Tooltip("加载圆圈图标")]
    public RectTransform spinnerIcon;

    [Header("XR Interactor Reference")]
    public XRRayInteractor rightRayInteractor;

    private Coroutine dotAnimCoroutine;
    private bool isProcessing = false;

    private void Awake()
    {
        if (feedbackRoot != null) feedbackRoot.SetActive(false);

        // 如果未在 Inspector 指定手柄，默认取父级
        if (targetController == null && transform.parent != null)
        {
            targetController = transform.parent;
        }
    }

    private void LateUpdate()
    {
        // 1. 基于手柄相对坐标系计算位姿
        UpdateRelativeTransform();

        // 2. Processing 期间旋转加载图标
        if (isProcessing && spinnerIcon != null)
        {
            spinnerIcon.Rotate(Vector3.forward, -240f * Time.deltaTime);
        }
    }

    private void UpdateRelativeTransform()
    {
        if (targetController == null) return;

        // 根据手柄的本地坐标系基向量计算世界坐标
        Vector3 targetWorldPos = targetController.position
            + targetController.right * localPositionOffset.x
            + targetController.up * localPositionOffset.y
            + targetController.forward * localPositionOffset.z;

        // 结合手柄朝向与指定的相对欧拉角计算世界旋转
        Quaternion targetWorldRot = targetController.rotation * Quaternion.Euler(localRotationOffset);

        if (smoothSpeed > 0f)
        {
            transform.position = Vector3.Lerp(transform.position, targetWorldPos, Time.deltaTime * smoothSpeed);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetWorldRot, Time.deltaTime * smoothSpeed);
        }
        else
        {
            transform.position = targetWorldPos;
            transform.rotation = targetWorldRot;
        }
    }

    /// <summary>
    /// 状态 1：开始录音/聆听中
    /// </summary>
    public void StartListening()
    {
        StopAnimation();
        isProcessing = false;

        if (feedbackRoot != null) feedbackRoot.SetActive(true);
        TriggerHaptic(0.4f, 0.08f);

        dotAnimCoroutine = StartCoroutine(AnimateTextDots("<color=#00FF66>🎙 Listening</color>"));
    }

    /// <summary>
    /// 状态 2：正在等待大模型处理
    /// </summary>
    public void StartProcessing()
    {
        StopAnimation();
        isProcessing = true;

        if (feedbackRoot != null) feedbackRoot.SetActive(true);

        dotAnimCoroutine = StartCoroutine(AnimateTextDots("<color=#00E5FF>✨ Processing</color>"));
    }

    /// <summary>
    /// 状态 3：处理完毕 / 请求结束
    /// </summary>
    public void StopFeedback(bool isSuccess = true, string feedbackMessage = "")
    {
        StopAnimation();
        isProcessing = false;

        if (statusText != null && !string.IsNullOrEmpty(feedbackMessage))
        {
            statusText.text = isSuccess
                ? $"<color=#00FF66>✔ {feedbackMessage}</color>"
                : $"<color=red>✘ {feedbackMessage}</color>";
        }

        if (isSuccess)
        {
            TriggerHaptic(0.3f, 0.1f);
        }

        StartCoroutine(HideDelayed(1.2f));
    }

    private void StopAnimation()
    {
        if (dotAnimCoroutine != null)
        {
            StopCoroutine(dotAnimCoroutine);
            dotAnimCoroutine = null;
        }
    }

    private IEnumerator AnimateTextDots(string prefix)
    {
        string[] dots = new string[] { "", ".", "..", "..." };
        int dotIndex = 0;

        while (true)
        {
            if (statusText != null)
            {
                statusText.text = $"{prefix}{dots[dotIndex]}";
            }
            dotIndex = (dotIndex + 1) % dots.Length;
            yield return new WaitForSeconds(0.3f);
        }
    }

    private void TriggerHaptic(float amplitude, float duration)
    {
        if (rightRayInteractor != null && rightRayInteractor.xrController != null)
        {
            rightRayInteractor.xrController.SendHapticImpulse(amplitude, duration);
        }
    }

    private IEnumerator HideDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (feedbackRoot != null) feedbackRoot.SetActive(false);
    }
}