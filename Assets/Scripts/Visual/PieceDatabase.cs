using UnityEngine;

namespace Chess
{
    // ScriptableObject: create one asset via Assets > Create > Chess > Piece Database.
    // Drag the 12 piece sprites from "Pieces and Boards" folder into the fields.
    [CreateAssetMenu(fileName = "PieceDatabase", menuName = "Chess/Piece Database")]
    public class PieceDatabase : ScriptableObject
    {
        [Header("White Pieces")]
        public Sprite whitePawn;
        public Sprite whiteKnight;
        public Sprite whiteBishop;
        public Sprite whiteRook;
        public Sprite whiteQueen;
        public Sprite whiteKing;

        [Header("Black Pieces")]
        public Sprite blackPawn;
        public Sprite blackKnight;
        public Sprite blackBishop;
        public Sprite blackRook;
        public Sprite blackQueen;
        public Sprite blackKing;

        public Sprite GetSprite(int piece)
        {
            var color = Piece.GetColor(piece);
            var type  = Piece.GetType(piece);

            if (color == PieceColor.White)
                return type switch
                {
                    Piece.Pawn   => whitePawn,
                    Piece.Knight => whiteKnight,
                    Piece.Bishop => whiteBishop,
                    Piece.Rook   => whiteRook,
                    Piece.Queen  => whiteQueen,
                    Piece.King   => whiteKing,
                    _            => null
                };
            else
                return type switch
                {
                    Piece.Pawn   => blackPawn,
                    Piece.Knight => blackKnight,
                    Piece.Bishop => blackBishop,
                    Piece.Rook   => blackRook,
                    Piece.Queen  => blackQueen,
                    Piece.King   => blackKing,
                    _            => null
                };
        }
    }
}
