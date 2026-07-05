using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))] // used as a template; not rendered
public class VertexTrailPainter : MonoBehaviour
{
    [Header("Line style")]
    public Material lineMaterial;
    public float lineWidth = 0.0025f;
    public bool useWorldSpace = true;

    [Header("Sampling")]
    public float minDistanceToAdd = 0.0025f; // metres
    public int   maxPoints        = 20000;

    [Header("Controls")]
    public KeyCode startKey = KeyCode.S;   // start new line
    public KeyCode endKey   = KeyCode.R;   // end current line (persist)
    public KeyCode clearKey = KeyCode.C;   // clear all lines

    [Header("Live Tail Fading (while painting)")]
    public bool  liveTail     = true;      // enable moving tail that fades as you draw
    [Min(0.01f)]
    public float tailSeconds  = 5.75f;     // duration of visible tail (seconds)
    [Range(0f, 1f)]
    public float tailStartAlpha = 0f;      // alpha at the tail start (usually 0)
    [Tooltip("Add extra mid-keys for a non-linear fade (optional). Leave empty for linear.")]
    public int tailAlphaMidKeys = 0;       // 0 = linear 0->1; >0 adds evenly spaced midpoints

    [Header("Fading after end (optional)")]
    public bool  fadeAfterEnd    = true;  // if true, finished strokes fade + get destroyed
    public float strokeLifetime  = 5.5f;   // seconds fully visible before fade starts
    public float fadeOutDuration = 5.0f;   // seconds to go from 1.0 -> 0.0 alpha

    private LimbMesh limb;
    private int vertexIndex = -1;

    // strokes
    private readonly List<LineRenderer> strokes = new();
    private LineRenderer currentLR;

    // We store time-stamped points for live tail trimming
    private struct TimedPoint { public Vector3 p; public float t; public TimedPoint(Vector3 p, float t){ this.p=p; this.t=t; } }
    private readonly List<TimedPoint> currentPoints = new();

    private bool isPainting = false;

    // template (the component on this GO acts as a template only)
    private LineRenderer template;

    public void Bind(LimbMesh limbMesh, int vtxIndex)
    {
        if (limb != null) limb.VerticesUpdated -= OnVerticesUpdated;

        limb = limbMesh;
        vertexIndex = Mathf.Clamp(vtxIndex, 0, limb.VertexCount - 1);
        limb.VerticesUpdated += OnVerticesUpdated;

        if (isPainting)
        {
            EndCurrentStroke();
            StartNewStroke();
        }
    }

    private void Awake()
    {
        template = GetComponent<LineRenderer>();
        template.useWorldSpace = useWorldSpace;
        template.positionCount = 0;

        // Ensure we have a visible, unlit material (Built-in & URP)
        if (lineMaterial == null)
        {
            var shader =
                Shader.Find("Sprites/Default") ??
                Shader.Find("Universal Render Pipeline/Unlit"); // URP fallback
            lineMaterial = new Material(shader);
        }
        template.material = lineMaterial;

        // Solid green, full alpha (we'll override alpha via gradient for live tail)
        template.colorGradient = Solid(Color.green);

    #if UNITY_6000_0_OR_NEWER
        template.widthMultiplier = Mathf.Max(0.01f, lineWidth);
    #else
        float w = Mathf.Max(0.01f, lineWidth);
        template.startWidth = template.endWidth = w;
    #endif

        template.enabled = false; // style holder only
    }

    private static Gradient Solid(Color c, float alpha = 1f)
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
            new[] { new GradientAlphaKey(alpha, 0f), new GradientAlphaKey(alpha, 1f) }
        );
        return g;
    }

    private static Gradient TailGradient(Color baseColor, float tailAlpha, int midKeys)
    {
        // Start transparent-ish at 0 (tail), end opaque at 1 (tip).
        int keyCount = Mathf.Clamp(midKeys, 0, 8) + 2; // 2 endpoints + optional mids
        var colorKeys = new GradientColorKey[keyCount];
        var alphaKeys = new GradientAlphaKey[keyCount];

        for (int i = 0; i < keyCount; i++)
        {
            float time = (keyCount == 1) ? 1f : (float)i / (keyCount - 1);
            colorKeys[i] = new GradientColorKey(baseColor, time);

            // linear alpha  (0 -> 1), with customizable start alpha
            float a = Mathf.Lerp(tailAlpha, 1f, time);
            alphaKeys[i] = new GradientAlphaKey(a, time);
        }

        var g = new Gradient();
        g.SetKeys(colorKeys, alphaKeys);
        return g;
    }

    private void Update()
    {
        if (Input.GetKeyDown(startKey)) StartNewStroke();
        if (Input.GetKeyDown(endKey))   EndCurrentStroke();
        if (Input.GetKeyDown(clearKey)) ClearAllStrokes();
    }

    private void OnDestroy()
    {
        if (limb != null) limb.VerticesUpdated -= OnVerticesUpdated;
    }

    // ----- Stroke lifecycle -----

    private void StartNewStroke()
    {
        if (limb == null || vertexIndex < 0) return;

        if (isPainting) EndCurrentStroke();

        var go = new GameObject("Stroke_" + strokes.Count);
        go.transform.SetParent(transform, false);

        var lr = go.AddComponent<LineRenderer>();
        CopyLineRendererSettings(template, lr);

        // For end-fade we can safely share the material; gradient is per-LR.
        // If your material uses a shader with premultiplied alpha or special states,
        // you can switch to a per-stroke instance by uncommenting the next line:
        // lr.material = new Material(template.material);

        // Set initial gradient for live tail (transparent at 0 -> opaque at 1)
        var baseCol = template.colorGradient.colorKeys.Length > 0
            ? template.colorGradient.colorKeys[0].color
            : Color.green;

        lr.colorGradient = liveTail
            ? TailGradient(baseCol, tailStartAlpha, tailAlphaMidKeys)
            : Solid(baseCol, 1f);

        lr.enabled = true;

        currentLR = lr;
        strokes.Add(lr);

        currentPoints.Clear();
        AddPointToCurrent(GetVertexWorld());

        isPainting = true;
    }

    private void EndCurrentStroke()
    {
        if (!isPainting) return;

        isPainting = false;

        if (fadeAfterEnd && currentLR != null)
        {
            // Freeze whatever points remain, then fade out the whole stroke.
            StartCoroutine(FadeAndDestroy(currentLR, strokeLifetime, fadeOutDuration));
        }

        currentLR = null;
        currentPoints.Clear();
    }

    private void ClearAllStrokes()
    {
        EndCurrentStroke();
        foreach (var lr in strokes)
            if (lr != null) Destroy(lr.gameObject);
        strokes.Clear();
    }

    // ----- Sampling -----

    private void OnVerticesUpdated()
    {
        if (!isPainting || limb == null || vertexIndex < 0 || currentLR == null) return;

        Vector3 p = GetVertexWorld();
        if (currentPoints.Count == 0 ||
            (p - currentPoints[currentPoints.Count - 1].p).sqrMagnitude >= minDistanceToAdd * minDistanceToAdd)
        {
            AddPointToCurrent(p);
        }

        if (liveTail)
        {
            TrimOldPoints(Time.time - tailSeconds);
            PushPointsToRenderer(currentLR, currentPoints);
        }
    }

    private void AddPointToCurrent(Vector3 p)
    {
        if (currentPoints.Count >= maxPoints) return;

        currentPoints.Add(new TimedPoint(p, Time.time));
        if (!liveTail && currentLR != null)
        {
            // No tail trimming: just append
            currentLR.positionCount = currentPoints.Count;
            currentLR.SetPosition(currentPoints.Count - 1, p);
        }
    }

    // Remove points older than cutoff time (keeps at least one point)
    private void TrimOldPoints(float cutoffTime)
    {
        if (currentPoints.Count <= 1) return;

        int firstKeep = 0;
        while (firstKeep < currentPoints.Count - 1 && currentPoints[firstKeep].t < cutoffTime)
            firstKeep++;

        if (firstKeep > 0)
            currentPoints.RemoveRange(0, firstKeep);
    }

    private static void PushPointsToRenderer(LineRenderer lr, List<TimedPoint> points)
    {
        if (lr == null) return;
        int n = points.Count;
        lr.positionCount = n;
        for (int i = 0; i < n; i++)
            lr.SetPosition(i, points[i].p);
    }

    // ----- Helpers -----

    private Vector3 GetVertexWorld()
    {
        Vector3 local = limb.GetLocalVertex(vertexIndex);
        return limb.GameObject.transform.TransformPoint(local);
    }

    private static void CopyLineRendererSettings(LineRenderer from, LineRenderer to)
    {
        to.useWorldSpace = from.useWorldSpace;
        to.material = from.material;
        to.colorGradient = from.colorGradient;   // will be overridden for live tail
    #if UNITY_6000_0_OR_NEWER
        to.widthMultiplier = from.widthMultiplier;
    #else
        to.startWidth = from.startWidth;
        to.endWidth = from.endWidth;
    #endif
        to.numCornerVertices = from.numCornerVertices;
        to.numCapVertices = from.numCapVertices;
        to.textureMode = from.textureMode;
        to.alignment = from.alignment;
        to.generateLightingData = from.generateLightingData;
    }

    // Per-stroke fade (whole stroke fades uniformly) after end
    private IEnumerator FadeAndDestroy(LineRenderer lr, float holdSeconds, float fadeSeconds)
    {
        if (lr == null) yield break;

        if (holdSeconds > 0f)
            yield return new WaitForSeconds(holdSeconds);

        float t = 0f;
        var baseCol = lr.colorGradient.colorKeys.Length > 0
            ? lr.colorGradient.colorKeys[0].color
            : Color.green;

        while (t < fadeSeconds && lr != null)
        {
            float a = 1f - Mathf.Clamp01(t / Mathf.Max(0.0001f, fadeSeconds));
            lr.colorGradient = Solid(baseCol, a);
            t += Time.deltaTime;
            yield return null;
        }

        if (lr != null)
        {
            lr.colorGradient = Solid(baseCol, 0f);
            Object.Destroy(lr.gameObject);
            strokes.Remove(lr);
        }
    }
}
