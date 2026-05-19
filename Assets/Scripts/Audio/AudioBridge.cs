using UnityEngine;

namespace Chess
{
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

        // ---- Public read-only: mirrors the last values sent to SuperCollider.
        //      Read by GameManager to populate the UI metrics panel.
        public float  SmoothQ  { get; private set; } = 0.5f;
        public float  SmoothDQ { get; private set; } = 0f;
        public float  LastC    { get; private set; } = 0f;
        public string Harmony  { get; private set; } = "Neutral";
        public string Jingle   { get; private set; } = "neutral";

        void Awake() => OSCSender.Init(OSCHost, OSCPort);
        void OnDestroy() => OSCSender.Close();

        // ── Called after every AI move (includes MCTS win-rate + visit count) ──
        public void OnMovePlayed(float winRate, float delta, int visitCount)
        {
            float C = Mathf.Log(visitCount + 1);

            _smoothQ     = (1f - SmoothingAlpha) * _smoothQ + SmoothingAlpha * winRate;
            float dQ     = _smoothQ - _prevSmoothQ;
            _prevSmoothQ = _smoothQ;

            // Lowered threshold 0.05→0.03 so more moves get positive/negative jingles
            string harmony = _smoothQ > 0.6f ? "Major" : _smoothQ < 0.4f ? "Minor" : "Neutral";
            string jingle  = dQ >  0.03f ? "positive" : dQ < -0.03f ? "negative" : "neutral";

            Debug.Log($"[AudioBridge] AI  Q={_smoothQ:F3}  ΔQ={dQ:+0.000;-0.000}  C={C:F2}  [{harmony}] {jingle}");

            SendAll(_smoothQ, dQ, C, harmony, jingle);
        }

        // ── Called after every player move (uses last known Q; signals SC about
        //    the move so background and a marker jingle fire immediately) ──
        public void OnPlayerMove()
        {
            // Keep smoothQ unchanged (no new MCTS data yet); use board evaluation
            // direction encoded in the sign of _prevSmoothQ vs 0.5 as a hint.
            // Simply re-send current state so SC re-evaluates background register.
            float dQ     = 0f;          // player move: no Q change until AI responds
            float C      = LastC;       // carry over last confidence
            string harmony = Harmony;   // carry over last harmony
            string jingle  = "neutral"; // every player move → neutral marker

            Debug.Log($"[AudioBridge] PLY Q={_smoothQ:F3}  (player move marker)");

            SendAll(_smoothQ, dQ, C, harmony, jingle);
        }

        void SendAll(float q, float dQ, float c, string harmony, string jingle)
        {
            OSCSender.Send("/chess/winrate",    q);
            OSCSender.Send("/chess/delta",      dQ);
            OSCSender.Send("/chess/confidence", c);
            OSCSender.Send("/chess/harmony",    harmony);
            OSCSender.Send("/chess/jingle",     jingle);

            SmoothQ  = q;
            SmoothDQ = dQ;
            LastC    = c;
            Harmony  = harmony;
            Jingle   = jingle;
        }

        public static string GetHarmony(float q) =>
            q > 0.6f ? "Major" : q < 0.4f ? "Minor" : "Neutral";

        public static string GetJingle(float delta) =>
            delta > 0.05f ? "positive" : delta < -0.05f ? "negative" : "neutral";
    }
}
