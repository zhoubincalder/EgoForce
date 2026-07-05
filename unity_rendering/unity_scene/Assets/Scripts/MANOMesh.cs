// using System;
// using System.IO;
// using UnityEngine;
// using Unity.Collections;
// using UnityEngine.Rendering;
// using System.Collections.Generic;

// public enum HandType
// {
//     LEFT = 0,
//     RIGHT = 1,
// }

// public class MANOMesh : IDisposable
// {
//     private GameObject gameObject;
//     private Mesh mesh;
//     private Vector3[] verticesBuffer;  // ← preallocated

//     private int vertexCount;

//     private string meshTemplatePath;

//     private int zOrder = 1;

//     // ── Physics helpers
//     private struct JointCollider
//     {
//         public int vertexIndex;  // the vertex driving this collider
//         public SphereCollider collider;     // collider component (transform cached via collider)
//     }
//     private readonly List<JointCollider> jointColliders = new();

//     private const float PALM_RADIUS = 0.03f; // metres (≈3 cm)
//     private const float FINGER_RADIUS = 0.005f; // ≈8 mm

//     /// <summary>
//     /// Approximate mesh‑vertex indices corresponding to MANO joints.
//     /// Update these for your own MANO topology if necessary.
//     ///
//     ///  0  wrist           333 thumb CMC  444 thumb MCP  555 thumb IP   672 thumb tip
//     ///  211 index MCP     219 index PIP  236 index DIP  244 index tip
//     ///  425 middle MCP    433 middle PIP 450 middle DIP 459 middle tip
//     ///  554 ring MCP      562 ring PIP   579 ring tip   (pinkie omitted for brevity)
//     /// </summary>
//     private static readonly int[] JointVertexIndices =
//     {
//         0,
//         745, // thumb  (5)
//         317, // index  (4)
//         444, // middle (4)
//         556, // ring   (3)
//         673, // pinkie (2)
//     };

//     /// <summary>
//     /// Constructor: 
//     ///  - Loads "mano_left_template.json" or "mano_right_template.json" from StreamingAssets,
//     ///    which must contain only two fields:
//     ///      { 
//     ///        "vertices": [x0,y0,z0, x1,y1,z1, …], 
//     ///        "triangles": [i0,i1,i2, i3,i4,i5, …] 
//     ///      }
//     ///  - Builds a Unity Mesh with those initial vertices & triangles.
//     ///  - Allocates a NativeArray<Vector3> of size vertexCount, initialized from the JSON.
//     ///  - Uploads the initial vertices into the mesh via SetVertexBufferData.
//     ///  - Sets mesh.triangles once (connectivity never changes).
//     ///  - Marks the mesh dynamic so that high‐frequency updates are fast.
//     /// </summary>
//     /// 
//     public MANOMesh(GameObject gmObj, HandType handType)
//     {
//         gameObject = gmObj;
//         // Flip X axis (left‐handed→right‐handed coordinate correction)
//         gameObject.transform.localScale = new Vector3(1.0f, -1.0f, 1.0f);


//         var rootRb = gameObject.GetComponent<Rigidbody>();
//         if (rootRb == null) rootRb = gameObject.AddComponent<Rigidbody>();
//         rootRb.isKinematic            = true;
//         rootRb.useGravity             = false;
//         rootRb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;


//         // Choose the correct JSON template for left or right hand
//         if (handType == HandType.LEFT)
//             meshTemplatePath = Path.Combine(Application.streamingAssetsPath, "mano_left_template.json");
//         else
//             meshTemplatePath = Path.Combine(Application.streamingAssetsPath, "mano_right_template.json");

//         // Load JSON (must exist!)
//         string json = ReadJsonFile(meshTemplatePath);
//         if (string.IsNullOrEmpty(json))
//         {
//             throw new FileNotFoundException($"MANOMesh: Could not read JSON at {meshTemplatePath}");
//         }

//         // Parse only vertices and triangles
//         var meshData = JsonUtility.FromJson<MeshData>(json);
//         if (meshData.vertices == null || meshData.triangles == null)
//         {
//             Debug.LogError($"MANOMesh: JSON did not contain both \"vertices\" and \"triangles\" arrays in {meshTemplatePath}");
//             return;
//         }

//         // Compute how many vertices
//         vertexCount = meshData.vertices.Length / 3;
//         if (vertexCount * 3 != meshData.vertices.Length)
//         {
//             Debug.LogError("MANOMesh: JSON \"vertices\" length is not a multiple of 3.");
//             return;
//         }

//         mesh = new Mesh { name = $"MANO_{handType}" };

//         verticesBuffer = new Vector3[vertexCount];

//         // 2) Fill the NativeArray from the flat float[] loaded from JSON
//         for (int i = 0; i < vertexCount; i++)
//         {
//             int baseIdx = i * 3;
//             verticesBuffer[i] = new Vector3(
//                 meshData.vertices[baseIdx + 0],
//                 meshData.vertices[baseIdx + 1],
//                 zOrder * meshData.vertices[baseIdx + 2]
//             );
//         }

//         mesh.vertices = verticesBuffer;
//         // 6) Assign triangles once
//         mesh.triangles = meshData.triangles;

//         // 6) Recompute bounds (once) so that the mesh has a valid bounding box
//         mesh.RecalculateBounds();

//         mesh.RecalculateNormals();

//         // 7) Mark as dynamic so Unity knows we'll update vertices frequently
//         mesh.MarkDynamic();

//         var mf = gameObject.GetComponent<MeshFilter>() ?? gameObject.AddComponent<MeshFilter>();
//         var mr = gameObject.GetComponent<MeshRenderer>() ?? gameObject.AddComponent<MeshRenderer>();
//         mf.sharedMesh = mesh;

//         // // 7 ‑ Spawn per‑joint colliders (children of gmObj)
//         CreateJointColliders(gameObject);
//         gameObject.AddComponent<MANOColliderVisualizer>();   // adds gizmo drawing

//     }

//     /// <summary>
//     /// Reads the entire JSON file from disk (StreamingAssets). Returns null or empty if missing.
//     /// </summary>
//     private string ReadJsonFile(string path)
//     {
//         if (File.Exists(path))
//         {
//             return File.ReadAllText(path);
//         }
//         else
//         {
//             Debug.LogError($"MANOMesh: File not found: {path}");
//             return null;
//         }
//     }

//     /// <summary>
//     /// Call this each frame (on the main Unity thread) with the latest MeshData.vertices
//     /// array (flat float[] of length vertexCount*3). This method:
//     ///  1) Copies those floats into our existing NativeArray<Vector3>.
//     ///  2) Calls SetVertexBufferData(...) to push the updated vertices to the GPU.
//     ///  3) Recalculates bounds (so the mesh culling updates correctly).
//     /// If you also want updated normals per frame, you can call mesh.RecalculateNormals() here.
//     /// </summary>
//     public void UpdateVertices(MeshData newMeshData)
//     {
//         if (newMeshData == null || newMeshData.vertices == null)
//         {
//             Debug.LogError("MANOMesh.UpdateVertices: newMeshData or its vertices array is null.");
//             return;
//         }

//         // Sanity check: must have the same number of floats
//         if (newMeshData.vertices.Length != vertexCount * 3)
//         {
//             Debug.LogError($"MANOMesh.UpdateVertices: Expected {vertexCount * 3} floats, but got {newMeshData.vertices.Length}.");
//             return;
//         }

//         // 1) Copy flat float[] → NativeArray<Vector3>
//         for (int i = 0; i < vertexCount; i++)
//         {
//             int b = i * 3;
//             verticesBuffer[i] = new Vector3(
//                 newMeshData.vertices[b + 0],
//                 newMeshData.vertices[b + 1],
//                 zOrder * newMeshData.vertices[b + 2]
//             );
//         }

//         // 3) Assign mesh.vertices directly (this is fast enough if vertexCount is moderate)
//         mesh.vertices = verticesBuffer;

//         // 4) Recalculate bounds & normals each frame
//         mesh.RecalculateBounds();
//         mesh.RecalculateNormals();


//         foreach (var jc in jointColliders)
//             jc.collider.transform.localPosition = verticesBuffer[jc.vertexIndex];
//     }

//     /// <summary>
//     /// Release the NativeArray when this object is destroyed.
//     /// </summary>
//     public void Dispose()
//     {
//     }

//     private void CreateJointColliders(GameObject go)
//     {
//         var parent = go.transform;

//         foreach (int vIdx in JointVertexIndices)
//         {
//             if (vIdx < 0 || vIdx >= vertexCount)
//             {
//                 Debug.LogWarning($"Joint vertex index {vIdx} out of bounds (0..{vertexCount - 1})");
//                 continue;
//             }

//             // Create child GameObject for this joint
//             var node = new GameObject($"Joint_{vIdx}")
//             {
//                 layer = go.layer
//             };
//             node.transform.SetParent(parent, false);

//             // Add SphereCollider on the GameObject
//             var sc = node.AddComponent<SphereCollider>();
//             sc.radius = (vIdx == 0) ? PALM_RADIUS : FINGER_RADIUS;
//             sc.isTrigger = true;

//             // Add kinematic Rigidbody on the same GameObject
//             // var jrb = node.AddComponent<Rigidbody>();
//             // jrb.isKinematic = true;
//             // jrb.useGravity = false;
//             // jrb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

//             // Reporter to log collisions/triggers
//             node.AddComponent<JointCollisionReporter>();

//             jointColliders.Add(new JointCollider { vertexIndex = vIdx, collider = sc });
//         }

//         // Add HandPinchDetector to the root GameObject
//         var pinch = go.AddComponent<HandPinchDetector>();
//         pinch.Initialize(
//             go.transform.Find("Joint_745"),
//             go.transform.Find("Joint_317")
//         );
//         // Add HandGrabController to the root GameObject
//         var grab = go.AddComponent<HandGrabController>();
//         grab.attachPoint = pinch.thumbTip; // default attach point is thumb tip
//     }
// }


// [ExecuteAlways]
// public class MANOColliderVisualizer : MonoBehaviour
// {
//     void OnDrawGizmos()
//     {
//         Gizmos.color = Color.cyan;
//         foreach (var sc in GetComponentsInChildren<SphereCollider>())
//             Gizmos.DrawWireSphere(sc.transform.position, sc.radius * sc.transform.lossyScale.x);
//     }
// }

// public class JointCollisionReporter : MonoBehaviour
// {
//     private void OnCollisionEnter(Collision col)  => Report(col.collider);
//     private void OnTriggerEnter(Collider other)   => Report(other);

//     private void Report(Collider other)
//     {
//         Debug.Log($"[{name}] hit → {other.name}");

//         var rend = other.GetComponent<Renderer>();
//         if (rend == null) return;
//         StartCoroutine(Flash(rend));
//     }

//     System.Collections.IEnumerator Flash(Renderer rend)
//     {
//         var original = rend.material.color;
//         rend.material.color = Color.yellow;
//         yield return new WaitForSeconds(0.15f);
//         rend.material.color = original;
//     }
// }


// public class HandPinchDetector : MonoBehaviour
// {
//     [Tooltip("Assign in Inspector or via Initialize()")]
//     public Transform thumbTip;
//     [Tooltip("Assign in Inspector or via Initialize()")]
//     public Transform indexTip;

//     [Tooltip("How close before we call it a pinch (meters)")]
//     public float pinchThreshold = 0.1f;

//     public bool IsPinching { get; private set; }

//     /// <summary>
//     /// If you want to set these from code instead of the Inspector:
//     /// </summary>
//     public void Initialize(Transform thumbTip, Transform indexTip)
//     {
//         this.thumbTip  = thumbTip;
//         this.indexTip  = indexTip;
//     }

//     void Update()
//     {
//         if (thumbTip == null || indexTip == null)
//         {
//             Debug.LogWarning("HandPinchDetector: thumbTip or indexTip not assigned!");
//             return;
//         }

//         float d = Vector3.Distance(thumbTip.position, indexTip.position);
//         bool currentlyPinching = d < pinchThreshold;

//         // Only log when state changes, to avoid spamming every frame
//         if (currentlyPinching && !IsPinching)
//             Debug.Log($"Pinch detected! Distance: {d:F3} m");
//         else if (!currentlyPinching && IsPinching)
//             Debug.Log($"Pinch released. Distance: {d:F3} m");

//         IsPinching = currentlyPinching;
//     }
// }



// /// <summary>Grabs and drops grabbable objects tagged "Grabbable".</summary>
// [RequireComponent(typeof(HandPinchDetector))]
// public class HandGrabController : MonoBehaviour
// {
//     public string grabbableTag = "Grabbable";
//     private HandPinchDetector pinch;
//     private GameObject         candidate;  // now holds the GameObject under the trigger
//     private GameObject         held;
//     public Transform           attachPoint; // e.g. thumbTip

//     void Awake()
//     {
//         pinch = GetComponent<HandPinchDetector>();
//         if (attachPoint == null)
//             attachPoint = pinch.thumbTip;
//     }

//     void OnTriggerEnter(Collider other)
//     {
//         Debug.Log($"OnTriggerEnter: {other.name} (held={held?.name}, candidate={candidate?.name})");

//         if (other.CompareTag(grabbableTag) && held == null)
//             candidate = other.gameObject;  


//     }

//     void OnTriggerExit(Collider other)
//     {
//         Debug.Log($"OnTriggerExit: {other.name} (held={held?.name}, candidate={candidate?.name})");
        
//         if (other.gameObject == candidate)
//             candidate = null;
//     }

//     void Update()
//     {
//         // pinch-close + candidate → grab
//         if (held == null && candidate != null && pinch.IsPinching)
//             Grab(candidate);

//         // pinch-open + holding → drop
//         else if (held != null && !pinch.IsPinching)
//             Drop();

//         Debug.Log($"Update: candidate={candidate?.name}, held={held?.name}, pinch.IsPinching={pinch.IsPinching}");
//     }

//     void Grab(GameObject obj)
//     {
//         held = obj;
//         var rb = held.GetComponent<Rigidbody>();
//         if (rb) rb.isKinematic = true;
//         held.transform.SetParent(attachPoint, true);

//         Debug.Log($"Grab {held.name} at {attachPoint.name}");

//     }

//     void Drop()
//     {
//         var rb = held.GetComponent<Rigidbody>();
//         held.transform.SetParent(null, true);
//         if (rb) rb.isKinematic = false;
//         held = candidate = null;

//         Debug.Log($"Drop {held.name} from {attachPoint.name}");
//     }
// }
