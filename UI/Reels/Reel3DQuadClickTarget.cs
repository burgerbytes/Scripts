using UnityEngine;

/// <summary>
/// Simple marker component attached to each front quad on a Reel3DColumn.
/// Used for Reelcraft selection (Mage transmutation).
/// </summary>
public class Reel3DQuadClickTarget : MonoBehaviour
{
    public Reel3DColumn Column { get; set; }
    public int QuadIndex { get; set; }
}
