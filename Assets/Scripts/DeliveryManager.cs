using UnityEngine;
using ClownTaxi.Blockout;
using System.Collections.Generic;

namespace ClownTaxi.Runtime
{
    [DisallowMultipleComponent]
    public sealed class DeliveryManager : MonoBehaviour
    {
        [Header("==== MISSION ====")]
        [SerializeField] private float pickupDistance = 4f;
        [SerializeField] private float dropOffDistance = 2.2f;

        [Header("==== MARKER ====")]
        [SerializeField] private Color markerColor = new Color(0.1f, 1f, 0.2f, 0.8f);
        [SerializeField] private float markerHeight = 0.04f;

        private BlockoutMarker pickup;
        private GameObject markerObject;
        private bool passengerOnBoard;
        private bool configured;
        private Transform currentDestination;
        private GameObject destinationObject;
        private Material markerMaterial;
        private readonly List<BlockoutMarker> dropOffs = new List<BlockoutMarker>();

        private GUIStyle compassStyle;
        private GUIStyle distanceStyle;

        public static DeliveryManager Instance { get; private set; }
        public string CurrentDestinationName => currentDestination == null ? "the destination" : currentDestination.name;

        public void Configure()
        {
            if (configured)
                return;
            configured = true;
            Instance = this;
            pickup = null;
            dropOffs.Clear();
            BlockoutMarker[] markers = FindObjectsByType<BlockoutMarker>(FindObjectsSortMode.None);
            foreach (BlockoutMarker marker in markers)
            {
                if (pickup == null && marker.kind == BlockoutMarkerKind.Pickup)
                    pickup = marker;
                if (marker.kind == BlockoutMarkerKind.DropOff && IsBuildingTarget(marker.transform))
                    dropOffs.Add(marker);
            }
            SetMarker(pickup == null ? null : pickup.transform);
        }

        private void Awake()
        {
            Instance = this;
            Configure();
        }

        public void TryPickupAtTaxi(Vector3 taxiPosition)
        {
            if (passengerOnBoard || pickup == null || dropOffs.Count == 0 || Vector3.Distance(taxiPosition, pickup.transform.position) > pickupDistance)
                return;

            if (!SelectDestination())
                return;
            passengerOnBoard = true;
            SetMarker(currentDestination);
        }

        public bool TryPickupNpc(PassengerNpc npc)
        {
            if (passengerOnBoard || npc == null || dropOffs.Count == 0)
                return false;

            if (!SelectDestination())
                return false;
            passengerOnBoard = true;
            SetMarker(currentDestination);
            return true;
        }

        public void TryCompleteAtTaxi(Vector3 taxiPosition)
        {
            Transform destination = currentDestination;
            Vector3 horizontalOffset = destination == null ? Vector3.zero : destination.position - taxiPosition;
            horizontalOffset.y = 0f;
            if (!passengerOnBoard || destination == null || horizontalOffset.magnitude > dropOffDistance)
                return;

            TaxiController taxi = FindFirstObjectByType<TaxiController>();
            if (taxi == null || taxi.CutsceneActive || !FindSafeDisembarkPosition(taxi, destination.position, out Vector3 exitPosition))
                return;
            taxi.CompletePassengerRide(exitPosition);
            passengerOnBoard = false;
            currentDestination = null;
            if (destinationObject != null)
                Destroy(destinationObject);
            SetMarker(pickup == null ? null : pickup.transform);
        }

        private void OnGUI()
        {
            if (!passengerOnBoard || currentDestination == null)
                return;

            TaxiController taxi = FindFirstObjectByType<TaxiController>();
            if (taxi == null || !taxi.IsOccupied || taxi.CutsceneActive)
                return;

            Vector3 offset = currentDestination.position - taxi.transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude < 0.01f)
                return;

            float angle = Vector3.SignedAngle(taxi.transform.forward, offset.normalized, Vector3.up);
            float size = Mathf.Clamp(Screen.width * 0.08f, 56f, 96f);
            Rect arrowRect = new Rect(Screen.width * 0.5f - size * 0.5f, 28f, size, size);
            Rect labelRect = new Rect(Screen.width * 0.5f - 110f, 28f + size, 220f, 28f);

            if (compassStyle == null)
            {
                compassStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = Mathf.RoundToInt(size * 0.55f),
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = markerColor }
                };
                distanceStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 16,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.white }
                };
            }

            GUIUtility.RotateAroundPivot(angle, arrowRect.center);
            GUI.Label(arrowRect, "▲", compassStyle);
            GUIUtility.RotateAroundPivot(-angle, arrowRect.center);
            GUI.Label(labelRect, $"DELIVER  {offset.magnitude:0}m", distanceStyle);
        }

        private void SetMarker(Transform target)
        {
            if (markerObject != null)
                Destroy(markerObject);
            if (markerMaterial != null)
            {
                Destroy(markerMaterial);
                markerMaterial = null;
            }
            if (target == null)
                return;

            markerObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            markerObject.name = "Delivery Marker";
            markerObject.transform.position = target.position + Vector3.up * markerHeight;
            markerObject.transform.localScale = new Vector3(dropOffDistance * 2f, 0.025f, dropOffDistance * 2f);
            Collider markerCollider = markerObject.GetComponent<Collider>();
            if (markerCollider != null)
            {
                markerCollider.enabled = false;
                Destroy(markerCollider);
            }

            Renderer renderer = markerObject.GetComponent<Renderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            Material material = new Material(shader);
            material.color = markerColor;
            renderer.sharedMaterial = material;
            markerMaterial = material;
        }

        private bool SelectDestination()
        {
            int start = Random.Range(0, dropOffs.Count);
            for (int i = 0; i < dropOffs.Count; i++)
            {
                BlockoutMarker marker = dropOffs[(start + i) % dropOffs.Count];
                if (marker == null)
                    continue;
                Transform destination = CreateStreetDestination(marker);
                if (destination == null)
                    continue;
                currentDestination = destination;
                return true;
            }
            return false;
        }

        private bool IsBuildingTarget(Transform target)
        {
            if (target == null || target.GetComponentInParent<PassengerNpc>() != null)
                return false;

            return target.GetComponent<BlockoutLandmark>() != null || FindNearestBuilding(target.position) != null ||
                target.name.Contains("Building") || target.name.Contains("House") || target.name.Contains("Academy") ||
                target.name.Contains("Circus") || target.name.Contains("Pavilion") || target.name.Contains("Wheel") ||
                target.name.Contains("Carousel") || target.name.Contains("Arcade") || target.name.Contains("Heights") ||
                target.name.Contains("Hotel") || target.name.Contains("Apartments") || target.name.Contains("Store") ||
                target.name.Contains("Garage");
        }

        private bool FindSafeDisembarkPosition(TaxiController taxi, Vector3 destination, out Vector3 exitPosition)
        {
            Bounds vehicleBounds = new Bounds(taxi.transform.position, Vector3.zero);
            foreach (Collider collider in taxi.GetComponentsInChildren<Collider>())
                if (collider.enabled && !collider.isTrigger)
                    vehicleBounds.Encapsulate(collider.bounds);
            Vector3 right = taxi.transform.right;
            Vector3 forward = taxi.transform.forward;
            float sideDistance = Mathf.Abs(right.x) * vehicleBounds.extents.x + Mathf.Abs(right.z) * vehicleBounds.extents.z + 0.8f;
            float endDistance = Mathf.Abs(forward.x) * vehicleBounds.extents.x + Mathf.Abs(forward.z) * vehicleBounds.extents.z + 0.8f;
            Vector3 center = vehicleBounds.center;
            if (Vector3.Dot(destination - center, right) < 0f)
                right = -right;
            Vector3[] candidates =
            {
                center + right * sideDistance,
                center - right * sideDistance,
                center + forward * endDistance,
                center - forward * endDistance
            };

            foreach (Vector3 candidate in candidates)
            {
                Vector3 point = candidate;
                if (!Physics.Raycast(new Vector3(point.x, destination.y + 3f, point.z), Vector3.down,
                    out RaycastHit ground, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    continue;
                if (ground.collider.GetComponentInParent<TaxiController>() != null || ground.point.y > destination.y + 0.5f)
                    continue;
                point.y = ground.point.y + 0.04f;
                Collider[] hits = Physics.OverlapCapsule(point + Vector3.up * 0.5f, point + Vector3.up * 1.6f, 0.45f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                bool blocked = false;
                foreach (Collider hit in hits)
                {
                    blocked = true;
                    break;
                }

                if (!blocked)
                {
                    exitPosition = point;
                    return true;
                }
            }

            exitPosition = default;
            return false;
        }

        private Transform FindNearestBuilding(Vector3 position)
        {
            GameObject[] sceneObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            Transform nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (GameObject sceneObject in sceneObjects)
            {
                if (!sceneObject.name.Contains("Building Block"))
                    continue;

                Renderer renderer = sceneObject.GetComponent<Renderer>();
                if (renderer == null)
                    continue;

                float distance = (renderer.bounds.ClosestPoint(position) - position).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = sceneObject.transform;
                }
            }

            return nearest;
        }

        private Transform CreateStreetDestination(BlockoutMarker destinationMarker)
        {
            Transform building = FindNearestBuilding(destinationMarker.transform.position);
            Vector3 targetPosition = building == null ? destinationMarker.transform.position : building.position;
            Collider nearestRoad = null;
            float nearestDistance = float.MaxValue;
            Vector3 streetPosition = default;
            GameObject[] sceneObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (GameObject sceneObject in sceneObjects)
            {
                if (!sceneObject.name.Contains("Road"))
                    continue;
                BoxCollider road = sceneObject.GetComponent<BoxCollider>();
                if (road == null || !road.enabled || road.isTrigger)
                    continue;
                Vector3 scale = road.transform.lossyScale;
                float inset = dropOffDistance + 0.2f;
                Vector3 halfSize = road.size * 0.5f;
                float insetX = inset / Mathf.Abs(scale.x);
                float insetZ = inset / Mathf.Abs(scale.z);
                if (halfSize.x < insetX || halfSize.z < insetZ)
                    continue;
                Vector3 localPoint = road.transform.InverseTransformPoint(targetPosition) - road.center;
                localPoint.x = Mathf.Clamp(localPoint.x, -halfSize.x + insetX, halfSize.x - insetX);
                localPoint.z = Mathf.Clamp(localPoint.z, -halfSize.z + insetZ, halfSize.z - insetZ);
                localPoint.y = halfSize.y;
                Vector3 point = road.transform.TransformPoint(localPoint + road.center);
                Collider[] padHits = Physics.OverlapBox(point + Vector3.up * 1.2f,
                    new Vector3(dropOffDistance, 1f, dropOffDistance), Quaternion.identity,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                bool padBlocked = false;
                foreach (Collider padHit in padHits)
                {
                    if (padHit == road)
                        continue;
                    padBlocked = true;
                    break;
                }
                if (padBlocked)
                    continue;
                float distance = (point - targetPosition).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestRoad = road;
                    streetPosition = point;
                }
            }

            if (nearestRoad == null)
                return null;

            if (destinationObject != null)
                Destroy(destinationObject);
            destinationObject = new GameObject(string.IsNullOrWhiteSpace(destinationMarker.locationName) ? destinationMarker.name : destinationMarker.locationName);
            destinationObject.transform.position = streetPosition;
            return destinationObject.transform;
        }

        private void OnDestroy()
        {
            if (markerObject != null)
                Destroy(markerObject);
            if (destinationObject != null)
                Destroy(destinationObject);
            if (markerMaterial != null)
                Destroy(markerMaterial);
            if (Instance == this)
                Instance = null;
        }
    }
}
