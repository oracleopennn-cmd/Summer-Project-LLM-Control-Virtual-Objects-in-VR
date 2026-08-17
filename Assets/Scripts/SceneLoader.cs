using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    [Header("默认要加载的场景名称")]
    public string targetSceneName;

    /// <summary>
    /// 方法 1：直接加载 Inspector 中 targetSceneName 填写的场景
    /// </summary>
    public void LoadTargetScene()
    {
        if (!string.IsNullOrEmpty(targetSceneName))
        {
            SceneManager.LoadScene(targetSceneName);
        }
        else
        {
            Debug.LogError("<color=red>[SceneLoader]</color> 未指定目标场景名称！");
        }
    }

    /// <summary>
    /// 方法 2：可以在 On Click 事件里直接手动输入场景名称
    /// </summary>
    public void LoadSceneByName(string sceneName)
    {
        if (!string.IsNullOrEmpty(sceneName))
        {
            SceneManager.LoadScene(sceneName);
        }
        else
        {
            Debug.LogError("<color=red>[SceneLoader]</color> 传入的场景名称为空！");
        }
    }
}