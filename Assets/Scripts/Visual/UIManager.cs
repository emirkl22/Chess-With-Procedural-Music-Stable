using UnityEngine;

namespace Chess
{
    /// <summary>
    /// In-world TextMesh UI -- two zones:
    ///   * Top bar    (above board, y > 8)   : turn indicator + status line
    ///   * Right panel (right of board, x>8.8): real-time audio metrics panel
    ///
    /// All strings are ASCII-safe (Unity default Arial font).
    /// Per-row colours are updated every half-move via RefreshMetrics().
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        // ---- Top bar -----------------------------------------------------
        TextMesh _turnLabel;
        TextMesh _statusLabel;

        // ---- Right-side metrics panel ------------------------------------
        TextMesh _pTitle;       // "[ AUDIO ENGINE ]"
        TextMesh _pSecBg;       // "-- BACKGROUND MUSIC --"
        TextMesh _pQ;           // "Q    0.730   [######--]"
        TextMesh _pC;           // "C    3.81    [####----]"
        TextMesh _pHarmony;     // "Harmony  MAJOR"
        TextMesh _pSecMove;     // "-- LAST MOVE --"
        TextMesh _pDQ;          // "dQ  +0.086  POSITIVE [+]"
        TextMesh _pSecState;    // "-- BOARD STATE --"
        TextMesh _pMaterial;    // "Material  +120 cp"
        TextMesh _pPhase;       // "Phase    OPENING"
        TextMesh _pLegal;       // "Mobility  28 moves"
        TextMesh _pSims;        // "Sims     1500"
        TextMesh _pCheck;       // "Check    ok"  OR  "Check    (!)"
        TextMesh _pPly;         // "Ply      #12"

        // ---- Controls panel (bottom right) -------------------------------
        TextMesh _pSecCtrl;     // "-- CONTROLS --"
        TextMesh _pCtrlAnim;    // "Anim    0.30s"
        TextMesh _pCtrlDiff;    // "AI      1500"
        TextMesh _pCtrlHist;    // "Move    5/12"
        TextMesh _pCtrlHelp;    // "Keys: <- -> [ ] , . R"

        // ---- Click-through callbacks (wired by GameManager.SetupComponents) ---
        public System.Action OnAnimSlower;
        public System.Action OnAnimFaster;
        public System.Action OnSimsDown;
        public System.Action OnSimsUp;
        public System.Action OnHistoryBack;
        public System.Action OnHistoryForward;
        public System.Action OnReset;

        // Cached unit sprite for button backgrounds
        Sprite _unitSprite;

        // Panel left-edge x  (board tiles occupy x in [−0.5 , 7.5])
        const float PX    = 9.8f;
        const float SCALE = 0.082f;

        // =================================================================
        // Build
        // =================================================================
        public void Build()
        {
            // Top bar (centered above the board)
            _turnLabel   = MkLabel("TurnLabel",   new Vector3(4f, 8.75f, 0f), 22, Color.white);
            _statusLabel = MkLabel("StatusLabel", new Vector3(4f, 9.25f, 0f), 18, Color.yellow);

            // Panel rows, stepping y downward
            Color gray   = new Color(0.48f, 0.48f, 0.48f);
            Color accent = new Color(0.85f, 0.82f, 1.00f);

            float y = 8.25f;
            _pTitle    = PRow("PTitle",    ref y, 0.00f, 16, accent);
            _pSecBg    = PRow("PSecBg",    ref y, 0.40f, 12, gray);
            _pQ        = PRow("PQ",        ref y, 0.34f, 14, Color.white);
            _pC        = PRow("PC",        ref y, 0.37f, 14, Color.white);
            _pHarmony  = PRow("PHarmony",  ref y, 0.37f, 14, Color.white);
            _pSecMove  = PRow("PSecMove",  ref y, 0.44f, 12, gray);
            _pDQ       = PRow("PDQ",       ref y, 0.34f, 14, Color.white);
            _pSecState = PRow("PSecState", ref y, 0.44f, 12, gray);
            _pMaterial = PRow("PMaterial", ref y, 0.34f, 14, Color.white);
            _pPhase    = PRow("PPhase",    ref y, 0.37f, 14, Color.white);
            _pLegal    = PRow("PLegal",    ref y, 0.37f, 14, Color.white);
            _pSims     = PRow("PSims",     ref y, 0.37f, 14, Color.white);
            _pCheck    = PRow("PCheck",    ref y, 0.37f, 14, Color.white);
            _pPly      = PRow("PPly",      ref y, 0.37f, 14, Color.white);

            // Controls panel
            _pSecCtrl  = PRow("PSecCtrl",  ref y, 0.46f, 12, gray);
            _pCtrlAnim = PRow("PCtrlAnim", ref y, 0.34f, 13, new Color(0.80f, 0.92f, 1f));
            _pCtrlDiff = PRow("PCtrlDiff", ref y, 0.36f, 13, new Color(0.80f, 0.92f, 1f));
            _pCtrlHist = PRow("PCtrlHist", ref y, 0.36f, 13, new Color(0.80f, 0.92f, 1f));
            _pCtrlHelp = PRow("PCtrlHelp", ref y, 0.42f, 10, gray);

            // Static header strings
            _pTitle.text    = "[ AUDIO ENGINE ]";
            _pSecBg.text    = "-- BACKGROUND MUSIC --";
            _pSecMove.text  = "-- LAST MOVE --";
            _pSecState.text = "-- BOARD STATE --";
            _pSecCtrl.text  = "-- CONTROLS --";
            _pCtrlHelp.text = "Click buttons or use keys";

            // Build the clickable buttons next to each control row
            BuildControlButtons();

            // Neutral initial values (shown before first AI move)
            RefreshMetrics(0.5f, 0f, 0f, "Neutral", "neutral", 0, 20, false, 0, "OPENING", 0);
            RefreshControls(0.30f, 1500, 0, 0);
        }

        // -----------------------------------------------------------------
        // Clickable control buttons (world-space, raycast by SelectionManager)
        // -----------------------------------------------------------------
        void BuildControlButtons()
        {
            // X positions for [<] [>] [R] buttons (must be right of the value text)
            const float xMinus = 11.75f;
            const float xPlus  = 12.30f;
            const float xExtra = 12.85f;

            float yAnim = _pCtrlAnim.transform.position.y;
            float yDiff = _pCtrlDiff.transform.position.y;
            float yHist = _pCtrlHist.transform.position.y;

            MakeButton("<", new Vector3(xMinus, yAnim, 0f), () => OnAnimSlower?.Invoke());
            MakeButton(">", new Vector3(xPlus,  yAnim, 0f), () => OnAnimFaster?.Invoke());

            MakeButton("<", new Vector3(xMinus, yDiff, 0f), () => OnSimsDown?.Invoke());
            MakeButton(">", new Vector3(xPlus,  yDiff, 0f), () => OnSimsUp?.Invoke());

            MakeButton("<", new Vector3(xMinus, yHist, 0f), () => OnHistoryBack?.Invoke());
            MakeButton(">", new Vector3(xPlus,  yHist, 0f), () => OnHistoryForward?.Invoke());
            MakeButton("R", new Vector3(xExtra, yHist, 0f), () => OnReset?.Invoke());
        }

        UIButton MakeButton(string label, Vector3 pos, System.Action onClick)
        {
            // Background quad
            var bg = new GameObject($"Btn_{label}");
            bg.transform.SetParent(transform, false);
            bg.transform.position   = pos;
            bg.transform.localScale = new Vector3(0.45f, 0.30f, 1f);

            var sr = bg.AddComponent<SpriteRenderer>();
            sr.sprite       = MakeUnitSprite();
            sr.sortingOrder = 4;

            var col = bg.AddComponent<BoxCollider2D>();
            col.size = Vector2.one;          // unit-sprite is 1×1, scaled by transform

            var btn = bg.AddComponent<UIButton>();
            btn.OnClickAction = onClick;

            // Label as separate GO (parented to UI root, NOT to scaled bg)
            var labelGO = new GameObject($"BtnLbl_{label}");
            labelGO.transform.SetParent(transform, false);
            labelGO.transform.position   = new Vector3(pos.x, pos.y, pos.z - 0.2f);
            labelGO.transform.localScale = Vector3.one * SCALE * 1.4f;

            var tm       = labelGO.AddComponent<TextMesh>();
            tm.text      = label;
            tm.fontSize  = 22;
            tm.alignment = TextAlignment.Center;
            tm.anchor    = TextAnchor.MiddleCenter;
            tm.color     = new Color(1f, 1f, 1f);
            var mr       = labelGO.GetComponent<MeshRenderer>();
            if (mr != null) mr.sortingOrder = 6;

            return btn;
        }

        Sprite MakeUnitSprite()
        {
            if (_unitSprite != null) return _unitSprite;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            _unitSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            return _unitSprite;
        }

        // Creates a row at the current y, then decrements y by stepDown.
        TextMesh PRow(string name, ref float y, float stepDown, int fontSize, Color color)
        {
            y -= stepDown;
            return MkLabel(name, new Vector3(PX, y, 0f), fontSize, color);
        }

        // =================================================================
        // Top-bar API
        // =================================================================

        public void SetTurn(PieceColor turn)
        {
            SetText(_turnLabel,
                turn == PieceColor.White ? "Your turn  (White)" : "AI thinking...  (Black)");
        }

        public void SetAIThinking(bool thinking)
        {
            SetText(_statusLabel, thinking ? "> Computing..." : "");
        }

        public void SetGameOver(string result)
        {
            SetText(_statusLabel, result);
        }

        // =================================================================
        // Metrics panel API
        // =================================================================

        /// <summary>
        /// Refresh every value row of the audio-metrics panel.
        /// Called after each half-move (player or AI).
        ///
        /// q          -- Smoothed MCTS win-rate [0,1] (from AudioBridge.SmoothQ)
        /// dq         -- Smoothed delta ΔQ      (from AudioBridge.SmoothDQ)
        /// c          -- Confidence log(N+1)    (from AudioBridge.LastC)
        /// harmony    -- "Major" / "Neutral" / "Minor"
        /// jingle     -- "positive" / "neutral" / "negative"
        /// materialCp -- Static board eval in centipawns (+ = White advantage)
        /// legalMoves -- Legal move count for the side to move next
        /// inCheck    -- Is the side to move currently in check?
        /// plyNumber  -- Half-move counter (0 = game start)
        /// gamePhase  -- "OPENING" / "MIDGAME" / "ENDGAME"
        /// simCount   -- MCTS simulation count for the last AI move
        /// </summary>
        public void RefreshMetrics(
            float  q,           float  dq,          float  c,
            string harmony,     string jingle,
            int    materialCp,  int    legalMoves,   bool   inCheck,
            int    plyNumber,   string gamePhase,    int    simCount)
        {
            // -- Win rate (Q) -----------------------------------------------
            _pQ.color = QColor(q);
            _pQ.text  = string.Format("Q    {0:F3}   {1}", q, Bar(q));

            // -- Confidence (C) ---------------------------------------------
            float cNorm = Mathf.Clamp01(c / 7.5f);
            _pC.color = Color.Lerp(new Color(0.50f, 0.65f, 1.00f),
                                   new Color(1.00f, 1.00f, 0.40f), cNorm);
            _pC.text  = string.Format("C    {0:F2}    {1}", c, Bar(cNorm));

            // -- Harmony ---------------------------------------------------
            _pHarmony.color = HarmonyColor(harmony);
            _pHarmony.text  = "Harmony  " + (harmony ?? "Neutral").ToUpper();

            // -- Delta + jingle --------------------------------------------
            string tag = dq >  0.05f ? "[+]"
                       : dq < -0.05f ? "[-]"
                       :               "[=]";
            _pDQ.color = DeltaColor(dq);
            _pDQ.text  = string.Format("dQ   {0:+0.000;-0.000}  {1} {2}",
                                       dq, (jingle ?? "neutral").ToUpper(), tag);

            // -- Material balance ------------------------------------------
            _pMaterial.color = materialCp >  25 ? new Color(0.25f, 1.00f, 0.38f) :
                               materialCp < -25 ? new Color(1.00f, 0.28f, 0.28f) :
                                                  new Color(0.72f, 0.72f, 0.72f);
            _pMaterial.text  = "Material  " + (materialCp >= 0 ? "+" : "") + materialCp + " cp";

            // -- Game phase ------------------------------------------------
            _pPhase.color = gamePhase == "OPENING" ? new Color(0.35f, 0.90f, 1.00f) :
                            gamePhase == "ENDGAME" ? new Color(0.85f, 0.45f, 1.00f) :
                                                     new Color(1.00f, 0.85f, 0.30f);
            _pPhase.text  = "Phase    " + gamePhase;

            // -- Mobility (legal moves) ------------------------------------
            _pLegal.color = legalMoves >= 25 ? new Color(0.30f, 1.00f, 0.42f) :
                            legalMoves <= 10 ? new Color(1.00f, 0.42f, 0.30f) :
                                               Color.white;
            _pLegal.text  = "Mobility  " + legalMoves + " moves";

            // -- MCTS simulations ------------------------------------------
            _pSims.color = new Color(0.55f, 0.65f, 1.00f);
            _pSims.text  = simCount > 0 ? "Sims     " + simCount : "Sims     --";

            // -- Check indicator -------------------------------------------
            _pCheck.color = inCheck ? new Color(1.00f, 0.20f, 0.20f)
                                    : new Color(0.36f, 0.36f, 0.36f);
            _pCheck.text  = inCheck ? "Check    (!)" : "Check    ok";

            // -- Ply counter -----------------------------------------------
            _pPly.color = new Color(0.50f, 0.50f, 0.50f);
            _pPly.text  = "Ply      #" + plyNumber;
        }

        /// <summary>
        /// Refresh the controls panel (animation speed, AI difficulty, history pos).
        ///
        /// animSeconds     -- piece-animation duration in seconds (0.05 .. 1.0)
        /// aiSims          -- MCTS simulations per AI move
        /// historyIdx      -- current position in move history (0 = start)
        /// historyTotal    -- total moves played so far
        /// </summary>
        public void RefreshControls(float animSeconds, int aiSims,
                                    int historyIdx, int historyTotal)
        {
            // Animation speed — green for fast, red for slow
            float animNorm = Mathf.InverseLerp(0.05f, 1.5f, animSeconds);
            _pCtrlAnim.color = Color.Lerp(new Color(0.30f, 1.00f, 0.40f),
                                          new Color(1.00f, 0.55f, 0.20f), animNorm);
            _pCtrlAnim.text  = string.Format("Anim   {0:F2}s", animSeconds);

            // AI difficulty — blue for low, gold for high
            float simNorm = Mathf.InverseLerp(100f, 5000f, aiSims);
            _pCtrlDiff.color = Color.Lerp(new Color(0.55f, 0.80f, 1.00f),
                                          new Color(1.00f, 0.85f, 0.20f), simNorm);
            _pCtrlDiff.text  = string.Format("AI    {0,5}", aiSims);

            // History position — gray when reviewing past, green at latest
            bool atLatest = historyIdx == historyTotal;
            _pCtrlHist.color = atLatest ? new Color(0.60f, 1.00f, 0.60f)
                                        : new Color(1.00f, 0.75f, 0.30f);
            _pCtrlHist.text  = string.Format("Move  {0,3}/{1,-3}", historyIdx, historyTotal);
        }

        // =================================================================
        // Helpers
        // =================================================================

        // 8-character ASCII progress bar, e.g. [######--]
        static string Bar(float t, int w = 8)
        {
            int n = Mathf.RoundToInt(Mathf.Clamp01(t) * w);
            return "[" + new string('#', n) + new string('-', w - n) + "]";
        }

        static Color QColor(float q) =>
            q > 0.62f ? new Color(0.20f, 1.00f, 0.35f) :   // White winning  -> green
            q < 0.38f ? new Color(1.00f, 0.25f, 0.25f) :   // Black winning  -> red
                        new Color(0.90f, 0.88f, 0.28f);     // Balanced       -> yellow

        static Color HarmonyColor(string h)
        {
            if (h == "Major") return new Color(0.20f, 1.00f, 0.35f);   // green
            if (h == "Minor") return new Color(0.85f, 0.30f, 0.95f);   // purple
            return new Color(0.70f, 0.70f, 0.70f);                      // gray (Neutral)
        }

        static Color DeltaColor(float dq) =>
            dq >  0.05f ? new Color(0.20f, 1.00f, 0.32f) :  // positive -> green
            dq < -0.05f ? new Color(1.00f, 0.25f, 0.25f) :  // negative -> red
                          new Color(0.62f, 0.62f, 0.62f);    // neutral  -> gray

        void SetText(TextMesh tm, string text)
        {
            if (tm != null) tm.text = text;
        }

        TextMesh MkLabel(string goName, Vector3 pos, int fontSize, Color color)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(transform, false);
            go.transform.position   = pos;
            go.transform.localScale = Vector3.one * SCALE;

            var tm       = go.AddComponent<TextMesh>();
            tm.fontSize  = fontSize;
            tm.alignment = TextAlignment.Left;
            tm.anchor    = TextAnchor.MiddleLeft;
            tm.color     = color;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sortingOrder = 5;
            return tm;
        }
    }
}
