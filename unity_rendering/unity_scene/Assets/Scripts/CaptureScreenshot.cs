using System.IO;
using UnityEngine;
using System.Collections;

public class CaptureScreenshot
{
    private string folderPath;  // Folder to save screenshots
    private int imageWidth;
    private int imageHeight;

    private Camera camera;
    private RenderTexture renderTexture;
    private Texture2D texture;
    private int screenshotIndex = 1;

    public CaptureScreenshot(Camera cameraToUse, string ScreenshotFolderPath, int width, int height) {
        camera = cameraToUse;
        folderPath = ScreenshotFolderPath;
        imageWidth = width;
        imageHeight = height;

        renderTexture = new RenderTexture(imageWidth, imageHeight, 24);
        texture = new Texture2D(imageWidth, imageHeight, TextureFormat.RGB24, false);

        // Ensure the camera exists
        if (cameraToUse == null) {
            cameraToUse = Camera.main;
        }
        
        camera = Object.Instantiate(cameraToUse);

        // Create the directory if it doesn't exist
        if (!Directory.Exists(folderPath)) {
            Directory.CreateDirectory(folderPath);
        }

    }
    public void Capture(int frameIndex = -1) {
        // Create a RenderTexture
        camera.targetTexture = renderTexture;

        // Render the camera's view to the RenderTexture
        camera.Render();

        // Read the pixels from the RenderTexture to the Texture2D
        RenderTexture.active = renderTexture;
        texture.ReadPixels(new Rect(0, 0, imageWidth, imageHeight), 0, 0);
        texture.Apply();

        // Reset the target texture and active texture
        camera.targetTexture = null;
        RenderTexture.active = null;

        // Encode the texture to PNG (or JPG)
        byte[] bytes = texture.EncodeToPNG();

        string screenshotFileName = GenerateScreenshotFileName(frameIndex);
        string filePath = Path.Combine(folderPath, screenshotFileName);

        // Save the encoded PNG to a file
        File.WriteAllBytes(filePath, bytes);

        Debug.Log($"Screenshot saved to: {filePath}");
    }

    private string GenerateScreenshotFileName(int frameIndex) {
        if (frameIndex >= 0) {
            return $"{frameIndex:D5}.png";
        }
        else {
            string fileName = $"{screenshotIndex:D5}.png";
            screenshotIndex++;
            return fileName;
        }
    }

    private void OnDestroy() {
        Object.Destroy(camera);
        Object.Destroy(renderTexture);
        Object.Destroy(texture);
    }

    ~CaptureScreenshot() {
        OnDestroy();
    }
}
