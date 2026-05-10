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
        }

        // -----------------------------------------------------------------------
        // Input callback (called by SelectionManager)
        // -----------------------------------------------------------------------
        public void OnSquareClicked(int row, int col)
        {
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
            if (piece == Piece.None || Piece.GetColor(piece) != PieceColor.White) return;

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
            _board.ApplyMove(move);
            _lastMove = move; _hasLastMove = true;
            _highlights.Clear();
            _highlights.ShowLastMove(move);
            _pieceVisuals.Refresh(_board);

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

                    _board.ApplyMove(move);
                    _lastMove = move; _hasLastMove = true;

                    _highlights.Clear();
                    _highlights.ShowLastMove(move);
                    _pieceVisuals.Refresh(_board);

                    _ui.SetAIThinking(false);
                    _ui.SetEvalInfo(winRate, delta, visits);
                    _audio.OnMovePlayed(winRate, delta, visits);

                    if (!CheckGameOver(PieceColor.White))
                    {
                        _phase = GamePhase.WaitingForInput;
                        _ui.SetTurn(PieceColor.White);
                    }
                })
            );
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
            // Selection manager lives on the camera
            var cam = Camera.main;
            if (cam != null)
            {
                var sel = cam.gameObject.AddComponent<SelectionManager>();
                sel.Init(this);
            }

            // MCTS agent
            _mcts = gameObject.AddComponent<MCTSAgent>();
            _mcts.SimulationsPerMove = mctsSimulations;

            // Audio bridge
            _audio = gameObject.AddComponent<AudioBridge>();
        }
    }
}
