using UnityEngine;

namespace Chess
{
    // Reads mouse input and forwards board-square clicks to GameManager.
    [RequireComponent(typeof(Camera))]
    public class SelectionManager : MonoBehaviour
    {
        GameManager _gm;
        Camera      _cam;

        public void Init(GameManager gm)
        {
            _gm  = gm;
            _cam = GetComponent<Camera>();
        }

        void Update()
        {
            if (!Input.GetMouseButtonDown(0)) return;

            Vector3 world = _cam.ScreenToWorldPoint(Input.mousePosition);
            if (BoardRenderer.WorldToBoard(world, out int row, out int col))
                _gm.OnSquareClicked(row, col);
        }
    }
}
