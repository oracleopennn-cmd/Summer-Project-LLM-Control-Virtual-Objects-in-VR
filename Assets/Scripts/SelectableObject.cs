using UnityEngine;

[DisallowMultipleComponent]
public class SelectableObject : MonoBehaviour
{
    [Header("UI Label & Semantic Settings")]
    [Tooltip("物体显示的名称标签及供 LLM 识别的语义名称（例如：Cube, Can）")]
    public string objectLabel = "Object";

    [Header("Runtime Status")]
    [Tooltip("物体当前是否处于被抓取状态")]
    public bool isGrabbed = false;

    private void Awake()
    {
        // 若未填写 objectLabel，默认使用 GameObject 自身的名称
        if (string.IsNullOrWhiteSpace(objectLabel))
        {
            objectLabel = gameObject.name;
        }
    }

    /// <summary>
    /// 抓取物体时调用（可绑定到 XR Grab Interactable 的 Select Entered 事件）
    /// </summary>
    public void OnObjectGrabbed()
    {
        isGrabbed = true;
    }

    /// <summary>
    /// 放下物体时调用（可绑定到 XR Grab Interactable 的 Select Exited 事件）
    /// </summary>
    public void OnObjectReleased()
    {
        isGrabbed = false;
    }

    /// <summary>
    /// 获取当前物体的有效显示名称
    /// </summary>
    public string GetDisplayName()
    {
        return string.IsNullOrWhiteSpace(objectLabel) ? gameObject.name : objectLabel;
    }
}