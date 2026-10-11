using UnityEngine;

namespace ClownTaxi.Blockout
{
    [DisallowMultipleComponent]
    public sealed class BlockoutLevel : MonoBehaviour
    {
        [Min(260f)] public float width = 260f;
        [Min(240f)] public float depth = 240f;
        [Range(6f, 12f)] public float roadWidth = 8f;
        [Range(0, 40)] public int treeCount = 18;
        [Range(0, 48)] public int npcCount = 24;
        public int seed = 42;
        public bool includeTaxi = true;
    }
}
