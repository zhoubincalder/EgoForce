using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using System.IO;
using System.Diagnostics;

public class DatasetScreenshotter
{
    private Camera cam;
    private RenderTexture rt;
    private int width, height;
    private string rootFolder;    // e.g., "Captures/Left"
    private string imagesFolder;  // e.g., "Captures/Left/images"
    private int frameCounter = 0;
    private bool transparent;

    public string RootFolder => rootFolder;
    public string ImagesFolder => imagesFolder;

    public DatasetScreenshotter(Camera sourceCam, string folderPath, int w, int h, bool transparentBG)
    {
        width = w;
        height = h;
        transparent = transparentBG;

        rootFolder = folderPath;

        if (!Directory.Exists(rootFolder))
            Directory.CreateDirectory(rootFolder);

        imagesFolder = Path.Combine(rootFolder, "images");
        if (Directory.Exists(imagesFolder))
        {
            try
            {
                Directory.Delete(imagesFolder, true);
                UnityEngine.Debug.Log($"🧹 Cleared old frames at {imagesFolder}");
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"Failed to clear old frames: {e.Message}");
            }
        }
        Directory.CreateDirectory(imagesFolder);

        cam = sourceCam != null ? sourceCam : Camera.main;
        cam.forceIntoRenderTexture = true;

        if (!transparent)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black; // Color.white
        }

        rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 1
        };
        rt.Create();
        cam.targetTexture = rt;
    }

    public void Capture(int? fixedFrameIndex = null)
    {
        // Render only if camera isn't already rendering to this RT
        cam.Render();
        CaptureRendered(fixedFrameIndex);
    }

    public void CaptureRendered(int? fixedFrameIndex = null) {
        // return;
        
        bool useAlpha = transparent;
        var readbackFormat = useAlpha ? TextureFormat.RGBA32 : TextureFormat.RGB24;


        AsyncGPUReadback.Request(rt, 0, readbackFormat, req => {
            if (req.hasError) { UnityEngine.Debug.LogError("GPU readback failed"); return; }

            var rawData = req.GetData<byte>().ToArray(); // convert to managed
            byte[] bytes;
            string ext;
            if (useAlpha)
                {
                bytes = ImageConversion.EncodeArrayToPNG(
                    rawData,
                    GraphicsFormat.R8G8B8A8_UNorm,
                    (uint)width, (uint)height);
                ext = ".png";
            }
            else
            {
                bytes = ImageConversion.EncodeArrayToJPG(
                    rawData,
                    GraphicsFormat.R8G8B8_UNorm,
                    (uint)width, (uint)height,
                    quality: 90);
                ext = ".jpg";
            }

            int idx = fixedFrameIndex ?? frameCounter++;
            string filename = $"{idx:D5}{ext}";
            File.WriteAllBytes(Path.Combine(imagesFolder, filename), bytes);
                
        // UnityEngine.Debug.Log($"Writing {Path.Combine(imagesFolder, filename)} ({pngBytes.Length} bytes)"); 
        });
    }

    public void Dispose()
    {
        if (rt != null)
        {
            rt.Release();
            rt = null;
        }
        if (cam != null) cam.targetTexture = null;
    }
}



public class CameraRecorder : MonoBehaviour
{
    public Camera camA;
    public Camera camB;

    public string folderA = "Captures/Left";
    public string folderB = "Captures/Right";
    public int width = 1408;
    public int height = 1408;

    public bool transparentBG = false;
    public bool captureEveryFrame = true;
    public int captureInterval = 1;
    public KeyCode singleShotKey = KeyCode.F12;


    [Header("Video Encoding")]
    public int fps = 15;                  // video frame rate
    public string ffmpegPath = "ffmpeg";  // path or just "ffmpeg" if in PATH
    public bool encodeOnStop = true;      // encode when exiting Play
    [Range(0, 51)] public int crf = 18;   // quality: 0=lossless (big) … 23=default … 51=worst


    private DatasetScreenshotter shotA;
    private DatasetScreenshotter shotB;
    private int sharedFrameIndex = 0;
    private bool requestThisFrame = false;


    void OnEnable()
    {
        if (camA == null || camB == null)
        {
            UnityEngine.Debug.LogError("Assign camA and camB in the inspector.");
            enabled = false; return;
        }

        // Cameras must be enabled so the SRP renders them
        camA.enabled = true;
        camB.enabled = true;

        shotA = new DatasetScreenshotter(camA, folderA, width, height, transparentBG);
        shotB = new DatasetScreenshotter(camB, folderB, width, height, transparentBG);

        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;

        UnityEngine.Debug.Log($"Recording to:\n  A: {shotA.ImagesFolder}\n  B: {shotB.ImagesFolder}");
    }

    void OnDisable()
    {
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        shotA?.Dispose();
        shotB?.Dispose();

        if (encodeOnStop)
        {
            string shotScreen = Path.Combine(Path.GetDirectoryName(shotA?.RootFolder), "Screen");
            string shotScreenImages = Path.Combine(shotScreen, "images");

            string firstMp4 = TryEncode(shotScreen, shotScreenImages);
            string secondMp4 = TryEncode(shotA?.RootFolder, shotA?.ImagesFolder);
            string thirdMp4 = TryEncode(shotB?.RootFolder, shotB?.ImagesFolder);

            if (!string.IsNullOrEmpty(firstMp4) && !string.IsNullOrEmpty(secondMp4) && !string.IsNullOrEmpty(thirdMp4))
                CombineSideBySide(firstMp4, secondMp4, thirdMp4);
            else
                UnityEngine.Debug.LogWarning("Skipping side-by-side combination — one or both MP4s are missing.");
        }

    }

    void LateUpdate()
    {
        bool shouldCapture = captureEveryFrame && (Time.frameCount % captureInterval == 0);
        if (shouldCapture || Input.GetKeyDown(singleShotKey))
        {
            // Flag once; both cameras will hit the callback this frame
            requestThisFrame = true;
        }

        // UnityEngine.Debug.Log($"LateUpdate: requestThisFrame={requestThisFrame}");  
    }

    void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        // UnityEngine.Debug.Log($"OnEndCameraRendering: {cam.name}, requestThisFrame={requestThisFrame}"); 

        if (!requestThisFrame) return;

        if (cam == camA) shotA.CaptureRendered(sharedFrameIndex);
        else if (cam == camB) shotB.CaptureRendered(sharedFrameIndex);

        // After both have been serviced, clear and advance index.
        // We can’t easily know ordering, so when camB is serviced we advance.
        // If order is unknown, you can count hits per frame instead.
        if (cam == camB)
        {
            sharedFrameIndex++;
            requestThisFrame = false;
        }
    }

    string TryEncode(string root, string images)
    {
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(images)) return null;
        if (!Directory.Exists(images))
        {
            UnityEngine.Debug.LogWarning($"No images folder found: {images}");
            return null;
        }

        // Auto-derive basename from folder name (e.g., "Left", "FrontCam", etc.)
        string basename = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        // If there are <2 frames, skip
        var pngs = Directory.GetFiles(images, "*.png");
        var jpgs = Directory.GetFiles(images, "*.jpg");
        int totalFrames = pngs.Length + jpgs.Length;

        if (totalFrames < 2)
        {
            UnityEngine.Debug.LogWarning($"Not enough frames in {images} to encode a video.");
            return null;
        }

        string outputMp4 = Path.Combine(root, $"{basename}.mp4");

        // Build ffmpeg args:
        string jpgPattern = Path.Combine(images, "%05d.jpg");
        string pngPattern = Path.Combine(images, "%05d.png");
        string pattern = File.Exists(Path.Combine(images, "00000.jpg")) ? jpgPattern : pngPattern;
        pattern = pattern.Replace("\\", "/");
        outputMp4 = outputMp4.Replace("\\", "/");

        string args = $"-y -framerate {fps} -i \"{pattern}\" " +
                    $"-c:v libx264 -pix_fmt yuv420p -crf {crf} -r {fps} \"{outputMp4}\"";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using (var p = Process.Start(psi))
            {
                string stderr = p.StandardError.ReadToEnd();
                string stdout = p.StandardOutput.ReadToEnd();
                p.WaitForExit();

                UnityEngine.Debug.Log($"ffmpeg exit code: {p.ExitCode}\n{stderr}");
                if (p.ExitCode == 0)
                {
                    UnityEngine.Debug.Log($"✅ Wrote video: {outputMp4}");
                    return outputMp4;
                }
                else
                {
                    UnityEngine.Debug.LogError($"ffmpeg failed for {basename}.mp4\nArgs: {args}\n{stderr}");
                    return null;
                }
            }
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"Failed to run ffmpeg. Is it installed / in PATH?\n{e.Message}");
            return null;
        }
    }
    void CombineSideBySide(string leftPath, string middlePath, string rightPath)
    {
        string parentDir = Path.GetDirectoryName(Path.GetDirectoryName(leftPath));
        if (string.IsNullOrEmpty(parentDir)) parentDir = Application.persistentDataPath;

        string combinedOutput = Path.Combine(parentDir, "combined.mp4").Replace("\\", "/");

        leftPath   = leftPath.Replace("\\", "/");
        middlePath = middlePath.Replace("\\", "/");
        rightPath  = rightPath.Replace("\\", "/");

        // 🧠 3-input horizontal stack
        string args = $"-y -i \"{leftPath}\" -i \"{middlePath}\" -i \"{rightPath}\" " +
                    "-filter_complex \"[0:v][1:v][2:v]hstack=inputs=3[v]\" -map \"[v]\" " +
                    $"-c:v libx264 -pix_fmt yuv420p -crf {crf} -r {fps} \"{combinedOutput}\"";

        UnityEngine.Debug.Log($"Running ffmpeg with args: {args}");
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using (var p = System.Diagnostics.Process.Start(psi))
            {
                string stderr = p.StandardError.ReadToEnd();
                string stdout = p.StandardOutput.ReadToEnd();
                p.WaitForExit();

                if (p.ExitCode == 0)
                    UnityEngine.Debug.Log($"✅ Combined 3-video file created: {combinedOutput}");
                else
                    UnityEngine.Debug.LogError($"❌ Failed to combine videos.\nArgs: {args}\n{stderr}");
            }
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"Failed to run ffmpeg for 3-way combine.\n{e.Message}");
        }
    }
}
