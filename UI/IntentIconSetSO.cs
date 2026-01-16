////////////////////////////////////////////////////////////
// PATH: Assets/Scripts/UI/IntentIconSetSO.cs
// GUID: cba0aa3b5829d2d478a6076ff475c4a7
////////////////////////////////////////////////////////////
using UnityEngine;

[CreateAssetMenu(menuName = "UI/Intent Icon Set", fileName = "IntentIconSet")]
public class IntentIconSetSO : ScriptableObject
{
    public Sprite normal;
    public Sprite statusDebuffOnly;
    public Sprite damageAndStatus;
    public Sprite aoe;
    public Sprite statusAndAoe;
    public Sprite selfBuff;

    public Sprite Get(IntentCategory category)
    {
        switch (category)
        {
            case IntentCategory.StatusDebuffOnly:
                return statusDebuffOnly;

            case IntentCategory.DamageAndStatus:
                return damageAndStatus;

            case IntentCategory.Aoe:
                return aoe;

            case IntentCategory.StatusAndAoe:
                return statusAndAoe;

            case IntentCategory.SelfBuff:
                return selfBuff;

            case IntentCategory.Normal:
            default:
                return normal;
        }
    }
}
