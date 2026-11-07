using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TLP
{
    public enum PState { Hidden, Waiting, Walking, Seated, Standing, Gone }

    /// <summary>A passenger with a persistent identity, seat, ticket and boarding state.</summary>
    public class Passenger : MonoBehaviour
    {
        public string id, displayName, destination, seatLabel;
        public bool child, isDriver;
        public int lightIndex = -1;
        public Vector3 seatPos;
        public Transform body, head;
        public Animator anim;
        public Vector3 sitOffset = new Vector3(0, -0.27f, 0);
        public Interactable interact;
        public PState state = PState.Hidden;
        public bool ticketed, needsTicket;
        public float walkSpeed = 1.15f;
        public PersonBuilder.Recipe recipe;

        public static readonly Vector3 FrontOutside = new Vector3(-2.6f, -0.15f, 4.6f);
        public static readonly Vector3 FrontDoor = new Vector3(-0.95f, 0f, 4.6f);
        public static readonly Vector3 FrontAisle = new Vector3(0f, 0f, 3.95f);
        public static readonly Vector3 RearOutside = new Vector3(-2.6f, -0.15f, -2.6f);
        public static readonly Vector3 RearDoor = new Vector3(-0.95f, 0f, -2.6f);
        public static readonly Vector3 RearAisle = new Vector3(0f, 0f, -2.6f);

        public bool leaving, named;
        public bool Aboard => !leaving && (state == PState.Seated || state == PState.Standing || state == PState.Walking);

        Vector3? lookTarget; Vector3 lastLook; float lookW; float talkUntil;

        public void Hide() { state = PState.Hidden; leaving = false; gameObject.SetActive(false); }

        public void Sit(bool s)
        {
            if (body) body.localPosition = s ? sitOffset : Vector3.zero;
        }

        /// <summary>Plays the talking animation for a few seconds.</summary>
        public void Talk(float seconds = 3f) { talkUntil = Time.time + seconds; }

        void Update()
        {
            if (!anim) return;
            int pose;
            bool talking = Time.time < talkUntil;
            if (state == PState.Walking) pose = 1;
            else if (state == PState.Seated) pose = isDriver ? 5 : talking ? 3 : 2;
            else pose = talking ? 4 : 0;
            anim.SetInteger("Pose", pose);
        }

        void LateUpdate()
        {
            if (!anim || !head) return;
            lookW = Mathf.MoveTowards(lookW, lookTarget.HasValue ? 1f : 0f, Time.deltaTime * 2.5f);
            if (lookTarget.HasValue) lastLook = lookTarget.Value;
            if (lookW <= 0.001f) return;
            var dir = lastLook - head.position;
            dir.y *= 0.6f;
            if (dir.sqrMagnitude < 0.01f) return;
            var delta = Quaternion.FromToRotation(transform.forward, dir.normalized);
            float ang = Quaternion.Angle(Quaternion.identity, delta);
            if (ang > 75f) delta = Quaternion.Slerp(Quaternion.identity, delta, 75f / ang);
            head.rotation = Quaternion.Slerp(Quaternion.identity, delta, lookW) * head.rotation;
        }

        public void PlaceSeated()
        {
            gameObject.SetActive(true);
            transform.position = seatPos;
            transform.rotation = Quaternion.identity;
            Sit(true);
            state = PState.Seated;
        }

        public void PlaceAt(Vector3 p, float yaw, PState s)
        {
            gameObject.SetActive(true);
            transform.position = p;
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            Sit(s == PState.Seated);
            state = s;
        }

        public void LookAt(Vector3 world)
        {
            if (!head) return;
            if (anim) { lookTarget = world; return; }
            var d = world - head.position;
            d.y *= 0.5f;
            if (d.sqrMagnitude < 0.01f) return;
            var local = Quaternion.Inverse(transform.rotation) * Quaternion.LookRotation(d);
            var e = local.eulerAngles;
            float yaw = Mathf.DeltaAngle(0, e.y);
            yaw = Mathf.Clamp(yaw, -80, 80);
            head.localRotation = Quaternion.Euler(Mathf.Clamp(Mathf.DeltaAngle(0, e.x), -20, 20), yaw, 0);
        }

        public void LookForward()
        {
            lookTarget = null;
            if (head && !anim) head.localRotation = Quaternion.identity;
        }

        public void FaceFlat(Vector3 world)
        {
            var d = world - transform.position; d.y = 0;
            if (d.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(d);
        }

        public IEnumerator Walk(params Vector3[] pts)
        {
            gameObject.SetActive(true);
            Sit(false);
            state = PState.Walking;
            float stepT = 0;
            foreach (var target in pts)
            {
                while (true)
                {
                    var pos = transform.position;
                    var d = target - pos;
                    var flat = new Vector3(d.x, 0, d.z);
                    if (flat.magnitude < 0.03f) { transform.position = target; break; }
                    float step = walkSpeed * Time.deltaTime;
                    var move = flat.normalized * Mathf.Min(step, flat.magnitude);
                    float yStep = Mathf.Abs(d.y) > 0.001f ? Mathf.Sign(d.y) * Mathf.Min(Mathf.Abs(d.y), step * Mathf.Abs(d.y) / Mathf.Max(flat.magnitude, 0.01f)) : 0;
                    transform.position = pos + move + new Vector3(0, yStep, 0);
                    var want = Quaternion.LookRotation(flat.normalized);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, want, 360 * Time.deltaTime);
                    if (body && !anim) body.localPosition = new Vector3(0, Mathf.Abs(Mathf.Sin(Time.time * 7f)) * 0.025f, 0);
                    stepT += Time.deltaTime;
                    if (stepT > 0.5f)
                    {
                        stepT = 0;
                        if (AudioDirector.I) AudioDirector.I.PlayAt(AudioDirector.I.step, transform.position, child ? 0.25f : 0.4f, Random.Range(0.85f, 1.15f));
                    }
                    yield return null;
                }
            }
            if (body) body.localPosition = Vector3.zero;
            state = PState.Standing;
        }

        public IEnumerator BoardFront()
        {
            yield return Walk(FrontDoor, FrontAisle, new Vector3(0, 0, seatPos.z), seatPos);
            transform.rotation = Quaternion.identity;
            Sit(true);
            state = PState.Seated;
        }

        public IEnumerator ExitFront(Vector3 awayPoint)
        {
            yield return Walk(new Vector3(0, 0, seatPos.z), FrontAisle, FrontDoor, FrontOutside, awayPoint);
            gameObject.SetActive(false);
            leaving = false;
            state = PState.Gone;
        }

        public IEnumerator ExitRear(Vector3 awayPoint)
        {
            yield return Walk(new Vector3(0, 0, seatPos.z), RearAisle, RearDoor, RearOutside, awayPoint);
            gameObject.SetActive(false);
            leaving = false;
            state = PState.Gone;
        }
    }

    public static class PersonBuilder
    {
        /// <summary>Everything needed to build (or copy) a person.</summary>
        public class Recipe
        {
            public string clothes, legs, skin, hair;
            public float scale = 1f;
            public bool child;
            public string model;                       // Quaternius character, or null for the greybox body
            public Dictionary<string, string> recolor; // model material name -> our material ("*" = every slot)
        }

        // Seated pose correction for the animated characters (hips onto the seat cushion).
        public static Vector3 ModelSitOffset = new Vector3(0, 0.0f, -0.12f);

        public static Passenger Make(Transform parent, string id, string displayName, string clothes, string legsMat, string skin, string hair, float scale, bool child)
            => Make(parent, id, displayName, new Recipe { clothes = clothes, legs = legsMat, skin = skin, hair = hair, scale = scale, child = child });

        public static Passenger Copy(Passenger src, Transform parent, string id) => Make(parent, id, src.displayName, src.recipe);

        public static Passenger Make(Transform parent, string id, string displayName, Recipe r)
        {
            var root = B.Empty(parent, "P_" + id, Vector3.zero);
            root.transform.localScale = Vector3.one * r.scale;
            var p = root.AddComponent<Passenger>();
            p.id = id; p.displayName = displayName; p.child = r.child; p.recipe = r;
            var body = B.Empty(root.transform, "Body", Vector3.zero).transform;
            p.body = body;

            var prefab = string.IsNullOrEmpty(r.model) ? null : GameAssets.Model(r.model);
            if (prefab && GameAssets.Animator)
            {
                // Quaternius characters face -Z after import; turn them to face +Z like everything else.
                var pivot = B.Empty(body, "ModelPivot", Vector3.zero, new Vector3(0, ModelYaw, 0)).transform;
                var m = Object.Instantiate(prefab, pivot, false);
                m.name = r.model;
                var anim = m.GetComponent<Animator>();
                if (!anim) anim = m.AddComponent<Animator>();
                anim.runtimeAnimatorController = GameAssets.Animator;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                p.anim = anim;
                p.head = anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
                p.sitOffset = ModelSitOffset;
                foreach (var rend in m.GetComponentsInChildren<Renderer>())
                {
                    rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    if (r.recolor == null) continue;
                    var mats = rend.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        string key = mats[i] ? mats[i].name.Replace(" (Instance)", "") : "";
                        if (r.recolor.TryGetValue(key, out var to) || r.recolor.TryGetValue("*", out to)) mats[i] = Mats.Get(to);
                    }
                    rend.sharedMaterials = mats;
                }
            }
            else
            {
                B.Box(body, "LegL", new Vector3(-0.09f, 0.33f, 0), new Vector3(0.15f, 0.66f, 0.2f), r.legs);
                B.Box(body, "LegR", new Vector3(0.09f, 0.33f, 0), new Vector3(0.15f, 0.66f, 0.2f), r.legs);
                B.Cap(body, "Torso", new Vector3(0, 1.03f, 0), new Vector3(0.42f, 0.4f, 0.28f), r.clothes);
                B.Box(body, "ArmL", new Vector3(-0.25f, 1.05f, 0.02f), new Vector3(0.1f, 0.58f, 0.12f), r.clothes, false, new Vector3(0, 0, 4));
                B.Box(body, "ArmR", new Vector3(0.25f, 1.05f, 0.02f), new Vector3(0.1f, 0.58f, 0.12f), r.clothes, false, new Vector3(0, 0, -4));
                B.Box(body, "HandL", new Vector3(-0.27f, 0.74f, 0.03f), new Vector3(0.08f, 0.1f, 0.09f), r.skin);
                B.Box(body, "HandR", new Vector3(0.27f, 0.74f, 0.03f), new Vector3(0.08f, 0.1f, 0.09f), r.skin);
                B.Cyl(body, "Neck", new Vector3(0, 1.43f, 0), new Vector3(0.1f, 0.06f, 0.1f), r.skin);
                var head = B.Empty(body, "Head", new Vector3(0, 1.58f, 0)).transform;
                p.head = head;
                B.Sph(head, "Face", Vector3.zero, new Vector3(0.22f, 0.26f, 0.23f), r.skin);
                B.Sph(head, "Hair", new Vector3(0, 0.05f, -0.025f), new Vector3(0.235f, 0.22f, 0.235f), r.hair);
                B.Sph(head, "EyeL", new Vector3(-0.045f, 0.01f, 0.105f), new Vector3(0.03f, 0.02f, 0.02f), "Black");
                B.Sph(head, "EyeR", new Vector3(0.045f, 0.01f, 0.105f), new Vector3(0.03f, 0.02f, 0.02f), "Black");
            }
            var col = root.AddComponent<CapsuleCollider>();
            col.isTrigger = true;
            col.center = new Vector3(0, 0.95f, 0);
            col.height = 1.8f;
            col.radius = 0.28f;
            p.interact = root.AddComponent<Interactable>();
            p.interact.prompt = "";
            return p;
        }

        public static float ModelYaw = 180f;

        /// <summary>Attach a prop to a bone, keeping its world size. People without a skeleton keep it on the body.</summary>
        public static GameObject Attach(Passenger p, HumanBodyBones bone, GameObject prop, Vector3 worldOffset)
        {
            Transform t = p.anim && p.anim.isHuman ? p.anim.GetBoneTransform(bone) : null;
            if (!t) return prop;
            // Animated bones carry import scale, so props follow the bone instead of being parented to it.
            var f = prop.AddComponent<BoneFollow>();
            f.bone = t;
            var worldPos = t.position + p.transform.rotation * worldOffset;
            f.offset = Quaternion.Inverse(t.rotation) * (worldPos - t.position);
            f.rot = Quaternion.Inverse(t.rotation) * prop.transform.rotation;
            return prop;
        }
    }

    public class BoneFollow : MonoBehaviour
    {
        public Transform bone;
        public Vector3 offset;
        public Quaternion rot = Quaternion.identity;
        void LateUpdate()
        {
            if (!bone) return;
            transform.SetPositionAndRotation(bone.position + bone.rotation * offset, bone.rotation * rot);
        }
    }

    /// <summary>Runtime handles to the bus: doors, lights, display, mirror and story props.</summary>
    public class BusSystems : MonoBehaviour
    {
        public static BusSystems I;
        public Transform frontDoor, rearDoor;
        public Vector3 frontClosed, frontOpen, rearClosed, rearOpen;
        public bool frontIsOpen, rearIsOpen;
        public Light[] cabinLights;
        public Renderer[] tubes;
        public TextMesh routeDisplay, exteriorBoard;
        public Camera mirrorCam;
        public RenderTexture mirrorRT;
        public Light brakeLight, headLight;
        public Transform bellPoint, speakerPoint, rearSpeakerPoint, jumpSeat;
        public readonly Dictionary<string, Interactable> props = new Dictionary<string, Interactable>();
        public readonly Dictionary<string, GameObject> objs = new Dictionary<string, GameObject>();
        public float[] lightBase;
        bool[] lightOn;

        void Awake() { I = this; }

        public Interactable Prop(string key) => props.TryGetValue(key, out var i) ? i : null;
        public GameObject Obj(string key) => objs.TryGetValue(key, out var g) ? g : null;
        public void ShowObj(string key, bool on) { var g = Obj(key); if (g) g.SetActive(on); }

        public IEnumerator Door(bool front, bool open)
        {
            var t = front ? frontDoor : rearDoor;
            var from = t.localPosition;
            var to = front ? (open ? frontOpen : frontClosed) : (open ? rearOpen : rearClosed);
            if (front) frontIsOpen = open; else rearIsOpen = open;
            if (AudioDirector.I) AudioDirector.I.PlayAt(AudioDirector.I.door, t.position, 0.7f);
            float d = 1.0f;
            for (float x = 0; x < d; x += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0, 1, x / d);
                t.localPosition = Vector3.Lerp(from, to, k);
                yield return null;
            }
            t.localPosition = to;
        }

        public void SetDoorInstant(bool front, bool open)
        {
            if (front) { frontDoor.localPosition = open ? frontOpen : frontClosed; frontIsOpen = open; }
            else { rearDoor.localPosition = open ? rearOpen : rearClosed; rearIsOpen = open; }
        }

        public void SetLight(int i, bool on)
        {
            if (lightOn == null) { lightOn = new bool[cabinLights.Length]; for (int k = 0; k < lightOn.Length; k++) lightOn[k] = true; }
            if (i < 0 || i >= cabinLights.Length) return;
            lightOn[i] = on;
            cabinLights[i].enabled = on;
            cabinLights[i].intensity = lightBase[i];
            tubes[i].sharedMaterial = Mats.Get(on ? "TubeOn" : "TubeOff");
        }

        public void SetAllLights(bool on) { for (int i = 0; i < cabinLights.Length; i++) SetLight(i, on); }

        public void SetLightTint(Color c)
        {
            foreach (var l in cabinLights) l.color = c;
        }

        public IEnumerator Flicker(int i, float duration)
        {
            float end = Time.time + duration;
            while (Time.time < end)
            {
                bool on = Random.value > 0.45f;
                cabinLights[i].enabled = on;
                tubes[i].sharedMaterial = Mats.Get(on ? "TubeOn" : "TubeOff");
                yield return new WaitForSeconds(Random.Range(0.03f, 0.18f));
            }
            SetLight(i, true);
        }

        public void SetDisplay(string s) { if (routeDisplay) routeDisplay.text = s; }
        public void Brake(bool on) { if (brakeLight) brakeLight.intensity = on ? 2.2f : 0.35f; }
    }

    public static class BusBuilder
    {
        public const float HalfW = 1.25f, HalfL = 5.5f;

        static Interactable MakeInteract(BusSystems bus, GameObject g, string key, string prompt = "Inspect")
        {
            var c = g.GetComponent<Collider>();
            if (!c) { var bc = g.AddComponent<BoxCollider>(); bc.isTrigger = true; }
            else c.isTrigger = true;
            var i = g.AddComponent<Interactable>();
            i.prompt = prompt;
            bus.props[key] = i;
            bus.objs[key] = g;
            return i;
        }

        static void Seat(Transform t, string n, float x, float z, float w)
        {
            B.Box(t, n + "_Frame", new Vector3(x, 0.21f, z), new Vector3(w, 0.42f, 0.42f), "SeatFrame", true);
            B.Box(t, n + "_Cushion", new Vector3(x, 0.47f, z + 0.01f), new Vector3(w, 0.1f, 0.46f), "Seat");
            B.Box(t, n + "_Back", new Vector3(x, 0.84f, z - 0.23f), new Vector3(w, 0.62f, 0.08f), "Seat", true, new Vector3(-8, 0, 0));
            B.Rail(t, n + "_Grab", new Vector3(x - w * 0.5f + 0.06f, 1.18f, z - 0.27f), new Vector3(x + w * 0.5f - 0.06f, 1.18f, z - 0.27f), 0.022f, "Rail");
        }

        static void Side(Transform t, float s, (float z0, float z1)[] segs)
        {
            foreach (var (z0, z1) in segs)
            {
                float len = z1 - z0, mid = (z0 + z1) * 0.5f;
                B.Box(t, "WallLow", new Vector3(s * 1.21f, 0.475f, mid), new Vector3(0.08f, 0.95f, len), "Interior", true);
                B.Box(t, "SkinLow", new Vector3(s * 1.265f, 0.32f, mid), new Vector3(0.03f, 1.28f, len), "BusMaroon");
                B.Box(t, "Stripe", new Vector3(s * 1.285f, 0.88f, mid), new Vector3(0.02f, 0.09f, len), "BusCream");
                B.Box(t, "Sill", new Vector3(s * 1.17f, 0.965f, mid), new Vector3(0.12f, 0.03f, len), "SeatFrame");
                B.Box(t, "Glass", new Vector3(s * 1.235f, 1.45f, mid), new Vector3(0.02f, 1.0f, len), "Glass");
                B.Box(t, "GlassBlock", new Vector3(s * 1.235f, 1.45f, mid), new Vector3(0.02f, 1.0f, len), null, true).GetComponent<Renderer>().enabled = false;
                int n = Mathf.Max(1, Mathf.RoundToInt(len / 1.0f));
                for (int i = 0; i <= n; i++)
                {
                    float z = Mathf.Lerp(z0 + 0.04f, z1 - 0.04f, i / (float)n);
                    B.Box(t, "Pillar", new Vector3(s * 1.22f, 1.45f, z), new Vector3(0.07f, 1.0f, 0.07f), "Interior");
                    B.Box(t, "PillarOut", new Vector3(s * 1.26f, 1.45f, z), new Vector3(0.03f, 1.0f, 0.09f), "BusCream");
                }
            }
        }

        public static BusSystems Build(Transform t)
        {
            var bus = t.gameObject.AddComponent<BusSystems>();
            BusSystems.I = bus;

            // Shell
            B.Box(t, "Floor", new Vector3(0, -0.05f, 0), new Vector3(2.5f, 0.1f, 11f), "Floor", true);
            B.Box(t, "AisleRubber", new Vector3(0, 0.003f, -0.4f), new Vector3(0.62f, 0.006f, 9.8f), "Rubber");
            B.Box(t, "Under", new Vector3(0, -0.28f, 0), new Vector3(2.45f, 0.36f, 10.9f), "Black");
            B.Box(t, "Roof", new Vector3(0, 2.36f, 0), new Vector3(2.6f, 0.1f, 11.1f), "BusCream", true);
            B.Box(t, "Ceiling", new Vector3(0, 2.3f, 0), new Vector3(2.42f, 0.02f, 10.9f), "Ceiling");

            Side(t, -1, new[] { (-5.5f, -3.1f), (-2.1f, 4.2f), (5.0f, 5.5f) });
            Side(t, 1, new[] { (-5.5f, 5.5f) });
            // Upper band runs the whole length on both sides (over the doors too)
            B.Box(t, "BandL", new Vector3(-1.23f, 2.12f, 0), new Vector3(0.08f, 0.38f, 11f), "BusCream", true);
            B.Box(t, "BandR", new Vector3(1.23f, 2.12f, 0), new Vector3(0.08f, 0.38f, 11f), "BusCream", true);
            B.Box(t, "BandInL", new Vector3(-1.18f, 2.12f, 0), new Vector3(0.02f, 0.36f, 10.9f), "Interior");
            B.Box(t, "BandInR", new Vector3(1.18f, 2.12f, 0), new Vector3(0.02f, 0.36f, 10.9f), "Interior");

            // Front
            B.Box(t, "FrontLow", new Vector3(0, 0.5f, 5.48f), new Vector3(2.5f, 1.0f, 0.08f), "Dash", true);
            B.Box(t, "FrontSkin", new Vector3(0, 0.35f, 5.535f), new Vector3(2.56f, 1.3f, 0.03f), "BusMaroon");
            B.Box(t, "Windshield", new Vector3(0, 1.5f, 5.5f), new Vector3(2.42f, 1.0f, 0.02f), "Glass");
            B.Box(t, "WindshieldBlock", new Vector3(0, 1.5f, 5.5f), new Vector3(2.42f, 1.0f, 0.02f), null, true).GetComponent<Renderer>().enabled = false;
            B.Box(t, "FrontPillarMid", new Vector3(0, 1.5f, 5.5f), new Vector3(0.06f, 1.0f, 0.05f), "BusCream");
            B.Box(t, "FrontHeader", new Vector3(0, 2.15f, 5.48f), new Vector3(2.5f, 0.32f, 0.08f), "BusCream");
            B.Box(t, "BoardBack", new Vector3(0, 2.15f, 5.535f), new Vector3(1.9f, 0.24f, 0.02f), "Black");
            bus.exteriorBoard = B.Text(t, "ExtBoard", "117  CENTRAL BUS STAND", new Vector3(0, 2.15f, 5.55f), new Vector3(0, 180, 0), 0.13f, new Color(1f, 0.55f, 0.1f));
            B.Sph(t, "HeadlampL", new Vector3(-0.9f, 0.35f, 5.55f), new Vector3(0.22f, 0.22f, 0.06f), "Headlight");
            B.Sph(t, "HeadlampR", new Vector3(0.9f, 0.35f, 5.55f), new Vector3(0.22f, 0.22f, 0.06f), "Headlight");
            var hl = B.Empty(t, "HeadLight", new Vector3(0, 0.9f, 5.8f), new Vector3(10, 0, 0)).AddComponent<Light>();
            hl.type = LightType.Spot; hl.range = 45; hl.spotAngle = 75; hl.intensity = 3f; hl.color = new Color(1f, 0.9f, 0.72f); hl.shadows = LightShadows.None;
            bus.headLight = hl;

            // Rear
            B.Box(t, "RearLow", new Vector3(0, 0.6f, -5.48f), new Vector3(2.5f, 1.2f, 0.08f), "Interior", true);
            B.Box(t, "RearSkin", new Vector3(0, 0.45f, -5.535f), new Vector3(2.56f, 1.5f, 0.03f), "BusMaroon");
            B.Box(t, "RearGlass", new Vector3(0, 1.55f, -5.5f), new Vector3(2.0f, 0.7f, 0.02f), "Glass");
            B.Box(t, "RearGlassBlock", new Vector3(0, 1.55f, -5.5f), new Vector3(2.0f, 0.7f, 0.02f), null, true).GetComponent<Renderer>().enabled = false;
            B.Box(t, "RearSideL", new Vector3(-1.12f, 1.55f, -5.48f), new Vector3(0.26f, 0.7f, 0.08f), "Interior");
            B.Box(t, "RearSideR", new Vector3(1.12f, 1.55f, -5.48f), new Vector3(0.26f, 0.7f, 0.08f), "Interior");
            B.Box(t, "RearTop", new Vector3(0, 2.1f, -5.48f), new Vector3(2.5f, 0.4f, 0.08f), "BusCream");
            B.Quad(t, "TailL", new Vector3(-0.95f, 0.4f, -5.56f), new Vector3(0.25f, 0.15f, 1), "Taillight", new Vector3(0, 0, 0));
            B.Quad(t, "TailR", new Vector3(0.95f, 0.4f, -5.56f), new Vector3(0.25f, 0.15f, 1), "Taillight", new Vector3(0, 0, 0));
            bus.brakeLight = B.PointLight(t, "BrakeLight", new Vector3(0, 0.5f, -6.3f), new Color(1f, 0.08f, 0.04f), 0.35f, 7f);

            // Wheels
            foreach (var x in new[] { -1.2f, 1.2f })
                foreach (var z in new[] { -3.6f, 3.4f })
                {
                    B.Cyl(t, "Wheel", new Vector3(x, -0.05f, z), new Vector3(0.9f, 0.12f, 0.9f), "Rubber", false, new Vector3(0, 0, 90));
                    B.Cyl(t, "Hub", new Vector3(x * 1.06f, -0.05f, z), new Vector3(0.35f, 0.02f, 0.35f), "Rail", false, new Vector3(0, 0, 90));
                }

            // Doors (slide along the outside of the body)
            bus.frontClosed = new Vector3(-1.27f, 1.0f, 4.6f);
            bus.frontOpen = new Vector3(-1.36f, 1.0f, 3.82f);
            bus.rearClosed = new Vector3(-1.27f, 1.0f, -2.6f);
            bus.rearOpen = new Vector3(-1.36f, 1.0f, -1.82f);
            bus.frontDoor = MakeDoor(t, "FrontDoor", bus.frontClosed).transform;
            bus.rearDoor = MakeDoor(t, "RearDoor", bus.rearClosed).transform;
            B.Box(t, "FrontStep", new Vector3(-1.15f, -0.02f, 4.6f), new Vector3(0.22f, 0.05f, 0.8f), "Rail");
            B.Box(t, "RearStep", new Vector3(-1.15f, -0.02f, -2.6f), new Vector3(0.22f, 0.05f, 1.0f), "Rail");

            // Seats: left side (door side), right side, rear bench
            float[] leftZ = { 3.0f, 2.0f, 1.0f, 0.0f, -1.0f, -3.8f };
            float[] rightZ = { 3.4f, 2.4f, 1.4f, 0.4f, -0.6f, -1.6f, -2.6f, -3.6f };
            foreach (var z in leftZ) Seat(t, "SeatL" + z, -0.8f, z, 0.78f);
            foreach (var z in rightZ) Seat(t, "SeatR" + z, 0.8f, z, 0.78f);
            Seat(t, "RearBench", 0, -4.95f, 2.38f);
            foreach (var z in new[] { 3.0f, 2.0f, 1.0f, 0.0f, -1.0f })
                B.Text(t, "Ladies", "LADIES", new Vector3(-1.165f, 2.12f, z), new Vector3(0, -90, 0), 0.05f, new Color(0.15f, 0.15f, 0.2f));

            // Rails and poles
            B.Rail(t, "CeilRailL", new Vector3(-0.42f, 2.04f, -5.2f), new Vector3(-0.42f, 2.04f, 4.3f), 0.025f, "Rail");
            B.Rail(t, "CeilRailR", new Vector3(0.42f, 2.04f, -5.2f), new Vector3(0.42f, 2.04f, 4.3f), 0.025f, "Rail");
            foreach (var z in new[] { 4.15f, 2.5f, -0.5f, -2.05f, -3.2f })
                B.Rail(t, "Pole", new Vector3(-0.44f, 0, z), new Vector3(-0.44f, 2.04f, z), 0.025f, "Rail");
            foreach (var z in new[] { 2.9f, -0.1f, -3.1f })
                B.Rail(t, "Pole", new Vector3(0.44f, 0, z), new Vector3(0.44f, 2.04f, z), 0.025f, "Rail");
            B.Rail(t, "BellCord", new Vector3(-1.1f, 2.08f, -5.2f), new Vector3(-1.1f, 2.08f, 5.2f), 0.008f, "Rubber");

            // Driver cabin
            B.Box(t, "DriverSeatFrame", new Vector3(0.75f, 0.21f, 4.85f), new Vector3(0.5f, 0.42f, 0.45f), "SeatFrame", true);
            B.Box(t, "DriverSeat", new Vector3(0.75f, 0.47f, 4.85f), new Vector3(0.5f, 0.1f, 0.46f), "Seat");
            B.Box(t, "DriverBack", new Vector3(0.75f, 0.88f, 4.6f), new Vector3(0.5f, 0.7f, 0.1f), "Seat");
            B.Box(t, "Partition", new Vector3(0.79f, 0.5f, 4.4f), new Vector3(0.84f, 1.0f, 0.04f), "Interior", true);
            B.Rail(t, "PartitionRail", new Vector3(0.36f, 1.05f, 4.4f), new Vector3(1.2f, 1.05f, 4.4f), 0.022f, "Rail");
            B.Rail(t, "PartitionPole", new Vector3(0.36f, 0, 4.4f), new Vector3(0.36f, 2.04f, 4.4f), 0.025f, "Rail");
            B.Box(t, "EngineCover", new Vector3(0.02f, 0.28f, 4.95f), new Vector3(0.6f, 0.56f, 0.9f), "Dash", true);
            B.Box(t, "EnginePad", new Vector3(0.02f, 0.565f, 4.95f), new Vector3(0.62f, 0.03f, 0.92f), "Rubber");
            B.Box(t, "Dashboard", new Vector3(0, 1.0f, 5.27f), new Vector3(2.42f, 0.2f, 0.42f), "Dash", true);
            B.Cyl(t, "Column", new Vector3(0.75f, 0.92f, 5.1f), new Vector3(0.06f, 0.2f, 0.06f), "Black", false, new Vector3(-50, 0, 0));
            B.Cyl(t, "Wheel", new Vector3(0.75f, 1.1f, 4.98f), new Vector3(0.44f, 0.015f, 0.44f), "Black", false, new Vector3(-55, 0, 0));
            B.Box(t, "Towel", new Vector3(0.95f, 1.12f, 5.3f), new Vector3(0.3f, 0.03f, 0.2f), "Towel");
            bus.bellPoint = B.Sph(t, "BellDome", new Vector3(0.35f, 2.2f, 5.3f), new Vector3(0.1f, 0.06f, 0.1f), "Rail").transform;
            bus.speakerPoint = B.Box(t, "Speaker", new Vector3(0, 2.26f, 1.2f), new Vector3(0.25f, 0.06f, 0.25f), "Black").transform;
            bus.rearSpeakerPoint = B.Box(t, "SpeakerRear", new Vector3(0, 2.26f, -3.6f), new Vector3(0.25f, 0.06f, 0.25f), "Black").transform;

            // Route display above the windshield (faces the cabin)
            B.Box(t, "DisplayBox", new Vector3(0.25f, 2.0f, 5.4f), new Vector3(1.25f, 0.22f, 0.06f), "Black");
            bus.routeDisplay = B.Text(t, "RouteDisplay", "117  OLD MARKET DEPOT", new Vector3(0.25f, 2.0f, 5.365f), Vector3.zero, 0.085f, new Color(1f, 0.5f, 0.08f));

            // Rear-view mirror: a real camera into a render texture. The mirror camera also sees layer 8.
            B.Box(t, "MirrorFrame", new Vector3(-0.66f, 1.95f, 5.43f), new Vector3(0.82f, 0.3f, 0.03f), "MirrorFrame");
            var mq = B.Quad(t, "Mirror", new Vector3(-0.66f, 1.95f, 5.412f), new Vector3(0.76f, 0.25f, 1f), null);
            bus.mirrorRT = new RenderTexture(640, 220, 16) { name = "MirrorRT" };
            var mm = new Material(Mats.Get("Mirror")) { mainTexture = bus.mirrorRT, mainTextureScale = new Vector2(-1, 1), mainTextureOffset = new Vector2(1, 0) };
            mq.GetComponent<Renderer>().sharedMaterial = mm;
            var camGo = B.Empty(t, "MirrorCam", new Vector3(-0.66f, 1.88f, 5.3f), new Vector3(9, 180, 0));
            var mc = camGo.AddComponent<Camera>();
            mc.targetTexture = bus.mirrorRT;
            mc.fieldOfView = 46; mc.nearClipPlane = 0.05f; mc.farClipPlane = 60f;
            mc.clearFlags = CameraClearFlags.SolidColor; mc.backgroundColor = new Color(0.01f, 0.012f, 0.018f);
            mc.cullingMask = ~0;
            mc.depth = -5;
            bus.mirrorCam = mc;

            // Cabin lights
            float[] lz = { 3.4f, 1.6f, 0.0f, -1.6f, -3.4f, -4.8f };
            bus.cabinLights = new Light[lz.Length];
            bus.tubes = new Renderer[lz.Length];
            bus.lightBase = new float[lz.Length];
            for (int i = 0; i < lz.Length; i++)
            {
                bus.tubes[i] = B.Box(t, "Tube" + i, new Vector3(0, 2.275f, lz[i]), new Vector3(0.07f, 0.035f, 0.9f), "TubeOn").GetComponent<Renderer>();
                bus.cabinLights[i] = B.PointLight(t, "CabinLight" + i, new Vector3(0, 2.1f, lz[i]), new Color(0.72f, 1f, 0.88f), 0.85f, 4.2f);
                bus.lightBase[i] = 0.85f;
            }

            // Conductor's corner: jump seat, ticket machine shelf, manifest hook, route card, bell buttons
            bus.jumpSeat = B.Empty(t, "JumpSeatAnchor", new Vector3(-0.98f, 0, 3.92f), new Vector3(0, 90, 0)).transform;
            B.Box(t, "JumpSeatFrame", new Vector3(-1.0f, 0.21f, 3.92f), new Vector3(0.4f, 0.42f, 0.38f), "SeatFrame", true);
            B.Box(t, "JumpSeat", new Vector3(-1.0f, 0.46f, 3.92f), new Vector3(0.42f, 0.08f, 0.4f), "Seat");
            B.Box(t, "JumpBack", new Vector3(-1.17f, 0.8f, 3.92f), new Vector3(0.06f, 0.6f, 0.4f), "Seat");

            B.Box(t, "MachineShelf", new Vector3(-1.08f, 1.02f, 3.5f), new Vector3(0.26f, 0.03f, 0.32f), "SeatFrame");
            var tm = B.Box(t, "TicketMachine", new Vector3(-1.07f, 1.1f, 3.5f), new Vector3(0.2f, 0.13f, 0.26f), "Dash");
            B.Cyl(tm.transform, "Roll", new Vector3(0, 0.6f, -0.2f), new Vector3(0.5f, 0.35f, 0.5f), "Paper", false, new Vector3(0, 0, 90));
            B.Quad(tm.transform, "Screen", new Vector3(0, 0.51f, 0.15f), new Vector3(0.6f, 0.3f, 1), "PhoneScreen", new Vector3(90, 0, 0));
            MakeInteract(bus, tm, "TicketMachine", "Check the ticket machine");

            var hook = B.Box(t, "ManifestBoard", new Vector3(-1.165f, 1.4f, 3.25f), new Vector3(0.02f, 0.32f, 0.24f), "Wood");
            B.Box(hook.transform, "Sheet", new Vector3(-0.8f, -0.05f, 0), new Vector3(0.5f, 0.85f, 0.85f), "Paper");
            MakeInteract(bus, hook, "Manifest", "Take the manifest");

            var card = B.Box(t, "RouteCard", new Vector3(-0.75f, 1.115f, 5.2f), new Vector3(0.24f, 0.01f, 0.16f), "Paper", false, new Vector3(-20, 0, 0));
            MakeInteract(bus, card, "RouteCard", "Read the route card");

            var bellF = B.Sph(t, "BellButtonFront", new Vector3(-1.15f, 1.55f, 4.18f), new Vector3(0.07f, 0.07f, 0.07f), "BarrierRed");
            MakeInteract(bus, bellF, "BellFront", "Ring the bell");
            var bellR = B.Sph(t, "BellButtonRear", new Vector3(-1.15f, 1.55f, -2.0f), new Vector3(0.07f, 0.07f, 0.07f), "BarrierRed");
            MakeInteract(bus, bellR, "BellRear", "Ring the bell");

            var handle = B.Box(t, "RearHandle", new Vector3(-1.17f, 1.1f, -2.2f), new Vector3(0.04f, 0.45f, 0.04f), "Rail");
            // The handle is thin, so the clickable area covers the whole rear door (about 1 m wide, 1.9 m tall).
            var handleCol = handle.AddComponent<BoxCollider>();
            handleCol.size = new Vector3(0.25f / 0.04f, 1.9f / 0.45f, 1.1f / 0.04f);
            handleCol.center = new Vector3(0, -0.1f / 0.45f, -0.4f / 0.04f);
            MakeInteract(bus, handle, "RearDoor", "Inspect the rear door");
            // The handle is thin: let the whole doorway (from inside the bus) count as the target.
            var hc = handle.GetComponent<BoxCollider>();
            hc.size = new Vector3(0.3f / 0.04f, 1.9f / 0.45f, 1.0f / 0.04f);
            hc.center = new Vector3(0.12f / 0.04f, -0.1f / 0.45f, -0.4f / 0.04f);
            var wet = B.Box(t, "RearHandleWet", new Vector3(-1.165f, 1.1f, -2.2f), new Vector3(0.05f, 0.25f, 0.05f), "Wet");
            bus.objs["RearWet"] = wet; wet.SetActive(false);

            // Fare chart on the front-left band
            B.Text(t, "FareChart",
                "FARE CHART  -  ROUTE 117\nOld Market .............. Rs 8\nHospital Road ......... Rs 10\nCinema Street ......... Rs 12\nRiver Road ............. Rs 14\nCentral Bus Stand ... Rs 18",
                new Vector3(-1.16f, 1.62f, 2.45f), new Vector3(0, -90, 0), 0.028f, new Color(0.1f, 0.1f, 0.12f), TextAnchor.MiddleLeft);
            B.Box(t, "FareBoard", new Vector3(-1.175f, 1.62f, 2.65f), new Vector3(0.01f, 0.26f, 0.5f), "Paper");

            // Story props (hidden until their chapter)
            var lp = B.Box(t, "LostPropertyPouch", new Vector3(-1.0f, 1.35f, -5.4f), new Vector3(0.28f, 0.32f, 0.08f), "Wood");
            B.Text(t, "LPText", "LOST\nPROPERTY", new Vector3(-1.0f, 1.38f, -5.355f), new Vector3(0, 180, 0), 0.04f, new Color(0.9f, 0.85f, 0.7f));
            MakeInteract(bus, lp, "Pouch", "Open the lost-property pouch");

            var log = B.Box(t, "Logbook", new Vector3(0.05f, 0.6f, 5.05f), new Vector3(0.22f, 0.04f, 0.3f), "BusMaroon");
            MakeInteract(bus, log, "Logbook", "Read the driver's logbook");

            var bag = B.Box(t, "ConductorBag", new Vector3(-1.0f, 0.57f, 3.92f), new Vector3(0.3f, 0.16f, 0.22f), "Wood");
            MakeInteract(bus, bag, "Bag", "Search the conductor's bag");

            var phone = B.Box(t, "Phone", new Vector3(-0.95f, 0.535f, 0.05f), new Vector3(0.08f, 0.015f, 0.15f), "Phone");
            B.Quad(phone.transform, "PhoneScreen", new Vector3(0, 0.6f, 0), new Vector3(0.85f, 0.85f, 1), "PhoneScreen", new Vector3(90, 0, 0));
            MakeInteract(bus, phone, "Phone", "Pick up the phone").gameObject.SetActive(false);

            var paper = B.Box(t, "Newspaper", new Vector3(-0.52f, 0.012f, 0.25f), new Vector3(0.32f, 0.006f, 0.24f), "Paper", false, new Vector3(0, 18, 0));
            MakeInteract(bus, paper, "Newspaper", "Pick up the newspaper clipping").gameObject.SetActive(false);

            var mt = B.Box(t, "MeenaTicket", new Vector3(0.82f, 0.53f, 3.42f), new Vector3(0.06f, 0.004f, 0.11f), "Paper", false, new Vector3(0, 25, 0));
            MakeInteract(bus, mt, "MeenaTicket", "Pick up the ticket").gameObject.SetActive(false);
            var ws = B.Box(t, "WetSeat", new Vector3(0.98f, 0.524f, 3.41f), new Vector3(0.4f, 0.004f, 0.42f), "Wet");
            bus.objs["WetSeat"] = ws; ws.SetActive(false);
            var hp = B.Quad(t, "Handprint", new Vector3(1.222f, 1.42f, 3.38f), new Vector3(0.16f, 0.2f, 1), "Handprint", new Vector3(0, 90, 0));
            bus.objs["Handprint"] = hp; hp.SetActive(false);

            var badge = B.Box(t, "Badge", new Vector3(-1.0f, 1.35f, -5.36f), new Vector3(0.07f, 0.03f, 0.01f), "Badge");
            bus.objs["BadgeProp"] = badge; badge.SetActive(false);

            return bus;
        }

        static GameObject MakeDoor(Transform t, string n, Vector3 pos)
        {
            var d = B.Box(t, n, pos, new Vector3(0.05f, 2.0f, 0.8f), "BusCream", true);
            B.Box(d.transform, "Glass", new Vector3(0, 0.18f, 0), new Vector3(1.2f, 0.4f, 0.8f), "Glass");
            B.Box(d.transform, "Lower", new Vector3(0, -0.3f, 0), new Vector3(1.3f, 0.35f, 1.02f), "BusMaroon");
            return d;
        }
    }
}
