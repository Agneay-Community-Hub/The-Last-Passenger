using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TLP
{
    /// <summary>
    /// Seven chapters, authored horror events, the manifest rules and three diegetic endings.
    /// Every scare is tied to a story state and fires once.
    /// </summary>
    public class StoryDirector : MonoBehaviour
    {
        public static StoryDirector I;
        public static readonly string[] Stops = { "Old Market", "Hospital Road", "Cinema Street", "River Road", "Central Bus Stand" };
        public static readonly int[] Fares = { 8, 10, 12, 14, 18 };
        public static string pendingStart;          // survives scene reload: "new" / "continue"
        public static readonly string[] PassengerIds = { "meena", "selvam", "kavin", "lakshmi", "bala" };

        public SaveData data = new SaveData();
        public StoryFlags F => data.flags;
        public Settings settings;
        public float timeScale = 1f;
        public bool finalMode;
        public int conductorSign;

        public readonly Dictionary<string, Passenger> P = new Dictionary<string, Passenger>();
        public Passenger driver, ghostMeena, silentMan, conductorDouble;
        public GameObject reflectionBadge;
        readonly List<GameObject> crowd = new List<GameObject>();

        BusSystems bus; RouteManager route; UIManager ui; PlayerController pl; AudioDirector au;
        readonly Dictionary<string, (string prompt, Action act)> special = new Dictionary<string, (string, Action)>();
        readonly List<string> specialOrder = new List<string>();
        Action bellAction;
        string currentStop = "Old Market Depot";
        readonly Dictionary<string, int> idleIdx = new Dictionary<string, int>();
        bool ending;
        float nextGrumble;
        Coroutine main;

        // dev-only automation (never active in release builds)
        public bool bot, botTutorial, botHowTo; public string botEnding = "A"; public string shotDir; int shotIdx;

        void Awake() { I = this; settings = Settings.Load(); AudioListener.volume = settings.volume; }

        public void Init(BusSystems b, RouteManager r, UIManager u, PlayerController p, AudioDirector a)
        {
            UIManager.OnLine = (speaker, dur) =>
            {
                if (speaker == "RAGHAVAN") { if (driver) driver.Talk(dur); return; }
                foreach (var ps in P.Values) if (ps.displayName.ToUpper() == speaker && ps.gameObject.activeInHierarchy) ps.Talk(dur);
            };
            bus = b; route = r; ui = u; pl = p; au = a;
            pl.settings = settings;
            foreach (var pp in P.Values.Concat(new[] { driver }))
            {
                var ps = pp;
                ps.interact.promptFunc = () => PassengerPrompt(ps);
                ps.interact.onUse = () => PassengerUse(ps);
            }
            bus.Prop("BellFront").Set("Ring the bell", RingBell);
            bus.Prop("BellRear").Set("Ring the bell", RingBell);
        }

        // ================================================================== menu / flow

        public bool HasSave => PlayerPrefs.HasKey("tlp_save");
        public int SavedChapter
        {
            get
            {
                try { return JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString("tlp_save")).flags.chapter; } catch { return 1; }
            }
        }
        public string EndingsFound()
        {
            var e = PlayerPrefs.GetString("tlp_endings", "");
            return (e.Contains("A") ? "A " : "_ ") + (e.Contains("B") ? "B " : "_ ") + (e.Contains("C") ? "C" : "_") + "  (of 3)";
        }

        public void ShowTitle()
        {
            ui.menuOpen = true; ui.gameStarted = false;
            SetupState(1, true);
            ui.fadeTarget = 0;
        }

        public void NewGame()
        {
            if (ui.gameStarted) { pendingStart = "new"; SceneManager.LoadScene(0); return; }
            data = new SaveData();
            if (Tutorial.I) Tutorial.I.ResetAll();
            StartRun(1);
        }

        public void Continue()
        {
            if (ui.gameStarted) { pendingStart = "continue"; SceneManager.LoadScene(0); return; }
            try { data = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString("tlp_save")) ?? new SaveData(); }
            catch { data = new SaveData(); }
            StartRun(Mathf.Clamp(F.chapter, 1, 7));
        }

        public void ToTitle() { pendingStart = null; SceneManager.LoadScene(0); }

        void StartRun(int chapter)
        {
            ui.menuOpen = false; ui.settingsOpen = false; ui.gameStarted = true;
            if (main != null) StopCoroutine(main);
            main = StartCoroutine(Run(chapter));
        }

        void Save()
        {
            PlayerPrefs.SetString("tlp_save", JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }

        IEnumerator Run(int start)
        {
            SetupState(start, false);
            ui.fade = 1; ui.fadeTarget = 0;
            if (start == 1 && settings.tutorial && (!bot || botTutorial)) yield return Training();
            ui.ShowHint(start == 1 ? 25 : 12);
            StartCoroutine(AmbientDread());
            for (int c = start; c <= 7 && !ending; c++)
            {
                F.chapter = c;
                ui.dread = 0.2f + 0.08f * c;
                Save();
                Shot("ch" + c + "_start");
                switch (c)
                {
                    case 1: yield return Chapter1(); break;
                    case 2: yield return Chapter2(); break;
                    case 3: yield return Chapter3(); break;
                    case 4: yield return Chapter4(); break;
                    case 5: yield return Chapter5(); break;
                    case 6: yield return Chapter6(); break;
                    case 7: yield return Chapter7(); break;
                }
            }
        }

        // ================================================================== state

        /// <summary>Puts every passenger, prop, light and door where they belong at the start of a chapter.</summary>
        void SetupState(int n, bool titleScreen)
        {
            special.Clear(); specialOrder.Clear();
            bellAction = null; finalMode = false; conductorSign = 0; ending = false;
            foreach (var p in P.Values) { p.Hide(); p.ticketed = n >= 2; p.needsTicket = false; p.named = false; p.LookForward(); }
            ghostMeena.Hide(); silentMan.Hide(); conductorDouble.Hide();
            foreach (var key in new[] { "Phone", "Newspaper", "MeenaTicket", "WetSeat", "Handprint", "RearWet", "BadgeProp" }) bus.ShowObj(key, false);
            foreach (var kv in bus.props) if (!kv.Key.StartsWith("Bell")) kv.Value.Clear();
            bus.ShowObj("Manifest", n == 1);
            bus.SetAllLights(true);
            bus.SetLightTint(new Color(0.72f, 1f, 0.88f));
            bus.SetDoorInstant(true, n == 1);
            bus.SetDoorInstant(false, false);
            reflectionBadge.SetActive(F.badge);
            ui.hasManifest = n >= 2;
            au.engineOn = true; au.SetDrone(n >= 5 ? 0.12f : 0f); au.SetHeart(0); au.SetRain(0.45f); au.SetHum(0.12f);

            route.zone = n == 1 ? Zone.Depot : n == 2 ? Zone.City : n == 3 ? Zone.Hospital : n == 4 ? Zone.Cinema : n == 5 ? Zone.River : Zone.City;
            route.ResetWorld();
            driver.PlaceSeated();

            if (n == 1)
            {
                route.Spawned(route.BuildDepot(route.world, RouteManager.DoorZ));
                route.HaltNow();
                P["kavin"].PlaceSeated();
                P["lakshmi"].PlaceSeated();
                if (titleScreen)
                {
                    pl.Teleport(new Vector3(-6.5f, -0.13f, 1.2f), 70);
                    pl.pitch = -2;
                    P["meena"].PlaceAt(new Vector3(-3.4f, -0.15f, 6.4f), 200, PState.Standing);
                    P["bala"].PlaceAt(new Vector3(-3.0f, -0.15f, 7.0f), 210, PState.Standing);
                }
                else pl.Teleport(new Vector3(-4.6f, -0.13f, 4.6f), 90);
                bus.SetDisplay("117  OLD MARKET DEPOT");
                currentStop = "Old Market Depot";
            }
            else
            {
                route.Cruise(); route.speed = route.maxSpeed;
                pl.Teleport(new Vector3(0f, 0.02f, 3.3f), 180);
                foreach (var id in new[] { "kavin", "lakshmi", "bala" }) P[id].PlaceSeated();
                if (n == 2) P["meena"].PlaceSeated();
                if (n >= 3) P["selvam"].PlaceSeated();
                if (n == 7) P["meena"].PlaceSeated();
                string[] disp = { "", "", "117  OLD MARKET", "117  CINEMA STREET", "117  CINEMA STREET", "117  RIVER ROAD", "117  CENTRAL BUS STAND", "117  CENTRAL BUS STAND" };
                bus.SetDisplay(disp[n]);
                currentStop = n == 2 ? "Old Market Depot" : n == 3 ? "Hospital Road" : n == 4 ? "Cinema Street" : n == 5 ? "Cinema Street" : "River Road";
            }
            if (n == 7)
            {
                route.HaltNow();
                route.Spawned(route.BuildBusStand(route.world, RouteManager.DoorZ));
                bus.SetDoorInstant(true, true);
                foreach (var p in P.Values) p.named = true;
            }
            SyncCrowd();
        }

        void SyncCrowd()
        {
            while (crowd.Count < F.errors) SpawnCrowd();
        }

        void SpawnCrowd()
        {
            int i = crowd.Count;
            var p = PersonBuilder.Make(transform, "crowd" + i, "", new PersonBuilder.Recipe { clothes = "Ghost", legs = "Ghost", skin = "Ghost", hair = "Ghost", model = "Suit", recolor = new Dictionary<string, string> { ["*"] = "Ghost" } });
            Destroy(p.GetComponent<Collider>());
            p.interact.active = false;
            float z = -4.3f + (i % 10) * 0.8f;
            float x = (i / 10) % 2 == 0 ? 0f : 0.2f;
            p.PlaceAt(new Vector3(x, 0, z), i % 2 == 0 ? 0 : 180, PState.Standing);
            crowd.Add(p.gameObject);
        }

        // ================================================================== helpers

        IEnumerator Talk(params (string sp, string tx)[] ls)
        {
            foreach (var l in ls) ui.Say(l.sp, l.tx);
            yield return new WaitUntil(() => !ui.Speaking);
        }

        void Obj(string text) => ui.SetObjective(null, () => text);
        void Obj(Func<string> f) => ui.SetObjective(null, f);
        void Chapter(int n, string title) { ui.chapterTitle = "CHAPTER " + n + "  -  " + title; }

        IEnumerator Until(Func<bool> cond, Action botSolve = null, float botDelay = 1.0f)
        {
            float t = 0; bool solved = false;
            while (!cond())
            {
                t += Time.deltaTime;
                if (bot && botSolve != null && !solved && t > botDelay) { solved = true; botSolve(); }
                yield return null;
            }
        }

        void Special(string id, string prompt, Action act)
        {
            special[id] = (prompt, act);
            if (!specialOrder.Contains(id)) specialOrder.Add(id);
        }
        void Unspecial(string id) { special.Remove(id); specialOrder.Remove(id); }

        Interactable Use(string prop, string prompt, Action act, bool show = true)
        {
            var it = bus.Prop(prop);
            if (show) it.gameObject.SetActive(true);
            it.Set(prompt, act);
            it.highlight = true;
            return it;
        }

        void Evidence(string prop, string prompt, string title, string body, Action after = null)
        {
            Interactable it = null;
            it = Use(prop, prompt, () => { it.Clear(); ui.ShowDoc(title, body, after); });
        }

        ManifestRow Row(string id) => data.rows.Find(r => r.id == id);
        ManifestRow AddRow(Passenger p, string dest)
        {
            var r = Row(p.id);
            if (r == null) { r = new ManifestRow { id = p.id, name = p.displayName, seat = p.seatLabel, dest = dest }; data.rows.Add(r); }
            return r;
        }
        bool Marked(string id, int status) { var r = Row(id); return r != null && r.status == status; }
        public int ManifestCount() => data.rows.Count(r => r.status == 1);
        int VisibleAboard() => P.Values.Count(p => p.Aboard);

        public void OnMark(ManifestRow row, int status)
        {
            if (ending || row.status == status) return;
            if (!P.TryGetValue(row.id, out var p)) return;
            bool wrong = (status == 2 && p.Aboard) || (status == 1 && !p.Aboard);
            row.status = status;
            au.Play2D(au.tear, 0.12f, 2.6f);
            if (wrong)
            {
                F.errors++;
                Debug.Log("[TLP] Manifest error #" + F.errors + " (" + row.id + " -> " + status + ")");
                SpawnCrowd();
                if (status == 2) au.PlayAt(au.whisper, new Vector3(0, 1.4f, -4.5f), 0.5f);
            }
        }

        public void OnRemember(ManifestRow row, bool on) { if (!ending) { row.remembered = on; au.Play2D(au.tear, 0.1f, 2.8f); } }

        public bool BellPending => bellAction != null;

        void RingBell()
        {
            if (Tutorial.I) Tutorial.I.bellRings++;
            au.PlayAt(au.bell, bus.bellPoint.position, 0.8f);
            if (bellAction != null) { var a = bellAction; bellAction = null; a(); }
            else if (Time.time > nextGrumble && !ending) { nextGrumble = Time.time + 25f; ui.Say("RAGHAVAN", "Not now, conductor."); }
        }

        IEnumerator WaitBell(string objective, bool doubleRing)
        {
            bool rung = false;
            bellAction = () =>
            {
                rung = true;
                if (doubleRing) StartCoroutine(Delayed(0.35f, () => au.PlayAt(au.bell, bus.bellPoint.position, 0.8f)));
            };
            bus.Prop("BellFront").highlight = true; bus.Prop("BellRear").highlight = true;
            Obj(objective);
            yield return Until(() => rung, () => RingBell());
            bus.Prop("BellFront").highlight = false; bus.Prop("BellRear").highlight = false;
        }

        IEnumerator Delayed(float s, Action a) { yield return new WaitForSeconds(s); a(); }

        void OfferTicket(Passenger p, Action after)
        {
            p.needsTicket = true;
            Special(p.id, "Issue ticket - " + p.displayName, () => ui.OpenTicket(p, currentStop, dest =>
            {
                if (dest == p.destination)
                {
                    PrintTicket(p);
                    Unspecial(p.id);
                    after?.Invoke();
                }
                else ui.Say(p.displayName.ToUpper(), "No, no. " + p.destination + ".");
            }));
        }

        void PrintTicket(Passenger p)
        {
            au.PlayAt(au.printer, pl.cam.transform.position + pl.transform.forward * 0.3f - Vector3.up * 0.4f, 0.7f);
            p.ticketed = true; p.needsTicket = false;
            AddRow(p, p.destination);
            int idx = Array.IndexOf(Stops, p.destination);
            int fare = idx >= 0 ? Fares[idx] : 0;
            if (p.child) fare /= 2;
            ui.Toast("<b>KAVERIPURAM TRANSPORT</b>\nROUTE 117 · NIGHT SERVICE\n\n" + currentStop.ToUpper() + "\n  ->  " + p.destination.ToUpper() +
                     "\n\nRs " + fare + (p.child ? "  CHILD" : "  ADULT") + "\n07-11-2015   01:" + (17 + data.rows.Count) + "\nNo. 0041" + (17 + data.rows.Count), 5f, true);
            ui.Say("", "Mark " + p.displayName + " aboard in the manifest (TAB).");
        }

        IEnumerator CountCheck(int truth, string objective)
        {
            bool done = false; int ans = -1;
            ui.RequestCount(n => { ans = n; done = true; });
            Obj(objective);
            yield return Until(() => done, () => ui.AnswerCount(truth));
            ui.manifestOpen = false;
            if (ans != truth)
            {
                F.errors++;
                SpawnCrowd();
                yield return Talk(("RAGHAVAN", "That's not what I count. Count properly."));
            }
            else ui.Toast("Count confirmed: " + truth + " passengers.", 3f);
        }

        string PassengerPrompt(Passenger p)
        {
            if (ending) return "";
            if (special.TryGetValue(p.id, out var s)) return s.prompt;
            if (p != driver && !p.Aboard) return "";
            if (p.state == PState.Walking) return "";
            return "Talk - " + p.displayName;
        }

        void PassengerUse(Passenger p)
        {
            if (special.TryGetValue(p.id, out var s)) { s.act(); return; }
            if (ui.Speaking) return;
            string[] lines;
            switch (p.id)
            {
                case "meena": lines = new[] { "Visiting hours end at two. They'll still let me in, no?", "My mother keeps the window open for me. Every night.", "Is it far still?" }; break;
                case "selvam": lines = new[] { "Tea, conductor? No? The flask is still warm. It's always warm.", "Two rupees a cup. Some things don't change.", "You look tired. Night duty is no joke." }; break;
                case "kavin": lines = new[] { "Still no network. Dead zone or what?", "My phone died at one twenty-four. Exactly. Weird, no?", "Anna, wake me at River Road. Don't forget." }; break;
                case "lakshmi": lines = new[] { "My son will come to the bus stand. He always comes.", "Sit, child. You've been standing all night.", "I know your face from somewhere." }; break;
                case "bala": lines = new[] { "My other shoe fell off. Don't tell Amma.", "Is the river big tonight?", "Uncle, why is your uniform wet?" }; break;
                default: lines = new[] { "Eyes on the passengers, conductor.", "Keep the count. That's your job.", "Don't bother me while I'm driving." }; break;
            }
            idleIdx.TryGetValue(p.id, out int i);
            idleIdx[p.id] = i + 1;
            if (p != driver) p.LookAt(pl.cam.transform.position);
            ui.Say(p == driver ? "RAGHAVAN" : p.displayName.ToUpper(), lines[i % lines.Length]);
            if (p.id == "selvam") au.PlayAt(au.clink, p.transform.position + Vector3.up, 0.6f);
        }

        void Shot(string name)
        {
            if (string.IsNullOrEmpty(shotDir)) return;
            try
            {
                Directory.CreateDirectory(shotDir);
                ScreenCapture.CaptureScreenshot(Path.Combine(shotDir, (shotIdx++).ToString("00") + "_" + name + ".png"));
            }
            catch (Exception e) { Debug.LogWarning(e); }
        }

        Vector3 Seat(string id) => P[id].seatPos + Vector3.up * 0.6f;

        // ================================================================== TRAINING (before chapter 1)

        public bool inTraining;
        public Func<Vector3> markerOverride;

        /// <summary>Where the on-screen objective marker should point right now (null = nowhere).</summary>
        public Vector3? CurrentTarget
        {
            get
            {
                if (ending || ui == null || !ui.gameStarted || ui.cinematic || pl == null) return null;
                if (markerOverride != null) return markerOverride();
                foreach (var id in specialOrder)
                {
                    Passenger p = id == "raghavan" ? driver : (P.TryGetValue(id, out var q) ? q : null);
                    if (p && p.gameObject.activeInHierarchy)
                        return (p.head ? p.head.position : p.transform.position + Vector3.up * 1.5f) + Vector3.up * 0.35f;
                }
                foreach (var kv in bus.props)
                {
                    var it = kv.Value;
                    if (kv.Key.StartsWith("Bell") || !it.highlight || !it.Usable || !it.gameObject.activeInHierarchy) continue;
                    return it.transform.position + Vector3.up * 0.12f;
                }
                if (BellPending)
                {
                    var a = bus.Prop("BellFront").transform.position; var b = bus.Prop("BellRear").transform.position;
                    return (pl.transform.position - a).sqrMagnitude < (pl.transform.position - b).sqrMagnitude ? a : b;
                }
                return null;
            }
        }

        /// <summary>
        /// A short, mandatory practice run at the depot. Murugan, the timekeeper, creates every situation the job needs
        /// (a passenger to ticket, a count, someone in the mirror, someone getting off), and each step must be done to continue.
        /// </summary>
        IEnumerator Training()
        {
            inTraining = true;
            var t = Tutorial.I;
            const int N = 15;
            int step = 0;
            void Step(string title, string text)
            {
                step++;
                t.Force(step, N, title, text);
                ui.chapterTitle = "TRAINING  -  BEFORE THE SHIFT";
                Obj(text);
                au.Play2D(au.clink, 0.12f, 1.4f);
                Debug.Log("[TLP] Training step " + step + ": " + title);
                if (bot) StartCoroutine(Delayed(0.4f, () => Shot("train" + step.ToString("00") + "_" + title.Replace(' ', '_'))));
            }
            var mur = P["murugan"];
            P["kavin"].Hide(); P["lakshmi"].Hide();
            mur.PlaceAt(new Vector3(-3.6f, -0.15f, 7.4f), 210, PState.Standing);
            pl.Teleport(new Vector3(-7.0f, -0.13f, 1.8f), 20);
            ui.Card("TRAINING", "Murugan, the depot timekeeper, will show you the job.\nDo each step to move on to the next.");
            yield return new WaitForSeconds(4f);
            ui.HideCard();

            // 1. Look
            float look0 = pl.lookTravel;
            Step("LOOK", "Move the MOUSE to look around the depot.");
            yield return Until(() => pl.lookTravel - look0 > 150f, () => pl.lookTravel += 400f);

            // 2. Walk
            markerOverride = () => mur.transform.position + Vector3.up * 2.1f;
            Step("WALK", "Walk to Murugan using  W A S D. The yellow marker shows where to go.");
            yield return Until(() => Vector3.Distance(pl.transform.position, mur.transform.position) < 2.2f,
                () => { pl.Teleport(mur.transform.position + new Vector3(-1.4f, 0.02f, -0.6f), 70); pl.Aim(mur.head.position); });
            markerOverride = null;

            // 3. Interact
            bool talked = false;
            Special("murugan", "Talk - Murugan", () => { Unspecial("murugan"); talked = true; });
            Step("INTERACT", "Look at Murugan. When the dot in the middle of the screen turns orange, press  E  (or left-click).");
            yield return Until(() => talked);

            // 4. Skip a subtitle line
            mur.LookAt(pl.cam.transform.position);
            t.spaceSkipped = false;
            ui.Say("MURUGAN", "Ah, the relief conductor. Good. Raghavan won't wait for anyone tonight.");
            ui.Say("MURUGAN", "Before you go, I'll show you the job. Five minutes. You can practise on me.");
            ui.Say("MURUGAN", "I'll be your passenger. Pretend I'm going to Old Market.");
            ui.Say("MURUGAN", "Those words at the bottom keep coming until you skip them. Go on, try it.");
            Step("SKIP A LINE", "Subtitles move on by themselves. Press  SPACE  now to skip to the next line.");
            yield return Until(() => t.spaceSkipped, () => { t.spaceSkipped = true; ui.ClearLines(); });

            // 5. Board
            markerOverride = () => new Vector3(-1.05f, 1.2f, 4.6f);
            Step("BOARD THE BUS", "Climb aboard through the open front door of the bus (marked).");
            yield return Until(() => pl.transform.position.x > -1.1f, () => pl.Teleport(new Vector3(-0.4f, 0.02f, 4.2f), 180));
            markerOverride = null;

            // 6. Take the manifest
            bool got = false;
            Use("Manifest", "Take the manifest", () =>
            {
                got = true; ui.hasManifest = true;
                bus.ShowObj("Manifest", false);
                bus.Prop("Manifest").Clear();
            });
            Step("THE MANIFEST", "Take the passenger manifest from its hook beside the door. Look at it and press  E.");
            yield return Until(() => got);

            // 7. Open and close it
            bool opened = false;
            Step("OPEN THE MANIFEST", "Press  TAB  (or M) to open the manifest. Look it over, then press  TAB  again to close it.");
            yield return Until(() => { if (ui.manifestOpen) opened = true; return opened && !ui.manifestOpen; }, () => opened = true);

            // 8. Issue a ticket: Murugan boards and asks for one.
            StartCoroutine(TraineeBoards(mur));
            bool ticketDone = false;
            OfferTicket(mur, () => { ticketDone = true; ui.Say("MURUGAN", "Old Market, eight rupees. Correct."); });
            Step("ISSUE A TICKET", "Murugan is boarding and wants a ticket to OLD MARKET. Look at him, press  E, then pick Old Market on the ticket machine.");
            yield return Until(() => ticketDone);

            // 9. Mark aboard
            Step("MARK ABOARD", "Open the manifest (TAB) and press  ABOARD  on Murugan's row. Then close it (TAB).");
            yield return Until(() => Marked("murugan", 1) && !ui.manifestOpen, () => { var r = Row("murugan"); if (r != null) OnMark(r, 1); });

            // 10. Count
            Step("COUNT", "The driver needs a head count. Open the manifest, set the number to the passengers you can SEE (only Murugan), then press  Confirm count.");
            bool countOk = false;
            while (!countOk)
            {
                bool done = false; int ans = -1;
                ui.RequestCount(n => { ans = n; done = true; });
                yield return Until(() => done, () => ui.AnswerCount(1));
                ui.manifestOpen = false;
                Debug.Log("[TLP] Training count answer " + ans);
                if (ans == 1) countOk = true;
                else ui.Toast("Not quite: count only the people you can actually see aboard. That's just Murugan. Try again.", 5f);
            }
            yield return new WaitForSeconds(0.5f);

            // 11. Mirror: Murugan walks to the back.
            yield return new WaitUntil(() => mur.state == PState.Seated); // he may still be walking to his seat
            ui.Say("MURUGAN", "Now watch. I'll go to the back. Don't turn around.");
            Debug.Log("[TLP] Training: Murugan walks to the back from " + mur.transform.position + " state=" + mur.state);
            mur.Sit(false);
            yield return mur.Walk(new Vector3(0, 0, mur.seatPos.z), new Vector3(0, 0, -4.3f));
            mur.transform.rotation = Quaternion.identity;
            mur.state = PState.Standing;
            float held = 0;
            Step("THE MIRROR", "Murugan is at the back of the bus. Without turning around, hold  SHIFT  to glance at the rear-view mirror and find him.");
            yield return Until(() => { if (pl.mirrorHeld) held += Time.deltaTime; return held > 1.5f; },
                () => { pl.Aim(new Vector3(-0.66f, 1.9f, 5.4f)); pl.forceMirror = true; });
            pl.forceMirror = false;
            ui.Say("MURUGAN", "See me? Use that mirror often. It shows the whole bus behind you.");
            yield return new WaitForSeconds(1.5f);

            // 12. Mark exited: Murugan gets off at the rear door.
            yield return bus.Door(false, true);
            mur.leaving = true;
            yield return mur.Walk(Passenger.RearAisle, Passenger.RearDoor, Passenger.RearOutside, new Vector3(-6f, -0.15f, -5f));
            mur.gameObject.SetActive(false); mur.leaving = false; mur.state = PState.Gone;
            yield return bus.Door(false, false);
            Step("MARK EXITED", "Murugan got off through the rear door. Open the manifest and press  EXITED  on his row, then close it.");
            yield return Until(() => Marked("murugan", 2) && !ui.manifestOpen, () => { var r = Row("murugan"); if (r != null) OnMark(r, 2); });

            // 13. Read something
            bool read = false;
            Evidence("RouteCard", "Read the route card", "ROUTE CARD  -  117 NIGHT SERVICE",
                "1.  Old Market Depot\n2.  Old Market\n3.  Hospital Road\n4.  Cinema Street\n5.  River Road\n6.  Central Bus Stand\n\nNotes, tickets and papers you find tonight open like this. Some of them matter later.\n\nPress  E  to close.",
                () => read = true);
            Step("READ", "Look at the route card on the dashboard (marked) and press  E  to read it. Press  E  again to close it.");
            yield return Until(() => read);

            // 14. Bell
            bool rung = false;
            bellAction = () => rung = true;
            bus.Prop("BellFront").highlight = true; bus.Prop("BellRear").highlight = true;
            Step("THE BELL", "Press  E  on the red bell button beside a door. Two rings tell the driver to go; one ring asks him to stop.");
            yield return Until(() => rung, () => RingBell());
            bus.Prop("BellFront").highlight = false; bus.Prop("BellRear").highlight = false;
            ui.Say("RAGHAVAN", "Not yet. Finish your training first.");

            // 15. Pause
            bool sawPause = false;
            Step("PAUSE", "Press  ESC  to pause the game, then click  Resume  to come back. (How to play is also in that menu.)");
            yield return Until(() => { if (ui.paused) sawPause = true; return sawPause && !ui.paused; }, () => sawPause = true);

            // Done: clean up everything the practice changed.
            t.Force(0, 0, null, null);
            special.Clear(); specialOrder.Clear(); bellAction = null; markerOverride = null;
            ui.ClearLines();
            ui.Card("TRAINING COMPLETE", "You know the job. Now take the last service of the night.");
            Debug.Log("[TLP] Training complete");
            yield return new WaitForSeconds(3.5f);
            ui.HideCard();
            ui.fadeSpeed = 1.2f; ui.fadeTarget = 1;
            yield return new WaitForSeconds(1f);
            data = new SaveData();
            foreach (var c in crowd) if (c) Destroy(c);
            crowd.Clear();
            ui.hasManifest = false;
            t.ResetAll();
            inTraining = false;
            SetupState(1, false);
            ui.fadeTarget = 0; ui.fadeSpeed = 0.6f;
        }

        IEnumerator TraineeBoards(Passenger mur)
        {
            yield return mur.Walk(Passenger.FrontOutside);
            yield return mur.BoardFront();
        }

        // ================================================================== SCARES

        /// <summary>Stinger, red flash, camera shake and a heartbeat spike.</summary>
        void Scare(float s)
        {
            au.PlayAt(au.stinger, pl.cam.transform.position, Mathf.Clamp01(0.45f + s * 0.55f), 1f, 0f);
            ui.Flash(new Color(0.55f, 0f, 0f), 0.35f * s);
            pl.Shake(0.25f + 0.6f * s);
            au.SetHeart(Mathf.Max(0.35f, s * 0.9f));
            StartCoroutine(Delayed(5f, () => au.SetHeart(0.1f)));
            Debug.Log("[TLP] Scare " + s.ToString("0.0"));
            if (bot) StartCoroutine(Delayed(0.12f, () => Shot("scare_ch" + F.chapter)));
        }

        /// <summary>Puts a figure right in front of the camera for a moment.</summary>
        IEnumerator FaceJump(Passenger who, float seconds, float strength = 1f)
        {
            int oldLayer = who.gameObject.layer;
            B.Layer(who.gameObject, 0);
            var cam = pl.cam.transform;
            var f = cam.forward; f.y = 0; f.Normalize();
            var pos = cam.position + f * 0.5f;
            pos.y = cam.position.y - 1.58f * who.transform.localScale.y;
            who.PlaceAt(pos, Mathf.Atan2(-f.x, -f.z) * Mathf.Rad2Deg, PState.Standing);
            who.LookAt(cam.position);
            Scare(strength);
            yield return new WaitForSeconds(seconds);
            who.Hide();
            who.LookForward();
            B.Layer(who.gameObject, oldLayer);
        }

        /// <summary>Ch1: the knocking turns violent as the player gets close to the rear door.</summary>
        IEnumerator RearDoorViolence(Func<bool> stop)
        {
            var door = new Vector3(-1.2f, 1.1f, -2.6f);
            float t = 0;
            while (!stop() && Vector3.Distance(pl.transform.position, door) > 3.2f && t < 30f) { t += Time.deltaTime; yield return null; }
            if (stop()) yield break;
            for (int i = 0; i < 4; i++)
            {
                au.PlayAt(au.rattleHit, door, 1f, UnityEngine.Random.Range(0.85f, 1.05f));
                var leaf = bus.rearDoor;
                var basePos = leaf.localPosition;
                for (float s = 0; s < 0.18f; s += Time.deltaTime) { leaf.localPosition = basePos + new Vector3(UnityEngine.Random.Range(-0.03f, 0.03f), 0, 0); yield return null; }
                leaf.localPosition = basePos;
                if (i == 1) Scare(0.5f);
                yield return new WaitForSeconds(0.22f);
            }
            au.PlayAt(au.scrape, door, 0.7f);
        }

        /// <summary>Ch3: a wet face presses against the window of the moving bus, right beside you.</summary>
        IEnumerator WindowFace()
        {
            float t = 0;
            while (t < 25f && Vector3.Dot(pl.cam.transform.forward, Vector3.right) < 0.55f) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(0.4f);
            var face = PersonBuilder.Copy(P["meena"], transform, "window_face");
            Destroy(face.GetComponent<Collider>()); face.interact.active = false;
            float z = Mathf.Clamp(pl.transform.position.z, -4.5f, 3.5f);
            face.PlaceAt(new Vector3(1.42f, 0.05f, z), -90, PState.Standing);
            face.LookAt(pl.cam.transform.position);
            au.PlayAt(au.rattleHit, new Vector3(1.25f, 1.5f, z), 1f, 1.3f);
            Scare(0.75f);
            yield return new WaitForSeconds(1.1f);
            au.PlayAt(au.scrape, new Vector3(1.3f, 1.4f, z), 0.6f, 1.4f);
            Destroy(face.gameObject);
            ui.Say("", "There was a face on the glass. The bus is doing forty.");
        }

        /// <summary>Ch6: the silent man walks down the aisle only while nobody is looking at him.</summary>
        IEnumerator Stalker(Func<bool> done)
        {
            var man = silentMan;
            B.Layer(man.gameObject, 0);
            var rear = new Vector3(0, 0, -4.6f);
            man.PlaceAt(rear, 0, PState.Standing);
            int caught = 0; float stepT = 0, breathT = 0;
            ui.dread = 0.85f;
            while (!done() && !ending)
            {
                var head = man.head ? man.head.position : man.transform.position + Vector3.up * 1.6f;
                var pos = man.transform.position;
                var toPl = pl.transform.position - pos; toPl.y = 0;
                float dist = toPl.magnitude;
                bool watched = !pl.mirrorHeld && !ui.Modal && pl.LookingAt(head, 26f);
                if (!watched && dist > 0.75f)
                {
                    Vector3 target;
                    if (Mathf.Abs(pos.z - pl.transform.position.z) > 0.7f) target = new Vector3(0, 0, pl.transform.position.z);
                    else target = new Vector3(pl.transform.position.x, 0, pl.transform.position.z);
                    var d = target - pos; d.y = 0;
                    if (d.sqrMagnitude > 0.0004f)
                    {
                        man.transform.position = pos + d.normalized * Mathf.Min(d.magnitude, 0.8f * Time.deltaTime);
                        man.transform.rotation = Quaternion.RotateTowards(man.transform.rotation, Quaternion.LookRotation(d.normalized), 300 * Time.deltaTime);
                    }
                    man.state = PState.Walking;
                    stepT += Time.deltaTime;
                    if (stepT > 0.6f) { stepT = 0; au.PlayAt(au.step, man.transform.position, 0.55f, 0.75f); }
                }
                else
                {
                    man.state = PState.Standing;
                    man.FaceFlat(pl.transform.position);
                    man.LookAt(pl.cam.transform.position);
                }
                au.SetHeart(Mathf.Clamp01(1.25f - dist / 6f));
                breathT += Time.deltaTime;
                if (dist < 2.5f && breathT > 3f) { breathT = 0; au.PlayAt(au.breath, head, 0.8f); }
                if (dist < 0.75f)
                {
                    caught++;
                    Debug.Log("[TLP] Stalker caught the player (" + caught + ")");
                    var cam = pl.cam.transform;
                    var f = cam.forward; f.y = 0; f.Normalize();
                    man.PlaceAt(new Vector3(cam.position.x + f.x * 0.45f, 0, cam.position.z + f.z * 0.45f), Mathf.Atan2(-f.x, -f.z) * Mathf.Rad2Deg, PState.Standing);
                    man.LookAt(cam.position);
                    Scare(1f);
                    yield return new WaitForSeconds(0.45f);
                    bus.SetAllLights(false);
                    man.PlaceAt(rear, 0, PState.Standing);
                    yield return new WaitForSeconds(0.9f);
                    foreach (var p in P.Values) if (p.named) bus.SetLight(p.lightIndex, true);
                    ui.Say("", caught == 1 ? "He is back on the last bench. Keep your eyes on him." : "Back on the last bench. Again.");
                    yield return new WaitForSeconds(1.5f);
                }
                yield return null;
            }
            man.Hide();
            man.LookForward();
            B.Layer(man.gameObject, B.MirrorLayer);
            au.SetHeart(0.1f);
            ui.dread = 0.6f;
        }

        /// <summary>Small unexplained things between the scripted events, so the bus never feels safe.</summary>
        IEnumerator AmbientDread()
        {
            var used = new HashSet<int>();
            while (!ending)
            {
                yield return new WaitForSeconds(UnityEngine.Random.Range(30f, 55f));
                if (inTraining || ending || F.chapter < 2 || F.chapter > 6 || ui.cinematic || ui.Speaking) continue;
                if (used.Count >= 6) used.Clear();
                int k; int tries = 0;
                do { k = UnityEngine.Random.Range(0, 6); tries++; } while (used.Contains(k) && tries < 30);
                used.Add(k);
                var behind = pl.transform.position - pl.transform.forward * 0.6f + Vector3.up * 1.55f;
                switch (k)
                {
                    case 0: au.PlayAt(au.breath, behind, 0.85f); break;
                    case 1: au.PlayAt(au.whisper, behind, 0.75f); break;
                    case 2: StartCoroutine(FootstepsBehind()); break;
                    case 3:
                        int li = 0; float best = 999;
                        for (int i = 0; i < bus.cabinLights.Length; i++)
                        { float dz = Mathf.Abs(bus.cabinLights[i].transform.position.z - pl.transform.position.z); if (dz < best) { best = dz; li = i; } }
                        if (bus.cabinLights[li].enabled) StartCoroutine(bus.Flicker(li, 1.5f));
                        au.PlayAt(au.scrape, new Vector3(1.3f, 1.6f, pl.transform.position.z - 1f), 0.4f, 0.8f);
                        break;
                    case 4:
                        var aboard = P.Values.Where(p => p.Aboard && p.id != "murugan" && p.gameObject.activeInHierarchy).ToList();
                        if (aboard.Count > 0)
                        {
                            var p = aboard[UnityEngine.Random.Range(0, aboard.Count)];
                            p.LookAt(pl.cam.transform.position);
                            StartCoroutine(Delayed(8f, () => p.LookForward()));
                        }
                        break;
                    case 5: au.PlayAt(au.printer, bus.Prop("TicketMachine").transform.position, 0.6f, 0.9f); break;
                }
                Debug.Log("[TLP] Ambient dread event " + k);
            }
        }

        IEnumerator FootstepsBehind()
        {
            for (int i = 0; i < 4; i++)
            {
                var pos = pl.transform.position - pl.transform.forward * (3.2f - i * 0.75f);
                au.PlayAt(au.step, pos, 0.65f, 0.8f);
                yield return new WaitForSeconds(0.6f);
            }
        }

        // ================================================================== CHAPTER 1

        IEnumerator Chapter1()
        {
            Chapter(1, "THE EXTRA SHIFT");
            ui.Card("THE LAST PASSENGER", "Kaveripuram  ·  01:17 A.M.  ·  Route 117, the last service of the night");
            yield return new WaitForSeconds(4.5f);
            ui.HideCard();
            Obj("Board the bus.");
            yield return Until(() => pl.transform.position.x > -1.15f, () => pl.Teleport(new Vector3(-0.4f, 0.02f, 4.3f), 180));
            yield return Talk(("RAGHAVAN", "You're late. Put your bag there. Last service."), ("RAGHAVAN", "We don't wait after the river stop."));

            bool gotManifest = false, checkedMachine = false;
            Use("Manifest", "Take the manifest", () =>
            {
                gotManifest = true; ui.hasManifest = true;
                bus.ShowObj("Manifest", false);
                bus.Prop("Manifest").Clear();
                ui.Toast("Manifest taken. Press TAB to open it at any time.", 4f);
            });
            Evidence("TicketMachine", "Check the ticket machine", "TICKET MACHINE  -  SELF TEST",
                "ROUTE 117  ·  NIGHT SERVICE\nDATE      07-11-2015\nTIME      01:17\nBATTERY   LOW\nCONDUCTOR ________\n\nThe test ticket prints cleanly.\nThe date is eleven years wrong. The clock on the dashboard says 2026.",
                () => { checkedMachine = true; au.PlayAt(au.printer, bus.Prop("TicketMachine").transform.position, 0.6f); });
            Obj(() => "Take the manifest from the hook" + (gotManifest ? " (done)" : "") + " and check the ticket machine" + (checkedMachine ? " (done)" : "") + ".");
            yield return Until(() => gotManifest && checkedMachine);

            // Boarding
            var meena = P["meena"]; var bala = P["bala"];
            meena.PlaceAt(new Vector3(-5.5f, -0.15f, 1.5f), 60, PState.Standing);
            bala.PlaceAt(new Vector3(-6.5f, -0.15f, 9f), 140, PState.Standing);
            StartCoroutine(Board(meena, 0f));
            StartCoroutine(Board(bala, 3.5f));
            yield return new WaitForSeconds(1.2f);
            yield return Talk(("MEENA", "Hospital Road. One ticket. Please, I don't want to miss the visiting hour."));

            int marked() => new[] { "meena", "kavin", "lakshmi", "bala" }.Count(id => Marked(id, 1));
            int tickets() => new[] { "meena", "kavin", "lakshmi", "bala" }.Count(id => P[id].ticketed);
            Obj(() => "Issue a ticket to every passenger (" + tickets() + "/4) and mark each one ABOARD in the manifest (" + marked() + "/4).");
            OfferTicket(meena, () => StartCoroutine(Talk(("MEENA", "Thank you. My mother is in Ward 3."))));
            OfferTicket(P["kavin"], () => StartCoroutine(Talk(("KAVIN", "River Road, anna. My phone's dead, so if I fall asleep, wake me."))));
            OfferTicket(P["lakshmi"], () => StartCoroutine(Talk(("LAKSHMI", "Central bus stand, child. My son is waiting for me there."))));
            OfferTicket(bala, () => StartCoroutine(Talk(("BALA", "Uncle... is this the bus that goes over the big bridge?"), ("RAGHAVAN", "Not tonight. Sit down."))));
            yield return Until(() => tickets() == 4 && marked() == 4);
            Shot("ch1_tickets");
            yield return new WaitUntil(() => !ui.Speaking);

            yield return Talk(("RAGHAVAN", "Count properly. Once we leave the depot, I won't turn back."));
            ui.Toast("Hold SHIFT to glance at the rear-view mirror. It shows the whole cabin behind you.", 6f);
            yield return CountCheck(4, "Count the passengers. Open the manifest (TAB) and confirm how many you can see.");
            Shot("ch1_counted");

            // The knock at the rear door
            yield return new WaitForSeconds(2f);
            var rearPos = new Vector3(-1.3f, 1.1f, -2.6f);
            au.PlayAt(au.knock, rearPos, 1f);
            yield return new WaitForSeconds(1.6f);
            au.PlayAt(au.knock, rearPos, 1f, 0.9f);
            yield return new WaitForSeconds(2.4f);
            au.PlayAt(au.knock, rearPos, 0.8f, 0.85f);
            bool inspected = false;
            Use("RearDoor", "Inspect the rear door", () =>
            {
                bus.Prop("RearDoor").Clear();
                bus.ShowObj("RearWet", true);
                StartCoroutine(RearDoorOpen(() => inspected = true));
            });
            StartCoroutine(RearDoorViolence(() => inspected));
            Obj("Something knocked on the closed rear door. Walk down the aisle to the rear door (left side, behind the last left-hand seats before the back), look at it and press E.");
            yield return Until(() => inspected, null);
            F.wetHandle = true;

            bool readCard = false;
            Evidence("RouteCard", "Read the route card", "ROUTE CARD  -  117 NIGHT SERVICE",
                "1.  Old Market Depot\n2.  Old Market\n3.  Hospital Road\n4.  Cinema Street\n5.  River Road\n6.  Central Bus Stand\n\nHandwritten at the bottom, pressed hard into the card:\n\n<i>Do not stop on the flyover.</i>",
                () => { readCard = true; F.routeCard = true; });
            Obj("Read the route card on the dashboard.");
            yield return Until(() => readCard);

            yield return WaitBell("Ring the bell twice to tell the driver to go.", true);
            yield return Talk(("RAGHAVAN", "Right."));
            StartCoroutine(bus.Door(true, false));
            yield return new WaitForSeconds(1.2f);
            route.zone = Zone.City;
            route.Cruise();
        }

        IEnumerator Board(Passenger p, float delay)
        {
            yield return new WaitForSeconds(delay);
            yield return p.Walk(Passenger.FrontOutside);
            yield return p.BoardFront();
        }

        IEnumerator RearDoorOpen(Action done)
        {
            yield return bus.Door(false, true);
            ui.Say("", "The handle is wet. Fresh water, as if someone just held it with a soaked hand. The road outside is empty.");
            au.PlayAt(au.knock, new Vector3(-2.0f, 1.0f, -2.6f), 0.5f, 0.7f);
            yield return new WaitForSeconds(4f);
            yield return bus.Door(false, false);
            done();
        }

        // ================================================================== CHAPTER 2

        IEnumerator Chapter2()
        {
            Chapter(2, "OLD MARKET");
            Obj("Ride to Old Market. Talk to the passengers if you like.");
            yield return new WaitForSeconds(7f);
            yield return Talk(("KAVIN", "Anna, your machine says 2015. Is the date wrong?"), ("RAGHAVAN", "Old battery. Don't worry about it."));
            yield return new WaitForSeconds(6f);
            route.zone = Zone.Market;
            bus.SetDisplay("117  OLD MARKET");
            yield return route.DriveToStop(150f, (w, z) => { var g = route.BuildShelter(w, z, "Old Market"); route.Spawned(route.BuildTeaStall(w, z + 7f)); return g; });
            currentStop = "Old Market";
            yield return bus.Door(true, true);

            var selvam = P["selvam"];
            selvam.PlaceAt(new Vector3(-4.4f, -0.15f, 6.8f), 200, PState.Standing);
            yield return selvam.Walk(Passenger.FrontOutside);
            StartCoroutine(selvam.BoardFront());
            yield return Talk(("SELVAM", "Last bus, ah? Good. I thought they'd stopped this route."));
            bool talked = false;
            Special("selvam", "Talk - the new passenger", () =>
            {
                Unspecial("selvam");
                StartCoroutine(TalkThen(() => talked = true,
                    ("YOU", "Where are you going?"),
                    ("SELVAM", "Same place as always. Near the tea stall. You know the one."),
                    ("RAGHAVAN", "He doesn't need a ticket. Just let him sit.")));
            });
            Obj("Talk to the new passenger.");
            yield return Until(() => talked);
            AddRow(selvam, "- (no ticket)");
            selvam.ticketed = true;
            Obj("Record Selvam in the manifest (TAB -> Aboard).");
            yield return Until(() => Marked("selvam", 1));
            yield return WaitBell("Ring the bell twice to depart.", true);
            StartCoroutine(bus.Door(true, false));
            yield return new WaitForSeconds(1.2f);
            route.zone = Zone.City;
            route.Cruise();
            StartCoroutine(FlaskClinks());

            Obj("Ride on.");
            yield return new WaitForSeconds(10f);
            route.zone = Zone.Hospital;
            bus.SetDisplay("117  HOSPITAL ROAD");
            yield return Talk(("MEENA", "Conductor, Hospital Road is next. Ring the bell, please."));
            yield return WaitBell("Ring the bell for Hospital Road.", false);
            yield return Talk(("RAGHAVAN", "Hospital."));
            yield return route.DriveToStop(110f, (w, z) => route.BuildHospitalGate(w, z));
            currentStop = "Hospital Road";
            yield return bus.Door(true, true);
            var meena = P["meena"];
            meena.leaving = true;
            ui.Say("MEENA", "Does the hospital still have the blue gate?");
            yield return meena.ExitFront(new Vector3(-5.6f, -0.15f, 13f));
            yield return bus.Door(true, false);

            Obj("Mark Meena as EXITED in the manifest.");
            yield return Until(() => Marked("meena", 2));

            // Mirror seat: her reflection stays seated a few seconds.
            StartCoroutine(MirrorMeena());
            au.PlayAt(au.step, P["meena"].seatPos + Vector3.up * 0.4f, 0.25f, 0.6f);
            bus.ShowObj("WetSeat", true);
            bool lookedSeat = false;
            Evidence("MeenaTicket", "Pick up the ticket on her seat", "A TICKET, LEFT ON THE SEAT",
                "KAVERIPURAM TRANSPORT  ·  ROUTE 117\nOLD MARKET DEPOT  ->  HOSPITAL ROAD\nRs 10  ·  ADULT\n<b>07-11-2015   01:21</b>\n\nThe paper is soaked through and cold. It is not the ticket you printed for her. It is eleven years old.",
                () => { F.meenaTicket = true; lookedSeat = true; });
            Obj("Check her seat.");
            yield return Until(() => lookedSeat || (Vector3.Distance(pl.transform.position, P["meena"].seatPos) < 1.7f && pl.LookingAt(Seat("meena"), 25f)),
                () => { pl.Teleport(new Vector3(0.1f, 0.02f, 2.6f), 30); pl.Aim(Seat("meena")); pl.forceMirror = true; });
            if (bot) { pl.forceMirror = true; yield return new WaitForSeconds(0.6f); Shot("ch2_mirror_meena"); pl.forceMirror = false; }
            yield return new WaitForSeconds(0.6f);
            bus.ShowObj("Handprint", true);
            au.PlayAt(au.knock, new Vector3(1.3f, 1.4f, 3.4f), 0.25f, 1.6f);
            ui.Say("", "The seat is soaked. No rain has come into the bus. On the window beside it, a handprint is forming from the inside.");
            yield return new WaitForSeconds(4f);
            bus.Prop("MeenaTicket").highlight = false;
            yield return WaitBell("Ring the bell twice to depart.", true);
            route.zone = Zone.City;
            route.Cruise();
        }

        IEnumerator TalkThen(Action after, params (string, string)[] ls) { yield return Talk(ls); after(); }

        IEnumerator FlaskClinks()
        {
            while (!ending)
            {
                yield return new WaitForSeconds(UnityEngine.Random.Range(14f, 30f));
                var s = P["selvam"];
                if (s.Aboard) au.PlayAt(au.clink, s.transform.position + Vector3.up * 0.8f, 0.35f, UnityEngine.Random.Range(0.9f, 1.1f));
            }
        }

        IEnumerator MirrorMeena()
        {
            ghostMeena.PlaceSeated();
            float seenT = 0, idle = 0;
            bool turned = false;
            while (seenT < 4.5f && idle < 45f)
            {
                if (pl.mirrorHeld)
                {
                    seenT += Time.deltaTime; F.seenMirrorMeena = true;
                    if (!turned && seenT > 1.2f)
                    {
                        // her reflection turns its head and looks straight back at you
                        turned = true;
                        ghostMeena.LookAt(bus.mirrorCam.transform.position);
                        au.PlayAt(au.whisper, pl.cam.transform.position + Vector3.up * 0.1f, 0.8f, 0.9f);
                    }
                }
                else if (F.seenMirrorMeena) { if (turned) break; seenT += Time.deltaTime; }
                else idle += Time.deltaTime;
                yield return null;
            }
            ghostMeena.Hide();
            ghostMeena.LookForward();
            if (turned)
            {
                au.PlayAt(au.breath, pl.cam.transform.position - pl.transform.forward * 0.4f, 1f);
                Scare(0.45f);
                ui.Say("", "The moment you look away from the mirror, something breathes against the back of your neck.");
            }
        }

        // ================================================================== CHAPTER 3

        IEnumerator Chapter3()
        {
            Chapter(3, "THE CALL");
            Obj("Ride on toward Cinema Street.");
            yield return new WaitForSeconds(8f);
            yield return Talk(("KAVIN", "Anna, can I borrow your phone? Mine's dead. Amma keeps calling."),
                              ("YOU", "There's no signal here."),
                              ("KAVIN", "No, look. It's ringing. But the screen is black."));
            StartCoroutine(PhoneRing());
            yield return Talk(("RAGHAVAN", "Don't answer calls from this bus."));
            bool answered = false;
            Use("Phone", "Answer the phone", () =>
            {
                bus.Prop("Phone").Clear();
                F.phone = true;
                ui.ShowDoc("A PHONE ON AN EMPTY SEAT",
                    "The screen lights only when you lift it to your ear.\n\n<b>07 NOV 2015   01:24</b>\nINCOMING CALL:  AMMA\n\nA woman's voice, very close, as if she is sitting beside you:\n\n<i>\"Kavin? Get down. The bridge is broken.\"</i>",
                    () => answered = true);
            });
            Obj("Find the ringing phone.");
            yield return Until(() => answered);
            bus.ShowObj("Phone", false);
            StartCoroutine(WindowFace());
            yield return Talk(("KAVIN", "That's my mother. She was at home. She can't be here."),
                              ("LAKSHMI", "Child, you were going home. We all were."));
            yield return new WaitForSeconds(1f);
            au.PlayAt(au.speaker, bus.speakerPoint.position, 0.7f);
            yield return new WaitForSeconds(0.4f);
            ui.Say("SPEAKER  (your own voice)", "Get down. The bridge is broken.");
            au.PlayAt(au.whisper, bus.speakerPoint.position, 0.6f);
            yield return new WaitUntil(() => !ui.Speaking);

            bus.ShowObj("Newspaper", true);
            Interactable paper = null;
            paper = Use("Newspaper", "Pick up the newspaper clipping", () =>
            {
                paper.Clear();
                F.newspaper = true;
                ui.ShowDoc("KAVERIPURAM EXPRESS  ·  8 NOVEMBER 2015",
                    "<b>NIGHT BUS MISSING AFTER FLYOVER COLLAPSE</b>\n\nA section of the unfinished Kaveri flyover gave way during heavy rain shortly after 1:30 a.m. Route 117's last service was in the area.\n\nThe driver, R. Raghavan, was found on the river road and is the only confirmed survivor. Officials say the bus was empty. The passenger manifest and the conductor's ticket pouch have not been recovered.\n\n<b>The conductor remains unaccounted for.</b>");
            });
            paper.highlight = false; // optional evidence
            ui.Say("", "Something slid out from under the seat where the phone was.");
            yield return CountCheck(4, "Reconcile the manifest: open it (TAB) and confirm how many passengers are aboard.");
            yield return new WaitForSeconds(2f);
        }

        IEnumerator PhoneRing()
        {
            var ph = bus.Obj("Phone");
            ph.SetActive(true);
            while (!F.phone)
            {
                au.PlayAt(au.phone, ph.transform.position, 0.75f);
                yield return new WaitForSeconds(3.4f);
            }
        }

        // ================================================================== CHAPTER 4

        IEnumerator Chapter4()
        {
            Chapter(4, "THE ROUTE REPEATS");
            route.zone = Zone.Cinema;
            bus.SetDisplay("117  CINEMA STREET");
            currentStop = "Cinema Street";
            int passes = 0;
            StartCoroutine(CinemaLoop(n => passes = n));
            Obj("Ride on.");
            yield return new WaitUntil(() => passes >= 1);
            StartCoroutine(bus.Flicker(0, 1.2f));
            bus.SetDisplay("117   - - - - -");
            yield return new WaitForSeconds(2f);
            bus.SetDisplay("117  KAVERI FLYOVER");
            au.PlayAt(au.speaker, bus.speakerPoint.position, 0.4f);

            bool compared = false;
            Evidence("RouteCard", "Compare the route card with the display", "ROUTE CARD  -  117 NIGHT SERVICE",
                "1.  Old Market Depot\n2.  Old Market\n3.  Hospital Road\n4.  Cinema Street\n5.  River Road\n6.  Central Bus Stand\n\n<i>Do not stop on the flyover.</i>\n\nThe display says KAVERI FLYOVER. There is no such stop on the card.",
                () => compared = true);
            Obj("The route display shows a stop that is not on the route card. Compare them.");
            yield return Until(() => compared);

            bool asked = false;
            Special("raghavan", "Ask the driver about the flyover stop", () =>
            {
                Unspecial("raghavan");
                StartCoroutine(TalkThen(() => asked = true,
                    ("YOU", "This stop isn't on the route card."),
                    ("RAGHAVAN", "It used to be."),
                    ("YOU", "What happened here?"),
                    ("RAGHAVAN", "I told you. Keep your eyes on the passengers.")));
            });
            Obj("Ask Raghavan about it.");
            yield return Until(() => asked);

            Special("bala", "Talk - Bala", () =>
            {
                Unspecial("bala");
                P["bala"].LookAt(pl.cam.transform.position);
                StartCoroutine(TalkThen(() => F.balaTalk = true,
                    ("BALA", "You came back for me last time."),
                    ("YOU", "What do you mean?"),
                    ("BALA", "You said you'd come back. You rang the bell. Then the water came.")));
            });
            Obj("Bala is watching you. Talk to him.");
            yield return Until(() => F.balaTalk);
            yield return new WaitUntil(() => passes >= 3);
            Shot("ch4_cinema");
            yield return new WaitForSeconds(3f);

            // Knocking from the back: the lost-property pouch
            bool opened = false;
            StartCoroutine(RearKnocks(() => opened));
            Evidence("Pouch", "Open the lost-property pouch", "LOST PROPERTY",
                "·  one child's school shoe, black, size 1\n·  a paper windmill, still turning\n·  a brass conductor's badge, engraved:\n\n          <b>ARUN</b>\n\nThe pin of the badge is warm, as if someone has only just taken it off.",
                () => { opened = true; F.badge = true; reflectionBadge.SetActive(true); });
            Obj("Something is knocking at the back of the bus. Inspect the rear.");
            yield return Until(() => opened);

            float held = 0;
            Obj("Hold SHIFT and look at your own reflection in the mirror.");
            yield return Until(() => { if (pl.mirrorHeld) held += Time.deltaTime; return held > 1.3f; }, () => { pl.Teleport(new Vector3(-0.2f, 0.02f, 2.6f), 180); pl.forceMirror = true; });
            Shot("ch4_reflection");
            pl.forceMirror = false;
            ui.Say("", "In the mirror, the badge on your own uniform reads ARUN. It fits you perfectly. It always has.");
            yield return new WaitForSeconds(4f);

            // The stop that isn't there
            Obj("Ride on.");
            yield return Talk(("RAGHAVAN", "..."));
            route.zone = Zone.Flyover;
            yield return route.DriveToStop(140f, (w, z) => route.BuildFlyoverStop(w, z));
            bus.SetDisplay("117");
            var figures = new List<Passenger>();
            string[] ids = { "meena", "selvam", "kavin", "lakshmi", "bala" };
            for (int i = 0; i < ids.Length; i++)
            {
                var src = P[ids[i]];
                var f = MakeDouble(src, route.world);
                f.PlaceAt(new Vector3(-2.7f, -0.15f, 1.2f + i * 1.4f), 90, PState.Standing);
                f.transform.SetParent(route.world, true);
                route.Spawned(f.gameObject);
                figures.Add(f);
            }
            au.SetHum(0.02f); au.SetRain(0.2f);
            yield return Talk(("RAGHAVAN", "Don't open the door."));
            Obj("");
            yield return new WaitForSeconds(2.5f);
            // every face at the curb turns to you at once
            foreach (var f in figures) if (f) f.LookAt(pl.cam.transform.position);
            au.PlayAt(au.scrape, new Vector3(-1.3f, 1.2f, 2f), 0.8f, 0.7f);
            ui.dread = 0.75f;
            yield return new WaitForSeconds(3f);
            Shot("ch4_curb");
            bus.SetAllLights(false);
            au.PlayAt(au.thump, new Vector3(0, 2f, 0), 0.8f);
            yield return new WaitForSeconds(1.2f);
            foreach (var f in figures) if (f) f.gameObject.SetActive(false);
            // ...and when the lights come back, one of them is inside, at the door.
            var inside = MakeDouble(P["bala"], transform);
            inside.PlaceAt(Passenger.FrontDoor + new Vector3(0.2f, 0, -0.3f), 0, PState.Standing);
            inside.FaceFlat(pl.transform.position);
            inside.LookAt(pl.cam.transform.position);
            bus.SetAllLights(true);
            Scare(0.85f);
            yield return new WaitForSeconds(1.1f);
            bus.SetAllLights(false);
            yield return new WaitForSeconds(0.5f);
            Destroy(inside.gameObject);
            bus.SetAllLights(true);
            ui.dread = 0.55f;
            yield return new WaitForSeconds(1f);
            au.SetHum(0.12f); au.SetRain(0.45f);
            ui.Say("", "Everyone who was standing at the curb is still sitting inside. Every one of them.");
            route.zone = Zone.River;
            route.Cruise();
            yield return new WaitForSeconds(3f);
        }

        Passenger MakeDouble(Passenger src, Transform parent)
        {
            var d = PersonBuilder.Copy(src, parent, src.id + "_double");
            Destroy(d.GetComponent<Collider>());
            d.interact.active = false;
            return d;
        }

        IEnumerator CinemaLoop(Action<int> passed)
        {
            for (int pass = 1; pass <= 3; pass++)
            {
                if (pass == 3) yield return new WaitUntil(() => F.balaTalk);
                var g = route.Spawned(route.BuildCinema(route.world, route.AheadZ(175f), pass));
                yield return new WaitUntil(() => !g || g.transform.position.z < -2f);
                passed(pass);
                if (pass < 3) yield return new WaitForSeconds(2f);
            }
        }

        IEnumerator RearKnocks(Func<bool> stop)
        {
            var pos = new Vector3(0.2f, 0.5f, -5.3f);
            while (!stop())
            {
                au.PlayAt(au.knock, pos, 0.9f, 0.8f);
                yield return new WaitForSeconds(0.9f);
                if (stop()) break;
                au.PlayAt(au.knock, pos, 0.9f, 0.78f);
                yield return new WaitForSeconds(4.5f);
            }
        }

        // ================================================================== CHAPTER 5

        IEnumerator Chapter5()
        {
            Chapter(5, "RIVER ROAD");
            route.zone = Zone.River;
            bus.SetDisplay("117  RIVER ROAD");
            au.SetDrone(0.12f);

            bool asked = false;
            Special("raghavan", "Ask Raghavan who Arun is", () =>
            {
                Unspecial("raghavan");
                StartCoroutine(TalkThen(() => asked = true,
                    ("YOU", "Who is Arun?"),
                    ("RAGHAVAN", "A good conductor. Better than me."),
                    ("YOU", "Where is he now?"),
                    ("RAGHAVAN", "I kept driving. He went back for the child. I heard the bell after the bus had stopped."),
                    ("YOU", "Why am I wearing his badge?"),
                    ("RAGHAVAN", "Because you never got off.")));
            });
            Obj("Ask Raghavan about Arun.");
            yield return Until(() => asked);
            F.driverTalk = true;
            au.PlayAt(au.whisper, P["lakshmi"].transform.position + Vector3.up, 0.4f);
            yield return Talk(("LAKSHMI", "You counted us once. Then you counted us again. You forgot the little one."));

            bool read = false;
            Evidence("Logbook", "Read the driver's logbook", "LOGBOOK  -  R. RAGHAVAN  -  ROUTE 117",
                "06-11-2015   23:50   Depot. Vehicle checked. Wipers poor.\n07-11-2015   01:17   Last service departed. Heavy rain.\n01:31   Stopped before flyover. Deck cracking.\n01:33   Conductor got passengers down. Returned to bus.\n\n<b>Passenger count: 6. One child missing.</b>\n<b>Driver departed.</b>\n\nThe last line has been written over so many times the pen has gone through the paper.\n\n(You keep the logbook.)",
                () => { read = true; F.logbook = true; });
            Obj("Read the driver's logbook on the engine cover.");
            yield return Until(() => read);
            bus.ShowObj("Logbook", false);

            // The silent man
            silentMan.PlaceAt(new Vector3(0.05f, 0, -4.95f), 0, PState.Seated);
            float held = 0;
            Obj("Check the rear-view mirror.");
            yield return Until(() => { if (pl.mirrorHeld) held += Time.deltaTime; return held > 1.4f; },
                () => { pl.Teleport(new Vector3(0f, 0.02f, 2.0f), 0); pl.forceMirror = true; });
            F.seenSilentMan = true;
            Shot("ch5_mirror_man");
            pl.forceMirror = false;
            au.SetHeart(0.25f);
            Obj("There is a man on the last bench in the mirror. Turn around and look at the last row.");
            var bench = new Vector3(0, 1.0f, -4.95f);
            yield return Until(() => !pl.mirrorHeld && pl.LookingAt(bench, 18f), () => pl.Aim(bench));
            au.SetRain(0.1f); au.SetHum(0.0f); au.engineOn = false;
            ui.Say("", "The last bench is empty.");
            yield return new WaitForSeconds(2.5f);
            au.engineOn = true; au.SetRain(0.45f); au.SetHum(0.12f);
            held = 0;
            Obj("Look in the mirror again.");
            yield return Until(() => { if (pl.mirrorHeld) held += Time.deltaTime; return held > 0.4f; }, () => { pl.Aim(new Vector3(-0.66f, 1.9f, 5.4f)); pl.forceMirror = true; });
            var behind = pl.transform.position - pl.transform.forward * 0.8f + pl.transform.right * 0.38f;
            behind.x = Mathf.Clamp(behind.x, -0.38f, 0.38f); behind.z = Mathf.Clamp(behind.z, -4.5f, 4.0f); behind.y = 0;
            silentMan.PlaceAt(behind, pl.yaw, PState.Standing);
            au.PlayAt(au.printer, behind + Vector3.up * 1.2f, 0.8f, 0.9f);
            au.SetHeart(0.6f);
            yield return new WaitForSeconds(0.8f);
            Shot("ch5_behind");
            au.PlayAt(au.speaker, bus.speakerPoint.position, 0.6f);
            yield return Talk(("SPEAKER  (Raghavan)", "Conductor, don't turn around. The man behind you has no ticket."));
            pl.forceMirror = false;
            float startYaw = pl.yaw, t = 0;
            yield return Until(() => { t += Time.deltaTime; return Mathf.Abs(Mathf.DeltaAngle(startYaw, pl.yaw)) > 110f || t > 9f; });
            silentMan.Hide();
            yield return FaceJump(silentMan, 0.4f, 1f); // you turned around. He is right there.
            au.SetHeart(0.1f);
            ui.Say("", "Nobody. Only the ticket printer, still warm, and one blank ticket on the floor.");
            yield return new WaitForSeconds(4f);

            // River Road: Kavin's stop. Nobody gets off.
            Obj("Ride on.");
            yield return route.DriveToStop(120f, (w, z) => route.BuildShelter(w, z, "River Road"));
            currentStop = "River Road";
            yield return bus.Door(true, true);
            var kavin = P["kavin"];
            yield return Talk(("KAVIN", "River Road! This is me. Bye, anna."));
            yield return kavin.Walk(new Vector3(0, 0, kavin.seatPos.z), Passenger.FrontAisle, Passenger.FrontDoor);
            kavin.FaceFlat(Passenger.FrontOutside);
            yield return new WaitForSeconds(1.5f);
            yield return Talk(("KAVIN", "I can't... my legs won't... It's just water down there. Where's the road?"),
                              ("RAGHAVAN", "Nobody gets off before the end. Not tonight. Sit down, son."));
            yield return kavin.Walk(Passenger.FrontAisle, new Vector3(0, 0, kavin.seatPos.z), kavin.seatPos);
            kavin.PlaceSeated();
            yield return bus.Door(true, false);
            route.zone = Zone.City;
            route.Cruise();
            yield return new WaitForSeconds(2f);
        }

        // ================================================================== CHAPTER 6

        IEnumerator Chapter6()
        {
            Chapter(6, "FINAL COUNT");
            bus.SetDisplay("117  CENTRAL BUS STAND");
            au.SetDrone(0.3f);
            Obj("Ride to the end of the line.");
            yield return new WaitForSeconds(6f);
            route.zone = Zone.Stand;
            yield return route.DriveToStop(170f, (w, z) => route.BuildBusStand(w, z));
            currentStop = "Central Bus Stand";
            yield return Talk(("RAGHAVAN", "This is the last stop. If you remember them, let them go."));

            // Lights go out, back to front.
            for (int i = bus.cabinLights.Length - 1; i >= 0; i--)
            {
                bus.SetLight(i, false);
                au.PlayAt(au.thump, new Vector3(0, 2.2f, bus.cabinLights[i].transform.position.z), 0.35f, 1.4f);
                yield return new WaitForSeconds(0.8f);
            }
            P["meena"].PlaceSeated(); // she never got off
            au.SetHum(0f);
            yield return new WaitForSeconds(1f);
            Shot("ch6_dark");

            var lines = new Dictionary<string, (string, string)[]>
            {
                ["meena"] = new[] { ("YOU", "Meena. Hospital Road."), ("MEENA", "Does the hospital still have the blue gate? Tell my mother I was coming.") },
                ["selvam"] = new[] { ("YOU", "Selvam. The tea stall by the flyover."), ("SELVAM", "Two rupees a cup. You still owe me one, Arun.") },
                ["kavin"] = new[] { ("YOU", "Kavin. River Road."), ("KAVIN", "Tell Amma I picked up. Tell her I picked up.") },
                ["lakshmi"] = new[] { ("YOU", "Lakshmi amma. Central Bus Stand."), ("LAKSHMI", "Don't count the seats. Count the names.") },
                ["bala"] = new[] { ("YOU", "Bala."), ("BALA", "You told me to wait by the door."), ("YOU", "I came back for you."), ("BALA", "I know. I heard you ringing.") },
            };
            foreach (var id in PassengerIds)
            {
                var p = P[id];
                string pron = id == "meena" || id == "lakshmi" ? "her" : "his";
                Special(id, "Say " + pron + " name - " + p.displayName, () =>
                {
                    Unspecial(id);
                    p.LookAt(pl.cam.transform.position);
                    StartCoroutine(TalkThen(() =>
                    {
                        p.named = true;
                        bus.SetLight(p.lightIndex, true);
                        au.PlayAt(au.bell, p.transform.position + Vector3.up * 2f, 0.15f, 1.5f);
                    }, lines[id]));
                });
            }
            yield return Talk(("LAKSHMI", "Don't take your eyes off him, child. He only walks when nobody is counting him."));
            StartCoroutine(Stalker(() => PassengerIds.All(i => P[i].named)));
            Obj(() => "The lights went out. Say each passenger's name (" + PassengerIds.Count(i => P[i].named) + "/5). The man from the last bench moves only while you are NOT looking at him.");
            yield return Until(() => PassengerIds.All(i => P[i].named));
            yield return Talk(("RAGHAVAN", "There were six passengers. And one conductor. I wrote down only six."));

            bool found = false;
            Evidence("Bag", "Search the conductor's bag", "A TICKET IN THE CONDUCTOR'S BAG",
                "KAVERIPURAM TRANSPORT  ·  ROUTE 117\n\nPASSENGER:  <b>ARUN</b>\nONE WAY\n07-11-2015   01:33\n\n<b>UNPUNCHED</b>\n\nIt is printed in the same type as every ticket you issued tonight.",
                () => { found = true; F.finalTicket = true; });
            Obj("Search the conductor's bag on the jump seat by the front door.");
            yield return Until(() => found);
        }

        // ================================================================== CHAPTER 7

        IEnumerator Chapter7()
        {
            Chapter(7, "THE LAST PASSENGER");
            Obj("");
            au.SetDrone(0.35f);
            // The front door opens. For a moment you see the bus from outside.
            yield return bus.Door(true, true);
            yield return new WaitForSeconds(0.5f);
            ui.cinematic = true;
            ui.fadeSpeed = 2f; ui.fadeTarget = 1;
            yield return new WaitForSeconds(0.6f);
            conductorDouble.PlaceAt(bus.jumpSeat.position, 90, PState.Seated);
            var cam = pl.cam.transform;
            var oldParent = cam.parent;
            pl.lookOverrideWeight = 1;
            cam.SetParent(null, true);
            cam.position = new Vector3(-3.9f, 1.4f, 6.3f);
            cam.LookAt(new Vector3(-1.0f, 1.05f, 3.9f));
            ui.fadeTarget = 0;
            yield return new WaitForSeconds(2.2f);
            Shot("ch7_outside");
            // the silhouette turns a beat late
            if (conductorDouble.anim) { conductorDouble.LookAt(cam.position); yield return new WaitForSeconds(1.4f); }
            else for (float t = 0; t < 1.4f; t += Time.deltaTime) { conductorDouble.head.localRotation = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(0, -75, 0), t / 1.4f); yield return null; }
            yield return new WaitForSeconds(2f);
            ui.fadeTarget = 1;
            yield return new WaitForSeconds(0.6f);
            cam.SetParent(oldParent, false);
            pl.lookOverrideWeight = 0;
            conductorDouble.Hide();
            ui.fadeTarget = 0; ui.fadeSpeed = 0.6f;
            ui.cinematic = false;

            yield return Talk(("RAGHAVAN", "I can drive one more round. I can always drive one more round."),
                              ("LAKSHMI", "No. Let the road end."),
                              ("BALA", "Can we go home now?"),
                              ("RAGHAVAN", "I heard the bell. I should have stopped."));
            finalMode = true;
            if (F.logbook)
                Special("raghavan", "Give Raghavan his logbook", () => { if (!ending) StartCoroutine(EndingC()); });
            Obj("Open the manifest (TAB). Finish it properly - or tear the page out." + (F.logbook ? " Or give Raghavan back his logbook." : ""));
            yield return new WaitUntil(() => ending);
            yield return new WaitUntil(() => false); // endings take over from here
        }

        public void CompleteManifest()
        {
            if (!finalMode || ending) return;
            bool all = PassengerIds.All(id => { var r = Row(id); return r != null && r.remembered; });
            if (conductorSign == 2 || F.errors >= 3) { ui.manifestOpen = false; StartCoroutine(EndingB(false)); return; }
            if (!all || conductorSign == 0)
            {
                au.Play2D(au.whisper, 0.4f);
                ui.Toast(!all ? "The ink won't take. Someone is missing from the page." : "There is still a blank line where the conductor's name should be.", 4f);
                return;
            }
            ui.manifestOpen = false;
            StartCoroutine(EndingA());
        }

        public void TearManifest()
        {
            if (!finalMode || ending) return;
            ui.manifestOpen = false;
            StartCoroutine(EndingB(true));
        }

        void BeginEnding()
        {
            ending = true; finalMode = false;
            special.Clear(); specialOrder.Clear(); bellAction = null;
            ui.manifestOpen = false; ui.ClearLines();
            ui.SetObjective(null, () => "");
            Debug.Log("[TLP] Ending sequence started");
        }

        IEnumerator FinishEnding(string letter, string title, string sub)
        {
            Debug.Log("[TLP] Ending " + letter + " reached. errors=" + F.errors);
            var found = PlayerPrefs.GetString("tlp_endings", "");
            if (!found.Contains(letter)) PlayerPrefs.SetString("tlp_endings", found + letter);
            PlayerPrefs.Save();
            ui.fadeSpeed = 0.35f; ui.fadeTarget = 1;
            yield return new WaitForSeconds(3.2f);
            ui.Card(title, sub);
            Shot("ending_" + letter);
            yield return new WaitForSeconds(7f);
            ui.HideCard();
            yield return new WaitForSeconds(1.5f);
            bool done = false;
            ui.ShowCredits(new[]
            {
                "#THE LAST PASSENGER",
                "a night-shift horror for GitHub Game Off 2026",
                "theme: SHIFT",
                "",
                "#Ending " + letter + " of 3",
                title.Substring(title.IndexOf('-') + 1).Trim(),
                "",
                "Endings found: " + EndingsFound(),
                "",
                "#Made with",
                "Unity 6 · all geometry built in code · all sound synthesised",
                "",
                "#Thank you for travelling with us.",
                "Last route. No refunds. No one gets left behind.",
                "",
                "Next service: 01:17 A.M.",
            }, () => done = true);
            yield return new WaitUntil(() => done || bot);
            if (bot) { Debug.Log("[TLP] Bot finished OK"); yield return new WaitForSecondsRealtime(1.5f); Application.Quit(); }
            ToTitle();
        }

        IEnumerator EndingA()
        {
            BeginEnding();
            yield return Talk(("RAGHAVAN", "Read them once. Properly this time."),
                              ("YOU", "Meena. Selvam. Kavin. Lakshmi. Bala."),
                              ("YOU", "And Arun. Conductor. Not a passenger."));
            bus.SetAllLights(true);
            bus.SetLightTint(new Color(1f, 0.85f, 0.65f));
            au.SetDrone(0); au.SetHeart(0);
            au.PlayAt(au.bell, bus.bellPoint.position, 0.9f);
            if (!bus.frontIsOpen) yield return bus.Door(true, true);
            var away = new Vector3(-6f, -0.15f, 9f);
            foreach (var id in new[] { "lakshmi", "kavin", "selvam", "meena" })
            {
                var p = P[id]; p.leaving = true;
                StartCoroutine(p.ExitFront(away));
                yield return new WaitForSeconds(2.6f);
            }
            var bala = P["bala"]; bala.leaving = true;
            yield return bala.Walk(new Vector3(0, 0, bala.seatPos.z), Passenger.FrontAisle);
            bala.FaceFlat(pl.transform.position);
            bala.LookAt(pl.cam.transform.position);
            yield return Talk(("BALA", "You came back."), ("YOU", "I should have stayed."), ("BALA", "You did."));
            yield return bala.Walk(Passenger.FrontDoor, Passenger.FrontOutside, away);
            bala.gameObject.SetActive(false);
            yield return new WaitForSeconds(1.5f);
            driver.leaving = true;
            yield return driver.Walk(new Vector3(0.75f, 0, 4.55f), new Vector3(-0.5f, 0, 4.5f), Passenger.FrontDoor, Passenger.FrontOutside, away);
            driver.gameObject.SetActive(false);
            StartCoroutine(Dawn(8f));
            au.engineOn = false; au.SetRain(0.05f);
            ui.Say("", "The bus is empty. The manifest is complete. Somewhere, a dog is barking at the morning.");
            yield return new WaitForSeconds(7f);
            yield return FinishEnding("A", "ENDING A  -  THE CORRECT COUNT", "At dawn, a new conductor takes the route card from the hook.\nThe manifest beside it is complete, in handwriting that matches yours.");
        }

        IEnumerator EndingB(bool tore)
        {
            BeginEnding();
            if (tore) { au.Play2D(au.tear, 0.9f); ui.Say("", "You tear the page out of the manifest."); }
            else { au.Play2D(au.whisper, 0.7f); ui.Say("", "You sign. The ink spreads across every line until there is no room for anyone."); }
            yield return new WaitForSeconds(2.5f);
            foreach (var p in P.Values) if (p.gameObject.activeSelf) { p.FaceFlat(pl.transform.position); p.LookAt(pl.cam.transform.position); }
            au.engineOn = false;
            yield return new WaitForSeconds(1.5f);
            if (bus.frontIsOpen) yield return bus.Door(true, false);
            route.zone = Zone.Flyover;
            route.Cruise(7f);
            ui.Say("", "The engine is off. The bus is moving.");
            au.SetDrone(0.6f); au.SetHeart(0.5f);
            for (int i = 0; i < 18; i++)
            {
                SpawnCrowd();
                if (i % 3 == 0)
                {
                    au.PlayAt(au.printer, pl.cam.transform.position + pl.transform.forward * 0.3f, 0.7f, 1f + i * 0.02f);
                    ui.Toast("ROUTE 117 · NIGHT SERVICE\n\nPASSENGER:  ARUN\nONE WAY\n07-11-2015", 1.4f, true);
                }
                if (i % 4 == 0) StartCoroutine(bus.Flicker(UnityEngine.Random.Range(0, 6), 0.8f));
                yield return new WaitForSeconds(0.45f);
            }
            yield return Talk(("RAGHAVAN", "You can't leave them uncounted."));
            yield return bus.Door(true, true);
            ui.Say("", "The doors open onto the inside of the same bus. The aisle is full.");
            yield return new WaitForSeconds(3f);
            au.PlayAt(au.printer, pl.cam.transform.position, 0.9f, 0.8f);
            ui.Toast("<b>NO VACANT SEATS</b>", 6f, true);
            yield return new WaitForSeconds(3f);
            au.SetDrone(0); au.SetHeart(0);
            yield return FinishEnding("B", "ENDING B  -  NO VACANT SEATS", "Route 117 still runs at 01:17.\nIf you take it, count the passengers once. Only once.");
        }

        IEnumerator EndingC()
        {
            BeginEnding();
            yield return Talk(("YOU", "This is yours."),
                              ("RAGHAVAN", "..."),
                              ("RAGHAVAN", "I drove away while you went back for him. I told them the bus was empty."),
                              ("RAGHAVAN", "Eleven years I have driven it full. Every night I count them so somebody does."),
                              ("RAGHAVAN", "Here. Take mine. Ring the bell when it's time."));
            au.PlayAt(au.printer, driver.transform.position + Vector3.up, 0.6f);
            ui.Toast("KAVERIPURAM TRANSPORT · ROUTE 117\n\nR. RAGHAVAN  ·  DRIVER\nONE WAY\n07-11-2015   01:33\n\nSOLE SURVIVOR", 6f, true);
            if (!bus.frontIsOpen) yield return bus.Door(true, true);
            driver.leaving = true;
            yield return driver.Walk(new Vector3(0.75f, 0, 4.55f), new Vector3(-0.5f, 0, 4.5f), Passenger.FrontDoor, Passenger.FrontOutside, new Vector3(-6f, -0.15f, 8f));
            driver.gameObject.SetActive(false);
            yield return bus.Door(true, false);
            route.zone = Zone.City;
            route.Cruise(6f);
            ui.Say("", "The driver's seat is empty. The bus pulls away anyway.");
            yield return new WaitForSeconds(5f);
            bool rung = false;
            bellAction = () => rung = true;
            bus.Prop("BellFront").Set("Ring the bell", RingBell); bus.Prop("BellRear").Set("Ring the bell", RingBell);
            bus.Prop("BellFront").highlight = true;
            ui.SetObjective(null, () => "Ring the bell.");
            yield return Until(() => rung, () => RingBell());
            ui.SetObjective(null, () => "");
            yield return route.DriveToStop(80f, (w, z) => route.BuildBusStand(w, z));
            // a blackout, and the seats are empty
            bus.SetAllLights(false);
            yield return new WaitForSeconds(1.2f);
            foreach (var p in P.Values) p.gameObject.SetActive(false);
            silentMan.Hide();
            foreach (var c in crowd) if (c) c.SetActive(false);
            bus.SetAllLights(true);
            bus.SetLightTint(new Color(1f, 0.9f, 0.75f));
            StartCoroutine(Dawn(10f));
            au.engineOn = false; au.SetDrone(0); au.SetRain(0.05f);
            yield return bus.Door(true, true);
            yield return new WaitForSeconds(1.5f);
            ui.Say("RAGHAVAN  (outside, far away)", "This time, I got down.");
            yield return new WaitForSeconds(5f);
            au.PlayAt(au.printer, bus.Prop("TicketMachine").transform.position, 0.8f);
            ui.Toast("\n\n\n\n(blank)", 4f, true);
            yield return new WaitForSeconds(4f);
            yield return FinishEnding("C", "ENDING C  -  THE DRIVER'S TICKET", "Dawn comes in through the windshield.\nThe ticket machine prints one blank ticket, and stops.");
        }

        IEnumerator Dawn(float seconds)
        {
            var fog0 = RenderSettings.fogColor; var amb0 = RenderSettings.ambientLight;
            var bg0 = pl.cam.backgroundColor;
            var fog1 = new Color(0.46f, 0.44f, 0.5f); var amb1 = new Color(0.42f, 0.4f, 0.42f);
            float d0 = RenderSettings.fogDensity;
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                float k = t / seconds;
                RenderSettings.fogColor = Color.Lerp(fog0, fog1, k);
                RenderSettings.ambientLight = Color.Lerp(amb0, amb1, k);
                RenderSettings.fogDensity = Mathf.Lerp(d0, 0.06f, k);
                pl.cam.backgroundColor = Color.Lerp(bg0, fog1, k);
                yield return null;
            }
        }

        // ================================================================== dev tools

        void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            for (int c = 1; c <= 7; c++)
                if (Input.GetKeyDown(KeyCode.F1 + (c - 1)) && ui.gameStarted) JumpTo(c);
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void JumpTo(int c)
        {
            PrepData(c);
            ui.gameStarted = true; ui.menuOpen = false;
            if (main != null) StopCoroutine(main);
            StopAllCoroutines();
            main = StartCoroutine(Run(c));
            if (bot) StartCoroutine(Bot());
        }

        void PrepData(int c)
        {
            data = new SaveData();
            F.chapter = c;
            if (c >= 2)
                foreach (var id in new[] { "meena", "kavin", "lakshmi", "bala" })
                { var p = P[id]; data.rows.Add(new ManifestRow { id = id, name = p.displayName, seat = p.seatLabel, dest = p.destination, status = 1 }); }
            if (c >= 3) { data.rows.Add(new ManifestRow { id = "selvam", name = "Selvam", seat = P["selvam"].seatLabel, dest = "- (no ticket)", status = 1 }); Row("meena").status = 2; }
            F.routeCard = F.wetHandle = c >= 2; F.meenaTicket = c >= 3; F.phone = F.newspaper = c >= 4;
            F.badge = F.balaTalk = c >= 5; F.logbook = F.driverTalk = c >= 6; F.finalTicket = c >= 7;
        }
#endif

        public void StartBot(string endingLetter, string shots, int fromChapter)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            bot = true; botEnding = endingLetter; shotDir = shots; timeScale = 3f;
            Debug.Log("[TLP] Bot playthrough, ending " + endingLetter + ", from chapter " + fromChapter);
            StartCoroutine(BotStart(fromChapter));
#endif
        }

        /// <summary>Dev aid: close-up screenshots of seated people from the aisle.</summary>
        public IEnumerator Portraits(string dir)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            shotDir = dir;
            JumpTo(3);
            yield return new WaitForSecondsRealtime(2f);
            StopAllCoroutines(); // freeze the story (this coroutine runs on the UI object)
            route.HaltNow();
            var views = new (string name, Vector3 eye, Vector3 at)[]
            {
                ("selvam_side", new Vector3(0.15f, 1.0f, 1.0f), new Vector3(-0.98f, 0.7f, 1.0f)),
                ("bala_side", new Vector3(-0.15f, 1.0f, 0.4f), new Vector3(0.98f, 0.55f, 0.4f)),
                ("kavin_side", new Vector3(-0.15f, 1.0f, -1.6f), new Vector3(0.98f, 0.7f, -1.6f)),
                ("kavin_front", new Vector3(0.0f, 1.5f, -0.2f), new Vector3(0.98f, 0.8f, -1.6f)),
                ("lakshmi_front", new Vector3(0.0f, 1.5f, -2.4f), new Vector3(-0.98f, 0.8f, -3.8f)),
                ("driver", new Vector3(-0.3f, 1.55f, 4.1f), new Vector3(0.75f, 1.0f, 4.9f)),
            };
            foreach (var v in views)
            {
                ui.cinematic = true;
                pl.lookOverrideWeight = 1;
                pl.cam.transform.position = v.eye;
                pl.cam.transform.LookAt(v.at);
                yield return new WaitForSecondsRealtime(0.6f);
                Shot(v.name);
                yield return new WaitForSecondsRealtime(0.3f);
            }
            Application.Quit();
#else
            yield break;
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // ================================================================== TRAILER (dev builds only)
        // -tlptrailer -tlpshots <dir>: plays a scripted shot list at a locked 30 fps and writes every
        // frame as a JPG plus cues.txt (frame, kind, text) that the trailer cutting script lays sound and captions on.

        bool trRec; int trFrame; float trShake; StreamWriter trCues;

        void Cue(string kind, string text = "") { trCues?.WriteLine(trFrame + "\t" + kind + "\t" + text); trCues?.Flush(); }

        IEnumerator TrailerRecorder(string dir, int super)
        {
            var eof = new WaitForEndOfFrame();
            while (true)
            {
                yield return eof;
                if (!trRec) continue;
                var tex = ScreenCapture.CaptureScreenshotAsTexture(super);
                File.WriteAllBytes(Path.Combine(dir, "f" + trFrame.ToString("00000") + ".jpg"), tex.EncodeToJPG(93));
                Destroy(tex);
                trFrame++;
            }
        }

        /// <summary>Moves the camera from (p0 looking at l0) to (p1 looking at l1) over 'dur' seconds.</summary>
        IEnumerator TrCam(float dur, Vector3 p0, Vector3 l0, Vector3 p1, Vector3 l1, Action<float> each = null)
        {
            var c = pl.cam.transform;
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0, 1, t / dur);
                trShake = Mathf.MoveTowards(trShake, 0, Time.deltaTime * 1.6f);
                var j = new Vector3(Mathf.PerlinNoise(Time.time * 37f, 1) - 0.5f, Mathf.PerlinNoise(Time.time * 37f, 9) - 0.5f, 0) * trShake * 0.09f;
                c.position = Vector3.Lerp(p0, p1, k);
                c.rotation = Quaternion.LookRotation(Vector3.Lerp(l0, l1, k) - c.position) * Quaternion.Euler(j.y * 40f, j.x * 40f, j.x * 25f);
                each?.Invoke(t / dur);
                yield return null;
            }
        }

        IEnumerator TrStill(float dur, Vector3 p, Vector3 l, Action<float> each = null) => TrCam(dur, p, l, p, l, each);

        IEnumerator TrBlack(float dur)
        {
            ui.fade = 1; ui.fadeTarget = 1;
            yield return new WaitForSeconds(dur);
        }

        void TrOpen() { ui.fade = 0; ui.fadeTarget = 0; }

        void TrHit(float s, string kind = "hit")
        {
            Cue(kind, s.ToString("0.00"));
            ui.Flash(new Color(0.55f, 0f, 0f), 0.35f * s);
            trShake = Mathf.Max(trShake, 0.4f + s);
        }

        public IEnumerator Trailer(string dir, int super)
        {
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "f*.jpg")) File.Delete(f);
            trCues = new StreamWriter(Path.Combine(dir, "cues.txt"));
            au.DumpClips(Path.Combine(dir, "sfx"));
            settings.marker = false; settings.reduceMotion = false;
            ui.trailer = true; ui.objective = null; ui.ShowHint(0);
            PrepData(3);
            SetupState(3, false);
            ui.fade = 1; ui.fadeTarget = 1;
            yield return new WaitForSecondsRealtime(1.5f);
            StopAllCoroutines();                     // freeze the story; this coroutine runs on the UI object
            ui.StartCoroutine(TrailerRecorder(dir, super));
            Time.captureFramerate = 30;
            ui.cinematic = true; ui.ClearLines();
            pl.lookOverrideWeight = 1;
            pl.cam.transform.SetParent(null, true);
            pl.cam.fieldOfView = 58;
            route.zone = Zone.City; route.Cruise(); route.speed = route.maxSpeed;
            ui.dread = 0.25f;
            yield return new WaitForSeconds(2f);     // let the city fill in
            trRec = true;

            // 1. The bus at night, from across the road
            Cue("cut", "exterior"); Cue("music", "start");
            yield return TrBlack(1.0f);
            TrOpen(); ui.fade = 1; ui.fadeTarget = 0; ui.fadeSpeed = 0.8f;
            Cue("caption", "ROUTE 117  ·  LAST SERVICE  ·  01:17 A.M.");
            yield return TrCam(6.5f, new Vector3(5.2f, 1.1f, 13f), new Vector3(0, 1.6f, 4f), new Vector3(4.6f, 1.5f, 3f), new Vector3(0, 1.4f, -2f));

            // 2. Down the aisle
            Cue("cut", "aisle"); Cue("caption", "You are the conductor.");
            ui.fadeSpeed = 0.6f;
            yield return TrCam(5f, new Vector3(0f, 1.75f, 4.4f), new Vector3(0, 1.1f, -5f), new Vector3(0f, 1.62f, 2.2f), new Vector3(0, 1.0f, -5f));

            // 3. The ticket machine
            Cue("cut", "ticket"); Cue("bell");
            Cue("caption", "Issue every ticket.");
            ui.OpenTicket(P["kavin"], "Old Market", _ => { });
            yield return TrStill(1.6f, new Vector3(0.2f, 1.6f, 2.0f), new Vector3(0.98f, 0.9f, -1.6f));
            Cue("printer");
            ui.ticketOpen = false;
            ui.Toast("TICKET ISSUED  ·  KAVIN  ·  RIVER ROAD", 3f, true);
            yield return TrStill(1.2f, new Vector3(0.2f, 1.6f, 2.0f), new Vector3(0.98f, 0.9f, -1.6f));

            // 4. The manifest
            Cue("cut", "manifest"); Cue("caption", "Count every passenger.");
            ui.hasManifest = true; ui.manifestOpen = true;
            yield return TrStill(3f, new Vector3(0f, 1.62f, 3.0f), new Vector3(0, 1.2f, -5f));
            ui.manifestOpen = false;

            // 5. They all look at you
            Cue("cut", "stare"); Cue("caption", "The count doesn't stay right.");
            bool looked = false;
            yield return TrCam(3.4f, new Vector3(0f, 1.62f, 3.2f), new Vector3(0, 1.1f, -4f), new Vector3(0f, 1.6f, 2.5f), new Vector3(0, 1.05f, -4f), k =>
            {
                if (!looked && k > 0.35f)
                {
                    looked = true; Cue("whisper");
                    foreach (var p in P.Values) if (p.gameObject.activeInHierarchy) p.LookAt(pl.cam.transform.position);
                }
            });
            foreach (var p in P.Values) p.LookForward();

            // 6. The mirror
            Cue("cut", "mirror"); Cue("caption", "Hold SHIFT. The mirror sees more.");
            ghostMeena.PlaceSeated(); ghostMeena.LookForward();
            ui.cinematic = false; pl.forceMirror = true;
            bool turned = false;
            yield return TrStill(4.6f, new Vector3(0.1f, 1.62f, 1.6f), new Vector3(0.6f, 1.0f, 3.4f), k =>
            {
                if (!turned && k > 0.45f) { turned = true; ghostMeena.LookAt(bus.mirrorCam.transform.position); Cue("breath"); }
            });
            pl.forceMirror = false; ui.cinematic = true;
            ghostMeena.Hide(); ghostMeena.LookForward();
            TrHit(0.5f);
            yield return TrStill(0.5f, new Vector3(0.1f, 1.62f, 1.6f), new Vector3(0.9f, 0.9f, 3.4f));

            // ---- montage
            Cue("cut", "black"); Cue("drone", "rise");
            yield return TrBlack(0.4f);
            Cue("card", "Don't count the people you can hear.");
            yield return TrBlack(2.6f);
            ui.dread = 0.5f;

            // 7. Rear door
            Cue("cut", "reardoor"); TrOpen();
            var leaf = bus.rearDoor; var leafBase = leaf.localPosition;
            yield return TrStill(2.2f, new Vector3(0.35f, 1.55f, -0.4f), new Vector3(-1.2f, 1.1f, -2.6f), k =>
            {
                bool bang = (k > 0.2f && k < 0.3f) || (k > 0.5f && k < 0.58f) || (k > 0.75f && k < 0.86f);
                leaf.localPosition = leafBase + (bang ? new Vector3(UnityEngine.Random.Range(-0.04f, 0.04f), 0, 0) : Vector3.zero);
                if (bang && Time.frameCount % 3 == 0) trShake = Mathf.Max(trShake, 0.5f);
            });
            leaf.localPosition = leafBase;
            Cue("knock", "2.0"); // the cutting script places the bangs by this cue's frame

            // 8. Phone on an empty seat
            Cue("cut", "phone"); Cue("phone");
            bus.ShowObj("Phone", true);
            StartCoroutine(bus.Flicker(2, 2f));
            yield return TrCam(2.2f, new Vector3(-0.1f, 1.25f, 0.9f), new Vector3(-0.95f, 0.54f, 0.05f), new Vector3(-0.3f, 1.1f, 0.6f), new Vector3(-0.95f, 0.54f, 0.05f));
            bus.ShowObj("Phone", false);

            // 9. Face on the window
            Cue("cut", "window");
            Passenger face = null;
            yield return TrCam(2.2f, new Vector3(0.2f, 1.58f, -0.5f), new Vector3(1.4f, 1.5f, 0.0f), new Vector3(0.62f, 1.6f, -0.15f), new Vector3(1.42f, 1.62f, 0.1f), k =>
            {
                if (face == null && k > 0.4f)
                {
                    face = PersonBuilder.Copy(P["meena"], transform, "trailer_face");
                    Destroy(face.GetComponent<Collider>()); face.interact.active = false;
                    face.PlaceAt(new Vector3(1.42f, 0.05f, 0.1f), -90, PState.Standing);
                    face.LookAt(pl.cam.transform.position);
                    TrHit(0.75f);
                }
            });
            if (face) Destroy(face.gameObject);

            // 10. Don't turn around
            Cue("cut", "behind"); Cue("caption", "Don't turn around.");
            var man = silentMan;
            B.Layer(man.gameObject, 0);
            man.PlaceAt(new Vector3(0, 0, 0.75f), 0, PState.Standing);
            var cam = pl.cam.transform;
            var eye = new Vector3(0, 1.62f, 1.55f);
            yield return TrStill(1.6f, eye, eye + Vector3.forward * 5f);
            man.LookAt(eye);
            for (float t = 0; t < 0.22f; t += Time.deltaTime)
            {
                cam.position = eye; cam.rotation = Quaternion.Euler(-4f * t / 0.22f, 180f * Mathf.SmoothStep(0, 1, t / 0.22f), 0);
                yield return null;
            }
            TrHit(1f);
            yield return TrStill(1.0f, eye, new Vector3(0, 1.45f, 0.75f));
            Cue("cut", "black");
            yield return TrBlack(0.7f);

            // 11. Lights out: he only walks when you look away
            Cue("cut", "stalker"); Cue("caption", "He only moves when you look away.");
            bus.SetAllLights(false); ui.dread = 0.85f;
            var seat = new Vector3(0, 1.62f, 3.2f); var down = new Vector3(0, 1.2f, -5f);
            float[] zs = { -4.6f, -2.6f, -0.9f };
            for (int i = 0; i < zs.Length; i++)
            {
                TrOpen();
                man.PlaceAt(new Vector3(0, 0, zs[i]), 0, PState.Standing); man.LookAt(seat);
                Cue("heart", (i + 1).ToString());
                yield return TrStill(i == 0 ? 2.2f : 1.4f, seat, down);
                yield return TrBlack(i == 0 ? 0.5f : 0.35f);
            }
            TrOpen();
            man.PlaceAt(new Vector3(0, 0, 2.7f), 0, PState.Standing); man.LookAt(seat);
            TrHit(1.2f, "lunge");
            yield return TrStill(0.7f, seat, new Vector3(0, 1.5f, 2.7f));
            Cue("cut", "black");
            yield return TrBlack(1.0f);

            // 12. Title
            Cue("title");
            yield return TrBlack(6.5f);
            Cue("end");
            trRec = false;
            yield return null;
            trCues.Close();
            Debug.Log("[TLP] Trailer recorded " + trFrame + " frames");
            Application.Quit();
        }

#endif

        IEnumerator BotStart(int fromChapter)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (botHowTo)
            {
                ui.menuOpen = true; ui.howToOpen = true;
                yield return new WaitForSecondsRealtime(1.5f);
                Shot("howto");
                yield return new WaitForSecondsRealtime(0.5f);
                ui.howToOpen = false;
            }
            if (fromChapter > 1) JumpTo(fromChapter);
            else { data = new SaveData(); StartRun(1); StartCoroutine(Bot()); }
#else
            yield break;
#endif
        }

        IEnumerator Bot()
        {
            int lastChapter = 0;
            float started = Time.realtimeSinceStartup;
            while (true)
            {
                if (F.chapter != lastChapter) { lastChapter = F.chapter; Debug.Log("[TLP] Bot reached chapter " + lastChapter + " at " + (Time.realtimeSinceStartup - started).ToString("0") + "s, errors=" + F.errors); }
                if (Time.realtimeSinceStartup - started > 1500) { Debug.LogError("[TLP] Bot timeout in chapter " + F.chapter); Application.Quit(); }
                if (ui.docOpen) { yield return new WaitForSecondsRealtime(0.3f); ui.CloseDoc(); }
                if (ui.ticketOpen) ui.ChooseTicket(ui.TicketTarget);
                foreach (var row in data.rows.ToArray())
                {
                    if (!P.TryGetValue(row.id, out var p) || ending || finalMode) continue;
                    if (row.status != 1 && p.Aboard && p.state == PState.Seated && !(row.status == 2)) OnMark(row, 1);
                    if (row.status == 1 && p.state == PState.Gone) OnMark(row, 2);
                }
                if (!ui.Speaking && !ending)
                {
                    if (specialOrder.Count > 0 && !finalMode)
                    {
                        var id = specialOrder[0];
                        if (special.TryGetValue(id, out var s)) { Debug.Log("[TLP] Bot uses " + s.prompt); s.act(); }
                    }
                    else
                    {
                        foreach (var kv in bus.props)
                        {
                            var it = kv.Value;
                            if (it.highlight && it.Usable && it.gameObject.activeInHierarchy && !kv.Key.StartsWith("Bell")) { Debug.Log("[TLP] Bot uses " + it.Prompt); it.onUse(); break; }
                        }
                    }
                    if (finalMode)
                    {
                        Shot("ch7_choice");
                        if (botEnding == "C") { if (special.TryGetValue("raghavan", out var s)) s.act(); }
                        else if (botEnding == "B") TearManifest();
                        else
                        {
                            ui.manifestOpen = true;
                            yield return new WaitForSeconds(0.5f);
                            foreach (var r in data.rows) r.remembered = true;
                            conductorSign = 1;
                            yield return new WaitForSeconds(0.3f);
                            Shot("ch7_manifest");
                            yield return new WaitForSecondsRealtime(0.5f);
                            CompleteManifest();
                        }
                    }
                }
                yield return new WaitForSeconds(0.5f);
            }
        }
    }
}
