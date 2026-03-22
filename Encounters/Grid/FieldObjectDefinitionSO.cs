using UnityEngine;

[CreateAssetMenu(menuName = "Combat/Field Object Definition")]
public class FieldObjectDefinitionSO : ScriptableObject
{
    [Header("Identity")]
    public string displayName = "Field Object";
    [TextArea(2, 5)] public string description = "";
    public Sprite icon;
    public GameObject prefab;

    [Header("Properties")]
    public bool explosive = false;
    public bool electrified = false;
    public bool hasKnockdownProperty = false;

    [Header("Explosion")]
    [Min(0)] public int explosionDamage = 6;
    [Min(0)] public int explosionRadiusTiles = 1;
    public bool explosionKnocksBack = true;

    [Header("Electrified")]
    [Min(0)] public int stunPlayerPhases = 1;
}
