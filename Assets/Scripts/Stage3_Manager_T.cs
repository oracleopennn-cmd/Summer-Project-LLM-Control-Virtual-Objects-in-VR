using System.Collections;
using UnityEngine;
using TMPro;

public class Stage3_Manager_T : MonoBehaviour
{
    [Header("Model Group References")]
    public GameObject ghostContainer;
    public Transform solidBlockContainer;

    [Header("Bullet & Break Effect")]
    public GameObject bulletPrefab;
    public Transform bulletSpawnPoint;
    public float bulletSpeed = 60f;

    [Header("UI & Controller")]
    public TraditionalUIController controller;
    public TextMeshProUGUI directiveText;
    public GameObject uiCanvas;

    [Header("Timing Settings")]
    public float settleDuration = 3.0f;
    public float observeDuration = 2.5f;
    public float scatteredBlockScale = 0.1f;

    [Header("Trial & Tolerances")]
    public int totalTrials = 3;
    public float positionTolerance = 0.08f; // 💡 新增：位置误差范围
    public float rotationTolerance = 15.0f; // 💡 新增：旋转误差范围
    public float scaleTolerance = 0.15f;    // 💡 新增：缩放误差范围

    [Header("Runtime Status")]
    public int currentTrialIndex = 0;

    private FlexibleTargetSlot[] allSlots;
    private bool isRebuildPhaseActive = false;
    private float trialStartTime = 0f;

    private struct InitialPose
    {
        public Vector3 localPos;
        public Quaternion localRotation;
        public Vector3 localScale;
    }
    private InitialPose[] initialPoses;

    private void OnEnable()
    {
        totalTrials = ExperimentConfigManager.GlobalStage3TrialsT;

        if (controller == null)
        {
#if UNITY_2023_1_OR_NEWER
            controller = Object.FindFirstObjectByType<TraditionalUIController>();
#else
            controller = Object.FindObjectOfType<TraditionalUIController>();
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
                localRotation = child.localRotation,
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

        if (controller != null)
        {
            controller.ForceResetBinding();
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
            child.localRotation = initialPoses[i].localRotation;
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

        UpdateUI($"Stage 3: Rebuild Trial ({currentTrialIndex + 1}/{totalTrials})\nUse Traditional UI to match each shape to its hologram frame.");

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
            // 如果槽位有自定义容差接口，也可以在这里动态传递
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

        ghostContainer.transform.localScale = Vector3.one;

        Transform ghostParent = ghostContainer.transform;
        int count = Mathf.Min(ghostParent.childCount, solidBlockContainer.childCount);

        for (int i = 0; i < count; i++)
        {
            Transform ghostChild = ghostParent.GetChild(i);
            Transform solidChild = solidBlockContainer.GetChild(i);

            ghostChild.position = solidChild.position;
            ghostChild.rotation = solidChild.rotation;
            ghostChild.localScale = solidChild.localScale;

            ghostChild.gameObject.SetActive(true);

            // 💡 如果你的 FlexibleTargetSlot 支持直接接收容差，可以在这里设置
            // if (ghostChild.TryGetComponent<FlexibleTargetSlot>(out var slot)) {
            //     slot.positionTolerance = positionTolerance;
            //     slot.rotationTolerance = rotationTolerance;
            //     slot.scaleTolerance = scaleTolerance;
            // }

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
        for (int i = 0; i < solidBlockContainer.childCount; i++)
        {
            Transform child = solidBlockContainer.GetChild(i);
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