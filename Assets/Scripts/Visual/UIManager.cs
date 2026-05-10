using UnityEngine;

namespace Chess
{
    // In-world TextMesh UI: turn indicator, game-over message, AI status.
    // Positioned above the board (y > 8).
    public class UIManager : MonoBehaviour
    {
        TextMesh _turnLabel;
        TextMesh _statusLabel;
        TextMesh _evalLabel;

        public void Build()
        {
            _turnLabel   = CreateLabel("TurnLabel",   new Vector3(4f, 8.6f, 0f), 24, Color.white);
            _statusLabel = CreateLabel("StatusLabel", new Vector3(4f, 9.1f, 0f), 20, Color.yellow);
            _evalLabel   = CreateLabel("EvalLabel",   new Vector3(4f, 8.1f, 0f), 16, new Color(0.8f,0.8f,0.8f));
        }

        public void SetTurn(PieceColor turn) =>
            SetText(_turnLabel, turn == PieceColor.White ? "White's turn" : "Black's turn (AI thinking…)");

        public void SetAIThinking(bool thinking) =>
            SetText(_statusLabel, thinking ? "AI is thinking…" : "");

        public void SetGameOver(string result) =>
            SetText(_statusLabel, result);

        public void SetEvalInfo(float winRate, float delta, int visits)
        {
            string harmony = winRate > 0.6f ? "Major" : winRate < 0.4f ? "Minor" : "Neutral";
            SetText(_evalLabel, $"Q={winRate:F2}  ΔQ={delta:+0.00;-0.00}  N={visits}  [{harmony}]");
        }

        void SetText(TextMesh tm, string text)
        {
            if (tm != null) tm.text = text;
        }

        TextMesh CreateLabel(string name, Vector3 pos, int fontSize, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position   = pos;
            go.transform.localScale = Vector3.one * 0.12f;

            var tm          = go.AddComponent<TextMesh>();
            tm.fontSize     = fontSize;
            tm.alignment    = TextAlignment.Center;
            tm.anchor       = TextAnchor.MiddleCenter;
            tm.color        = color;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sortingOrder = 5;

            return tm;
        }
    }
}
