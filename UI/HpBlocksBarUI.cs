using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders an "HP blocks" bar:
/// - Green = current HP after preview damage applied
/// - Orange = incoming HP damage preview (after shields are considered)
/// - Gray = missing HP
/// - Blue = shield blocks (appended after max HP)
///
/// This is intentionally UI-only; it does NOT change gameplay.
/// </summary>
public class HpBlocksBarUI : MonoBehaviour
{
    public enum BlockType { HP, Missing, Preview, Shield }

    [Header("Container")]
    [Tooltip("Where block Images will be created (children). If null, we use this transform.")]
    [SerializeField] private RectTransform container;

    [Tooltip("Optional prefab for each block. If null, a basic Image will be created.")]
    [SerializeField] private Image blockPrefab;

    [Header("Sprites")]
    [SerializeField] private Sprite hpSprite;        // green
    [SerializeField] private Sprite missingSprite;   // gray
    [SerializeField] private Sprite previewSprite;   // orange
    [SerializeField] private Sprite shieldSprite;    // blue

    [Header("Layout")]
    [Tooltip("If no prefab is supplied, created Images will use this size (UI units).")]
    [SerializeField] private Vector2 blockSize = new Vector2(10f, 10f);

    [Tooltip("If true, we disable raycasts on generated Images.")]
    [SerializeField] private bool disableRaycastTargets = true;

    // We pool for zero-GC updates during combat.
    private readonly List<Image> _pool = new List<Image>(64);

    private RectTransform ContainerRT => container != null ? container : (RectTransform)transform;

    public bool IsConfigured =>
        (hpSprite != null || missingSprite != null || previewSprite != null || shieldSprite != null);

    public void Clear()
    {
        for (int i = 0; i < _pool.Count; i++)
        {
            if (_pool[i] != null)
                _pool[i].gameObject.SetActive(false);
        }
    }

    public void Render(int maxHp, int currentHp, int shield, int incomingDamagePreview)
    {
        maxHp = Mathf.Max(0, maxHp);
        currentHp = Mathf.Clamp(currentHp, 0, maxHp);
        shield = Mathf.Max(0, shield);
        incomingDamagePreview = Mathf.Max(0, incomingDamagePreview);

        // Shield absorbs first. Preview only shows HP damage that "gets through" shields.
        int hpDamageAfterShield = Mathf.Max(0, incomingDamagePreview - shield);

        int predictedHp = Mathf.Clamp(currentHp - hpDamageAfterShield, 0, maxHp);
        int green = predictedHp;
        int orange = Mathf.Clamp(currentHp - predictedHp, 0, maxHp);
        int gray = Mathf.Clamp(maxHp - currentHp, 0, maxHp);

        int total = maxHp + shield;

        EnsurePoolSize(total);

        int idx = 0;

        // HP zone
        for (int i = 0; i < green; i++) SetBlock(idx++, BlockType.HP);
        for (int i = 0; i < orange; i++) SetBlock(idx++, BlockType.Preview);
        for (int i = 0; i < gray; i++) SetBlock(idx++, BlockType.Missing);

        // Shield zone appended
        for (int i = 0; i < shield; i++) SetBlock(idx++, BlockType.Shield);

        // Disable remainder
        for (int i = idx; i < _pool.Count; i++)
            _pool[i].gameObject.SetActive(false);
    }

    private void EnsurePoolSize(int needed)
    {
        if (needed < 0) needed = 0;

        while (_pool.Count < needed)
        {
            Image img = null;

            if (blockPrefab != null)
            {
                img = Instantiate(blockPrefab, ContainerRT);
            }
            else
            {
                var go = new GameObject("HpBlock", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(ContainerRT, false);
                img = go.GetComponent<Image>();
                var rt = (RectTransform)go.transform;
                rt.sizeDelta = blockSize;
            }

            if (disableRaycastTargets && img != null)
                img.raycastTarget = false;

            _pool.Add(img);
        }
    }

    private void SetBlock(int index, BlockType type)
    {
        if (index < 0 || index >= _pool.Count) return;

        var img = _pool[index];
        if (img == null) return;

        img.gameObject.SetActive(true);

        switch (type)
        {
            case BlockType.HP:      img.sprite = hpSprite; break;
            case BlockType.Missing: img.sprite = missingSprite; break;
            case BlockType.Preview: img.sprite = previewSprite; break;
            case BlockType.Shield:  img.sprite = shieldSprite; break;
        }

        img.enabled = img.sprite != null;
    }
}
