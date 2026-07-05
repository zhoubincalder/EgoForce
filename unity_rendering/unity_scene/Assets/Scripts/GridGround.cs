using UnityEngine;

/// <summary>
/// Generates a soft gray grid texture for a Unity Plane (default 10×10 units).
/// - Minor grid lines every <minorCellSizeUnits> world units
/// - Darker/thicker major lines every <majorEvery> minor cells
/// - Subtle vignette / corner fade for depth
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class GridGround : MonoBehaviour
{
    [Header("Resolution / Scale")]
    [Tooltip("Pixels per world unit. Higher = crisper lines (and larger texture).")]
    public int pixelsPerUnit = 64;

    [Header("Grid Layout (in world units)")]
    [Tooltip("Size of one minor grid cell in world units.")]
    public float minorCellSizeUnits = 0.5f;
    [Tooltip("Every N minor cells draw a darker/thicker major line.")]
    public int majorEvery = 4;

    [Header("Line Thickness (in pixels)")]
    public int minorThicknessPx = 1;
    public int majorThicknessPx = 2;

    [Header("Colors")]
    public Color background = new Color(0.90f, 0.90f, 0.88f, 1f); // warm light gray
    public Color minorLine  = new Color(0.74f, 0.74f, 0.72f, 1f); // thin gray
    public Color majorLine  = new Color(0.55f, 0.55f, 0.55f, 1f); // darker

    [Header("Subtle Shading")]
    [Tooltip("Amount of vignette/corner fade (0 = none, 1 = strong).")]
    [Range(0f, 1f)] public float vignetteStrength = 0.15f;
    [Tooltip("Diagonal corner fade towards the top-right (0 = off).")]
    [Range(0f, 1f)] public float diagonalFadeStrength = 0.10f;

    private Texture2D _texture;
    private Material  _material;

    void Start()
    {
        // World size of the Plane (Unity Plane is 10×10 by default).
        Vector3 s = transform.localScale;
        float worldW = Mathf.Abs(s.x) * 10f;
        float worldH = Mathf.Abs(s.z) * 10f;

        // Texture resolution.
        int texW = Mathf.Max(4, Mathf.RoundToInt(worldW * pixelsPerUnit));
        int texH = Mathf.Max(4, Mathf.RoundToInt(worldH * pixelsPerUnit));

        // Derived pixel steps.
        int minorStepPx = Mathf.Max(1, Mathf.RoundToInt(minorCellSizeUnits * pixelsPerUnit));
        int majorStepPx = Mathf.Max(minorStepPx, minorStepPx * Mathf.Max(1, majorEvery));

        // Create texture.
        _texture = new Texture2D(texW, texH, TextureFormat.RGBA32, false, false);
        _texture.wrapMode = TextureWrapMode.Clamp;
        _texture.filterMode = FilterMode.Point;

        var pixels = new Color[texW * texH];

        // Precompute for speed.
        int halfMinor = Mathf.Max(0, (minorThicknessPx - 1) / 2);
        int halfMajor = Mathf.Max(0, (majorThicknessPx - 1) / 2);

        // Draw pixels.
        for (int y = 0; y < texH; y++)
        {
            // Distances to nearest minor/major horizontal grid lines
            int yModMinor = Mod(y, minorStepPx);
            int distMinorY = Mathf.Min(yModMinor, minorStepPx - yModMinor);
            int yModMajor = Mod(y, majorStepPx);
            int distMajorY = Mathf.Min(yModMajor, majorStepPx - yModMajor);

            for (int x = 0; x < texW; x++)
            {
                // Base color
                Color c = background;

                // Distances to vertical lines
                int xModMinor = Mod(x, minorStepPx);
                int distMinorX = Mathf.Min(xModMinor, minorStepPx - xModMinor);
                int xModMajor  = Mod(x, majorStepPx);
                int distMajorX = Mathf.Min(xModMajor, majorStepPx - xModMajor);

                bool onMajorX = distMajorX <= halfMajor;
                bool onMajorY = distMajorY <= halfMajor;
                bool onMinorX = distMinorX <= halfMinor;
                bool onMinorY = distMinorY <= halfMinor;

                // Decide which line (if any) covers this pixel.
                // Major lines take precedence.
                if (onMajorX || onMajorY)
                {
                    c = majorLine;
                }
                else if (onMinorX || onMinorY)
                {
                    c = minorLine;
                }

                // Subtle vignette from edges
                if (vignetteStrength > 0f)
                {
                    float nx = (x + 0.5f) / texW;  // [0,1]
                    float ny = (y + 0.5f) / texH;
                    float edge = Mathf.Min(Mathf.Min(nx, 1f - nx), Mathf.Min(ny, 1f - ny));
                    float v = Mathf.Lerp(1f - 0.8f * vignetteStrength, 1f, Mathf.Clamp01(edge * 3f));
                    c = Color.Lerp(background, c, v);
                }

                // Very light diagonal fade toward top-right corner
                if (diagonalFadeStrength > 0f)
                {
                    float d = ((x + y) / (float)(texW + texH)); // 0 (bottom-left) → 1 (top-right)
                    float t = Mathf.Lerp(1f, 1f - 0.25f * diagonalFadeStrength, d);
                    c *= t;
                }

                pixels[y * texW + x] = c;
            }
        }

        _texture.SetPixels(pixels);
        _texture.Apply(false, false);

        // Assign to an unlit material
        var r = GetComponent<MeshRenderer>();
        _material = new Material(Shader.Find("Unlit/Texture"));
        _material.mainTexture = _texture;
        r.material = _material;
    }

    void OnDestroy()
    {
        if (_material != null) Destroy(_material);
        if (_texture  != null) Destroy(_texture);
    }

    // Positive modulo
    private static int Mod(int a, int m)
    {
        int r = a % m;
        return (r < 0) ? r + m : r;
    }
}
