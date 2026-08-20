using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class SceneLoader : MonoBehaviour
{
#if UNITY_EDITOR
    [Header("直接拖入场景资源 (.unity)")]
    [Tooltip("可直接从 Project 窗口拖入任意路径下的 Scene 文件")]
    public SceneAsset sceneAsset;
#endif

    [Header("运行时读取的场景路径 (自动生成，解决同名冲突)")]
    [Tooltip("拖入 SceneAsset 后会自动同步完整路径，精准区分同名场景")]
    public string targetScenePath;

#if UNITY_EDITOR
    /// <summary>
    /// 当在 Inspector 中拖入场景资源时，自动提取其精准资源路径
    /// </summary>
    private void OnValidate()
    {
        if (sceneAsset != null)
        {
            // 获取如 "Assets/Scenes/LLM+Voice Control/Tutorial.unity" 的完整唯一路径
            targetScenePath = AssetDatabase.GetAssetPath(sceneAsset);
        }
    }
#endif

    /// <summary>
    /// 方法 1：加载 Inspector 中拖入的场景（按唯一路径加载，不怕同名）
    /// </summary>
    public void LoadTargetScene()
    {
        if (!string.IsNullOrEmpty(targetScenePath))
        {
            SceneManager.LoadScene(targetScenePath);
        }
        else
        {
            Debug.LogError("<color=red>[SceneLoader]</color> 未指定目标场景！请在 Inspector 拖入 SceneAsset。", this);
        }
    }

    /// <summary>
    /// 方法 2：手动按场景名或路径加载
    /// </summary>
    public void LoadSceneByName(string sceneNameOrPath)
    {
        if (!string.IsNullOrEmpty(sceneNameOrPath))
        {
            SceneManager.LoadScene(sceneNameOrPath);
        }
        else
        {
            Debug.LogError("<color=red>[SceneLoader]</color> 传入的场景名称/路径为空！", this);
        }
    }
}