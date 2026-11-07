using System;
using System.Collections.Generic;
using UnityEngine;

namespace TLP
{
    public class Line
    {
        public string speaker, text;
        public float dur;
    }

    /// <summary>All 2D presentation (IMGUI): subtitles, prompts, objective, mirror glance, manifest, ticket machine, menus.</summary>
    public class UIManager : MonoBehaviour
    {
        public static UIManager I;
        public static System.Action<string, float> OnLine;

        // modal state
        public bool menuOpen = true, paused, settingsOpen, howToOpen, manifestOpen, ticketOpen, docOpen, cinematic, creditsOpen;
        public bool hasManifest, gameStarted;
        public bool Modal => menuOpen || paused || manifestOpen || ticketOpen || docOpen || cinematic || creditsOpen;

        // subtitles
        readonly Queue<Line> lines = new Queue<Line>();
        Line current; float lineT;
        public bool Speaking => current != null || lines.Count > 0;

        // objective
        public string chapterTitle = "";
        public Func<string> objective;
        float objectiveFlash;
        string lastObjective = "";

        // document
        string docTitle, docBody; Action onDocClose;

        // toast (ticket printouts, hints)
        string toast; float toastT; bool toastIsTicket;

        // title / ending cards
        string card, cardSub; float cardAlpha, cardTarget;
        public float fade = 1f, fadeTarget = 0f, fadeSpeed = 0.6f;

        // ticket panel
        Passenger ticketFor; Action<string> onTicket; string ticketFrom;

        // count
        public bool countRequested; public int countValue; Action<int> onCount;

        // credits
        string[] credits; float creditsT; Action onCreditsDone;

        public string controlsHint = "WASD move   ·   Mouse look   ·   E / Click interact   ·   hold SHIFT glance at the mirror   ·   TAB manifest   ·   SPACE next line   ·   ESC pause";
        float hintT;

        GUIStyle sTitle, sBig, sBody, sSmall, sSub, sSpeaker, sPrompt, sButton, sPaper, sPaperBold, sPaperSmall, sToggle, sCenter, sObjective, sChapter;
        Texture2D tGreen, tBlack, tPaper, tDim, tWhite, tFrame, tTicket, tRed;
        float S => Screen.height / 1080f;
        float VW => Screen.width / S;
        const float VH = 1080f;
        bool hadLock;

        void Awake() { I = this; }

        static Texture2D Tex(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

        void InitStyles()
        {
            if (sTitle != null) return;
            tBlack = Tex(new Color(0, 0, 0, 1));
            tGreen = Tex(new Color(0.4f, 0.85f, 0.45f));
            tDim = Tex(new Color(0, 0, 0, 0.62f));
            tPaper = Tex(new Color(0.86f, 0.82f, 0.7f, 0.98f));
            tWhite = Tex(Color.white);
            tFrame = Tex(new Color(0.08f, 0.08f, 0.08f, 1));
            tTicket = Tex(new Color(0.93f, 0.9f, 0.8f, 1));
            tRed = Tex(new Color(0.5f, 0.06f, 0.05f, 1));
            Font f = B.Font;
            sTitle = new GUIStyle { font = f, fontSize = 96, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            sTitle.normal.textColor = new Color(0.92f, 0.86f, 0.72f);
            sBig = new GUIStyle(sTitle) { fontSize = 54 };
            sBody = new GUIStyle { font = f, fontSize = 30, wordWrap = true, richText = true };
            sBody.normal.textColor = new Color(0.9f, 0.88f, 0.82f);
            sSmall = new GUIStyle(sBody) { fontSize = 22 };
            sSmall.normal.textColor = new Color(0.7f, 0.7f, 0.68f);
            sCenter = new GUIStyle(sBody) { alignment = TextAnchor.MiddleCenter };
            sSub = new GUIStyle(sBody) { alignment = TextAnchor.MiddleCenter, fontSize = 32 };
            sSpeaker = new GUIStyle(sSub) { fontStyle = FontStyle.Bold };
            sPrompt = new GUIStyle(sBody) { alignment = TextAnchor.MiddleCenter, fontSize = 26 };
            sPrompt.normal.textColor = new Color(1f, 0.92f, 0.7f);
            sObjective = new GUIStyle(sBody) { fontSize = 25 };
            sChapter = new GUIStyle(sBody) { fontSize = 19, fontStyle = FontStyle.Bold };
            sChapter.normal.textColor = new Color(0.95f, 0.6f, 0.25f);
            sButton = new GUIStyle { font = f, fontSize = 30, alignment = TextAnchor.MiddleCenter };
            sButton.normal.textColor = new Color(0.85f, 0.82f, 0.74f);
            sButton.hover.textColor = new Color(1f, 0.62f, 0.2f);
            sButton.hover.background = Tex(new Color(1, 1, 1, 0.06f));
            sButton.active.textColor = Color.white;
            sPaper = new GUIStyle { font = f, fontSize = 24, wordWrap = true, richText = true };
            sPaper.normal.textColor = new Color(0.12f, 0.1f, 0.12f);
            sPaperBold = new GUIStyle(sPaper) { fontStyle = FontStyle.Bold, fontSize = 28 };
            sPaperSmall = new GUIStyle(sPaper) { fontSize = 20 };
            sToggle = new GUIStyle(sPaperSmall) { alignment = TextAnchor.MiddleCenter };
            sToggle.normal.background = Tex(new Color(0, 0, 0, 0.08f));
            sToggle.hover.background = Tex(new Color(0.5f, 0.1f, 0.05f, 0.18f));
            sToggle.hover.textColor = new Color(0.3f, 0.05f, 0.03f);
            sToggle.onNormal.background = Tex(new Color(0.2f, 0.25f, 0.55f, 0.35f));
            sToggle.onNormal.textColor = new Color(0.05f, 0.08f, 0.3f);
            sToggle.onHover.background = sToggle.onNormal.background;
            sToggle.onHover.textColor = sToggle.onNormal.textColor;
        }

        // ------------------------------------------------------------------ API

        public void Say(string speaker, string text)
        {
            float d = Mathf.Clamp(1.4f + text.Length * 0.055f, 2.4f, 8f);
            lines.Enqueue(new Line { speaker = speaker, text = text, dur = d });
        }

        public void ClearLines() { lines.Clear(); current = null; }

        public void SetObjective(string chapter, Func<string> obj)
        {
            chapterTitle = chapter ?? chapterTitle;
            objective = obj;
        }

        public void ShowDoc(string title, string body, Action onClose = null)
        {
            docTitle = title; docBody = body; onDocClose = onClose; docOpen = true;
        }

        public void CloseDoc()
        {
            if (!docOpen) return;
            docOpen = false;
            var a = onDocClose; onDocClose = null;
            a?.Invoke();
        }

        public void Toast(string text, float seconds = 4f, bool ticket = false) { toast = text; toastT = seconds; toastIsTicket = ticket; }

        public void Card(string title, string sub) { card = title; cardSub = sub; cardTarget = 1; }
        public void HideCard() { cardTarget = 0; }

        public void OpenTicket(Passenger p, string from, Action<string> cb) { ticketFor = p; ticketFrom = from; onTicket = cb; ticketOpen = true; }
        public string TicketTarget => ticketFor ? ticketFor.destination : null;
        public void ChooseTicket(string dest) { ticketOpen = false; var cb = onTicket; onTicket = null; cb?.Invoke(dest); }

        public void RequestCount(Action<int> cb) { countRequested = true; countValue = 0; onCount = cb; }
        public void AnswerCount(int n) { countRequested = false; var cb = onCount; onCount = null; cb?.Invoke(n); }

        public void ShowCredits(string[] c, Action done) { credits = c; creditsT = 0; onCreditsDone = done; creditsOpen = true; }

        public void ShowHint(float seconds) { hintT = seconds; }

        // ------------------------------------------------------------------ update

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            fade = Mathf.MoveTowards(fade, fadeTarget, dt * fadeSpeed);
            cardAlpha = Mathf.MoveTowards(cardAlpha, cardTarget, dt * 0.7f);
            if (toastT > 0) toastT -= dt;
            if (hintT > 0) hintT -= dt;
            objectiveFlash = Mathf.Max(0, objectiveFlash - dt);

            // subtitles
            if (current == null && lines.Count > 0) { current = lines.Dequeue(); lineT = 0; OnLine?.Invoke(current.speaker, current.dur); }
            if (current != null)
            {
                if (!paused && !menuOpen) lineT += Time.deltaTime;
                if (lineT > current.dur || (Input.GetKeyDown(KeyCode.Space) && lineT > 0.35f && !menuOpen && !paused))
                {
                    if (lineT <= current.dur && Tutorial.I) Tutorial.I.spaceSkipped = true;
                    current = null;
                }
            }

            if (creditsOpen)
            {
                creditsT += dt;
                if (creditsT > 4f && (Input.anyKeyDown || creditsT > 38f)) { creditsOpen = false; var a = onCreditsDone; onCreditsDone = null; a?.Invoke(); }
            }

            if (gameStarted && !menuOpen && !creditsOpen)
            {
                bool othersOpen = ticketOpen || docOpen || cinematic || paused;
                if ((Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.M)) && hasManifest && !othersOpen)
                    manifestOpen = !manifestOpen;
                if (docOpen && (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Space)) && docOpenedFrame != Time.frameCount) CloseDoc();
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    if (manifestOpen) manifestOpen = false;
                    else if (docOpen) CloseDoc();
                    else if (ticketOpen) ticketOpen = false;
                    else if (!cinematic) { paused = !paused; settingsOpen = false; }
                }
#if UNITY_WEBGL && !UNITY_EDITOR
                // The browser releases pointer lock on ESC without telling us: treat it as pause.
                if (!Modal)
                {
                    if (Cursor.lockState == CursorLockMode.Locked) hadLock = true;
                    else if (hadLock) { hadLock = false; paused = true; }
                }
#endif
            }

            bool wantCursor = Modal && !cinematic;
            if (!gameStarted) wantCursor = true;
            if (wantCursor) { if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None; Cursor.visible = true; hadLock = false; }
            else { if (Cursor.lockState != CursorLockMode.Locked) Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }

            Time.timeScale = (paused || (menuOpen && gameStarted)) ? 0f : (StoryDirector.I ? StoryDirector.I.timeScale : 1f);
            AudioListener.pause = paused;
        }

        int docOpenedFrame;
        bool wasDoc;

        // ------------------------------------------------------------------ drawing

        void OnGUI()
        {
            InitStyles();
            if (docOpen && !wasDoc) docOpenedFrame = Time.frameCount;
            wasDoc = docOpen;
            GUI.matrix = Matrix4x4.Scale(new Vector3(S, S, 1));
            float w = VW, h = VH;
            var st = StoryDirector.I;
            var pl = PlayerController.I;
            int subSize = st != null && st.settings != null ? new[] { 26, 32, 40 }[Mathf.Clamp(st.settings.subtitleSize, 0, 2)] : 32;

            // mirror glance (SHIFT)
            if (pl && pl.mirrorHeld && BusSystems.I && BusSystems.I.mirrorRT && !Modal)
            {
                GUI.DrawTexture(new Rect(0, 0, w, h), tDim);
                float mw = Mathf.Min(1250, w - 80), mh = mw * 220f / 640f;
                var r = new Rect((w - mw) / 2, 70, mw, mh);
                GUI.DrawTexture(new Rect(r.x - 14, r.y - 14, r.width + 28, r.height + 28), tFrame);
                GUI.DrawTextureWithTexCoords(r, BusSystems.I.mirrorRT, new Rect(1, 0, -1, 1));
                GUI.Label(new Rect(r.x, r.yMax + 18, r.width, 30), "rear-view mirror", new GUIStyle(sSmall) { alignment = TextAnchor.MiddleCenter });
            }

            if (gameStarted && !menuOpen)
            {
                // objective
                if (objective != null && !cinematic && !manifestOpen && !ticketOpen)
                {
                    string o = objective();
                    if (o != lastObjective) { lastObjective = o; objectiveFlash = 2.5f; }
                    if (!string.IsNullOrEmpty(o))
                    {
                        var oc = sObjective.normal.textColor;
                        sObjective.normal.textColor = Color.Lerp(new Color(0.88f, 0.86f, 0.8f), new Color(1f, 0.7f, 0.3f), Mathf.PingPong(objectiveFlash * 2, 1));
                        GUI.DrawTexture(new Rect(24, 24, 640, 40 + sObjective.CalcHeight(new GUIContent(o), 600)), tDim);
                        GUI.Label(new Rect(42, 32, 600, 26), chapterTitle, sChapter);
                        GUI.Label(new Rect(42, 58, 600, 400), o, sObjective);
                        sObjective.normal.textColor = oc;
                    }
                }

                // crosshair & prompt
                if (!Modal && pl)
                {
                    bool f = pl.focus;
                    var cc = GUI.color;
                    GUI.color = f ? new Color(1, 0.8f, 0.4f, 0.95f) : new Color(1, 1, 1, 0.45f);
                    float cs = f ? 10 : 6;
                    GUI.DrawTexture(new Rect(w / 2 - cs / 2, h / 2 - cs / 2, cs, cs), tWhite);
                    GUI.color = cc;
                    if (f)
                    {
                        string p = "[E]  " + pl.focus.Prompt;
                        GUI.Label(new Rect(w / 2 - 500, h / 2 + 30, 1000, 40), p, Shadowed(sPrompt));
                    }
                }

                // objective marker
                if (!Modal && st != null && st.settings.marker && pl) DrawMarker(st, pl, w, h);

                // hint line
                if (hintT > 0 && !Modal)
                {
                    var c = GUI.color; GUI.color = new Color(1, 1, 1, Mathf.Clamp01(hintT));
                    GUI.Label(new Rect(0, h - 48, w, 30), controlsHint, new GUIStyle(sSmall) { alignment = TextAnchor.MiddleCenter });
                    GUI.color = c;
                }

                // subtitles
                if (current != null)
                {
                    var style = new GUIStyle(sSub) { fontSize = subSize };
                    string sp = current.speaker;
                    string txt = string.IsNullOrEmpty(sp) ? "<i>" + current.text + "</i>" : "<b><color=" + SpeakerColor(sp) + ">" + sp + "</color></b>   " + current.text;
                    style.richText = true;
                    float bw = Mathf.Min(1300, w - 100);
                    float bh = style.CalcHeight(new GUIContent(txt), bw - 40) + 26;
                    var r = new Rect((w - bw) / 2, h - 150 - bh, bw, bh);
                    GUI.DrawTexture(r, tDim);
                    GUI.Label(new Rect(r.x + 20, r.y + 13, r.width - 40, r.height - 26), txt, style);
                }

                // toast / ticket printout
                if (toastT > 0 && !string.IsNullOrEmpty(toast))
                {
                    var c = GUI.color; GUI.color = new Color(1, 1, 1, Mathf.Clamp01(toastT * 2));
                    if (toastIsTicket)
                    {
                        var r = new Rect(w - 420, h - 470, 360, 300);
                        GUI.DrawTexture(new Rect(r.x + 6, r.y + 6, r.width, r.height), tDim);
                        GUI.DrawTexture(r, tTicket);
                        GUI.DrawTexture(new Rect(r.x, r.y, r.width, 8), tRed);
                        GUI.Label(new Rect(r.x + 22, r.y + 22, r.width - 44, r.height - 30), toast, new GUIStyle(sPaperSmall) { font = B.Font });
                    }
                    else
                    {
                        var style = new GUIStyle(sBody) { alignment = TextAnchor.MiddleCenter, fontSize = 26 };
                        float tw = Mathf.Min(900, w - 80);
                        float th = style.CalcHeight(new GUIContent(toast), tw - 30) + 24;
                        var r = new Rect((w - tw) / 2, 130, tw, th);
                        GUI.DrawTexture(r, tDim);
                        GUI.Label(new Rect(r.x + 15, r.y + 12, r.width - 30, r.height - 24), toast, style);
                    }
                    GUI.color = c;
                }

                if (!Modal || manifestOpen || ticketOpen) DrawTutorial(w, h);
                if (manifestOpen && st) DrawManifest(st, w, h);
                if (ticketOpen) DrawTicketPanel(w, h);
                if (docOpen) DrawDoc(w, h);
            }

            // fade & card
            if (fade > 0.001f) { var c = GUI.color; GUI.color = new Color(1, 1, 1, fade); GUI.DrawTexture(new Rect(0, 0, w, h), tBlack); GUI.color = c; }
            if (cardAlpha > 0.001f && !string.IsNullOrEmpty(card))
            {
                var c = GUI.color; GUI.color = new Color(1, 1, 1, cardAlpha);
                GUI.Label(new Rect(0, h * 0.36f, w, 120), card, sBig);
                if (!string.IsNullOrEmpty(cardSub)) GUI.Label(new Rect(w * 0.15f, h * 0.36f + 120, w * 0.7f, 120), cardSub, new GUIStyle(sCenter) { fontSize = 28 });
                GUI.color = c;
            }

            if (creditsOpen) DrawCredits(w, h);
            if (menuOpen) DrawMenu(st, w, h);
            else if (paused) DrawPause(st, w, h);
        }

        GUIStyle Shadowed(GUIStyle s) => s;

        static string SpeakerColor(string sp)
        {
            switch (sp)
            {
                case "RAGHAVAN": return "#f0b05a";
                case "MEENA": return "#5fc9c0";
                case "SELVAM": return "#e8e2d0";
                case "KAVIN": return "#8fa8ff";
                case "LAKSHMI": return "#e3c060";
                case "BALA": return "#ff9fb0";
                case "YOU": return "#bfbfbf";
                case "PHONE": return "#c79bff";
                default: return "#ff6a5a";
            }
        }

        // ------------------------------------------------------------------ manifest

        void DrawManifest(StoryDirector st, float w, float h)
        {
            GUI.DrawTexture(new Rect(0, 0, w, h), tDim);
            float pw = Mathf.Min(1280, w - 60), ph = 900;
            var r = new Rect((w - pw) / 2, (h - ph) / 2, pw, ph);
            GUI.DrawTexture(new Rect(r.x + 10, r.y + 10, r.width, r.height), tDim);
            GUI.DrawTexture(r, tPaper);
            float x = r.x + 40, y = r.y + 30;
            GUI.Label(new Rect(x, y, pw - 80, 36), "PASSENGER MANIFEST  -  ROUTE 117  -  LAST SERVICE 01:17", sPaperBold);
            y += 42;
            int count = st.ManifestCount();
            GUI.Label(new Rect(x, y, pw - 80, 30), "Conductor: ____________        Aboard by your marks: <b>" + count + "</b>        Mistakes cannot be erased.", sPaperSmall);
            y += 34;
            if (st.data.flags.chapter <= 2 && st.settings.tutorial && !st.finalMode)
                GUI.Label(new Rect(x, y, pw - 80, 26), "<i>ABOARD = they got on and have a ticket.   EXITED = you saw them get off.   Notes are optional reminders.</i>", sPaperSmall);
            y += 30;
            float[] col = { 0, 50, 260, 340, 560, 700, 950 };
            string[] head = { "#", "Name", "Seat", "To", "Status", "Mark", "Notes" };
            for (int i = 0; i < head.Length; i++) GUI.Label(new Rect(x + col[i], y, 240, 28), "<b>" + head[i] + "</b>", sPaperSmall);
            y += 32;
            GUI.DrawTexture(new Rect(x, y, pw - 80, 2), tFrame);
            y += 8;
            var rows = st.data.rows;
            for (int i = 0; i < 6; i++)
            {
                float ry = y + i * 62;
                GUI.Label(new Rect(x + col[0], ry + 12, 40, 30), (i + 1).ToString(), sPaper);
                if (i < rows.Count)
                {
                    var row = rows[i];
                    GUI.Label(new Rect(x + col[1], ry + 12, 210, 30), row.name, sPaper);
                    GUI.Label(new Rect(x + col[2], ry + 12, 80, 30), row.seat, sPaper);
                    GUI.Label(new Rect(x + col[3], ry + 12, 220, 30), row.dest, sPaperSmall);
                    string status = row.status == 1 ? "<color=#1a3a8a>ABOARD</color>" : row.status == 2 ? "<color=#7a1010>EXITED</color>" : "-";
                    if (st.finalMode && row.remembered) status += "\n<color=#205020>remembered</color>";
                    GUI.Label(new Rect(x + col[4], ry + 4, 140, 56), status, sPaperSmall);
                    if (!st.finalMode)
                    {
                        if (GUI.Button(new Rect(x + col[5], ry + 8, 110, 40), "Aboard", sToggle)) st.OnMark(row, 1);
                        if (GUI.Button(new Rect(x + col[5] + 120, ry + 8, 110, 40), "Exited", sToggle)) st.OnMark(row, 2);
                    }
                    else
                    {
                        bool rem = GUI.Toggle(new Rect(x + col[5], ry + 8, 230, 40), row.remembered, row.remembered ? "Remembered" : "Remember", sToggle);
                        if (rem != row.remembered) st.OnRemember(row, rem);
                    }
                    row.noteMirror = GUI.Toggle(new Rect(x + col[6], ry + 8, 82, 40), row.noteMirror, "mirror", sToggle);
                    row.noteRear = GUI.Toggle(new Rect(x + col[6] + 88, ry + 8, 82, 40), row.noteRear, "rear", sToggle);
                    row.noteUnknown = GUI.Toggle(new Rect(x + col[6] + 176, ry + 8, 100, 40), row.noteUnknown, "unsure", sToggle);
                }
                else GUI.Label(new Rect(x + col[1], ry + 12, 400, 30), "<color=#00000055>__________________</color>", sPaper);
                GUI.DrawTexture(new Rect(x, ry + 58, pw - 80, 1), tDim);
            }
            y += 6 * 62 + 14;

            // general notes
            GUI.Label(new Rect(x, y + 6, 200, 30), "<b>Notes:</b>", sPaperSmall);
            st.data.gNoteMirror = GUI.Toggle(new Rect(x + 110, y, 260, 40), st.data.gNoteMirror, "someone seen in mirror", sToggle);
            st.data.gNoteRear = GUI.Toggle(new Rect(x + 380, y, 260, 40), st.data.gNoteRear, "heard at rear door", sToggle);
            st.data.gNoteUnknown = GUI.Toggle(new Rect(x + 650, y, 260, 40), st.data.gNoteUnknown, "unknown passenger", sToggle);
            y += 60;

            if (countRequested)
            {
                GUI.DrawTexture(new Rect(x - 10, y - 6, pw - 60, 64), tDim);
                GUI.Label(new Rect(x + 10, y + 10, 420, 34), "<color=#f0e0c0>How many passengers can you see aboard?</color>", sBody);
                if (GUI.Button(new Rect(x + 600, y + 6, 50, 44), "-", sButton)) countValue = Mathf.Max(0, countValue - 1);
                GUI.Label(new Rect(x + 655, y + 6, 60, 44), "<color=#ffd080>" + countValue + "</color>", new GUIStyle(sButton) { richText = true });
                if (GUI.Button(new Rect(x + 715, y + 6, 50, 44), "+", sButton)) countValue = Mathf.Min(12, countValue + 1);
                if (GUI.Button(new Rect(x + 800, y + 6, 330, 44), "Confirm count", sButton)) AnswerCount(countValue);
                y += 74;
            }

            if (st.finalMode)
            {
                GUI.DrawTexture(new Rect(x - 10, y - 6, pw - 60, 150), tDim);
                GUI.Label(new Rect(x + 10, y + 4, 300, 34), "<color=#f0e0c0>Conductor:</color>", sBody);
                if (st.data.flags.badge)
                {
                    int sign = st.conductorSign;
                    if (GUI.Toggle(new Rect(x + 190, y, 370, 44), sign == 1, "Sign: ARUN - conductor", sToggle) && sign != 1) st.conductorSign = 1;
                    if (GUI.Toggle(new Rect(x + 575, y, 400, 44), sign == 2, "Add ARUN as a passenger", sToggle) && sign != 2) st.conductorSign = 2;
                }
                else GUI.Label(new Rect(x + 190, y + 4, 600, 34), "<color=#c0b0a0>(you can't remember the name)</color>", sBody);
                if (GUI.Button(new Rect(x + 10, y + 70, 520, 56), "Complete the manifest", sButton)) st.CompleteManifest();
                if (GUI.Button(new Rect(x + 560, y + 70, 520, 56), "Tear out the page", sButton)) st.TearManifest();
            }

            GUI.Label(new Rect(r.x, r.yMax - 46, pw, 30), "[TAB] close", new GUIStyle(sPaperSmall) { alignment = TextAnchor.MiddleCenter });
        }

        void DrawTicketPanel(float w, float h)
        {
            GUI.DrawTexture(new Rect(0, 0, w, h), tDim);
            float pw = 620, ph = 640;
            var r = new Rect((w - pw) / 2, (h - ph) / 2, pw, ph);
            GUI.DrawTexture(r, tFrame);
            GUI.Label(new Rect(r.x + 30, r.y + 24, pw - 60, 40), "TICKET MACHINE  ·  07-11-2015  01:17", new GUIStyle(sBody) { fontSize = 24 });
            GUI.Label(new Rect(r.x + 30, r.y + 64, pw - 60, 40), "Passenger: " + (ticketFor ? ticketFor.displayName : "?") + "      From: " + ticketFrom, sSmall);
            GUI.Label(new Rect(r.x + 30, r.y + 92, pw - 60, 30), "Choose the stop the passenger asked for.", new GUIStyle(sSmall) { fontSize = 19, normal = { textColor = new Color(0.95f, 0.6f, 0.25f) } });
            float y = r.y + 124;
            var stops = StoryDirector.Stops;
            for (int i = 0; i < stops.Length; i++)
            {
                bool child = ticketFor && ticketFor.child;
                int fare = StoryDirector.Fares[i];
                string label = stops[i] + "   ·   Rs " + (child ? fare / 2 + " (child)" : fare.ToString());
                if (GUI.Button(new Rect(r.x + 30, y, pw - 60, 64), label, sButton)) { ChooseTicket(stops[i]); return; }
                y += 72;
            }
            if (GUI.Button(new Rect(r.x + 30, r.yMax - 84, pw - 60, 60), "Cancel", sButton)) ticketOpen = false;
        }

        void DrawDoc(float w, float h)
        {
            GUI.DrawTexture(new Rect(0, 0, w, h), tDim);
            float pw = Mathf.Min(980, w - 60);
            float bh = sPaper.CalcHeight(new GUIContent(docBody), pw - 100);
            float ph = Mathf.Min(h - 80, bh + 200);
            var r = new Rect((w - pw) / 2, (h - ph) / 2, pw, ph);
            GUI.DrawTexture(new Rect(r.x + 10, r.y + 10, r.width, r.height), tDim);
            GUI.DrawTexture(r, tPaper);
            GUI.Label(new Rect(r.x + 50, r.y + 36, pw - 100, 40), docTitle, sPaperBold);
            GUI.Label(new Rect(r.x + 50, r.y + 92, pw - 100, ph - 150), docBody, sPaper);
            if (GUI.Button(new Rect(r.x + pw / 2 - 160, r.yMax - 66, 320, 50), "[E]  Close", new GUIStyle(sButton) { normal = { textColor = new Color(0.3f, 0.1f, 0.05f) } }) && docOpenedFrame != Time.frameCount)
                CloseDoc();
        }

        void DrawCredits(float w, float h)
        {
            GUI.DrawTexture(new Rect(0, 0, w, h), tBlack);
            if (credits == null) return;
            float y = h - creditsT * 60f;
            foreach (var line in credits)
            {
                bool head = line.StartsWith("#");
                var s = head ? new GUIStyle(sBig) { fontSize = 46 } : new GUIStyle(sCenter) { fontSize = 28 };
                GUI.Label(new Rect(0, y, w, 60), head ? line.Substring(1) : line, s);
                y += head ? 80 : 46;
            }
            if (creditsT > 4f) GUI.Label(new Rect(0, h - 50, w, 30), "press any key", new GUIStyle(sSmall) { alignment = TextAnchor.MiddleCenter });
        }

        // ------------------------------------------------------------------ menus

        void DrawMenu(StoryDirector st, float w, float h)
        {
            GUI.DrawTexture(new Rect(0, 0, w, h), tDim);
            if (settingsOpen) { DrawSettings(st, w, h); return; }
            if (howToOpen) { DrawHowTo(w, h); return; }
            GUI.Label(new Rect(0, h * 0.14f, w, 120), "THE LAST PASSENGER", sTitle);
            GUI.Label(new Rect(0, h * 0.14f + 110, w, 50), "NIGHT SHIFT  ·  ROUTE 117  ·  01:17 A.M.", new GUIStyle(sCenter) { fontSize = 28, normal = { textColor = new Color(0.95f, 0.6f, 0.25f) } });
            GUI.Label(new Rect(0, h * 0.14f + 160, w, 50), "\"Conductor... don't count the people you can hear.\"", new GUIStyle(sCenter) { fontSize = 24, fontStyle = FontStyle.Italic });

            float bw = 440, bx = (w - bw) / 2, y = h * 0.47f;
            if (GUI.Button(new Rect(bx, y, bw, 60), "Begin the night shift", sButton)) st.NewGame();
            y += 70;
            if (st.HasSave)
            {
                if (GUI.Button(new Rect(bx, y, bw, 60), "Continue  (Chapter " + st.SavedChapter + ")", sButton)) st.Continue();
                y += 70;
            }
            if (GUI.Button(new Rect(bx, y, bw, 60), "How to play", sButton)) howToOpen = true;
            y += 70;
            if (GUI.Button(new Rect(bx, y, bw, 60), "Settings", sButton)) settingsOpen = true;
            y += 70;
#if !UNITY_WEBGL
            if (GUI.Button(new Rect(bx, y, bw, 60), "Quit", sButton)) Application.Quit();
            y += 70;
#endif
            GUI.Label(new Rect(0, h - 140, w, 30), "Endings found: " + st.EndingsFound(), new GUIStyle(sSmall) { alignment = TextAnchor.MiddleCenter });
            GUI.Label(new Rect(0, h - 100, w, 30), "Headphones recommended. Approx. 25-35 minutes. Contains psychological horror.", new GUIStyle(sSmall) { alignment = TextAnchor.MiddleCenter });
            GUI.Label(new Rect(0, h - 64, w, 30), "GitHub Game Off 2026  ·  theme: SHIFT", new GUIStyle(sSmall) { alignment = TextAnchor.MiddleCenter });
        }

        void DrawPause(StoryDirector st, float w, float h)
        {
            GUI.DrawTexture(new Rect(0, 0, w, h), tDim);
            if (settingsOpen) { DrawSettings(st, w, h); return; }
            if (howToOpen) { DrawHowTo(w, h); return; }
            GUI.Label(new Rect(0, h * 0.18f, w, 80), "PAUSED", sBig);
            float bw = 520, bx = (w - bw) / 2, y = h * 0.34f;
            if (GUI.Button(new Rect(bx, y, bw, 60), "Resume", sButton)) { paused = false; Cursor.lockState = CursorLockMode.Locked; }
            y += 70;
            if (GUI.Button(new Rect(bx, y, bw, 60), "How to play", sButton)) howToOpen = true;
            y += 70;
            if (GUI.Button(new Rect(bx, y, bw, 60), "Settings", sButton)) settingsOpen = true;
            y += 70;
            if (GUI.Button(new Rect(bx, y, bw, 60), "Restart chapter", sButton)) { paused = false; st.Continue(); }
            y += 70;
            if (GUI.Button(new Rect(bx, y, bw, 60), "Quit to title", sButton)) { paused = false; st.ToTitle(); }
            GUI.Label(new Rect(w * 0.1f, h * 0.78f, w * 0.8f, 80), controlsHint, new GUIStyle(sSmall) { alignment = TextAnchor.MiddleCenter, wordWrap = true });
        }

        void DrawTutorial(float w, float h)
        {
            var t = Tutorial.I;
            if (t == null) return;
            bool flash = t.justCompleted > 0;
            if (string.IsNullOrEmpty(t.CurrentText) && !flash) return;
            float pw = 480;
            var body = new GUIStyle(sBody) { fontSize = 25 };
            string text = flash && string.IsNullOrEmpty(t.CurrentText) ? "" : t.CurrentText ?? "";
            float bh = string.IsNullOrEmpty(text) ? 0 : body.CalcHeight(new GUIContent(text), pw - 40);
            var r = new Rect(w - pw - 24, h * 0.24f, pw, bh + 92);
            if (manifestOpen || ticketOpen)
            {
                // Compact banner above the open panel so it never covers the manifest.
                pw = Mathf.Min(1280, w - 60);
                bh = body.CalcHeight(new GUIContent(text), pw - 40);
                r = new Rect((w - pw) / 2, 4, pw, Mathf.Min(bh + 40, 84));
                GUI.DrawTexture(r, tDim);
                GUI.Label(new Rect(r.x + 16, r.y + 6, pw - 32, r.height - 8), "<b><color=#f09a40>" + (t.Forced ? "TRAINING " + t.forcedIndex + "/" + t.forcedTotal : "TIP") + "</color></b>   " + text, new GUIStyle(body) { fontSize = 21, richText = true });
                return;
            }
            GUI.DrawTexture(r, tDim);
            GUI.DrawTexture(new Rect(r.x, r.y, 4, r.height), flash ? tGreen : tRed);
            var head = new GUIStyle(sChapter) { fontSize = 18 };
            string title = t.Forced ? "TRAINING  " + t.forcedIndex + "/" + t.forcedTotal + "  ·  " + t.forcedTitle
                                    : "TUTORIAL" + (string.IsNullOrEmpty(t.CurrentTitle) ? "" : "  ·  " + t.CurrentTitle);
            if (flash) title = "DONE  ·  " + t.lastCompleted;
            GUI.Label(new Rect(r.x + 20, r.y + 12, pw - 40, 26), title, head);
            GUI.Label(new Rect(r.x + 20, r.y + 42, pw - 40, bh + 4), text, body);
            GUI.Label(new Rect(r.x + 20, r.yMax - 32, pw - 40, 24), "training can be switched off in Settings for replays", new GUIStyle(sSmall) { fontSize = 16 });
        }

        void DrawMarker(StoryDirector st, PlayerController pl, float w, float h)
        {
            var target = st.CurrentTarget;
            if (!target.HasValue) return;
            var cam = pl.cam;
            var sp = cam.WorldToScreenPoint(target.Value);
            bool behind = sp.z < 0;
            float x = sp.x / S, y = (Screen.height - sp.y) / S;
            if (behind) { x = w - x; y = h - y; }
            float m = 70;
            bool off = behind || x < m || x > w - m || y < m || y > h - m;
            if (off)
            {
                // push to the screen edge in the target's direction
                var c = new Vector2(w / 2, h / 2);
                var d = new Vector2(x, y) - c;
                if (behind && d.sqrMagnitude < 1) d = new Vector2(0, 1);
                float k = Mathf.Min((w / 2 - m) / Mathf.Max(Mathf.Abs(d.x), 0.001f), (h / 2 - m) / Mathf.Max(Mathf.Abs(d.y), 0.001f));
                var e = c + d * k; x = e.x; y = e.y;
            }
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 5f);
            var old = GUI.matrix; var oc = GUI.color;
            GUI.color = new Color(1f, 0.78f, 0.25f, pulse);
            GUIUtility.RotateAroundPivot(45, new Vector2(x * S, y * S));
            GUI.DrawTexture(new Rect(x - 9, y - 9, 18, 18), tWhite);
            GUI.matrix = old;
            GUI.color = new Color(0, 0, 0, 0.9f);
            GUIUtility.RotateAroundPivot(45, new Vector2(x * S, y * S));
            GUI.DrawTexture(new Rect(x - 4, y - 4, 8, 8), tWhite);
            GUI.matrix = old;
            GUI.color = oc;
            float dist = Vector3.Distance(pl.transform.position, target.Value);
            string label = off ? (behind ? "behind you" : "this way") : dist.ToString("0") + " m";
            GUI.Label(new Rect(x - 80, y + 14, 160, 26), label, new GUIStyle(sSmall) { alignment = TextAnchor.MiddleCenter, fontSize = 18, normal = { textColor = new Color(1f, 0.85f, 0.5f) } });
        }

        void DrawHowTo(float w, float h)
        {
            GUI.Label(new Rect(0, h * 0.06f, w, 80), "HOW TO PLAY", sBig);
            float cw = Mathf.Min(1500, w - 80), x = (w - cw) / 2, colW = (cw - 60) / 2, y = h * 0.16f;
            var head = new GUIStyle(sChapter) { fontSize = 22 };
            var body = new GUIStyle(sBody) { fontSize = 22 };
            string left =
                "<b>YOUR JOB</b>\n" +
                "You are the conductor on the last night bus. The driver drives. You look after the passengers:\n" +
                "  1.  Talk to anyone without a ticket (E) and print one for the stop they want.\n" +
                "  2.  Open the manifest (TAB) and mark them ABOARD.\n" +
                "  3.  When someone gets off, mark them EXITED.\n" +
                "  4.  When asked, confirm how many people you can see.\n" +
                "  5.  Ring the bell: two rings = go, one ring = stop.\n\n" +
                "<b>THE MANIFEST</b>\n" +
                "Your memory of the night. Entries are in ink and can't be erased. Marking someone off who is still on the bus, or getting a count wrong, has consequences. You'll see them.\n\n" +
                "<b>THE MIRROR  (SHIFT)</b>\n" +
                "Hold SHIFT to glance at the rear-view mirror from anywhere on the bus. It shows the whole cabin behind you. Compare it with what you see when you turn around.";
            string right =
                "<b>CONTROLS</b>\n" +
                "W A S D ............ walk\n" +
                "Mouse ................ look\n" +
                "E / left click ...... interact (talk, tickets, bell, doors, evidence)\n" +
                "hold SHIFT ......... rear-view mirror\n" +
                "TAB  or  M ........ manifest\n" +
                "SPACE ............... next subtitle line\n" +
                "ESC ................... pause\n\n" +
                "<b>GOOD TO KNOW</b>\n" +
                "·  Nothing can kill you. There is no combat and no chase.\n" +
                "·  Your objective is always shown in the top-left corner.\n" +
                "·  Look closely at things that have no objective: seats, windows, signs, the rear of the bus.\n" +
                "·  The game saves at the start of every chapter.\n" +
                "·  There are three endings. How you finish the manifest matters.\n" +
                "·  Play with headphones, in the dark.";
            GUI.Label(new Rect(x, y, colW, h * 0.7f), left, body);
            GUI.Label(new Rect(x + colW + 60, y, colW, h * 0.7f), right, body);
            if (GUI.Button(new Rect(w / 2 - 200, h - 110, 400, 60), "Back", sButton)) howToOpen = false;
        }

        void DrawSettings(StoryDirector st, float w, float h)
        {
            var s = st.settings;
            GUI.Label(new Rect(0, h * 0.14f, w, 80), "SETTINGS", sBig);
            float x = w / 2 - 420, y = h * 0.3f;
            GUI.Label(new Rect(x, y, 380, 40), "Mouse sensitivity  " + s.sensitivity.ToString("0.0"), sBody);
            s.sensitivity = GUI.HorizontalSlider(new Rect(x + 440, y + 14, 400, 30), s.sensitivity, 0.3f, 6f);
            y += 64;
            GUI.Label(new Rect(x, y, 380, 40), "Volume  " + Mathf.RoundToInt(s.volume * 100) + "%", sBody);
            s.volume = GUI.HorizontalSlider(new Rect(x + 440, y + 14, 400, 30), s.volume, 0f, 1f);
            AudioListener.volume = s.volume;
            y += 64;
            GUI.Label(new Rect(x, y, 380, 40), "Subtitle size", sBody);
            string[] sizes = { "Small", "Medium", "Large" };
            for (int i = 0; i < 3; i++)
                if (GUI.Button(new Rect(x + 440 + i * 135, y, 130, 44), (s.subtitleSize == i ? "> " : "") + sizes[i], sButton)) s.subtitleSize = i;
            y += 64;
            if (GUI.Button(new Rect(x, y, 840, 50), "Invert mouse Y:  " + (s.invertY ? "ON" : "OFF"), sButton)) s.invertY = !s.invertY;
            y += 60;
            if (GUI.Button(new Rect(x, y, 840, 50), "Reduce motion (no head bob / bus sway):  " + (s.reduceMotion ? "ON" : "OFF"), sButton)) s.reduceMotion = !s.reduceMotion;
            y += 60;
            if (GUI.Button(new Rect(x, y, 840, 50), "Training before a new shift:  " + (s.tutorial ? "ON" : "OFF"), sButton)) s.tutorial = !s.tutorial;
            y += 60;
            if (GUI.Button(new Rect(x, y, 840, 50), "Objective marker on screen:  " + (s.marker ? "ON" : "OFF"), sButton)) s.marker = !s.marker;
            y += 80;
            if (GUI.Button(new Rect(w / 2 - 200, y, 400, 60), "Back", sButton)) { settingsOpen = false; s.Save(); }
        }
    }
}
