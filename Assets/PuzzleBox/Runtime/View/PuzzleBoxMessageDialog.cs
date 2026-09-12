using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PuzzleBox.View
{
    /// <summary>A dismissible runtime modal; owns no rule state and never resumes a failed game by itself.</summary>
    public sealed class PuzzleBoxMessageDialog : MonoBehaviour
    {
        private Canvas canvas;
        private RectTransform panel;
        private Text title;
        private Text body;
        private Text badge;
        private Text closeLabel;
        private Image stripe;
        private Font font;
        private Sprite rounded;
        private Action undo;
        private Action restart;
        public event Action Dismissed;
        public bool IsOpen => canvas != null && canvas.gameObject.activeSelf;
        public int OpenedFrame { get; private set; }
        public PuzzleBoxFeedbackMessage Message { get; private set; }
        public Canvas RootCanvas => canvas;
        public Button CloseButton { get; private set; }
        public Button CornerCloseButton { get; private set; }
        public Button UndoButton { get; private set; }
        public Button RestartButton { get; private set; }

        public void ConfigureActions(Action undoAction, Action restartAction)
        { undo = undoAction; restart = restartAction; }

        public void Show(PuzzleBoxFeedbackMessage message, bool canUndo)
        {
            EnsureBuilt();
            Message = message;
            title.text = message.Title;
            body.text = message.Body;
            var success = message.Kind == PuzzleBoxMessageKind.Success;
            var blocked = message.Kind == PuzzleBoxMessageKind.Blocked;
            var accent = success ? new Color(.29f, .48f, .30f) : blocked ? new Color(.65f, .43f, .17f) : new Color(.67f, .32f, .25f);
            stripe.color = accent;
            badge.color = accent;
            badge.text = success ? "DELIVERY COMPLETE" : blocked ? "PATH BLOCKED" : "TRY ANOTHER WAY";
            closeLabel.text = success ? "继续观察" : "知道了";
            CloseButton.GetComponent<Image>().color = accent;
            UndoButton.gameObject.SetActive(!blocked);
            UndoButton.interactable = canUndo;
            RestartButton.gameObject.SetActive(!blocked);
            Place(CloseButton.GetComponent<RectTransform>(), blocked ? 230f : 424f, 264f, 160f, 48f);
            canvas.gameObject.SetActive(true);
            OpenedFrame = Time.frameCount;
            // No automatic button focus: the Space that moved the cat must not dismiss this.
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        public void Hide()
        {
            if (!IsOpen) return;
            canvas.gameObject.SetActive(false);
            Dismissed?.Invoke();
        }

        private void EnsureBuilt()
        {
            if (canvas != null) return;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Microsoft YaHei UI", "PingFang SC", "Noto Sans CJK SC", "Arial Unicode MS", "Arial" }, 24);
            font.hideFlags = HideFlags.DontSave;
            rounded = CreateRoundedSprite();
            var root = new GameObject("Puzzle Box Message UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.hideFlags = HideFlags.DontSave;
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(700, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var overlay = Image("Input-blocking shade", root.transform, new Color(.10f, .16f, .10f, .62f), false);
            Stretch(overlay.rectTransform);
            var shadow = Image("Card shadow", root.transform, new Color(.08f, .14f, .08f, .32f), true);
            Center(shadow.rectTransform, new Vector2(628f, 356f), new Vector2(0f, -8f));
            var paper = Image("Paper card", root.transform, new Color(.98f, .97f, .91f), true);
            panel = paper.rectTransform;
            Center(panel, new Vector2(620f, 350f), Vector2.zero);
            stripe = Image("Status stripe", panel, Color.white, true);
            Place(stripe.rectTransform, 30, 26, 6, 40);
            badge = Label("Badge", panel, 14, FontStyle.Bold);
            Place(badge.rectTransform, 50, 26, 475, 30);
            title = Label("Title", panel, 36, FontStyle.Bold);
            Place(title.rectTransform, 36, 76, 548, 58);
            body = Label("Explanation", panel, 22, FontStyle.Normal);
            body.lineSpacing = 1.15f;
            Place(body.rectTransform, 36, 144, 548, 103);
            var hint = Label("Dismiss hint", panel, 12, FontStyle.Normal);
            hint.color = new Color(.46f, .51f, .41f);
            hint.text = "按任意键关闭提示 · 关闭不会撤销当前状态";
            Place(hint.rectTransform, 36, 322, 548, 20);
            CornerCloseButton = MakeButton("Close X", "×", 538, 18, 54, 46, Hide, false);
            UndoButton = MakeButton("Undo", "撤销一步", 36, 264, 176, 48, () => { Hide(); undo?.Invoke(); }, false);
            RestartButton = MakeButton("Restart", "重新开始", 230, 264, 176, 48, () => { Hide(); restart?.Invoke(); }, false);
            CloseButton = MakeButton("Dismiss", "知道了", 424, 264, 160, 48, Hide, true);
            closeLabel = CloseButton.GetComponentInChildren<Text>();
            if (EventSystem.current == null && FindObjectOfType<EventSystem>() == null)
            {
                var events = new GameObject("Puzzle Box UI Events", typeof(EventSystem), typeof(StandaloneInputModule));
                events.transform.SetParent(transform, false);
                events.hideFlags = HideFlags.DontSave;
            }
            root.SetActive(false);
        }

        private Button MakeButton(string name, string text, float x, float y, float width, float height, Action click, bool primary)
        {
            var image = Image(name, panel, primary ? new Color(.29f, .48f, .30f) : new Color(.87f, .89f, .80f), true);
            Place(image.rectTransform, x, y, width, height);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, 1f, .9f);
            colors.pressedColor = new Color(.8f, .85f, .73f);
            button.colors = colors;
            button.onClick.AddListener(() => click());
            var label = Label("Label", image.transform, 20, FontStyle.Bold);
            label.text = text;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = primary ? new Color(.99f, .98f, .94f) : new Color(.25f, .34f, .24f);
            Stretch(label.rectTransform);
            return button;
        }

        private Image Image(string name, Transform parent, Color color, bool round)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            if (round) { image.sprite = rounded; image.type = UnityEngine.UI.Image.Type.Sliced; }
            return image;
        }

        private Text Label(string name, Transform parent, int size, FontStyle style)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var label = go.GetComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = new Color(.23f, .32f, .23f);
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }

        private static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
        }
        private static void Center(RectTransform rect, Vector2 size, Vector2 offset)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
            rect.sizeDelta = size;
            rect.anchoredPosition = offset;
        }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }

        private static Sprite CreateRoundedSprite()
        {
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
            texture.wrapMode = TextureWrapMode.Clamp;
            for (var y = 0; y < 32; y++)
            for (var x = 0; x < 32; x++)
            {
                var dx = Mathf.Max(Mathf.Abs(x - 15.5f) - 8f, 0f);
                var dy = Mathf.Max(Mathf.Abs(y - 15.5f) - 8f, 0f);
                texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(8f - Mathf.Sqrt(dx * dx + dy * dy))));
            }
            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0, 0, 32, 32), Vector2.one * .5f, 100f, 0,
                SpriteMeshType.FullRect, Vector4.one * 8f);
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        private void OnDestroy()
        {
            Release(font);
            if (rounded != null) { Release(rounded.texture); Release(rounded); }
        }
        private static void Release(UnityEngine.Object value)
        { if (value != null) { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); } }
    }
}
