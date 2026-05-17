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

        public void OnMovePlayed(float winRate, float delta, int visitCount)
        {
            float C = Mathf.Log(visitCount + 1);

            _smoothQ     = (1f - SmoothingAlpha) * _smoothQ + SmoothingAlpha * winRate;
            float dQ     = _smoothQ - _prevSmoothQ;
            _prevSmoothQ = _smoothQ;

            string harmony = _smoothQ > 0.6f ? "Major" : _smoothQ < 0.4f ? "Minor" : "Neutral";
            string jingle  = dQ >  0.05f ? "positive" : dQ < -0.05f ? "negative" : "neutral";

            Debug.Log($"[AudioBridge] Q={_smoothQ:F3}  ΔQ={dQ:+0.000;-0.000}  C={C:F2}  [{harmony}] {jingle}");

            OSCSender.Send("/chess/winrate",    _smoothQ);
            OSCSender.Send("/chess/delta",      dQ);
            OSCSender.Send("/chess/confidence", C);
            OSCSender.Send("/chess/harmony",    harmony);
            OSCSender.Send("/chess/jingle",     jingle);

            // Expose to UI panel (read by GameManager.RefreshMetricsPanel)
            SmoothQ  = _smoothQ;
            SmoothDQ = dQ;
            LastC    = C;
            Harmony  = harmony;
            Jingle   = jingle;
        }

        public static string GetHarmony(float q) =>
            q > 0.6f ? "Major" : q < 0.4f ? "Minor" : "Neutral";

        public static string GetJingle(float delta) =>
            delta > 0.05f ? "positive" : delta < -0.05f ? "negative" : "neutral";
    }
}
