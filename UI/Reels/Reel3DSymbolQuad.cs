using UnityEngine;

/// <summary>
/// Renders a ReelSymbolSO icon on a world-space quad by setting UVs to match the sprite rect.
/// Works with sprite atlases (uses sprite.texture + rect-based UVs).
///
/// IMPORTANT: This component may receive SetSymbol() calls before Awake() (depending on build order),
/// so it lazily ensures a quad mesh exists whenever a sprite is applied.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class Reel3DSymbolQuad : MonoBehaviour
{
    [Header("Render")]
    [Tooltip("Optional override. If null, will use MeshRenderer.sharedMaterial.")]
    [SerializeField] private Material materialOverride;

    [Tooltip("When true, applies a ~1px padding inward on UVs to reduce atlas bleeding.")]
    [SerializeField] private bool insetUvByOnePixel = true;

    private MeshFilter _mf;
    private MeshRenderer _mr;
    private Mesh _mesh;

    private MaterialPropertyBlock _mpb;

    private void Awake()
    {
        _mf = GetComponent<MeshFilter>();
        _mr = GetComponent<MeshRenderer>();
        _mpb = new MaterialPropertyBlock();

        EnsureMeshExists();

        if (materialOverride != null)
            _mr.sharedMaterial = materialOverride;
    }

    /// <summary>Assigns a symbol to this quad (sets texture + UVs).</summary>
    public void SetSymbol(ReelSymbolSO symbol)
    {
        if (symbol == null || symbol.icon == null)
        {
            Clear();
            return;
        }

        ApplySprite(symbol.icon);
    }

    public void Clear()
    {
        if (_mr == null) _mr = GetComponent<MeshRenderer>();
        if (_mr != null) _mr.enabled = false;
    }

    private void ApplySprite(Sprite sprite)
    {
        if (_mr == null) _mr = GetComponent<MeshRenderer>();
        if (_mf == null) _mf = GetComponent<MeshFilter>();
        if (_mpb == null) _mpb = new MaterialPropertyBlock();

        _mr.enabled = true;

        EnsureMeshExists();

        // Set the texture via MaterialPropertyBlock (so all quads can share the same material).
        Texture2D tex = sprite.texture;

        _mr.GetPropertyBlock(_mpb);
        // Built-in / legacy
        _mpb.SetTexture("_MainTex", tex);
        // URP (Lit/Unlit) common
        _mpb.SetTexture("_BaseMap", tex);
        _mr.SetPropertyBlock(_mpb);

        // Use textureRect so this works with packed atlases.
        Rect r = sprite.textureRect;

        float texW = tex.width;
        float texH = tex.height;

        // Inset UVs by ~1 pixel to reduce atlas bleed.
        float padX = insetUvByOnePixel ? 1f / texW : 0f;
        float padY = insetUvByOnePixel ? 1f / texH : 0f;

        float xMin = (r.xMin / texW) + padX;
        float xMax = (r.xMax / texW) - padX;
        float yMin = (r.yMin / texH) + padY;
        float yMax = (r.yMax / texH) - padY;

        Vector2[] uvs = new Vector2[4];
        uvs[0] = new Vector2(xMin, yMin); // bottom-left
        uvs[1] = new Vector2(xMin, yMax); // top-left
        uvs[2] = new Vector2(xMax, yMax); // top-right
        uvs[3] = new Vector2(xMax, yMin); // bottom-right

        EnsureMeshIsWritable();
        _mesh.uv = uvs;
    }

    private void EnsureMeshExists()
    {
        if (_mf.sharedMesh == null)
        {
            _mesh = BuildQuadMesh();
            _mf.sharedMesh = _mesh;
        }
        else
        {
            _mesh = _mf.sharedMesh;
        }
    }

    private void EnsureMeshIsWritable()
    {
        // Avoid mutating a shared mesh asset across multiple instances.
        if (_mesh != null && _mesh.name != "Reel3D_QuadMesh_Runtime")
        {
            Mesh cloned = Instantiate(_mesh);
            cloned.name = "Reel3D_QuadMesh_Runtime";
            _mesh = cloned;
            _mf.sharedMesh = _mesh;
        }
    }

    private static Mesh BuildQuadMesh()
    {
        Mesh m = new Mesh();
        m.name = "Reel3D_QuadMesh_Runtime";

        // 1x1 quad centered at origin facing +Z
        Vector3[] verts = new Vector3[4]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f),
            new Vector3( 0.5f, -0.5f, 0f),
        };

        int[] tris = new int[6] { 0, 1, 2, 0, 2, 3 };

        Vector2[] uvs = new Vector2[4]
        {
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 0f),
        };

        Vector3[] norms = new Vector3[4]
        {
            Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward
        };

        m.vertices = verts;
        m.triangles = tris;
        m.uv = uvs;
        m.normals = norms;

        m.RecalculateBounds();
        return m;
    }
}
