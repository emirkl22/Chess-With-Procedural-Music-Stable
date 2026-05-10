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
                    int mat   = PieceValues[type];
                    int pst   = GetPST(type, r, c, color);
                    int sign  = color == PieceColor.White ? 1 : -1;

                    score += sign * (mat + pst);
                }
            return score;
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
