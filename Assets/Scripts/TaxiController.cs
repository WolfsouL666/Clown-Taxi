using UnityEngine;
using UnityEngine.InputSystem;

namespace ClownTaxi.Runtime
{
    [DisallowMultipleComponent]
    public sealed class TaxiController : MonoBehaviour
    {
        [Header("==== DRIVING ====")]
        [SerializeField] private float acceleration = 18f;
        [SerializeField] private float maxSpeed = 20f;
        [SerializeField] private float reverseSpeed = 7f;
        [SerializeField] private float steering = 105f;
        [SerializeField] private float braking = 24f;
        [SerializeField] private float steeringWeight = 1f;
        [SerializeField] private float steeringResponse = 8f;
        [SerializeField] private float collisionPadding = 0.15f;

        [Header("==== REFERENCES ====")]
        [SerializeField] private Transform driverSeat;
        [SerializeField] private Transform cameraSeat;
        [SerializeField] private Transform exitPoint;

        private FirstPersonPlayer occupant;
        private float speed;
        private float steeringInput;
        private bool cutsceneActive;
        private Transform passengerSeat;
        private PassengerNpc boardedPassenger;
        private Vector3 localVehicleCenter;
        private Vector3 localVehicleHalfExtents;
        private bool vehicleBoundsCached;

        public static TaxiController Instance { get; private set; }
        public Transform CameraSeat => cameraSeat;
        public Transform ExitPoint => exitPoint;
        public bool IsOccupied => occupant != null;
        public Transform PassengerSeat => passengerSeat;
        public bool CutsceneActive => cutsceneActive;

        public void Configure()
        {
            Instance = this;

            if (driverSeat == null)
                driverSeat = CreatePoint("Driver Seat", new Vector3(0f, 1.1f, 0.35f));
            if (cameraSeat == null)
                cameraSeat = CreatePoint("Camera Seat", new Vector3(0f, 1.45f, 0.25f));
            if (exitPoint == null)
                exitPoint = CreatePoint("Exit Point", new Vector3(-2.2f, 0f, 0f));
            if (passengerSeat == null)
                passengerSeat = CreatePoint("Passenger Seat", new Vector3(0f, 0.9f, -1.15f));

            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            if (colliders.Length == 0)
            {
                BoxCollider box = gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(2f, 1.5f, 4f);
                box.center = new Vector3(0f, 0.75f, 0f);
            }

            CacheVehicleBounds();

            Rigidbody body = GetComponent<Rigidbody>();
            if (body == null)
                body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            Debug.Log($"[TaxiController] Configured on {gameObject.name}");
        }

        private void Update()
        {
            if (occupant == null)
                return;

            if (cutsceneActive)
            {
                speed = 0f;
                steeringInput = 0f;
                return;
            }

            float throttle = 0f;
            if (Keyboard.current != null)
            {
                throttle = (Keyboard.current.wKey.isPressed ? 1f : 0f) - (Keyboard.current.sKey.isPressed ? 1f : 0f);
            }

            float target = throttle >= 0f ? throttle * maxSpeed : throttle * reverseSpeed;
            float rate = Mathf.Abs(target) > 0.01f ? acceleration : braking;
            speed = Mathf.MoveTowards(speed, target, rate * Time.deltaTime);

            float steeringTarget = 0f;
            if (Keyboard.current != null)
                steeringTarget = (Keyboard.current.dKey.isPressed ? 1f : 0f) - (Keyboard.current.aKey.isPressed ? 1f : 0f);

            steeringInput = Mathf.MoveTowards(steeringInput, steeringTarget, steeringResponse * Time.deltaTime);
            float speedRatio = Mathf.Clamp01(Mathf.Abs(speed) / maxSpeed);
            float direction = speed < -0.1f ? -1f : 1f;
            float steeringAmount = steeringInput * steering * Mathf.Lerp(0.45f, 1f, speedRatio) * direction * steeringWeight;
            Quaternion previousRotation = transform.rotation;
            Quaternion nextRotation = previousRotation * Quaternion.Euler(0f, steeringAmount * Time.deltaTime, 0f);
            if (CanOccupy(nextRotation))
                transform.rotation = nextRotation;
            MoveWithCollision(speed * Time.deltaTime);

            DeliveryManager.Instance?.TryCompleteAtTaxi(transform.position);
        }

        public void TryEnter(FirstPersonPlayer player)
        {
            if (IsOccupied || player == null)
            {
                Debug.Log($"[TaxiController] Entry failed: occupied={IsOccupied}, player={player != null}");
                return;
            }

            Debug.Log($"[TaxiController] Player entering taxi");
            Enter(player);
        }

        public void TryPickUpNpc(PassengerNpc npc)
        {
            if (occupant == null || npc == null || !npc.WantsPickup || cutsceneActive || boardedPassenger != null || DeliveryManager.Instance == null)
                return;

            if (DeliveryManager.Instance.TryPickupNpc(npc))
            {
                boardedPassenger = npc;
                npc.BoardTaxi(passengerSeat);
                cutsceneActive = true;
                speed = 0f;
                steeringInput = 0f;
                occupant.BeginPickupCutscene(npc, DeliveryManager.Instance.CurrentDestinationName, EndPickupCutscene);
            }
        }

        private void EndPickupCutscene()
        {
            cutsceneActive = false;
        }

        private void Enter(FirstPersonPlayer player)
        {
            occupant = player;
            player.SetDriving(true, driverSeat, this);
            Debug.Log($"[TaxiController] Player is now driving");
        }

        public void TryExit(FirstPersonPlayer player)
        {
            if (!cutsceneActive && player != null && occupant == player)
                Exit();
        }

        private void Exit()
        {
            Debug.Log($"[TaxiController] Player exiting taxi");
            FirstPersonPlayer player = occupant;
            occupant = null;
            speed = 0f;
            steeringInput = 0f;
            cutsceneActive = false;
            player.SetDriving(false, driverSeat, this);
        }

        public void CompletePassengerRide(Vector3 streetPosition)
        {
            if (boardedPassenger == null)
                return;

            PassengerNpc passenger = boardedPassenger;
            boardedPassenger = null;
            passenger.Disembark(streetPosition);
        }

        private void MoveWithCollision(float distance)
        {
            if (Mathf.Abs(distance) < 0.001f)
                return;

            Vector3 direction = transform.forward * Mathf.Sign(distance);
            GetWorldVehicleBounds(transform.rotation, out Vector3 center, out Vector3 halfExtents);
            float skin = Mathf.Max(0f, collisionPadding);
            RaycastHit[] hits = Physics.BoxCastAll(center, halfExtents, direction, transform.rotation,
                Mathf.Abs(distance) + skin, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float closestHit = Mathf.Infinity;
            foreach (RaycastHit hit in hits)
            {
                if (!IsBlocking(hit.collider, hit.normal, center.y - halfExtents.y))
                    continue;

                closestHit = Mathf.Min(closestHit, hit.distance);
            }

            if (closestHit < Mathf.Infinity)
            {
                float allowedDistance = Mathf.Clamp(closestHit - skin, 0f, Mathf.Abs(distance));
                transform.position += direction * allowedDistance;
                speed = 0f;
                return;
            }

            transform.position += direction * Mathf.Abs(distance);
        }

        private void CacheVehicleBounds()
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            Bounds localBounds = new Bounds(Vector3.zero, Vector3.zero);
            bool hasBounds = false;
            foreach (Collider collider in colliders)
            {
                if (!collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger)
                    continue;

                BoxCollider box = collider as BoxCollider;
                Bounds sourceBounds = box != null ? new Bounds(box.center, box.size) : collider.bounds;
                Vector3 min = sourceBounds.min;
                Vector3 max = sourceBounds.max;
                Vector3[] corners =
                {
                    new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z),
                    new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z),
                    new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z),
                    new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z)
                };
                foreach (Vector3 corner in corners)
                {
                    Vector3 worldCorner = box != null ? box.transform.TransformPoint(corner) : corner;
                    Vector3 localCorner = transform.InverseTransformPoint(worldCorner);
                    if (hasBounds)
                        localBounds.Encapsulate(localCorner);
                    else
                    {
                        localBounds = new Bounds(localCorner, Vector3.zero);
                        hasBounds = true;
                    }
                }
            }

            if (!hasBounds)
            {
                localBounds = new Bounds(new Vector3(0f, 0.75f, 0f), new Vector3(2f, 1.5f, 4f));
            }

            localVehicleCenter = localBounds.center;
            localVehicleHalfExtents = localBounds.extents;
            vehicleBoundsCached = true;
        }

        private bool CanOccupy(Quaternion rotation)
        {
            if (!vehicleBoundsCached)
                CacheVehicleBounds();

            Quaternion startRotation = transform.rotation;
            float angle = Quaternion.Angle(startRotation, rotation);
            if (angle < 0.0001f)
                return true;

            int steps = Mathf.Max(1, Mathf.CeilToInt(angle / 5f));
            GetWorldVehicleBounds(startRotation, out Vector3 startCenter, out Vector3 startExtents);
            float radius = Vector3.Distance(transform.position, startCenter) + startExtents.magnitude;
            float sweepMargin = 2f * radius * Mathf.Sin(angle / steps * Mathf.Deg2Rad * 0.25f);
            for (int i = 1; i <= steps; i++)
            {
                Quaternion testRotation = Quaternion.Slerp(startRotation, rotation, i / (float)steps);
                GetWorldVehicleBounds(testRotation, out Vector3 center, out Vector3 halfExtents);
                Collider[] hits = Physics.OverlapBox(center, halfExtents + Vector3.one * (Mathf.Max(0f, collisionPadding) + sweepMargin),
                    testRotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                foreach (Collider hit in hits)
                {
                    if (IsBlocking(hit, Vector3.zero, center.y - halfExtents.y))
                        return false;
                }
            }

            return true;
        }

        private void GetWorldVehicleBounds(Quaternion rotation, out Vector3 center, out Vector3 halfExtents)
        {
            Vector3 scale = transform.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            center = transform.position + rotation * Vector3.Scale(localVehicleCenter, scale);
            halfExtents = Vector3.Scale(localVehicleHalfExtents, scale);
        }

        private bool IsBlocking(Collider collider, Vector3 hitNormal, float vehicleBottom)
        {
            if (collider == null || !collider.enabled || collider.isTrigger || IsOwnCollider(collider))
                return false;
            if (collider.bounds.max.y <= vehicleBottom + 0.01f)
                return false;
            if (hitNormal.sqrMagnitude > 0.01f && hitNormal.y > 0.65f)
                return false;
            return true;
        }

        private bool IsOwnCollider(Collider collider)
        {
            return collider.transform == transform || collider.transform.IsChildOf(transform);
        }

        private Transform CreatePoint(string pointName, Vector3 localPosition)
        {
            GameObject point = new GameObject(pointName);
            point.transform.SetParent(transform);
            point.transform.localPosition = localPosition;
            point.transform.localRotation = Quaternion.identity;
            return point.transform;
        }
    }
}
