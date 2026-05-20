// =============================================================================
// Chess With Procedural Music — SuperCollider Engine  v4  (BEAT EDITION)
// =============================================================================
// USAGE:
//   1. Select ALL (Ctrl+A) then evaluate (Ctrl+Enter) to boot and start.
//   2. Keep this window open while playing chess in Unity.
//
// DESIGN
//   Background  : continuous drum-machine beat (kick/snare/hat/tom/clap)
//                 plus pitched bass note + chord pad.
//                 Tempo, pattern density, bass pitch all driven LINEARLY by
//                 game state (Q = winrate, C = confidence, material diff).
//                 The beat keeps playing forever; only its parameters change.
//   Jingles     : per-move melodic arpeggio on piano or guitar timbre.
//                 3-5 notes, ascending/descending and major/minor depending on
//                 the move's evaluation. Heavy captures (queen, rook) produce
//                 dramatic 5-note ascending bursts with octave jumps.
//
// OSC PROTOCOL (Unity → SC, port 57120)
//   /chess/winrate    float           Q in [0,1]
//   /chess/confidence float           C = log(N+1)
//   /chess/harmony    string          "Major" / "Neutral" / "Minor"
//   /chess/material   float           material differential, pawn units
//                                     (positive = white ahead)
//   /chess/intensity  float           jingle magnitude in [0,1]
//   /chess/direction  float           +1 (positive) / -1 (negative) / 0
//   /chess/capture    float           captured piece value, 0..9
//   /chess/jingle     string "fire"   trigger — SC reads intensity/dir/capture
// =============================================================================

(
s.options.numOutputBusChannels = 2;
s.options.memSize = 131072;

s.waitForBoot {

// ============================================================================
// 1. GLOBAL STATE
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

~scaleDeg = (
    major:      [0, 2, 4, 5, 7, 9, 11, 12, 14, 16, 19],
    minor:      [0, 2, 3, 5, 7, 8, 10, 12, 14, 15, 17],
    pentatonic: [0, 2, 5, 7, 9, 12, 14, 17, 19, 21, 24]
);

// ============================================================================
// 2. DRUM KIT SYNTHDEFS
// ============================================================================

SynthDef(\kick, { |out=0, amp=0.75, pitch=45|
    var sig, click, env, freq;
    freq = pitch.midicps;
    env  = EnvGen.kr(Env.perc(0.001, 0.42, 1, -8), doneAction: 2);
    sig  = SinOsc.ar(freq * XLine.kr(2.8, 1.0, 0.06)) * env;
    click = HPF.ar(WhiteNoise.ar(0.55), 1500) * EnvGen.kr(Env.perc(0.001, 0.018));
    Out.ar(out, (sig + click) * amp ! 2);
}).add;

SynthDef(\snare, { |out=0, amp=0.55, pitch=180|
    var noise, tone, env;
    env   = EnvGen.kr(Env.perc(0.001, 0.18, 1, -5), doneAction: 2);
    noise = HPF.ar(WhiteNoise.ar(1), 500)
            * EnvGen.kr(Env.perc(0.001, 0.14, 1, -4)) * 0.7;
    tone  = SinOsc.ar(pitch.midicps) * env * 0.4;
    Out.ar(out, (noise + tone) * amp ! 2);
}).add;

SynthDef(\hihat, { |out=0, amp=0.22, pan=0.15|
    var sig, env;
    env = EnvGen.kr(Env.perc(0.001, 0.045, 1, -5), doneAction: 2);
    sig = HPF.ar(WhiteNoise.ar(1), 7000) * env * amp;
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

SynthDef(\openhat, { |out=0, amp=0.26, pan=0.25|
    var sig, env;
    env = EnvGen.kr(Env.perc(0.001, 0.28, 1, -4), doneAction: 2);
    sig = HPF.ar(WhiteNoise.ar(1), 6000) * env * amp;
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

SynthDef(\tom, { |out=0, amp=0.42, pitch=58, pan=0|
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

SynthDef(\ride, { |out=0, amp=0.20, pan= -0.25|
    var sig, env;
    env = EnvGen.kr(Env.perc(0.002, 0.55, 1, -3), doneAction: 2);
    sig = BPF.ar(WhiteNoise.ar(1), 5500, 0.4) * env * amp * 1.5;
    Out.ar(out, Pan2.ar(sig, pan));
}).add;

// Bass — short pitched note with smooth attack, glides on pitch parameter
SynthDef(\bassNote, { |out=0, amp=0.34, pitch=33, dur=0.4|
    var sig, env, p;
    p   = Lag.kr(pitch, 0.05);
    env = EnvGen.kr(Env.perc(0.005, dur, 1, -3), doneAction: 2);
    sig = (LFTri.ar(p.midicps) * 0.55 + SinOsc.ar(p.midicps * 0.5) * 0.55);
    sig = LPF.ar(sig, (p.midicps * 7).clip(120, 2400));
    sig = (sig * 1.3).tanh * env * amp;
    Out.ar(out, sig ! 2);
}).add;

// Pad — sustaining chord, gated
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
// 3. JINGLE SYNTHDEFS  (piano + guitar)
// ============================================================================

SynthDef(\jPiano, { |out=0, freq=440, amp=0.55, pan=0|
    var sig, env, mod, attack, body;
    env    = EnvGen.kr(Env.perc(0.002, 2.4, 1, -4), doneAction: 2);
    mod    = SinOsc.ar(freq * 3.0, 0, freq * 3.0 * EnvGen.kr(Env.perc(0.002, 0.4, 1, -8)));
    attack = SinOsc.ar(freq + mod) * EnvGen.kr(Env.perc(0.002, 0.20, 1, -6));
    body   = Mix([
        SinOsc.ar(freq)         * 1.0,
        SinOsc.ar(freq * 2)     * 0.30,
        SinOsc.ar(freq * 3)     * 0.10,
        SinOsc.ar(freq * 4.01)  * 0.05
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

s.sync;

// ============================================================================
// 4. DRUM PATTERNS  (4 complexity levels, 16 steps each)
// ============================================================================
~patterns = [

    // P0 — SIMPLE  (low C: tentative position, few simulations)
    (
        kick:    [1,0,0,0, 0,0,0,0, 1,0,0,0, 0,0,0,0],
        snare:   [0,0,0,0, 1,0,0,0, 0,0,0,0, 1,0,0,0],
        hihat:   [1,0,1,0, 1,0,1,0, 1,0,1,0, 1,0,1,0],
        openhat: [0,0,0,0, 0,0,0,0, 0,0,0,0, 0,0,0,1],
        tom:     [0,0,0,0, 0,0,0,0, 0,0,0,0, 0,0,0,0],
        clap:    [0,0,0,0, 0,0,0,0, 0,0,0,0, 0,0,0,0],
        ride:    [0,0,0,0, 0,0,0,0, 0,0,0,0, 0,0,0,0]
    ),

    // P1 — BASIC ROCK  (medium C: standard backbone)
    (
        kick:    [1,0,0,0, 0,0,1,0, 1,0,0,0, 0,0,0,0],
        snare:   [0,0,0,0, 1,0,0,0, 0,0,0,0, 1,0,0,1],
        hihat:   [1,0,1,0, 1,0,1,0, 1,0,1,0, 1,0,1,0],
        openhat: [0,0,0,0, 0,0,0,1, 0,0,0,0, 0,0,0,0],
        tom:     [0,0,0,0, 0,0,0,0, 0,0,0,0, 0,0,0,0],
        clap:    [0,0,0,0, 1,0,0,0, 0,0,0,0, 1,0,0,0],
        ride:    [0,0,0,0, 0,0,0,0, 0,0,0,0, 0,0,0,0]
    ),

    // P2 — BUSY  (high C: confident position, complex play)
    (
        kick:    [1,0,0,1, 0,0,1,0, 1,0,0,1, 0,0,1,0],
        snare:   [0,0,0,0, 1,0,0,1, 0,0,1,0, 1,0,0,1],
        hihat:   [1,1,0,1, 1,1,0,1, 1,1,0,1, 1,1,0,1],
        openhat: [0,0,1,0, 0,0,0,0, 0,0,1,0, 0,0,0,0],
        tom:     [0,0,0,0, 0,0,0,1, 0,0,0,0, 0,0,0,0],
        clap:    [0,0,0,0, 1,0,0,0, 0,0,0,0, 1,0,0,1],
        ride:    [1,0,0,0, 0,0,0,0, 1,0,0,0, 0,0,0,0]
    ),

    // P3 — EXPLOSIVE  (max C: critical position, every sim matters)
    (
        kick:    [1,0,1,0, 1,0,1,1, 1,0,1,0, 1,1,0,1],
        snare:   [0,0,0,1, 1,0,1,0, 0,1,0,1, 1,0,1,1],
        hihat:   [1,1,1,1, 1,1,1,1, 1,1,1,1, 1,1,1,1],
        openhat: [0,0,0,0, 0,0,1,0, 0,0,0,1, 0,1,0,0],
        tom:     [0,0,0,1, 0,0,0,0, 1,0,1,0, 0,0,0,1],
        clap:    [0,1,0,0, 1,0,0,1, 0,1,0,0, 1,0,1,0],
        ride:    [1,0,0,1, 0,1,0,0, 1,0,0,1, 0,1,0,0]
    )
];

// ============================================================================
// 5. BEAT ENGINE  (continuous drum-machine + bass + pad)
// ============================================================================

// Current beat parameters — read every step
~currentBPM        = 100;
~currentPatternIdx = 1;
~currentBassMidi   = 33;
~currentStep       = 0;

~padSynth = nil;
~beatTask = nil;

// Re-trigger the chord pad (called on harmony change)
~retriggerPad = {
    var harm = ~state[\harmony];
    var root = ~roots[harm] ?? 60;
    var freq = (root - 12).midicps;
    var q    = ~state[\winrate].asFloat;
    ~padSynth !? { ~padSynth.set(\gate, 0) };
    ~padSynth = Synth(\pad, [\freq, freq, \q, q, \amp, 0.06]);
};

~startBeat = {
    ~beatTask.stop;
    ~currentStep = 0;
    ~beatTask = Task({
        loop {
            var bpm     = ~currentBPM.max(40);
            var stepDur = (60 / bpm / 4).clip(0.04, 0.5);
            var pat     = ~patterns[~currentPatternIdx.clip(0, 3)];
            var step    = ~currentStep;
            var bassMidi = ~currentBassMidi;
            var bassPan  = 0;

            // Drum-kit triggers for this step
            if(pat[\kick][step]    == 1, { Synth(\kick,    [\amp, 0.85, \pitch, 45]) });
            if(pat[\snare][step]   == 1, { Synth(\snare,   [\amp, 0.55]) });
            if(pat[\hihat][step]   == 1, { Synth(\hihat,   [\amp, 0.22, \pan, 0.18]) });
            if(pat[\openhat][step] == 1, { Synth(\openhat, [\amp, 0.26, \pan, 0.25]) });
            if(pat[\tom][step]     == 1, {
                Synth(\tom, [\amp, 0.45,
                             \pitch, [55, 60, 65, 72].choose,
                             \pan, 0.4.rand2]);
            });
            if(pat[\clap][step]    == 1, { Synth(\clap, [\amp, 0.40, \pan, -0.15]) });
            if(pat[\ride][step]    == 1, { Synth(\ride, [\amp, 0.22, \pan, -0.25]) });

            // Bass note follows kick on strong beats (steps 0, 4, 8, 12)
            // pitch tracks ~currentBassMidi which is updated by material/harmony
            if((pat[\kick][step] == 1) and: { (step % 4) == 0 }, {
                Synth(\bassNote, [\pitch, bassMidi, \amp, 0.36, \dur, stepDur * 3.2]);
            });

            ~currentStep = (step + 1) % 16;
            stepDur.wait;
        }
    }).play;
};

// Read game state and update beat parameters (called on every state change)
~updateBeat = {
    var q        = ~state[\winrate].asFloat;
    var c        = ~state[\confidence].asFloat;
    var harm     = ~state[\harmony];
    var material = ~state[\materialPawns].asFloat;
    var rootMidi = ~roots[harm] ?? 60;

    // Linear tempo: 75 BPM (Q=0) ... 145 BPM (Q=1); small bump from C
    ~currentBPM = q.linlin(0, 1, 75, 145) + c.linlin(0, 8, -5, 10);
    ~currentBPM = ~currentBPM.clip(60, 180);

    // Pattern density: 0 (simple) ... 3 (explosive)
    ~currentPatternIdx = c.linlin(0, 8, 0, 3).round.asInteger.clip(0, 3);

    // Bass note: harmony root - 24 semitones (deep bass) + material offset
    //   Each pawn-unit advantage shifts bass up by 1 semitone
    //   Big advantage (queen up = +9) brightens entire low end
    ~currentBassMidi = (rootMidi - 24) + material.clip(-9, 9);

    ("BEAT  BPM=" ++ ~currentBPM.round(1)
     ++ "  pat=" ++ ~currentPatternIdx
     ++ "  bass=" ++ ~currentBassMidi.round(0.1)
     ++ "  | Q=" ++ q.round(0.01)
     ++ " C=" ++ c.round(0.01)
     ++ " mat=" ++ material.round(0.1)).postln;
};

// ============================================================================
// 6. JINGLE ENGINE  (3-5 note melodic arpeggio)
// ============================================================================

~lastTimbre = \jPiano;
~padDuckRoutine = nil;

// Brief pad-only duck during the arpeggio (drums keep grooving)
~duckPad = { |duckAmt=0.35, holdTime=0.6, fadeTime=1.4|
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

~fireJingle = {
    var intensity = ~state[\intensity].asFloat.clip(0, 1);
    var direction = ~state[\direction].asFloat;
    var capture   = ~state[\capture].asFloat;
    var noteCount, interval, scale, scaleName, rootMidi, timbre, ampBase;

    // Note count: 3 (small) → 5 (intense or big capture)
    noteCount = (3 + (intensity * 2)).round.asInteger.clip(3, 5);
    if(capture >= 5, { noteCount = 5 });

    // Speed: 0.10s/note (very fast, intense) ... 0.30s/note (calm)
    interval = 0.30 - (intensity * 0.20);

    // Scale: major / minor / pentatonic
    scale = case
        { direction > 0.5 }  { ~scaleDeg[\major] }
        { direction < -0.5 } { ~scaleDeg[\minor] }
        { true }             { ~scaleDeg[\pentatonic] };
    scaleName = case
        { direction > 0.5 }  { "MAJOR" }
        { direction < -0.5 } { "MINOR" }
        { true }             { "PENTA" };

    // Root: harmony root, one octave above middle, +12 for capture ≥ 5
    rootMidi = (~roots[~state[\harmony]] ?? 60) + 12;
    if(capture >= 5, { rootMidi = rootMidi + 12 });

    // Timbre: alternate piano / guitar
    timbre = if(~lastTimbre == \jPiano, { \jGuitar }, { \jPiano });
    ~lastTimbre = timbre;

    // Amplitude: scales with intensity + capture bonus
    ampBase = 0.45 + (intensity * 0.40) + ((capture / 9) * 0.20);
    ampBase = ampBase.clip(0.4, 1.0);

    (">>> JINGLE  " ++ timbre.asString.toUpper
     ++ "  notes=" ++ noteCount
     ++ "  intvl=" ++ interval.round(0.01) ++ "s"
     ++ "  dir=" ++ direction.round(1)
     ++ "  intensity=" ++ intensity.round(0.01)
     ++ "  capture=" ++ capture.round(1)
     ++ "  scale=" ++ scaleName).postln;

    // Quick pad duck only — let drums keep grooving
    ~duckPad.value(0.35, interval * noteCount, 1.4);

    noteCount.do { |i|
        // direction > 0 → ascending; direction < 0 → descending
        var stepIdx, degree, octBonus, freq, amp, pan;
        stepIdx = if(direction >= 0, { i }, { noteCount - 1 - i });
        degree  = scale[stepIdx % scale.size];

        // Huge capture: last note jumps an extra octave for emphasis
        octBonus = if((capture >= 8) and: { i == (noteCount - 1) }, { 12 }, { 0 });
        freq = (rootMidi + degree + octBonus).midicps;
        amp  = ampBase * (1.0 - (i * 0.04));
        pan  = ((i / (noteCount - 1).max(1)) - 0.5) * 1.4;

        SystemClock.sched(i * interval, {
            Synth(timbre, [\freq, freq, \amp, amp, \pan, pan]);
            nil
        });
    };
};

// ============================================================================
// 7. OSC RESPONDERS
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

OSCdef(\intensity, { |msg|
    ~state[\intensity] = msg[1].asFloat;
}, '/chess/intensity');

OSCdef(\direction, { |msg|
    ~state[\direction] = msg[1].asFloat;
}, '/chess/direction');

OSCdef(\capture, { |msg|
    ~state[\capture] = msg[1].asFloat;
}, '/chess/capture');

OSCdef(\jingleFire, { |msg|
    ("OSC /jingle      fire").postln;
    { ~fireJingle.value }.defer;
}, '/chess/jingle');

// ============================================================================
// 8. START
// ============================================================================
0.5.wait;
~retriggerPad.value;
~updateBeat.value;
~startBeat.value;

// Status display in Post Window every 10 s
~statusRoutine = Routine({
    loop {
        10.0.wait;
        ("--- STATE | Q=" ++ ~state[\winrate].round(0.001)
         ++ " C=" ++ ~state[\confidence].round(0.01)
         ++ " harm=" ++ ~state[\harmony]
         ++ " mat=" ++ ~state[\materialPawns].round(0.1)
         ++ " BPM=" ++ ~currentBPM.round(1)
         ++ " pat=" ++ ~currentPatternIdx).postln;
    }
}).play;

// Audio self-test: fires a sample jingle 2 seconds after boot.
SystemClock.sched(2.0, {
    "*** STARTUP TEST: firing sample jingle ***".postln;
    ~state[\intensity] = 0.7;
    ~state[\direction] = 1.0;
    ~state[\capture]   = 3.0;
    { ~fireJingle.value }.defer;
    nil
});

"".postln;
"=== Chess Music Engine v4 (BEAT EDITION) ready on port 57120. ===".postln;
"=== Background = drum machine | Jingles = melodic arpeggios.   ===".postln;
"=== Watch Post Window for OSC messages from Unity.             ===".postln;

}; // end waitForBoot
)
