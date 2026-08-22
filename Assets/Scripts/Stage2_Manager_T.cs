using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public class Stage2_Manager_T : MonoBehaviour
{
    [Header("UI & References")]
    public GameObject uiCanvas;
    public TextMeshProUGUI hintText;
    public TraditionalUIController controller;
    public GameObject nextStageObject;

    [Header("Docking Objects")]
    public GameObject sourceObject;

    [Header("Target Objects Pool")]
    public GameObject[] targetObjectsPool;

    [Header("Trial & Tolerances")]
    public int totalTrials = 3;
    public float positionTolerance = 0.08f;
    public float rotationTolerance = 15.0f;
    public float scaleTolerance = 0.15f;

    [Header("Runtime Status & Records")]
    public int currentTrialIndex = 0;
    public GameObject currentTargetObject;
    public List<GameObject> usedTargetObjects = new List<GameObject>();

    private bool isTrialActive = false;
    private float trialStartTime = 0f;
    private float holdTimer = 0f;
    private const float REQUIRED_HOLD_TIME = 1.0f;

    private List<int> shuffledIndices = new List<int>();

    private struct InitialTransformState
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 localScale;
        public bool isKinematic;
    }

    private Dictionary<GameObject, InitialTransformState> initialSelectableStates = new Dictionary<GameObject, InitialTransformState>();

    private void Awake()
    {
#if UNITY_2023_1_OR_NEWER
        SelectableObject[] allSelectables = FindObjectsByType<SelectableObject>(FindObjectsSortMode.None);
#else
        SelectableObject[] allSelectables = FindObjectsOfType<SelectableObject>();
#endif
        foreach (var selectable in allSelectables)
        {
            if (selectable != null)
            {
                InitialTransformState state = new InitialTransformState
                {
                    position = selectable.transform.position,
                    rotation = selectable.transform.rotation,
                    localScale = selectable.transform.localScale,
                    isKinematic = selectable.TryGetComponent<Rigidbody>(out var rb) ? rb.isKinematic : true
                };
                initialSelectableStates[selectable.gameObject] = state;
            }
        }
    }

    private void OnEnable()
    {
        totalTrials = ExperimentConfigManager.GlobalStage2TrialsT;

        if (controller == null)
        {
#if UNITY_2023_1_OR_NEWER
            controller = FindFirstObjectByType<TraditionalUIController>();
#else
            controller = FindObjectOfType<TraditionalUIController>();
#endif
        }

        if (hintText == null && uiCanvas != null)
        {
            hintText = uiCanvas.GetComponentInChildren<TextMeshProUGUI>();
        }

        if (targetObjectsPool == null || targetObjectsPool.Length == 0) return;
        if (sourceObject == null) return;

        HideAllTargets();
        usedTargetObjects.Clear();

        InitShuffledPool();
        currentTrialIndex = 0;
        StartNextTrial();
    }

    private void Update()
    {
        if (!isTrialActive || currentTrialIndex >= totalTrials) return;
        CheckDockingCondition();
    }

    private void UpdateUI(string message)
    {
        if (hintText == null && uiCanvas != null)
        {
            hintText = uiCanvas.GetComponentInChildren<TextMeshProUGUI>();
        }

        if (hintText != null)
        {
            hintText.text = message;
        }

        if (uiCanvas != null && !uiCanvas.activeSelf)
        {
            uiCanvas.SetActive(true);
        }
    }

    private void HideAllTargets()
    {
        foreach (var obj in targetObjectsPool) if (obj != null) obj.SetActive(false);
    }

    private void InitShuffledPool()
    {
        shuffledIndices.Clear();
        for (int i = 0; i < targetObjectsPool.Length; i++) shuffledIndices.Add(i);
        for (int i = 0; i < shuffledIndices.Count; i++)
        {
            int randomIndex = Random.Range(i, shuffledIndices.Count);
            int temp = shuffledIndices[i];
            shuffledIndices[i] = shuffledIndices[randomIndex];
            shuffledIndices[randomIndex] = temp;
        }
    }

    private void StartNextTrial()
    {
        if (currentTrialIndex >= totalTrials || currentTrialIndex >= shuffledIndices.Count)
        {
            CompleteStage2();
            return;
        }

        ResetAllSelectableObjects();

        if (controller != null)
        {
            controller.ForceResetBinding();
        }

        HideAllTargets();

        int targetIndex = shuffledIndices[currentTrialIndex];
        currentTargetObject = targetObjectsPool[targetIndex];

        if (currentTargetObject != null)
        {
            currentTargetObject.SetActive(true);
            if (!usedTargetObjects.Contains(currentTargetObject)) usedTargetObjects.Add(currentTargetObject);
        }

        string sourceLabel = GetObjectLabel(sourceObject);
        string targetLabel = GetObjectLabel(currentTargetObject);

        UpdateUI($"Stage 2: Simple Docking ({currentTrialIndex + 1}/{totalTrials})\n\n" +
                 $"Use Traditional UI to bind <color=yellow>{sourceLabel}</color> and match it to the target.");

        trialStartTime = Time.time;
        holdTimer = 0f;
        isTrialActive = true;
    }

    private void ResetAllSelectableObjects()
    {
        foreach (var kvp in initialSelectableStates)
        {
            GameObject obj = kvp.Key;
            InitialTransformState state = kvp.Value;
            if (obj != null)
            {
                obj.transform.position = state.position;
                obj.transform.rotation = state.rotation;
                obj.transform.localScale = state.localScale;

                if (obj.TryGetComponent<Rigidbody>(out Rigidbody rb))
                {
#if UNITY_6000_0_OR_NEWER
                    rb.linearVelocity = Vector3.zero;
#else
                    rb.velocity = Vector3.zero;
#endif
                    rb.angularVelocity = Vector3.zero;
                    rb.isKinematic = state.isKinematic;
                }

                if (obj.TryGetComponent<SelectableObject>(out var selectable))
                {
                    selectable.isGrabbed = false;
                }
            }
        }
    }

    private void CheckDockingCondition()
    {
        if (sourceObject == null || currentTargetObject == null) return;

        float distance = Vector3.Distance(sourceObject.transform.position, currentTargetObject.transform.position);
        float angle = Quaternion.Angle(sourceObject.transform.rotation, currentTargetObject.transform.rotation);
        float scaleDiff = Vector3.Distance(sourceObject.transform.localScale, currentTargetObject.transform.localScale);

        if (distance <= positionTolerance && angle <= rotationTolerance && scaleDiff <= scaleTolerance)
        {
            holdTimer += Time.deltaTime;
            UpdateUI($"Stage 2: Simple Docking ({currentTrialIndex + 1}/{totalTrials})\n\n" +
                     $"<color=#00FF00>Holding alignment... ({holdTimer:F1}s / {REQUIRED_HOLD_TIME:F1}s)</color>");

            if (holdTimer >= REQUIRED_HOLD_TIME) OnTrialCompleted();
        }
        else
        {
            holdTimer = 0f;
        }
    }

    private void OnTrialCompleted()
    {
        isTrialActive = false;
        float duration = Time.time - trialStartTime;
        UpdateUI($"🎉 Trial {currentTrialIndex + 1} Completed!\nTime: {duration:F1}s");
        currentTrialIndex++;
        Invoke(nameof(StartNextTrial), 1.5f);
    }

    private void CompleteStage2()
    {
        isTrialActive = false;
        HideAllTargets();
        UpdateUI("🎉 Stage 2 Complete!\nProceeding to Stage 3...");
        Invoke(nameof(TransitionToStage3), 2.0f);
    }

    private void TransitionToStage3()
    {
        if (nextStageObject != null) nextStageObject.SetActive(true);
        this.enabled = false;
    }

    private string GetObjectLabel(GameObject obj)
    {
        if (obj == null) return "None";
        SelectableObject selectable = obj.GetComponent<SelectableObject>();
        if (selectable != null && !string.IsNullOrEmpty(selectable.objectLabel)) return selectable.objectLabel;
        return obj.name;
    }
}