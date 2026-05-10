using System;
using UnityEngine;

namespace Chess
{
    // -----------------------------------------------------------------------
    // AudioBridge
    //
    // Computes the three audio parameters from Progress Report 2 after each move:
    //
    //   Q  = win rate (W/N from MCTS root)           range [0, 1]
    //   ΔQ = Q_after – Q_before                      range [-1, 1]
    //   C  = log(N+1) — confidence / clarity         range [0, ~5.3 for N=200]
    //
    // Harmony classification:
    //   Q > 0.6  → Major
    //   Q < 0.4  → Minor
    //   else     → Neutral
    //
    // Jingle classification:
    //   ΔQ > +0.05  → positive (major flourish)
    //   ΔQ < -0.05  → negative (minor / dissonant)
    //   else        → neutral
    //
    // OSC address map (what SuperCollider will receive):
    //   /chess/winrate    f  Q
    //   /chess/delta      f  ΔQ
    //   /chess/confidence f  C
    //   /chess/harmony    s  "Major"|"Neutral"|"Minor"
    //   /chess/jingle     s  "positive"|"neutral"|"negative"
    //
    // TO INTEGRATE SuperCollider:
    //   Replace every call to OSCStub.Send(addr, val) below with your actual
    //   OSC sender (e.g., extOSC, UnityOSC, or SharpOSC).
    //   The addresses and value types are already defined here.
    // -----------------------------------------------------------------------
    public class AudioBridge : MonoBehaviour
    {
        [Header("OSC target (fill in when SC is ready)")]
        [Tooltip("IP address of the machine running SuperCollider")]
        public string OSCHost = "127.0.0.1";

        [Tooltip("Port SuperCollider is listening on")]
        public int OSCPort = 57120;

        float _prevQ = 0.5f;

        // Called by GameManager after every AI move
        public void OnMovePlayed(float winRate, float delta, int visitCount)
        {
            float Q = winRate;
            float dQ = delta;
            float C = Mathf.Log(visitCount + 1);

            string harmony = Q > 0.6f ? "Major" : Q < 0.4f ? "Minor" : "Neutral";
            string jingle  = dQ > 0.05f ? "positive" : dQ < -0.05f ? "negative" : "neutral";

            _prevQ = Q;

            // --- Log what would be sent via OSC ---
            Debug.Log($"[AudioBridge] OSC → /chess/winrate    {Q:F4}");
            Debug.Log($"[AudioBridge] OSC → /chess/delta      {dQ:+0.0000;-0.0000}");
            Debug.Log($"[AudioBridge] OSC → /chess/confidence {C:F4}");
            Debug.Log($"[AudioBridge] OSC → /chess/harmony    \"{harmony}\"");
            Debug.Log($"[AudioBridge] OSC → /chess/jingle     \"{jingle}\"");

            // --- OSC send stubs ---
            // Uncomment and replace with real OSC sender when SuperCollider is integrated:
            //
            // OSCSender.Send(OSCHost, OSCPort, "/chess/winrate",    Q);
            // OSCSender.Send(OSCHost, OSCPort, "/chess/delta",      dQ);
            // OSCSender.Send(OSCHost, OSCPort, "/chess/confidence", C);
            // OSCSender.Send(OSCHost, OSCPort, "/chess/harmony",    harmony);
            // OSCSender.Send(OSCHost, OSCPort, "/chess/jingle",     jingle);
        }

        // Harmony from win rate (for background music layer)
        public static string GetHarmony(float q) =>
            q > 0.6f ? "Major" : q < 0.4f ? "Minor" : "Neutral";

        // Jingle type from delta (for event-triggered jingle layer)
        public static string GetJingle(float delta) =>
            delta > 0.05f ? "positive" : delta < -0.05f ? "negative" : "neutral";
    }
}
