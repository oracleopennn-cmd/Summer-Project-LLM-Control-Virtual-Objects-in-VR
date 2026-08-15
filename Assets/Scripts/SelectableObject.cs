using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public class SelectableObject : MonoBehaviour
{
    [Header("UI Label Settings")]
    [Tooltip("物体在悬浮 UI 上显示的名称（例如：Cube, Can）")]
    public string objectLabel = "Object";

    [Tooltip("第一步制作的名称标签预制体 (ObjectNameTag)")]
    public GameObject nameTagPrefab;

    [Tooltip("名称标签悬浮在物体上方的位置偏移量")]
    public Vector3 uiOffset = new Vector3(0f, 0.25f, 0f);

    private GameObject instantiatedTag;
    private TextMeshProUGUI nameTextComponent;
    private bool isGrabbed = false;

    private void Start()
    {
        InitializeNameTag();
    }

    /// <summary>
    /// 初始化并实例化悬浮标签
    /// </summary>
    private void InitializeNameTag()
    {
        if (nameTagPrefab == null)
        {
            Debug.LogWarning($"[{gameObject.name}] SelectableObject: 未指定 Name Tag Prefab！", this);
            return;
        }

        // 实例化预制体并作为当前物体的子节点
        instantiatedTag = Instantiate(nameTagPrefab, transform.position + uiOffset, Quaternion.identity, transform);

        // 获取 TextMeshProUGUI 组件并更新文本
        nameTextComponent = instantiatedTag.GetComponentInChildren<TextMeshProUGUI>();
        if (nameTextComponent != null)
        {
            nameTextComponent.text = objectLabel;
        }
        else
        {
            Debug.LogWarning($"[{gameObject.name}] SelectableObject: Prefab 中找不到 TextMeshProUGUI 组件！", this);
        }

        // 默认隐藏标签
        instantiatedTag.SetActive(false);
    }

    /// <summary>
    /// 射线悬停进入时调用（仅在未被抓取时显示 UI）
    /// </summary>
    public void ShowNameTag()
    {
        if (!isGrabbed && instantiatedTag != null)
        {
            // 确保每次显示时再次同步文本（防止运行时动态改了 objectLabel）
            if (nameTextComponent != null)
            {
                nameTextComponent.text = objectLabel;
            }
            instantiatedTag.SetActive(true);
        }
    }

    /// <summary>
    /// 射线悬停离开时调用
    /// </summary>
    public void HideNameTag()
    {
        if (instantiatedTag != null)
        {
            instantiatedTag.SetActive(false);
        }
    }

    /// <summary>
    /// 抓取物体时调用（绑定到 Select Entered 事件）
    /// </summary>
    public void OnObjectGrabbed()
    {
        isGrabbed = true;
        HideNameTag(); // 抓取时立即强制隐藏 UI
    }

    /// <summary>
    /// 放下物体时调用（绑定到 Select Exited 事件）
    /// </summary>
    public void OnObjectReleased()
    {
        isGrabbed = false;
    }

    /// <summary>
    /// 在 Scene 视图中绘制 Gizmo，方便在编辑器内可视化调整 UI 的悬浮位置
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position + uiOffset, 0.03f);
    }
}