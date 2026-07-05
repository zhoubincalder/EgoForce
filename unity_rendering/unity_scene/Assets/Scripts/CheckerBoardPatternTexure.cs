using UnityEngine;

/// <summary>
/// Attach this script to a Unity Plane (default size 10×10 units).
/// It will generate a procedural checkerboard texture at runtime,
/// using one of several color schemes (default, gray‐white, green‐olive, or custom).
/// Each square is `squareSizePixels` pixels on a side. The overall texture size
/// is computed from the plane’s world‐space dimensions.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class CheckerboardGround : MonoBehaviour
{
    public enum Scheme
    {
        Default,    // black & white
        GrayWhite,  // dark gray & white
        GreenOlive, // light green & dark olive
        Custom      // use color1/color2
    }

    [Header("Checkerboard Settings")]
    [Tooltip("Size, in pixels, of each checker square. Higher = crisper squares.")]
    public int squareSizePixels = 16;

    [Tooltip("Choose a preset color scheme, or Custom to pick your own colors.")]
    public Scheme scheme = Scheme.GreenOlive;

    [Tooltip("First color (used if scheme==Custom).")]
    public Color customColor1 = Color.cyan;

    [Tooltip("Second color (used if scheme==Custom).")]
    public Color customColor2 = Color.magenta;

    private Texture2D _texture;
    private Material  _material;

    void Start()
    {
        // 1) Determine world‐space size of the plane. Unity’s default Plane mesh is 10×10 units.
        Vector3 planeScale = transform.localScale;
        float worldWidth  = planeScale.x * 10f;  // e.g. scale.x=1 → 10 units
        float worldHeight = planeScale.z * 10f;  // z‐axis for height on a Unity Plane

        // 2) Compute how many checker squares fit along each axis:
        //    Since each square is squareSizePixels pixels, and we want exactly
        //    (worldWidth / 1 unit) squares per unit, we make texture width:
        //    texWidth  = worldWidth  * squareSizePixels
        //    texHeight = worldHeight * squareSizePixels
        int texWidth  = Mathf.RoundToInt(worldWidth * squareSizePixels);
        int texHeight = Mathf.RoundToInt(worldHeight * squareSizePixels);

        // Clamp to at least 1×1
        texWidth  = Mathf.Max(1, texWidth);
        texHeight = Mathf.Max(1, texHeight);

        // 3) Create the checkerboard Texture2D
        _texture = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false);
        _texture.filterMode = FilterMode.Point;
        _texture.wrapMode   = TextureWrapMode.Clamp;

        // 4) Determine the two Colors based on the chosen scheme
        Color c1, c2;
        switch (scheme)
        {
            case Scheme.GrayWhite:
                c1 = new Color(0.2f, 0.2f, 0.2f, 1f);
                c2 = Color.white;
                break;
            case Scheme.GreenOlive:
                c1 = new Color(0.727023f, 0.802122f, 0.020948f, 1f);  // light green
                c2 = new Color(0.006949f, 0.199935f, 0.000000f, 1f);  // dark olive
                break;
            case Scheme.Custom:
                c1 = customColor1;
                c2 = customColor2;
                break;
            default: // Default = black & white
                c1 = Color.white;
                c2 = Color.black;
                break;
        }

        // 5) Fill in the texture, one pixel at a time
        //    Each "square" in world‐space corresponds to squareSizePixels×squareSizePixels pixels.
        for (int y = 0; y < texHeight; y++)
        {
            for (int x = 0; x < texWidth; x++)
            {
                // Determine which square (in x,y) this pixel belongs to:
                int sqX = x / squareSizePixels;
                int sqY = y / squareSizePixels;

                // Checker logic: if sum of indices is even → color1, else color2
                bool isEven = ((sqX + sqY) % 2) == 0;
                _texture.SetPixel(x, y, isEven ? c1 : c2);
            }
        }

        _texture.Apply();

        // 6) Assign this texture to the plane’s material
        var renderer = GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            // Use a simple Unlit/Texture shader so color isn’t affected by lighting
            _material = new Material(Shader.Find("Unlit/Texture"));
            _material.mainTexture = _texture;
            renderer.material = _material;
        }
        else
        {
            Debug.LogError("CheckerboardGround: no MeshRenderer found!");
        }
    }

    void OnDestroy()
    {
        // Clean up the generated texture to free memory
        if (_texture != null)
        {
            Destroy(_texture);
            _texture = null;
        }
        if (_material != null)
        {
            Destroy(_material);
            _material = null;
        }
    }
}
