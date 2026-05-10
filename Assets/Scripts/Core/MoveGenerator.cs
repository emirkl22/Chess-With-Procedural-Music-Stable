using System.Collections.Generic;

namespace Chess
{
    public static class MoveGenerator
    {
        // Returns all moves that are legal (don't leave own king in check).
        public static List<Move> GetLegalMoves(BoardState state)
        {
            var pseudo = GetPseudoLegalMoves(state, state.CurrentTurn);
            var legal  = new List<Move>(pseudo.Count);
            PieceColor mover = state.CurrentTurn;

            foreach (var move in pseudo)
            {
                var next = new BoardState(state);
                next.ApplyMove(move);
                if (!IsInCheck(next, mover))
                    legal.Add(move);
            }
            return legal;
        }

        public static bool IsInCheck(BoardState state, PieceColor color)
        {
            var (kr, kc) = state.FindKing(color);
            if (kr < 0) return true; // king missing — treat as check
            return IsSquareAttacked(state, kr, kc, Piece.Opposite(color));
        }

        // ----- internals -----

        static bool IsSquareAttacked(BoardState state, int row, int col, PieceColor byColor)
        {
            var b = state.Board;

            // Pawn attacks (pawn at byColor's pawn row attacks this square diagonally)
            int pDir = byColor == PieceColor.White ? 1 : -1; // pawns move in +pDir direction
            int pr = row - pDir;                              // row where an attacking pawn would stand
            if (InBounds(pr, col - 1) && b[pr, col - 1] == Piece.Make(Piece.Pawn, byColor)) return true;
            if (InBounds(pr, col + 1) && b[pr, col + 1] == Piece.Make(Piece.Pawn, byColor)) return true;

            // Knight attacks
            int[] ndr = { 2, 2, -2, -2,  1,  1, -1, -1 };
            int[] ndc = { 1,-1,  1, -1,  2, -2,  2, -2 };
            for (int i = 0; i < 8; i++)
            {
                int nr = row + ndr[i], nc = col + ndc[i];
                if (InBounds(nr, nc) && b[nr, nc] == Piece.Make(Piece.Knight, byColor)) return true;
            }

            // Sliding attacks (bishop/rook/queen)
            int[,] dirs = { {1,1},{1,-1},{-1,1},{-1,-1},{1,0},{-1,0},{0,1},{0,-1} };
            for (int d = 0; d < 8; d++)
            {
                bool isDiag = d < 4;
                int r = row + dirs[d, 0], c = col + dirs[d, 1];
                while (InBounds(r, c))
                {
                    int p = b[r, c];
                    if (p != Piece.None)
                    {
                        if (Piece.GetColor(p) == byColor)
                        {
                            int t = Piece.GetType(p);
                            if (t == Piece.Queen)                    return true;
                            if (isDiag  && t == Piece.Bishop)        return true;
                            if (!isDiag && t == Piece.Rook)          return true;
                        }
                        break;
                    }
                    r += dirs[d, 0]; c += dirs[d, 1];
                }
            }

            // King attacks
            for (int dr = -1; dr <= 1; dr++)
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    int nr = row + dr, nc = col + dc;
                    if (InBounds(nr, nc) && b[nr, nc] == Piece.Make(Piece.King, byColor)) return true;
                }

            return false;
        }

        static List<Move> GetPseudoLegalMoves(BoardState state, PieceColor color)
        {
            var moves = new List<Move>(60);
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                {
                    int piece = state.Board[r, c];
                    if (piece == Piece.None || Piece.GetColor(piece) != color) continue;
                    switch (Piece.GetType(piece))
                    {
                        case Piece.Pawn:   AddPawnMoves  (state, r, c, color, moves); break;
                        case Piece.Knight: AddKnightMoves(state, r, c, color, moves); break;
                        case Piece.Bishop: AddSliding    (state, r, c, color, moves, true,  false); break;
                        case Piece.Rook:   AddSliding    (state, r, c, color, moves, false, true);  break;
                        case Piece.Queen:  AddSliding    (state, r, c, color, moves, true,  true);  break;
                        case Piece.King:   AddKingMoves  (state, r, c, color, moves); break;
                    }
                }
            return moves;
        }

        static void AddPawnMoves(BoardState state, int r, int c, PieceColor color, List<Move> moves)
        {
            var b       = state.Board;
            int dir     = color == PieceColor.White ? 1 : -1;
            int startR  = color == PieceColor.White ? 1 : 6;
            int promR   = color == PieceColor.White ? 7 : 0;

            int nr = r + dir;
            if (!InBounds(nr, c)) return;

            // Single push
            if (b[nr, c] == Piece.None)
            {
                if (nr == promR) AddPromotions(r, c, nr, c, 0, moves);
                else             moves.Add(new Move(r, c, nr, c));

                // Double push from starting rank
                if (r == startR)
                {
                    int nr2 = r + 2 * dir;
                    if (b[nr2, c] == Piece.None)
                        moves.Add(new Move(r, c, nr2, c));
                }
            }

            // Diagonal captures + en passant
            foreach (int dc in new[] { -1, 1 })
            {
                int nc = c + dc;
                if (!InBounds(nr, nc)) continue;

                int target = b[nr, nc];
                if (target != Piece.None && Piece.GetColor(target) != color)
                {
                    if (nr == promR) AddPromotions(r, c, nr, nc, target, moves);
                    else             moves.Add(new Move(r, c, nr, nc));
                }
                else if (state.EnPassantRow == nr && state.EnPassantCol == nc)
                {
                    moves.Add(new Move(r, c, nr, nc) { IsEnPassant = true });
                }
            }
        }

        static void AddPromotions(int fr, int fc, int tr, int tc, int captured, List<Move> moves)
        {
            foreach (int promo in new[] { Piece.Queen, Piece.Rook, Piece.Bishop, Piece.Knight })
                moves.Add(new Move(fr, fc, tr, tc) { PromotionPiece = promo });
        }

        static void AddKnightMoves(BoardState state, int r, int c, PieceColor color, List<Move> moves)
        {
            int[] dr = { 2, 2, -2, -2,  1,  1, -1, -1 };
            int[] dc = { 1,-1,  1, -1,  2, -2,  2, -2 };
            for (int i = 0; i < 8; i++)
            {
                int nr = r + dr[i], nc = c + dc[i];
                if (!InBounds(nr, nc)) continue;
                int target = state.Board[nr, nc];
                if (target == Piece.None || Piece.GetColor(target) != color)
                    moves.Add(new Move(r, c, nr, nc));
            }
        }

        static void AddSliding(BoardState state, int r, int c, PieceColor color,
                               List<Move> moves, bool diag, bool straight)
        {
            int start = diag ? 0 : 4, end = straight ? 8 : 4;
            int[,] dirs = { {1,1},{1,-1},{-1,1},{-1,-1},{1,0},{-1,0},{0,1},{0,-1} };
            for (int d = start; d < end; d++)
            {
                int nr = r + dirs[d, 0], nc = c + dirs[d, 1];
                while (InBounds(nr, nc))
                {
                    int target = state.Board[nr, nc];
                    if (target != Piece.None)
                    {
                        if (Piece.GetColor(target) != color) moves.Add(new Move(r, c, nr, nc));
                        break;
                    }
                    moves.Add(new Move(r, c, nr, nc));
                    nr += dirs[d, 0]; nc += dirs[d, 1];
                }
            }
        }

        static void AddKingMoves(BoardState state, int r, int c, PieceColor color, List<Move> moves)
        {
            for (int dr = -1; dr <= 1; dr++)
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    int nr = r + dr, nc = c + dc;
                    if (!InBounds(nr, nc)) continue;
                    int t = state.Board[nr, nc];
                    if (t == Piece.None || Piece.GetColor(t) != color)
                        moves.Add(new Move(r, c, nr, nc));
                }

            // Castling
            int backR = color == PieceColor.White ? 0 : 7;
            if (r != backR || c != 4) return;
            PieceColor opp = Piece.Opposite(color);
            int ki = color == PieceColor.White ? 0 : 2; // castling rights indices

            // King-side
            if (state.CastlingRights[ki] &&
                state.Board[backR, 5] == Piece.None && state.Board[backR, 6] == Piece.None &&
                !IsSquareAttacked(state, backR, 4, opp) &&
                !IsSquareAttacked(state, backR, 5, opp) &&
                !IsSquareAttacked(state, backR, 6, opp))
            {
                moves.Add(new Move(r, c, backR, 6) { IsCastling = true });
            }

            // Queen-side
            if (state.CastlingRights[ki + 1] &&
                state.Board[backR, 3] == Piece.None &&
                state.Board[backR, 2] == Piece.None &&
                state.Board[backR, 1] == Piece.None &&
                !IsSquareAttacked(state, backR, 4, opp) &&
                !IsSquareAttacked(state, backR, 3, opp) &&
                !IsSquareAttacked(state, backR, 2, opp))
            {
                moves.Add(new Move(r, c, backR, 2) { IsCastling = true });
            }
        }

        static bool InBounds(int r, int c) => (uint)r < 8 && (uint)c < 8;
    }
}
