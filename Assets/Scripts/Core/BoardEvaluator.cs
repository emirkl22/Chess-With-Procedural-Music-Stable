namespace Chess
{
    // Material + piece-square table evaluation.
    // Returns positive score = White advantage (centipawns).
    public static class BoardEvaluator
    {
        static readonly int[] PieceValues = { 0, 100, 320, 330, 500, 900, 20000 };

        // All PSTs are from White's perspective, rank8→rank1 top-to-bottom, a→h left-to-right.
        // For White:  idx = (7 - row) * 8 + col
        // For Black:  idx = row * 8 + col   (mirror)
        static readonly int[] PawnPST =
        {
             0,  0,  0,  0,  0,  0,  0,  0,
            50, 50, 50, 50, 50, 50, 50, 50,
            10, 10, 20, 30, 30, 20, 10, 10,
             5,  5, 10, 25, 25, 10,  5,  5,
             0,  0,  0, 20, 20,  0,  0,  0,
             5, -5,-10,  0,  0,-10, -5,  5,
             5, 10, 10,-20,-20, 10, 10,  5,
             0,  0,  0,  0,  0,  0,  0,  0
        };

        static readonly int[] KnightPST =
        {
            -50,-40,-30,-30,-30,-30,-40,-50,
            -40,-20,  0,  0,  0,  0,-20,-40,
            -30,  0, 10, 15, 15, 10,  0,-30,
            -30,  5, 15, 20, 20, 15,  5,-30,
            -30,  0, 15, 20, 20, 15,  0,-30,
            -30,  5, 10, 15, 15, 10,  5,-30,
            -40,-20,  0,  5,  5,  0,-20,-40,
            -50,-40,-30,-30,-30,-30,-40,-50
        };

        static readonly int[] BishopPST =
        {
            -20,-10,-10,-10,-10,-10,-10,-20,
            -10,  0,  0,  0,  0,  0,  0,-10,
            -10,  0,  5, 10, 10,  5,  0,-10,
            -10,  5,  5, 10, 10,  5,  5,-10,
            -10,  0, 10, 10, 10, 10,  0,-10,
            -10, 10, 10, 10, 10, 10, 10,-10,
            -10,  5,  0,  0,  0,  0,  5,-10,
            -20,-10,-10,-10,-10,-10,-10,-20
        };

        static readonly int[] RookPST =
        {
             0,  0,  0,  0,  0,  0,  0,  0,
             5, 10, 10, 10, 10, 10, 10,  5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
             0,  0,  0,  5,  5,  0,  0,  0
        };

        static readonly int[] QueenPST =
        {
            -20,-10,-10, -5, -5,-10,-10,-20,
            -10,  0,  0,  0,  0,  0,  0,-10,
            -10,  0,  5,  5,  5,  5,  0,-10,
             -5,  0,  5,  5,  5,  5,  0, -5,
              0,  0,  5,  5,  5,  5,  0, -5,
            -10,  5,  5,  5,  5,  5,  0,-10,
            -10,  0,  5,  0,  0,  0,  0,-10,
            -20,-10,-10, -5, -5,-10,-10,-20
        };

        static readonly int[] KingPST =
        {
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -20,-30,-30,-40,-40,-30,-30,-20,
            -10,-20,-20,-20,-20,-20,-20,-10,
             20, 20,  0,  0,  0,  0, 20, 20,
             20, 30, 10,  0,  0, 10, 30, 20
        };

        public static int Evaluate(BoardState state)
        {
            int score = 0;
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                {
                    int piece = state.Board[r, c];
                    if (piece == Piece.None) continue;

                    var color = Piece.GetColor(piece);
                    int type  = Piece.GetType(piece);
                    int sign  = color == PieceColor.White ? 1 : -1;

                    score += sign * (PieceValues[type] + GetPST(type, r, c, color));
                }

            score += KingTropism(state, PieceColor.White) - KingTropism(state, PieceColor.Black);
            score -= KingSafety(state, PieceColor.White);
            score += KingSafety(state, PieceColor.Black);

            return score;
        }

        // Bonus for having pieces close to the opponent's king (encourages attacking).
        // Each non-pawn, non-king piece earns (7 - Chebyshev_distance) * 5 cp.
        static int KingTropism(BoardState state, PieceColor attacker)
        {
            PieceColor defender = Piece.Opposite(attacker);
            int kingRow = -1, kingCol = -1;
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                {
                    int p = state.Board[r, c];
                    if (p != Piece.None && Piece.GetColor(p) == defender && Piece.GetType(p) == Piece.King)
                    { kingRow = r; kingCol = c; }
                }
            if (kingRow < 0) return 0;

            int bonus = 0;
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                {
                    int p = state.Board[r, c];
                    if (p == Piece.None || Piece.GetColor(p) != attacker) continue;
                    int type = Piece.GetType(p);
                    if (type == Piece.Pawn || type == Piece.King) continue;
                    int dr = r - kingRow; if (dr < 0) dr = -dr;
                    int dc = c - kingCol; if (dc < 0) dc = -dc;
                    int dist = dr > dc ? dr : dc;
                    bonus += (7 - dist) * 5;
                }
            return bonus;
        }

        // Penalty (positive = bad) for an unsafe king:
        //   +40 if king on central files (cols 2-5, not yet castled)
        //   +20 per missing pawn in the adjacent shield row
        static int KingSafety(BoardState state, PieceColor color)
        {
            int kingRow = -1, kingCol = -1;
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                {
                    int p = state.Board[r, c];
                    if (p != Piece.None && Piece.GetColor(p) == color && Piece.GetType(p) == Piece.King)
                    { kingRow = r; kingCol = c; }
                }
            if (kingRow < 0) return 0;

            int penalty = 0;
            if (kingCol >= 2 && kingCol <= 5) penalty += 40;

            int shieldRow = color == PieceColor.White ? kingRow + 1 : kingRow - 1;
            if (shieldRow >= 0 && shieldRow < 8)
            {
                for (int dc = -1; dc <= 1; dc++)
                {
                    int sc = kingCol + dc;
                    if (sc < 0 || sc >= 8) continue;
                    int p = state.Board[shieldRow, sc];
                    bool hasPawn = p != Piece.None && Piece.GetColor(p) == color && Piece.GetType(p) == Piece.Pawn;
                    if (!hasPawn) penalty += 20;
                }
            }
            return penalty;
        }

        static int GetPST(int type, int row, int col, PieceColor color)
        {
            int idx = color == PieceColor.White
                ? (7 - row) * 8 + col
                : row * 8 + col;

            return type switch
            {
                Piece.Pawn   => PawnPST[idx],
                Piece.Knight => KnightPST[idx],
                Piece.Bishop => BishopPST[idx],
                Piece.Rook   => RookPST[idx],
                Piece.Queen  => QueenPST[idx],
                Piece.King   => KingPST[idx],
                _            => 0
            };
        }
    }
}
