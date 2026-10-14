using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ricochet.Client.Board;
using Ricochet.Client.Game;
using SharedLogic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Ricochet.Client.Scratchpad
{
    /// <summary>
    /// Drives the <see cref="BoardSession"/> the player sees: input, replay timing, offline boards and
    /// hints, and the camera. The state lives in <see cref="Session"/>; <see cref="BoardView"/> draws the
    /// board and <see cref="BoardOverlay"/> what's around it.
    /// </summary>
    /// <remarks>
    /// Controls: click a robot (or 1-4 / Tab) to select it, arrows / WASD or drag to move,
    /// Z / Backspace to undo, R to reset, N for a new board, H for the shortest solution (offline).
    /// </remarks>
    public class ScratchpadController : MonoBehaviour
    {
        private const float MinDragDistance = 0.4f;

        [SerializeField] private BoardView _boardView;
        [SerializeField] private Camera _camera;
        [Tooltip("0 picks a random seed each board.")]
        [SerializeField] private int _seed;

        private bool _dragging;
        private Vector3 _dragStart;
        private Coroutine _replay;

        /// <summary>What's on the board; views read it.</summary>
        public BoardSession Session { get; } = new BoardSession();
        public Camera Camera => _camera;
        public BoardView View => _boardView;

        private void Start()
        {
            if (_camera == null)
                _camera = Camera.main;
            if (Session.Round == null)
                NewBoard();
        }

        /// <summary>Shows the server's board for an online round.</summary>
        public void LoadRound(RoundSetup round) => Show(round, practice: false);

        /// <summary>Back to offline practice (e.g. between matches): a fresh random board, N / H enabled.</summary>
        public void PlayOffline() => NewBoard();

        /// <summary>Puts the robots back at the round's start and clears the move list (same as pressing R).</summary>
        public void ResetToStart() => Apply(Session.Reset);

        /// <summary>Dims the board under a countdown until the next board arrives (<see cref="LoadRound"/>).</summary>
        public void ShowCountdown(float seconds, string caption)
        {
            Session.StartCountdown(Time.time, seconds, caption);
            _dragging = false;
        }

        /// <summary>The round is over and there's nothing to replay: the board stays, locked.</summary>
        public void EndRound()
        {
            Session.EndRound();
            _dragging = false;
        }

        /// <summary>
        /// Puts the robots back at the round's start and plays <paramref name="moves"/> one every
        /// <paramref name="stepSeconds"/>. With <paramref name="lockUntilNextBoard"/> the board stays locked
        /// on the final position until the next board; otherwise the player can carry on (R to retry).
        /// </summary>
        public void PlayReplay(IReadOnlyList<Move> moves, float stepSeconds, bool lockUntilNextBoard = true)
        {
            // A short beat on the starting position so the first move is easy to follow.
            PlayReplays(new[] { new ReplaySegment("Shortest solution", moves, stepSeconds, isShortest: true) },
                Mathf.Min(0.5f, stepSeconds), lockUntilNextBoard);
        }

        /// <summary>
        /// Plays several solutions one after another (e.g. the winner's, then the shortest), each from the
        /// round's start after <paramref name="pauseSeconds"/> on the starting position.
        /// </summary>
        public void PlayReplays(IReadOnlyList<ReplaySegment> segments, float pauseSeconds, bool lockUntilNextBoard = true)
        {
            StopReplay();
            _replay = StartCoroutine(ReplayRoutine(segments, pauseSeconds, lockUntilNextBoard));
        }

        private IEnumerator ReplayRoutine(IReadOnlyList<ReplaySegment> segments, float pauseSeconds, bool lockUntilNextBoard)
        {
            _dragging = false;
            foreach (ReplaySegment segment in segments)
            {
                Apply(() => Session.BeginReplay(segment));
                yield return new WaitForSeconds(pauseSeconds);
                while (Session.ReplayStep < segment.Moves.Count)
                {
                    Session.ReplayNextMove();
                    _boardView.ShowSelection(Session.Selected);
                    _boardView.ShowRobots(Session.Robots, instant: false);
                    yield return new WaitForSeconds(segment.StepSeconds);
                }
            }
            _replay = null;
            Session.FinishReplays(lockUntilNextBoard);
        }

        private void StopReplay()
        {
            if (_replay != null)
                StopCoroutine(_replay);
            _replay = null;
            Session.StopReplay();
        }

        // ---- Offline hint (H): solve in the background, then replay the shortest solution ----

        // The quicker pace of the shortest-solution replay in a lobby (a player's replay is 1 s/move).
        private const float HintStepSeconds = 0.6f;
        // Generated boards solve in well under a second; this only guards against a pathological one.
        private static readonly System.TimeSpan HintTimeLimit = System.TimeSpan.FromSeconds(30);
        private Task<SolverResult> _hintTask;
        private RoundSetup _hintRound;

        private void RequestHint()
        {
            if (_hintTask != null)
                return;
            RoundSetup round = Session.Round;
            _hintRound = round;
            Session.HintText = "Finding the shortest solution...";
#if UNITY_WEBGL && !UNITY_EDITOR
            // Browsers have no threads: solve right here (usually ~1 ms; a rare hard board stalls a moment).
            _hintTask = Task.FromResult(Solver.Solve(round, timeLimit: HintTimeLimit));
#else
            _hintTask = Task.Run(() => Solver.Solve(round, timeLimit: HintTimeLimit));
#endif
        }

        // Called every frame; applies the hint once the background solve finishes.
        private void CheckHint()
        {
            if (_hintTask == null || !_hintTask.IsCompleted)
                return;

            Task<SolverResult> task = _hintTask;
            _hintTask = null;
            if (_hintRound != Session.Round)
                return; // the board changed while solving

            if (task.IsFaulted)
            {
                Session.HintText = "Couldn't solve this board.";
                Debug.LogException(task.Exception);
                return;
            }
            SolverResult result = task.Result;
            if (!result.Solved)
            {
                Session.HintText = result.HitLimit
                    ? $"Gave up after {result.Elapsed.TotalSeconds:0} s. This board is extremely hard."
                    : "This board has no solution.";
                return;
            }
            PlayReplay(result.Moves, HintStepSeconds, lockUntilNextBoard: false);
            Session.HintText = $"Shortest solution: {result.Moves.Length} moves (found in {result.Elapsed.TotalSeconds:0.0} s). " +
                "Make a move to try it yourself.";
        }

        private void NewBoard()
        {
            int seed = _seed != 0 ? _seed : Random.Range(1, int.MaxValue);
            RoundSetup generated = BoardGenerator.GenerateRound(new System.Random(seed));
            // Go through the wire format so the scratchpad sees exactly what a networked client would.
            Show(BoardCodec.Decode(BoardCodec.Encode(generated)), practice: true, seed);
        }

        private void Show(RoundSetup round, bool practice, int seed = 0)
        {
            StopReplay();
            Session.Load(round, practice, seed);
            _boardView.Build(round, Session.Robots);
            _boardView.ShowSelection(Session.Selected);
            FrameCamera();
        }

        // Runs a change to the session, then shows where the robots ended up.
        private void Apply(System.Action change)
        {
            change();
            _boardView.ShowSelection(Session.Selected);
            _boardView.ShowRobots(Session.Robots, instant: true);
        }

        private void Select(RobotColor robot)
        {
            Session.Select(robot);
            _boardView.ShowSelection(robot);
        }

        private void TryMove(Direction direction)
        {
            ClearShownSolution();
            if (Session.TryMove(direction))
                _boardView.ShowRobots(Session.Robots, instant: false);
        }

        private void Undo()
        {
            if (Session.IsHoldingReplay)
            {
                ClearShownSolution();
                return;
            }
            if (Session.Undo())
                _boardView.ShowRobots(Session.Robots, instant: false);
        }

        // The practice hint's solution is still on the board: snap back to the start (no diagonal slides)
        // before the player's own move.
        private void ClearShownSolution()
        {
            if (Session.IsHoldingReplay)
                Apply(Session.Reset);
        }

        private void Update()
        {
            CheckHint();
            _boardView.ShowMode(Session.Mode);
            if (!Session.AcceptsInput)
            {
                _dragging = false;
                return;
            }
            HandleKeyboard();
            HandlePointer();
        }

        private void HandleKeyboard()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || IsTypingInUI())
                return;

            if (keyboard.digit1Key.wasPressedThisFrame) Select(RobotColor.Red);
            if (keyboard.digit2Key.wasPressedThisFrame) Select(RobotColor.Blue);
            if (keyboard.digit3Key.wasPressedThisFrame) Select(RobotColor.Green);
            if (keyboard.digit4Key.wasPressedThisFrame) Select(RobotColor.Yellow);
            if (keyboard.tabKey.wasPressedThisFrame)
            {
                Session.SelectNext();
                _boardView.ShowSelection(Session.Selected);
            }

            if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame) TryMove(Direction.Up);
            if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame) TryMove(Direction.Right);
            if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame) TryMove(Direction.Down);
            if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame) TryMove(Direction.Left);

            if (keyboard.zKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame) Undo();
            if (keyboard.rKey.wasPressedThisFrame) ResetToStart();
            // Offline practice only; online it would give the answer away.
            if (keyboard.nKey.wasPressedThisFrame && Session.IsPractice) NewBoard();
            if (keyboard.hKey.wasPressedThisFrame && Session.IsPractice) RequestHint();
        }

        // Typing a name into an input field shouldn't also move robots.
        private static bool IsTypingInUI()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null || eventSystem.currentSelectedGameObject == null)
                return false;
            InputField field = eventSystem.currentSelectedGameObject.GetComponent<InputField>();
            return field != null && field.isFocused;
        }

        // Press on a robot to select it; release after dragging to move it in the drag's main direction.
        // Pointer covers both mouse and touch.
        private void HandlePointer()
        {
            Pointer pointer = Pointer.current;
            if (pointer == null || _camera == null)
                return;

            Vector3 world = _camera.ScreenToWorldPoint(pointer.position.ReadValue());

            if (pointer.press.wasPressedThisFrame)
            {
                GridPos cell = BoardView.WorldToCell(world);
                foreach (RobotColor robot in new[] { RobotColor.Red, RobotColor.Blue, RobotColor.Green, RobotColor.Yellow })
                {
                    if (Session.Robots[robot] == cell)
                    {
                        Select(robot);
                        _dragging = true;
                        _dragStart = world;
                    }
                }
            }

            if (pointer.press.wasReleasedThisFrame && _dragging)
            {
                _dragging = false;
                Vector3 delta = world - _dragStart;
                if (delta.magnitude < MinDragDistance)
                    return;
                if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
                    TryMove(delta.x > 0 ? Direction.Right : Direction.Left);
                else
                    TryMove(delta.y > 0 ? Direction.Up : Direction.Down);
            }
        }

        private void FrameCamera()
        {
            if (_camera == null)
                return;

            float width = Session.Round.Grid.Width;
            float height = Session.Round.Grid.Height;
            // Extra room above the board for the banner (see BoardOverlay).
            const float margin = 1f;
            const float bottom = 0.8f;
            const float top = 2f;
            _camera.orthographic = true;
            _camera.transform.position = new Vector3(width / 2f, height / 2f + (top - bottom) / 2f, -10f);
            _camera.orthographicSize = Mathf.Max((height + top + bottom) / 2f, width / 2f / _camera.aspect + margin);
        }
    }
}
