using UnityEngine;

namespace ClownTaxi.Blockout
{
    public enum BlockoutMarkerKind { PlayerSpawn, TaxiSpawn, Pickup, DropOff }

    [DisallowMultipleComponent]
    public sealed class BlockoutMarker : MonoBehaviour
    {
        public BlockoutMarkerKind kind;
        public string locationName;
    }
}
