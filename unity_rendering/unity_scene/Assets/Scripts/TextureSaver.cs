using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using System.IO;
using System.Collections.Concurrent;
using System.Threading;

public class TextureSaver
{
    private string rootFolder;
    private string imagesFolder;
    private int frameCounter = 0;

    private ConcurrentQueue<(byte[], int, int, int)> pendingSaves = new();
    private Thread workerThread;
    private bool running = true;
    private RenderTexture reusableRT;

    public string RootFolder => rootFolder;
    public string ImagesFolder => imagesFolder;
    
    public TextureSaver(string folderPath)
    {
        rootFolder = Path.IsPathRooted(folderPath)
            ? folderPath
            : Path.Combine(Application.persistentDataPath, folderPath);

        imagesFolder = Path.Combine(rootFolder, "images");
        if (Directory.Exists(imagesFolder))
        {
            try
            {
                Directory.Delete(imagesFolder, true);
                UnityEngine.Debug.Log($"🧹 Cleared old images in {imagesFolder}");
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"Failed to clear old frames: {e.Message}");
            }
        }

        Directory.CreateDirectory(imagesFolder);

        // 🚀 Start background writer thread
        workerThread = new Thread(BackgroundSaveLoop) { IsBackground = true };
        workerThread.Start();
    }
    public void Save(Texture source, int width, int height, int? fixedFrameIndex = null)
    {
        return;
        
        if (reusableRT == null)
        {
            reusableRT = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            reusableRT.Create();
        }

        Graphics.Blit(source, reusableRT);

        int frameIndex = fixedFrameIndex ?? frameCounter++;

        AsyncGPUReadback.Request(reusableRT, 0, TextureFormat.RGB24, req =>
        {
            if (req.hasError) { Debug.LogError("GPU readback failed"); return; }

            var raw = req.GetData<byte>().ToArray();
            pendingSaves.Enqueue((raw, width, height, frameIndex));
        });
    }

    private void BackgroundSaveLoop()
    {
        while (running)
        {
            while (pendingSaves.TryDequeue(out var item))
            {
                var (raw, w, h, index) = item;
                byte[] jpg = ImageConversion.EncodeArrayToJPG(raw, GraphicsFormat.R8G8B8_UNorm, (uint)w, (uint)h, quality: 90);
                string filename = $"{index:D5}.jpg";
                string path = Path.Combine(imagesFolder, filename);
                File.WriteAllBytes(path, jpg);
            }

            Thread.Sleep(1); // avoid CPU spin
        }
    }

    public void Dispose()
    {
        reusableRT?.Release();
        running = false;
        workerThread?.Join();
    }
}
