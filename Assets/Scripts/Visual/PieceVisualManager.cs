using UnityEngine;

namespace Chess
{
    // Maintains a pool of GameObjects (one per piece on the board).
    // Call Refresh() after every move to sync visuals with BoardState.
    public class PieceVisualManager : MonoBehaviour
    {
        [SerializeField] public PieceDatabase pieceDatabase;
        [SerializeField] int sortingOrder = 2;

        GameObject[,] _pieceGOs = new GameObject[8, 8];
        Sprite        _fallback; // colored square used when no sprite is assigned

        public void Refresh(BoardState state)
        {
            // Remove all existing piece GameObjects
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                    if (_pieceGOs[r, c] != null)
                    {
                        Destroy(_pieceGOs[r, c]);
                        _pieceGOs[r, c] = null;
                    }

            // Recreate
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                {
                    int piece = state.Board[r, c];
                    if (piece == Chess.Piece.None) continue;
                    _pieceGOs[r, c] = CreatePieceGO(piece, r, c);
                }
        }

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
                // Scale sprite so it fits within ~0.9 units
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
                // Label with piece letter
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

            // TextMesh as a lightweight in-world label
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
