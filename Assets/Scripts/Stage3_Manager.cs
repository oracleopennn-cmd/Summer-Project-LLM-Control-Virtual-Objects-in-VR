using System.Collections;
using UnityEngine;
using TMPro;

public class Stage3_Manager : MonoBehaviour
{
    [Header("Model Group References")]
    [Tooltip("存放所有 Ghost 全息槽位的父节点")]
    public GameObject ghostContainer;
    [Tooltip("存放所有实体积木的父节点")]
    public Transform solidBlockContainer;

    [Header("Bullet & Break Effect")]
    public GameObject bulletPrefab;
    public Transform bulletSpawnPoint;
    public float bulletSpeed = 60f;

    [Header("UI & Controller")]
    public MonoBehaviour controller;
    public TextMeshProUGUI directiveText;
    public GameObject uiCanvas;

    [Header("Timing Settings")]
    public float settleDuration = 3.0f; // 确保开局有足够时间落地
    public float observeDuration = 2.5f;

    [Tooltip("打散后实体积木缩小的目标比例（玩法设计：强制玩家放大）")]
    public float scatteredBlockScale = 0.1f;

    [Header("Trial & Configuration")]
    [Tooltip("Stage 3 需要完成几轮组装（可自定义 Trial 数量）")]
    public int totalTrials = 3;

    [Header("Runtime Status")]
    public int currentTrialIndex = 0;

    private FlexibleTargetSlot[] allSlots;
    private bool isRebuildPhaseActive = false;
    private float trialStartTime = 0f;

    private struct InitialPose
    {
        public Vector3 localPos;
        public Quaternion localRot;
        public Vector3 localScale;
    }
    private InitialPose[] initialPoses;

    private void OnEnable()
    {
        if (controller == null)
        {
#if UNITY_2023_1_OR_NEWER
            controller = Object.FindFirstObjectByType<LLMSemanticController>();
#else
            controller = Object.FindObjectOfType<LLMSemanticController>();
#endif
        }

        if (directiveText == null && uiCanvas != null)
        {
            directiveText = uiCanvas.GetComponentInChildren<TextMeshProUGUI>();
        }

        if (ghostContainer != null)
        {
            allSlots = ghostContainer.GetComponentsInChildren<FlexibleTargetSlot>(true);
        }

        if (allSlots == null || allSlots.Length == 0)
        {
            Debug.LogError("<color=red>[Stage 3 Error]</color> 场景中没有找到任何 FlexibleTargetSlot 槽位！");
            UpdateUI("<color=red>⚠️ Stage 3 Error:</color>\nNo Target Slots found in scene!");
            return;
        }

        CacheInitialPoses();
        currentTrialIndex = 0;
        StartNextTrial();
    }

    private void CacheInitialPoses()
    {
        if (solidBlockContainer == null) return;
        int count = solidBlockContainer.childCount;
        initialPoses = new InitialPose[count];
        for (int i = 0; i < count; i++)
        {
            Transform child = solidBlockContainer.GetChild(i);
            initialPoses[i] = new InitialPose
            {
                localPos = child.localPosition,
                localRot = child.localRotation,
                localScale = child.localScale
            };
        }
    }

    private void StartNextTrial()
    {
        if (currentTrialIndex >= totalTrials)
        {
            CompleteStage3();
            return;
        }

        if (controller != null && controller is LLMSemanticController semanticCtrl)
        {
            semanticCtrl.ForceResetBinding();
        }

        ResetBlocksForNewTrial();
        StartCoroutine(Stage3Routine());
    }

    private void ResetBlocksForNewTrial()
    {
        if (solidBlockContainer == null) return;
        for (int i = 0; i < solidBlockContainer.childCount; i++)
        {
            Transform child = solidBlockContainer.GetChild(i);
            child.localPosition = initialPoses[i].localPos;
            child.localRotation = initialPoses[i].localRot;

            // ⚠️ 恢复原始大尺寸，准备开启新一轮的下落
            child.localScale = initialPoses[i].localScale;

            if (child.TryGetComponent<Rigidbody>(out Rigidbody rb))
            {
#if UNITY_6000_0_OR_NEWER
                rb.linearVelocity = Vector3.zero;
#else
                rb.velocity = Vector3.zero;
#endif
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = false;
                rb.WakeUp();
            }

            if (child.TryGetComponent<BlockIdentity>(out BlockIdentity blockId))
            {
                blockId.isMatched = false;
            }
        }
    }

    private IEnumerator Stage3Routine()
    {
        isRebuildPhaseActive = false;

        if (ghostContainer != null)
        {
            ghostContainer.transform.localScale = Vector3.one;
            ghostContainer.SetActive(false);
        }

        UpdateUI($"Stage 3: Rebuild Trial ({currentTrialIndex + 1}/{totalTrials})\nObserving initial block positions...");
        yield return new WaitForSeconds(settleDuration);

        RecordStablePoseToGhosts();
        yield return new WaitForSeconds(observeDuration);

        UpdateUI($"Stage 3: Trial ({currentTrialIndex + 1}/{totalTrials})\nBreaking and scattering blocks!");
        FireBulletAndScatterBlocks();
        yield return new WaitForSeconds(2.0f);

        // 💡 打散后将实体积木强制缩小，增加游戏难度
        ScaleDownBlocksAndEnablePhysics();

        if (ghostContainer != null)
        {
            ghostContainer.SetActive(true);

            MeshRenderer[] mrs = ghostContainer.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var mr in mrs)
            {
                if (mr != null) mr.enabled = true;
            }
        }

        UpdateUI($"Stage 3: Rebuild Trial ({currentTrialIndex + 1}/{totalTrials})\nMatch each shape to its hologram frame using voice commands.");

        trialStartTime = Time.time;
        isRebuildPhaseActive = true;
    }

    private void Update()
    {
        if (!isRebuildPhaseActive) return;
        CheckAllSlotsFilled();
    }

    private void CheckAllSlotsFilled()
    {
        if (allSlots == null || allSlots.Length == 0) return;

        bool allFilled = true;
        foreach (var slot in allSlots)
        {
            if (slot != null && !slot.IsFilled)
            {
                allFilled = false;
                break;
            }
        }

        if (allFilled)
        {
            OnTrialCompleted();
        }
    }

    private void OnTrialCompleted()
    {
        isRebuildPhaseActive = false;
        float duration = Time.time - trialStartTime;

        UpdateUI($"🎉 Trial {currentTrialIndex + 1} Completed!\nTime: {duration:F1}s\nPreparing next trial...");
        currentTrialIndex++;

        Invoke(nameof(StartNextTrial), 2.0f);
    }

    private void CompleteStage3()
    {
        isRebuildPhaseActive = false;
        UpdateUI("🎉 Stage 3 Complete!\nAll trials successfully finished!");
    }

    private void RecordStablePoseToGhosts()
    {
        if (ghostContainer == null || solidBlockContainer == null) return;

        // 父节点必须是 1:1 才能正确记录真实坐标
        ghostContainer.transform.localScale = Vector3.one;

        Transform ghostParent = ghostContainer.transform;
        int count = Mathf.Min(ghostParent.childCount, solidBlockContainer.childCount);

        for (int i = 0; i < count; i++)
        {
            Transform ghostChild = ghostParent.GetChild(i);
            Transform solidChild = solidBlockContainer.GetChild(i);

            // 1:1 完全复制开局沉降后的坐标、旋转和大小
            ghostChild.position = solidChild.position;
            ghostChild.rotation = solidChild.rotation;
            ghostChild.localScale = solidChild.localScale;

            ghostChild.gameObject.SetActive(true);

            if (ghostChild.TryGetComponent<Collider>(out Collider col))
            {
                col.enabled = false;
            }
        }
    }

    private void FireBulletAndScatterBlocks()
    {
        if (bulletPrefab != null && bulletSpawnPoint != null)
        {
            GameObject bullet = Instantiate(bulletPrefab, bulletSpawnPoint.position, bulletSpawnPoint.rotation);
            if (bullet.TryGetComponent<Rigidbody>(out Rigidbody rb))
            {
#if UNITY_6000_0_OR_NEWER
                rb.linearVelocity = bulletSpawnPoint.forward * bulletSpeed;
#else
                rb.velocity = bulletSpawnPoint.forward * bulletSpeed;
#endif
            }
        }

        for (int i = 0; i < solidBlockContainer.childCount; i++)
        {
            Transform child = solidBlockContainer.GetChild(i);
            if (child.TryGetComponent<Rigidbody>(out Rigidbody rb))
            {
                rb.isKinematic = false;
                rb.AddForce(new Vector3(Random.Range(-2f, 2f), 3f, Random.Range(-2f, 2f)), ForceMode.Impulse);
            }
        }
    }

    private void ScaleDownBlocksAndEnablePhysics()
    {
        // 💡 玩法机制：仅仅将实体积木强制缩小，迫使玩家使用 Scale 功能复原它们！
        for (int i = 0; i < solidBlockContainer.childCount; i++)
        {
            Transform child = solidBlockContainer.GetChild(i);

            // 将实体积木统一缩小到指定的迷你尺寸 (默认 0.1)
            child.localScale = new Vector3(scatteredBlockScale, scatteredBlockScale, scatteredBlockScale);

            if (child.TryGetComponent<Rigidbody>(out Rigidbody rb))
            {
#if UNITY_6000_0_OR_NEWER
                rb.linearVelocity = Vector3.zero;
#else
                rb.velocity = Vector3.zero;
#endif
                rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            }
        }

        // ⚠️ 绝不调整 ghostContainer 的缩放，保留全息体 1:1 的巨大原始轮廓，提供目标参照物。
    }

    private void UpdateUI(string message)
    {
        if (directiveText == null && uiCanvas != null)
        {
            directiveText = uiCanvas.GetComponentInChildren<TextMeshProUGUI>();
        }

        if (directiveText != null)
        {
            directiveText.text = message;
        }

        if (uiCanvas != null && !uiCanvas.activeSelf)
        {
            uiCanvas.SetActive(true);
        }
    }
}