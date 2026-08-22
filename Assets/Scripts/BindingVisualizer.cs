using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class BindingVisualizer : MonoBehaviour
{
    [Header("Controller References (支持二选一或自动查找)")]
    public LLMSemanticController semanticController;
    public TraditionalUIController traditionalUIController;

    [Header("Line Visual Settings")]
    public float lineWidth = 0.015f; // 1.5 cm
    public Color moveColor = new Color(0f, 1f, 0.5f, 0.8f);    // 移动连线色 (青绿)
    public Color rotateColor = new Color(0f, 0.8f, 1f, 0.8f);  // 旋转连线色 (亮蓝)
    public Color scaleColor = new Color(1f, 0.8f, 0f, 0.8f);   // 缩放连线色 (橙黄)
    public Color lockedColor = new Color(1f, 0.2f, 0.2f, 0.8f);// 锁定连线色 (红色)

    private LineRenderer lineRenderer;

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.positionCount = 2;
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.enabled = false;

        // 自动查找引用
        if (semanticController == null)
        {
#if UNITY_2023_1_OR_NEWER
            semanticController = FindFirstObjectByType<LLMSemanticController>();
#else
            semanticController = FindObjectOfType<LLMSemanticController>();
#endif
        }

        if (traditionalUIController == null)
        {
#if UNITY_2023_1_OR_NEWER
            traditionalUIController = FindFirstObjectByType<TraditionalUIController>();
#else
            traditionalUIController = FindObjectOfType<TraditionalUIController>();
#endif
        }
    }

    private void Update()
    {
        // 1. 优先检测 Traditional UI 控制器状态
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
            return;
        }

        // 2. 其次检测 LLM Semantic 控制器状态
        if (semanticController != null &&
            semanticController.isBound &&
            semanticController.currentSource != null &&
            semanticController.currentTarget != null)
        {
            UpdateVisualLine(
                semanticController.currentSource.transform.position,
                semanticController.currentTarget.transform.position,
                semanticController.activeAction,
                semanticController.isLocked
            );
            return;
        }

        // 3. 无任何绑定时隐藏连线
        if (lineRenderer.enabled)
        {
            lineRenderer.enabled = false;
        }
    }

    private void UpdateVisualLine(Vector3 startPos, Vector3 endPos, string action, bool isLocked)
    {
        if (!lineRenderer.enabled)
        {
            lineRenderer.enabled = true;
        }

        // 实时更新两端坐标
        lineRenderer.SetPosition(0, startPos);
        lineRenderer.SetPosition(1, endPos);

        // 动态根据操作模式与锁定状态切换材质颜色
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
        else // Move / Translate
        {
            targetColor = moveColor;
        }

        lineRenderer.startColor = targetColor;
        lineRenderer.endColor = targetColor;
    }
}