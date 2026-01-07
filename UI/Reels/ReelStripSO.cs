using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Idle Wasteland/Reels/Reel Strip", fileName = "ReelStrip_")]
public class ReelStripSO : ScriptableObject
{
    public List<ReelSymbolSO> symbols = new List<ReelSymbolSO>();
}
