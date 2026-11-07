using System;
using System.Collections.Generic;
using UnityEngine;

namespace TLP
{
    /// <summary>
    /// Every sound in the game is synthesised at startup: diesel engine, monsoon rain, fluorescent hum,
    /// mechanical bell, dot-matrix ticket printer, knocks, air brakes, an old phone, whispers and a drone.
    /// </summary>
    public class AudioDirector : MonoBehaviour
    {
        public static AudioDirector I;
        const int SR = 22050;

        public AudioClip engine, rain, hum, rattle, drone, bell, printer, knock, door, brake, indicator,
                         phone, whisper, step, speaker, thump, tear, heartbeat, clink, voiceMurmur, stinger, breath, scrape, rattleHit;

        AudioSource engineSrc, rainSrc, humSrc, rattleSrc, droneSrc, heartSrc;
        readonly List<AudioSource> pool = new List<AudioSource>();
        System.Random rng = new System.Random(117);

        float engineTarget = 0.35f, enginePitch = 0.8f, droneTarget, heartTarget, rainTarget = 0.45f, humTarget = 0.12f;
        public bool engineOn = true;

        void Awake()
        {
            I = this;
            Generate();
            engineSrc = Loop(engine, 0.35f);
            rainSrc = Loop(rain, 0.45f);
            humSrc = Loop(hum, 0.12f);
            rattleSrc = Loop(rattle, 0.25f);
            droneSrc = Loop(drone, 0f);
            heartSrc = Loop(heartbeat, 0f);
            for (int i = 0; i < 16; i++)
            {
                var g = new GameObject("sfx" + i);
                g.transform.SetParent(transform, false);
                var s = g.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 1f; s.maxDistance = 22f;
                s.dopplerLevel = 0;
                pool.Add(s);
            }
        }

        AudioSource Loop(AudioClip c, float vol)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = c; s.loop = true; s.volume = vol; s.spatialBlend = 0; s.playOnAwake = false;
            s.Play();
            return s;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            engineSrc.volume = Mathf.MoveTowards(engineSrc.volume, engineOn ? engineTarget : 0f, dt * 0.6f);
            engineSrc.pitch = Mathf.MoveTowards(engineSrc.pitch, enginePitch, dt * 0.5f);
            rattleSrc.volume = Mathf.MoveTowards(rattleSrc.volume, engineOn ? engineTarget * 0.6f : 0f, dt);
            droneSrc.volume = Mathf.MoveTowards(droneSrc.volume, droneTarget, dt * 0.08f);
            heartSrc.volume = Mathf.MoveTowards(heartSrc.volume, heartTarget, dt * 0.3f);
            rainSrc.volume = Mathf.MoveTowards(rainSrc.volume, rainTarget, dt * 0.3f);
            humSrc.volume = Mathf.MoveTowards(humSrc.volume, humTarget, dt * 0.5f);
        }

        public void SetSpeed(float speed01)
        {
            engineTarget = 0.28f + speed01 * 0.22f;
            enginePitch = 0.75f + speed01 * 0.45f;
        }
        public void SetDrone(float v) => droneTarget = v;
        public void SetHeart(float v) => heartTarget = v;
        public void SetRain(float v) => rainTarget = v;
        public void SetHum(float v) => humTarget = v;

        public AudioSource PlayAt(AudioClip c, Vector3 pos, float vol = 1f, float pitch = 1f, float spatial = 1f)
        {
            if (!c) return null;
            AudioSource s = null;
            foreach (var p in pool) if (!p.isPlaying) { s = p; break; }
            if (s == null) s = pool[0];
            s.transform.position = pos;
            s.clip = c; s.volume = vol; s.pitch = pitch; s.spatialBlend = spatial; s.loop = false;
            s.Play();
            return s;
        }

        public AudioSource Play2D(AudioClip c, float vol = 1f, float pitch = 1f) => PlayAt(c, Vector3.zero, vol, pitch, 0f);

        public void StopAllOneShots() { foreach (var p in pool) p.Stop(); }

        // ------------------------------------------------------------------ synthesis

        float N() => (float)(rng.NextDouble() * 2.0 - 1.0);

        AudioClip Make(string name, float seconds, Func<float, float> f, bool loopFade = false)
        {
            int n = Mathf.CeilToInt(seconds * SR);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = f(i / (float)SR);
            if (loopFade)
            {
                int fade = Mathf.Min(n / 4, SR / 5);
                for (int i = 0; i < fade; i++)
                {
                    float a = i / (float)fade;
                    data[i] = data[i] * a + data[n - fade + i] * (1 - a);
                }
                Array.Resize(ref data, n - fade);
                n -= fade;
            }
            float peak = 0.0001f;
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            float g = 0.9f / peak;
            for (int i = 0; i < n; i++) data[i] *= g;
            var clip = AudioClip.Create(name, n, 1, SR, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Sin(float hz, float t, float ph = 0) => Mathf.Sin(2f * Mathf.PI * hz * t + ph);

        void Generate()
        {
            // Diesel idle/drive: low fundamental, firing pulse, brown noise.
            float brown = 0;
            engine = Make("engine", 3f, t =>
            {
                brown = Mathf.Clamp(brown + N() * 0.04f, -1, 1) * 0.995f;
                float pulse = 0.6f + 0.4f * Mathf.Pow(Mathf.Abs(Sin(6f, t)), 3);
                return (0.45f * Sin(36, t) + 0.25f * Sin(72, t, 0.4f) + 0.12f * Sin(108, t, 1.1f) + 0.5f * brown) * pulse;
            }, true);

            // Rain on the roof: filtered noise plus sparse drips.
            float lp = 0, lp2 = 0;
            rain = Make("rain", 4f, t =>
            {
                lp += (N() - lp) * 0.35f;
                lp2 += (lp - lp2) * 0.5f;
                float drip = rng.NextDouble() < 0.0009 ? N() * 3f : 0f;
                return lp2 * 0.6f + drip;
            }, true);

            hum = Make("hum", 2f, t => 0.5f * Sin(100, t) + 0.25f * Sin(200, t) + 0.15f * Sin(300, t) + 0.02f * N(), true);

            // Loose window rattle.
            float rv = 0;
            rattle = Make("rattle", 3f, t =>
            {
                if (rng.NextDouble() < 0.004 * (1 + Sin(0.66f, t))) rv = 1f;
                rv *= 0.985f;
                return rv * N() * 0.5f + rv * Sin(900, t) * 0.3f;
            }, true);

            // Sub drone for the last chapters.
            float dl = 0;
            drone = Make("drone", 4f, t =>
            {
                dl += (N() - dl) * 0.02f;
                return 0.5f * Sin(55, t) + 0.45f * Sin(55.5f, t) + 0.2f * Sin(82.5f, t) + dl * 0.8f;
            }, true);

            heartbeat = Make("heartbeat", 1.2f, t =>
            {
                float a = Mathf.Exp(-((t - 0.1f) * (t - 0.1f)) * 900f) + 0.7f * Mathf.Exp(-((t - 0.38f) * (t - 0.38f)) * 900f);
                return a * Sin(48, t);
            }, true);

            // Mechanical bell: bright inharmonic partials.
            bell = Make("bell", 1.6f, t =>
            {
                float s = Sin(1650, t) * Mathf.Exp(-t * 3.5f) + 0.6f * Sin(2490, t) * Mathf.Exp(-t * 5f)
                        + 0.4f * Sin(3960, t) * Mathf.Exp(-t * 7f) + 0.25f * Sin(5230, t) * Mathf.Exp(-t * 9f);
                float strike = t < 0.01f ? N() : 0;
                return s + strike;
            });

            // Dot-matrix ticket printer chatter.
            printer = Make("printer", 1.3f, t =>
            {
                if (t > 1.15f) return 0;
                float ph = (t * 26f) % 1f;
                float burst = ph < 0.35f ? Mathf.Exp(-ph * 30f) : 0;
                float feed = t > 0.95f ? 0.3f * N() * (1.15f - t) * 5f : 0;
                return burst * (0.7f * N() + 0.5f * Sin(1100, t)) + feed;
            });

            knock = Make("knock", 0.5f, t => Sin(78, t) * Mathf.Exp(-t * 16f) + 0.5f * N() * Mathf.Exp(-t * 70f) + 0.3f * Sin(160, t) * Mathf.Exp(-t * 25f));

            float dh = 0;
            door = Make("door", 1.6f, t =>
            {
                dh += (N() - dh) * 0.6f;
                float hiss = t < 1.1f ? dh * Mathf.Min(1, t * 20) * Mathf.Exp(-t * 2.2f) : 0;
                float clunk = t > 1.1f ? (Sin(90, t) + N() * 0.5f) * Mathf.Exp(-(t - 1.1f) * 25f) : 0;
                return hiss * 0.7f + clunk;
            });

            float bh = 0;
            brake = Make("brake", 1.4f, t =>
            {
                bh += (N() - bh) * 0.8f;
                return bh * Mathf.Min(1, t * 30) * Mathf.Exp(-t * 2.8f);
            });

            indicator = Make("indicator", 0.12f, t => (N() * 0.5f + Sin(2200, t)) * Mathf.Exp(-t * 90f));

            phone = Make("phone", 3f, t =>
            {
                bool on = (t < 0.45f) || (t > 0.65f && t < 1.1f);
                if (!on) return 0;
                float mod = Sin(20, t) > 0 ? 1 : 0.2f;
                return (Sin(440, t) + Sin(480, t)) * 0.5f * mod;
            });

            float wl = 0, wenv = 0, wtarget = 0;
            whisper = Make("whisper", 2.6f, t =>
            {
                if (rng.NextDouble() < 0.0006) wtarget = (float)rng.NextDouble();
                if (rng.NextDouble() < 0.0008) wtarget = 0;
                wenv += (wtarget - wenv) * 0.002f;
                float n = N();
                wl += (n - wl) * 0.5f;
                return (n - wl) * wenv * (t < 2.4f ? 1 : (2.6f - t) * 5);
            });

            step = Make("step", 0.25f, t => Sin(70, t) * Mathf.Exp(-t * 30f) + 0.4f * N() * Mathf.Exp(-t * 40f));

            float sl = 0;
            speaker = Make("speaker", 0.7f, t =>
            {
                sl += (N() - sl) * 0.4f;
                float crack = rng.NextDouble() < 0.02 ? N() : 0;
                return (sl * 0.6f + crack) * (t < 0.6f ? 1 : (0.7f - t) * 10) + Sin(1000, t) * Mathf.Exp(-t * 40) * 0.5f;
            });

            thump = Make("thump", 1.5f, t => Sin(42, t) * Mathf.Exp(-t * 3f) + 0.3f * N() * Mathf.Exp(-t * 12f));

            tear = Make("tear", 0.9f, t =>
            {
                float g = rng.NextDouble() < 0.25 ? N() : 0;
                return g * Mathf.Min(1, t * 8) * (t < 0.8f ? 1 : (0.9f - t) * 10);
            });

            clink = Make("clink", 0.5f, t => (Sin(3150, t) + 0.5f * Sin(4720, t)) * Mathf.Exp(-t * 9f));

            // Indistinct crowd murmur (passengers you cannot see).
            float m1 = 0, m2 = 0, menv = 0;
            // Horror stinger: a dissonant low cluster hit with a screeching overtone.
            stinger = Make("stinger", 2.2f, t =>
            {
                float env = Mathf.Min(1, t * 200) * Mathf.Exp(-t * 1.6f);
                float saw(float hz) => 2f * ((t * hz) % 1f) - 1f;
                float low = saw(55f) + saw(58.3f) + 0.7f * saw(77.8f) + 0.5f * saw(116.5f);
                float screech = Mathf.Sin(2f * Mathf.PI * (1750f * t + 40f * Mathf.Sin(2f * Mathf.PI * 7f * t))) * Mathf.Exp(-t * 2.5f) * 0.6f;
                float hit = N() * Mathf.Exp(-t * 18f);
                return (low * 0.35f + screech + hit) * env;
            });

            // Close, slow breathing (inhale / exhale).
            float bl = 0, bl2 = 0;
            breath = Make("breath", 3.2f, t =>
            {
                bl += (N() - bl) * 0.25f; bl2 += (bl - bl2) * 0.3f;
                float ph = (t / 3.2f) % 1f;
                float env = ph < 0.4f ? Mathf.Sin(ph / 0.4f * Mathf.PI) * 0.6f : Mathf.Sin((ph - 0.4f) / 0.6f * Mathf.PI);
                return (bl - bl2) * env * env;
            }, true);

            // Metal scraping along the bus.
            float sc = 0;
            scrape = Make("scrape", 1.6f, t =>
            {
                sc += (N() - sc) * 0.7f;
                float res = Mathf.Sin(2f * Mathf.PI * (820f + 120f * Mathf.Sin(t * 9f)) * t);
                return (sc * 0.5f + res * 0.4f) * Mathf.Min(1, t * 6) * Mathf.Exp(-t * 1.2f) * (0.6f + 0.4f * Mathf.PerlinNoise(t * 30f, 0));
            });

            // Door/panel slam.
            rattleHit = Make("slam", 0.9f, t => (Sin(65, t) * Mathf.Exp(-t * 8f) + N() * Mathf.Exp(-t * 25f) + 0.4f * Sin(310, t) * Mathf.Exp(-t * 14f)));

            voiceMurmur = Make("murmur", 4f, t =>
            {
                m1 += (N() - m1) * 0.08f;
                m2 += (m1 - m2) * 0.1f;
                menv = 0.5f + 0.5f * Sin(2.5f, t) * Sin(0.75f, t, 1f);
                return m2 * menv * (Sin(140, t) * 0.4f + 0.6f);
            }, true);
        }
    }
}
