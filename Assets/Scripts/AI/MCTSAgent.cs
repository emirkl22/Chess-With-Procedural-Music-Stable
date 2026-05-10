using System;
using System.Collections;
using UnityEngine;

namespace Chess
{
    // MCTS agent implementing the algorithm described in Progress Report 2.
    // Runs as a coroutine to avoid blocking the Unity main thread.
    public class MCTSAgent : MonoBehaviour
    {
        [Tooltip("Number of MCTS simulations per move (100–300 recommended)")]
        public int SimulationsPerMove = 200;

        [Tooltip("Maximum random-playout depth per simulation")]
        public int MaxPlayoutDepth = 25;

        const float C_UCT = 1.41421356f; // sqrt(2) exploration constant

        // Exposed for AudioBridge after each move
        public float LastRootWinRate  { get; private set; }
        public int   LastVisitCount   { get; private set; }

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        // Yields control back to Unity every 50 sims to keep the game responsive.
        public IEnumerator FindBestMove(BoardState state, Action<Move, float, int> onComplete)
        {
            var root = new MCTSNode(new BoardState(state));

            for (int i = 0; i < SimulationsPerMove; i++)
            {
                var leaf = Select(root);
                if (!leaf.IsTerminal) leaf = Expand(leaf);
                float result = Simulate(leaf.State);
                Backpropagate(leaf, result);

                if (i % 50 == 49) yield return null;
            }

            // Pick the child with the highest visit count (most robust criterion)
            MCTSNode best = null;
            foreach (var child in root.Children)
                if (best == null || child.N > best.N)
                    best = child;

            LastVisitCount  = root.N;
            LastRootWinRate = best != null ? best.Q : 0.5f;

            if (best?.MoveThatLedHere.HasValue == true)
                onComplete(best.MoveThatLedHere.Value, LastRootWinRate, LastVisitCount);
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
                    // UCB1: Q from current node's perspective + exploration bonus
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

            int  idx   = UnityEngine.Random.Range(0, node.UntriedMoves.Count);
            Move move  = node.UntriedMoves[idx];
            node.UntriedMoves.RemoveAt(idx);

            var childState = new BoardState(node.State);
            childState.ApplyMove(move);

            var child = new MCTSNode(childState, move, node);
            node.Children.Add(child);
            return child;
        }

        // Returns win probability for White (1.0 = White wins, 0.0 = Black wins).
        float Simulate(BoardState startState)
        {
            var sim = new BoardState(startState);

            for (int depth = 0; depth < MaxPlayoutDepth; depth++)
            {
                var moves = MoveGenerator.GetLegalMoves(sim);
                if (moves.Count == 0)
                {
                    if (MoveGenerator.IsInCheck(sim, sim.CurrentTurn))
                    {
                        // Checkmate: the player to move has lost
                        return sim.CurrentTurn == PieceColor.White ? 0f : 1f;
                    }
                    return 0.5f; // stalemate
                }

                sim.ApplyMove(moves[UnityEngine.Random.Range(0, moves.Count)]);
            }

            // Playout limit: use static evaluation as proxy
            float eval = BoardEvaluator.Evaluate(sim);
            return Mathf.Clamp01(0.5f + eval / 3000f);
        }

        // W tracks cumulative White-wins.  Every node in the path gets updated.
        void Backpropagate(MCTSNode node, float whiteResult)
        {
            while (node != null)
            {
                node.N++;
                node.W += whiteResult;
                node = node.Parent;
            }
        }
    }
}
