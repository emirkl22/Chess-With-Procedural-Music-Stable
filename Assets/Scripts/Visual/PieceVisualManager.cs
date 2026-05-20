using System.Collections;
using UnityEngine;

namespace Chess
{
    // Maintains a pool of GameObjects (one per piece on the board).
    //   * Refresh(state)            — snap-refresh everything (used at startup
    //                                 and when navigating move history).
    //   * AnimateMove(move, ...)    — smoothly lerps the moved piece to its
    //                                 destination; handles captures, en passant,
    //                                 promotion, and castling.
    public class PieceVisualManager : MonoBehaviour
    {
        [SerializeField] public PieceDatabase pieceDatabase;
        [SerializeField] int sortingOrder = 2;

        GameObject[,] _pieceGOs = new GameObject[8, 8];
        Sprite        _fallback;

        // -------------------------------------------------------------------
        // Snap refresh — rebuilds every piece GO from the given board state
        // -------------------------------------------------------------------
        public void Refresh(BoardState state)
        {
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                    if (_pieceGOs[r, c] != null)
                    {
                        Destroy(_pieceGOs[r, c]);
                        _pieceGOs[r, c] = null;
                    }

            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                {
                    int piece = state.Board[r, c];
                    if (piece == Chess.Piece.None) continue;
                    _pieceGOs[r, c] = CreatePieceGO(piece, r, c);
                }
        }

        // -------------------------------------------------------------------
        // Animated move
        //   move        — the move being played
        //   postState   — board state AFTER the move (used for promotion sprite)
        //   mover       — colour of the piece being moved (needed for en-passant)
        //   duration    — animation length in seconds
        // -------------------------------------------------------------------
        public IEnumerator AnimateMove(Move move, BoardState postState,
                                       PieceColor mover, float duration)
        {
            duration = Mathf.Max(0.02f, duration);

            var srcGO = _pieceGOs[move.FromRow, move.FromCol];
            if (srcGO == null)
            {
                // Source GO missing — fall back to a snap refresh
                Refresh(postState);
                yield break;
            }

            // Render moving piece on top while it travels
            var srcSR = srcGO.GetComponent<SpriteRenderer>();
            int origOrder = 0;
            if (srcSR != null) { origOrder = srcSR.sortingOrder; srcSR.sortingOrder = origOrder + 10; }

            // Remove any captured piece sitting on the destination square
            if (_pieceGOs[move.ToRow, move.ToCol] != null)
            {
                Destroy(_pieceGOs[move.ToRow, move.ToCol]);
                _pieceGOs[move.ToRow, move.ToCol] = null;
            }
            // En passant — the captured pawn is one row behind the destination
            if (move.IsEnPassant)
            {
                int capRow = mover == PieceColor.White ? move.ToRow - 1 : move.ToRow + 1;
                if (capRow >= 0 && capRow < 8 && _pieceGOs[capRow, move.ToCol] != null)
                {
                    Destroy(_pieceGOs[capRow, move.ToCol]);
                    _pieceGOs[capRow, move.ToCol] = null;
                }
            }

            // Lerp position from source square to destination square
            Vector3 from = BoardRenderer.SquareCenter(move.FromRow, move.FromCol);
            Vector3 to   = BoardRenderer.SquareCenter(move.ToRow,   move.ToCol);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                float t     = elapsed / duration;
                float eased = 1f - Mathf.Pow(1f - t, 3f);   // ease-out cubic
                srcGO.transform.localPosition = Vector3.Lerp(from, to, eased);
                elapsed += Time.deltaTime;
                yield return null;
            }
            srcGO.transform.localPosition = to;
            if (srcSR != null) srcSR.sortingOrder = origOrder;

            // Update tracking array
            _pieceGOs[move.ToRow,   move.ToCol]   = srcGO;
            _pieceGOs[move.FromRow, move.FromCol] = null;

            // Promotion — destroy the pawn GO and replace with promoted-piece sprite
            if (move.IsPromotion)
            {
                Destroy(srcGO);
                int newPiece = postState.Board[move.ToRow, move.ToCol];
                _pieceGOs[move.ToRow, move.ToCol] = CreatePieceGO(newPiece, move.ToRow, move.ToCol);
            }

            // Castling — snap the rook into place (no animation; it's adjacent anyway)
            if (move.IsCastling)
            {
                int rookFromCol = move.ToCol == 6 ? 7 : 0;
                int rookToCol   = move.ToCol == 6 ? 5 : 3;
                var rookGO = _pieceGOs[move.FromRow, rookFromCol];
                if (rookGO != null)
                {
                    rookGO.transform.localPosition =
                        BoardRenderer.SquareCenter(move.FromRow, rookToCol);
                    _pieceGOs[move.FromRow, rookToCol]   = rookGO;
                    _pieceGOs[move.FromRow, rookFromCol] = null;
                }
            }
        }

        // -------------------------------------------------------------------
        // Internal helpers (unchanged from original)
        // -------------------------------------------------------------------
        GameObject CreatePieceGO(int piece, int row, int col)
        {
            var go = new GameObject($"Piece_{piece}_{row}_{col}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = BoardRenderer.SquareCenter(row, col);

            var sr          = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = sortingOrder;

            Sprite sp = pieceDatabase != null ? pieceDatabase.GetSprite(piece) : null;
            if (sp != null)
            {
                sr.sprite = sp;
                float size = 0.9f / Mathf.Max(sp.bounds.size.x, sp.bounds.size.y);
                go.transform.localScale = Vector3.one * size;
            }
            else
            {
                sr.sprite = GetFallback();
                sr.color  = Piece.GetColor(piece) == PieceColor.White
                            ? new Color(1f, 1f, 0.9f)
                            : new Color(0.15f, 0.15f, 0.15f);
                go.transform.localScale = Vector3.one * 0.7f;
                AddPieceLabel(go, piece);
            }
            return go;
        }

        void AddPieceLabel(GameObject parent, int piece)
        {
            string letter = Piece.GetType(piece) switch
            {
                Chess.Piece.Pawn   => "P",
                Chess.Piece.Knight => "N",
                Chess.Piece.Bishop => "B",
                Chess.Piece.Rook   => "R",
                Chess.Piece.Queen  => "Q",
                Chess.Piece.King   => "K",
                _                  => "?"
            };
            if (Piece.GetColor(piece) == PieceColor.Black) letter = letter.ToLower();

            var textGO = new GameObject("Label");
            textGO.transform.SetParent(parent.transform, false);
            textGO.transform.localPosition = Vector3.zero;

            var tm          = textGO.AddComponent<TextMesh>();
            tm.text         = letter;
            tm.fontSize     = 18;
            tm.alignment    = TextAlignment.Center;
            tm.anchor       = TextAnchor.MiddleCenter;
            tm.color        = Piece.GetColor(piece) == PieceColor.White ? Color.black : Color.white;
            textGO.transform.localScale = Vector3.one * 0.06f;
        }

        Sprite GetFallback()
        {
            if (_fallback != null) return _fallback;
            var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            for (int x = 0; x < 32; x++)
                for (int y = 0; y < 32; y++)
                {
                    float cx = x - 15.5f, cy = y - 15.5f;
                    tex.SetPixel(x, y, cx * cx + cy * cy < 200f ? Color.white : Color.clear);
                }
            tex.Apply();
            _fallback = Sprite.Create(tex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 32f);
            return _fallback;
        }
    }
}
