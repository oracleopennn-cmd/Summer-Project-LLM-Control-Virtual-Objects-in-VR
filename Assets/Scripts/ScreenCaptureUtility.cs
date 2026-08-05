using UnityEngine;

public static class ScreenCaptureUtility
{
    public static byte[] CaptureCameraView(Camera cam, int width = 512, int height = 512)
    {
        RenderTexture rt = new RenderTexture(width, height, 24);
        RenderTexture previous = cam.targetTexture;

        cam.targetTexture = rt;
        Texture2D screenShot = new Texture2D(width, height, TextureFormat.RGB24, false);
        cam.Render();

        RenderTexture.active = rt;
        screenShot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        screenShot.Apply();

        cam.targetTexture = previous;
        RenderTexture.active = null;
        Object.Destroy(rt);

        byte[] bytes = screenShot.EncodeToJPG(75); // 压缩为 JPG 减少传输大小
        Object.Destroy(screenShot);

        return bytes;
    }
}