using UnityEngine;

namespace Chess
{
    // Creates the 8x8 board as colored square SpriteRenderers.
    // Each square is 1 world-unit wide; board occupies (0,0)–(8,8).
    public class BoardRenderer : MonoBehaviour
    {
        [SerializeField] Color lightSquareColor = new Color(0.93f, 0.85f, 0.72f);
        [SerializeField] Color darkSquareColor  = new Color(0.71f, 0.53f, 0.39f);
        [SerializeField] int   sortingOrder     = 0;

        Sprite _whitePixel;

        public void Build()
        {
            _whitePixel = MakeWhitePixel();
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                    CreateSquare(r, c);
        }

        void CreateSquare(int row, int col)
        {
            var go = new GameObject($"Square_{row}_{col}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = SquareCenter(row, col);

            var sr          = go.AddComponent<SpriteRenderer>();
            sr.sprite       = _whitePixel;
            sr.color        = (row + col) % 2 == 0 ? darkSquareColor : lightSquareColor;
            sr.sortingOrder = sortingOrder;
        }

        // Square center in local space (same as world space when transform is at origin)
        public static Vector3 SquareCenter(int row, int col) =>
            new Vector3(col + 0.5f, row + 0.5f, 0f);

        // Board-space position → (row, col); returns false if out of bounds
        public static bool WorldToBoard(Vector3 worldPos, out int row, out int col)
        {
            row = Mathf.FloorToInt(worldPos.y);
            col = Mathf.FloorToInt(worldPos.x);
            return row >= 0 && row < 8 && col >= 0 && col < 8;
        }

        static Sprite MakeWhitePixel()
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
    }
}
