using UnityEngine;

public class VRHUDFollow : MonoBehaviour
{
    [Header("Camera Reference")]
    public Transform targetCamera;

    [Header("Position Offset (Left-Bottom)")]
    // X: -0.35 (左), Y: -0.2 (下), Z: 0.8 (前方距离)
    public Vector3 offset = new Vector3(-0.35f, -0.2f, 0.8f);

    [Header("UI Size Control")]
    [Tooltip("控制 UI 的物理缩放尺寸。VR World Space UI 推荐设置在 0.0005 ~ 0.001 之间")]
    public Vector3 targetScale = new Vector3(0.0008f, 0.0008f, 0.0008f);

    [Header("Smooth Speeds")]
    public float moveSpeed = 6.0f;
    public float rotateSpeed = 6.0f;

    private void Start()
    {
        if (targetCamera == null && Camera.main != null)
        {
            targetCamera = Camera.main.transform;
        }

        // 初始化时直接应用设定的缩放尺寸
        transform.localScale = targetScale;
    }

    private void LateUpdate()
    {
        if (targetCamera == null) return;

        Vector3 targetPos = targetCamera.TransformPoint(offset);
        Quaternion targetRot = targetCamera.rotation;

        transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * moveSpeed);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotateSpeed);
    }
}