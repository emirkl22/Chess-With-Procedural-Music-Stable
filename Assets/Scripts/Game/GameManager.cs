using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Chess
{
    // -----------------------------------------------------------------------
    // GameManager – single entry point for the scene.
    //
    // Setup instructions (Unity Editor):
    //   1. Create an empty GameObject named "GameManager".
    //   2. Add this component.
    //   3. Create a PieceDatabase asset (Assets > Create > Chess > Piece Database)
    //      and assign the 12 sprites from "Pieces and Boards" folder.
    //   4. Drag the PieceDatabase asset into the "Piece Database" field.
    //   5. Hit Play.
    // -----------------------------------------------------------------------
    public class GameManager : MonoBehaviour
    {
        // ---- Inspector ----
        [Header("Assets")]
        [SerializeField] PieceDatabase pieceDatabase;

        [Header("AI Settings")]
        [SerializeField] int mctsSimulations = 200;

        // ---- State machine ----
        enum GamePhase { WaitingForInput, PieceSelected, AIThinking, GameOver }
        GamePhase _phase = GamePhase.WaitingForInput;

        // ---- Core ----
        BoardState _board;
        List<Move> _selectedMoves = new List<Move>();
        int        _selectedRow = -1, _selectedCol = -1;
        Move       _lastMove;
        bool       _hasLastMove;

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
            // Board rendering uses absolute world coords — GameManager must be at origin.
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
            RefreshMetricsPanel();   // show neutral defaults before first move
        }

        // -----------------------------------------------------------------------
        // Input callback (called by SelectionManager)
        // -----------------------------------------------------------------------
        public void OnSquareClicked(int row, int col)
        {
            Debug.Log($"[GameManager] Kare tıklandı: row={row} col={col}  phase={_phase}  piece={_board.Board[row, col]}");
            if (_phase == GamePhase.AIThinking || _phase == GamePhase.GameOver) return;

            if (_phase == GamePhase.WaitingForInput)
            {
                TrySelectPiece(row, col);
            }
            else if (_phase == GamePhase.PieceSelected)
            {
                // Check if clicked a valid move target
                Move? matched = null;
                foreach (var m in _selectedMoves)
                    if (m.ToRow == row && m.ToCol == col) { matched = m; break; }

                if (matched.HasValue)
                {
                    // Auto-promote to queen
                    var move = matched.Value;
                    if (move.IsPromotion) move.PromotionPiece = Piece.Queen;

                    ExecutePlayerMove(move);
                }
                else
                {
                    // Clicked elsewhere — try to select a different piece
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
            if (piece == Piece.None)
            {
                Debug.Log($"[TrySelect] row={row} col={col} → boş kare, seçilecek taş yok.");
                return;
            }
            if (Piece.GetColor(piece) != PieceColor.White)
            {
                Debug.Log($"[TrySelect] row={row} col={col} → siyah taş ({piece}), sadece beyaz taşlar seçilebilir.");
                return;
            }

            // Compute legal moves for this piece
            _selectedMoves.Clear();
            var all = MoveGenerator.GetLegalMoves(_board);
            foreach (var m in all)
                if (m.FromRow == row && m.FromCol == col) _selectedMoves.Add(m);

            if (_selectedMoves.Count == 0) return;

            _selectedRow = row; _selectedCol = col;
            _highlights.ShowSelection(row, col, _selectedMoves);
            _phase = GamePhase.PieceSelected;
        }

        void ExecutePlayerMove(Move move)
        {
            // Detect capture BEFORE applying the move (destination square holds the victim)
            int captureValue = GetCaptureValue(move);

            _board.ApplyMove(move);
            _lastMove = move; _hasLastMove = true;
            _highlights.Clear();
            _highlights.ShowLastMove(move);
            _pieceVisuals.Refresh(_board);

            _plyNumber++;

            int materialCp = BoardEvaluator.Evaluate(_board);
            _audio.OnPlayerMove(materialCp, captureValue);
            RefreshMetricsPanel();

            if (CheckGameOver(PieceColor.Black)) return;

            _phase = GamePhase.AIThinking;
            _ui.SetTurn(PieceColor.Black);
            _ui.SetAIThinking(true);
            StartCoroutine(RunAI());
        }

        // -----------------------------------------------------------------------
        // AI move
        // -----------------------------------------------------------------------
        IEnumerator RunAI()
        {
            yield return StartCoroutine(
                _mcts.FindBestMove(_board, (move, winRate, visits) =>
                {
                    float delta = winRate - _prevWinRate;
                    _prevWinRate = winRate;

                    int captureValue = GetCaptureValue(move);

                    _board.ApplyMove(move);
                    _lastMove = move; _hasLastMove = true;

                    _highlights.Clear();
                    _highlights.ShowLastMove(move);
                    _pieceVisuals.Refresh(_board);

                    _ui.SetAIThinking(false);

                    int materialCp = BoardEvaluator.Evaluate(_board);
                    _audio.OnMovePlayed(winRate, delta, visits, materialCp, captureValue);

                    _plyNumber++;
                    RefreshMetricsPanel();

                    if (!CheckGameOver(PieceColor.White))
                    {
                        _phase = GamePhase.WaitingForInput;
                        _ui.SetTurn(PieceColor.White);
                    }
                })
            );
        }

        // -----------------------------------------------------------------------
        // Capture detection
        // -----------------------------------------------------------------------
        // Returns the standard piece value of whatever piece this move captures,
        // or 0 if the move is not a capture.
        //   Pawn=1, Knight=3, Bishop=3, Rook=5, Queen=9
        int GetCaptureValue(Move move)
        {
            if (move.IsEnPassant) return 1;       // captured pawn

            int victim = _board.Board[move.ToRow, move.ToCol];
            if (victim == Piece.None) return 0;
            int type = Piece.GetType(victim);
            switch (type)
            {
                case Piece.Pawn:   return 1;
                case Piece.Knight: return 3;
                case Piece.Bishop: return 3;
                case Piece.Rook:   return 5;
                case Piece.Queen:  return 9;
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
                result = "Stalemate – Draw!";
            }

            _ui.SetGameOver(result);
            Debug.Log($"[GameManager] Game over: {result}");
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
            cam.orthographic     = true;
            cam.orthographicSize = 5.5f;
            cam.transform.position = new Vector3(4f, 4.5f, -10f);
            cam.backgroundColor  = new Color(0.12f, 0.12f, 0.14f);
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
                Debug.LogError("[GameManager] Camera.main bulunamadı! Sahnede 'MainCamera' tag'li bir kamera olduğundan emin ol.");

            // SelectionManager artık GameManager'ın kendi GO'sunda, kamera referansını parametre olarak alıyor
            var sel = gameObject.AddComponent<SelectionManager>();
            sel.Init(this, cam);

            // MCTS agent
            _mcts = gameObject.AddComponent<MCTSAgent>();
            _mcts.SimulationsPerMove = mctsSimulations;

            // Audio bridge
            _audio = gameObject.AddComponent<AudioBridge>();
        }

        // -----------------------------------------------------------------------
        // Metrics panel helpers
        // -----------------------------------------------------------------------

        /// <summary>
        /// Gather board + audio metrics and push them to UIManager.RefreshMetrics().
        /// Safe to call at any time; AudioBridge properties default to neutral values
        /// before the first AI move.
        /// </summary>
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

        /// <summary>
        /// Heuristic game-phase label used by the metrics panel and music engine.
        ///   OPENING  : fewer than 10 half-moves played AND 28+ pieces remain
        ///   ENDGAME  : no queens on the board OR 14 or fewer pieces remain
        ///   MIDGAME  : everything else
        /// </summary>
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
