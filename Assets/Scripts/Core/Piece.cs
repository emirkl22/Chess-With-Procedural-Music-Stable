using System;

namespace Chess
{
    public enum PieceColor { White, Black, None }

    public static class Piece
    {
        public const int None   = 0;
        public const int Pawn   = 1;
        public const int Knight = 2;
        public const int Bishop = 3;
        public const int Rook   = 4;
        public const int Queen  = 5;
        public const int King   = 6;

        public static PieceColor GetColor(int piece)
        {
            if (piece > 0) return PieceColor.White;
            if (piece < 0) return PieceColor.Black;
            return PieceColor.None;
        }

        public static int GetType(int piece) => piece < 0 ? -piece : piece;

        public static int Make(int type, PieceColor color) =>
            color == PieceColor.White ? type : color == PieceColor.Black ? -type : 0;

        public static bool IsColor(int piece, PieceColor color) => GetColor(piece) == color;

        public static PieceColor Opposite(PieceColor color) =>
            color == PieceColor.White ? PieceColor.Black : PieceColor.White;
    }
}
