using NetMQ;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
// using MsgPack;
// using MsgPack.Serialization;
using System.Threading.Tasks;
using TMPro;


public class Client : MonoBehaviour {
    public GameObject leftHandGameObject;   // assign in Inspector
    public GameObject rightHandGameObject;  // assign in Inspector
    public TextMeshProUGUI indexDistanceText;

    public GameObject virtualCameraGO;      // assign in Inspector ("VirtualCamera")
    public int trackedVertexIndex = 333;   // choose any valid index
    public string screenFolder = "Captures/Screen";

    int rightThumbTipIndex = 672;  // example; set to your actual right-hand tip index

    private TextureSaver screenSaver;
    private int screenFrameIndex = 0;

    private LimbMesh leftLimb;
    private LimbMesh rightLimb;
    private ZmqReceiver receiver;
    private VertexTrailPainter painter;

    public GameObject screenQuad;           // assign in Inspector ("ScreenQuad")
    private Material screenMat;             // we'll grab ScreenQuad's material
    private Renderer screenRend;
    private MaterialPropertyBlock mpb;
    private int mainTexId;


    private void Start()
    {
        // 1) Create and start your ZMQ receiver
        var go = new GameObject("ZMQ Receiver");
        receiver = go.AddComponent<ZmqReceiver>();
        receiver.address = "tcp://localhost:5555";

        // 2) Instantiate your two Limb meshes (which read JSON templates once)
        leftLimb = new LimbMesh(leftHandGameObject, HandType.LEFT);
        rightLimb = new LimbMesh(rightHandGameObject, HandType.RIGHT);

        painter = new GameObject("VertexTrail").AddComponent<VertexTrailPainter>();
        painter.transform.SetParent(transform, false);
        // painter.lineMaterial = lineMaterial;   // optional
        painter.Bind(rightLimb, trackedVertexIndex);

        screenSaver = new TextureSaver(screenFolder);


        // 3) Grab the material from screenQuad so we can swap textures at runtime
        if (screenQuad != null)
        {
            screenRend = screenQuad.GetComponent<Renderer>();
            if (screenRend == null)
            {
                Debug.LogError("Client: screenQuad has no Renderer!");
                return;
            }

            screenMat = screenRend.sharedMaterial; // don’t instantiate a new material per access
            mpb = new MaterialPropertyBlock();

            // Pick the right texture slot for the active shader
            mainTexId = Shader.PropertyToID("_MainTex");
            if (screenMat != null)
            {
                if (screenMat.HasProperty("_BaseMap"))      mainTexId = Shader.PropertyToID("_BaseMap");      // URP
                else if (screenMat.HasProperty("_BaseColorMap")) mainTexId = Shader.PropertyToID("_BaseColorMap"); // HDRP
                else if (screenMat.HasProperty("_MainTex")) mainTexId = Shader.PropertyToID("_MainTex");
                else Debug.LogWarning($"Client: shader {screenMat.shader.name} has no known main texture property.");
            }
        }
        else
        {
            Debug.LogError("Client: screenQuad reference not set in Inspector!");
        }


        // 4) Ensure VirtualCamera has the Visualizer script attached
        if (virtualCameraGO == null)
        {
            Debug.LogError("Client: virtualCameraGO is not set!");
        }
        else
        {
            
        Transform quadT = screenQuad.transform;
        Transform camT  = virtualCameraGO.transform;

        // 2) Quad center & normal
        Vector3 quadCenter = quadT.position;
        Vector3 quadNormal = quadT.forward;

        // 3) Pick a distance in front of the quad for the “camera”
        float desiredDistance = 2.0f;

        // 4) Position & rotate the virtual camera
        camT.position = quadCenter - quadNormal * desiredDistance;
        camT.rotation = Quaternion.LookRotation(quadNormal, Vector3.up);

        // 5) Compute half-width / half-height from the quad’s scale
        float halfWidth  = quadT.localScale.x * 0.5f;
        float halfHeight = quadT.localScale.y * 0.5f;

        // 6) Assign into the Visualizer
        var vis = virtualCameraGO.GetComponent<VirtualCameraVisualizer>();
        if (vis != null)
        {
            vis.frustumDepth      = desiredDistance;
            vis.frustumHalfWidth  = halfWidth;
            vis.frustumHalfHeight = halfHeight;
        }
        else
        {
            Debug.LogError("No VirtualCameraVisualizer on virtualCameraGO!");
        }


        }
    }
    
    private void AddGrabRig(GameObject handGO, Transform attachPoint)
{
    // 1) Trigger collider for proximity (Sphere recommended)
    var sphere = handGO.GetComponent<SphereCollider>();
    if (sphere == null) sphere = handGO.AddComponent<SphereCollider>();
    sphere.isTrigger = true;
    sphere.radius = 0.06f; // ~6 cm bubble around hand root; tweak as needed

    // 2) KeyGrabController
    var grab = handGO.GetComponent<KeyGrabController>();
    if (grab == null) grab = handGO.AddComponent<KeyGrabController>();
    grab.attachPoint = attachPoint;
    grab.grabbableTag = "Grabbable";
    grab.maxGrabDistance = 0.25f;
}

    private void Update()
    {
        if (receiver == null) return;

        var data = receiver.GetData();

        if (data.LeftHand != null && data.RightHand != null)
        {
            leftLimb.UpdateVertices(data.LeftHand);
            rightLimb.UpdateVertices(data.RightHand);
            leftLimb.SetVisible(data.LeftHandVisible);
            rightLimb.SetVisible(data.RightHandVisible);

            if (data.LeftHandVisible && data.RightHandVisible)
            {
                Vector3 leftIndex = leftLimb.GetLocalVertex(317);
                Vector3 rightIndex = rightLimb.GetLocalVertex(317);

                float indexDistMm = Vector3.Distance(leftIndex, rightIndex) * 1000f; // in mm
                if (indexDistanceText != null)
                    indexDistanceText.text = $"Left to right index finger distance: {indexDistMm:F1} mm";
            }
            else if (indexDistanceText != null)
            {
                indexDistanceText.text = "Left to right index finger distance: n/a";
            }
        }
        
        if (data.LatestFrameTex != null && screenMat != null)
        {
            // Debug.Log($"Tex {data.LatestFrameTex.width}x{data.LatestFrameTex.height} fmt={data.LatestFrameTex.graphicsFormat}");

            screenRend.GetPropertyBlock(mpb);
            mpb.SetTexture(mainTexId, data.LatestFrameTex);
            screenRend.SetPropertyBlock(mpb);

            int texWidth = data.LatestFrameTex.width;
            int texHeight = data.LatestFrameTex.height;
            screenSaver.Save(data.LatestFrameTex, texWidth, texHeight, screenFrameIndex++);
        }
    }

    void OnDestroy()
    {
        if (receiver != null)
        {
            // receiver.Cleanup(); 
        }
        leftLimb?.Dispose();
        rightLimb?.Dispose();
    }

}

