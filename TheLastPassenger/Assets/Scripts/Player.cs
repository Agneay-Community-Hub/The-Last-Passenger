using UnityEngine;

namespace TLP
{
    /// <summary>
    /// First-person conductor. WASD to walk, mouse to look, E / left click to interact,
    /// hold SHIFT to glance at the rear-view mirror, TAB (or M) for the manifest, ESC to pause.
    /// There is no sprint: on this bus, SHIFT shifts your eyes to the mirror.
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        public static PlayerController I;
        public Camera cam;
        public CharacterController cc;
        public Transform reflection;       // mirror-only body that follows the player
        public float walkSpeed = 2.0f;
        public bool inputEnabled = true;
        public bool mirrorHeld, forceMirror;
        public Interactable focus;
        public float yaw, pitch;
        float vy, bobT, stepT, sway;
        public float lookOverrideWeight; // cinematic camera control
        public Settings settings;
        public float lookTravel, moveTravel;
        public float shakeT;
        public void Shake(float amount) { shakeT = Mathf.Max(shakeT, amount); }   // for the tutorial
        public int interactions;
        Passenger reflPassenger;

        void Awake() { I = this; }

        public void Teleport(Vector3 pos, float yawDeg)
        {
            cc.enabled = false;
            transform.position = pos;
            yaw = yawDeg; pitch = 0;
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            cc.enabled = true;
        }

        void Update()
        {
            var ui = UIManager.I;
            bool modal = ui != null && ui.Modal;
            bool canControl = inputEnabled && !modal;

            if (canControl)
            {
                float sens = settings != null ? settings.sensitivity : 2f;
                float my = Input.GetAxisRaw("Mouse Y") * sens * (settings != null && settings.invertY ? -1 : 1);
                float mx = Input.GetAxisRaw("Mouse X") * sens;
                yaw += mx;
                lookTravel += Mathf.Abs(mx) + Mathf.Abs(my);
                pitch = Mathf.Clamp(pitch - my, -80f, 80f);
            }
            transform.rotation = Quaternion.Euler(0, yaw, 0);

            // Movement
            Vector3 move = Vector3.zero;
            if (canControl)
            {
                float h = Input.GetAxisRaw("Horizontal"), v = Input.GetAxisRaw("Vertical");
                move = (transform.right * h + transform.forward * v);
                if (move.sqrMagnitude > 1) move.Normalize();
            }
            if (cc.isGrounded) vy = -1f; else vy -= 9.81f * Time.deltaTime;
            if (cc.enabled) cc.Move((move * walkSpeed + Vector3.up * vy) * Time.deltaTime);
            moveTravel += move.magnitude * walkSpeed * Time.deltaTime;

            // Footsteps / head bob
            bool reduce = settings != null && settings.reduceMotion;
            float moving = move.magnitude;
            bobT += Time.deltaTime * 8f * moving;
            if (moving > 0.1f)
            {
                stepT += Time.deltaTime * moving;
                if (stepT > 0.48f && AudioDirector.I) { stepT = 0; AudioDirector.I.PlayAt(AudioDirector.I.step, transform.position, 0.35f, Random.Range(0.9f, 1.1f)); }
            }
            float bob = reduce ? 0 : Mathf.Sin(bobT) * 0.03f * moving;

            // Gentle bus sway from road speed
            float spd = RouteManager.I ? RouteManager.I.speed : 0;
            float target = reduce ? 0 : Mathf.Sin(Time.time * 0.9f) * 0.6f * Mathf.Clamp01(spd / 9f) + Mathf.PerlinNoise(Time.time * 2.1f, 0.3f) * 0.3f * Mathf.Clamp01(spd / 9f);
            sway = Mathf.Lerp(sway, target, Time.deltaTime * 2f);

            if (lookOverrideWeight <= 0)
            {
                shakeT = Mathf.Max(0, shakeT - Time.unscaledDeltaTime);
                float sk = reduce ? shakeT * 0.3f : shakeT;
                var jitter = new Vector3(Mathf.PerlinNoise(Time.time * 40f, 1) - 0.5f, Mathf.PerlinNoise(Time.time * 40f, 7) - 0.5f, 0) * sk * 0.12f;
                cam.transform.localPosition = new Vector3(0, 1.62f + bob, 0) + jitter;
                cam.transform.localRotation = Quaternion.Euler(pitch + jitter.y * 40f, jitter.x * 30f, sway + jitter.x * 40f);
            }

            // Mirror glance and manifest
            mirrorHeld = (canControl && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) || forceMirror;

            // Interaction
            focus = null;
            if (canControl && cam.enabled)
            {
                var ray = new Ray(cam.transform.position, cam.transform.forward);
                int mask = ~((1 << B.MirrorLayer) | (1 << 2));
                if (Physics.Raycast(ray, out var hit, 2.4f, mask, QueryTriggerInteraction.Collide))
                {
                    var it = hit.collider.GetComponentInParent<Interactable>();
                    if (it && it.Usable) focus = it;
                }
                if (focus && (Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(0)))
                {
                    var f = focus;
                    interactions++;
                    f.onUse?.Invoke();
                }
            }

            if (reflection)
            {
                reflection.position = transform.position;
                reflection.rotation = Quaternion.Euler(0, yaw, 0);
                if (!reflPassenger) reflPassenger = reflection.GetComponent<Passenger>();
                if (reflPassenger) reflPassenger.state = moving > 0.1f ? PState.Walking : PState.Standing;
            }
        }

        public void Aim(Vector3 world)
        {
            var d = world - cam.transform.position;
            yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -80, 80);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            cam.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
        }

        /// <summary>True if the camera is pointing within 'deg' degrees of a world point.</summary>
        public bool LookingAt(Vector3 world, float deg)
        {
            var d = world - cam.transform.position;
            return Vector3.Angle(cam.transform.forward, d) < deg;
        }
    }
}
