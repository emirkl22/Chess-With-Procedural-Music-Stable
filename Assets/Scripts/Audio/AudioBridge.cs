using UnityEngine;

namespace Chess
{
    // -----------------------------------------------------------------------
    // AudioBridge — sends OSC parameters to SuperCollider (port 57120) every
    // half-move. The SC side runs a continuous drum-beat background whose
    // tempo / pattern / bass note track the game state, plus melodic jingles
    // triggered on every move.
    //
    // OSC parameters sent:
    //   /chess/winrate    float    smoothed Q (EMA of MCTS win-rate)
    //   /chess/confidence float    log(N+1)
    //   /chess/harmony    string   Major / Neutral / Minor
    //   /chess/material   float    material differential, pawn units
    //                              (+ = white ahead, − = black ahead)
    //   /chess/intensity  float    jingle magnitude [0,1]
    //   /chess/direction  float    +1 / 0 / -1 (positive / neutral / negative)
    //   /chess/capture    float    captured piece value (0=none, 9=queen)
    //   /chess/jingle     string   "fire" — trigger; SC reads above state
    // -----------------------------------------------------------------------
    public class AudioBridge : MonoBehaviour
    {
        [Header("OSC target")]
        public string OSCHost = "127.0.0.1";
        public int    OSCPort = 57120;

        [Header("Smoothing")]
        [Range(0.05f, 1f)]
        public float SmoothingAlpha = 0.3f;

        float _smoothQ     = 0.5f;
        float _prevSmoothQ = 0.5f;

        // Mirror of the last values sent — read by GameManager.RefreshMetricsPanel
        public float  SmoothQ  { get; private set; } = 0.5f;
        public float  SmoothDQ { get; private set; } = 0f;
        public float  LastC    { get; private set; } = 0f;
        public string Harmony  { get; private set; } = "Neutral";
        public string Jingle   { get; private set; } = "neutral";

        void Awake()    => OSCSender.Init(OSCHost, OSCPort);
        void OnDestroy() => OSCSender.Close();

        // -------------------------------------------------------------------
        // Called after every AI move — full MCTS data available.
        // -------------------------------------------------------------------
        public void OnMovePlayed(float winRate, float delta, int visitCount,
                                 int materialCp, int captureValue)
        {
            float C  = Mathf.Log(visitCount + 1);

            _smoothQ     = (1f - SmoothingAlpha) * _smoothQ + SmoothingAlpha * winRate;
            float dQ     = _smoothQ - _prevSmoothQ;
            _prevSmoothQ = _smoothQ;

            string harmony = _smoothQ > 0.6f ? "Major"
                           : _smoothQ < 0.4f ? "Minor"
                           : "Neutral";

            // Map |dQ| → intensity [0,1].  dQ = 0.125 already saturates.
            float intensity = Mathf.Clamp01(Mathf.Abs(dQ) * 8f);
            float direction = dQ >  0.02f ?  1f
                            : dQ < -0.02f ? -1f
                            :                0f;

            // Big captures override / boost the jingle intensity even if MCTS
            // win-rate barely shifted (e.g. capturing a hanging queen).
            // Standard piece values: P=1, N/B=3, R=5, Q=10
            if (captureValue >= 3)  intensity = Mathf.Max(intensity, 0.45f);
            if (captureValue >= 5)  intensity = Mathf.Max(intensity, 0.70f);
            if (captureValue >= 10) intensity = 1.0f;

            // Capture forces positive/negative direction if MCTS didn't decide.
            if (direction == 0f && captureValue > 0) direction = +1f;

            float material = materialCp / 100f;

            Debug.Log($"[AudioBridge] AI  Q={_smoothQ:F3}  dQ={dQ:+0.000;-0.000}  " +
                      $"int={intensity:F2} dir={direction:+0;-0} cap={captureValue}  " +
                      $"mat={material:+0.0;-0.0}");

            string jingleLabel = direction > 0.5f ? "positive"
                               : direction < -0.5f ? "negative"
                               : "neutral";

            SendAllAndFire(_smoothQ, dQ, C, harmony, material,
                           intensity, direction, captureValue, jingleLabel);
        }

        // -------------------------------------------------------------------
        // Called after every player move — no MCTS data, just board state +
        // any captured piece.  Triggers a jingle whose intensity comes purely
        // from the capture value.
        // -------------------------------------------------------------------
        public void OnPlayerMove(int materialCp, int captureValue)
        {
            float intensity, direction;
            if (captureValue > 0)
            {
                intensity = Mathf.Clamp01(captureValue / 10f * 1.1f);
                if (captureValue >= 5)  intensity = Mathf.Max(intensity, 0.75f);
                if (captureValue >= 10) intensity = 1.0f;
                direction = +1f;   // a capture is good for the player
            }
            else
            {
                intensity = 0.22f;   // small marker so player hears feedback
                direction = 0f;
            }

            float material = materialCp / 100f;

            Debug.Log($"[AudioBridge] PLY mat={material:+0.0;-0.0} cap={captureValue} " +
                      $"int={intensity:F2}");

            string jingleLabel = direction > 0.5f ? "positive" : "neutral";

            SendAllAndFire(_smoothQ, 0f, LastC, Harmony, material,
                           intensity, direction, captureValue, jingleLabel);
        }

        // -------------------------------------------------------------------
        // Order matters: state messages first, then the jingle trigger so SC
        // already has the new intensity/direction/capture when /chess/jingle
        // handler runs.
        // -------------------------------------------------------------------
        void SendAllAndFire(float q, float dQ, float c, string harmony,
                            float material, float intensity, float direction,
                            int captureValue, string jingleLabel)
        {
            OSCSender.Send("/chess/winrate",    q);
            OSCSender.Send("/chess/confidence", c);
            OSCSender.Send("/chess/harmony",    harmony);
            OSCSender.Send("/chess/material",   material);

            OSCSender.Send("/chess/intensity",  intensity);
            OSCSender.Send("/chess/direction",  direction);
            OSCSender.Send("/chess/capture",    (float)captureValue);
            OSCSender.Send("/chess/jingle",     "fire");

            SmoothQ  = q;
            SmoothDQ = dQ;
            LastC    = c;
            Harmony  = harmony;
            Jingle   = jingleLabel;
        }

        // -------------------------------------------------------------------
        // Called during history navigation — updates SC's bass-note pitch
        // (via /chess/material) without firing a jingle.
        // -------------------------------------------------------------------
        public void SendMaterialOnly(int materialCp)
        {
            float material = materialCp / 100f;
            OSCSender.Send("/chess/material", material);
        }

        // ── Legacy helpers retained for UI/metrics code ────────────────────
        public static string GetHarmony(float q) =>
            q > 0.6f ? "Major" : q < 0.4f ? "Minor" : "Neutral";

        public static string GetJingle(float delta) =>
            delta > 0.02f ? "positive" : delta < -0.02f ? "negative" : "neutral";
    }
}
