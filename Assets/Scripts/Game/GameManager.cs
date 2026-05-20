using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Chess
{
    // -----------------------------------------------------------------------
    // GameManager – single entry point for the scene.
    //
    // ExecuteAlways: board, pieces, and the UI panel are also created in
    // edit-mode so they are visible in the Scene view before Play is pressed.
    // Gameplay components (SelectionManager, MCTSAgent, AudioBridge) are
    // only attached during Play mode so they don't get serialised into the
    // saved scene and don't try to open UDP sockets at edit time.
    //
    // Adjustable at runtime (Inspector sliders, on-screen buttons, OR keys):
    //   * animationSeconds — piece-move animation duration
    //   * mctsSimulations  — AI difficulty (more sims = stronger but slower)
    //   * fontScale        — UI font multiplier
    //
    // Keys (new Input System, Play mode only):
    //   <- / ->     Navigate move history
    //   [   /  ]    Slower / faster piece animation
    //   ,   /  .    Lower / higher AI difficulty
    //   ;   /  '    Smaller / bigger UI fonts
    //    R          Jump to the latest played position
    // -----------------------------------------------------------------------
    [ExecuteAlways]
    public class GameManager : MonoBehaviour
    {
        // ---- Inspector ----
        [Header("Assets")]
        [SerializeField] PieceDatabase pieceDatabase;

        [Header("AI Settings — also adjustable in-game via UI buttons / , . keys")]
        [Range(100, 5000)]
        [SerializeField] int mctsSimulations = 1500;

        [Header("Animation — also adjustable in-game via UI buttons / [ ] keys")]
        [Range(0.05f, 1.50f)]
        [SerializeField] float animationSeconds = 0.30f;

        [Header("UI Font scale — also adjustable in-game via UI buttons / ; ' keys")]
        [Range(0.6f, 1.6f)]
        [SerializeField] float fontScale = 1.60f;

        // Used to detect Inspector edits at runtime and propagate them
        int   _lastSyncedSims;
        float _lastSyncedAnim;
        float _lastSyncedFont;

        // ---- State machine ----
        enum GamePhase { WaitingForInput, PieceSelected, AIThinking, GameOver, Reviewing }
        GamePhase _phase = GamePhase.WaitingForInput;

        // ---- Core ----
        BoardState _board;
        List<Move> _selectedMoves = new List<Move>();
        int        _selectedRow = -1, _selectedCol = -1;

        // ---- Move history (snapshots AFTER each ply) ---------------------
        readonly List<BoardState> _historyStates = new List<BoardState>();
        readonly List<Move>       _historyMoves  = new List<Move>();
        int _historyIdx = 0;   // 0 = initial position; N = after N moves played

        // ---- Audio tracking ----
        float _prevWinRate = 0.5f;

        // ---- Ply counter (incremented after every half-move) ----
        int _plyNumber = 0;

        // ---- Components ----
        BoardRenderer       _boardRenderer;
        PieceVisualManager  _pieceVisuals;
        HighlightManager    _highlights;
        UIManager           _ui;
        MCTSAgent           _mcts;
        AudioBridge         _audio;

        // ===================================================================
        // Lifecycle — edit mode and play mode both go through Build()
        // ===================================================================
        void OnEnable()
        {
            // Tear down any stale children from a previous build (post script
            // reload, post scene reload, or stale serialised state) and then
            // rebuild a fresh hierarchy.
            TeardownChildren();
            Build();
        }

        void OnDisable()
        {
            // Only clean up when transitioning out in edit mode.  Play-mode
            // exit lets Unity revert the scene by itself; tearing down here
            // would just race with that.
            if (!Application.isPlaying) TeardownChildren();
        }

        // -----------------------------------------------------------------
        // Build the visual hierarchy (always) + gameplay components (Play only)
        // -----------------------------------------------------------------
        void Build()
        {
            // Board rendering uses absolute world coords — keep at origin.
            transform.position = Vector3.zero;
            transform.rotation = Quaternion.identity;

            _board = new BoardState();

            // Visuals are always created (so Scene-view preview works)
            if (Application.isPlaying) SetupCamera();
            SetupBoard();
            SetupUI();

            _pieceVisuals.Refresh(_board);
            _ui.SetTurn(PieceColor.White);
            _ui.SetFontScale(fontScale);

            // Gameplay components only in Play mode — these own UDP sockets
            // and AI coroutines so they must NOT live in the edit-mode scene.
            if (Application.isPlaying)
            {
                SetupComponents();
                WireUICallbacks();
            }

            // Sync caches must be seeded in both modes so Update() doesn't
            // think every Inspector value is "newly changed" on first tick.
            _lastSyncedSims = mctsSimulations;
            _lastSyncedAnim = animationSeconds;
            _lastSyncedFont = fontScale;

            RefreshMetricsPanel();
            _ui.RefreshControls(animationSeconds, mctsSimulations,
                                _historyIdx, _historyStates.Count);
        }

        // Destroys every child GO under GameManager + any play-mode-only
        // components that may have been accidentally serialised.
        void TeardownChildren()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var go = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(go);
                else                       DestroyImmediate(go);
            }

            if (!Application.isPlaying)
            {
                var sel   = GetComponent<SelectionManager>();
                var mcts  = GetComponent<MCTSAgent>();
                var audio = GetComponent<AudioBridge>();
                if (sel   != null) DestroyImmediate(sel);
                if (mcts  != null) DestroyImmediate(mcts);
                if (audio != null) DestroyImmediate(audio);
            }
        }

        // -------------------------------------------------------------------
        // Keyboard + Inspector-edit sync — Play mode only
        // -------------------------------------------------------------------
        void Update()
        {
            // 1. Inspector → runtime sync (also runs in edit mode so dragging
            //    a slider updates the Scene-view preview immediately).
            bool changed = false;
            if (mctsSimulations != _lastSyncedSims)
            {
                if (_mcts != null) _mcts.SimulationsPerMove = mctsSimulations;
                _lastSyncedSims = mctsSimulations;
                changed = true;
            }
            if (!Mathf.Approximately(animationSeconds, _lastSyncedAnim))
            {
                _lastSyncedAnim = animationSeconds;
                changed = true;
            }
            if (!Mathf.Approximately(fontScale, _lastSyncedFont))
            {
                if (_ui != null) _ui.SetFontScale(fontScale);
                _lastSyncedFont = fontScale;
                changed = true;
            }
            if (changed && _ui != null)
                _ui.RefreshControls(animationSeconds, mctsSimulations, _historyIdx, _historyStates.Count);

            // 2. Keyboard shortcuts — Play mode only
            if (!Application.isPlaying) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            if      (kb.leftArrowKey   .wasPressedThisFrame) NavigateHistory(-1);
            else if (kb.rightArrowKey  .wasPressedThisFrame) NavigateHistory(+1);
            else if (kb.leftBracketKey .wasPressedThisFrame) AdjustAnimSpeed(+0.05f);
            else if (kb.rightBracketKey.wasPressedThisFrame) AdjustAnimSpeed(-0.05f);
            else if (kb.commaKey       .wasPressedThisFrame) AdjustSims(-100);
            else if (kb.periodKey      .wasPressedThisFrame) AdjustSims(+100);
            else if (kb.semicolonKey   .wasPressedThisFrame) AdjustFontScale(-0.05f);
            else if (kb.quoteKey       .wasPressedThisFrame) AdjustFontScale(+0.05f);
            else if (kb.rKey           .wasPressedThisFrame) ResetToLatest();
        }

        void AdjustAnimSpeed(float delta)
        {
            animationSeconds = Mathf.Clamp(animationSeconds + delta, 0.05f, 1.50f);
            _lastSyncedAnim  = animationSeconds;
            _ui.RefreshControls(animationSeconds, mctsSimulations, _historyIdx, _historyStates.Count);
        }

        void AdjustSims(int delta)
        {
            mctsSimulations  = Mathf.Clamp(mctsSimulations + delta, 100, 5000);
            _lastSyncedSims  = mctsSimulations;
            if (_mcts != null) _mcts.SimulationsPerMove = mctsSimulations;
            _ui.RefreshControls(animationSeconds, mctsSimulations, _historyIdx, _historyStates.Count);
        }

        void AdjustFontScale(float delta)
        {
            fontScale       = Mathf.Clamp(fontScale + delta, 0.6f, 1.6f);
            _lastSyncedFont = fontScale;
            _ui.SetFontScale(fontScale);
            _ui.RefreshControls(animationSeconds, mctsSimulations, _historyIdx, _historyStates.Count);
        }

        // -------------------------------------------------------------------
        // Input callback (from SelectionManager — Play mode only)
        // -------------------------------------------------------------------
        public void OnSquareClicked(int row, int col)
        {
            if (_phase == GamePhase.AIThinking || _phase == GamePhase.GameOver) return;
            if (_phase == GamePhase.Reviewing) return;

            if (_phase == GamePhase.WaitingForInput)
            {
                TrySelectPiece(row, col);
            }
            else if (_phase == GamePhase.PieceSelected)
            {
                Move? matched = null;
                foreach (var m in _selectedMoves)
                    if (m.ToRow == row && m.ToCol == col) { matched = m; break; }

                if (matched.HasValue)
                {
                    var move = matched.Value;
                    if (move.IsPromotion) move.PromotionPiece = Piece.Queen;
                    StartCoroutine(ExecutePlayerMoveCo(move));
                }
                else
                {
                    _highlights.Clear();
                    _phase = GamePhase.WaitingForInput;
                    TrySelectPiece(row, col);
                }
            }
        }

        // -------------------------------------------------------------------
        // Player move
        // -------------------------------------------------------------------
        void TrySelectPiece(int row, int col)
        {
            int piece = _board.Board[row, col];
            if (piece == Piece.None) return;
            if (Piece.GetColor(piece) != PieceColor.White) return;

            _selectedMoves.Clear();
            var all = MoveGenerator.GetLegalMoves(_board);
            foreach (var m in all)
                if (m.FromRow == row && m.FromCol == col) _selectedMoves.Add(m);

            if (_selectedMoves.Count == 0) return;

            _selectedRow = row; _selectedCol = col;
            _highlights.ShowSelection(row, col, _selectedMoves);
            _phase = GamePhase.PieceSelected;
        }

        IEnumerator ExecutePlayerMoveCo(Move move)
        {
            _phase = GamePhase.AIThinking;

            int        captureValue = GetCaptureValue(move);
            PieceColor mover        = _board.CurrentTurn;

            _board.ApplyMove(move);
            _highlights.Clear();

            yield return StartCoroutine(
                _pieceVisuals.AnimateMove(move, _board, mover, animationSeconds));

            _highlights.ShowLastMove(move);

            _plyNumber++;
            PushHistory(move, _board);

            int materialCp = BoardEvaluator.Evaluate(_board);
            _audio.OnPlayerMove(materialCp, captureValue);
            RefreshAll();

            if (CheckGameOver(PieceColor.Black)) yield break;

            _ui.SetTurn(PieceColor.Black);
            _ui.SetAIThinking(true);
            yield return StartCoroutine(RunAI());
        }

        // -------------------------------------------------------------------
        // AI move
        // -------------------------------------------------------------------
        IEnumerator RunAI()
        {
            Move? captured = null;
            float capturedWR = 0f;
            int   capturedV  = 0;

            yield return StartCoroutine(
                _mcts.FindBestMove(_board, (move, winRate, visits) =>
                {
                    captured   = move;
                    capturedWR = winRate;
                    capturedV  = visits;
                })
            );

            if (!captured.HasValue) yield break;
            Move m = captured.Value;

            float delta = capturedWR - _prevWinRate;
            _prevWinRate = capturedWR;

            int        captureValue = GetCaptureValue(m);
            PieceColor mover        = _board.CurrentTurn;

            _board.ApplyMove(m);
            _highlights.Clear();

            yield return StartCoroutine(
                _pieceVisuals.AnimateMove(m, _board, mover, animationSeconds));

            _highlights.ShowLastMove(m);
            _ui.SetAIThinking(false);

            int materialCp = BoardEvaluator.Evaluate(_board);
            _audio.OnMovePlayed(capturedWR, delta, capturedV, materialCp, captureValue);

            _plyNumber++;
            PushHistory(m, _board);
            RefreshAll();

            if (!CheckGameOver(PieceColor.White))
            {
                _phase = GamePhase.WaitingForInput;
                _ui.SetTurn(PieceColor.White);
            }
        }

        // -------------------------------------------------------------------
        // History management
        // -------------------------------------------------------------------
        void PushHistory(Move m, BoardState postState)
        {
            if (_historyIdx < _historyStates.Count)
            {
                _historyStates.RemoveRange(_historyIdx, _historyStates.Count - _historyIdx);
                _historyMoves .RemoveRange(_historyIdx, _historyMoves .Count - _historyIdx);
            }
            _historyStates.Add(new BoardState(postState));
            _historyMoves .Add(m);
            _historyIdx = _historyStates.Count;
        }

        void NavigateHistory(int direction)
        {
            if (_phase == GamePhase.AIThinking) return;

            int newIdx = Mathf.Clamp(_historyIdx + direction, 0, _historyStates.Count);
            if (newIdx == _historyIdx) return;
            _historyIdx = newIdx;

            _board = newIdx == 0
                   ? new BoardState()
                   : new BoardState(_historyStates[newIdx - 1]);

            _pieceVisuals.Refresh(_board);
            _highlights.Clear();
            if (newIdx > 0) _highlights.ShowLastMove(_historyMoves[newIdx - 1]);

            if (newIdx == _historyStates.Count)
            {
                _phase = _phase == GamePhase.GameOver ? GamePhase.GameOver
                                                      : GamePhase.WaitingForInput;
            }
            else
            {
                _phase = GamePhase.Reviewing;
            }

            int matCp = BoardEvaluator.Evaluate(_board);
            if (_audio != null) _audio.SendMaterialOnly(matCp);

            RefreshAll();
        }

        void ResetToLatest()
        {
            if (_historyIdx == _historyStates.Count) return;
            NavigateHistory(_historyStates.Count - _historyIdx);
        }

        // -------------------------------------------------------------------
        // Capture value lookup (Pawn=1, Knight/Bishop=3, Rook=5, Queen=10)
        // -------------------------------------------------------------------
        int GetCaptureValue(Move move)
        {
            if (move.IsEnPassant) return 1;
            int victim = _board.Board[move.ToRow, move.ToCol];
            if (victim == Piece.None) return 0;
            int type = Piece.GetType(victim);
            switch (type)
            {
                case Piece.Pawn:   return 1;
                case Piece.Knight: return 3;
                case Piece.Bishop: return 3;
                case Piece.Rook:   return 5;
                case Piece.Queen:  return 10;
                default:           return 0;
            }
        }

        bool CheckGameOver(PieceColor playerToCheck)
        {
            var legalMoves = MoveGenerator.GetLegalMoves(_board);
            if (legalMoves.Count > 0) return false;

            string result;
            if (MoveGenerator.IsInCheck(_board, playerToCheck))
            {
                string winner = playerToCheck == PieceColor.White ? "Black wins!" : "White wins!";
                result = $"Checkmate! {winner}";
            }
            else
            {
                result = "Stalemate - Draw!";
            }

            _ui.SetGameOver(result);
            _phase = GamePhase.GameOver;
            return true;
        }

        // -------------------------------------------------------------------
        // Scene / component setup
        // -------------------------------------------------------------------
        void SetupCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;
            cam.orthographic       = true;
            cam.orthographicSize   = 5.5f;
            cam.transform.position = new Vector3(4f, 4.5f, -10f);
            cam.backgroundColor    = new Color(0.12f, 0.12f, 0.14f);
        }

        void SetupBoard()
        {
            var boardGO = new GameObject("Board");
            boardGO.transform.SetParent(transform, false);
            _boardRenderer = boardGO.AddComponent<BoardRenderer>();
            _boardRenderer.Build();

            var piecesGO = new GameObject("Pieces");
            piecesGO.transform.SetParent(transform, false);
            _pieceVisuals = piecesGO.AddComponent<PieceVisualManager>();
            _pieceVisuals.pieceDatabase = pieceDatabase;

            var hlGO = new GameObject("Highlights");
            hlGO.transform.SetParent(transform, false);
            _highlights = hlGO.AddComponent<HighlightManager>();
        }

        void SetupUI()
        {
            var uiGO = new GameObject("UI");
            uiGO.transform.SetParent(transform, false);
            _ui = uiGO.AddComponent<UIManager>();
            _ui.Build();
        }

        void SetupComponents()
        {
            var cam = Camera.main;
            if (cam == null)
                Debug.LogError("[GameManager] Camera.main bulunamadi.");

            var sel = gameObject.AddComponent<SelectionManager>();
            sel.Init(this, cam);

            _mcts = gameObject.AddComponent<MCTSAgent>();
            _mcts.SimulationsPerMove = mctsSimulations;

            _audio = gameObject.AddComponent<AudioBridge>();
        }

        void WireUICallbacks()
        {
            _ui.OnFontSmaller    = () => AdjustFontScale(-0.05f);
            _ui.OnFontBigger     = () => AdjustFontScale(+0.05f);
            _ui.OnAnimSlower     = () => AdjustAnimSpeed(+0.05f);
            _ui.OnAnimFaster     = () => AdjustAnimSpeed(-0.05f);
            _ui.OnSimsDown       = () => AdjustSims(-100);
            _ui.OnSimsUp         = () => AdjustSims(+100);
            _ui.OnHistoryBack    = () => NavigateHistory(-1);
            _ui.OnHistoryForward = () => NavigateHistory(+1);
            _ui.OnReset          = () => ResetToLatest();
        }

        // -------------------------------------------------------------------
        // Metrics + controls refresh — handles null _audio (edit mode)
        // -------------------------------------------------------------------
        void RefreshAll()
        {
            RefreshMetricsPanel();
            _ui.RefreshControls(animationSeconds, mctsSimulations,
                                _historyIdx, _historyStates.Count);
        }

        void RefreshMetricsPanel()
        {
            int  mat     = BoardEvaluator.Evaluate(_board);
            var  moves   = MoveGenerator.GetLegalMoves(_board);
            bool inCheck = MoveGenerator.IsInCheck(_board, _board.CurrentTurn);

            _ui.RefreshMetrics(
                _audio != null ? _audio.SmoothQ  : 0.5f,
                _audio != null ? _audio.SmoothDQ : 0f,
                _audio != null ? _audio.LastC    : 0f,
                _audio != null ? _audio.Harmony  : "Neutral",
                _audio != null ? _audio.Jingle   : "neutral",
                mat, moves.Count, inCheck,
                _plyNumber, ComputePhase(),
                _mcts  != null ? _mcts.LastVisitCount : 0);
        }

        string ComputePhase()
        {
            int  pieces    = 0;
            bool hasQueens = false;
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                {
                    int p = _board.Board[r, c];
                    if (p == Piece.None) continue;
                    pieces++;
                    if (Piece.GetType(p) == Piece.Queen) hasQueens = true;
                }

            if (_plyNumber < 10 && pieces >= 28) return "OPENING";
            if (!hasQueens || pieces <= 14)       return "ENDGAME";
            return "MIDGAME";
        }
    }
}
