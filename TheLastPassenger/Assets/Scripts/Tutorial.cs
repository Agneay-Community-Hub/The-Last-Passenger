using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TLP
{
    /// <summary>
    /// Step-by-step tips during the first shift. Each tip appears when it becomes relevant and
    /// ticks itself off when the player actually does the thing. Can be turned off in Settings.
    /// </summary>
    public class Tutorial : MonoBehaviour
    {
        public static Tutorial I;

        class Step
        {
            public string title, text;
            public Func<bool> available, done;
            public bool complete;
            public float shown;
        }

        readonly List<Step> steps = new List<Step>();
        public string CurrentTitle, CurrentText;
        public int Done => steps.Count(s => s.complete);
        public int Total => steps.Count;
        public float justCompleted;   // seconds left of the "done" flash
        public string lastCompleted;

        // signals from other systems
        public bool manifestOpened, manifestClosedAfterOpen, spaceSkipped, countSeen;
        public float mirrorHeldT;
        public int bellRings;

        Step current;

        void Awake() { I = this; Build(); }

        void Build()
        {
            var st = (Func<StoryDirector>)(() => StoryDirector.I);
            var ui = (Func<UIManager>)(() => UIManager.I);
            var pl = (Func<PlayerController>)(() => PlayerController.I);

            Add("LOOK", "Move the MOUSE to look around.",
                () => true, () => pl().lookTravel > 120f);
            Add("WALK", "Use  W  A  S  D  to walk. The bus is right in front of you.",
                () => true, () => pl().moveTravel > 2.5f);
            Add("INTERACT", "When the dot in the middle of the screen turns orange, press  E  or left-click to use what you're looking at.",
                () => true, () => pl().interactions >= 1);
            Add("THE MANIFEST", "Press  TAB  (or M) to open the passenger manifest. Press it again to close it.",
                () => ui().hasManifest, () => manifestClosedAfterOpen);
            Add("TICKETS", "Walk up to a passenger and press  E. Pick the stop they asked for on the ticket machine. The fare chart is on the wall by the door.",
                () => st().P.Values.Any(p => p.needsTicket), () => st().data.rows.Count >= 1);
            Add("RECORD", "Open the manifest and press  ABOARD  next to each passenger you ticketed. Ink can't be erased, so mark carefully.",
                () => st().data.rows.Count >= 1, () => st().data.rows.Any(r => r.status == 1));
            Add("DIALOGUE", "Subtitle lines move on by themselves. Press  SPACE  to skip to the next line.",
                () => ui().Speaking && st().data.rows.Count >= 1, () => spaceSkipped);
            Add("THE MIRROR", "Hold  SHIFT  to glance at the rear-view mirror. It shows the whole cabin behind you. Check it often.",
                () => st().data.rows.Count >= 2, () => mirrorHeldT > 1.0f);
            Add("COUNTING", "When the driver asks for a count, open the manifest and set the number of passengers you can actually see, then confirm.",
                () => ui().countRequested, () => countSeen && !ui().countRequested);
            Add("THE BELL", "Press the red bell button beside either door. Two rings tell the driver to go, one ring asks him to stop.",
                () => st().BellPending, () => bellRings >= 1);
            Add("ANY TIME", "ESC pauses the game. Settings and 'How to play' are in the pause menu. That's the job. Good luck, conductor.",
                () => steps.Take(steps.Count - 1).All(s => s.complete), null);
        }

        void Add(string title, string text, Func<bool> available, Func<bool> done)
        {
            var s = new Step { title = title, text = text, available = available };
            s.done = done ?? (() => s.shown > 9f);
            steps.Add(s);
        }

        public void ResetAll()
        {
            foreach (var s in steps) { s.complete = false; s.shown = 0; }
            manifestOpened = manifestClosedAfterOpen = spaceSkipped = countSeen = false;
            mirrorHeldT = 0; bellRings = 0;
        }

        public bool Active
        {
            get
            {
                var st = StoryDirector.I; var ui = UIManager.I;
                return st != null && ui != null && ui.gameStarted && !ui.menuOpen && !ui.creditsOpen
                       && st.settings.tutorial && st.F.chapter <= 2 && (!st.bot || st.botTutorial) && Done < Total;
            }
        }

        void Update()
        {
            if (justCompleted > 0) justCompleted -= Time.unscaledDeltaTime;
            CurrentTitle = CurrentText = null;
            var ui = UIManager.I; var pl = PlayerController.I;
            if (ui == null || pl == null) return;

            if (ui.manifestOpen) manifestOpened = true;
            else if (manifestOpened) manifestClosedAfterOpen = true;
            if (ui.countRequested) countSeen = true;
            if (pl.mirrorHeld) mirrorHeldT += Time.deltaTime;

            if (!Active) return;
            try
            {
                foreach (var s in steps)
                {
                    if (s.complete) continue;
                    if (s.done())
                    {
                        s.complete = true;
                        lastCompleted = s.title;
                        if (s == current) { justCompleted = 1.2f; if (AudioDirector.I) AudioDirector.I.Play2D(AudioDirector.I.clink, 0.15f, 1.6f); }
                    }
                }
                current = steps.FirstOrDefault(s => !s.complete && s.available());
            }
            catch (Exception) { current = null; }

            if (current != null)
            {
                current.shown += Time.deltaTime;
                CurrentTitle = current.title;
                CurrentText = current.text;
            }
        }
    }
}
