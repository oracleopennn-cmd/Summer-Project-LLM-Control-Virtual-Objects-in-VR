using UnityEngine;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit;

public class VRNameTagManager : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("视角固定位置的 Canvas 或 HUD 父节点")]
    public GameObject hudContainer;
    [Tooltip("用于显示物体名称的 TextMeshProUGUI 组件")]
    public TextMeshProUGUI nameText;

    private GameObject currentHoveredObject;

    private void Awake()
    {
        if (hudContainer != null)
        {
            hudContainer.SetActive(false);
        }
    }

    /// <summary>
    /// 直接绑定到 XR Interactable 的 First Hover Entered 事件 (动态参数)
    /// </summary>
    public void OnHoverEntered(HoverEnterEventArgs args)
    {
        if (args == null || args.interactableObject == null) return;

        GameObject targetObj = args.interactableObject.transform.gameObject;
        currentHoveredObject = targetObj;

        // 读取物体名称或 SelectableObject 标签
        string displayName = targetObj.name;
        if (targetObj.TryGetComponent<SelectableObject>(out var selectable))
        {
            if (selectable.isGrabbed) return; // 抓取中不显示
            displayName = selectable.GetDisplayName();
        }

        if (nameText != null)
        {
            nameText.text = displayName;
        }

        if (hudContainer != null && !hudContainer.activeSelf)
        {
            hudContainer.SetActive(true);
        }
    }

    /// <summary>
    /// 直接绑定到 XR Interactable 的 Last Hover Exited 事件 (动态参数)
    /// </summary>
    public void OnHoverExited(HoverExitEventArgs args)
    {
        if (args == null || args.interactableObject == null) return;

        GameObject targetObj = args.interactableObject.transform.gameObject;
        if (currentHoveredObject == targetObj)
        {
            currentHoveredObject = null;
            if (hudContainer != null && hudContainer.activeSelf)
            {
                hudContainer.SetActive(false);
            }
        }
    }
}