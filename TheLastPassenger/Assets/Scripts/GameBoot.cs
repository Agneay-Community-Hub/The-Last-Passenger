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
            B.Box(env, "Road", new Vector3(0, -0.6f, 0), new Vector3(400, 0.1f, 400), "Black", true);

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

            // People. Men use the Quaternius characters (animated); people without a model keep the greybox body.
            var peopleRoot = new GameObject("Passengers").transform;
            var story = new GameObject("Story").AddComponent<StoryDirector>();
            System.Collections.Generic.Dictionary<string, string> Map(params string[] kv)
            {
                var d = new System.Collections.Generic.Dictionary<string, string>();
                for (int i = 0; i + 1 < kv.Length; i += 2) d[kv[i]] = kv[i + 1];
                return d;
            }
            var khakiDriver = Map("Skin", "SkinDark", "Skin_Darker", "SkinDark", "White", "Khaki", "LightBrown", "Khaki", "LightBlue", "Khaki", "Red_Dark", "Black");
            var khakiConductor = Map("Skin", "Skin", "Skin_Darker", "Skin", "White", "Khaki", "LightBrown", "Khaki", "LightBlue", "Khaki", "Red_Dark", "Black");
            var ghostAll = Map("*", "Ghost");

            Passenger Make(string id, string name, PersonBuilder.Recipe r, Vector3 seat, string label, string dest, int light)
            {
                var p = PersonBuilder.Make(peopleRoot, id, name, r);
                p.seatPos = seat; p.seatLabel = label; p.destination = dest; p.lightIndex = light;
                story.P[id] = p;
                p.Hide();
                return p;
            }
            var meena = Make("meena", "Meena", new PersonBuilder.Recipe { clothes = "Meena", legs = "Meena", skin = "Skin", hair = "Hair" },
                new Vector3(0.98f, 0, 3.4f), "R1", "Hospital Road", 0);
            B.Box(meena.body, "ClothBag", new Vector3(0.3f, 0.85f, 0.1f), new Vector3(0.12f, 0.3f, 0.25f), "Lakshmi");

            var selvam = Make("selvam", "Selvam", new PersonBuilder.Recipe { clothes = "Selvam", legs = "Selvam", skin = "SkinDark", hair = "HairGrey", model = "Beach",
                recolor = Map("Skin", "SkinDark", "Hair", "HairGrey", "Eyebrows", "HairGrey", "White", "Selvam") }, new Vector3(-0.98f, 0, 1.0f), "L3", "Old Market", 1);
            PersonBuilder.Attach(selvam, HumanBodyBones.RightHand,
                B.Cyl(selvam.body, "Flask", new Vector3(0.22f, 0.8f, 0.18f), new Vector3(0.1f, 0.14f, 0.1f), "Steel"), new Vector3(0, -0.06f, 0.04f));

            var kavin = Make("kavin", "Kavin", new PersonBuilder.Recipe { clothes = "Kavin", legs = "Jeans", skin = "Skin", hair = "Hair", scale = 0.98f, model = "Casual",
                recolor = Map("Skin", "Skin") }, new Vector3(0.98f, 0, -1.6f), "R6", "River Road", 3);
            PersonBuilder.Attach(kavin, HumanBodyBones.Chest,
                B.Box(kavin.body, "Backpack", new Vector3(0, 1.05f, -0.2f), new Vector3(0.32f, 0.4f, 0.16f), "Black"), new Vector3(0, -0.05f, -0.2f));

            var lak = Make("lakshmi", "Lakshmi", new PersonBuilder.Recipe { clothes = "Lakshmi", legs = "Lakshmi", skin = "SkinDark", hair = "HairGrey", scale = 0.93f },
                new Vector3(-0.98f, 0, -3.8f), "L6", "Central Bus Stand", 4);
            B.Box(lak.body, "Tiffin", new Vector3(0.22f, 0.78f, 0.2f), new Vector3(0.14f, 0.2f, 0.14f), "Steel");

            var bala = Make("bala", "Bala", new PersonBuilder.Recipe { clothes = "Bala", legs = "BalaShorts", skin = "Skin", hair = "Hair", scale = 0.68f, child = true, model = "Casual2",
                recolor = Map("Skin", "Skin", "Skin_Darker", "Skin", "White", "Bala", "LightBrown", "Bala", "LightBlue", "BalaShorts", "Red_Dark", "Black") },
                new Vector3(0.98f, 0, 0.4f), "R4", "Central Bus Stand", 2);
            PersonBuilder.Attach(bala, HumanBodyBones.RightHand,
                B.Box(bala.body, "Windmill", new Vector3(0.28f, 1.05f, 0.15f), new Vector3(0.18f, 0.18f, 0.02f), "Windmill", false, new Vector3(0, 0, 45)), new Vector3(0, 0.08f, 0.04f));
            PersonBuilder.Attach(bala, HumanBodyBones.Chest,
                B.Box(bala.body, "Satchel", new Vector3(0, 1.05f, -0.2f), new Vector3(0.3f, 0.32f, 0.12f), "BalaShorts"), new Vector3(0, -0.05f, -0.14f));

            // Murugan, the depot timekeeper, only appears in the training run.
            Make("murugan", "Murugan", new PersonBuilder.Recipe { clothes = "Selvam", legs = "Khaki", skin = "SkinDark", hair = "Hair", model = "Farmer",
                recolor = Map("Skin", "SkinDark") }, new Vector3(0.98f, 0, 2.4f), "R3", "Old Market", 1);

            var driver = PersonBuilder.Make(peopleRoot, "raghavan", "Raghavan", new PersonBuilder.Recipe { clothes = "Khaki", legs = "Khaki", skin = "SkinDark", hair = "Hair", model = "Casual2", recolor = khakiDriver });
            driver.seatPos = new Vector3(0.75f, 0, 4.85f);
            driver.isDriver = true;
            PersonBuilder.Attach(driver, HumanBodyBones.Head,
                B.Box(driver.body, "Moustache", new Vector3(0, -0.05f, 0.11f), new Vector3(0.09f, 0.02f, 0.02f), "Black"), new Vector3(0, -0.035f, 0.115f));
            PersonBuilder.Attach(driver, HumanBodyBones.Chest,
                B.Box(driver.body, "TowelShoulder", new Vector3(-0.15f, 1.42f, 0), new Vector3(0.14f, 0.04f, 0.3f), "Towel"), new Vector3(-0.13f, 0.17f, 0));
            story.driver = driver;

            // Mirror-only people (layer 8): the reflection that stays, the silent man, and you.
            var ghost = PersonBuilder.Copy(meena, peopleRoot, "ghost_meena");
            ghost.seatPos = meena.seatPos; Object.Destroy(ghost.GetComponent<Collider>());
            B.Layer(ghost.gameObject, B.MirrorLayer); story.ghostMeena = ghost; ghost.Hide();

            var silent = PersonBuilder.Make(peopleRoot, "silent", "", new PersonBuilder.Recipe { clothes = "Ghost", legs = "Ghost", skin = "Ghost", hair = "Ghost", scale = 1.04f, model = "Suit", recolor = ghostAll });
            Object.Destroy(silent.GetComponent<Collider>());
            PersonBuilder.Attach(silent, HumanBodyBones.RightHand,
                B.Quad(silent.body, "BlankTicket", new Vector3(0.27f, 0.72f, 0.12f), new Vector3(0.06f, 0.1f, 1f), "Paper", new Vector3(0, 180, 0)), new Vector3(0, -0.03f, 0.05f));
            B.Layer(silent.gameObject, B.MirrorLayer); story.silentMan = silent; silent.Hide();

            var dbl = PersonBuilder.Make(peopleRoot, "conductor_double", "", new PersonBuilder.Recipe { clothes = "Khaki", legs = "Khaki", skin = "Skin", hair = "Hair", model = "Casual2", recolor = khakiConductor });
            Object.Destroy(dbl.GetComponent<Collider>());
            PersonBuilder.Attach(dbl, HumanBodyBones.Hips,
                B.Box(dbl.body, "Pouch", new Vector3(0.2f, 0.9f, 0.16f), new Vector3(0.16f, 0.2f, 0.08f), "Wood"), new Vector3(0.2f, 0, 0.12f));
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

            var refl = PersonBuilder.Make(playerGo.transform.parent, "reflection", "", new PersonBuilder.Recipe { clothes = "Khaki", legs = "Khaki", skin = "Skin", hair = "Hair", model = "Casual2", recolor = khakiConductor });
            Object.Destroy(refl.GetComponent<Collider>());
            refl.interact.active = false;
            var group = new GameObject("BadgeGroup").transform; group.SetParent(refl.body, false);
            group.localPosition = new Vector3(-0.1f, 1.2f, 0.15f);
            var badge = B.Box(group, "Badge", Vector3.zero, new Vector3(0.2f, 0.08f, 0.01f), "Badge");
            // Mirrored text so it reads correctly in the mirror.
            var bt = B.Text(group, "BadgeName", "ARUN", new Vector3(0, 0, 0.008f), new Vector3(0, 180, 0), 0.07f, new Color(0.08f, 0.05f, 0.02f));
            bt.transform.localScale = new Vector3(-1, 1, 1);
            PersonBuilder.Attach(refl, HumanBodyBones.Chest, group.gameObject, new Vector3(-0.09f, 0.06f, 0.16f));
            B.Layer(refl.gameObject, B.MirrorLayer);
            refl.gameObject.SetActive(true);
            refl.state = PState.Standing;
            pl.reflection = refl.transform;
            story.reflectionBadge = group.gameObject;

            var ui = new GameObject("UI").AddComponent<UIManager>();
            ui.gameObject.AddComponent<Tutorial>();
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
            if (System.Array.IndexOf(args, "-tlpportrait") >= 0) { story.ShowTitle(); ui.menuOpen = false; ui.gameStarted = true; ui.StartCoroutine(story.Portraits(shots)); return; }
            if (botEnding != null)
            {
                story.botTutorial = System.Array.IndexOf(args, "-tlptutorial") >= 0;
                story.botHowTo = System.Array.IndexOf(args, "-tlphowto") >= 0;
                story.StartBot(botEnding, shots, fromCh);
                return;
            }
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
