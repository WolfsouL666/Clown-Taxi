using System.Collections.Generic;
using UnityEngine;

namespace ClownTaxi.Runtime
{
    [DisallowMultipleComponent]
    public sealed class PassengerNpc : MonoBehaviour
    {
        [SerializeField] private float pickupRadius = 2.2f;
        [SerializeField] private float streetSearchRadius = 35f;
        [SerializeField] private Color pickupAreaColor = new Color(0.1f, 1f, 0.2f, 0.8f);
        [SerializeField] private float walkSpeed = 1.4f;
        [SerializeField] private float requestDelayMin = 8f;
        [SerializeField] private float requestDelayMax = 22f;
        [SerializeField] private float requestDuration = 35f;
        [SerializeField] [Range(0f, 1f)] private float requestChance = 0.35f;

        private readonly List<Collider> roadColliders = new List<Collider>();
        private readonly List<Waypoint> waypoints = new List<Waypoint>();
        private bool collected;
        private bool wantsPickup;
        private Collider ownCollider;
        private Animator anim; //Eilaf added
        private Vector3 pickupPoint;
        private GameObject pickupArea;
        private Material pickupMaterial;
        private Vector3 walkTarget;
        private float nextRequestTime;
        private float requestEndTime;
        private float pauseUntil;
        private float targetTimeout;
        private float blockedSince;
        private Vector3 originalWorldScale = Vector3.one;
        private bool configured;
        private bool hasPickupPoint;
        private Vector3 previousTarget;

        private struct Waypoint
        {
            public Vector3 point;
            public Collider road;
        }

        public bool IsCollected => collected;
        public bool WantsPickup => wantsPickup;

        private void Awake()
        {
            originalWorldScale = transform.lossyScale;
        }

        public void Configure()
        {
            if (configured)
                return;
            ownCollider = GetComponent<Collider>();
            anim = GetComponent<Animator>(); //Eilaf
            originalWorldScale = transform.lossyScale;
            CacheRoads();
            pickupPoint = FindStreetPickupPoint();
            CreatePickupArea();
            pickupArea.SetActive(false);
            walkTarget = SelectWalkTarget();
            targetTimeout = Time.time + 14f;
            pauseUntil = Time.time + Random.Range(0.5f, 2.5f);
            nextRequestTime = Time.time + Random.Range(requestDelayMin, requestDelayMax);
            configured = true;
        }

        public void Collect()
        {
            collected = true;
            if (pickupArea != null)
                pickupArea.SetActive(false);
            if (ownCollider != null)
                ownCollider.enabled = false;
            gameObject.SetActive(false);
        }

        public void BoardTaxi(Transform passengerSeat)
        {
            collected = true;
            wantsPickup = false;
            if (anim != null)
            {
                anim.SetBool("Collected", true); //Eilaf
                anim.SetBool("Cab", false); //Eilaf
            }
            if (pickupArea != null)
                pickupArea.SetActive(false);
            if (ownCollider != null)
                ownCollider.enabled = false;
            transform.SetParent(passengerSeat, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }

        public void Disembark(Vector3 streetPosition)
        {
            collected = false;
            wantsPickup = false;
            if (anim != null)
            {
                anim.SetTrigger("Idle"); //Eilaf
                anim.SetBool("Collected", false); //Eilaf
                anim.SetBool("Cab", false); //Eilaf
            }

            transform.SetParent(null, false);
            transform.localScale = originalWorldScale;
            transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            if (ownCollider != null)
                ownCollider.enabled = true;
            transform.position = GroundPoint(streetPosition);
            pickupPoint = FindStreetPickupPoint();
            if (pickupArea != null)
            {
                pickupArea.transform.position = pickupPoint;
                pickupArea.SetActive(false);
            }
            walkTarget = SelectWalkTarget();
            targetTimeout = Time.time + 14f;
            blockedSince = 0f;
            pauseUntil = Time.time + Random.Range(0.5f, 2f);
            nextRequestTime = Time.time + Random.Range(requestDelayMin, requestDelayMax);
        }

        private void Start()
        {
            Configure();
        }

        private void Update()
        {
            if (collected)
                return;

            UpdateLife();

            if (!wantsPickup)
                return;

            Collider[] hits = Physics.OverlapSphere(pickupPoint, pickupRadius,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (Collider hit in hits)
            {
                if (hit == ownCollider)
                    continue;

                TaxiController taxi = hit.GetComponentInParent<TaxiController>();
                if (taxi == null)
                    continue;

                taxi.TryPickUpNpc(this);
                return;
            }
        }

        private void UpdateLife()
        {
            float now = Time.time;
            if (wantsPickup)
            {
                pickupPoint = FindStreetPickupPoint();
                pickupArea.transform.position = pickupPoint;
                if (now >= requestEndTime)
                {
                    wantsPickup = false;
                    pickupArea.SetActive(false);
                    if (anim != null)
                        anim.SetBool("Cab", false); //Eilaf

                    nextRequestTime = now + Random.Range(requestDelayMin, requestDelayMax);
                }
                return;
            }

            if (now >= nextRequestTime)
            {
                nextRequestTime = now + Random.Range(requestDelayMin, requestDelayMax);
                if (Random.value <= requestChance)
                {
                    pickupPoint = FindStreetPickupPoint();
                    if (!hasPickupPoint)
                        return;
                    pickupArea.transform.position = pickupPoint;
                    wantsPickup = true;
                    if (anim != null)
                        anim.SetBool("Cab", true); //Eilaf
                    requestEndTime = now + requestDuration;
                    pickupArea.SetActive(true);
                    return;
                }
            }

            if (now < pauseUntil)
                return;

            Vector3 direction = walkTarget - transform.position;
            direction.y = 0f;
            float distance = direction.magnitude;
            if (distance < 0.35f || now >= targetTimeout)
            {
                walkTarget = SelectWalkTarget();
                targetTimeout = now + Random.Range(6f, 14f);
                pauseUntil = now + Random.Range(0.8f, 3f);
                blockedSince = 0f;
                return;
            }

            Vector3 heading = direction / distance;
            float step = Mathf.Min(walkSpeed * Time.deltaTime, distance);
            if (IsBlocked(heading, step + 0.25f))
            {
                if (blockedSince <= 0f)
                    blockedSince = now;
                if (now - blockedSince > 1.25f)
                {
                    walkTarget = SelectWalkTarget();
                    targetTimeout = now + Random.Range(5f, 10f);
                    blockedSince = 0f;
                }
                return;
            }

            blockedSince = 0f;
            transform.position = GroundPoint(transform.position + heading * step);
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(heading), 7f * Time.deltaTime);
        }

        private void CacheRoads()
        {
            roadColliders.Clear();
            waypoints.Clear();
            Collider[] colliders = FindObjectsByType<Collider>(FindObjectsSortMode.None);
            foreach (Collider candidate in colliders)
            {
                if (candidate == ownCollider || !candidate.enabled || candidate.isTrigger || !candidate.gameObject.name.Contains("Road"))
                    continue;

                roadColliders.Add(candidate);
                Bounds bounds = candidate.bounds;
                BoxCollider box = candidate as BoxCollider;
                Bounds rect = box != null ? new Bounds(box.center, box.size) : bounds;
                bool alongX = rect.size.x >= rect.size.z;
                float length = alongX ? bounds.size.x : bounds.size.z;
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / 4f));
                for (int i = 0; i <= steps; i++)
                {
                    for (int side = 0; side < 2; side++)
                    {
                        Vector3 sample = alongX
                            ? new Vector3(Mathf.Lerp(rect.min.x, rect.max.x, (float)i / steps), rect.max.y, side == 0 ? rect.min.z : rect.max.z)
                            : new Vector3(side == 0 ? rect.min.x : rect.max.x, rect.max.y, Mathf.Lerp(rect.min.z, rect.max.z, (float)i / steps));
                        if (box != null)
                            sample = box.transform.TransformPoint(sample);
                        Vector3 point;
                        if (TryRoadPoint(candidate, sample, 0.6f, out point))
                            AddWaypoint(candidate, point);
                    }
                }
            }
        }

        private void AddWaypoint(Collider road, Vector3 point)
        {
            waypoints.Add(new Waypoint { road = road, point = point });
        }

        private Vector3 SelectWalkTarget()
        {
            List<int> candidates = new List<int>();
            Vector3 feet = transform.position - Vector3.up * GroundOffset();
            Vector3 nearest = feet;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < waypoints.Count; i++)
            {
                Vector3 delta = waypoints[i].point - feet;
                delta.y = 0f;
                float distance = delta.magnitude;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = waypoints[i].point;
                }
                if (distance > 0.5f && distance <= Mathf.Min(streetSearchRadius, 7f) &&
                    Vector3.Distance(waypoints[i].point, previousTarget) > 0.75f &&
                    IsRoadConnection(feet, waypoints[i].point) && !IsBlocked(delta.normalized, distance))
                    candidates.Add(i);
            }

            previousTarget = walkTarget;
            if (candidates.Count == 0)
            {
                Vector3 delta = nearest - feet;
                delta.y = 0f;
                return nearestDistance <= streetSearchRadius && !IsBlocked(delta.normalized, delta.magnitude)
                    ? nearest : feet;
            }

            return waypoints[candidates[Random.Range(0, candidates.Count)]].point;
        }

        private Vector3 FindStreetPickupPoint()
        {
            Vector3 nearest = transform.position;
            float nearestDistance = streetSearchRadius * streetSearchRadius;
            hasPickupPoint = false;
            foreach (Collider road in roadColliders)
            {
                Vector3 point;
                if (!TryRoadSidePoint(road, transform.position, pickupRadius + 0.05f, out point))
                    continue;
                float distance = (point - transform.position).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearest = point;
                    nearestDistance = distance;
                    hasPickupPoint = true;
                }
            }
            return nearest;
        }

        private bool TryRoadSidePoint(Collider road, Vector3 position, float inset, out Vector3 point)
        {
            BoxCollider box = road as BoxCollider;
            if (box == null)
                return TryRoadPoint(road, position, inset, out point);

            Bounds rect = new Bounds(box.center, box.size);
            Vector3 scale = box.transform.lossyScale;
            Vector3 local = box.transform.InverseTransformPoint(position);
            float insetX = inset / Mathf.Max(0.0001f, Mathf.Abs(scale.x));
            float insetZ = inset / Mathf.Max(0.0001f, Mathf.Abs(scale.z));
            if (rect.size.x < insetX * 2f || rect.size.z < insetZ * 2f)
            {
                point = position;
                return false;
            }

            bool alongX = rect.size.x >= rect.size.z;
            float along = alongX
                ? Mathf.Clamp(local.x, rect.min.x + insetX, rect.max.x - insetX)
                : Mathf.Clamp(local.z, rect.min.z + insetZ, rect.max.z - insetZ);
            float sideMin = alongX ? rect.min.z + insetZ : rect.min.x + insetX;
            float sideMax = alongX ? rect.max.z - insetZ : rect.max.x - insetX;
            float currentSide = alongX ? local.z : local.x;
            float side = Mathf.Abs(currentSide - sideMin) <= Mathf.Abs(currentSide - sideMax) ? sideMin : sideMax;

            local.x = alongX ? along : side;
            local.z = alongX ? side : along;
            local.y = rect.max.y;
            point = box.transform.TransformPoint(local);
            point.y += 0.04f;
            return true;
        }

        private bool TryRoadPoint(Collider road, Vector3 position, float inset, out Vector3 point)
        {
            BoxCollider box = road as BoxCollider;
            Bounds rect = box != null ? new Bounds(box.center, box.size) : road.bounds;
            Vector3 scale = box != null ? box.transform.lossyScale : Vector3.one;
            Vector3 local = box != null ? box.transform.InverseTransformPoint(position) : position;
            float insetX = inset / Mathf.Max(0.0001f, Mathf.Abs(scale.x));
            float insetZ = inset / Mathf.Max(0.0001f, Mathf.Abs(scale.z));
            point = position;
            if (rect.size.x < insetX * 2f || rect.size.z < insetZ * 2f)
                return false;
            local.x = Mathf.Clamp(local.x, rect.min.x + insetX, rect.max.x - insetX);
            local.z = Mathf.Clamp(local.z, rect.min.z + insetZ, rect.max.z - insetZ);
            local.y = rect.max.y;
            point = box != null ? box.transform.TransformPoint(local) : local;
            if (box == null)
            {
                RaycastHit hit;
                if (!road.Raycast(new Ray(point + Vector3.up, Vector3.down), out hit, 3f))
                    return false;
                point = hit.point;
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * Mathf.PI / 4f;
                    Vector3 edge = point + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * inset;
                    if (!road.Raycast(new Ray(edge + Vector3.up, Vector3.down), out hit, 3f))
                        return false;
                }
            }
            point.y += 0.04f;
            return true;
        }

        private bool IsRoadConnection(Vector3 start, Vector3 end)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(start, end) / 0.5f));
            for (int i = 0; i <= steps; i++)
            {
                Vector3 sample = Vector3.Lerp(start, end, (float)i / steps);
                bool covered = false;
                foreach (Collider road in roadColliders)
                {
                    Vector3 point;
                    if (!TryRoadPoint(road, sample, 0.35f, out point))
                        continue;
                    Vector3 difference = point - sample;
                    difference.y = 0f;
                    if (difference.sqrMagnitude < 0.04f)
                    {
                        covered = true;
                        break;
                    }
                }
                if (!covered)
                    return false;
            }
            return true;
        }

        private Vector3 GroundPoint(Vector3 point)
        {
            foreach (Collider road in roadColliders)
            {
                Vector3 surface;
                if (TryRoadPoint(road, point, 0f, out surface))
                {
                    Vector3 delta = surface - point;
                    delta.y = 0f;
                    if (delta.sqrMagnitude < 0.01f)
                    {
                        point.y = surface.y + GroundOffset();
                        return point;
                    }
                }
            }
            RaycastHit[] hits = Physics.RaycastAll(point + Vector3.up * 3f, Vector3.down, 10f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float highest = float.NegativeInfinity;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<TaxiController>() != null)
                    continue;
                if (hit.normal.y > 0.5f && hit.point.y > highest)
                    highest = hit.point.y;
            }
            if (!float.IsNegativeInfinity(highest))
                point.y = highest + 0.04f + GroundOffset();
            return point;
        }

        private float GroundOffset()
        {
            if (ownCollider == null)
                return 1.04f;
            CapsuleCollider capsule = ownCollider as CapsuleCollider;
            if (capsule != null)
            {
                Vector3 scale = transform.lossyScale;
                Vector3 axis = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
                float axisScale = capsule.direction == 0 ? Mathf.Abs(scale.x) : capsule.direction == 1 ? Mathf.Abs(scale.y) : Mathf.Abs(scale.z);
                float radiusScale = capsule.direction == 0 ? Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)) :
                    capsule.direction == 1 ? Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)) : Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
                float radius = capsule.radius * radiusScale;
                float extent = Mathf.Abs(transform.TransformDirection(axis).y) * Mathf.Max(0f, capsule.height * axisScale * 0.5f - radius) + radius;
                return extent - transform.TransformVector(capsule.center).y;
            }
            return transform.position.y - ownCollider.bounds.min.y;
        }

        private bool IsBlocked(Vector3 direction, float distance)
        {
            if (distance <= 0f || direction.sqrMagnitude < 0.001f)
                return false;
            float radius = ownCollider != null ? Mathf.Max(0.2f, Mathf.Min(ownCollider.bounds.extents.x, ownCollider.bounds.extents.z) * 0.9f) : 0.35f;
            Vector3 feet = transform.position - Vector3.up * GroundOffset();
            for (int level = 0; level < 2; level++)
            {
                Vector3 origin = feet + Vector3.up * (radius + 0.1f + level * 0.7f);
                RaycastHit[] hits = Physics.SphereCastAll(origin, radius, direction, distance,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                foreach (RaycastHit hit in hits)
                {
                    if (hit.collider == ownCollider || hit.collider.transform.IsChildOf(transform) ||
                        hit.collider.bounds.max.y <= feet.y + 0.1f || IsIgnoredCollider(hit.collider))
                        continue;
                    return true;
                }
            }
            return false;
        }

        private bool IsIgnoredCollider(Collider collider)
        {
            if (collider.GetComponentInParent<TaxiController>() != null)
                return true;
            foreach (Collider road in roadColliders)
            {
                if (collider == road)
                    return true;
            }
            return false;
        }

        private void CreatePickupArea()
        {
            pickupArea = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pickupArea.name = $"{name} Street Pickup Area";
            pickupArea.transform.position = pickupPoint;
            pickupArea.transform.localScale = new Vector3(pickupRadius * 2f, 0.025f, pickupRadius * 2f);
            Collider areaCollider = pickupArea.GetComponent<Collider>();
            if (areaCollider != null)
                Destroy(areaCollider);
            Renderer renderer = pickupArea.GetComponent<Renderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            pickupMaterial = new Material(shader);
            pickupMaterial.color = pickupAreaColor;
            renderer.sharedMaterial = pickupMaterial;
        }

        private void OnDestroy()
        {
            if (pickupArea != null)
                Destroy(pickupArea);
            if (pickupMaterial != null)
                Destroy(pickupMaterial);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(pickupPoint == Vector3.zero ? transform.position : pickupPoint, pickupRadius);
        }
    }
}
