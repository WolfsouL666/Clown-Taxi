using UnityEngine;
using ClownTaxi.Blockout;

namespace ClownTaxi.Runtime
{
    public static class ClownTaxiRuntimeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            GameObject runtime = new GameObject("Clown Taxi Runtime");

            GameObject playerObject = GameObject.Find("Player - Cylinder");
            GameObject taxiObject = GameObject.Find("Taxi - Static Placeholder");
            GameObject cameraObject = GameObject.FindWithTag("MainCamera");
            Transform playerSpawn = FindMarker(BlockoutMarkerKind.PlayerSpawn);
            Transform taxiSpawn = FindMarker(BlockoutMarkerKind.TaxiSpawn);

            if (playerObject != null)
            {
                if (playerSpawn != null)
                    playerObject.transform.SetPositionAndRotation(playerSpawn.position, playerSpawn.rotation);
                FirstPersonPlayer player = playerObject.GetComponent<FirstPersonPlayer>();
                if (player == null)
                    player = playerObject.AddComponent<FirstPersonPlayer>();
                Camera playerCamera = cameraObject == null ? Camera.main : cameraObject.GetComponent<Camera>();
                if (playerCamera != null)
                    player.Configure(playerCamera);
            }

            if (taxiObject != null)
            {
                if (taxiSpawn != null)
                    taxiObject.transform.SetPositionAndRotation(taxiSpawn.position, taxiSpawn.rotation);
                TaxiController taxi = taxiObject.GetComponent<TaxiController>();
                if (taxi == null)
                    taxi = taxiObject.AddComponent<TaxiController>();
                taxi.Configure();
            }

            ConfigureBuildingCollisions(runtime.transform);

            GameObject[] sceneObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (GameObject sceneObject in sceneObjects)
            {
                if (!sceneObject.name.StartsWith("Clown NPC"))
                    continue;

                PassengerNpc passenger = sceneObject.GetComponent<PassengerNpc>();
                if (passenger == null)
                    passenger = sceneObject.AddComponent<PassengerNpc>();
                passenger.Configure();
            }

            DeliveryManager delivery = runtime.AddComponent<DeliveryManager>();
            delivery.Configure();
        }

        private static void ConfigureBuildingCollisions(Transform runtimeRoot)
        {
            GameObject[] sceneObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
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
                collision.transform.SetParent(runtimeRoot, true);
                collision.transform.position = bounds.center;
                BoxCollider box = collision.AddComponent<BoxCollider>();
                box.size = bounds.size;
            }
        }

        private static bool IsBuildingObject(GameObject sceneObject)
        {
            if (sceneObject == null || sceneObject.name.Contains("Collision") || sceneObject.name.Contains("Pickup Area"))
                return false;

            BlockoutMarker marker = sceneObject.GetComponent<BlockoutMarker>();
            if (marker != null && marker.kind == BlockoutMarkerKind.DropOff)
                return true;

            return sceneObject.name.Contains("Building Block");
        }

        private static Transform FindMarker(BlockoutMarkerKind kind)
        {
            BlockoutMarker[] markers = Object.FindObjectsByType<BlockoutMarker>(FindObjectsSortMode.None);
            foreach (BlockoutMarker marker in markers)
                if (marker.kind == kind)
                    return marker.transform;
            return null;
        }
    }
}
