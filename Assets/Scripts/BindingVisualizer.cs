using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class BindingVisualizer : MonoBehaviour
{
    [Header("绑定控制器引用")]
    public LLMSemanticController semanticController;

    private LineRenderer lineRenderer;

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.positionCount = 2;
        lineRenderer.startWidth = 0.015f; // 线条宽度 1.5 cm
        lineRenderer.endWidth = 0.015f;
        lineRenderer.enabled = false;

        // 如果 Inspector 里没有手动拖入，自动尝试获取同物体上的组件
        if (semanticController == null)
        {
            semanticController = GetComponent<LLMSemanticController>();
        }
    }

    void Update()
    {
        if (semanticController == null) return;

        // 改为匹配 LLMSemanticController 中的小写字段名
        if (semanticController.isBound &&
            semanticController.currentSource != null &&
            semanticController.currentTarget != null)
        {
            lineRenderer.enabled = true;

            // 实时更新发光连线的起点（易拉罐）和终点（立方体）位置
            lineRenderer.SetPosition(0, semanticController.currentSource.transform.position);
            lineRenderer.SetPosition(1, semanticController.currentTarget.transform.position);
        }
        else
        {
            lineRenderer.enabled = false;
        }
    }
}