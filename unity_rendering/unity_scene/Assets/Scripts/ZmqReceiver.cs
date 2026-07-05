using UnityEngine;
using System;
using System.IO;
using System.Threading;
using System.Collections.Concurrent;
using NetMQ;
using NetMQ.Sockets;
using MessagePack;
using System.Collections.Generic;
using System.Collections;

public class ZmqReceiver : MonoBehaviour
{
    [Header("ZMQ Settings")]
    public string address = "tcp://localhost:5555";

    // Queues to hand off data to the main thread
    private ConcurrentQueue<(MeshBufferData, MeshBufferData)> _handMeshBuffers = new ConcurrentQueue<(MeshBufferData, MeshBufferData)>();
    private ConcurrentQueue<ImageData> _imageQueue = new ConcurrentQueue<ImageData>();
    private Texture2D _reusableTex;

    // Publicly visible “latest” so you can hook them up in Update()

    private Thread _worker;
    private bool _running;


    void Start()
    {
        _running = true;
        _reusableTex = null;  // we’ll allocate on first image
        _worker = new Thread(ReceiveLoop) { IsBackground = true };
        _worker.Start();
    }


    void OnDestroy()
    {
        _running = false;
        _worker?.Join();
        NetMQConfig.Cleanup();
    }

    void ReceiveLoop()
    {
        AsyncIO.ForceDotNet.Force();
        using (var pull = new PullSocket())
        {
            pull.Connect(address);

            MeshBufferData leftBuffer = null;
            MeshBufferData rightBuffer = null;

            while (_running)
            {
                if (!pull.TryReceiveFrameString(TimeSpan.FromMilliseconds(100), out string jsonMeta))
                {
                    // Timed out, loop again to check _running
                    continue;
                }
                byte[] payload = pull.ReceiveFrameBytes();
                var meta = JsonUtility.FromJson<MetaHeader>(jsonMeta);
                
                if (meta.type == "mesh_buffer")
                {
                    // Parse raw float32 bytes into a MeshBufferData struct
                    int N = meta.numVerts; // number of (x,y,z) tuples

                    int expectedBytes = N * 3 * sizeof(float);
                    if (payload.Length != expectedBytes)
                    {
                        Debug.LogError($"[ZmqReceiver] Payload length {payload.Length} ≠ expected {expectedBytes} for numVerts={N}");
                    }

                    float[] verts = new float[N * 3];
                    Buffer.BlockCopy(payload, 0, verts, 0, payload.Length);

                    var mb = new MeshBufferData()
                    {
                        frameIndex = meta.frameIndex,
                        numVerts = N,
                        rawVerts = verts,
                        visible = meta.visible
                    };

                    if (meta.tag == "leftLimb")
                        leftBuffer = mb;
                    else if (meta.tag == "rightLimb")
                        rightBuffer = mb;

                    // Once we’ve seen both left & right for this frameIndex, enqueue them as a pair
                    if (leftBuffer != null && rightBuffer != null
                        && leftBuffer.frameIndex == rightBuffer.frameIndex)
                    {
                        _handMeshBuffers.Enqueue((leftBuffer, rightBuffer));
                        leftBuffer = null;
                        rightBuffer = null;
                    }
                }
                else if (meta.type == "image")
                {
                    // Debug.Log($"[ZmqReceiver] Received image frameIndex={meta.frameIndex}, shape=[{string.Join(",", meta.shape)}], payload bytes={payload.Length}");    

                    // Queue the JPEG bytes + shape + frameIndex
                    _imageQueue.Enqueue(new ImageData
                    {
                        JpegBytes = payload,
                        Width = meta.shape[1],
                        Height = meta.shape[0],
                        frameIndex = meta.frameIndex
                    });
                }
            }

            pull.Close();
        }
    }

    public Data GetData()
    {
        var data = new Data();

        // 1) Try to dequeue one (left, right) mesh pair
        if (_handMeshBuffers.TryDequeue(out var tuple))
        {
            var (leftMb, rightMb) = tuple;

            // Create MeshData instances containing only the rawVertices float[].
            // (Triangles, normals, uv remain null because we're only streaming vertices.)
            data.LeftHand = new MeshData
            {
                vertices  = leftMb.rawVerts,
                triangles = null,
                normals   = null,
                uv        = null
            };
            data.LeftHandVisible = leftMb.visible;

            data.RightHand = new MeshData
            {
                vertices  = rightMb.rawVerts,
                triangles = null,
                normals   = null,
                uv        = null
            };
            data.RightHandVisible = rightMb.visible;

            // We can choose to carry the frameIndex from either side; they match
            data.frameIndex = leftMb.frameIndex;
        }

        // 2) Try to dequeue one image
        if (_imageQueue.TryDequeue(out var im))
        {
            // Reuse or (on first use) allocate the Texture2D
            if (_reusableTex == null
                || _reusableTex.width  != im.Width
                || _reusableTex.height != im.Height)
            {
                _reusableTex = new Texture2D(im.Width, im.Height, TextureFormat.RGB24, false);
            }
            _reusableTex.LoadImage(im.JpegBytes);

            data.LatestFrameTex = _reusableTex;
            data.frameIndex    = im.frameIndex;

            // Debug.Log($"[ZmqReceiver] Decoded image frameIndex={im.frameIndex}, size=({_reusableTex.width}x{_reusableTex.height})");
        }

        return data;
    }

    class MeshBufferData
    {
        public int     frameIndex;
        public int     numVerts;
        public float[] rawVerts;  // length = 3*numVerts
        public bool    visible;
    }

    class ImageData
    {
        public byte[] JpegBytes;
        public int Width, Height;
        public int frameIndex;
    }

    [Serializable]
    public class MetaHeader
    {
        public string type;      // "mesh_buffer" or "image"
        public string tag;       // for mesh_buffer: "leftHand" or "rightHand"
        public int    numVerts;  // for mesh_buffer
        public int    frameIndex;
        public bool   visible;
        public int[]  shape;     // for image: [height, width, channels]
    }
    
}

