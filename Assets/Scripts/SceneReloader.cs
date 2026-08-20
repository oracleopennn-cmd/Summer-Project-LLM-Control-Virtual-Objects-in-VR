using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneReloader : MonoBehaviour
{
    /// <summary>
    /// 提供给 Inspector / UnityEvent / Button OnClick 调用的公有方法
    /// </summary>
    public void ReloadCurrentScene()
    {
        // 💡 检查全局重载开关，如果已禁用则拦截重载请求
        if (!LLMSemanticController.isSceneReloadEnabled)
        {
            Debug.Log("<color=yellow>[SceneReloader]</color> Scene Reload is currently disabled by ExperimentConfig.");
            return;
        }

        Debug.Log("<color=yellow>[SceneReloader]</color> Reloading current active scene...");
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}