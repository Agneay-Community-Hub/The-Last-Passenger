using UnityEngine;

namespace TLP
{
    /// <summary>Builds the whole game at startup: lighting, road, bus, people, player, systems.</summary>
    public class GameBoot : MonoBehaviour
    {
        void Awake()
        {
            var mats = FindAnyObjectByType<Mats>();
            Mats.Init(mats ? mats.list : null);
            Application.targetFrameRate = 60;
            QualitySettings.pixelLightCount = 6;
            QualitySettings.shadows = ShadowQuality.Disable;

            // Night: deep blue-black fog, almost no ambient.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.025f, 0.03f, 0.042f);
            RenderSettings.fogDensity = 0.028f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.07f, 0.075f, 0.09f);
            RenderSettings.skybox = null;

            var audioGo = new GameObject("Audio");
            var au = audioGo.AddComponent<AudioDirector>();

            // Road surface (static; the moving dashes and kerbs give the motion).
            var env = new GameObject("Environment").transform;
            B.Box(env, "Road", new Vector3(0, -0.55f, 0), new Vector3(400, 0.1f, 400), "Road", true);

            var worldRoot = new GameObject("WorldRoot").transform;
            var route = new GameObject("Route").AddComponent<RouteManager>();
            route.world = worldRoot;

            // The bus sits at the origin, facing +Z.
            var busRoot = new GameObject("Bus").transform;
            var bus = BusBuilder.Build(busRoot);

            // Rain outside the bus (two volumes so none falls through the roof).
            MakeRain(busRoot, new Vector3(-9f, 9f, 6f), new Vector3(15f, 0.1f, 50f));
            MakeRain(busRoot, new Vector3(9f, 9f, 6f), new Vector3(15f, 0.1f, 50f));
            MakeRain(busRoot, new Vector3(0, 9f, 22f), new Vector3(4f, 0.1f, 30f));

            // People
            var peopleRoot = new GameObject("Passengers").transform;
            var story = new GameObject("Story").AddComponent<StoryDirector>();
            Passenger Make(string id, string name, string clothes, string legs, string skin, string hair, float scale, bool child, Vector3 seat, string label, string dest, int light)
            {
                var p = PersonBuilder.Make(peopleRoot, id, name, clothes, legs, skin, hair, scale, child);
                p.seatPos = seat; p.seatLabel = label; p.destination = dest; p.lightIndex = light;
                story.P[id] = p;
                p.Hide();
                return p;
            }
            var meena = Make("meena", "Meena", "Meena", "Meena", "Skin", "Hair", 1f, false, new Vector3(0.98f, 0, 3.4f), "R1", "Hospital Road", 0);
            B.Box(meena.body, "ClothBag", new Vector3(0.3f, 0.85f, 0.1f), new Vector3(0.12f, 0.3f, 0.25f), "Lakshmi");
            var selvam = Make("selvam", "Selvam", "Selvam", "Selvam", "SkinDark", "HairGrey", 1f, false, new Vector3(-0.98f, 0, 1.0f), "L3", "Old Market", 1);
            B.Cyl(selvam.body, "Flask", new Vector3(0.22f, 0.8f, 0.18f), new Vector3(0.1f, 0.14f, 0.1f), "Steel");
            Make("kavin", "Kavin", "Kavin", "Jeans", "Skin", "Hair", 1.02f, false, new Vector3(0.98f, 0, -1.6f), "R6", "River Road", 3);
            B.Box(story.P["kavin"].body, "Backpack", new Vector3(0, 1.05f, -0.2f), new Vector3(0.32f, 0.4f, 0.16f), "Black");
            var lak = Make("lakshmi", "Lakshmi", "Lakshmi", "Lakshmi", "SkinDark", "HairGrey", 0.93f, false, new Vector3(-0.98f, 0, -3.8f), "L6", "Central Bus Stand", 4);
            B.Box(lak.body, "Tiffin", new Vector3(0.22f, 0.78f, 0.2f), new Vector3(0.14f, 0.2f, 0.14f), "Steel");
            var bala = Make("bala", "Bala", "Bala", "BalaShorts", "Skin", "Hair", 0.68f, true, new Vector3(0.98f, 0, 0.4f), "R4", "Central Bus Stand", 2);
            B.Box(bala.body, "Windmill", new Vector3(0.28f, 1.05f, 0.15f), new Vector3(0.18f, 0.18f, 0.02f), "Windmill", false, new Vector3(0, 0, 45));
            B.Box(bala.body, "Satchel", new Vector3(0, 1.05f, -0.2f), new Vector3(0.3f, 0.32f, 0.12f), "BalaShorts");

            var driver = PersonBuilder.Make(peopleRoot, "raghavan", "Raghavan", "Khaki", "Khaki", "SkinDark", "Hair", 1f, false);
            driver.seatPos = new Vector3(0.75f, 0, 4.85f);
            B.Box(driver.head, "Moustache", new Vector3(0, -0.05f, 0.11f), new Vector3(0.09f, 0.02f, 0.02f), "Black");
            B.Box(driver.body, "TowelShoulder", new Vector3(-0.15f, 1.42f, 0), new Vector3(0.14f, 0.04f, 0.3f), "Towel");
            story.driver = driver;

            // Mirror-only people (layer 8): the reflection that stays, the silent man, and you.
            var ghost = PersonBuilder.Make(peopleRoot, "ghost_meena", "Meena", "Meena", "Meena", "Skin", "Hair", 1f, false);
            ghost.seatPos = meena.seatPos; Object.Destroy(ghost.GetComponent<Collider>());
            B.Layer(ghost.gameObject, B.MirrorLayer); story.ghostMeena = ghost; ghost.Hide();

            var silent = PersonBuilder.Make(peopleRoot, "silent", "", "Ghost", "Ghost", "Ghost", "Ghost", 1.04f, false);
            Object.Destroy(silent.GetComponent<Collider>());
            B.Quad(silent.body, "BlankTicket", new Vector3(0.27f, 0.72f, 0.12f), new Vector3(0.06f, 0.1f, 1f), "Paper", new Vector3(0, 180, 0));
            B.Layer(silent.gameObject, B.MirrorLayer); story.silentMan = silent; silent.Hide();

            var dbl = PersonBuilder.Make(peopleRoot, "conductor_double", "", "Khaki", "Khaki", "Skin", "Hair", 1f, false);
            Object.Destroy(dbl.GetComponent<Collider>());
            B.Box(dbl.body, "Pouch", new Vector3(0.2f, 0.9f, 0.16f), new Vector3(0.16f, 0.2f, 0.08f), "Wood");
            story.conductorDouble = dbl; dbl.Hide();

            // Player
            var playerGo = new GameObject("Player");
            var cc = playerGo.AddComponent<CharacterController>();
            cc.height = 1.7f; cc.radius = 0.24f; cc.center = new Vector3(0, 0.85f, 0); cc.stepOffset = 0.3f; cc.slopeLimit = 50; cc.skinWidth = 0.03f;
            var pl = playerGo.AddComponent<PlayerController>();
            pl.cc = cc;
            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(playerGo.transform, false);
            camGo.transform.localPosition = new Vector3(0, 1.62f, 0);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 170f; cam.fieldOfView = 70;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = RenderSettings.fogColor;
            cam.cullingMask = ~(1 << B.MirrorLayer);
            cam.depth = 0;
            camGo.AddComponent<AudioListener>();
            camGo.tag = "MainCamera";
            pl.cam = cam;
            bus.mirrorCam.backgroundColor = RenderSettings.fogColor;

            var refl = PersonBuilder.Make(playerGo.transform.parent, "reflection", "", "Khaki", "Khaki", "Skin", "Hair", 1f, false);
            Object.Destroy(refl.GetComponent<Collider>());
            refl.interact.active = false;
            var badge = B.Box(refl.body, "Badge", new Vector3(-0.1f, 1.2f, 0.15f), new Vector3(0.2f, 0.08f, 0.01f), "Badge");
            // Mirrored text so it reads correctly in the mirror.
            var bt = B.Text(refl.body, "BadgeName", "ARUN", new Vector3(-0.1f, 1.2f, 0.158f), new Vector3(0, 180, 0), 0.07f, new Color(0.08f, 0.05f, 0.02f));
            bt.transform.localScale = new Vector3(-1, 1, 1);
            var group = new GameObject("BadgeGroup").transform; group.SetParent(refl.body, false);
            badge.transform.SetParent(group, true); bt.transform.SetParent(group, true);
            B.Layer(refl.gameObject, B.MirrorLayer);
            refl.gameObject.SetActive(true);
            pl.reflection = refl.transform;
            story.reflectionBadge = group.gameObject;

            var ui = new GameObject("UI").AddComponent<UIManager>();
            story.Init(bus, route, ui, pl, au);

            string pending = StoryDirector.pendingStart;
            StoryDirector.pendingStart = null;
            story.ShowTitle();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Automated playthrough for testing:  -tlpbot A|B|C  [-tlpshots <dir>] [-tlpchapter N]
            var args = System.Environment.GetCommandLineArgs();
            string botEnding = null, shots = null; int fromCh = 1;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-tlpbot") botEnding = args[i + 1];
                if (args[i] == "-tlpshots") shots = args[i + 1];
                if (args[i] == "-tlpchapter") int.TryParse(args[i + 1], out fromCh);
            }
            if (botEnding != null) { story.StartBot(botEnding, shots, fromCh); return; }
#endif
            if (pending == "new") story.NewGame();
            else if (pending == "continue") story.Continue();
        }

        static void MakeRain(Transform parent, Vector3 pos, Vector3 box)
        {
            var go = new GameObject("Rain");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = true;
            main.startLifetime = 1.1f;
            main.startSpeed = 0f;
            main.startSize = 0.03f;
            main.maxParticles = 1400;
            main.startColor = new Color(0.65f, 0.7f, 0.78f, 0.35f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var em = ps.emission; em.rateOverTime = box.x * box.z * 0.9f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = box;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(-0.6f); vel.y = new ParticleSystem.MinMaxCurve(-11f); vel.z = new ParticleSystem.MinMaxCurve(-3f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.035f; r.lengthScale = 1f;
            r.sharedMaterial = Mats.Get("Rain");
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
        }
    }
}
