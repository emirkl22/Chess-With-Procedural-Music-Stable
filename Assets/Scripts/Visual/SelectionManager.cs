using UnityEngine;
using UnityEngine.InputSystem;

namespace Chess
{
    // Reads mouse input and forwards board-square clicks to GameManager.
    public class SelectionManager : MonoBehaviour
    {
        GameManager _gm;
        Camera      _cam;

        public void Init(GameManager gm, Camera cam)
        {
            _gm  = gm;
            _cam = cam;
        }

        void Update()
        {
            if (_gm == null || _cam == null) return;

            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;

            Vector2 screenPos = mouse.position.ReadValue();

            // For orthographic camera: z = distance from camera to the world plane (board is at z=0)
            float depth = Mathf.Abs(_cam.transform.position.z);
            Vector3 world = _cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, depth));

            // 1. UI buttons (control panel) take priority over board clicks
            var hit = Physics2D.OverlapPoint(new Vector2(world.x, world.y));
            if (hit != null)
            {
                var btn = hit.GetComponent<UIButton>();
                if (btn != null) { btn.OnClick(); return; }
            }

            // 2. Board click — forward to GameManager
            if (BoardRenderer.WorldToBoard(world, out int row, out int col))
                _gm.OnSquareClicked(row, col);
        }
    }
}
