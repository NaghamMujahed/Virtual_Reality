using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PaintBucketSim.Systems.UI
{
    [DisallowMultipleComponent]
    public sealed class LabPanelResizeHandle : MonoBehaviour,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerClickHandler
    {
        private static Texture2D _resizeCursor;

        private PaintBucketLabUI _owner;
        private Image _image;
        private Color _accent;
        private bool _dragging;
        private bool _pointerInside;

        public void Initialize(
            PaintBucketLabUI owner,
            Image image,
            Color accent)
        {
            _owner = owner;
            _image = image;
            _accent = accent;
            UpdateVisual();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _dragging = true;
            SetResizeCursor();
            Resize(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            Resize(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _dragging = false;
            UpdateCursor();
            UpdateVisual();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _pointerInside = true;
            SetResizeCursor();
            UpdateVisual();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _pointerInside = false;
            UpdateCursor();
            UpdateVisual();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.clickCount >= 2)
                _owner?.ResetInspectorPanelWidth();
        }

        private void OnDisable()
        {
            _dragging = false;
            _pointerInside = false;
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        private void Resize(PointerEventData eventData)
        {
            if (_owner == null || eventData == null)
                return;

            _owner.ResizeInspectorPanel(
                eventData.position,
                eventData.pressEventCamera);
            UpdateVisual();
        }

        private void UpdateCursor()
        {
            if (_dragging || _pointerInside)
                SetResizeCursor();
            else
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        private void UpdateVisual()
        {
            if (_image == null)
                return;

            float alpha = _dragging ? 0.95f : (_pointerInside ? 0.65f : 0.08f);
            _image.color = new Color(_accent.r, _accent.g, _accent.b, alpha);
        }

        private static void SetResizeCursor()
        {
            Cursor.SetCursor(GetResizeCursor(), new Vector2(12f, 12f), CursorMode.Auto);
        }

        private static Texture2D GetResizeCursor()
        {
            if (_resizeCursor != null)
                return _resizeCursor;

            const int size = 24;
            _resizeCursor = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "Horizontal UI Resize Cursor",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color32 clear = new Color32(0, 0, 0, 0);
            Color32 white = new Color32(245, 250, 252, 255);
            Color32 outline = new Color32(18, 24, 27, 255);
            Color32[] pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = clear;

            DrawCursorShape(pixels, size, 11, outline);
            DrawCursorShape(pixels, size, 12, white);
            _resizeCursor.SetPixels32(pixels);
            _resizeCursor.Apply(false, false);
            return _resizeCursor;
        }

        private static void DrawCursorShape(
            Color32[] pixels,
            int size,
            int y,
            Color32 color)
        {
            for (int x = 4; x <= 19; x++)
                SetPixel(pixels, size, x, y, color);

            for (int offset = 0; offset < 5; offset++)
            {
                SetPixel(pixels, size, 4 + offset, y + offset, color);
                SetPixel(pixels, size, 4 + offset, y - offset, color);
                SetPixel(pixels, size, 19 - offset, y + offset, color);
                SetPixel(pixels, size, 19 - offset, y - offset, color);
            }
        }

        private static void SetPixel(
            Color32[] pixels,
            int size,
            int x,
            int y,
            Color32 color)
        {
            if (x < 0 || x >= size || y < 0 || y >= size)
                return;

            pixels[y * size + x] = color;
        }
    }
}
