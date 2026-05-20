using UnityEngine;

namespace Chess
{
    // World-space clickable button.  SelectionManager raycasts and invokes
    // OnClickAction on the matched UIButton.  Briefly flashes when pressed.
    [RequireComponent(typeof(BoxCollider2D))]
    public class UIButton : MonoBehaviour
    {
        public System.Action OnClickAction;

        public Color NormalColor  = new Color(0.20f, 0.28f, 0.40f, 1f);
        public Color PressedColor = new Color(0.55f, 0.78f, 1.00f, 1f);

        SpriteRenderer _sr;
        float          _flashUntil;

        void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            if (_sr != null) _sr.color = NormalColor;
        }

        void Update()
        {
            if (_sr != null && _flashUntil > 0f && Time.time >= _flashUntil)
            {
                _sr.color   = NormalColor;
                _flashUntil = 0f;
            }
        }

        public void OnClick()
        {
            if (_sr != null) { _sr.color = PressedColor; _flashUntil = Time.time + 0.10f; }
            OnClickAction?.Invoke();
        }
    }
}
