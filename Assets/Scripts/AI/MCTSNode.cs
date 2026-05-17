using System.Collections.Generic;

namespace Chess
{
    public class MCTSNode
    {
        public readonly BoardState State;
        public readonly Move?      MoveThatLedHere; // null for root
        public readonly MCTSNode   Parent;

        public List<MCTSNode> Children    = new List<MCTSNode>();
        public List<Move>     UntriedMoves;

        public int   N = 0;     // visit count
        public float W = 0f;    // wins for the player who MADE THE MOVE to reach this node

        // Win rate for the player who made the move to reach this node.
        // UCB selection always maximizes Q — hangi seviyede olursa olsun doğru çalışır.
        public float Q => N > 0 ? W / N : 0f;

        public bool IsTerminal      => UntriedMoves.Count == 0 && Children.Count == 0;
        public bool IsFullyExpanded => UntriedMoves.Count == 0;

        public MCTSNode(BoardState state, Move? move = null, MCTSNode parent = null)
        {
            State             = state;
            MoveThatLedHere   = move;
            Parent            = parent;
            UntriedMoves      = MoveGenerator.GetLegalMoves(state);
        }
    }
}
