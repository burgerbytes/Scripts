using UnityEngine;

public class FieldObjectInstance : MonoBehaviour
{
    [SerializeField] private FieldObjectDefinitionSO definition;

    private CombatGridOccupant gridOccupant;

    public FieldObjectDefinitionSO Definition => definition;
    public CombatGridOccupant GridOccupant => gridOccupant;

    private void Awake()
    {
        gridOccupant = GetComponent<CombatGridOccupant>();

        if (gridOccupant == null)
            gridOccupant = gameObject.AddComponent<CombatGridOccupant>();
    }

    public void Initialize(FieldObjectDefinitionSO newDefinition)
    {
        definition = newDefinition;
    }

    public void TriggerDirectInteraction(GameObject source)
    {
        if (definition == null)
            return;

        if (definition.explosive)
        {
            Debug.Log($"[FieldObjectInstance] {name} direct interaction triggered explosion.");
            TriggerExplosion();
            return;
        }

        Debug.Log($"[FieldObjectInstance] {name} direct interaction from {(source != null ? source.name : "null")}.");
    }

    // Compatibility overload for existing BattleManager call sites
    public void TriggerDirectInteraction(object source, object context)
    {
        GameObject sourceGo = ExtractGameObject(source);
        TriggerDirectInteraction(sourceGo);

        Debug.Log($"[FieldObjectInstance] context arg = {context}");
    }

    public void TriggerExplosion()
    {
        Debug.Log($"[FieldObjectInstance] {name} exploded.");
    }

    public void TriggerElectrifiedEffect(GameObject target)
    {
        if (target == null)
            return;

        Debug.Log($"[FieldObjectInstance] {name} electrified {target.name}.");
    }

    private GameObject ExtractGameObject(object obj)
    {
        if (obj == null)
            return null;

        if (obj is GameObject go)
            return go;

        if (obj is Component comp)
            return comp.gameObject;

        return null;
    }
}