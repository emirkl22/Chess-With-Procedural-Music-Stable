namespace Chess
{
    public struct Move
    {
        public int FromRow, FromCol, ToRow, ToCol;
        public int PromotionPiece; // 0 = none; otherwise Piece.Queen/Rook/Bishop/Knight
        public bool IsEnPassant;
        public bool IsCastling;

        public Move(int fr, int fc, int tr, int tc)
        {
            FromRow = fr; FromCol = fc;
            ToRow   = tr; ToCol   = tc;
            PromotionPiece = 0;
            IsEnPassant = false;
            IsCastling  = false;
        }

        public bool IsPromotion => PromotionPiece != 0;

        public override string ToString() =>
            $"{ColLetter(FromCol)}{FromRow + 1}{ColLetter(ToCol)}{ToRow + 1}" +
            (IsPromotion ? $"={PromotionPiece}" : "");

        static char ColLetter(int col) => (char)('a' + col);
    }
}
