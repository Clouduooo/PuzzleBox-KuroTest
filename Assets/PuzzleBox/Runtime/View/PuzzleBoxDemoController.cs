using System.Collections;
using PuzzleBox.Data;
using PuzzleBox.Rules;
using UnityEngine;

namespace PuzzleBox.View
{
    [RequireComponent(typeof(PuzzleBoxLevelView))]
    public sealed class PuzzleBoxDemoController : MonoBehaviour
    {
        [SerializeField] private LevelDefinition level;
        [SerializeField] private PuzzleBoxAudioSettings audioSettings;

        private PuzzleBoxLevelView levelView;
        private PuzzleBoxRuleEngine engine;
        private PuzzleBoxIsometricCameraController cameraController;
        private GridDirection facingDirection = GridDirection.South;
        private bool isAnimating;
        private GUIStyle heading;
        private GUIStyle caption;
        private GUIStyle button;
        private PuzzleBoxAudioPlayer audioPlayer;
        private PuzzleBoxMessageDialog messageDialog;
        private int inputResumeFrame;
        private static readonly KeyCode[] KeyboardKeys = System.Array.FindAll(
            (KeyCode[])System.Enum.GetValues(typeof(KeyCode)),
            key => (int)key > (int)KeyCode.None && (int)key < (int)KeyCode.Mouse0);
        private static readonly System.Func<KeyCode, bool> ReadKeyDown = Input.GetKeyDown;

        public GridDirection FacingDirection => facingDirection;
        public bool IsAnimating => isAnimating || (cameraController != null && cameraController.IsRotating);
        public PuzzleBoxMessageDialog MessageDialog => messageDialog;

        public void SetLevel(LevelDefinition value)
        {
            level = value;
            if (levelView != null) levelView.SetLevel(value);
        }

        private void Start()
        {
            levelView = GetComponent<PuzzleBoxLevelView>();
            audioPlayer = PuzzleBoxAudioPlayer.Ensure(audioSettings);
            levelView.CatLanded += PlayCatSound;
            levelView.BoxesLanded += PlayBoxSound;
            messageDialog = GetComponent<PuzzleBoxMessageDialog>();
            if (messageDialog == null) messageDialog = gameObject.AddComponent<PuzzleBoxMessageDialog>();
            messageDialog.ConfigureActions(UndoAction, Restart);
            messageDialog.Dismissed += OnMessageDismissed;
            if (level == null) level = levelView.Level;
            if (level != null && level.ArtTheme != null)
            {
                if (Camera.main != null)
                {
                    cameraController = Camera.main.GetComponent<PuzzleBoxIsometricCameraController>();
                    if (cameraController == null)
                        cameraController = Camera.main.gameObject.AddComponent<PuzzleBoxIsometricCameraController>();
                    cameraController.Initialize(level);
                }
                if (RenderSettings.sun != null) level.ArtTheme.ConfigureSun(RenderSettings.sun);
            }
            levelView.SetPlayerFacing(facingDirection);
            Restart();
        }

        private void Update()
        {
            if (ConsumeModalInput(ReadKeyDown)) return;
            if (cameraController != null) cameraController.ReadInput();
            if (engine == null) return;
            if (IsAnimating) return;
            if (Input.GetKeyDown(KeyCode.R)) { Restart(); return; }
            if (Input.GetKeyDown(KeyCode.Z)) { UndoAction(); return; }
            if (Input.GetKeyDown(KeyCode.Q)) { RotateView(-1); return; }
            if (Input.GetKeyDown(KeyCode.E)) { RotateView(1); return; }

            GridDirection? viewDirection = null;
            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) viewDirection = GridDirection.North;
            else if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) viewDirection = GridDirection.East;
            else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) viewDirection = GridDirection.South;
            else if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) viewDirection = GridDirection.West;

            if (viewDirection.HasValue)
            {
                var worldDirection = cameraController == null
                    ? viewDirection.Value
                    : cameraController.ViewToWorldDirection(viewDirection.Value);
                SetFacing(worldDirection);
                return;
            }

            if (Input.GetKeyDown(KeyCode.Space)) Move(facingDirection);
        }

        private bool ConsumeModalInput(System.Func<KeyCode, bool> isKeyDown)
        {
            if (messageDialog != null && messageDialog.IsOpen)
            {
                // Ignore the key that opened the modal, including startup/animation completion.
                if (Time.frameCount > messageDialog.OpenedFrame)
                    foreach (var key in KeyboardKeys)
                        if (isKeyDown(key)) { messageDialog.Hide(); break; }
                // Mouse buttons are deliberately excluded so UI Undo/Restart still receive clicks.
                // Per-key edges also work when another key is already held.
                return true;
            }
            return Time.frameCount <= inputResumeFrame;
        }

        private void SetFacing(GridDirection direction)
        {
            facingDirection = direction;
            levelView.SetPlayerFacing(direction);
        }

        private void RotateView(int turns)
        {
            if (!engine.RotateView(turns)) return;
            cameraController?.RotateQuarter(turns);
            levelView.SetViewQuarter(engine.State.ViewQuarter);
        }

        private void UndoAction()
        {
            if (!engine.Undo()) return;
            messageDialog?.Hide();
            cameraController?.SetViewQuarter(engine.State.ViewQuarter);
            levelView.ShowState(engine.State);
        }

        private void Move(GridDirection direction)
        {
            if (IsAnimating || (messageDialog != null && messageDialog.IsOpen)) return;
            if (engine.State.IsComplete || engine.State.Failure != State.FailureReason.None) return;
            var result = engine.TryMove(direction);
            if (!result.Succeeded)
            {
                messageDialog.Show(PuzzleBoxFeedbackMessage.Blocked(result.BlockReason), engine.CanUndo);
                return;
            }
            StartCoroutine(AnimateMove(result));
        }

        private IEnumerator AnimateMove(MoveResult result)
        {
            isAnimating = true;
            yield return levelView.AnimateToState(engine.State, result);
            isAnimating = false;
            ShowOutcome();
        }

        private void Restart()
        {
            if (level == null) return;
            messageDialog?.Hide();
            engine = new PuzzleBoxRuleEngine(level.CreateLayout());
            levelView.ShowState(engine.State);
            levelView.SetPlayerFacing(facingDirection);
            cameraController?.SetViewQuarter(engine.State.ViewQuarter, true);
            ShowOutcome();
        }

        private void ShowOutcome()
        {
            if (engine.State.IsComplete)
                messageDialog.Show(PuzzleBoxFeedbackMessage.Completed(engine.State.MoveCount, engine.State.PushCount), engine.CanUndo);
            else if (engine.State.Failure != State.FailureReason.None)
                messageDialog.Show(PuzzleBoxFeedbackMessage.Failed(engine.State.Failure), engine.CanUndo);
        }

        private void PlayCatSound() { audioPlayer?.PlayCatStep(); }
        private void PlayBoxSound(float dropCells) { audioPlayer?.PlayBoxLanding(dropCells); }
        private void OnMessageDismissed() { inputResumeFrame = Time.frameCount + 1; }

        private void OnDestroy()
        {
            if (levelView != null) { levelView.CatLanded -= PlayCatSound; levelView.BoxesLanded -= PlayBoxSound; }
            if (messageDialog != null) messageDialog.Dismissed -= OnMessageDismissed;
        }

        private void OnGUI()
        {
            if (engine == null || (messageDialog != null && messageDialog.IsOpen)) return;
            if (heading == null)
            {
                heading = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold };
                caption = new GUIStyle(GUI.skin.label) { fontSize = 13 };
                heading.normal.textColor = caption.normal.textColor = new Color(.22f, .32f, .22f);
                button = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };
            }
            var old = GUI.color;
            GUI.color = new Color(.98f, .97f, .90f, .94f);
            GUI.DrawTexture(new Rect(22, 22, 300, 78), Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(38, 31, 280, 33), level.DisplayName, heading);
            GUI.Label(new Rect(40, 68, 278, 24), $"{engine.State.MoveCount:00} MOVES / {engine.State.PushCount:00} PUSHES / V{engine.State.ViewQuarter}", caption);
            var oldEnabled = GUI.enabled;
            GUI.enabled = !IsAnimating && Time.frameCount > inputResumeFrame;
            if (GUI.Button(new Rect(Screen.width - 220, 28, 88, 40), "Undo · Z", button))
            {
                UndoAction();
            }
            if (GUI.Button(new Rect(Screen.width - 122, 28, 98, 40), "Reset · R", button)) Restart();
            GUI.enabled = oldEnabled;
            var status = "WASD / ARROWS · Face    SPACE · Move    Q / E · Rotate    WHEEL · Zoom";
            if (IsAnimating) status = "ON THE MOVE...";
            else if (engine.State.IsComplete) status = "DELIVERY COMPLETE!    Z to undo · R to play again";
            else if (engine.State.Failure != State.FailureReason.None)
                status = "DELIVERY INTERRUPTED    Z to undo · R to restart";
            GUI.Label(new Rect(30, Screen.height - 42, Screen.width - 60, 28), status, caption);
        }
    }
}
