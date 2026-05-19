// =============================================================================
// Chess With Procedural Music — SuperCollider Engine  v3  (CHAOS EDITION)
// =============================================================================
// USAGE:
//   1. Select ALL (Ctrl+A) then evaluate (Ctrl+Enter) to boot and start.
//   2. Keep this window open while playing chess in Unity.
//
// ARCHITECTURE OVERVIEW
//   Background  : voice-pool system — 8 SynthDef types, 1-3 active at once.
//                 Active voices are replaced (not just morphed) on harmony change
//                 and periodically mutated every 8-20 s for continuous variety.
//   Melody      : stochastic Task — irregular rhythm, rotating timbres,
//                 density and register driven by Q and C.
//   Feedback    : jingle pool — 4 patterns per type, never repeats consecutively.
//   All variation is seeded by game state so it ALWAYS reacts to chess events.
// =============================================================================

(
s.options.numOutputBusChannels = 2;
s.options.memSize = 131072;

s.waitForBoot {

// ============================================================================
// 1. GLOBAL STATE
// ============================================================================
~state = (
    winrate:    0.5,
    delta:      0.0,
    confidence: 3.0,
    harmony:    \Neutral,
    jingle:     \neutral
);

~scales = (
    \Major:   Scale.lydian,
    \Neutral: Scale.dorian,
    \Minor:   Scale.locrian
);

~roots = ( \Major: 62, \Neutral: 60, \Minor: 57 );  // Minor is lower = darker

~chords = (
    \Major:   [0, 4, 7, 11, 14, 19],
    \Neutral: [0, 3, 7, 10, 14, 17],
    \Minor:   [0, 3, 6, 10, 13, 15]
);

// Per-slot amplitude targets (slot 0 = loudest, slot 2 = quietest)
~slotAmps  = [0.15, 0.10, 0.07];
~voices    = Array.newClear(3);   // active background Synths
~voiceKeys = Array.newClear(3);   // which SynthDef is in each slot

// Last jingle index per type — for anti-repetition
~lastJingleIdx = IdentityDictionary[\positive -> -1, \negative -> -1, \neutral -> -1];

// ============================================================================
// 2. BACKGROUND SYNTHDEFS  (8 types — radically different timbres)
// ============================================================================

// ── Type 0: ADDITIVE DRONE ──────────────────────────────────────────────────
// Low-Q = inharmonic / beating partials. High-Q = clean harmonics.
SynthDef(\bgAdditive, { |out=0, freq=60, amp=0.15, q=0.5, c=3, gate=1|
    var sig, env;
    var inharm = q.linlin(0, 1, 0.28, 0.0);
    env = EnvGen.kr(Env.asr(5, 1, 6), gate, doneAction: 2);
    sig = Mix.fill(7, { |i|
        var ratio = (i + 1) * (1 + (inharm * LFNoise2.kr(0.08 + (i*0.017)).range(-1,1)));
        SinOsc.ar(freq * ratio, 0, (1.0 / (i+1)).pow(1.15))
    });
    sig = LeakDC.ar(sig * env * amp * 0.6);
    Out.ar(out, Pan2.ar(sig, LFNoise1.kr(0.04).range(-0.2, 0.2)));
}).add;

// ── Type 1: FM CHAOS DRONE ──────────────────────────────────────────────────
// Low-Q = wildly high modulation index → metallic/noisy.
// High-Q = gentle FM → warm bell-like tone.
SynthDef(\bgFMDrone, { |out=0, freq=60, amp=0.13, q=0.5, c=3, gate=1|
    var car, mod, sig, env;
    var modRatio = q.linlin(0,1, 0.33, 2.5) + LFNoise1.kr(0.12).range(-0.15, 0.15);
    var modIdx   = q.linlin(0,1, 22.0, 1.2) * (1 + LFNoise2.kr(0.2).range(-0.3, 0.3));
    env = EnvGen.kr(Env.asr(6, 1, 7), gate, doneAction: 2);
    mod = SinOsc.ar(freq * modRatio, 0, freq * modIdx.abs);
    car = SinOsc.ar(freq + mod) * env * amp;
    sig = LeakDC.ar(HPF.ar(LPF.ar(car, 9000), 25));
    Out.ar(out, Pan2.ar(sig, LFNoise1.kr(0.06).range(-0.35, 0.35)));
}).add;

// ── Type 2: FILTERED NOISE BED ──────────────────────────────────────────────
// High-Q = high, bright. Low-Q = low, dark, smeared.
// C controls bandwidth: low C = wide smear, high C = focused resonance.
SynthDef(\bgNoise, { |out=0, freq=200, amp=0.10, q=0.5, c=3, gate=1|
    var sig, env;
    var cf  = freq * q.linexp(0, 1, 0.35, 5.0);
    var rq  = c.linlin(0, 8, 0.85, 0.08);
    env = EnvGen.kr(Env.asr(3.5, 1, 4.5), gate, doneAction: 2);
    sig = BPF.ar(GrayNoise.ar(1), cf, rq) * (1/rq).sqrt * 0.35;
    sig = sig + (BPF.ar(GrayNoise.ar(0.6), cf * 1.77, rq * 0.7) * 0.2);
    sig = FreeVerb.ar(sig * env * amp, 0.70, 0.88, 0.3);
    Out.ar(out, Pan2.ar(sig, LFNoise1.kr(0.09).range(-0.55, 0.55)));
}).add;

// ── Type 3: COMB RESONATOR ──────────────────────────────────────────────────
// Metallic ringing. Low-Q = fast decay = percussive metallic pings.
// High-Q = long resonance = harmonic steel.
SynthDef(\bgComb, { |out=0, freq=80, amp=0.09, q=0.5, c=3, gate=1|
    var sig, env, exc;
    var decay = c.linlin(0, 8, 0.4, 5.0) + (q * 2.5);
    env = EnvGen.kr(Env.asr(7, 1, 8), gate, doneAction: 2);
    exc = LFNoise2.ar(80) * 0.18;
    sig = Mix.fill(4, { |i|
        var f = freq * (i+1) * (1 + (LFNoise1.kr(0.06 + i*0.011).range(-0.018, 0.018)));
        CombC.ar(exc, 1.0/20, f.reciprocal.clip(1e-4, 0.05), decay * (1 - (i*0.18)))
    });
    sig = FreeVerb.ar(sig * env * amp, 0.75, 0.92);
    Out.ar(out, Pan2.ar(sig, 0));
}).add;

// ── Type 4: RING MODULATION ──────────────────────────────────────────────────
// Carrier × modulator = sum and difference sidebands.
// Low-Q = wide irrational ratio = atonal alien texture.
// High-Q = simple ratio = transparent shimmer.
SynthDef(\bgRingMod, { |out=0, freq=55, amp=0.11, q=0.5, c=3, gate=1|
    var car, modOsc, sig, env;
    var ratio = q.linexp(0,1, 0.18, 3.1) + LFNoise1.kr(0.08).range(-0.04, 0.04);
    env = EnvGen.kr(Env.asr(5.5, 1, 6.5), gate, doneAction: 2);
    car    = LeakDC.ar(Saw.ar(freq, 0.35) + SinOsc.ar(freq * 2.01, 0, 0.25));
    modOsc = SinOsc.ar(freq * ratio);
    sig = RLPF.ar(car * modOsc, (freq * ratio * 4.5).clip(150, 7000), 0.48);
    sig = FreeVerb.ar(sig * env * amp, 0.52, 0.83);
    Out.ar(out, Pan2.ar(sig, LFNoise1.kr(0.07).range(-0.4, 0.4)));
}).add;

// ── Type 5: PULSE / PWM ──────────────────────────────────────────────────────
// Low-Q = very narrow pulse = aggressive razor buzz.
// High-Q = 50% duty cycle = hollow, open.
SynthDef(\bgPulse, { |out=0, freq=60, amp=0.10, q=0.5, c=3, gate=1|
    var sig, env;
    var width  = q.linlin(0, 1, 0.04, 0.50);
    var cutoff = c.linlin(0, 8, 250, 3500) * (q + 0.25);
    env = EnvGen.kr(Env.asr(4, 1, 5.5), gate, doneAction: 2);
    sig = Pulse.ar([freq, freq * 2.003], width, 0.28).sum;
    sig = RLPF.ar(sig, cutoff.clip(100, 8000), 0.38);
    sig = (sig * 1.6).tanh * 0.65;  // soft-clip saturation
    sig = FreeVerb.ar(sig * env * amp, 0.58, 0.82);
    Out.ar(out, Pan2.ar(sig, LFNoise1.kr(0.10).range(-0.38, 0.38)));
}).add;

// ── Type 6: WAVEFOLD DISTORTION ─────────────────────────────────────────────
// Low-Q = extreme fold = complex inharmonic spectrum.
// High-Q = mild fold = warm, slightly distorted sine.
SynthDef(\bgWarp, { |out=0, freq=65, amp=0.09, q=0.5, c=3, gate=1|
    var sig, env, raw;
    var foldAmt = q.linlin(0, 1, 6.5, 0.4);
    env = EnvGen.kr(Env.asr(4.5, 1, 5.5), gate, doneAction: 2);
    raw = SinOsc.ar(freq)
        + (SinOsc.ar(freq * 2.997, 0, 0.45))
        + (SinOsc.ar(freq * 5.013, 0, 0.22));
    sig = (raw * foldAmt).fold(-1, 1);      // wavefold
    sig = LPF.ar(sig, c.linlin(0, 8, 400, 6000).clip(200, 8000));
    sig = FreeVerb.ar(sig * env * amp * 0.55, 0.75, 0.90);
    Out.ar(out, Pan2.ar(sig, LFNoise1.kr(0.05).range(-0.3, 0.3)));
}).add;

// ── Type 7: DETUNED CLUSTER ──────────────────────────────────────────────────
// Low-Q = wide spread = dense dissonant cloud.
// High-Q = tight cluster = near-unison shimmer.
SynthDef(\bgCluster, { |out=0, freq=60, amp=0.08, q=0.5, c=3, gate=1|
    var sig, env;
    var spread = q.linlin(0, 1, 0.28, 0.025);
    env = EnvGen.kr(Env.asr(7, 1, 9), gate, doneAction: 2);
    sig = Mix.fill(9, { |i|
        var detune = LFNoise2.kr(0.04 + (i * 0.014)).range(spread.neg, spread);
        SinOsc.ar(freq * (1 + detune), 0, 1.0 / 9)
    });
    sig = FreeVerb.ar(sig * env * amp, 0.82, 0.94);
    Out.ar(out, Pan2.ar(sig, LFNoise1.kr(0.035).range(-0.5, 0.5)));
}).add;

// ============================================================================
// 3. MELODY SYNTHDEFS  (5 types — rotated randomly)
// ============================================================================

SynthDef(\mPluck, { |out=0, freq=440, amp=0.14, pan=0|
    var sig, env;
    env = EnvGen.kr(Env.perc(0.001, 2.2, 1, -4), doneAction: 2);
    sig = Pluck.ar(WhiteNoise.ar(0.3), Impulse.ar(0), 0.05, freq.reciprocal, 2.2, 0.25);
    sig = FreeVerb.ar(sig * env * amp, 0.28, 0.72);
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

SynthDef(\mBell, { |out=0, freq=440, amp=0.12, pan=0|
    var sig, env;
    env = EnvGen.kr(Env.perc(0.001, 4.0, 1, -5), doneAction: 2);
    sig = Klank.ar(
        `[ [1.0, 2.83, 5.1, 7.4], nil, [2.5, 1.8, 1.0, 0.5] ],
        Impulse.ar(0) * amp) * env;
    sig = FreeVerb.ar(sig, 0.42, 0.82);
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

SynthDef(\mFM, { |out=0, freq=440, amp=0.11, pan=0|
    var mod, car, sig, env;
    env = EnvGen.kr(Env.perc(0.003, 1.8, 1, -3), doneAction: 2);
    mod = SinOsc.ar(freq * 2.0, 0, freq * 3.5 * env);
    car = SinOsc.ar(freq + mod) * env * amp;
    sig = FreeVerb.ar(car, 0.32, 0.76);
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

SynthDef(\mBow, { |out=0, freq=220, amp=0.10, pan=0|
    var sig, env;
    env = EnvGen.kr(Env.new([0,0,1,0.8,0], [0.06, 0.12, 0.55, 0.35]), doneAction: 2);
    sig = LeakDC.ar(
        (SinOsc.ar(freq, 0, 0.6) + SinOsc.ar(freq*2, 0, 0.28) + SinOsc.ar(freq*3.01, 0, 0.14))
        * LFNoise2.kr(12).range(0.88, 1.0)  // bow jitter
    );
    sig = FreeVerb.ar(sig * env * amp, 0.52, 0.86);
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

SynthDef(\mClick, { |out=0, freq=440, amp=0.16, pan=0|
    var sig, click, env;
    env   = EnvGen.kr(Env.perc(0.001, 0.45, 1, -8), doneAction: 2);
    click = HPF.ar(WhiteNoise.ar(1.0), freq) * EnvGen.kr(Env.perc(0.001, 0.025));
    sig   = (SinOsc.ar(freq, 0, 0.5) * env + click) * amp;
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

// ============================================================================
// 4. JINGLE SYNTHDEFS  (building blocks for pool patterns)
// ============================================================================

// -- Positive pool synths -------------------------------------------------------
SynthDef(\jFMBurst, { |out=0, freq=440, amp=0.52, pan=0, rise=0.002, fall=2.0, idx=4|
    var mod, car, sig, env;
    env = EnvGen.kr(Env.perc(rise, fall, 1, -3), doneAction: 2);
    mod = SinOsc.ar(freq * 2.1, 0, freq * idx * env);
    car = SinOsc.ar(freq + mod) * env * amp;
    sig = FreeVerb.ar(car, 0.58, 0.90);
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

SynthDef(\jKarplusArp, { |out=0, freq=440, amp=0.50, pan=0|
    var sig, env;
    env = EnvGen.kr(Env.perc(0.001, 3.8, 1, -3), doneAction: 2);
    sig = Pluck.ar(WhiteNoise.ar(1.0), Impulse.ar(0), 0.05, freq.reciprocal, 4.0, 0.25);
    sig = HPF.ar(FreeVerb.ar(sig * env * amp, 0.58, 0.90), 80);
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

SynthDef(\jSineSpray, { |out=0, freq=880, amp=0.32, spread=0.05|
    var sig, env;
    env = EnvGen.kr(Env.perc(0.008, 3.2, 1, -5), doneAction: 2);
    sig = Mix.fill(12, { |i|
        SinOsc.ar(freq * (1 + ((i - 5.5) * spread * 0.18)
            + LFNoise2.kr(0.5).range(-0.008, 0.008)), 0, 1.0/12)
    });
    sig = FreeVerb.ar(sig * env * amp, 0.80, 0.96);
    Out.ar(out, sig ! 2);
}).add;

SynthDef(\jBellChord, { |out=0, freq=440, amp=0.50, pan=0|
    var sig, env;
    env = EnvGen.kr(Env.perc(0.001, 4.2, 1, -5), doneAction: 2);
    sig = Klank.ar(
        `[ [1.0, 1.19, 1.50, 1.78, 2.0, 2.51, 3.01],
           nil,
           [2.5, 2.2, 2.0, 1.6, 1.3, 0.9, 0.6] ],
        Impulse.ar(0) * amp) * env;
    sig = FreeVerb.ar(sig, 0.62, 0.92);
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

SynthDef(\jRhythmBurst, { |out=0, freq=440, amp=0.45, pan=0|
    var sig, gate, env;
    // Noise gated at rhythmic rate → pulsed energy burst
    gate = LFPulse.ar(8 + (freq * 0.005), 0, 0.35);
    env  = EnvGen.kr(Env.perc(0.01, 2.0, 1, -3), doneAction: 2);
    sig  = BPF.ar(WhiteNoise.ar(1.0), freq, 0.4) * gate * env * amp;
    sig  = FreeVerb.ar(sig, 0.5, 0.85);
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

// -- Negative pool synths -------------------------------------------------------
SynthDef(\jThudDeep, { |out=0, freq=45, amp=0.74, pan=0|
    var mod, car, sig, env;
    env = EnvGen.kr(Env.perc(0.001, 2.2, 1, -4), doneAction: 2);
    mod = SinOsc.ar(freq * 0.5, 0, freq * 16 * env);
    car = SinOsc.ar(freq + mod) * env;
    sig = LPF.ar(car + (WhiteNoise.ar(0.22) * EnvGen.kr(Env.perc(0.001, 0.09))), 380);
    Out.ar(out, Pan2.ar(sig * amp, pan));
}).add;

SynthDef(\jFMScream, { |out=0, freq=280, amp=0.58, pan=0|
    var mod, car, sig, env;
    env = EnvGen.kr(Env.perc(0.006, 2.8, 1, -2.2), doneAction: 2);
    mod = SinOsc.ar(freq * XLine.kr(0.45, 0.08, 2.8), 0, freq * 14.0 * env);
    car = SinOsc.ar(freq * XLine.kr(1.6, 0.22, 2.8) + mod) * env * amp;
    sig = (car * 3.8).tanh;
    sig = RLPF.ar(sig, (2200 * env + 80).clip(60, 5000), 0.4);
    sig = FreeVerb.ar(sig, 0.65, 0.88);
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

SynthDef(\jClusterSmash, { |out=0, freq=110, amp=0.55|
    var sig, env;
    env = EnvGen.kr(Env.perc(0.004, 2.4, 1, -2.4), doneAction: 2);
    sig = Mix.fill(8, { |i|
        var f = freq * (1 + ((i - 3.5) * 0.12) + LFNoise0.kr(0.15).range(-0.1, 0.1));
        Pan2.ar(Saw.ar(f, 0.18), i.linlin(0, 7, -1.0, 1.0))
    });
    sig = RLPF.ar(sig.sum, (1800 * env + 70).clip(50, 4000), 0.45);
    sig = (sig * 2.2).tanh * 0.6;
    sig = FreeVerb.ar(sig * env * amp, 0.68, 0.89);
    Out.ar(out, sig ! 2);
}).add;

SynthDef(\jBassImpact, { |out=0, freq=50, amp=0.72|
    var sig, click, env, subEnv;
    env    = EnvGen.kr(Env.perc(0.001, 2.0, 1, -5), doneAction: 2);
    subEnv = EnvGen.kr(Env.perc(0.001, 0.55, 1, -8));
    click  = HPF.ar(WhiteNoise.ar(0.85), 180) * subEnv;
    sig    = SinOsc.ar(freq * XLine.kr(2.2, 1.0, 0.09)) * env;
    sig    = LPF.ar((sig + click) * amp, 520);
    Out.ar(out, sig ! 2);
}).add;

// -- Neutral pool synths --------------------------------------------------------
SynthDef(\jKlank3, { |out=0, freq=500, amp=0.52, pan=0|
    var sig, env;
    env = EnvGen.kr(Env.perc(0.001, 3.8, 1, -5), doneAction: 2);
    sig = Klank.ar(
        `[ [0.5, 1.0, 1.19, 1.56, 2.0], nil, [2.2, 2.0, 1.6, 1.2, 0.8] ],
        Impulse.ar(0) * amp) * env;
    sig = FreeVerb.ar(sig, 0.52, 0.84);
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

SynthDef(\jBlip, { |out=0, freq=440, amp=0.38, pan=0|
    var sig, env;
    env = EnvGen.kr(Env.perc(0.001, 0.65, 1, -6), doneAction: 2);
    sig = SinOsc.ar(freq) * env * amp;
    sig = FreeVerb.ar(sig, 0.32, 0.72);
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

SynthDef(\jChime, { |out=0, freq=660, amp=0.40, pan=0|
    var mod, car, sig, env;
    env = EnvGen.kr(Env.perc(0.001, 2.8, 1, -5), doneAction: 2);
    mod = SinOsc.ar(freq * 1.5, 0, freq * 2.2 * env);
    car = SinOsc.ar(freq + mod) * env * amp;
    sig = FreeVerb.ar(car, 0.58, 0.90);
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

s.sync;

// ============================================================================
// 5. VOICE MANAGER
// ============================================================================

~bgPool = [\bgAdditive, \bgFMDrone, \bgNoise, \bgComb,
           \bgRingMod,  \bgPulse,   \bgWarp,  \bgCluster];

~harshPool  = [\bgFMDrone, \bgCluster, \bgWarp, \bgPulse, \bgNoise];
~smoothPool = [\bgAdditive, \bgComb, \bgRingMod, \bgNoise];

~slotAmps  = [0.18, 0.12, 0.08];
~voices    = Array.newClear(3);
~voiceKeys = Array.newClear(3);

~lastJingleIdx = IdentityDictionary[\positive -> -1, \negative -> -1, \neutral -> -1];

// Track Q at last respawn — used to decide when to do a full respawn
~lastRespawnQ = 0.5;

~voiceCount = { |c| (c / 2.6).ceil.clip(1, 3).asInteger };

~spawnVoice = { |slot, defName|
    var q, c, harm, root, freq;
    ~voices[slot] !? { |s| s.set(\gate, 0) };
    q    = ~state[\winrate].asFloat;
    c    = ~state[\confidence].asFloat;
    harm = ~state[\harmony];
    root = ~roots[harm] ?? 60;
    // ±18 semitones = 1.5-octave range; losing → bass register, winning → bright
    freq = (root + q.linlin(0, 1, -18, 12)).midicps;
    ~voices[slot]    = Synth(defName, [\freq, freq, \q, q, \c, c, \amp, ~slotAmps[slot]]);
    ~voiceKeys[slot] = defName;
    ("+ [" ++ slot ++ "] " ++ defName ++ "  freq=" ++ freq.round(0.1) ++ " Hz").postln;
};

~killSlot = { |slot|
    ~voices[slot] !? { |s| s.set(\gate, 0); ~voices[slot] = nil; ~voiceKeys[slot] = nil };
};

~morphVoices = {
    var q    = ~state[\winrate].asFloat;
    var c    = ~state[\confidence].asFloat;
    var harm = ~state[\harmony];
    var root = ~roots[harm] ?? 60;
    var freq = (root + q.linlin(0, 1, -18, 12)).midicps;
    ~voices.doWithIndex { |v, i|
        v !? { v.set(\freq, freq, \q, q, \c, c, \amp, ~slotAmps[i]) };
    };
};

~respawnAll = {
    var q    = ~state[\winrate].asFloat;
    var c    = ~state[\confidence].asFloat;
    var n    = ~voiceCount.value(c);
    var pool;

    if(q < 0.38, {
        pool = (~harshPool ++ ~bgPool.reject{ |d| ~harshPool.includes(d) }).scramble;
    }, {
        if(q > 0.62, {
            pool = (~smoothPool ++ ~bgPool.reject{ |d| ~smoothPool.includes(d) }).scramble;
        }, {
            pool = ~bgPool.copy.scramble;
        });
    });

    (n..2).do { |i| ~killSlot.value(i) };
    n.do { |i| ~spawnVoice.value(i, pool[i]) };

    ~lastRespawnQ = q;
    ("RESPAWN " ++ n ++ " voices | Q=" ++ q.round(0.01)
     ++ " harm=" ++ ~state[\harmony]).postln;
};

~mutateOneVoice = {
    var active = (0..2).select { |i| ~voices[i].notNil };
    if(active.size > 0, {
        var slot    = active.choose;
        var current = ~voiceKeys[slot];
        var newDef  = ~bgPool.reject{ |d| d == current }.choose;
        ~spawnVoice.value(slot, newDef);
        ("~ mutate [" ++ slot ++ "] " ++ current ++ " -> " ++ newDef).postln;
    });
};

// ============================================================================
// 6. MELODY ENGINE
// ============================================================================
~melodyDefs   = [\mPluck, \mBell, \mFM, \mBow, \mClick];
~melodyDefIdx = 0;
~melodyTask   = nil;

~startMelody = {
    ~melodyTask.stop;
    ~melodyTask = Task({
        loop {
            var q     = ~state[\winrate].asFloat;
            var c     = ~state[\confidence].asFloat;
            var harm  = ~state[\harmony];
            var scale = ~scales[harm] ?? Scale.dorian;
            var root  = ~roots[harm] ?? 60;
            var degs  = scale.degrees;

            var tempo = (c / 5.2).clip(0.25, 1.9) * (1.0 + ((q - 0.5).abs * 0.9));
            var dur   = (1.0 / tempo).clip(0.10, 1.3);
            var restP = c.linlin(0, 8, 0.50, 0.07);

            if(restP.coin, {
                dur.wait;
            }, {
                var burst  = (c > 4.8) and: { 0.15.coin };
                var nNotes = if(burst, { rrand(2, 3) }, { 1 });

                nNotes.do {
                    var octShift = if(q > 0.62,
                        { [0, 12].wchoose([0.28, 0.72]) },
                        { if(q < 0.38,
                            { [0, -12].wchoose([0.25, 0.75]) },
                            { [-12, 0, 12].wchoose([0.2, 0.6, 0.2]) }) });
                    var deg  = if(q > 0.62, { rrand(3, degs.size-1) },
                               if(q < 0.38, { rrand(0, 3) },
                                            { rrand(0, degs.size-1) }));
                    var semi = degs[deg % degs.size];
                    var freq = (root + semi + 12 + octShift).midicps;
                    var amp  = c.linlin(0, 8, 0.05, 0.14) * rrand(0.72, 1.0);

                    if(0.28.coin, { ~melodyDefIdx = rrand(0, ~melodyDefs.size - 1) });
                    Synth(~melodyDefs[~melodyDefIdx % ~melodyDefs.size],
                        [\freq, freq, \amp, amp, \pan, 1.0.rand2]);
                };
                dur.wait;
            });
        }
    }).play;
};

// ============================================================================
// 7. DUCK FUNCTION  — background drops to ~5% while jingle plays
// ============================================================================
~duck = { |duckAmt=0.05, holdTime=1.4, fadeTime=2.4|
    ~voices.doWithIndex { |v, i| v !? { v.set(\amp, ~slotAmps[i] * duckAmt) } };
    Routine({
        holdTime.wait;
        30.do { |i|
            var t = (i + 1) / 30.0;
            ~voices.doWithIndex { |v, j|
                v !? { v.set(\amp, ~slotAmps[j] * (duckAmt + (t * (1.0 - duckAmt)))) };
            };
            (fadeTime / 30.0).wait;
        };
    }).play;
};

// ============================================================================
// 8. JINGLE POOLS  (4 positive / 4 negative / 3 neutral — anti-repetition)
//    Amplitudes raised ~1.8× vs. old file for maximum audibility.
// ============================================================================
~jinglePools = IdentityDictionary.new;

~fireJingle = { |sym|
    var pool = ~jinglePools[sym];
    var last = ~lastJingleIdx[sym] ?? -1;
    if(pool.notNil and: { pool.size > 0 }, {
        var available = (0..pool.size-1).reject { |i| i == last };
        var idx = available.choose;
        ~lastJingleIdx[sym] = idx;
        (">>> JINGLE [" ++ sym ++ "] #" ++ idx).postln;
        ~duck.value(0.05, 1.5, 2.4);
        pool[idx].value;
    }, {
        ("!!! fireJingle: pool not found for sym=" ++ sym).postln;
    });
};

// ── POSITIVE ──────────────────────────────────────────────────────────────────
~jinglePools[\positive] = [

    // P-0: Rising Karplus arpeggio + FM chord stabs
    {
        var r = ~roots[~state[\harmony]] ?? 60;
        [0, 7, 12].do { |s, i|
            Synth(\jFMBurst, [\freq, (r+s).midicps, \amp, 0.78,
                              \pan, [-0.6,0,0.6][i], \idx, 3.5]);
        };
        [0, 2, 4, 7, 9, 11, 14, 16].do { |s, i|
            SystemClock.sched(i * 0.072, {
                Synth(\jKarplusArp, [\freq, (r+s+12).midicps, \amp, 0.85,
                                     \pan, i.linlin(0,7,-0.85,0.85)]);
                nil
            });
        };
    },

    // P-1: Bell chord explosion — multi-octave simultaneous hit
    {
        var r = ~roots[~state[\harmony]] ?? 60;
        [0, 4, 7, 12, 16, 19].do { |s, i|
            Synth(\jBellChord, [\freq, (r+s+12).midicps, \amp, 0.88,
                                \pan, i.linlin(0,5,-0.95,0.95)]);
        };
        SystemClock.sched(0.25, {
            Synth(\jSineSpray, [\freq, (r+24).midicps, \amp, 0.55, \spread, 0.045]);
            nil
        });
        SystemClock.sched(0.65, {
            Synth(\jSineSpray, [\freq, (r+31).midicps, \amp, 0.40, \spread, 0.09]);
            nil
        });
    },

    // P-2: FM stab cascade — rising with increasing FM index
    {
        var r = ~roots[~state[\harmony]] ?? 60;
        [0, 3, 7, 10, 14, 17, 21].do { |s, i|
            SystemClock.sched(i * 0.055, {
                Synth(\jFMBurst, [\freq, (r+s+12).midicps, \amp, 0.88,
                                  \pan, i.linlin(0,6,-1,1), \rise, 0.001,
                                  \fall, 2.0, \idx, 3.0+(i*0.7)]);
                nil
            });
        };
    },

    // P-3: Rhythmic noise burst + shimmer
    {
        var r = ~roots[~state[\harmony]] ?? 60;
        [0, 7, 12].do { |s, i|
            Synth(\jRhythmBurst, [\freq, (r+s+12).midicps, \amp, 0.80,
                                   \pan, [-0.5,0,0.5][i]]);
        };
        SystemClock.sched(0.4, {
            [0, 4, 7, 12].do { |s, i|
                Synth(\jBellChord, [\freq, (r+s+19).midicps, \amp, 0.68,
                                    \pan, i.linlin(0,3,-0.7,0.7)]);
            };
            nil
        });
        SystemClock.sched(0.8, {
            Synth(\jSineSpray, [\freq, (r+28).midicps, \amp, 0.45, \spread, 0.06]);
            nil
        });
    }
];

// ── NEGATIVE ──────────────────────────────────────────────────────────────────
~jinglePools[\negative] = [

    // N-0: Deep thud + descending FM scream cascade
    {
        var r = ~roots[~state[\harmony]] ?? 60;
        Synth(\jThudDeep,  [\freq, (r-24).midicps, \amp, 0.95, \pan, 0]);
        [0, -1, -3, -5, -8, -11].do { |s, i|
            SystemClock.sched(i * 0.10, {
                Synth(\jFMScream, [\freq, (r+s).midicps,
                                   \amp, 0.90 - (i*0.05),
                                   \pan, i.linlin(0,5,0.3,-0.3)]);
                nil
            });
        };
    },

    // N-1: Cluster smash + bass wall
    {
        var r = ~roots[~state[\harmony]] ?? 60;
        Synth(\jBassImpact,   [\freq, (r-24).midicps, \amp, 0.95]);
        Synth(\jClusterSmash, [\freq, (r-12).midicps, \amp, 0.88]);
        SystemClock.sched(0.32, {
            Synth(\jClusterSmash, [\freq, (r-8).midicps, \amp, 0.72]);
            nil
        });
        SystemClock.sched(0.65, {
            Synth(\jFMScream, [\freq, (r-3).midicps, \amp, 0.75, \pan, 0]);
            nil
        });
    },

    // N-2: Triple bass + descending FM screams
    {
        var r = ~roots[~state[\harmony]] ?? 60;
        [0, -5, -10].do { |s, i|
            SystemClock.sched(i * 0.14, {
                Synth(\jThudDeep, [\freq, (r - 24 + s).midicps,
                                   \amp, 0.90 - (i*0.08),
                                   \pan, i.linlin(0,2,-0.4,0.4)]);
                nil
            });
        };
        SystemClock.sched(0.22, {
            [0, -4, -8].do { |s, i|
                SystemClock.sched(i * 0.16, {
                    Synth(\jFMScream, [\freq, (r+s+3).midicps,
                                       \amp, 0.82-(i*0.07),
                                       \pan, (i.even.if(-0.45, 0.45))]);
                    nil
                });
            };
            nil
        });
    },

    // N-3: Maximum chaos wall
    {
        var r = ~roots[~state[\harmony]] ?? 60;
        Synth(\jBassImpact, [\freq, 40.midicps, \amp, 1.0]);
        2.do { |i|
            Synth(\jClusterSmash, [\freq, (r - 12 + (i * 7)).midicps,
                                    \amp, 0.82 - (i*0.08)]);
        };
        [0, 0.18, 0.38, 0.58].do { |t, i|
            SystemClock.sched(t, {
                Synth(\jFMScream, [\freq, (r + [-6,-3,0,3][i]).midicps,
                                   \amp, 0.88-(i*0.06),
                                   \pan, [0.4,-0.4,0.4,-0.4][i]]);
                nil
            });
        };
    }
];

// ── NEUTRAL ───────────────────────────────────────────────────────────────────
~jinglePools[\neutral] = [

    // Nt-0: Bell trio + blip chord
    {
        var r = ~roots[~state[\harmony]] ?? 60;
        [0, 0.20, 0.36].do { |t, i|
            SystemClock.sched(t, {
                Synth(\jKlank3, [\freq, (r + [7,12,7][i]).midicps,
                                 \amp, [0.88,0.72,0.62][i],
                                 \pan, [-0.42,0,0.42][i]]);
                nil
            });
        };
        [0, 5, 7].do { |s, i|
            Synth(\jBlip, [\freq, (r+s).midicps, \amp, 0.58,
                           \pan, i.linlin(0,2,-0.5,0.5)]);
        };
    },

    // Nt-1: Deep bell + rising chime cascade
    {
        var r = ~roots[~state[\harmony]] ?? 60;
        Synth(\jKlank3, [\freq, (r+7).midicps, \amp, 0.88, \pan, -0.32]);
        [0, 7, 12, 16, 19].do { |s, i|
            SystemClock.sched(i * 0.13, {
                Synth(\jChime, [\freq, (r+s+12).midicps,
                                \amp, 0.70 - (i*0.06),
                                \pan, i.linlin(0,4,-0.65,0.65)]);
                nil
            });
        };
    },

    // Nt-2: Blip cluster + FM chime
    {
        var r = ~roots[~state[\harmony]] ?? 60;
        [0, 3, 7, 10, 14].do { |s, i|
            SystemClock.sched(i * 0.08, {
                Synth(\jBlip, [\freq, (r+s+12).midicps,
                               \amp, 0.62 - (i*0.05),
                               \pan, i.linlin(0,4,-0.8,0.8)]);
                nil
            });
        };
        SystemClock.sched(0.25, {
            Synth(\jChime, [\freq, (r+19).midicps, \amp, 0.72, \pan, 0]);
            nil
        });
    }
];

// ============================================================================
// 9. MUTATION ROUTINE  (random timbre swap every 8-20 s)
// ============================================================================
~mutationRoutine = Routine({
    loop {
        rrand(8.0, 20.0).wait;
        if(~voices.any { |v| v.notNil }, { ~mutateOneVoice.value });
    }
}).play;

// ============================================================================
// 10. OSC RESPONDERS
//     Every handler logs to Post Window — use this to verify Unity→SC link.
// ============================================================================

OSCdef(\winrate, { |msg|
    var newQ = msg[1].asFloat;
    ("OSC /winrate  Q=" ++ newQ.round(0.001)).postln;
    ~state[\winrate] = newQ;
    // Full respawn whenever Q drifts more than 0.03 from last respawn
    // (makes the background audibly track every significant board change)
    if((newQ - ~lastRespawnQ).abs > 0.03, {
        { ~respawnAll.value }.defer;
    }, {
        { ~morphVoices.value }.defer;
    });
}, '/chess/winrate');

OSCdef(\delta, { |msg|
    ~state[\delta] = msg[1].asFloat;
}, '/chess/delta');

OSCdef(\confidence, { |msg|
    var prevC = ~state[\confidence];
    var newC  = msg[1].asFloat;
    ~state[\confidence] = newC;
    if(~voiceCount.value(prevC) != ~voiceCount.value(newC), {
        { ~respawnAll.value }.defer;
    }, {
        { ~morphVoices.value }.defer;
    });
}, '/chess/confidence');

OSCdef(\harmony, { |msg|
    var sym = msg[1].asString.asSymbol;
    ("OSC /harmony  " ++ sym).postln;
    if(sym != ~state[\harmony], {
        ~state[\harmony] = sym;
        { ~respawnAll.value; ~startMelody.value }.defer;
    });
}, '/chess/harmony');

OSCdef(\jingle, { |msg|
    var sym = msg[1].asString.asSymbol;
    ("OSC /jingle   " ++ sym).postln;
    ~state[\jingle] = sym;
    { ~fireJingle.value(sym) }.defer;
}, '/chess/jingle');

// ============================================================================
// 11. START
// ============================================================================
0.5.wait;
~respawnAll.value;
~startMelody.value;

// Status display — posts every 10 s so you can confirm state without Unity
~statusRoutine = Routine({
    loop {
        10.0.wait;
        ("--- STATE | Q=" ++ ~state[\winrate].round(0.001)
         ++ " C=" ++ ~state[\confidence].round(0.01)
         ++ " harm=" ++ ~state[\harmony]
         ++ " voices=" ++ ~voices.select(_.notNil).size).postln;
    }
}).play;

// Audio self-test: fires one neutral jingle 2 s after boot.
// If you hear it → SC audio works. Check Post Window for OSC messages from Unity.
SystemClock.sched(2.0, {
    "*** STARTUP TEST: firing neutral jingle (audio check) ***".postln;
    { ~fireJingle.value(\neutral) }.defer;
    nil
});

"=== Chess Music Engine v3 ready on port 57120. ===".postln;
"=== Watch Post Window for OSC messages from Unity.  ===".postln;

}; // end waitForBoot
)
