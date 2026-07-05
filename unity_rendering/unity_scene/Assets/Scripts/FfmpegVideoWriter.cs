using UnityEngine;
using UnityEngine.Rendering;
using System;
using System.Diagnostics;
using System.IO;

public class FfmpegVideoWriter : IDisposable
{
    private RenderTexture reusableRT;
    private Process ffmpegProcess;
    private Stream ffmpegStream;
    private int width, height;
    private string outputPath;
    private bool isRunning = false;

    public FfmpegVideoWriter(string outputFile, int w, int h, int fps = 60, int crf = 18)
    {
        width = w;
        height = h;
        outputPath = outputFile;

        // 🔥 Start ffmpeg process once
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-y -f rawvideo -pixel_format rgb24 -video_size {w}x{h} -framerate {fps} -i - -c:v libx264 -pix_fmt yuv420p -crf {crf} \"{outputFile}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        ffmpegProcess = Process.Start(psi);
        ffmpegStream = ffmpegProcess.StandardInput.BaseStream;
        isRunning = true;

        // optional: log ffmpeg stderr asynchronously
        ffmpegProcess.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) UnityEngine.Debug.Log("[ffmpeg] " + e.Data); };
        ffmpegProcess.BeginErrorReadLine();

        reusableRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
        reusableRT.Create();
    }

    public void PushFrame(Texture source)
    {
        if (!isRunning) return;

        // Copy texture into RT
        Graphics.Blit(source, reusableRT);

        // Async readback → sync write
        AsyncGPUReadback.Request(reusableRT, 0, TextureFormat.RGB24, req =>
        {
            if (req.hasError) { UnityEngine.Debug.LogError("GPU readback failed"); return; }

            var raw = req.GetData<byte>();
            ffmpegStream.Write(raw.ToArray(), 0, raw.Length);
        });
    }

    public void Dispose()
    {
        if (!isRunning) return;

        try
        {
            ffmpegStream.Flush();
            ffmpegStream.Close();
            ffmpegProcess.WaitForExit();
            UnityEngine.Debug.Log($"✅ Video written: {outputPath}");
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"Failed to close ffmpeg stream: {e.Message}");
        }

        reusableRT?.Release();
        isRunning = false;
    }
}
