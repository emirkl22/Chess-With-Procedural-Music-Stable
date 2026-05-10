# SuperCollider Entegrasyon Planı
## Adaptive Procedural Audio System Based on Chess AI Evaluation

---

## 1. Genel Mimari

```
┌─────────────────────────────────────────────────────────────┐
│                        UNITY (C#)                           │
│                                                             │
│  BoardState → MCTSAgent → AudioBridge                       │
│                                │                            │
│                         OSC mesajları                       │
│                         (UDP, port 57120)                   │
└────────────────────────────────┼────────────────────────────┘
                                 │
                          UDP/OSC protokolü
                                 │
┌────────────────────────────────▼────────────────────────────┐
│                     SUPERCOLLIDER                           │
│                                                             │
│  OSCdef handlers → Synthdef parametreleri güncelle          │
│                                                             │
│  Layer 1: Sürekli arka plan müziği (Q değerinden)          │
│  Layer 2: Hamle-tetikli jingle (ΔQ değerinden)             │
└─────────────────────────────────────────────────────────────┘
```

---

## 2. OSC Mesaj Tablosu

Unity'nin `AudioBridge.cs` dosyası her AI hamlesi sonrasında şu mesajları gönderir:

| OSC Adresi           | Tip     | Değer Aralığı | Açıklama                                      |
|----------------------|---------|---------------|-----------------------------------------------|
| `/chess/winrate`     | float   | [0.0 – 1.0]   | MCTS kazanma oranı Q = W/N                   |
| `/chess/delta`       | float   | [-1.0 – 1.0]  | ΔQ = Q_sonra − Q_önce                        |
| `/chess/confidence`  | float   | [0.0 – ~5.3]  | C = log(N+1), N = ziyaret sayısı             |
| `/chess/harmony`     | string  | Major/Neutral/Minor | Q > 0.6 → Major, Q < 0.4 → Minor       |
| `/chess/jingle`      | string  | positive/neutral/negative | ΔQ > +0.05 → positive, < −0.05 → negative |

### Matematiksel Tanımlar (Progress Report 2'den)

```
Q    = W / N                         (kazanma oranı)
ΔQ   = Q_after − Q_before            (hamle kalitesi)
C    = log(N + 1)                    (güven / netlik)

Harmony: { Major    → Q > 0.6
           Neutral  → 0.4 ≤ Q ≤ 0.6
           Minor    → Q < 0.4       }
```

---

## 3. Unity Tarafı: OSC Paketi Ekleme

### 3.1 Önerilen Paket: extOSC

1. Unity Editor → **Window → Package Manager** → **Add package from git URL**:
   ```
   https://github.com/Iam1337/extOSC.git
   ```

2. `AudioBridge.cs` dosyasını aç. Yorum satırlarındaki `OSCSender.Send(...)` çağrılarını
   extOSC ile değiştir:

```csharp
// AudioBridge.cs içine ekle (Awake veya Start'ta):
using extOSC;

OSCTransmitter _transmitter;

void Awake()
{
    _transmitter = gameObject.AddComponent<OSCTransmitter>();
    _transmitter.RemoteHost = OSCHost;   // "127.0.0.1"
    _transmitter.RemotePort = OSCPort;   // 57120
}

// OnMovePlayed içindeki OSC stub'larını şunlarla değiştir:
void SendFloat(string address, float value)
{
    var msg = new OSCMessage(address);
    msg.AddValue(OSCValue.Float(value));
    _transmitter.Send(msg);
}

void SendString(string address, string value)
{
    var msg = new OSCMessage(address);
    msg.AddValue(OSCValue.String(value));
    _transmitter.Send(msg);
}
```

### 3.2 Alternatif: UnityOSC (daha minimal)

```
https://github.com/jorgegarcia/UnityOSC.git
```

---

## 4. SuperCollider Tarafı: Alım Yapısı

### 4.1 Boilerplate — OSC Dinleyici

```supercollider
// ============================================================
// Chess Procedural Audio — SuperCollider Boilerplate
// Dosya: chess_audio.scd
// ============================================================

s.boot;

// Genel state değişkenleri
~winRate   = 0.5;
~delta     = 0.0;
~confidence = 1.0;
~harmony   = "Neutral";
~jingle    = "neutral";

// ---- OSC dinleyiciler ----

OSCdef(\chessWinrate, { |msg|
    ~winRate = msg[1].asFloat;
    "winrate: %".format(~winRate).postln;
    ~updateBackground.value;   // arka plan müziğini güncelle
}, '/chess/winrate');

OSCdef(\chessDelta, { |msg|
    ~delta = msg[1].asFloat;
    "delta: %".format(~delta).postln;
}, '/chess/delta');

OSCdef(\chessConfidence, { |msg|
    ~confidence = msg[1].asFloat;
    "confidence: %".format(~confidence).postln;
}, '/chess/confidence');

OSCdef(\chessHarmony, { |msg|
    ~harmony = msg[1].asString;
    "harmony: %".format(~harmony).postln;
    ~updateBackground.value;
}, '/chess/harmony');

OSCdef(\chessJingle, { |msg|
    ~jingle = msg[1].asString;
    "jingle: %".format(~jingle).postln;
    ~triggerJingle.value;      // hamle jinglini tetikle
}, '/chess/jingle');
```

---

## 5. İki Ses Katmanı

### Katman 1: Sürekli Arka Plan Müziği

Bu katman Q değerine göre sürekli değişir. Parametreler:

| Müzik Parametresi     | Q küçük (Black kazanıyor) | Q = 0.5 (eşit) | Q büyük (White kazanıyor) |
|-----------------------|--------------------------|-----------------|--------------------------|
| Skala modu            | Minor / Dorian           | Neutral (Aeolian)| Major / Lydian           |
| Tempo (BPM)           | 60 – 75 (yavaş, ağır)   | 85               | 100 – 120 (hızlı, enerjik)|
| Harmonik yoğunluk     | Az, seyrek               | Orta             | Yoğun, dolu              |
| Parlak/karanlık       | Karanlık tını (bass)     | Orta             | Parlak tını (treble)     |
| Ambiyans yoğunluğu    | Geniş reverb             | Orta reverb      | Kuru, yakın              |

```supercollider
// Arka plan müziği güncelleme fonksiyonu (skel — senin müziğinle doldur)
~updateBackground = {
    var q   = ~winRate;
    var tempo = q.linlin(0.0, 1.0, 60, 120);   // BPM interpolasyon
    var mode  = if(q > 0.6, "major", if(q < 0.4, "minor", "neutral"));

    // Burada kendi Synth'lerine parametreleri gönder, örneğin:
    // ~bgSynth.set(\cutoff,  q.linexp(0.0, 1.0, 200, 4000));
    // ~bgSynth.set(\reverb,  q.linlin(0.0, 1.0, 0.8, 0.1));
    // ~bgSynth.set(\tempo,   tempo);

    "Background updated: Q=% mode=% tempo=%".format(q, mode, tempo).postln;
};
```

### Katman 2: Hamle-Tetikli Jinglelar (1–3 saniye)

ΔQ değerine göre üç farklı kısa motif:

| Jingle Tipi | ΔQ Koşulu    | Müzikal Karakteristik              |
|-------------|--------------|-------------------------------------|
| `positive`  | ΔQ > +0.05   | Major arpeji, parlak, yükselen      |
| `neutral`   | -0.05 ≤ ΔQ ≤ +0.05 | Kararlı harmonik ton          |
| `negative`  | ΔQ < -0.05   | Minor / disonant motif, alçalan     |

```supercollider
~triggerJingle = {
    var jType = ~jingle;
    var mag   = ~delta.abs;  // şiddet: büyük ΔQ = daha dramatik jingle

    case
    { jType == "positive" } {
        // Buraya major arpeji / fanfare ekle
        // Örnek: Synth(\jinglePositive, [\intensity, mag]);
        "Jingle: POSITIVE (mag=%)".format(mag).postln;
    }
    { jType == "negative" } {
        // Buraya minor / disonant motif ekle
        // Örnek: Synth(\jingleNegative, [\intensity, mag]);
        "Jingle: NEGATIVE (mag=%)".format(mag).postln;
    }
    {
        // Neutral
        // Örnek: Synth(\jingleNeutral, [\intensity, mag]);
        "Jingle: NEUTRAL".postln;
    };
};
```

---

## 6. Adım Adım Entegrasyon Kılavuzu

### Adım 1 — SuperCollider'ı Hazırla
1. SuperCollider'ı aç, `chess_audio.scd` dosyasını yükle.
2. `s.boot` ile server'ı başlat.
3. OSC dinleyicileri çalıştır (Ctrl+Enter ile tüm dosyayı eval et).
4. SuperCollider'ın varsayılan OSC portu: **57120** — Unity ile eşleşmeli.

### Adım 2 — Unity'ye OSC Paketi Ekle
1. extOSC veya UnityOSC'yi Package Manager'dan ekle.
2. `AudioBridge.cs` içindeki OSC stub yorum satırlarını gerçek göndericilerle değiştir.
3. `OSCHost = "127.0.0.1"` ve `OSCPort = 57120` değerlerini kontrol et.

### Adım 3 — Bağlantıyı Test Et
1. Unity'yi Play moduna al.
2. Bir hamle yap → AI hamle yapsın.
3. SuperCollider Post Window'da şunu görmelisin:
   ```
   winrate: 0.5234
   delta: -0.0123
   confidence: 5.2983
   harmony: Neutral
   jingle: neutral
   ```

### Adım 4 — Müziği Geliştir
1. `~updateBackground` fonksiyonu içine kendi Synth tanımlarını yaz.
2. `~triggerJingle` fonksiyonu içine 3 jingle Synth'i ekle.
3. Parametreleri Q ve ΔQ değerlerine bağla.

### Adım 5 — İnce Ayar
- Harmony geçişlerini pürüzsüz yapmak için interpolasyon kullan (`XFade2`, `Lag`).
- Jingleların arka plan müziğiyle çakışmaması için volume ducking ekle.
- `~confidence` (C) değerini ses netliğine bağla (yüksek C = daha stabil/net ses).

---

## 7. OSC Parametrelerinin Müzikal Yorumu

### Win Rate Q → Arka Plan Harmoni
```
Q = 0.0  →  Derin minor, karanlık (siyah kesin kazanıyor)
Q = 0.4  →  Hafif minor (siyah üstün)
Q = 0.5  →  Dengeli, nötr
Q = 0.6  →  Hafif major (beyaz üstün)
Q = 1.0  →  Parlak major, triumfal (beyaz kesin kazanıyor)
```

### Delta ΔQ → Jingle Şiddeti
```
ΔQ = -0.3  →  Büyük hata: güçlü disonant fanfare
ΔQ = -0.05 →  Küçük dezavantaj: hafif minor ton
ΔQ = 0.0   →  Nötr hamle: kararlı harmonik ton
ΔQ = +0.05 →  Hafif iyi hamle: minor'dan major'a geçiş
ΔQ = +0.3  →  Mükemmel hamle: güçlü major arpeji
```

### Confidence C → Ses Netliği
```
C düşük (az simülasyon, belirsiz) → Geniş reverb, bulanık timbre
C yüksek (çok simülasyon, emin)  → Kuru, net, stabil ses
```

---

## 8. Referans OSC Test Komutu (SuperCollider'dan Unity'yi Simüle Etmek)

Unity olmadan SC tarafını test etmek için SC'den bu mesajları kendin gönderebilirsin:

```supercollider
// Test mesajları — Unity olmadan SC tarafını test etmek için
n = NetAddr("127.0.0.1", 57120);

// Bir "iyi hamle" simüle et:
n.sendMsg('/chess/winrate',    0.72);
n.sendMsg('/chess/delta',      0.15);
n.sendMsg('/chess/confidence', 5.10);
n.sendMsg('/chess/harmony',    "Major");
n.sendMsg('/chess/jingle',     "positive");

// Bir "kötü hamle" simüle et:
n.sendMsg('/chess/winrate',    0.31);
n.sendMsg('/chess/delta',     -0.22);
n.sendMsg('/chess/confidence', 4.80);
n.sendMsg('/chess/harmony',    "Minor");
n.sendMsg('/chess/jingle',     "negative");
```

---

## 9. Önerilen Dosya Yapısı (SC Tarafı)

```
supercollider/
  chess_audio.scd          ← Ana dosya: OSC handlers ve state
  synths/
    background_synth.scd   ← Sürekli arka plan Synth tanımı
    jingle_positive.scd    ← Pozitif hamle jingle Synth
    jingle_neutral.scd     ← Nötr hamle jingle Synth
    jingle_negative.scd    ← Negatif hamle jingle Synth
  utils/
    scale_utils.scd        ← Skala/mod yardımcı fonksiyonlar
    osc_debug.scd          ← Test mesajları
```

---

## 10. Mevcut Proje Durumu

| Bileşen                           | Durum        |
|-----------------------------------|--------------|
| Chess engine (8x8 board, moves)   | ✅ Tamamlandı |
| MCTS AI (200 simülasyon/hamle)    | ✅ Tamamlandı |
| Board & piece rendering (Unity)   | ✅ Tamamlandı |
| Move selection & highlighting     | ✅ Tamamlandı |
| AudioBridge (parametre hesaplama) | ✅ Tamamlandı (OSC stub) |
| OSC gönderici (extOSC entegrasyonu)| ⏳ Sana kaldı |
| SC arka plan müzik sentezi        | ⏳ Sana kaldı |
| SC jingle sentezi (3 tip)         | ⏳ Sana kaldı |

---

*Doküman: Muhammed Emir Kılıç — 2021555040 — Bahar 2026*
