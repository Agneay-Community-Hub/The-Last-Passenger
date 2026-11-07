using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TLP
{
    public enum Zone { Depot, City, Market, Hospital, Cinema, Flyover, River, Stand }

    /// <summary>
    /// The bus never moves: the city scrolls past it. Spawns roadside scenery for the current zone
    /// and brings the bus to an exact stop with a smooth speed profile.
    /// </summary>
    public class RouteManager : MonoBehaviour
    {
        public static RouteManager I;
        public Transform world;
        public float speed, targetSpeed, maxSpeed = 9f, accel = 1.6f, decel = 1.5f;
        public float traveled;
        public bool arrived;
        public Zone zone = Zone.Depot;
        float? stopAt;
        float nextLight, nextL, nextR, nextDash, nextKerb, nextSpecial;
        readonly List<Transform> spawned = new List<Transform>();
        float indicatorT;
        public System.Random rng = new System.Random(1117);
        public bool ghostRoad; // ending B: the bus moves with the engine off

        public const float DoorZ = 4.6f;
        const float Ahead = 140f, Behind = -45f;

        void Awake() { I = this; }

        public void ResetWorld()
        {
            foreach (var s in spawned) if (s) Destroy(s.gameObject);
            spawned.Clear();
            traveled = 0; speed = 0; targetSpeed = 0; stopAt = null; arrived = false;
            world.position = Vector3.zero;
            nextLight = nextL = nextR = nextDash = nextKerb = -40f;
            nextSpecial = 0;
            Fill(-40f, Ahead);
        }

        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        void Update()
        {
            float dt = Time.deltaTime;
            float desired = targetSpeed;
            if (stopAt.HasValue)
            {
                float rem = stopAt.Value - traveled;
                desired = Mathf.Min(desired, Mathf.Max(0.35f, Mathf.Sqrt(2f * decel * Mathf.Max(rem, 0))));
                if (rem <= 0.03f)
                {
                    traveled = stopAt.Value; speed = 0; targetSpeed = 0; stopAt = null; arrived = true;
                    if (AudioDirector.I) AudioDirector.I.PlayAt(AudioDirector.I.brake, new Vector3(0, 0, -4), 0.6f);
                    if (BusSystems.I) BusSystems.I.Brake(false);
                }
                else
                {
                    if (BusSystems.I) BusSystems.I.Brake(desired < speed - 0.05f);
                    if (rem < 40f)
                    {
                        indicatorT -= dt;
                        if (indicatorT <= 0 && AudioDirector.I) { indicatorT = 0.45f; AudioDirector.I.PlayAt(AudioDirector.I.indicator, new Vector3(0.6f, 1.0f, 5.2f), 0.25f); }
                    }
                }
            }
            float rate = desired > speed ? accel : decel * 1.6f;
            speed = Mathf.MoveTowards(speed, desired, rate * dt);
            if (stopAt.HasValue || speed > 0) traveled += speed * dt;
            world.position = new Vector3(0, 0, -traveled);
            if (AudioDirector.I) AudioDirector.I.SetSpeed(Mathf.Clamp01(speed / maxSpeed));

            Fill(traveled - 10f, traveled + Ahead);
            for (int i = spawned.Count - 1; i >= 0; i--)
            {
                var s = spawned[i];
                if (!s) { spawned.RemoveAt(i); continue; }
                if (s.position.z < Behind) { Destroy(s.gameObject); spawned.RemoveAt(i); }
            }
        }

        // ------------------------------------------------------------------ driving API

        public void Cruise(float v = -1) { stopAt = null; arrived = false; targetSpeed = v < 0 ? maxSpeed : v; }

        /// <summary>Drive and stop exactly so that a stop placed at 'distance' ahead lines up with the front door.</summary>
        public float PlanStop(float distance)
        {
            stopAt = traveled + distance;
            arrived = false;
            targetSpeed = maxSpeed;
            return stopAt.Value + DoorZ; // world-root local z for the stop marker
        }

        public IEnumerator DriveToStop(float distance, System.Func<Transform, float, GameObject> buildStop)
        {
            float z = PlanStop(distance);
            if (buildStop != null) Spawned(buildStop(world, z));
            while (!arrived) yield return null;
        }

        public void HaltNow() { stopAt = null; targetSpeed = 0; speed = 0; arrived = true; if (BusSystems.I) BusSystems.I.Brake(false); }

        public GameObject Spawned(GameObject g) { if (g) spawned.Add(g.transform); return g; }

        /// <summary>Local z on the world root that is 'ahead' metres in front of the bus right now.</summary>
        public float AheadZ(float ahead) => traveled + ahead;

        // ------------------------------------------------------------------ scenery

        void Fill(float from, float to)
        {
            while (nextDash < to) { Dash(nextDash); nextDash += 6f; }
            while (nextKerb < to) { Kerb(nextKerb); nextKerb += 20f; }
            while (nextLight < to) { StreetLight(nextLight); nextLight += zone == Zone.River ? 34f : 26f; }
            while (nextL < to) nextL += SideBlock(nextL, -1);
            while (nextR < to) nextR += SideBlock(nextR, 1);
        }

        Transform W(GameObject g) { g.transform.SetParent(world, true); spawned.Add(g.transform); return g.transform; }

        void Dash(float z)
        {
            var g = B.Box(world, "Dash", new Vector3(2.3f, -0.495f, z), new Vector3(0.12f, 0.01f, 2.6f), "LaneLine");
            spawned.Add(g.transform);
        }

        void Kerb(float z)
        {
            var g = B.Empty(world, "Kerb", new Vector3(0, 0, z));
            spawned.Add(g.transform);
            bool river = zone == Zone.River;
            B.Box(g.transform, "KerbL", new Vector3(-2.15f, -0.42f, 10), new Vector3(0.3f, 0.18f, 20f), "Kerb");
            B.Box(g.transform, "PavL", new Vector3(-4.8f, -0.35f, 10), new Vector3(5.2f, 0.3f, 20f), "Concrete");
            B.Box(g.transform, "KerbR", new Vector3(7.7f, -0.42f, 10), new Vector3(0.3f, 0.18f, 20f), "Kerb");
            B.Box(g.transform, "PavR", new Vector3(9.4f, -0.35f, 10), new Vector3(3.2f, 0.3f, 20f), "Concrete");
            if (zone == Zone.Flyover)
            {
                // Striped construction barriers and pillars of the unfinished flyover on the right.
                for (int i = 0; i < 4; i++)
                    B.Box(g.transform, "Barrier", new Vector3(7.2f, -0.1f, i * 5f + 2.5f), new Vector3(0.3f, 0.8f, 4.6f), i % 2 == 0 ? "Barrier" : "BarrierRed");
                B.Box(g.transform, "Pillar", new Vector3(12f, 3.2f, 10f), new Vector3(1.6f, 7.4f, 1.6f), "Concrete");
                bool broken = R(0, 1) < 0.3f;
                if (!broken) B.Box(g.transform, "Deck", new Vector3(10f, 7.3f, 10f), new Vector3(7f, 0.8f, 20.5f), "Concrete");
                else
                {
                    B.Box(g.transform, "DeckBroken", new Vector3(10f, 7.3f, 4f), new Vector3(7f, 0.8f, 8f), "Concrete", false, new Vector3(6, 0, 0));
                    B.Rail(g.transform, "Rebar", new Vector3(8f, 7.2f, 8f), new Vector3(8.5f, 6.4f, 9.5f), 0.04f, "Rail");
                    B.Rail(g.transform, "Rebar", new Vector3(11f, 7.1f, 8f), new Vector3(11.3f, 6.2f, 9.8f), 0.04f, "Rail");
                }
            }
            if (river)
            {
                B.Box(g.transform, "Water", new Vector3(-30f, -1.6f, 10), new Vector3(46f, 0.1f, 20.2f), "Water");
                B.Box(g.transform, "Wall", new Vector3(-7.4f, -0.9f, 10), new Vector3(0.4f, 1.4f, 20f), "Concrete");
                B.Rail(g.transform, "RailTop", new Vector3(-7.3f, 0.45f, 0), new Vector3(-7.3f, 0.45f, 20), 0.04f, "Rail");
                for (int i = 0; i < 5; i++)
                    B.Rail(g.transform, "Post", new Vector3(-7.3f, -0.2f, i * 4f), new Vector3(-7.3f, 0.45f, i * 4f), 0.035f, "Rail");
                if (R(0, 1) < 0.5f)
                    B.Box(g.transform, "FarLight", new Vector3(R(-80, -50), R(-0.5f, 2), R(0, 20)), new Vector3(0.4f, 0.4f, 0.4f), R(0, 1) < 0.5f ? "WindowLit" : "Sodium");
            }
        }

        void StreetLight(float z)
        {
            bool left = zone == Zone.River ? false : (z / 26f) % 2 < 1f;
            float x = left ? -4.3f : 8.3f;
            float dir = left ? 1 : -1;
            var g = B.Empty(world, "StreetLight", new Vector3(x, -0.5f, z));
            spawned.Add(g.transform);
            B.Cyl(g.transform, "Pole", new Vector3(0, 3.5f, 0), new Vector3(0.12f, 3.5f, 0.12f), "Pole");
            B.Box(g.transform, "Arm", new Vector3(dir * 0.9f, 6.95f, 0), new Vector3(1.8f, 0.08f, 0.08f), "Pole");
            B.Box(g.transform, "Lamp", new Vector3(dir * 1.7f, 6.85f, 0), new Vector3(0.6f, 0.12f, 0.28f), "Sodium");
            bool dead = R(0, 1) < (zone == Zone.Flyover ? 0.6f : 0.15f);
            if (!dead)
            {
                var l = B.PointLight(g.transform, "Sodium", new Vector3(dir * 1.7f, 6.4f, 0), new Color(1f, 0.62f, 0.25f), 2.6f, 15f);
                l.renderMode = LightRenderMode.Auto;
            }
            else B.SetMat(g.transform.Find("Lamp").gameObject, "Pole");
        }

        /// <summary>Spawns one building or gap on a side. Returns its length along the road.</summary>
        float SideBlock(float z, int side)
        {
            float edge = side < 0 ? -7.4f : 11f;
            switch (zone)
            {
                case Zone.River when side < 0: return 40f;
                case Zone.Flyover when side > 0: return 40f;
                case Zone.Hospital when side < 0:
                {
                    var g = B.Empty(world, "HospitalWall", new Vector3(edge, -0.5f, z));
                    spawned.Add(g.transform);
                    B.Box(g.transform, "Wall", new Vector3(0, 1.2f, 10), new Vector3(0.4f, 2.4f, 20f), "HospitalWall");
                    B.Box(g.transform, "Block", new Vector3(-14f, 6f, 10), new Vector3(16f, 12f, 18f), "HospitalWall");
                    for (int i = 0; i < 6; i++)
                        B.Box(g.transform, "Win", new Vector3(-5.95f, R(3, 11), R(2, 18)), new Vector3(0.05f, 1.0f, 1.4f), R(0, 1) < 0.4f ? "WindowCool" : "WindowDark");
                    return 20f;
                }
            }

            float len = R(6f, 14f);
            if (R(0, 1) < (zone == Zone.City || zone == Zone.Depot ? 0.12f : 0.08f)) return R(4, 10); // gap / vacant lot
            float h = zone == Zone.Market ? R(4, 8) : R(5, 14);
            float d = R(6, 10);
            var b = B.Empty(world, "Building", new Vector3(edge + side * d * 0.5f, -0.5f, z + len * 0.5f));
            spawned.Add(b.transform);
            string[] bm = { "Building1", "Building2", "Building3", "Building4" };
            B.Box(b.transform, "Body", new Vector3(0, h * 0.5f, 0), new Vector3(d, h, len - 0.4f), bm[rng.Next(bm.Length)]);
            float faceX = -side * (d * 0.5f + 0.02f);
            // ground floor: rolled-down shop shutters, sometimes a lit shop
            int shops = Mathf.Max(1, Mathf.FloorToInt(len / 3.2f));
            for (int i = 0; i < shops; i++)
            {
                float sz = -len * 0.5f + (i + 0.5f) * (len / shops);
                bool lit = R(0, 1) < (zone == Zone.Market ? 0.18f : 0.07f);
                B.Box(b.transform, "Shutter", new Vector3(faceX, 1.4f, sz), new Vector3(0.05f, 2.6f, len / shops - 0.4f), lit ? "WindowLit" : "Shutter");
            }
            B.Box(b.transform, "Signboard", new Vector3(faceX, 3.2f, 0), new Vector3(0.06f, 0.7f, len * 0.8f), R(0, 1) < 0.5f ? "Poster" : "Building3");
            int floors = Mathf.FloorToInt((h - 3.5f) / 3f);
            for (int f = 0; f < floors; f++)
                for (float wz = -len * 0.5f + 1.5f; wz < len * 0.5f - 1f; wz += 2.4f)
                {
                    if (R(0, 1) < 0.35f) continue;
                    float r = R(0, 1);
                    string m = r < 0.12f ? "WindowLit" : r < 0.18f ? "WindowCool" : "WindowDark";
                    B.Box(b.transform, "Win", new Vector3(faceX, 4.8f + f * 3f, wz), new Vector3(0.05f, 1.2f, 1.1f), m);
                }
            if (zone == Zone.Market && R(0, 1) < 0.35f)
            {
                // tarpaulin awnings over the market pavement
                B.Box(b.transform, "Awning", new Vector3(faceX - side * 1.3f, 2.8f, 0), new Vector3(2.6f, 0.05f, len * 0.7f), R(0, 1) < 0.5f ? "BlueGate" : "Poster", false, new Vector3(0, 0, side * 10));
            }
            return len;
        }

        // ------------------------------------------------------------------ authored places

        /// <summary>Stop spawning ordinary buildings on the left until z, so an authored building has room.</summary>
        public void ReserveLeft(float z) { nextL = Mathf.Max(nextL, z); }

        public GameObject BuildShelter(Transform parent, float z, string name, bool lit = true)
        {
            var g = B.Empty(parent, "Stop_" + name, new Vector3(-4.0f, -0.5f, z));
            B.Box(g.transform, "Slab", new Vector3(0, 0.2f, 0), new Vector3(3.6f, 0.3f, 6f), "Concrete", true);
            B.Box(g.transform, "Roof", new Vector3(-0.6f, 2.9f, 0), new Vector3(2.6f, 0.1f, 5f), "Shutter");
            B.Box(g.transform, "Back", new Vector3(-1.85f, 1.6f, 0), new Vector3(0.08f, 2.6f, 5f), "Building2");
            foreach (var pz in new[] { -2.3f, 2.3f })
                B.Cyl(g.transform, "Post", new Vector3(0.6f, 1.6f, pz), new Vector3(0.1f, 1.3f, 0.1f), "Pole");
            B.Box(g.transform, "Bench", new Vector3(-1.3f, 0.75f, 0), new Vector3(0.5f, 0.08f, 3.6f), "Wood");
            B.Box(g.transform, "SignBoard", new Vector3(0.7f, 3.35f, 0), new Vector3(0.06f, 0.55f, 3.2f), "Paper");
            if (!string.IsNullOrEmpty(name))
                B.Text(g.transform, "SignText", name.ToUpper(), new Vector3(0.75f, 3.35f, 0), new Vector3(0, 90, 0), 0.26f, new Color(0.15f, 0.05f, 0.05f));
            if (lit)
            {
                B.Box(g.transform, "Tube", new Vector3(-0.6f, 2.83f, 0), new Vector3(0.08f, 0.04f, 1.2f), "TubeOn");
                B.PointLight(g.transform, "Light", new Vector3(-0.6f, 2.5f, 0), new Color(0.75f, 1f, 0.9f), 1.5f, 7f);
            }
            return g;
        }

        public GameObject BuildDepot(Transform parent, float z)
        {
            var g = B.Empty(parent, "Depot", new Vector3(0, -0.5f, z));
            B.Box(g.transform, "Platform", new Vector3(-6.2f, 0.17f, 0), new Vector3(9.6f, 0.36f, 22f), "Concrete", true);
            B.Box(g.transform, "PlatformEdge", new Vector3(-1.45f, 0.17f, 0), new Vector3(0.12f, 0.36f, 22f), "KerbStripe");
            B.Box(g.transform, "ShedRoof", new Vector3(-4f, 5.2f, 0), new Vector3(12f, 0.15f, 24f), "Shutter");
            for (int i = -2; i <= 2; i++)
            {
                B.Box(g.transform, "Column", new Vector3(-8.5f, 2.6f, i * 5f), new Vector3(0.3f, 5.2f, 0.3f), "Pole");
                B.Box(g.transform, "Column", new Vector3(2.6f, 2.6f, i * 5f), new Vector3(0.3f, 5.2f, 0.3f), "Pole");
            }
            B.Box(g.transform, "BackWall", new Vector3(-10.8f, 2.6f, 0), new Vector3(0.3f, 5.2f, 24f), "Building1");
            B.Box(g.transform, "Office", new Vector3(-9.2f, 1.6f, -6f), new Vector3(3f, 3.2f, 5f), "Building3");
            B.Box(g.transform, "OfficeWin", new Vector3(-7.68f, 1.8f, -6f), new Vector3(0.05f, 1.2f, 2.4f), "WindowLit");
            B.Box(g.transform, "Board", new Vector3(-10.6f, 4.0f, 2f), new Vector3(0.05f, 1.1f, 7f), "Paper");
            B.Text(g.transform, "DepotName", "OLD MARKET DEPOT\nKAVERIPURAM TRANSPORT  -  NIGHT SERVICES", new Vector3(-10.55f, 4.0f, 2f), new Vector3(0, -90, 0), 0.22f, new Color(0.25f, 0.05f, 0.05f));
            B.Box(g.transform, "Timetable", new Vector3(-10.6f, 1.9f, 6f), new Vector3(0.05f, 1.2f, 1.6f), "Paper");
            B.Text(g.transform, "TimetableText", "ROUTE 117\nLAST SERVICE\n01:17", new Vector3(-10.55f, 1.95f, 6f), new Vector3(0, -90, 0), 0.14f, new Color(0.1f, 0.1f, 0.1f));
            for (int i = -1; i <= 1; i++)
            {
                B.Box(g.transform, "Tube", new Vector3(-4f, 5.05f, i * 8f), new Vector3(0.1f, 0.05f, 1.4f), "TubeOn");
                B.PointLight(g.transform, "DepotLight", new Vector3(-4f, 4.6f, i * 8f), new Color(0.78f, 1f, 0.9f), 1.6f, 11f);
            }
            // An old bus parked in the next bay, never used again
            B.Box(g.transform, "OldBus", new Vector3(5.5f, 1.4f, -2f), new Vector3(2.5f, 2.6f, 11f), "BusMaroon");
            B.Box(g.transform, "OldBusWin", new Vector3(4.24f, 2.0f, -2f), new Vector3(0.02f, 0.9f, 9.5f), "WindowDark");
            return g;
        }

        public GameObject BuildTeaStall(Transform parent, float z)
        {
            var g = B.Empty(parent, "TeaStall", new Vector3(-6.4f, -0.5f, z));
            B.Box(g.transform, "Kiosk", new Vector3(0, 1.0f, 0), new Vector3(1.6f, 2f, 2.2f), "Wood");
            B.Box(g.transform, "Counter", new Vector3(0.9f, 1.0f, 0), new Vector3(0.3f, 0.1f, 2.2f), "Wood");
            B.Box(g.transform, "Roof", new Vector3(0.3f, 2.3f, 0), new Vector3(2.4f, 0.08f, 2.8f), "BlueGate");
            B.Cyl(g.transform, "Kettle", new Vector3(0.85f, 1.2f, 0.4f), new Vector3(0.25f, 0.15f, 0.25f), "Steel");
            B.Sph(g.transform, "Bulb", new Vector3(0.5f, 2.05f, 0), new Vector3(0.1f, 0.1f, 0.1f), "WindowLit");
            B.PointLight(g.transform, "Bulb", new Vector3(0.6f, 1.95f, 0), new Color(1f, 0.7f, 0.35f), 1.4f, 5f);
            return g;
        }

        public GameObject BuildHospitalGate(Transform parent, float z)
        {
            var g = BuildShelter(parent, z, "Hospital Road", true);
            var gate = B.Empty(parent, "BlueGate", new Vector3(-7.4f, -0.5f, z + 9f));
            ReserveLeft(z + 14f);
            B.Box(gate.transform, "GateL", new Vector3(0, 1.4f, -1.5f), new Vector3(0.12f, 2.8f, 2.8f), "BlueGate");
            B.Box(gate.transform, "GateR", new Vector3(0, 1.4f, 1.5f), new Vector3(0.12f, 2.8f, 2.8f), "BlueGate");
            B.Box(gate.transform, "Arch", new Vector3(0, 3.4f, 0), new Vector3(0.3f, 0.8f, 7f), "HospitalWall");
            B.Text(gate.transform, "ArchText", "GOVT. GENERAL HOSPITAL", new Vector3(0.18f, 3.4f, 0), new Vector3(0, 90, 0), 0.32f, new Color(0.1f, 0.2f, 0.5f));
            Spawned(gate);
            return g;
        }

        public GameObject BuildCinema(Transform parent, float z, int pass)
        {
            var g = B.Empty(parent, "Cinema" + pass, new Vector3(-9.1f, -0.5f, z));
            ReserveLeft(z + 10f);
            B.Box(g.transform, "Hall", new Vector3(-3f, 5f, 0), new Vector3(9f, 10f, 16f), "Building4");
            B.Box(g.transform, "Shutters", new Vector3(1.52f, 1.5f, 0), new Vector3(0.05f, 3f, 8f), "Shutter");
            B.Box(g.transform, "Marquee", new Vector3(1.8f, 4.2f, 0), new Vector3(0.6f, 1.2f, 10f), "Black");
            var sign = B.Text(g.transform, "Name", "SRI KAVERI TALKIES", new Vector3(2.12f, 4.2f, 0), new Vector3(0, 90, 0), 0.6f, new Color(1f, 0.15f, 0.1f));
            sign.gameObject.AddComponent<SignFlicker>().text = sign;
            B.Box(g.transform, "PosterFrame", new Vector3(1.54f, 6.8f, -3f), new Vector3(0.06f, 2.6f, 2.0f), "Black");
            bool crash = pass >= 3;
            B.Box(g.transform, "Poster", new Vector3(1.58f, 6.8f, -3f), new Vector3(0.04f, 2.4f, 1.8f), crash ? "PosterCrash" : "Poster");
            string ptxt = crash ? "NIGHT SERVICE\n117\n\n07 . 11 . 2015" : "NOW\nSHOWING\n\n- torn -";
            B.Text(g.transform, "PosterText", ptxt, new Vector3(1.62f, 6.8f, -3f), new Vector3(0, 90, 0), 0.22f, crash ? new Color(0.85f, 0.85f, 0.8f) : new Color(0.95f, 0.85f, 0.3f));
            if (crash)
            {
                // a crude "photograph" of this bus on the poster
                B.Box(g.transform, "PhotoBus", new Vector3(1.61f, 6.55f, -3f), new Vector3(0.02f, 0.45f, 1.3f), "BusMaroon");
                B.Box(g.transform, "PhotoBus2", new Vector3(1.615f, 6.75f, -3f), new Vector3(0.02f, 0.15f, 1.3f), "BusCream");
            }
            B.PointLight(g.transform, "MarqueeGlow", new Vector3(3.2f, 4.2f, 0), new Color(1f, 0.2f, 0.12f), 1.2f, 9f);
            return g;
        }

        public GameObject BuildBusStand(Transform parent, float z)
        {
            var g = B.Empty(parent, "BusStand", new Vector3(0, -0.5f, z));
            B.Box(g.transform, "Platform", new Vector3(-6.2f, 0.17f, 0), new Vector3(9.6f, 0.36f, 30f), "Concrete", true);
            B.Box(g.transform, "Edge", new Vector3(-1.45f, 0.17f, 0), new Vector3(0.12f, 0.36f, 30f), "KerbStripe");
            B.Box(g.transform, "Canopy", new Vector3(-5f, 4.8f, 0), new Vector3(10f, 0.25f, 30f), "Concrete");
            for (int i = -3; i <= 3; i++)
                B.Box(g.transform, "Column", new Vector3(-2.2f, 2.4f, i * 4.5f), new Vector3(0.45f, 4.8f, 0.45f), "Concrete");
            B.Box(g.transform, "SignBack", new Vector3(-1.9f, 5.6f, 0), new Vector3(0.1f, 1.0f, 14f), "Black");
            B.Text(g.transform, "Sign", "KAVERIPURAM  CENTRAL  BUS  STAND", new Vector3(-1.82f, 5.6f, 0), new Vector3(0, 90, 0), 0.42f, new Color(1f, 0.5f, 0.1f));
            B.Box(g.transform, "Bay", new Vector3(-8f, 1.0f, 2f), new Vector3(1.5f, 2f, 1.5f), "Building2");
            B.Text(g.transform, "BayNo", "BAY 7", new Vector3(-7.2f, 1.6f, 2f), new Vector3(0, 90, 0), 0.25f, new Color(0.9f, 0.9f, 0.85f));
            B.PointLight(g.transform, "StandLight", new Vector3(-4f, 4.2f, -4f), new Color(0.75f, 1f, 0.9f), 1.3f, 10f);
            B.PointLight(g.transform, "StandLight2", new Vector3(-4f, 4.2f, 6f), new Color(1f, 0.62f, 0.25f), 1.0f, 10f);
            return g;
        }

        public GameObject BuildFlyoverStop(Transform parent, float z)
        {
            // A dark roadside shelter with no name, right under the broken deck.
            var g = BuildShelter(parent, z, "", false);
            var deck = B.Empty(parent, "FlyoverEnd", new Vector3(0, -0.5f, z + 12f));
            B.Box(deck.transform, "Deck", new Vector3(4f, 7.3f, 0), new Vector3(16f, 0.8f, 10f), "Concrete", false, new Vector3(-5, 0, 0));
            B.Box(deck.transform, "Barrier", new Vector3(7.2f, 0.3f, 0), new Vector3(0.3f, 1f, 10f), "Barrier");
            B.Text(deck.transform, "Warn", "ROAD CLOSED", new Vector3(7.0f, 0.4f, 0), new Vector3(0, 90, 0), 0.4f, new Color(0.1f, 0.1f, 0.1f));
            Spawned(deck);
            return g;
        }
    }

    public class SignFlicker : MonoBehaviour
    {
        public TextMesh text;
        Color baseColor;
        void Start() { baseColor = text.color; }
        void Update()
        {
            float n = Mathf.PerlinNoise(Time.time * 3f, transform.position.z);
            text.color = n > 0.3f ? baseColor : baseColor * 0.25f;
        }
    }
}
