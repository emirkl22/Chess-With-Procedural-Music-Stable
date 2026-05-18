# Adaptif Prosedürel Ses Sistemi — Teknik Döküman

**Proje:** Chess With Procedural Music  
**Öğrenci:** Muhammed Emir Kılıç — 2021555040  
**Dönem:** Bahar 2026  
**Motor:** Unity (C#) + SuperCollider 3.14

---

## İçindekiler

1. [Genel Mimari](#1-genel-mimari)
2. [Veri Akışı](#2-veri-akışı)
3. [Ses Parametreleri](#3-ses-parametreleri)
4. [Unity Tarafı](#4-unity-tarafı)
5. [OSC Protokolü](#5-osc-protokolü)
6. [SuperCollider Motoru](#6-supercollider-motoru)
   - 6.1 [Background Ses Havuzu](#61-background-ses-havuzu)
   - 6.2 [Parametre Eşleme Tablosu](#62-parametre-eşleme-tablosu)
   - 6.3 [Ses Havuzu Yönetimi](#63-ses-havuzu-yönetimi)
   - 6.4 [Melodi Motoru](#64-melodi-motoru)
   - 6.5 [Jingle / Feedback Sistemi](#65-jingle--feedback-sistemi)
   - 6.6 [Duck Fonksiyonu](#66-duck-fonksiyonu)
   - 6.7 [Mutasyon Rutini](#67-mutasyon-rutini)
7. [Gerçek Zamanlı Metrik Paneli](#7-gerçek-zamanlı-metrik-paneli)
8. [Oyun Fazı Tespiti](#8-oyun-fazı-tespiti)
9. [Kurulum ve Çalıştırma](#9-kurulum-ve-çalıştırma)

---

## 1. Genel Mimari

```
┌──────────────────────────────────────────────────────┐
│                       UNITY                          │
│                                                      │
│  ┌─────────────┐    ┌─────────────────────────────┐  │
│  │  MCTSAgent  │───▶│       AudioBridge           │  │
│  │  (AI oyun)  │    │  Q, ΔQ, C, Harmony, Jingle  │  │
│  └─────────────┘    └──────────────┬──────────────┘  │
│                                    │ her hamlede      │
│  ┌─────────────┐                   │                  │
│  │  UIManager  │◀──────────────────┘                  │
│  │ (metrik UI) │    ┌─────────────────────────────┐  │
│  └─────────────┘    │       OSCSender             │  │
│                     │  UDP paket oluştur & gönder  │  │
│                     └──────────────┬──────────────┘  │
└──────────────────────────────────── │ ───────────────┘
                                      │ UDP port 57120
                    ┌─────────────────▼──────────────────┐
                    │           SUPERCOLLIDER             │
                    │                                     │
                    │  ┌──────────────────────────────┐  │
                    │  │   Background Voice Pool       │  │
                    │  │   (8 SynthDef tipi, 1-3 aktif)│  │
                    │  └──────────────────────────────┘  │
                    │  ┌──────────────────────────────┐  │
                    │  │   Stokastik Melodi Motoru     │  │
                    │  │   (5 tını, düzensiz ritim)   │  │
                    │  └──────────────────────────────┘  │
                    │  ┌──────────────────────────────┐  │
                    │  │   Jingle Havuzu              │  │
                    │  │   (11 farklı desen)          │  │
                    │  └──────────────────────────────┘  │
                    └─────────────────────────────────────┘
```

Sistem tek yönlü çalışır: Unity AI hesaplamalar yapar → ses parametrelerini hesaplar → OSC üzerinden SuperCollider'a gönderir → SC ses üretir. Geriye herhangi bir veri dönmez.

---

## 2. Veri Akışı

Her AI hamlesi şu sırayla işlenir:

```
AI hamlesi tamamlanır
        │
        ▼
MCTSAgent.FindBestMove()
  → En çok ziyaret edilen düğümün kazanma oranı (ham Q)
  → Toplam simülasyon sayısı (N)
        │
        ▼
GameManager.RunAI() callback
  → ham delta hesapla: Δ_raw = winRate - prevWinRate
  → AudioBridge.OnMovePlayed(winRate, delta, visits) çağır
        │
        ▼
AudioBridge.OnMovePlayed()
  → Beş parametreyi hesapla (bkz. §3)
  → OSCSender üzerinden SuperCollider'a gönder
  → Public property'leri güncelle (UI için)
        │
        ▼
GameManager.RefreshMetricsPanel()
  → BoardEvaluator.Evaluate() (statik değerlendirme)
  → MoveGenerator.GetLegalMoves() (hareket sayısı)
  → UIManager.RefreshMetrics() çağır
```

---

## 3. Ses Parametreleri

AudioBridge beş parametre üretir ve her hamlede SuperCollider'a gönderir.

### 3.1 Q — Kazanma Oranı (Win Rate)

Ham MCTS kazanma oranı üzerine **üstel hareketli ortalama (EMA)** uygulanır:

```
Q_smooth[t] = (1 - α) × Q_smooth[t-1]  +  α × Q_raw[t]
```

- **α = 0.3** (SmoothingAlpha — Unity Inspector'da ayarlanabilir)
- **Başlangıç değeri:** 0.5 (nötr / tarafsız konum)
- **Aralık:** [0, 1] — 1.0 = Beyaz kesinlikle kazanıyor, 0.0 = Siyah kesinlikle kazanıyor

EMA'nın amacı: ani Q sıçramalarını yumuşatarak müzikteki geçişlerin doğal hissettirmesi.

### 3.2 ΔQ — Hamle Kalitesi Değişimi (Delta)

```
ΔQ = Q_smooth[t] - Q_smooth[t-1]
```

- **Aralık:** yaklaşık [-1, 1]
- Büyük pozitif ΔQ → iyi hamle (beyaz avantaj kazandı)
- Büyük negatif ΔQ → kötü hamle (beyaz avantaj kaybetti)
- **Jingle eşiği:** |ΔQ| > 0.05

### 3.3 C — Güven (Confidence)

```
C = log(N + 1)
```

- **N:** MCTS'in o hamle için yaptığı toplam simülasyon sayısı
- **Tipik aralık:** 0 – 7.5 (N = 0 → C = 0; N = 1500 → C ≈ 7.31)
- Yüksek C → AI pozisyonu iyi analiz etti, müzik daha yoğun ve odaklı
- Düşük C → Mat gibi kesin durumlar (N=1 veya N=2), müzik seyrek

**Not:** Zorlanmış mat aramada N=1 veya N=2 döner; normal MCTS'de N = `SimulationsPerMove` (varsayılan: 1500).

### 3.4 Harmony — Uyum Sınıfı

Q_smooth değerine göre üç kategoriden biri seçilir:

| Koşul | Harmony | Anlamı |
|---|---|---|
| Q > 0.60 | **Major** | Beyaz açıkça üstün |
| Q < 0.40 | **Minor** | Siyah açıkça üstün |
| 0.40 ≤ Q ≤ 0.60 | **Neutral** | Dengeli pozisyon |

Harmony değişimi SC'de tam ses havuzu yeniden oluşumunu tetikler (tüm sesler yeni tiple değiştirilir).

### 3.5 Jingle — Hamle Türü

ΔQ değerine göre üç kategoriden biri seçilir:

| Koşul | Jingle | Anlamı |
|---|---|---|
| ΔQ > +0.05 | **positive** | Güçlü / iyi hamle |
| ΔQ < −0.05 | **negative** | Zayıf / kötü hamle |
| −0.05 ≤ ΔQ ≤ +0.05 | **neutral** | Normal hamle |

---

## 4. Unity Tarafı

### 4.1 OSCSender.cs

Harici paket gerektirmeyen, `System.Net.Sockets.UdpClient` tabanlı minimal bir OSC uygulayıcısı.

- `Init(host, port)` — UDP soketi açar, hedef uç noktayı ayarlar
- `Send(address, float)` — float argümanlı OSC mesajı gönderir
- `Send(address, string)` — string argümanlı OSC mesajı gönderir
- OSC paket yapısı: adres string → virgülle tip etiketi (`,f` veya `,s`) → argüman değeri; her alan 4-bayt sınırına yuvarlama ile null-sonlandırılmış, float'lar big-endian IEEE 754

### 4.2 AudioBridge.cs

`MonoBehaviour` olarak GameManager'ın aynı GameObject'ine eklenir.

- `Awake()` → `OSCSender.Init(OSCHost, OSCPort)` (varsayılan: 127.0.0.1:57120)
- `OnMovePlayed(winRate, delta, visits)` → beş parametreyi hesapla, OSC gönder, public property'leri güncelle
- `OnDestroy()` → UDP soketini kapat

**Public property'ler** (UIManager için):

| Property | Tür | Açıklama |
|---|---|---|
| `SmoothQ` | float | Son EMA sonrası Q değeri |
| `SmoothDQ` | float | Son ΔQ değeri |
| `LastC` | float | Son güven değeri |
| `Harmony` | string | "Major" / "Neutral" / "Minor" |
| `Jingle` | string | "positive" / "neutral" / "negative" |

### 4.3 MCTSAgent.cs

Her hamle için iki aşamalı arama:

**Aşama 1 — Zorlanmış Mat Araması:**
Derinlik 1 ve 2 için mat-in-N araması yapılır. Mat bulunursa MCTS'e gerek kalmaz; `winRate = 1.0`, `visitCount = depth` döner.

**Aşama 2 — MCTS + Quiescence:**
- `SimulationsPerMove = 1500` simülasyon
- UCB1 seçim: $UCB = Q + \sqrt{2} \cdot \sqrt{\frac{\ln(N_{parent}+1)}{N_{child}+1}}$
- Her yaprak düğümde alfa-beta **quiescence search** (`QSearchDepth = 3`) — ufuk etkisini önler
- Statik değerlendirme: materyal + taş-kare tabloları + kral emniyeti + kral yakınlığı

---

## 5. OSC Protokolü

SuperCollider `57120` portunu UDP üzerinden dinler (SC varsayılanı).

| OSC Adresi | Tür | Aralık | Açıklama |
|---|---|---|---|
| `/chess/winrate` | float | [0, 1] | Düzleştirilmiş Q (EMA) |
| `/chess/delta` | float | ~[−1, 1] | Düzleştirilmiş ΔQ |
| `/chess/confidence` | float | [0, ~7.5] | log(N+1) |
| `/chess/harmony` | string | — | "Major" / "Neutral" / "Minor" |
| `/chess/jingle` | string | — | "positive" / "neutral" / "negative" |

Mesajlar **her AI hamlesi sonrasında** gönderilir (oyuncu hamlelerinde gönderilmez — oyuncu hamlesini AI yanıtlar, AI yanıtında mesaj gönderilir).

---

## 6. SuperCollider Motoru

`SuperCollider/chess_music.scd` — Ctrl+A → Ctrl+Enter ile çalıştırılır.

Sunucu başlatma ayarları:
- `numOutputBusChannels = 2` (stereo çıkış)
- `memSize = 131072` (128 MB RT belleği — granüler ve çok sesli işlem için)

### 6.1 Background Ses Havuzu

Sekiz farklı sentez yöntemi uygulanmıştır. Her biri `freq`, `q`, `c`, `amp`, `gate` kontrollerine sahiptir. `gate = 0` gönderildiğinde ASR zarfı serbest bırakılır ve synth kendiliğinden sonlanır.

---

#### `bgAdditive` — Toplamalı Drone

**Sentez:** Harmonik seri üzerinde 7 sinüs osilatörü. Her parsiyellin genliği `1/(n^1.15)` ile azalır.

**Q etkisi:** Düşük Q → inharmonisite katsayısı (0.28) devreye girer, her parsiyel hafifçe ıskalanır → titreşen, huzursuz tını. Yüksek Q → tam harmonik seri → temiz, sakin drone.

```
inharm = q.linlin(0, 1,  0.28,  0.0)
ratio_i = (i+1) × (1 + inharm × LFNoise)
```

**ASR zarfı:** Attack 5 s / Sustain / Release 6 s

---

#### `bgFMDrone` — FM Kaos Drone

**Sentez:** Frekans modülasyonu. `modulator → carrier` zinciri.

**Q etkisi:**

| Parametre | Q = 0 (kayıp) | Q = 1 (kazanç) |
|---|---|---|
| Mod Oranı | 0.33 | 2.50 |
| Mod İndeksi | 22.0 | 1.2 |

Düşük Q → çok yüksek modülasyon indeksi → metalik, gürültüye yakın tını (yan bantlar kalabalıklaşır). Yüksek Q → düşük indeks → yumuşak, ılık FM tını.

---

#### `bgNoise` — Filtreli Gürültü Yatağı

**Sentez:** `GrayNoise` iki BPF üzerinden paralel filtreleme.

**Q etkisi:** Bant merkezi frekansı Q ile `linexp(0,1, 0.35, 5.0)` ile ölçeklenir → düşük Q = alçak, karanlık perde; yüksek Q = parlak, tiz perde.  
**C etkisi:** Bant genişliği (rq) `linlin(0,8, 0.85, 0.08)` → düşük C = geniş, bulanık; yüksek C = dar, odaklı rezonans.

---

#### `bgComb` — Tarak Rezonatörü

**Sentez:** `CombC` (interpolasyonlu tarak filtre). Gürültü sinyali 4 farklı frekanstaki tarak filtreden geçirilir.

**Q + C etkisi:** Sönüm süresi = `c.linlin(0,8, 0.4, 5.0) + (q × 2.5)` → Yüksek Q ve C: uzun rezonans → çelik/metallofon etkisi. Düşük değerler: hızlı sönüm → metalik ping sesi.

---

#### `bgRingMod` — Halka Modülasyonu

**Sentez:** Testere dalgası + sinüs carrier, sinüs modülatör ile çarpılır → toplam ve fark frekansları (yan bantlar).

**Q etkisi:** Modülatör/carrier oranı `linexp(0,1, 0.18, 3.1)` → düşük Q = irrasyonel oran = tonal olmayan, yabancı tını; yüksek Q = basit oran = şeffaf shimmer.

---

#### `bgPulse` — PWM (Darbe Genişliği Modülasyonu)

**Sentez:** Darbe dalgası, LPF + yumuşak kırpma (tanh).

**Q etkisi:** Duty cycle `linlin(0,1, 0.04, 0.50)` → düşük Q = çok dar darbe = keskin, agresif vızıltı; yüksek Q = %50 duty = içi boş, açık tını.  
**C etkisi:** Filtre kesme frekansı `linlin(0,8, 250, 3500)` → yüksek C = daha parlak.

---

#### `bgWarp` — Dalga Katlama Distorsiyonu

**Sentez:** Üç sinüs toplamı, `fold(-1, 1)` işlemi ile katlanır → karmaşık inharmonik spektrum.

**Q etkisi:** Katlama miktarı `linlin(0,1, 6.5, 0.4)` → düşük Q = çok fazla katlama = büyük bozulma, zengin inharmonik içerik; yüksek Q = hafif katlama = ılık, hafifçe bozulmuş.

---

#### `bgCluster` — Detone Bulut

**Sentez:** 9 sinüs osilatörü, her biri bağımsız LFNoise2 ile detuned.

**Q etkisi:** Detuning aralığı `linlin(0,1, 0.28, 0.025)` → düşük Q = geniş yayılım = yoğun disonant bulut; yüksek Q = dar yayılım = unison shimmer.

---

### 6.2 Parametre Eşleme Tablosu

Aşağıdaki tablo, Q ve C değerlerinin her SynthDef'deki temel ses parametrelerini nasıl etkilediğini özetler.

| SynthDef | Q → (düşük) | Q → (yüksek) | C → (düşük) | C → (yüksek) |
|---|---|---|---|---|
| bgAdditive | İnharmonik, titreşen | Saf harmonik seri | — | — |
| bgFMDrone | Gürültüye yakın (idx=22) | Yumuşak FM (idx=1.2) | — | — |
| bgNoise | Karanlık, alçak perde | Parlak, tiz perde | Geniş band | Dar rezonans |
| bgComb | Hızlı sönüm (ping) | Uzun çelik resonans | Kısa decay | Uzun decay |
| bgRingMod | Yabancı, atonale yakın | Şeffaf shimmer | — | — |
| bgPulse | Dar darbe, agresif buzz | %50 duty, açık | Alçak kesme | Parlak kesme |
| bgWarp | Aşırı distorsiyon | Hafif, ılık | Dar LPF | Geniş LPF |
| bgCluster | Disonant bulut | Unison shimmer | — | — |

### 6.3 Ses Havuzu Yönetimi

#### Aktif ses sayısı

```
n = ceil(C / 2.6).clip(1, 3)
```

| C aralığı | Aktif ses sayısı |
|---|---|
| 0.0 – 2.6 | 1 |
| 2.6 – 5.2 | 2 |
| 5.2 – 7.5+ | 3 |

#### Slot amplitüd hedefleri

| Slot | Amplitüd |
|---|---|
| 0 (birincil) | 0.15 |
| 1 (ikincil) | 0.10 |
| 2 (üçüncül) | 0.07 |

#### Havuz seçimi (Q bias)

```
Q < 0.38  →  harshPool önce:  [bgFMDrone, bgCluster, bgWarp, bgPulse, bgNoise, ...]
Q > 0.62  →  smoothPool önce: [bgAdditive, bgComb, bgRingMod, bgNoise, ...]
0.38–0.62 →  tam karışık pool (tamamen rastgele)
```

Her havuz kendi içinde `scramble` ile karıştırılır; slot i için `pool[i]` seçilir.

#### Yeniden oluşturma tetikleyicileri

| Tetikleyici | Eylem |
|---|---|
| `/chess/harmony` değişir | `~respawnAll` — tüm slotlar yeni tiplerle yeniden doldurulur |
| `/chess/confidence` ses sayısını değiştirir | `~respawnAll` |
| `/chess/winrate` veya `/chess/confidence` (sayı sabit) | `~morphVoices` — sadece parametre güncellenir |
| Mutasyon rutini (8-20 s) | `~mutateOneVoice` — tek slot değiştirilir |

#### Morph vs. Respawn

- **Morph:** Mevcut Synth nesneleri `.set(\freq, ..., \q, ..., \c, ...)` ile anlık olarak güncellenir. Ses kesilmez.
- **Respawn:** Eski Synth'e `gate = 0` gönderilir (ASR release başlar), yeni bir Synth hemen başlatılır. İki ses kısa süre üst üste çalar (crossfade etkisi).

### 6.4 Melodi Motoru

Stokastik bir `Task` döngüsü, her iterasyonda kararlar alır.

#### Tempo

```
tempo = (C / 5.2).clip(0.25, 1.9)  ×  (1 + |Q - 0.5| × 0.9)
dur   = (1 / tempo).clip(0.10, 1.3)  saniye/nota
```

Yüksek C ve pozisyon dengesizliği → hızlı, yoğun melodi.  
Düşük C ve denge → yavaş, seyrek.

#### Dinlenme olasılığı (rest)

```
restP = C.linlin(0, 8,  0.50,  0.07)
```

Düşük C → her notanın %50 olasılıkla atlandığı sessiz, seyrek doku.  
Yüksek C → sessizlik nadirdir (%7).

#### Nota seçimi

Aktif gamdan (`~scales[harmony]`) bir derece seçilir. Q değeri üst/alt oktavı ve dereceleri etkiler:

| Q | Derece aralığı | Oktav kaydırma |
|---|---|---|
| > 0.62 | Üst dereceler (3–7) | +12 olasılığı %72 |
| < 0.38 | Alt dereceler (0–3) | −12 olasılığı %75 |
| Nötr | Tam aralık | ±0 ağırlıklı |

#### Akor patlaması

`C > 4.8` iken her notalarda %15 olasılıkla 2–3 nota aynı anda çalar.

#### Tını rotasyonu

Beş tını tipi (`mPluck`, `mBell`, `mFM`, `mBow`, `mClick`) sırayla veya %28 olasılıkla rastgele değiştirilir.

| Tını | Sentez yöntemi | Karakter |
|---|---|---|
| mPluck | Karplus-Strong fiziksel modelleme | Gitar benzeri koparma |
| mBell | Klank rezonator bankası | Çan harmonikleri |
| mFM | Frekans modülasyonu | Parlak FM nota |
| mBow | Additive + gürültü jitter | Yayla çalgı sürme |
| mClick | Sinüs + filtrelenmiş gürültü | Darbe / tahta tıklama |

### 6.5 Jingle / Feedback Sistemi

Her hamle sonrası bir jingle tipi belirlenir ve havuzdan bir desen rastgele seçilir.

#### Anti-tekrarlama

```
~lastJingleIdx[sym] = önceki desen indeksi
→ mevcut seçimde bu indeks hariç tutulur
→ art arda aynı desen asla çalmaz
```

#### Positive havuzu (4 desen)

| # | İsim | İçerik |
|---|---|---|
| P-0 | Karplus arpeji + FM stab | Yükselen 8 notalu Karplus arpeji + eş zamanlı 3 FM akoru |
| P-1 | Çan akor patlaması | 6 çan aynı anda, geniş panlama, sonrasında iki katmanlı sine spray |
| P-2 | FM stab kaskadı | 7 nota, artan FM indeksiyle hızlı kaskad (55 ms aralık) |
| P-3 | Ritmik gürültü patlaması | Kapılı gürültü burst + gecikmeli çan akor + sine spray |

#### Negative havuzu (4 desen)

| # | İsim | İçerik |
|---|---|---|
| N-0 | Thud + çökme kaskadı | Derin bass darbe + 6 inen FM çığlığı |
| N-1 | Cluster ezme + bass | Bass darbe, iki cluster smash, gecikmeli FM çığlığı |
| N-2 | Üçlü bass + inen çığlık | 3 farklı perdede bass darbe + 3 FM çığlığı |
| N-3 | Kargaşa duvarı | Subwoofer + 2 cluster eş zamanlı + 4 FM çığlığı (180 ms aralık) |

#### Neutral havuzu (3 desen)

| # | İsim | İçerik |
|---|---|---|
| Nt-0 | Çan üçlüsü + blip | 3 Klank çan (200 ms arayla) + 3 blip akor |
| Nt-1 | Derin çan + chime kaskadı | Tek Klank çan + 5 yükselen FM chime |
| Nt-2 | Blip kümesi + chime | 5 blip (80 ms arayla) + merkezi FM chime |

#### Jingle synth bileşenleri

| SynthDef | Kullanıldığı havuz | Sentez | Karakter |
|---|---|---|---|
| jKarplusArp | Positive | Karplus-Strong (uzun decay) | Parlak, sustain'li koparma |
| jFMBurst | Positive, Negative | FM perc (ayarlanabilir indeks) | Parlak veya saldırgan FM nota |
| jSineSpray | Positive | 12 detuned sinüs, uzun reverb | Parlak shimmer bulutu |
| jBellChord | Positive, Neutral | Klank rezonator bankası | Zengin çan |
| jRhythmBurst | Positive | Kapılı BPF gürültü | Ritmik enerji patlaması |
| jThudDeep | Negative | FM bas darbe + gürültü transient | Ağır, derin çarma |
| jFMScream | Negative | Kayan FM + tanh distorsiyon | Alçalan çığlık |
| jClusterSmash | Negative | 8 Saw akoru + tanh kırpma | Sert disonant ezme |
| jBassImpact | Negative | Kayan sinüs + click transient | Subwoofer vuruşu |
| jKlank3 | Neutral | Klank rezonator (5 bant) | Klasik çan |
| jBlip | Neutral | Kısa sinüs perc | Hızlı, temiz bildirim |
| jChime | Neutral | Kısa FM, uzun decay | Sakin chime |

### 6.6 Duck Fonksiyonu

Jingle çalarken background ses düzeyi geçici olarak düşürülür:

```
1. Jingle tetiklenir
2. Tüm aktif background ses amplitüdleri → hedef × 0.18 (yaklaşık %18)
3. holdTime = 1.5 s bekle
4. fadeTime = 2.4 s içinde 30 adımda smooth fade-back
5. Amplitüdler yeniden slot hedeflerine ulaşır (0.15 / 0.10 / 0.07)
```

### 6.7 Mutasyon Rutini

SuperCollider motoru, OSC mesajı gelmeksizin müziği çeşitlendirmek için arka planda çalışır:

```
loop:
    wait(rrand(8.0, 20.0))   ← rastgele 8-20 saniye
    aktif slotlardan birini rastgele seç
    mevcut SynthDef'ten farklı bir tipi rastgele seç
    ~spawnVoice(slot, yeniTip)
```

Bu sayede oyuncu uzun süre düşünse bile müzik değişmeye devam eder.

---

## 7. Gerçek Zamanlı Metrik Paneli

Unity sahnesinde tahtanın sağında (x ≈ 9.8) 14 satırlık TextMesh paneli görüntülenir. Her satırın rengi değere göre dinamik olarak güncellenir.

| Satır | İçerik | Renk mantığı |
|---|---|---|
| Q | Win rate + `[######--]` bar | Yeşil (>0.62) · Sarı (denge) · Kırmızı (<0.38) |
| C | Confidence + bar | Mavi (C=0) → Sarı (C=7.5) |
| Harmony | MAJOR / NEUTRAL / MINOR | Yeşil · Gri · Mor |
| dQ | Δ değeri + [+]/[-]/[=] etiketi + jingle adı | Yeşil · Gri · Kırmızı |
| Material | Statik tahta değerlendirmesi (centipawn) | Yeşil (+) · Gri (±25) · Kırmızı (−) |
| Phase | OPENING / MIDGAME / ENDGAME | Mavi · Sarı · Mor |
| Mobility | Sıradaki tarafın yasal hamle sayısı | Yeşil (≥25) · Beyaz · Turuncu (≤10) |
| Sims | MCTS simülasyon sayısı | Açık mavi |
| Check | Şah durumu | Kırmızı (şah var) · Koyu gri (yok) |
| Ply | Yarım hamle sayacı | Gri |

Panel, oyuncunun hamlesi ve AI hamlesi sonrasında olmak üzere her yarım hamlede yenilenir. Oyuncu hamlesi sonrası Q/Harmony/Jingle değerleri önceki AI hamlesine ait değerleri göstermeye devam eder (yeni AI hamlesi gelene kadar).

---

## 8. Oyun Fazı Tespiti

```csharp
string ComputePhase():
    tahtadaki taş sayısı = pieces
    vezir var mı?         = hasQueens

    ply < 10  &&  pieces >= 28  →  "OPENING"
    !hasQueens  ||  pieces <= 14 →  "ENDGAME"
    diğer                        →  "MIDGAME"
```

Bu değer şu an yalnızca UI panelinde gösterilmektedir. İlerideki bir geliştirmede `/chess/phase` OSC mesajı eklenerek SuperCollider'ın da faza göre ses seçimi yapması sağlanabilir.

---

## 9. Kurulum ve Çalıştırma

### Unity

1. Unity Editor'da `Assets/Scenes/SampleScene.unity` sahnesini aç.
2. Sahnede `GameManager` adlı GameObject'te `GameManager` bileşeninin `Piece Database` alanına `Assets/PieceDatabase.asset` sürükle.
3. Play butonuna bas.

### SuperCollider

1. `SuperCollider/chess_music.scd` dosyasını SuperCollider IDE'de aç.
2. **Ctrl+A** ile tümünü seç, **Ctrl+Enter** ile değerlendir.
3. Sunucu başlayıp SynthDef'ler yüklendikten sonra Post Window'da şu satırı görmelisin:
   ```
   === Chess Music Engine v3 (CHAOS EDITION) ready on port 57120. ===
   ```
4. Unity'de bir hamle yap. Post Window'da şu türde çıktılar görünmeli:
   ```
   + [0] bgFMDrone
   + [1] bgCluster
   Harmony -> Minor
   Jingle -> negative  pattern#2
   ~ mutate [1] bgCluster -> bgWarp
   ```

### Bağlantı testi

SuperCollider, Unity'nin gönderdiği UDP paketlerini alıp almadığını doğrulamak için Post Window'u izle. `[AudioBridge]` Unity Console çıktısı ile SC Post Window çıktıları aynı anda görünüyorsa bağlantı sağdır.

---

*Tüm kaynak dosyalar `main` branch'inde, commit `a368104` itibarıyla günceldir.*
