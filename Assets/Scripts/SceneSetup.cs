using UnityEngine;
using ClownTaxi.Blockout;

namespace ClownTaxi.Runtime
{
    [DisallowMultipleComponent]
    public sealed class SceneSetup : MonoBehaviour
    {
        [Header("==== REFERENCES ====")]
        [SerializeField] private Transform player;
        [SerializeField] private Transform taxi;

        private void Awake()
        {
            if (player == null)
            {
                GameObject playerObject = GameObject.Find("Player - Cylinder");
                if (playerObject != null)
                    player = playerObject.transform;
            }
            if (taxi == null)
            {
                GameObject taxiObject = GameObject.Find("Taxi - Static Placeholder");
                if (taxiObject != null)
                    taxi = taxiObject.transform;
            }

            MoveToSpawn(player, BlockoutMarkerKind.PlayerSpawn);
            MoveToSpawn(taxi, BlockoutMarkerKind.TaxiSpawn);
            AddBuildingCollisions();
        }

        private void MoveToSpawn(Transform target, BlockoutMarkerKind kind)
        {
            if (target == null)
                return;

            BlockoutMarker[] markers = FindObjectsByType<BlockoutMarker>(FindObjectsSortMode.None);
            foreach (BlockoutMarker marker in markers)
            {
                if (marker.kind != kind)
                    continue;
                target.SetPositionAndRotation(marker.transform.position, marker.transform.rotation);
                return;
            }
        }

        private void AddBuildingCollisions()
        {
            GameObject[] sceneObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (GameObject sceneObject in sceneObjects)
            {
                if (!IsBuildingObject(sceneObject) || sceneObject.GetComponentInParent<PassengerNpc>() != null)
                    continue;

                Renderer[] renderers = sceneObject.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                    continue;

                Collider[] existingColliders = sceneObject.GetComponents<Collider>();
                bool hasSolidCollider = false;
                foreach (Collider existingCollider in existingColliders)
                {
                    if (!existingCollider.isTrigger && existingCollider.enabled)
                    {
                        hasSolidCollider = true;
                        break;
                    }
                }

                if (hasSolidCollider)
                    continue;

                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);

                GameObject collision = new GameObject($"{sceneObject.name} Collision");
                collision.transform.SetParent(transform, true);
                collision.transform.position = bounds.center;
                BoxCollider box = collision.AddComponent<BoxCollider>();
                box.size = bounds.size;
            }
        }

        private bool IsBuildingObject(GameObject sceneObject)
        {
            if (sceneObject == null || sceneObject.name.Contains("Collision") || sceneObject.name.Contains("Pickup Area"))
                return false;

            BlockoutMarker marker = sceneObject.GetComponent<BlockoutMarker>();
            if (marker != null && marker.kind == BlockoutMarkerKind.DropOff)
                return true;

            return sceneObject.name.Contains("Building Block");
        }
    }
}
