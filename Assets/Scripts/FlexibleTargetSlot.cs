using System;
using UnityEngine;

public class FlexibleTargetSlot : MonoBehaviour
{
    [Header("Matching Configuration")]
    public BlockShape requiredShape;

    [Header("Error Tolerance Settings")]
    [Tooltip("基准位置容差（单位：米）。建议设为 0.15 ~ 0.25")]
    public float positionTolerance = 0.20f;

    [Tooltip("允许的最大旋转角度误差（单位：度）。")]
    public float rotationTolerance = 35.0f;

    [Tooltip("允许的最大缩放比例误差")]
    public float scaleTolerance = 0.15f;

    [Tooltip("⚠️ 警告：如果在 Stage 3 缩小了物体，请务必保持此项为 false，否则容差会变得极小！")]
    public bool scaleThresholdWithObject = false;

    [Header("Runtime Status")]
    public bool IsFilled { get; private set; } = false;
    public GameObject MatchedBlock { get; private set; }

    // 全局事件广播
    public static event Action OnAnySlotFilled;
    public static event Action OnAnySlotUnfilled;

    // 抓取该物体及其子物体的所有 MeshRenderer
    private MeshRenderer[] meshRenderers;

    // 缓存 Stage3_Manager
    private Stage3_Manager stage3Manager;

    private void Start()
    {
        meshRenderers = GetComponentsInChildren<MeshRenderer>(true);

#if UNITY_2023_1_OR_NEWER
        stage3Manager = UnityEngine.Object.FindFirstObjectByType<Stage3_Manager>();
#else
        stage3Manager = UnityEngine.Object.FindObjectOfType<Stage3_Manager>();
#endif
    }

    private void Update()
    {
        float scaleMultiplier = scaleThresholdWithObject ? transform.lossyScale.x : 1.0f;
        float effectivePositionTolerance = positionTolerance * scaleMultiplier;

        if (IsFilled && MatchedBlock != null)
        {
            // ==========================================
            // 状态 1：已填充 -> 持续监测是否脱离判定范围
            // ==========================================
            Vector3 slotCenter = GetCenterPosition(transform);
            Vector3 blockCenter = GetCenterPosition(MatchedBlock.transform);

            float distance = Vector3.Distance(slotCenter, blockCenter);
            float angle = GetSymmetricAngle(transform.rotation, MatchedBlock.transform.rotation);
            float scaleDiff = Vector3.Distance(transform.lossyScale, MatchedBlock.transform.lossyScale);

            // 引入 50% 滞回防抖阈值
            float unmatchDist = effectivePositionTolerance * 1.5f;
            float unmatchAngle = rotationTolerance + 15.0f;
            float unmatchScale = scaleTolerance * 1.5f;

            if (distance > unmatchDist || angle > unmatchAngle || scaleDiff > unmatchScale)
            {
                UnregisterMatch();
            }
        }
        else
        {
            // ==========================================
            // 状态 2：未填充 -> 就近寻找最合适的积木
            // ==========================================
#if UNITY_2023_1_OR_NEWER
            BlockIdentity[] blocks = UnityEngine.Object.FindObjectsByType<BlockIdentity>(FindObjectsSortMode.None);
#else
            BlockIdentity[] blocks = UnityEngine.Object.FindObjectsOfType<BlockIdentity>();
#endif

            Vector3 slotCenter = GetCenterPosition(transform);
            BlockIdentity bestBlock = null;
            float minDistance = float.MaxValue;

            foreach (var block in blocks)
            {
                if (block.isMatched || block.shapeType != requiredShape) continue;

                Vector3 blockCenter = GetCenterPosition(block.transform);
                float dist = Vector3.Distance(slotCenter, blockCenter);

                if (dist < minDistance)
                {
                    minDistance = dist;
                    bestBlock = block;
                }
            }

            if (bestBlock != null)
            {
                Vector3 blockCenter = GetCenterPosition(bestBlock.transform);
                float distance = minDistance;
                float angle = GetSymmetricAngle(transform.rotation, bestBlock.transform.rotation);
                float scaleDiff = Vector3.Distance(transform.lossyScale, bestBlock.transform.lossyScale);

                // 判定是否匹配成功
                if (distance <= effectivePositionTolerance && angle <= rotationTolerance && scaleDiff <= scaleTolerance)
                {
                    RegisterMatch(bestBlock);
                }
            }
        }
    }

    /// <summary>
    /// 获取物体真实的视觉几何网格中心（消除模型 Pivot 轴心点错位影响）
    /// </summary>
    private Vector3 GetCenterPosition(Transform target)
    {
        if (target == null) return Vector3.zero;

        if (target.TryGetComponent<Renderer>(out Renderer r))
        {
            return r.bounds.center;
        }

        Renderer childRenderer = target.GetComponentInChildren<Renderer>();
        if (childRenderer != null)
        {
            return childRenderer.bounds.center;
        }

        return target.position;
    }

    private void RegisterMatch(BlockIdentity block)
    {
        IsFilled = true;
        block.isMatched = true;
        MatchedBlock = block.gameObject;

        if (meshRenderers != null)
        {
            foreach (var mr in meshRenderers)
            {
                if (mr != null) mr.enabled = false;
            }
        }

        Debug.Log($"<color=green>[Stage 3 Match]</color> Slot ({requiredShape}) matched with [{block.name}]!");
        OnAnySlotFilled?.Invoke();
    }

    private void UnregisterMatch()
    {
        if (MatchedBlock != null)
        {
            Debug.Log($"<color=orange>[Stage 3 Unmatch]</color> Slot ({requiredShape}) lost block [{MatchedBlock.name}]!");

            if (MatchedBlock.TryGetComponent<BlockIdentity>(out BlockIdentity block))
            {
                block.isMatched = false;
            }
        }

        IsFilled = false;
        MatchedBlock = null;

        if (meshRenderers != null)
        {
            foreach (var mr in meshRenderers)
            {
                if (mr != null) mr.enabled = true;
            }
        }

        OnAnySlotUnfilled?.Invoke();
    }

    private float GetSymmetricAngle(Quaternion rotA, Quaternion rotB)
    {
        Quaternion deltaRot = Quaternion.Inverse(rotA) * rotB;
        Vector3 euler = deltaRot.eulerAngles;

        float diffX = euler.x > 180f ? euler.x - 360f : euler.x;
        float diffY = euler.y > 180f ? euler.y - 360f : euler.y;
        float diffZ = euler.z > 180f ? euler.z - 360f : euler.z;

        string shapeName = requiredShape.ToString().ToLower();

        if (shapeName.Contains("cube"))
        {
            diffX = Mathf.Repeat(euler.x + 45f, 90f) - 45f;
            diffY = Mathf.Repeat(euler.y + 45f, 90f) - 45f;
            diffZ = Mathf.Repeat(euler.z + 45f, 90f) - 45f;
        }
        else if (shapeName.Contains("rect") || shapeName.Contains("plank"))
        {
            diffX = Mathf.Repeat(euler.x + 90f, 180f) - 90f;
            diffY = Mathf.Repeat(euler.y + 90f, 180f) - 90f;
            diffZ = Mathf.Repeat(euler.z + 90f, 180f) - 90f;
        }
        else if (shapeName.Contains("tri"))
        {
            diffY = Mathf.Repeat(euler.y + 90f, 180f) - 90f;
            diffZ = Mathf.Repeat(euler.z + 90f, 180f) - 90f;
        }

        return Mathf.Sqrt(diffX * diffX + diffY * diffY + diffZ * diffZ);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = IsFilled ? Color.green : Color.yellow;
        float scaleMultiplier = scaleThresholdWithObject ? transform.lossyScale.x : 1.0f;
        float effectivePositionTolerance = positionTolerance * scaleMultiplier;

        Vector3 center = GetCenterPosition(transform);
        Gizmos.DrawWireSphere(center, effectivePositionTolerance);
    }
}