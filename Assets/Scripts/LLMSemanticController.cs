using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// ==========================================
// 1. JSON 数据结构定义
// ==========================================

[Serializable]
public class BindingData
{
    public string source;   // 控制源，例如 "Can"
    public string target;   // 被控目标，例如 "Cube"
    public string action;   // 动作类型，例如 "Rotate", "Clear" 或 "None"
}

// 专用于文本请求的 Part
[Serializable]
public class GeminiTextPart
{
    public string text;
}

[Serializable]
public class GeminiInlineData
{
    public string mimeType;
    public string data;
}

// --- 文本请求 Payload 结构 ---
[Serializable]
public class GeminiTextContent
{
    public GeminiTextPart[] parts;
}

[Serializable]
public class GeminiTextRequest
{
    public GeminiTextContent[] contents;
    public GeminiGenerationConfig generationConfig;
}

[Serializable]
public class GeminiGenerationConfig
{
    public string response_mime_type = "application/json";
    public float temperature = 0.1f;
}

// --- API Response 响应结构 ---
[Serializable]
public class GeminiPartResponse
{
    public string text;
}

[Serializable]
public class GeminiContentResponse
{
    public GeminiPartResponse[] parts;
}

[Serializable]
public class GeminiCandidate
{
    public GeminiContentResponse content;
}

[Serializable]
public class GeminiResponse
{
    public GeminiCandidate[] candidates;
}

// 语音/多模态 Part 节点
[Serializable]
public class GeminiAudioPart
{
    public GeminiInlineData inlineData;
}

// ==========================================
// 2. 主控制逻辑脚本
// ==========================================

public class LLMSemanticController : MonoBehaviour
{
    [Header("Gemini API 配置")]
    [Tooltip("在 Google AI Studio 获取的 Gemini API Key")]
    public string geminiApiKey = "YOUR_GEMINI_API_KEY_HERE";

    [Tooltip("推荐使用 gemini-1.5-flash")]
    public string modelName = "gemini-1.5-flash";

    [Header("场景物体关联")]
    public GameObject canObject;
    public GameObject cubeObject;

    // 运行时状态变量
    public bool isBound = false;
    public GameObject currentSource;
    public GameObject currentTarget;

    // 相对姿态计算变量
    private Quaternion initialSourceRot;
    private Quaternion initialTargetRot;

    private void Update()
    {
        // 核心：当绑定建立且源/目标物体均有效时，每帧实时计算姿态差并同步
        if (isBound && currentSource != null && currentTarget != null)
        {
            // 1. 计算源物体（Can）相比于绑定时的旋转增量 (Delta Rotation)
            // Quaternion 乘法顺序：Delta = Current * Inverse(Initial)
            Quaternion sourceDeltaRot = currentSource.transform.rotation * Quaternion.Inverse(initialSourceRot);

            // 2. 将此旋转增量叠加到目标物体（Cube）的初始姿态上
            currentTarget.transform.rotation = sourceDeltaRot * initialTargetRot;
        }
    }

    /// <summary>
    /// 外部调用入口：文本指令
    /// </summary>
    public void SendUserPrompt(string userInput)
    {
        if (string.IsNullOrEmpty(geminiApiKey) || geminiApiKey == "YOUR_GEMINI_API_KEY_HERE")
        {
            Debug.LogError("[Gemini Controller] 请先在 Inspector 面板中填入有效的 Gemini API Key！");
            return;
        }

        StartCoroutine(PostToGemini(userInput));
    }

    /// <summary>
    /// 外部调用入口：语音 Base64 指令 (留待后续使用)
    /// </summary>
    public void SendAudioPrompt(string base64Audio, string mimeType = "audio/wav")
    {
        if (string.IsNullOrEmpty(geminiApiKey) || geminiApiKey == "YOUR_GEMINI_API_KEY_HERE")
        {
            Debug.LogError("[Gemini Controller] 请先在 Inspector 面板中填入有效的 Gemini API Key！");
            return;
        }

        StartCoroutine(PostAudioToGemini(base64Audio, mimeType));
    }

    // --- 文本请求 ---
    private IEnumerator PostToGemini(string userInput)
    {
        string promptText = "你是一个 VR 场景语义映射解析器。场景中有以下物体：\n" +
                            "- 'Can' (易拉罐)\n" +
                            "- 'Cube' (立方体)\n\n" +
                            "请分析用户的控制意图，提取控制源(source)、受控目标(target)和动作(action)。\n" +
                            "如果用户想建立旋转控制，返回：{\"source\": \"Can\", \"target\": \"Cube\", \"action\": \"Rotate\"}\n" +
                            "如果用户想取消控制，返回：{\"source\": \"\", \"target\": \"\", \"action\": \"Clear\"}\n\n" +
                            "用户指令：'" + userInput + "'";

        GeminiTextRequest requestData = new GeminiTextRequest
        {
            contents = new GeminiTextContent[]
            {
                new GeminiTextContent
                {
                    parts = new GeminiTextPart[]
                    {
                        new GeminiTextPart { text = promptText }
                    }
                }
            },
            generationConfig = new GeminiGenerationConfig()
        };

        string jsonPayload = JsonUtility.ToJson(requestData);

        string cleanModelName = modelName.Trim();
        if (!cleanModelName.StartsWith("models/"))
        {
            cleanModelName = "models/" + cleanModelName;
        }

        string cleanApiKey = geminiApiKey.Trim();
        string url = $"https://generativelanguage.googleapis.com/v1beta/{cleanModelName}:generateContent?key={cleanApiKey}";

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.certificateHandler = new BypassCertificate();

            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string rawJsonResponse = request.downloadHandler.text;
                Debug.Log($"[Gemini Controller] 收到 API 响应: {rawJsonResponse}");

                string jsonStringFromGemini = ExtractJsonFromGeminiResponse(rawJsonResponse);
                if (!string.IsNullOrEmpty(jsonStringFromGemini))
                {
                    ApplySemanticBinding(jsonStringFromGemini);
                }
            }
            else
            {
                Debug.LogError($"[Gemini Controller] 请求失败! 错误代码: {request.responseCode}\n详细返回: {request.downloadHandler.text}");
            }
        }
    }

    // --- 语音请求占位 ---
    private IEnumerator PostAudioToGemini(string base64Audio, string mimeType)
    {
        Debug.Log("[Gemini Controller] 正在发送语音音频至 Gemini...");

        string promptText = "你是一个 VR 场景语义映射解析器。场景中仅有以下物体：\n" +
                            "- 'Can' (易拉罐)\n" +
                            "- 'Cube' (立方体)\n\n" +
                            "规则：\n" +
                            "1. 只有当用户明确表达用 Can/易拉罐 旋转/控制 Cube/立方体 时，才返回：{\"source\": \"Can\", \"target\": \"Cube\", \"action\": \"Rotate\"}\n" +
                            "2. 当用户表达解绑/取消/停止时，返回：{\"source\": \"\", \"target\": \"\", \"action\": \"Clear\"}\n" +
                            "3. 如果用户的输入与上述控制意图无关（比如乱码、打招呼、问别的问题、或说的话不包含这两个物体），必须严格返回：{\"source\": \"\", \"target\": \"\", \"action\": \"None\"}";

        // 使用刚才新增的 GeminiAudioPart 数据结构，序列化生成标准 JSON
        string textPartJson = JsonUtility.ToJson(new GeminiTextPart { text = promptText });
        string audioPartJson = JsonUtility.ToJson(new GeminiAudioPart { inlineData = new GeminiInlineData { mimeType = mimeType, data = base64Audio } });

        string jsonPayload = $"{{\"contents\":[{{\"parts\":[{textPartJson},{audioPartJson}]}}],\"generationConfig\":{{\"response_mime_type\":\"application/json\",\"temperature\":0.1}}}}";

        string cleanModelName = modelName.Trim();
        if (!cleanModelName.StartsWith("models/"))
        {
            cleanModelName = "models/" + cleanModelName;
        }

        string cleanApiKey = geminiApiKey.Trim();
        string url = $"https://generativelanguage.googleapis.com/v1beta/{cleanModelName}:generateContent?key={cleanApiKey}";

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.certificateHandler = new BypassCertificate();

            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string rawJsonResponse = request.downloadHandler.text;
                Debug.Log($"[Gemini Controller] 收到语音 API 响应: {rawJsonResponse}");

                string jsonStringFromGemini = ExtractJsonFromGeminiResponse(rawJsonResponse);
                if (!string.IsNullOrEmpty(jsonStringFromGemini))
                {
                    ApplySemanticBinding(jsonStringFromGemini);
                }
            }
            else
            {
                Debug.LogError($"[Gemini Controller] 语音请求失败! 错误代码: {request.responseCode}\n详细返回: {request.downloadHandler.text}");
            }
        }
    }

    // --- JSON 解析逻辑 ---
    private string ExtractJsonFromGeminiResponse(string rawResponse)
    {
        try
        {
            GeminiResponse responseObj = JsonUtility.FromJson<GeminiResponse>(rawResponse);
            if (responseObj != null && responseObj.candidates != null && responseObj.candidates.Length > 0)
            {
                string extractedText = responseObj.candidates[0].content.parts[0].text;
                extractedText = extractedText.Replace("```json", "").Replace("```", "").Trim();
                Debug.Log($"[Gemini Controller] 成功解析出语义 JSON: {extractedText}");
                return extractedText;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[Gemini Controller] 解析 Gemini 返回格式失败: {e.Message}");
        }
        return null;
    }

    // --- 应用语义绑定 ---
    private void ApplySemanticBinding(string jsonContent)
    {
        try
        {
            BindingData data = JsonUtility.FromJson<BindingData>(jsonContent);

            if (data.action == "Rotate")
            {
                currentSource = (data.source.Equals("can", StringComparison.OrdinalIgnoreCase)) ? canObject : null;
                currentTarget = (data.target.Equals("cube", StringComparison.OrdinalIgnoreCase)) ? cubeObject : null;

                if (currentSource != null && currentTarget != null)
                {
                    // 绑定时刻记录两者的初始姿态，保证不会产生瞬移或跳变
                    initialSourceRot = currentSource.transform.rotation;
                    initialTargetRot = currentTarget.transform.rotation;
                    isBound = true;
                    Debug.Log($"<color=green>[成功绑定]</color> 通过 {currentSource.name} 旋转控制 {currentTarget.name}");
                }
            }
            else if (data.action == "Clear")
            {
                isBound = false;
                currentSource = null;
                currentTarget = null;
                Debug.Log("<color=yellow>[解除绑定]</color> 映射关联已清除");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[Gemini Controller] 反序列化 BindingData 失败: {e.Message}");
        }
    }
}

// ==========================================
// 3. 网络辅助类
// ==========================================

public class BypassCertificate : CertificateHandler
{
    protected override bool ValidateCertificate(byte[] certificateData)
    {
        return true;
    }
}