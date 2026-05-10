using System.Collections.Generic;
using UnityEngine;

namespace Chess
{
    // Draws colored overlays: selection highlight (yellow) and valid-move dots (green).
    public class HighlightManager : MonoBehaviour
    {
        [SerializeField] Color selectionColor  = new Color(1f, 0.92f, 0f, 0.6f);
        [SerializeField] Color validMoveColor  = new Color(0f, 0.8f, 0.2f, 0.45f);
        [SerializeField] Color lastMoveColor   = new Color(0.3f, 0.8f, 1f, 0.4f);
        [SerializeField] int   sortingOrder    = 1;

        readonly List<GameObject> _overlays = new List<GameObject>();
        Sprite _pixel;

        public void ShowSelection(int row, int col, List<Move> legalMoves)
        {
            Clear();
            CreateOverlay(row, col, selectionColor, 0.98f);
            foreach (var m in legalMoves)
                CreateOverlay(m.ToRow, m.ToCol, validMoveColor, 0.4f);
        }

        public void ShowLastMove(Move move)
        {
            CreateOverlay(move.FromRow, move.FromCol, lastMoveColor, 0.7f);
            CreateOverlay(move.ToRow,   move.ToCol,   lastMoveColor, 0.7f);
        }

        public void Clear()
        {
            foreach (var go in _overlays) Destroy(go);
            _overlays.Clear();
        }

        void CreateOverlay(int row, int col, Color color, float scale)
        {
            var go = new GameObject($"Highlight_{row}_{col}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = BoardRenderer.SquareCenter(row, col);
            go.transform.localScale    = Vector3.one * scale;

            var sr          = go.AddComponent<SpriteRenderer>();
            sr.sprite       = GetPixel();
            sr.color        = color;
            sr.sortingOrder = sortingOrder;

            _overlays.Add(go);
        }

        Sprite GetPixel()
        {
            if (_pixel != null) return _pixel;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            _pixel = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            return _pixel;
        }
    }
}
