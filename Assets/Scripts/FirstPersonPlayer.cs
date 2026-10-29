using UnityEngine;
using UnityEngine.InputSystem;

namespace ClownTaxi.Runtime
{
    [DisallowMultipleComponent]
    public sealed class FirstPersonPlayer : MonoBehaviour
    {
        [Header("==== MOVEMENT ====")]
        [SerializeField] private float walkSpeed = 5f;
        [SerializeField] private float gravity = -24f;
        [SerializeField] private float interactionDistance = 5f;

        [Header("==== LOOK ====")]
        [SerializeField] private float lookSensitivity = 0.08f;
        [SerializeField] private float lookLimit = 85f;

        [Header("==== REFERENCES ====")]
        [SerializeField] private Camera viewCamera;
        [SerializeField] public GameObject UI;

        private CharacterController controller;
        private Renderer[] bodyRenderers;
        private float verticalVelocity;
        private float pitch;
        private bool driving;
        private bool pickupCutscene;
        private System.Action pickupCutsceneFinished;
        private string pickupMessage;

        public Camera ViewCamera => viewCamera;
        public bool IsDriving => driving;

        public void Configure(Camera camera)
        {
            viewCamera = camera;
            controller = GetComponent<CharacterController>();
            if (controller == null)
                controller = gameObject.AddComponent<CharacterController>();

            controller.height = 1.8f;
            controller.radius = 0.35f;
            controller.center = Vector3.up * 0.9f;
            controller.stepOffset = 0.35f;
            controller.slopeLimit = 50f;

            CapsuleCollider oldCollider = GetComponent<CapsuleCollider>();
            if (oldCollider != null)
                oldCollider.enabled = false;

            bodyRenderers = GetComponentsInChildren<Renderer>(true);
            viewCamera.transform.SetParent(transform);
            viewCamera.transform.localPosition = new Vector3(0f, 0.72f, 0.08f);
            viewCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            viewCamera.enabled = true;

            AudioListener listener = viewCamera.GetComponent<AudioListener>();
            if (listener == null)
                listener = viewCamera.gameObject.AddComponent<AudioListener>();
            listener.enabled = true;

            SetBodyVisible(false);
            Debug.Log($"[FirstPersonPlayer] Configured on {gameObject.name}");
        }

        private void OnEnable()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Update()
        {
            if (driving)
            {
                if (pickupCutscene)
                    return;
                if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                    TaxiController.Instance?.TryExit(this);
                return;
            }

            if (controller == null || viewCamera == null)
                return;

            Look();
            Move();

            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                Debug.Log("[FirstPersonPlayer] E key pressed, attempting to enter taxi");
                TryEnterTaxi();
            }
        }

        private void TryEnterTaxi()
        {
            if (TaxiController.Instance == null)
            {
                Debug.Log("[FirstPersonPlayer] No taxi instance found!");
                return;
            }

            float distance = Vector3.Distance(transform.position, TaxiController.Instance.transform.position);
            Debug.Log($"[FirstPersonPlayer] Distance to taxi: {distance:F2}m (max: {interactionDistance}m)");
            
            if (distance <= interactionDistance)
            {
                TaxiController.Instance.TryEnter(this);
            }
            else
            {
                Debug.Log($"[FirstPersonPlayer] Too far from taxi");
            }
        }

        public void SetDriving(bool value, Transform seat, TaxiController taxi)
        {
            Debug.Log($"[FirstPersonPlayer] SetDriving called: value={value}");
            driving = value;
            verticalVelocity = 0f;
            if (controller != null)
                controller.enabled = false;

            if (value)
            {
                transform.SetParent(seat, false);
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
                viewCamera.transform.SetParent(taxi.CameraSeat);
                viewCamera.transform.localPosition = new Vector3(0f, 0.05f, 0f);
                viewCamera.transform.localRotation = Quaternion.identity;
                SetBodyVisible(false);
            }
            else
            {
                transform.SetParent(null);
                transform.SetPositionAndRotation(taxi.ExitPoint.position, taxi.ExitPoint.rotation);
                viewCamera.transform.SetParent(transform, false);
                viewCamera.transform.localPosition = new Vector3(0f, 0.72f, 0.08f);
                viewCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                SetBodyVisible(false);
                if (controller != null)
                    controller.enabled = true;
            }
        }

        private void SetBodyVisible(bool visible)
        {
            if (bodyRenderers == null)
                return;

            foreach (Renderer bodyRenderer in bodyRenderers)
                bodyRenderer.enabled = visible;
        }

        private void Look()
        {
            if (pickupCutscene)
                return;
            if (Mouse.current == null)
                return;

            Vector2 delta = Mouse.current.delta.ReadValue() * lookSensitivity;
            pitch = Mathf.Clamp(pitch - delta.y, -lookLimit, lookLimit);
            transform.Rotate(Vector3.up * delta.x);
            viewCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        public void BeginPickupCutscene(PassengerNpc passenger, string destinationName, System.Action finished)
        {
            if (viewCamera == null || passenger == null)
                return;

            pickupCutscene = true;
            pickupCutsceneFinished = finished;
            pickupMessage = $"Deliver me to {destinationName}";
            StartCoroutine(PickupCutscene(passenger.transform));
        }

        private System.Collections.IEnumerator PickupCutscene(Transform passenger)
        {
            Transform originalParent = viewCamera.transform.parent;
            Vector3 originalPosition = viewCamera.transform.localPosition;
            Quaternion originalRotation = viewCamera.transform.localRotation;
            Transform taxi = TaxiController.Instance.transform;
            viewCamera.transform.SetParent(taxi, false);
            viewCamera.transform.localPosition = new Vector3(0f, 1.35f, -2.7f);
            float elapsed = 0f;
            while (elapsed < 2.8f)
            {
                elapsed += Time.deltaTime;
                Vector3 target = passenger.position + Vector3.up * 0.75f;
                viewCamera.transform.rotation = Quaternion.Slerp(viewCamera.transform.rotation, Quaternion.LookRotation(target - viewCamera.transform.position), 8f * Time.deltaTime);
                passenger.localRotation = Quaternion.Euler(0f, Mathf.Sin(elapsed * 7f) * 12f, Mathf.Sin(elapsed * 9f) * 5f);
                passenger.localPosition = new Vector3(0f, Mathf.Sin(elapsed * 8f) * 0.06f, 0f);
                yield return null;
            }

            passenger.localPosition = Vector3.zero;
            passenger.localRotation = Quaternion.identity;
            viewCamera.transform.SetParent(originalParent, false);
            viewCamera.transform.localPosition = originalPosition;
            viewCamera.transform.localRotation = originalRotation;
            pickupCutscene = false;
            pickupCutsceneFinished?.Invoke();
            pickupCutsceneFinished = null;
        }

        private void OnGUI()
        {
            if (!pickupCutscene || string.IsNullOrEmpty(pickupMessage))
                return;

            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(Screen.width * 0.5f - 240f, Screen.height * 0.22f, 480f, 42f), pickupMessage, style);
        }

        private void Move()
        {
            Vector2 input = Vector2.zero;
            if (Keyboard.current != null)
            {
                input.x = (Keyboard.current.dKey.isPressed ? 1f : 0f) - (Keyboard.current.aKey.isPressed ? 1f : 0f);
                input.y = (Keyboard.current.wKey.isPressed ? 1f : 0f) - (Keyboard.current.sKey.isPressed ? 1f : 0f);
            }

            Vector3 direction = (transform.right * input.x + transform.forward * input.y).normalized;
            if (controller.isGrounded && verticalVelocity < 0f)
                verticalVelocity = -2f;

            verticalVelocity += gravity * Time.deltaTime;
            controller.Move((direction * walkSpeed + Vector3.up * verticalVelocity) * Time.deltaTime);
        }
    }
}
