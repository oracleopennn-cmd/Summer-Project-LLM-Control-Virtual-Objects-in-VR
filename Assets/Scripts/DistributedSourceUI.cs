using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DistributedSourceUI : MonoBehaviour
{
    [Header("UI 文本")]
    public TMP_Text titleText;
    public TMP_Text connectionTypeText;

    [Header("模式按钮")]
    public Button scaleButton;
    public Button moveButton;
    public Button rotateButton;
    public Button disconnectButton;

    private GameObject sourceOwner;

    private void Awake()
    {
        // 自动寻源并绑定点击事件
        SelectableObject selectable = GetComponentInParent<SelectableObject>();
        sourceOwner = selectable != null ? selectable.gameObject : transform.parent.gameObject;

        if (scaleButton != null)
            scaleButton.onClick.AddListener(() => TraditionalUIController.Instance?.OnButtonClicked(sourceOwner, "Scale"));
        if (moveButton != null)
            moveButton.onClick.AddListener(() => TraditionalUIController.Instance?.OnButtonClicked(sourceOwner, "Move"));
        if (rotateButton != null)
            rotateButton.onClick.AddListener(() => TraditionalUIController.Instance?.OnButtonClicked(sourceOwner, "Rotate"));
        if (disconnectButton != null)
            disconnectButton.onClick.AddListener(() => TraditionalUIController.Instance?.OnDisconnectClicked());
    }

    private void OnEnable()
    {
        RefreshUIState();
    }

    /// <summary>
    /// 根据 TraditionalUIController 的状态，严格控制自身组件的显隐
    /// </summary>
    public void RefreshUIState()
    {
        if (Camera.main != null)
        {
            transform.LookAt(Camera.main.transform);
            transform.Rotate(0, 180, 0);
        }

        bool isCurrentlyBound = TraditionalUIController.Instance != null &&
                               TraditionalUIController.Instance.isBound &&
                               TraditionalUIController.Instance.currentSource == sourceOwner;

        bool isLocked = TraditionalUIController.Instance != null &&
                        TraditionalUIController.Instance.isLocked;

        if (!isCurrentlyBound)
        {
            // 状态 1：未建立连接 -> 等待选择连接模式
            if (titleText != null) titleText.text = "Create Connection:";
            if (connectionTypeText != null) connectionTypeText.gameObject.SetActive(false);

            if (scaleButton != null) scaleButton.gameObject.SetActive(true);
            if (moveButton != null) moveButton.gameObject.SetActive(true);
            if (rotateButton != null) rotateButton.gameObject.SetActive(true);
            if (disconnectButton != null) disconnectButton.gameObject.SetActive(false);
        }
        else if (isLocked)
        {
            // 状态 2：有连接建立，且处于【锁定】状态 -> 允许直接点按钮切换连接类型
            if (titleText != null) titleText.text = "Switch Connection Type:";
            if (connectionTypeText != null)
            {
                connectionTypeText.gameObject.SetActive(true);
                connectionTypeText.text = $"Type: {TraditionalUIController.Instance.activeAction}";
            }

            if (scaleButton != null) scaleButton.gameObject.SetActive(true);
            if (moveButton != null) moveButton.gameObject.SetActive(true);
            if (rotateButton != null) rotateButton.gameObject.SetActive(true);
            if (disconnectButton != null) disconnectButton.gameObject.SetActive(false);
        }
        else
        {
            // 状态 3：有连接建立，且处于【未锁定】状态 -> 仅出现 Bind Unbind 与 Disconnect
            if (titleText != null) titleText.text = "Bind & Unbind Text";
            if (connectionTypeText != null) connectionTypeText.gameObject.SetActive(false);

            if (scaleButton != null) scaleButton.gameObject.SetActive(false);
            if (moveButton != null) moveButton.gameObject.SetActive(false);
            if (rotateButton != null) rotateButton.gameObject.SetActive(false);
            if (disconnectButton != null) disconnectButton.gameObject.SetActive(true);
        }
    }
}