using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Chess
{
    // -----------------------------------------------------------------------
    // GameManager – single entry point for the scene.
    //
    // Adjustable at runtime (Inspector sliders, on-screen buttons, OR keys):
    //   * animationSeconds — piece-move animation duration
    //   * mctsSimulations  — AI difficulty (more sims = stronger but slower)
    //
    // Keys (new Input System):
    //   <- / ->     Navigate move history
    //   [   /  ]    Slower / faster piece animation
    //   ,   /  .    Lower / higher AI difficulty
    //    R          Jump back to the latest played position
    // -----------------------------------------------------------------------
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

        // -----------------------------------------------------------------------
        void Awake()
        {
            transform.position = Vector3.zero;
            transform.rotation = Quaternion.identity;

            _board = new BoardState();
            SetupCamera();
            SetupBoard();
            SetupUI();
            SetupComponents();
        }

        void Start()
        {
            _pieceVisuals.Refresh(_board);
            _ui.SetTurn(PieceColor.White);
            RefreshMetricsPanel();
            _ui.RefreshControls(animationSeconds, mctsSimulations, _historyIdx, _historyMoves.Count);

            // Wire UI button callbacks → adjustment methods
            _ui.OnFontSmaller    = () => AdjustFontScale(-0.05f);
            _ui.OnFontBigger     = () => AdjustFontScale(+0.05f);
            _ui.OnAnimSlower     = () => AdjustAnimSpeed(+0.05f);
            _ui.OnAnimFaster     = () => AdjustAnimSpeed(-0.05f);
            _ui.OnSimsDown       = () => AdjustSims(-100);
            _ui.OnSimsUp         = () => AdjustSims(+100);
            _ui.OnHistoryBack    = () => NavigateHistory(-1);
            _ui.OnHistoryForward = () => NavigateHistory(+1);
            _ui.OnReset          = () => ResetToLatest();

            // Apply initial font scale and seed the sync caches
            _ui.SetFontScale(fontScale);
            _ui.RefreshControls(animationSeconds, mctsSimulations, _historyIdx, _historyStates.Count);

            _lastSyncedSims = mctsSimulations;
            _lastSyncedAnim = animationSeconds;
            _lastSyncedFont = fontScale;
        }

        // -----------------------------------------------------------------------
        // Keyboard shortcuts (new Input System) + Inspector-edit sync
        // -----------------------------------------------------------------------
        void Update()
        {
            // 1. Inspector → runtime sync.  If a slider is dragged in the
            //    Inspector during Play mode, push the change into MCTS + UI.
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
                _ui.SetFontScale(fontScale);
                _lastSyncedFont = fontScale;
                changed = true;
            }
            if (changed)
                _ui.RefreshControls(animationSeconds, mctsSimulations, _historyIdx, _historyStates.Count);

            // 2. Keyboard shortcuts (new Input System)
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

        // -----------------------------------------------------------------------
        // Input callback (from SelectionManager)
        // -----------------------------------------------------------------------
        public void OnSquareClicked(int row, int col)
        {
            if (_phase == GamePhase.AIThinking || _phase == GamePhase.GameOver) return;

            // If we're browsing history, a click should leap back to the latest
            // position. The user must explicitly press Right (or press R) to
            // get out of review mode — clicks alone don't accidentally branch.
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

        // -----------------------------------------------------------------------
        // Player move
        // -----------------------------------------------------------------------
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
            _phase = GamePhase.AIThinking;   // block clicks during animation

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

        // -----------------------------------------------------------------------
        // AI move
        // -----------------------------------------------------------------------
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

        // -----------------------------------------------------------------------
        // History management
        // -----------------------------------------------------------------------
        void PushHistory(Move m, BoardState postState)
        {
            // If we've navigated backward and then played a new move, the
            // forward history is discarded (standard chess-review branching).
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

            // Restore board state
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

            // Send material to SC so background follows reviewed position
            int matCp = BoardEvaluator.Evaluate(_board);
            _audio.SendMaterialOnly(matCp);

            RefreshAll();
        }

        void ResetToLatest()
        {
            if (_historyIdx == _historyStates.Count) return;
            NavigateHistory(_historyStates.Count - _historyIdx);
        }

        // -----------------------------------------------------------------------
        // Capture value lookup (standard piece values)
        //   Pawn=1, Knight=3, Bishop=3, Rook=5, Queen=10
        // -----------------------------------------------------------------------
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

        // -----------------------------------------------------------------------
        // Game over detection
        // -----------------------------------------------------------------------
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

        // -----------------------------------------------------------------------
        // Scene / component setup
        // -----------------------------------------------------------------------
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

        // -----------------------------------------------------------------------
        // Metrics + controls refresh
        // -----------------------------------------------------------------------
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
                _audio.SmoothQ,
                _audio.SmoothDQ,
                _audio.LastC,
                _audio.Harmony,
                _audio.Jingle,
                mat,
                moves.Count,
                inCheck,
                _plyNumber,
                ComputePhase(),
                _mcts != null ? _mcts.LastVisitCount : 0);
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
