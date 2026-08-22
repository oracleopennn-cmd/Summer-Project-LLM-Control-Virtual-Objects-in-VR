using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class BindingVisualizer_T : MonoBehaviour
{
    [Header("Controller Reference (留空则自动查找)")]
    public TraditionalUIController traditionalUIController;

    [Header("Line Visual Settings")]
    public float lineWidth = 0.015f; // 1.5 cm
    public Color moveColor = new Color(0f, 1f, 0.5f, 0.9f);    // 移动/位移：青绿色
    public Color rotateColor = new Color(0f, 0.8f, 1f, 0.9f);  // 旋转：亮蓝色
    public Color scaleColor = new Color(1f, 0.8f, 0f, 0.9f);   // 缩放：橙黄色
    public Color lockedColor = new Color(1f, 0.2f, 0.2f, 0.9f);// 锁定：红色

    private LineRenderer lineRenderer;

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.positionCount = 2;
        lineRenderer.useWorldSpace = true; // 启用世界坐标，防止 VR 头显双目渲染与手柄位移错位
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.enabled = false;

        // 材质安全检查，确保在 Quest VR 头显中正常着色
        if (lineRenderer.material == null || lineRenderer.material.name.Contains("Default"))
        {
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null) unlitShader = Shader.Find("Sprites/Default");
            if (unlitShader != null) lineRenderer.material = new Material(unlitShader);
        }

        // 自动探测场景中的 TraditionalUIController 引用
        if (traditionalUIController == null)
        {
            if (TraditionalUIController.Instance != null)
            {
                traditionalUIController = TraditionalUIController.Instance;
            }
            else
            {
#if UNITY_2023_1_OR_NEWER
                traditionalUIController = FindFirstObjectByType<TraditionalUIController>();
#else
                traditionalUIController = FindObjectOfType<TraditionalUIController>();
#endif
            }
        }
    }

    private void Update()
    {
        if (traditionalUIController == null && TraditionalUIController.Instance != null)
        {
            traditionalUIController = TraditionalUIController.Instance;
        }

        if (traditionalUIController != null &&
            traditionalUIController.isBound &&
            traditionalUIController.currentSource != null &&
            traditionalUIController.currentTarget != null)
        {
            UpdateVisualLine(
                traditionalUIController.currentSource.transform.position,
                traditionalUIController.currentTarget.transform.position,
                traditionalUIController.activeAction,
                traditionalUIController.isLocked
            );
        }
        else
        {
            if (lineRenderer.enabled)
            {
                lineRenderer.enabled = false;
            }
        }
    }

    private void UpdateVisualLine(Vector3 startPos, Vector3 endPos, string action, bool isLocked)
    {
        if (!lineRenderer.enabled)
        {
            lineRenderer.enabled = true;
        }

        // 实时更新两端世界坐标
        lineRenderer.SetPosition(0, startPos);
        lineRenderer.SetPosition(1, endPos);

        // 动态根据操作模式与锁定状态切换颜色
        Color targetColor;
        if (isLocked)
        {
            targetColor = lockedColor;
        }
        else if (action.Equals("Rotate", System.StringComparison.OrdinalIgnoreCase))
        {
            targetColor = rotateColor;
        }
        else if (action.Equals("Scale", System.StringComparison.OrdinalIgnoreCase))
        {
            targetColor = scaleColor;
        }
        else
        {
            targetColor = moveColor;
        }

        lineRenderer.startColor = targetColor;
        lineRenderer.endColor = targetColor;
    }
}