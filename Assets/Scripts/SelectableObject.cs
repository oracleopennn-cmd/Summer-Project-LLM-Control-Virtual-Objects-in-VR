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

    [Tooltip("名称标签悬浮在物体上方的位置偏移量（相对局部坐标）")]
    public Vector3 uiOffset = new Vector3(0f, 0.25f, 0f);

    [Tooltip("名称标签 UI 的缩放比例 (Vector3)")]
    public Vector3 uiScale = new Vector3(0.005f, 0.005f, 0.005f);

    private GameObject instantiatedTag;
    private TextMeshProUGUI nameTextComponent;
    private bool isGrabbed = false;

    private void Start()
    {
        InitializeNameTag();
    }

    /// <summary>
    /// 当在 Inspector 中修改数值时自动调用，实现在编辑器中实时刷新 Scale 和 Offset
    /// </summary>
    private void OnValidate()
    {
        if (instantiatedTag != null)
        {
            instantiatedTag.transform.localPosition = uiOffset;
            instantiatedTag.transform.localScale = uiScale;
        }
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

        // 实例化预制体，直接设置父节点
        instantiatedTag = Instantiate(nameTagPrefab, transform);

        // 使用 localPosition 设置相对偏移量
        instantiatedTag.transform.localPosition = uiOffset;
        instantiatedTag.transform.localRotation = Quaternion.identity;
        instantiatedTag.transform.localScale = uiScale;

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
            // 确保每次显示时同步 localPosition 与 Scale
            instantiatedTag.transform.localPosition = uiOffset;
            instantiatedTag.transform.localScale = uiScale;

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

    private void LateUpdate()
    {
        // 确保 UI 激活时始终面向 VR 视角/主相机，且位置精准维持在 localPosition 偏移上
        if (instantiatedTag != null && instantiatedTag.activeSelf && Camera.main != null)
        {
            instantiatedTag.transform.localPosition = uiOffset;
            instantiatedTag.transform.rotation = Quaternion.LookRotation(
                instantiatedTag.transform.position - Camera.main.transform.position
            );
        }
    }

    /// <summary>
    /// 在 Scene 视图中绘制 Gizmo，方便在编辑器内可视化调整 UI 的悬浮位置
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.TransformPoint(uiOffset), 0.03f);
    }
}