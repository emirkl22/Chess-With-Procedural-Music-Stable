using System;

namespace Chess
{
    // Immutable-style board state — copy constructor creates a full clone.
    public class BoardState
    {
        public int[,] Board = new int[8, 8];
        public PieceColor CurrentTurn = PieceColor.White;

        // Castling rights: [0]=WhiteKingSide [1]=WhiteQueenSide [2]=BlackKingSide [3]=BlackQueenSide
        public bool[] CastlingRights = new bool[4];

        // En passant target square (-1 if none).  This is the square the capturing pawn lands on.
        public int EnPassantRow = -1;
        public int EnPassantCol = -1;

        public BoardState() { SetupStartPosition(); }

        public BoardState(BoardState src)
        {
            Array.Copy(src.Board, Board, 64);
            CurrentTurn = src.CurrentTurn;
            Array.Copy(src.CastlingRights, CastlingRights, 4);
            EnPassantRow = src.EnPassantRow;
            EnPassantCol = src.EnPassantCol;
        }

        void SetupStartPosition()
        {
            // Row 0 = rank 1 (white back rank), Row 7 = rank 8 (black back rank)
            int[] backRank = { Piece.Rook, Piece.Knight, Piece.Bishop, Piece.Queen,
                               Piece.King, Piece.Bishop, Piece.Knight, Piece.Rook };
            for (int c = 0; c < 8; c++)
            {
                Board[0, c] =  backRank[c];   // white back rank
                Board[1, c] =  Piece.Pawn;    // white pawns
                Board[6, c] = -Piece.Pawn;    // black pawns
                Board[7, c] = -backRank[c];   // black back rank
            }
            for (int i = 0; i < 4; i++) CastlingRights[i] = true;
        }

        public void ApplyMove(Move move)
        {
            int piece     = Board[move.FromRow, move.FromCol];
            int pieceType = Piece.GetType(piece);

            // Place piece on target square (handle promotion)
            Board[move.ToRow, move.ToCol] = move.IsPromotion
                ? Piece.Make(move.PromotionPiece, CurrentTurn)
                : piece;
            Board[move.FromRow, move.FromCol] = Piece.None;

            // En passant: remove the captured pawn which sits one row behind the landing square
            if (move.IsEnPassant)
            {
                int captRow = CurrentTurn == PieceColor.White ? move.ToRow - 1 : move.ToRow + 1;
                Board[captRow, move.ToCol] = Piece.None;
            }

            // Castling: move the rook
            if (move.IsCastling)
            {
                if (move.ToCol == 6) // king-side
                {
                    Board[move.FromRow, 5] = Board[move.FromRow, 7];
                    Board[move.FromRow, 7] = Piece.None;
                }
                else // queen-side
                {
                    Board[move.FromRow, 3] = Board[move.FromRow, 0];
                    Board[move.FromRow, 0] = Piece.None;
                }
            }

            // Update en passant target square
            EnPassantRow = -1; EnPassantCol = -1;
            if (pieceType == Piece.Pawn && Math.Abs(move.ToRow - move.FromRow) == 2)
            {
                EnPassantRow = (move.FromRow + move.ToRow) / 2;
                EnPassantCol = move.FromCol;
            }

            // Update castling rights
            if (pieceType == Piece.King)
            {
                if (CurrentTurn == PieceColor.White) { CastlingRights[0] = false; CastlingRights[1] = false; }
                else                                  { CastlingRights[2] = false; CastlingRights[3] = false; }
            }
            if (pieceType == Piece.Rook)
            {
                if (move.FromRow == 0 && move.FromCol == 7) CastlingRights[0] = false; // WK
                if (move.FromRow == 0 && move.FromCol == 0) CastlingRights[1] = false; // WQ
                if (move.FromRow == 7 && move.FromCol == 7) CastlingRights[2] = false; // BK
                if (move.FromRow == 7 && move.FromCol == 0) CastlingRights[3] = false; // BQ
            }

            if (CurrentTurn == PieceColor.Black) { /* FullMoveNumber++ if needed */ }
            CurrentTurn = Piece.Opposite(CurrentTurn);
        }

        // Returns (-1,-1) if king not found (should never happen in a legal game).
        public (int row, int col) FindKing(PieceColor color)
        {
            int kingPiece = Piece.Make(Piece.King, color);
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                    if (Board[r, c] == kingPiece) return (r, c);
            return (-1, -1);
        }
    }
}
