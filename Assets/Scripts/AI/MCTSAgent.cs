using System;
using System.Collections;
using UnityEngine;

namespace Chess
{
    // MCTS agent with forced-mate pre-pass and quiescence search at leaf nodes.
    public class MCTSAgent : MonoBehaviour
    {
        [Tooltip("Number of MCTS simulations per move")]
        public int SimulationsPerMove = 1500;

        [Tooltip("Capture-search depth at each leaf node (higher = stronger tactics, slower)")]
        public int QSearchDepth = 3;

        const float C_UCT = 1.41421356f;
        const int   INF   = 1000000;

        public float LastRootWinRate  { get; private set; }
        public int   LastVisitCount   { get; private set; }

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        public IEnumerator FindBestMove(BoardState state, Action<Move, float, int> onComplete)
        {
            // Pre-pass: forced-mate search (mate-in-1, then mate-in-2).
            // Mate-in-3 removed — too slow in middlegame, rarely occurs.
            for (int depth = 1; depth <= 2; depth++)
            {
                if (TryForcedMate(state, depth, out Move mateMove))
                {
                    LastVisitCount  = depth;
                    LastRootWinRate = 1f;
                    onComplete(mateMove, 1f, depth);
                    yield break;
                }
                yield return null;
            }

            // MCTS with quiescence search at each leaf
            var root = new MCTSNode(new BoardState(state));

            for (int i = 0; i < SimulationsPerMove; i++)
            {
                var leaf = Select(root);
                if (!leaf.IsTerminal) leaf = Expand(leaf);
                float result = Simulate(leaf.State);
                Backpropagate(leaf, result);

                if (i % 150 == 149) yield return null;
            }

            MCTSNode best = null;
            foreach (var child in root.Children)
                if (best == null || child.N > best.N)
                    best = child;

            LastVisitCount  = root.N;
            LastRootWinRate = best != null ? 1f - best.Q : 0.5f;

            if (best?.MoveThatLedHere.HasValue == true)
                onComplete(best.MoveThatLedHere.Value, LastRootWinRate, LastVisitCount);
        }

        // ------------------------------------------------------------------
        // Forced-mate search (minimax, capture/check-only, early exit)
        // ------------------------------------------------------------------

        bool TryForcedMate(BoardState state, int movesAhead, out Move foundMove)
        {
            foundMove = default;
            foreach (var m in MoveGenerator.GetLegalMoves(state))
            {
                var after = new BoardState(state);
                after.ApplyMove(m);
                if (OpponentIsMated(after, movesAhead - 1))
                { foundMove = m; return true; }
            }
            return false;
        }

        bool OpponentIsMated(BoardState state, int movesLeft)
        {
            var oppMoves = MoveGenerator.GetLegalMoves(state);
            if (oppMoves.Count == 0)
                return MoveGenerator.IsInCheck(state, state.CurrentTurn);
            if (movesLeft == 0) return false;

            foreach (var om in oppMoves)
            {
                var afterOpp = new BoardState(state);
                afterOpp.ApplyMove(om);

                bool weCanMate = false;
                foreach (var am in MoveGenerator.GetLegalMoves(afterOpp))
                {
                    var afterUs = new BoardState(afterOpp);
                    afterUs.ApplyMove(am);
                    if (OpponentIsMated(afterUs, movesLeft - 1))
                    { weCanMate = true; break; }
                }
                if (!weCanMate) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------
        // MCTS phases
        // ------------------------------------------------------------------

        MCTSNode Select(MCTSNode node)
        {
            while (!node.IsTerminal && node.IsFullyExpanded)
            {
                MCTSNode best    = null;
                float    bestUCB = float.MinValue;
                foreach (var child in node.Children)
                {
                    float ucb = child.Q + C_UCT * Mathf.Sqrt(Mathf.Log(node.N + 1f) / (child.N + 1f));
                    if (ucb > bestUCB) { bestUCB = ucb; best = child; }
                }
                if (best == null) break;
                node = best;
            }
            return node;
        }

        MCTSNode Expand(MCTSNode node)
        {
            if (node.UntriedMoves.Count == 0) return node;
            int  idx  = UnityEngine.Random.Range(0, node.UntriedMoves.Count);
            Move move = node.UntriedMoves[idx];
            node.UntriedMoves.RemoveAt(idx);
            var childState = new BoardState(node.State);
            childState.ApplyMove(move);
            var child = new MCTSNode(childState, move, node);
            node.Children.Add(child);
            return child;
        }

        // Leaf evaluation: terminal check + quiescence search instead of raw static eval.
        // Quiescence search follows captures until the position is "quiet", preventing
        // the horizon effect (AI being fooled by hanging pieces just past its search depth).
        float Simulate(BoardState state)
        {
            var moves = MoveGenerator.GetLegalMoves(state);
            if (moves.Count == 0)
            {
                if (MoveGenerator.IsInCheck(state, state.CurrentTurn))
                    return state.CurrentTurn == PieceColor.White ? 0f : 1f;
                return 0.5f;
            }

            int eval = Quiescence(state, QSearchDepth, -INF, INF);
            return Mathf.Clamp01(0.5f + eval / 2000f);
        }

        // Alpha-beta quiescence search over captures only.
        // Returns centipawn score from White's perspective (positive = White winning).
        // Stand-pat: if the static eval already exceeds beta, prune immediately.
        int Quiescence(BoardState state, int depth, int alpha, int beta)
        {
            int standPat = BoardEvaluator.Evaluate(state);
            bool whiteToMove = state.CurrentTurn == PieceColor.White;

            // Stand-pat pruning
            if (whiteToMove)
            {
                if (standPat >= beta) return beta;
                if (standPat > alpha) alpha = standPat;
            }
            else
            {
                if (standPat <= alpha) return alpha;
                if (standPat < beta) beta = standPat;
            }

            if (depth == 0) return standPat;

            // Search captures only
            foreach (var m in MoveGenerator.GetLegalMoves(state))
            {
                bool isCapture = state.Board[m.ToRow, m.ToCol] != Piece.None || m.IsEnPassant;
                if (!isCapture) continue;

                var next = new BoardState(state);
                next.ApplyMove(m);
                int score = Quiescence(next, depth - 1, alpha, beta);

                if (whiteToMove)
                {
                    if (score > alpha) alpha = score;
                    if (alpha >= beta) return beta; // beta cutoff
                }
                else
                {
                    if (score < beta) beta = score;
                    if (beta <= alpha) return alpha; // alpha cutoff
                }
            }

            return whiteToMove ? alpha : beta;
        }

        // W = wins for the player who made the move to reach this node.
        void Backpropagate(MCTSNode node, float whiteResult)
        {
            while (node != null)
            {
                node.N++;
                PieceColor mover = Piece.Opposite(node.State.CurrentTurn);
                node.W += mover == PieceColor.White ? whiteResult : 1f - whiteResult;
                node = node.Parent;
            }
        }
    }
}
