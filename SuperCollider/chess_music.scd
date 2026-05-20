// =============================================================================
// Chess With Procedural Music — SuperCollider Engine  v5  (VARIETY EDITION)
// =============================================================================
// USAGE: Ctrl+A then Ctrl+Enter.  Keep this window open while playing.
//
// DESIGN
//   Background  : continuous drum-machine beat — 16 patterns across 4 tiers,
//                 random within-tier rotation each bar, random fills, ghost
//                 notes, swing.  Linearly responds to Q (tempo), C (density),
//                 material (bass pitch), harmony (chord pad).
//
//   Jingles     : 2-10 note melodic arpeggio.  Note count and speed scale with
//                 move importance (intensity × capture).  16 scales/modes are
//                 chosen at random per jingle, biased major/minor/neutral by
//                 the move direction.  6 timbres rotate (piano, guitar, e-piano,
//                 vibraphone, marimba, harp).  Melodic contour also varies:
//                 sequential, arpeggio, random walk, alternating.
//
//   Piece points: P=1  N=3  B=3  R=5  Q=10
//
// OSC PROTOCOL (Unity → SC, port 57120)
//   /chess/winrate    float    Q in [0,1]
//   /chess/confidence float    C = log(N+1)
//   /chess/harmony    string   "Major" / "Neutral" / "Minor"
//   /chess/material   float    material differential in pawn units (+ = white)
//   /chess/intensity  float    jingle magnitude [0,1]
//   /chess/direction  float    +1 / 0 / -1
//   /chess/capture    float    0..10 (queen = 10)
//   /chess/jingle     string "fire"     trigger
// =============================================================================

(
s.options.numOutputBusChannels = 2;
s.options.memSize = 131072;

s.waitForBoot {

// ============================================================================
// 1. STATE
// ============================================================================
~state = (
    winrate:        0.5,
    confidence:     3.0,
    harmony:        \Neutral,
    materialPawns:  0.0,
    intensity:      0.0,
    direction:      0.0,
    capture:        0.0
);

~roots = (\Major: 62, \Neutral: 60, \Minor: 57);

// ============================================================================
// 2. SCALES / MODES  (16 different colours)
// ============================================================================
~scales = (
    major:          [0, 2, 4, 5, 7, 9, 11, 12, 14, 16, 17, 19, 21],
    naturalMinor:   [0, 2, 3, 5, 7, 8, 10, 12, 14, 15, 17, 19, 20],
    harmonicMinor:  [0, 2, 3, 5, 7, 8, 11, 12, 14, 15, 17, 19, 20],
    melodicMinor:   [0, 2, 3, 5, 7, 9, 11, 12, 14, 15, 17, 19, 21],
    dorian:         [0, 2, 3, 5, 7, 9, 10, 12, 14, 15, 17, 19, 21],
    phrygian:       [0, 1, 3, 5, 7, 8, 10, 12, 13, 15, 17, 19, 20],
    lydian:         [0, 2, 4, 6, 7, 9, 11, 12, 14, 16, 18, 19, 21],
    mixolydian:     [0, 2, 4, 5, 7, 9, 10, 12, 14, 16, 17, 19, 21],
    locrian:        [0, 1, 3, 5, 6, 8, 10, 12, 13, 15, 17, 18, 20],
    pentMajor:      [0, 2, 4, 7, 9, 12, 14, 16, 19, 21, 24],
    pentMinor:      [0, 3, 5, 7, 10, 12, 15, 17, 19, 22, 24],
    blues:          [0, 3, 5, 6, 7, 10, 12, 15, 17, 19, 22, 24],
    wholeTone:      [0, 2, 4, 6, 8, 10, 12, 14, 16, 18, 20],
    hirajoshi:      [0, 2, 3, 7, 8, 12, 14, 15, 19, 20, 24],   // Japanese
    arabian:        [0, 2, 4, 5, 6, 8, 10, 12, 14, 16, 17, 18],
    chromatic:      [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]
);

// Grouped by mood for direction-biased random pick
~scalesPositive = [\major, \lydian, \mixolydian, \pentMajor];
~scalesNegative = [\naturalMinor, \harmonicMinor, \phrygian, \locrian, \blues];
~scalesNeutral  = [\dorian, \melodicMinor, \pentMinor, \wholeTone, \hirajoshi, \arabian, \chromatic];

// ============================================================================
// 3. DRUM KIT SYNTHDEFS  (extended)
// ============================================================================

SynthDef(\kick, { |out=0, amp=0.80, pitch=42|
    var sig, click, env, freq;
    freq  = pitch.midicps;
    env   = EnvGen.kr(Env.perc(0.001, 0.42, 1, -8), doneAction: 2);
    sig   = SinOsc.ar(freq * XLine.kr(2.8, 1.0, 0.06)) * env;
    click = HPF.ar(WhiteNoise.ar(0.55), 1500) * EnvGen.kr(Env.perc(0.001, 0.018));
    Out.ar(out, (sig + click) * amp ! 2);
}).add;

SynthDef(\snare, { |out=0, amp=0.55, pitch=185|
    var noise, tone, env;
    env   = EnvGen.kr(Env.perc(0.001, 0.18, 1, -5), doneAction: 2);
    noise = HPF.ar(WhiteNoise.ar(1), 500) * EnvGen.kr(Env.perc(0.001, 0.14, 1, -4)) * 0.7;
    tone  = SinOsc.ar(pitch.midicps) * env * 0.4;
    Out.ar(out, (noise + tone) * amp ! 2);
}).add;

SynthDef(\rimshot, { |out=0, amp=0.45, pitch=78, pan=0.1|
    var sig, click, env;
    env   = EnvGen.kr(Env.perc(0.001, 0.06, 1, -4), doneAction: 2);
    click = HPF.ar(WhiteNoise.ar(1), 2500) * EnvGen.kr(Env.perc(0.001, 0.02, 1, -2));
    sig   = (SinOsc.ar(pitch.midicps) * 0.7 + click * 0.9) * env * amp;
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

SynthDef(\hihat, { |out=0, amp=0.22, pan=0.15|
    var sig, env;
    env = EnvGen.kr(Env.perc(0.001, 0.045, 1, -5), doneAction: 2);
    sig = HPF.ar(WhiteNoise.ar(1), 7000) * env * amp;
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

SynthDef(\openhat, { |out=0, amp=0.26, pan=0.25|
    var sig, env;
    env = EnvGen.kr(Env.perc(0.001, 0.32, 1, -4), doneAction: 2);
    sig = HPF.ar(WhiteNoise.ar(1), 6000) * env * amp;
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

SynthDef(\shaker, { |out=0, amp=0.20, pan=0.35|
    var sig, env;
    env = EnvGen.kr(Env.perc(0.005, 0.08, 1, -3), doneAction: 2);
    sig = HPF.ar(BPF.ar(PinkNoise.ar(1), 5500, 0.7), 4000) * env * amp;
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

SynthDef(\tom, { |out=0, amp=0.45, pitch=58, pan=0|
    var sig, env;
    env = EnvGen.kr(Env.perc(0.001, 0.32, 1, -5), doneAction: 2);
    sig = SinOsc.ar(pitch.midicps * XLine.kr(1.5, 1.0, 0.06)) * env * amp;
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

SynthDef(\clap, { |out=0, amp=0.40, pan= -0.15|
    var sig, mainEnv;
    mainEnv = EnvGen.kr(Env.perc(0.001, 0.22, 1, -3), doneAction: 2);
    sig = HPF.ar(WhiteNoise.ar(1), 1100);
    sig = sig * (
        EnvGen.kr(Env.perc(0.001, 0.012)) +
        EnvGen.kr(Env.perc(0.013, 0.012)) +
        EnvGen.kr(Env.perc(0.026, 0.012)) +
        (mainEnv * 0.5)
    );
    Out.ar(out, Pan2.ar(sig * amp, pan));
}).add;

SynthDef(\ride, { |out=0, amp=0.22, pan= -0.25|
    var sig, env;
    env = EnvGen.kr(Env.perc(0.002, 0.55, 1, -3), doneAction: 2);
    sig = BPF.ar(WhiteNoise.ar(1), 5500, 0.4) * env * amp * 1.5;
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

SynthDef(\crash, { |out=0, amp=0.32, pan=0.05|
    var sig, env;
    env = EnvGen.kr(Env.perc(0.005, 1.2, 1, -3), doneAction: 2);
    sig = HPF.ar(WhiteNoise.ar(1), 5000) * env * amp * 1.6;
    sig = FreeVerb.ar(sig, 0.4, 0.7);
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

SynthDef(\cowbell, { |out=0, amp=0.30, pitch=72, pan= -0.2|
    var sig, env;
    env = EnvGen.kr(Env.perc(0.001, 0.18, 1, -4), doneAction: 2);
    sig = (Pulse.ar(pitch.midicps, 0.5) + Pulse.ar((pitch + 7.4).midicps, 0.5)) * 0.5;
    sig = BPF.ar(sig, 800, 0.4) * env * amp * 2;
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

// Bass — short pitched note
SynthDef(\bassNote, { |out=0, amp=0.36, pitch=33, dur=0.4|
    var sig, env, p;
    p   = Lag.kr(pitch, 0.05);
    env = EnvGen.kr(Env.perc(0.005, dur, 1, -3), doneAction: 2);
    sig = (LFTri.ar(p.midicps) * 0.55 + SinOsc.ar(p.midicps * 0.5) * 0.55);
    sig = LPF.ar(sig, (p.midicps * 7).clip(120, 2400));
    sig = (sig * 1.3).tanh * env * amp;
    Out.ar(out, sig ! 2);
}).add;

// Pad — sustaining chord
SynthDef(\pad, { |out=0, amp=0.08, freq=261, gate=1, q=0.5|
    var sig, env;
    env = EnvGen.kr(Env.asr(2.5, 1, 3.5), gate, doneAction: 2);
    sig = Mix.fill(4, { |i|
        var f = freq * [1.0, 1.503, 1.997, 2.498][i]
                     * LFNoise2.kr(0.08 + (i * 0.02)).range(0.997, 1.003);
        SinOsc.ar(f, 0, 1.0 / (i+1).pow(0.7))
    });
    sig = LPF.ar(sig, q.linexp(0, 1, 800, 3500));
    sig = FreeVerb.ar(sig * env * amp, 0.55, 0.85);
    Out.ar(out, sig ! 2);
}).add;

// ============================================================================
// 4. JINGLE SYNTHDEFS  (6 melodic timbres)
// ============================================================================

SynthDef(\jPiano, { |out=0, freq=440, amp=0.55, pan=0|
    var sig, env, mod, attack, body;
    env    = EnvGen.kr(Env.perc(0.002, 2.4, 1, -4), doneAction: 2);
    mod    = SinOsc.ar(freq * 3.0, 0, freq * 3.0 * EnvGen.kr(Env.perc(0.002, 0.4, 1, -8)));
    attack = SinOsc.ar(freq + mod) * EnvGen.kr(Env.perc(0.002, 0.20, 1, -6));
    body   = Mix([
        SinOsc.ar(freq)        * 1.0,
        SinOsc.ar(freq * 2)    * 0.30,
        SinOsc.ar(freq * 3)    * 0.10,
        SinOsc.ar(freq * 4.01) * 0.05
    ]) * env;
    sig = (attack * 0.7 + body * 0.8) * amp;
    sig = FreeVerb.ar(sig, 0.30, 0.72);
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

SynthDef(\jGuitar, { |out=0, freq=440, amp=0.55, pan=0|
    var sig, env, body;
    env  = EnvGen.kr(Env.perc(0.001, 2.8, 1, -3), doneAction: 2);
    sig  = Pluck.ar(WhiteNoise.ar(0.9), Impulse.ar(0), 0.05,
                    freq.reciprocal, 3.0, 0.30);
    body = BPF.ar(sig, freq * 1.5, 0.55) * 0.45;
    sig  = (sig + body) * env * amp;
    sig  = FreeVerb.ar(sig, 0.30, 0.72);
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

SynthDef(\jEPiano, { |out=0, freq=440, amp=0.50, pan=0|
    var sig, env, mod, car;
    env = EnvGen.kr(Env.perc(0.004, 1.9, 1, -3.5), doneAction: 2);
    mod = SinOsc.ar(freq * 1.0, 0, freq * 2.5 * EnvGen.kr(Env.perc(0.004, 0.55, 1, -4)));
    car = SinOsc.ar(freq + mod);
    sig = car * env * amp;
    sig = FreeVerb.ar(sig, 0.42, 0.84);
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

SynthDef(\jVibe, { |out=0, freq=440, amp=0.55, pan=0|
    var sig, env, trem;
    env  = EnvGen.kr(Env.perc(0.001, 3.2, 1, -3.5), doneAction: 2);
    trem = SinOsc.kr(5.5, 0, 0.18, 1);
    sig  = Klank.ar(
        `[ [1.0, 2.78, 5.40, 8.93], nil, [3.0, 2.4, 1.5, 0.7] ],
        Impulse.ar(0) * amp) * env * trem;
    sig = FreeVerb.ar(sig, 0.55, 0.92);
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

SynthDef(\jMarimba, { |out=0, freq=440, amp=0.55, pan=0|
    var sig, env;
    env = EnvGen.kr(Env.perc(0.001, 1.6, 1, -5), doneAction: 2);
    sig = Klank.ar(
        `[ [1.0, 3.93, 9.54], nil, [2.0, 1.0, 0.4] ],
        Impulse.ar(0) * amp) * env;
    sig = LPF.ar(sig, 5000);
    sig = FreeVerb.ar(sig, 0.32, 0.68);
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

SynthDef(\jHarp, { |out=0, freq=440, amp=0.50, pan=0|
    var sig, env;
    env = EnvGen.kr(Env.perc(0.002, 2.5, 1, -3), doneAction: 2);
    sig = Pluck.ar(PinkNoise.ar(1.2), Impulse.ar(0), 0.04, freq.reciprocal, 2.6, 0.45);
    sig = BPF.ar(sig, freq * 1.2, 0.65) * 0.35 + (sig * 0.7);
    sig = FreeVerb.ar(sig * env * amp, 0.55, 0.90);
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

s.sync;

// ============================================================================
// 5. DRUM PATTERNS  (16 patterns across 4 tiers — random pick per bar)
// ============================================================================
~patternTiers = [
    // Tier 0 — SIMPLE (very low C: tentative position)
    [
        (kick:[1,0,0,0, 0,0,0,0, 1,0,0,0, 0,0,0,0], snare:[0,0,0,0, 1,0,0,0, 0,0,0,0, 1,0,0,0],
         hihat:[1,0,1,0, 1,0,1,0, 1,0,1,0, 1,0,1,0], openhat:[0,0,0,0, 0,0,0,0, 0,0,0,0, 0,0,0,1],
         tom:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0], clap:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],
         ride:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0], rim:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],
         shaker:[0,1,0,1,0,1,0,1,0,1,0,1,0,1,0,1]),
        (kick:[1,0,0,0, 0,0,0,0, 0,0,1,0, 0,0,0,0], snare:[0,0,0,0, 1,0,0,0, 0,0,0,0, 1,0,0,0],
         hihat:[1,1,0,1, 1,1,0,1, 1,1,0,1, 1,1,0,1], openhat:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],
         tom:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0], clap:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],
         ride:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0], rim:[0,0,1,0, 0,0,0,0, 0,0,1,0, 0,0,0,0],
         shaker:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]),
        (kick:[1,0,0,0, 1,0,0,0, 1,0,0,0, 1,0,0,0], snare:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],
         hihat:[0,0,1,0, 0,0,1,0, 0,0,1,0, 0,0,1,0], openhat:[1,0,0,0, 1,0,0,0, 1,0,0,0, 1,0,0,0],
         tom:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0], clap:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],
         ride:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0], rim:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],
         shaker:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]),
        (kick:[1,0,0,0, 0,0,0,0, 0,0,1,0, 0,0,0,0], snare:[0,0,0,0, 1,0,0,0, 0,0,0,0, 1,0,0,0],
         hihat:[1,0,0,0, 1,0,0,0, 1,0,0,0, 1,0,0,0], openhat:[0,0,0,0, 0,0,1,0, 0,0,0,0, 0,0,1,0],
         tom:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0], clap:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],
         ride:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0], rim:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],
         shaker:[0,1,0,1, 0,1,0,1, 0,1,0,1, 0,1,0,1])
    ],

    // Tier 1 — BASIC ROCK
    [
        (kick:[1,0,0,0, 0,0,1,0, 1,0,0,0, 0,0,0,0], snare:[0,0,0,0, 1,0,0,0, 0,0,0,0, 1,0,0,1],
         hihat:[1,0,1,0, 1,0,1,0, 1,0,1,0, 1,0,1,0], openhat:[0,0,0,0, 0,0,0,1, 0,0,0,0, 0,0,0,0],
         tom:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0], clap:[0,0,0,0, 1,0,0,0, 0,0,0,0, 1,0,0,0],
         ride:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0], rim:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],
         shaker:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]),
        (kick:[1,0,1,0, 0,0,1,0, 1,0,0,0, 0,1,0,0], snare:[0,0,0,0, 1,0,0,0, 0,0,0,1, 1,0,0,0],
         hihat:[1,1,1,0, 1,1,1,0, 1,1,1,0, 1,1,1,0], openhat:[0,0,0,1, 0,0,0,0, 0,0,0,1, 0,0,0,0],
         tom:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0], clap:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],
         ride:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0], rim:[0,0,0,0,0,0,0,1, 0,0,0,0,0,0,0,1],
         shaker:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]),
        (kick:[1,0,0,1, 0,0,0,0, 1,0,0,1, 0,0,0,0], snare:[0,0,0,0, 1,0,0,0, 0,0,0,0, 1,0,0,0],
         hihat:[1,0,1,1, 1,0,1,1, 1,0,1,1, 1,0,1,1], openhat:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1],
         tom:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0], clap:[0,0,0,0, 0,1,0,0, 0,0,0,0, 0,1,0,0],
         ride:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0], rim:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],
         shaker:[0,1,0,0, 0,1,0,0, 0,1,0,0, 0,1,0,0]),
        (kick:[1,0,0,0, 1,0,0,1, 1,0,0,0, 1,0,0,1], snare:[0,0,0,0, 0,0,1,0, 0,0,0,0, 0,0,1,0],
         hihat:[1,0,1,0, 1,0,1,0, 1,0,1,0, 1,0,1,0], openhat:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],
         tom:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0], clap:[0,0,0,0, 1,0,0,0, 0,0,0,0, 1,0,0,0],
         ride:[1,0,0,0, 0,0,0,0, 1,0,0,0, 0,0,0,0], rim:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],
         shaker:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0])
    ],

    // Tier 2 — BUSY
    [
        (kick:[1,0,0,1, 0,0,1,0, 1,0,0,1, 0,0,1,0], snare:[0,0,0,0, 1,0,0,1, 0,0,1,0, 1,0,0,1],
         hihat:[1,1,0,1, 1,1,0,1, 1,1,0,1, 1,1,0,1], openhat:[0,0,1,0, 0,0,0,0, 0,0,1,0, 0,0,0,0],
         tom:[0,0,0,0, 0,0,0,1, 0,0,0,0, 0,0,0,0], clap:[0,0,0,0, 1,0,0,0, 0,0,0,0, 1,0,0,1],
         ride:[1,0,0,0, 0,0,0,0, 1,0,0,0, 0,0,0,0], rim:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],
         shaker:[0,0,1,0, 0,0,1,0, 0,0,1,0, 0,0,1,0]),
        (kick:[1,0,1,0, 0,1,0,0, 1,0,1,0, 0,1,0,0], snare:[0,0,0,0, 1,0,0,1, 0,0,0,1, 1,0,0,0],
         hihat:[1,1,1,0, 1,1,1,0, 1,1,1,0, 1,1,1,0], openhat:[0,0,0,1, 0,0,0,0, 0,0,0,1, 0,0,0,0],
         tom:[0,0,0,0, 0,0,0,0, 0,1,0,0, 0,0,0,0], clap:[0,0,0,0, 1,0,0,0, 0,0,0,1, 1,0,0,0],
         ride:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0], rim:[0,1,0,0, 0,1,0,0, 0,1,0,0, 0,1,0,0],
         shaker:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]),
        (kick:[1,1,0,0, 0,0,1,0, 1,1,0,0, 0,0,1,0], snare:[0,0,0,1, 1,0,0,0, 0,0,0,1, 1,0,0,0],
         hihat:[1,0,1,1, 1,0,1,1, 1,0,1,1, 1,0,1,1], openhat:[0,0,0,0, 0,0,0,1, 0,0,0,0, 0,0,0,1],
         tom:[0,0,0,0, 0,0,0,0, 0,0,1,0, 0,0,0,1], clap:[0,0,0,0, 1,0,0,1, 0,0,0,0, 1,0,0,1],
         ride:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0], rim:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],
         shaker:[0,1,0,1, 0,1,0,1, 0,1,0,1, 0,1,0,1]),
        (kick:[1,0,0,1, 1,0,0,0, 1,0,1,0, 0,0,1,0], snare:[0,0,0,0, 1,0,1,0, 0,0,0,1, 1,0,0,0],
         hihat:[1,1,1,1, 1,1,1,1, 1,1,1,1, 1,1,1,1], openhat:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],
         tom:[0,0,0,0, 0,0,0,0, 0,0,0,1, 0,0,0,0], clap:[0,0,0,0, 1,0,0,0, 0,0,0,0, 1,0,0,1],
         ride:[1,0,0,0, 1,0,0,0, 1,0,0,0, 1,0,0,0], rim:[0,0,1,0, 0,0,1,0, 0,0,1,0, 0,0,1,0],
         shaker:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0])
    ],

    // Tier 3 — EXPLOSIVE
    [
        (kick:[1,0,1,0, 1,0,1,1, 1,0,1,0, 1,1,0,1], snare:[0,0,0,1, 1,0,1,0, 0,1,0,1, 1,0,1,1],
         hihat:[1,1,1,1, 1,1,1,1, 1,1,1,1, 1,1,1,1], openhat:[0,0,0,0, 0,0,1,0, 0,0,0,1, 0,1,0,0],
         tom:[0,0,0,1, 0,0,0,0, 1,0,1,0, 0,0,0,1], clap:[0,1,0,0, 1,0,0,1, 0,1,0,0, 1,0,1,0],
         ride:[1,0,0,1, 0,1,0,0, 1,0,0,1, 0,1,0,0], rim:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],
         shaker:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]),
        (kick:[1,0,1,1, 0,0,1,0, 1,0,1,1, 0,1,1,0], snare:[0,0,0,1, 1,0,0,1, 0,1,0,1, 1,0,0,1],
         hihat:[1,1,1,1, 1,1,1,1, 1,1,1,1, 1,1,1,1], openhat:[0,0,1,0, 0,1,0,0, 0,0,1,0, 1,0,0,1],
         tom:[0,1,0,0, 0,0,0,1, 0,0,1,0, 0,0,0,1], clap:[0,0,0,0, 1,0,1,0, 0,0,0,0, 1,0,1,0],
         ride:[1,0,0,1, 1,0,0,0, 1,0,0,1, 0,0,0,1], rim:[0,1,0,0, 0,1,0,0, 0,1,0,0, 0,1,0,0],
         shaker:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]),
        (kick:[1,1,0,1, 1,0,1,1, 1,1,0,1, 1,1,0,1], snare:[0,0,1,0, 1,0,0,1, 0,1,0,0, 1,0,1,0],
         hihat:[1,0,1,1, 1,0,1,1, 1,0,1,1, 1,0,1,1], openhat:[0,0,0,1, 0,0,0,0, 0,0,0,1, 0,0,0,0],
         tom:[1,0,0,0, 0,1,0,0, 1,0,0,0, 0,1,0,1], clap:[0,0,1,1, 0,1,1,0, 0,1,1,0, 0,1,0,1],
         ride:[0,1,0,0, 0,1,0,0, 0,1,0,0, 0,1,0,0], rim:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],
         shaker:[1,1,1,0, 1,1,1,0, 1,1,1,0, 1,1,1,0]),
        (kick:[1,0,0,1, 0,1,1,0, 1,1,0,0, 1,0,1,1], snare:[0,1,0,0, 1,0,0,1, 1,0,0,1, 0,1,0,1],
         hihat:[1,1,1,1, 1,1,1,1, 1,1,1,1, 1,1,1,1], openhat:[0,0,1,0, 0,0,1,0, 0,0,1,0, 0,0,1,1],
         tom:[0,0,0,0, 0,0,0,1, 0,0,1,0, 1,0,0,0], clap:[0,0,0,0, 1,0,1,1, 0,0,0,0, 1,0,1,1],
         ride:[1,1,0,0, 1,1,0,0, 1,1,0,0, 1,1,0,0], rim:[1,0,0,0, 0,0,0,1, 1,0,0,0, 0,0,0,1],
         shaker:[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0])
    ]
];

// ============================================================================
// 6. BEAT ENGINE
// ============================================================================
~currentBPM        = 100;
~currentTier       = 1;
~currentBassMidi   = 33;
~currentStep       = 0;
~currentPattern    = nil;     // re-selected at bar start
~swing             = 0.0;     // 0 = straight, 0.15 = mild swing

~padSynth = nil;
~beatTask = nil;

~retriggerPad = {
    var harm = ~state[\harmony];
    var root = ~roots[harm] ?? 60;
    var freq = (root - 12).midicps;
    var q    = ~state[\winrate].asFloat;
    ~padSynth !? { ~padSynth.set(\gate, 0) };
    ~padSynth = Synth(\pad, [\freq, freq, \q, q, \amp, 0.06]);
};

~pickPattern = {
    // Random within current tier; occasionally pull from adjacent tier
    var tier = ~currentTier;
    if(0.10.coin, { tier = (tier + [-1, 1].choose).clip(0, 3) });
    ~currentPattern = ~patternTiers[tier].choose;
};

// A short fill (just a snare/tom roll) replacing one bar every 4..6 bars
~barCount = 0;
~doFill = {
    var fillType = [\snareRoll, \tomRoll, \handsCrash].choose;
    case
    { fillType == \snareRoll } {
        16.do { |i|
            SystemClock.sched(i * 0.06, {
                Synth(\snare, [\amp, 0.35 + (i / 16 * 0.25)]);
                nil
            });
        };
    }
    { fillType == \tomRoll } {
        8.do { |i|
            SystemClock.sched(i * 0.10, {
                Synth(\tom, [\amp, 0.5, \pitch, [50, 55, 60, 65, 72, 78][i.min(5)],
                             \pan, [-0.4, -0.2, 0, 0.2, 0.4, 0.6][i.min(5)]]);
                nil
            });
        };
    }
    { fillType == \handsCrash } {
        Synth(\crash, [\amp, 0.4]);
        [0, 0.20, 0.45, 0.75].do { |t|
            SystemClock.sched(t, {
                Synth(\clap, [\amp, 0.45, \pan, 1.0.rand2]);
                nil
            });
        };
    };
};

~startBeat = {
    ~beatTask.stop;
    ~currentStep = 0;
    ~barCount    = 0;
    ~pickPattern.value;

    ~beatTask = Task({
        loop {
            var bpm     = ~currentBPM.max(40);
            var stepDur = (60 / bpm / 4).clip(0.04, 0.5);
            var step    = ~currentStep;
            var pat     = ~currentPattern;
            var swing   = ~swing;
            // swing offset: alternate steps slightly longer/shorter
            var dur     = if(step.odd, { stepDur * (1 + swing) }, { stepDur * (1 - swing) });
            var bassMidi = ~currentBassMidi;

            // Drum-kit triggers
            if(pat[\kick][step]    == 1, { Synth(\kick,    [\amp, 0.85, \pitch, 42 + 3.0.rand2]) });
            if(pat[\snare][step]   == 1, { Synth(\snare,   [\amp, 0.55 + 0.1.rand2]) });
            if(pat[\hihat][step]   == 1, {
                Synth(\hihat, [\amp, 0.20 + 0.06.rand2, \pan, 0.15 + 0.10.rand2])
            });
            if(pat[\openhat][step] == 1, { Synth(\openhat, [\amp, 0.26, \pan, 0.25 + 0.1.rand2]) });
            if(pat[\tom][step]     == 1, {
                Synth(\tom, [\amp, 0.42, \pitch, [50,55,60,65,72].choose, \pan, 0.5.rand2])
            });
            if(pat[\clap][step]    == 1, { Synth(\clap, [\amp, 0.40, \pan, -0.15 + 0.1.rand2]) });
            if(pat[\ride][step]    == 1, { Synth(\ride, [\amp, 0.22, \pan, -0.25 + 0.1.rand2]) });
            if(pat[\rim] [step]    == 1, { Synth(\rimshot,[\amp, 0.42, \pan, 0.1.rand2]) });
            if(pat[\shaker][step]  == 1, { Synth(\shaker, [\amp, 0.18, \pan, 0.35 + 0.1.rand2]) });

            // 5% chance of a random ghost note on snare
            if(0.05.coin, { Synth(\snare, [\amp, 0.15]) });

            // Bass note on strong beats (every 4 steps, locked to kick)
            if((pat[\kick][step] == 1) and: { (step % 4) == 0 }, {
                Synth(\bassNote, [\pitch, bassMidi, \amp, 0.36, \dur, stepDur * 3.2]);
            });

            ~currentStep = (step + 1) % 16;
            // End-of-bar: pick a new pattern; every 4-6 bars insert a fill
            if(~currentStep == 0, {
                ~barCount = ~barCount + 1;
                if((~barCount % rrand(4, 6)) == 0, { ~doFill.value });
                ~pickPattern.value;
            });

            dur.wait;
        }
    }).play;
};

~updateBeat = {
    var q        = ~state[\winrate].asFloat;
    var c        = ~state[\confidence].asFloat;
    var material = ~state[\materialPawns].asFloat;
    var rootMidi = ~roots[~state[\harmony]] ?? 60;

    // Tempo: linear 75 → 145 BPM
    ~currentBPM = q.linlin(0, 1, 75, 145) + c.linlin(0, 8, -5, 10);
    ~currentBPM = ~currentBPM.clip(60, 180);

    // Complexity tier 0..3
    ~currentTier = c.linlin(0, 8, 0, 3).round.asInteger.clip(0, 3);

    // Swing: more swing when neutral, straight when extreme
    ~swing = (1.0 - ((q - 0.5).abs * 2)).clip(0, 1) * 0.10;

    // Bass note: harmony root - 24 (deep) + material shift (1 semi per pawn)
    ~currentBassMidi = (rootMidi - 24) + material.clip(-10, 10);

    ("BEAT  BPM=" ++ ~currentBPM.round(1)
     ++ "  tier=" ++ ~currentTier
     ++ "  bass=" ++ ~currentBassMidi.round(0.1)
     ++ "  swing=" ++ ~swing.round(0.01)
     ++ "  | Q=" ++ q.round(0.01)
     ++ " C=" ++ c.round(0.01)
     ++ " mat=" ++ material.round(0.1)).postln;
};

// ============================================================================
// 7. JINGLE ENGINE  (2-10 notes, 16 scales, 6 timbres, varied contours)
// ============================================================================

~timbres = [\jPiano, \jGuitar, \jEPiano, \jVibe, \jMarimba, \jHarp];
~lastTimbre = \jPiano;
~lastScale  = \major;
~padDuckRoutine = nil;

~duckPad = { |duckAmt=0.35, holdTime=0.5, fadeTime=1.3|
    ~padDuckRoutine.stop;
    ~padSynth !? { ~padSynth.set(\amp, 0.06 * duckAmt) };
    ~padDuckRoutine = Routine({
        holdTime.wait;
        20.do { |i|
            var t = (i + 1) / 20.0;
            ~padSynth !? {
                ~padSynth.set(\amp, 0.06 * (duckAmt + (t * (1.0 - duckAmt))))
            };
            (fadeTime / 20.0).wait;
        };
    }).play;
};

// Build a list of degree-indices into the chosen scale.
// contour ∈ {sequential, arpeggio, randomWalk, alternating, scaleRun, peak}
~buildMelody = { |noteCount, contour, scale, ascending|
    var scaleSize = scale.size;
    var degrees   = nil;
    case
    { contour == \sequential } {
        // 0,1,2,3,... (or reversed)
        degrees = if(ascending,
            { (0..noteCount-1) },
            { (0..noteCount-1).reverse });
    }
    { contour == \arpeggio } {
        // Triad/quad arpeggio: 0,2,4 (or 0,2,4,6)
        var arp = if(ascending, { [0, 2, 4, 7, 9, 11, 12, 14] }, { [14, 12, 11, 9, 7, 4, 2, 0] });
        degrees = arp.copyRange(0, noteCount - 1);
    }
    { contour == \randomWalk } {
        // Each note moves ±1..3 degrees from previous, biased by direction
        var cur = if(ascending, { 0 }, { 5 });
        degrees = Array.new(noteCount);
        noteCount.do {
            degrees.add(cur.clip(0, scaleSize - 1));
            cur = cur + (if(ascending, { rrand(1, 3) }, { rrand(-3, -1) }))
                      + ([-1, 0, 1].wchoose([0.2, 0.6, 0.2]));
        };
    }
    { contour == \alternating } {
        // 0,4,1,5,2,6,...
        var even = (0..((noteCount/2).ceil - 1));
        var odd  = (0..(noteCount/2 - 1)).collect { |i| i + 4 };
        degrees = Array.new(noteCount);
        noteCount.do { |i|
            degrees.add(if(i.even, { even[i/2] }, { odd[(i-1)/2] }));
        };
        if(ascending.not, { degrees = degrees.reverse });
    }
    { contour == \scaleRun } {
        // Sequential with one or two octave jumps
        degrees = (0..noteCount-1).collect { |i|
            if(i == (noteCount - 1), { i + 7 }, { i })
        };
        if(ascending.not, { degrees = degrees.reverse });
    }
    { contour == \peak } {
        // Up to middle then back down
        var half = (noteCount / 2).floor.asInteger;
        var up   = (0..half);
        var down = (half-1..0);
        degrees  = (up ++ down).copyRange(0, noteCount - 1);
        if(ascending.not, { degrees = degrees.reverse });
    };

    // Map degree indices to scale-relative semitones, wrapping with octave shifts
    degrees.collect { |d|
        var idx, oct;
        idx = d % scaleSize;
        oct = (d / scaleSize).floor * 12;
        scale[idx] + oct
    };
};

~fireJingle = {
    var intensity = ~state[\intensity].asFloat.clip(0, 1);
    var direction = ~state[\direction].asFloat;
    var capture   = ~state[\capture].asFloat;
    var noteCount, interval, scaleName, scaleArr, contour, ascending;
    var rootMidi, timbre, ampBase, semitones;

    // Note count: 2 (trivial) → 10 (queen capture / huge dQ)
    noteCount = (2 + (intensity * 6) + (capture * 0.4)).round.asInteger.clip(2, 10);
    // Force 10-note burst on queen capture
    if(capture >= 10, { noteCount = 10 });

    // Note interval: very fast (0.08s) when intense, slow (0.32s) when calm
    interval = 0.32 - (intensity * 0.22) - ((capture / 10) * 0.05);
    interval = interval.clip(0.07, 0.36);

    // Scale: pick randomly from direction-biased pool (different from last)
    scaleName = case
        { direction > 0.5 }  { ~scalesPositive.reject{ |s| s == ~lastScale }.choose }
        { direction < -0.5 } { ~scalesNegative.reject{ |s| s == ~lastScale }.choose }
        { true }             { ~scalesNeutral.reject { |s| s == ~lastScale }.choose };
    if(scaleName.isNil, { scaleName = \major });
    ~lastScale = scaleName;
    scaleArr  = ~scales[scaleName];

    // Contour: random; longer melodies use more interesting patterns
    contour = if(noteCount <= 3,
        { [\sequential, \arpeggio].choose },
        { [\sequential, \arpeggio, \randomWalk, \alternating, \scaleRun, \peak].choose });

    // Direction
    ascending = direction >= 0;

    // Root: harmony root + 12 (one octave up); +12 more for capture ≥ 5
    rootMidi = (~roots[~state[\harmony]] ?? 60) + 12;
    if(capture >= 5,  { rootMidi = rootMidi + 7  });  // up a fifth
    if(capture >= 10, { rootMidi = rootMidi + 5  });  // total octave for queen

    // Timbre: pick a different one from last time
    timbre = ~timbres.reject{ |t| t == ~lastTimbre }.choose;
    ~lastTimbre = timbre;

    // Amplitude
    ampBase = 0.42 + (intensity * 0.40) + ((capture / 10) * 0.20);
    ampBase = ampBase.clip(0.4, 1.0);

    // Build the actual semitone sequence
    semitones = ~buildMelody.value(noteCount, contour, scaleArr, ascending);

    (">>> JINGLE  " ++ timbre.asString.toUpper
     ++ "  notes=" ++ noteCount
     ++ "  intvl=" ++ interval.round(0.01) ++ "s"
     ++ "  scale=" ++ scaleName
     ++ "  contour=" ++ contour
     ++ "  dir=" ++ (if(ascending, "UP", "DOWN"))
     ++ "  int=" ++ intensity.round(0.01)
     ++ "  cap=" ++ capture.round(1)).postln;

    ~duckPad.value(0.35, interval * noteCount * 0.6, 1.3);

    // Schedule the notes
    semitones.do { |semi, i|
        var freq, amp, pan;
        freq = (rootMidi + semi).midicps;
        amp  = ampBase * (1.0 - (i * 0.03));
        // Pan sweeps with the melody — left to right (or right to left if descending)
        pan  = (((i / (noteCount - 1).max(1)) - 0.5) * 1.4) * (if(ascending, 1, -1));

        SystemClock.sched(i * interval, {
            Synth(timbre, [\freq, freq, \amp, amp, \pan, pan]);
            nil
        });
    };
};

// ============================================================================
// 8. OSC RESPONDERS
// ============================================================================

OSCdef(\winrate, { |msg|
    var newQ = msg[1].asFloat;
    ~state[\winrate] = newQ;
    ("OSC /winrate     " ++ newQ.round(0.001)).postln;
    { ~updateBeat.value }.defer;
}, '/chess/winrate');

OSCdef(\confidence, { |msg|
    var newC = msg[1].asFloat;
    ~state[\confidence] = newC;
    ("OSC /confidence  " ++ newC.round(0.01)).postln;
    { ~updateBeat.value }.defer;
}, '/chess/confidence');

OSCdef(\harmony, { |msg|
    var sym = msg[1].asString.asSymbol;
    ("OSC /harmony     " ++ sym).postln;
    if(sym != ~state[\harmony], {
        ~state[\harmony] = sym;
        { ~retriggerPad.value; ~updateBeat.value }.defer;
    });
}, '/chess/harmony');

OSCdef(\material, { |msg|
    var m = msg[1].asFloat;
    ~state[\materialPawns] = m;
    ("OSC /material    " ++ m.round(0.1) ++ " pawns").postln;
    { ~updateBeat.value }.defer;
}, '/chess/material');

OSCdef(\intensity, { |msg| ~state[\intensity] = msg[1].asFloat }, '/chess/intensity');
OSCdef(\direction, { |msg| ~state[\direction] = msg[1].asFloat }, '/chess/direction');
OSCdef(\capture,   { |msg| ~state[\capture]   = msg[1].asFloat }, '/chess/capture');

OSCdef(\jingleFire, { |msg|
    ("OSC /jingle      fire").postln;
    { ~fireJingle.value }.defer;
}, '/chess/jingle');

// ============================================================================
// 9. START
// ============================================================================
0.5.wait;
~retriggerPad.value;
~updateBeat.value;
~startBeat.value;

~statusRoutine = Routine({
    loop {
        12.0.wait;
        ("--- STATE | Q=" ++ ~state[\winrate].round(0.001)
         ++ " C=" ++ ~state[\confidence].round(0.01)
         ++ " harm=" ++ ~state[\harmony]
         ++ " mat=" ++ ~state[\materialPawns].round(0.1)
         ++ " BPM=" ++ ~currentBPM.round(1)
         ++ " tier=" ++ ~currentTier).postln;
    }
}).play;

SystemClock.sched(2.0, {
    "*** STARTUP TEST: firing sample jingle ***".postln;
    ~state[\intensity] = 0.6;
    ~state[\direction] = 1.0;
    ~state[\capture]   = 3.0;
    { ~fireJingle.value }.defer;
    nil
});

"".postln;
"=== Chess Music Engine v5 (VARIETY EDITION) ready on port 57120. ===".postln;
"=== 16 drum patterns | 6 melodic timbres | 16 scales              ===".postln;
"=== Jingles: 2-10 notes scaled by move importance + capture       ===".postln;

}; // end waitForBoot
)
