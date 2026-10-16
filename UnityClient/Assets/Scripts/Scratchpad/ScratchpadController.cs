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
        // In cells; small so a short flick counts (the move dots are there for precise moves).
        private const float MinDragDistance = 0.25f;
        // After watching another solution, its final position stays up this long before the player's attempt returns.
        private const float WatchHoldSeconds = 1f;

        [SerializeField] private BoardView _boardView;
        [SerializeField] private Camera _camera;
        [Tooltip("0 picks a random seed each board.")]
        [SerializeField] private int _seed;

        private bool _dragging;
        private Vector3 _dragStart;
        private Coroutine _replay;
        private BoardOverlay _overlay;
        private int _lastTick;
        // Where the selected robot would stop in each direction (indexed by Direction), for the move dots.
        private readonly GridPos?[] _previewStops = new GridPos?[4];
        private bool _previewShown;

        /// <summary>What's on the board; views read it.</summary>
        public BoardSession Session { get; } = new BoardSession();
        public Camera Camera => _camera;
        public BoardView View => _boardView;

        /// <summary>The player's own move just solved the board (raised before the slide plays), with the move count.</summary>
        public event System.Action<int> Solved;
        /// <summary>The UI panel on the right; the camera keeps the board clear of it.</summary>
        public RectTransform SidePanel { get; set; }

        private void Start()
        {
            if (_camera == null)
                _camera = Camera.main;
            _overlay = GetComponent<BoardOverlay>();
            if (Session.Round == null)
                NewBoard();
        }

        /// <summary>Shows the server's board for an online round.</summary>
        public void LoadRound(RoundSetup round)
        {
            // The board arriving is the "go" after a countdown.
            bool afterCountdown = Session.IsCountingDown;
            Show(round, practice: false);
            if (afterCountdown)
                _boardView.PlayStart();
        }

        /// <summary>Back to offline practice (e.g. between matches): a fresh random board, N / H enabled.</summary>
        public void PlayOffline() => NewBoard();

        /// <summary>Puts the robots back at the round's start and clears the move list (same as pressing R).</summary>
        public void ResetToStart() => Apply(Session.Reset);

        /// <summary>Dims the board under a countdown until the next board arrives (<see cref="LoadRound"/>).</summary>
        public void ShowCountdown(float seconds, string caption)
        {
            CancelSearch();
            Session.StartCountdown(Time.time, seconds, caption);
            _dragging = false;
        }

        /// <summary>The round is over and there's nothing to replay: the board stays, locked.</summary>
        public void EndRound()
        {
            CancelSearch();
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
            CancelSearch();
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

        /// <summary>
        /// During a round: plays <paramref name="segment"/> (e.g. the current best) on this board, holds the final
        /// position a moment, then puts the player's own attempt back. Cut short by anything else taking the
        /// board (the round ending, a new board).
        /// </summary>
        public void WatchSolution(ReplaySegment segment)
        {
            if (!Session.AcceptsInput)
                return;
            var attempt = new List<Move>(Session.Moves);
            StopReplay();
            _replay = StartCoroutine(WatchRoutine(segment, attempt, Session.Round));
        }

        private IEnumerator WatchRoutine(ReplaySegment segment, List<Move> attempt, RoundSetup round)
        {
            yield return ReplayRoutine(new[] { segment }, Mathf.Min(0.5f, segment.StepSeconds), lockUntilNextBoard: true);
            yield return new WaitForSeconds(WatchHoldSeconds);
            if (Session.Round == round && Session.CurrentReplay == segment && !Session.IsCountingDown)
                Apply(() => Session.Restore(attempt));
        }

        /// <summary>A finished board from the match history, as a practice board to look at and try.</summary>
        public void ShowHistoryBoard(RoundSetup round) => Show(round, practice: true);

        /// <summary>Plays a solution on the board shown (e.g. from the match history); the player's next move starts afresh.</summary>
        public void ShowSolution(ReplaySegment segment) =>
            PlayReplays(new[] { segment }, Mathf.Min(0.5f, segment.StepSeconds), lockUntilNextBoard: false);

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

        // ---- Practice boards: the same search as the server's (BoardSearch), a few solves per frame ----

        // Browsers have no threads, so the search runs on the main thread in slices this long; the board is
        // dimmed meanwhile, so a slow solve's hitch doesn't show.
        private const float SearchSliceSeconds = 0.025f;
        // Like the server: after this long, the longest board found so far.
        private const float PracticeSearchSeconds = 20f;
        // Shorter than the server's 5 s, as one solve blocks a frame; boards it gives up on are skipped.
        private static readonly System.TimeSpan PracticeSolverLimit = System.TimeSpan.FromSeconds(2);
        private BoardSearch _search;
        private int _searchSeed;
        private float _searchStarted;
        // The shown practice board's shortest solution length when a search found it, else 0 (unknown).
        private int _shownShortest;

        /// <summary>"Finding a board..." while a practice board is being searched for, else null.</summary>
        public string SearchStatus => _search == null ? null
            : _search.Longest == null ? $"Finding a {_search.MinMoves}-move board..."
            : $"Finding a {_search.MinMoves}-move board...\nlongest so far: {_search.LongestMoves.Length}";

        /// <summary>
        /// Saves the practice boards' minimum. The board shown stays if it still qualifies (its shortest solution
        /// is at least that long); otherwise a new one is dealt.
        /// </summary>
        public void SetPracticeMinMoves(int minMoves)
        {
            PlayerSettings.PracticeMinMoves = minMoves;
            if (_search != null || (Session.IsPractice && _shownShortest < minMoves))
                NewBoard();
        }

        private void NewBoard()
        {
            _searchSeed = _seed != 0 ? _seed : Random.Range(1, int.MaxValue);
            _search = new BoardSearch(PlayerSettings.PracticeMinMoves, new System.Random(_searchSeed), PracticeSolverLimit);
            _searchStarted = Time.realtimeSinceStartup;
            _dragging = false;
            StepSearch(); // usually done at once: no dimmed frame
        }

        private void StepSearch()
        {
            float sliceStart = Time.realtimeSinceStartup;
            do
            {
                bool outOfTime = Time.realtimeSinceStartup - _searchStarted >= PracticeSearchSeconds;
                if (_search.Step(noTimeLimit: outOfTime && _search.Longest == null) || (outOfTime && _search.Longest != null))
                {
                    RoundSetup found = _search.Longest;
                    int shortest = _search.LongestMoves.Length;
                    _search = null;
                    // Go through the wire format so the scratchpad sees exactly what a networked client would.
                    Show(BoardCodec.Decode(BoardCodec.Encode(found)), practice: true, _searchSeed);
                    _shownShortest = shortest;
                    return;
                }
            } while (Time.realtimeSinceStartup - sliceStart < SearchSliceSeconds);
        }

        private void CancelSearch() => _search = null;

        private void Show(RoundSetup round, bool practice, int seed = 0)
        {
            CancelSearch();
            _shownShortest = 0;
            StopReplay();
            Session.Load(round, practice, seed);
            _boardView.Build(round, Session.Robots);
            _boardView.ShowSelection(Session.Selected);
            FrameCamera();
        }

        // Every frame, so resizing the window (or the browser) keeps the board in view.
        private void LateUpdate()
        {
            if (Session.Round != null)
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
            {
                _boardView.ShowRobots(Session.Robots, instant: false);
                if (Session.IsSolved)
                    Solved?.Invoke(Session.Moves.Count);
            }
        }

        /// <summary>
        /// Puts the solved board back to the start after <paramref name="seconds"/> (time to see the robot land),
        /// unless it changed meanwhile (new board, replays, the player undid).
        /// </summary>
        public void ResetAfter(float seconds) => StartCoroutine(ResetSolvedBoard(Session.Round, seconds));

        private IEnumerator ResetSolvedBoard(RoundSetup round, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (Session.Round == round && Session.IsSolved && !Session.IsReplaying)
                ResetToStart();
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
            Keyboard keys = Keyboard.current;
            if (keys != null && keys.mKey.wasPressedThisFrame && !IsTypingInUI())
                PlayerSettings.Muted = !PlayerSettings.Muted;
            TickCountdown();
            if (_search != null)
            {
                // N or Esc stops looking (the current board stays); robots stay put meanwhile.
                if (keys != null && !IsTypingInUI() && (keys.nKey.wasPressedThisFrame || keys.escapeKey.wasPressedThisFrame))
                    CancelSearch();
                else
                    StepSearch();
                _dragging = false;
                UpdateMovePreview();
                return;
            }
            if (!Session.AcceptsInput)
            {
                _dragging = false;
                HandleHintShortcuts();
                UpdateMovePreview();
                return;
            }
            HandleKeyboard();
            HandlePointer();
            UpdateMovePreview();
        }

        // A beep on each countdown second (3, 2, 1).
        private void TickCountdown()
        {
            int second = Session.IsCountingDown ? Mathf.CeilToInt(Session.CountdownLeft(Time.time)) : 0;
            if (second != _lastTick && second > 0)
                _boardView.PlayTick();
            _lastTick = second;
        }

        // The move dots (PlayerSettings.MovePreview): where the selected robot would stop, while the player can move it.
        private void UpdateMovePreview()
        {
            bool show = PlayerSettings.MovePreview && Session.AcceptsInput && !Session.IsHoldingReplay && !Session.IsSolved
                && _search == null;
            if (show)
            {
                RobotColor robot = Session.Selected;
                GridPos from = Session.Robots[robot];
                for (int i = 0; i < _previewStops.Length; i++)
                {
                    GridPos stop = GameRules.ComputeStop(Session.Round.Grid, Session.Robots, robot, (Direction)i);
                    _previewStops[i] = stop == from ? (GridPos?)null : stop;
                }
                _boardView.ShowMovePreview(robot, _previewStops);
            }
            else if (_previewShown)
            {
                _boardView.ShowMovePreview(Session.Selected, null);
            }
            _previewShown = show;
        }

        // A click on one of the selected robot's move dots makes that move.
        private bool TryMoveToDot(GridPos cell)
        {
            if (!_previewShown)
                return false;
            for (int i = 0; i < _previewStops.Length; i++)
            {
                if (_previewStops[i] == cell)
                {
                    TryMove((Direction)i);
                    return true;
                }
            }
            return false;
        }

        // The practice hint's replay can be cut short (online replays can't): R back to the start, N a new board.
        private void HandleHintShortcuts()
        {
            Keyboard keyboard = Keyboard.current;
            if (!Session.IsPractice || !Session.IsReplaying || keyboard == null || IsTypingInUI())
                return;
            if (keyboard.rKey.wasPressedThisFrame)
            {
                StopReplay();
                ResetToStart();
            }
            else if (keyboard.nKey.wasPressedThisFrame)
            {
                NewBoard();
            }
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
        // Pressing one of the move dots moves there. Pointer covers both mouse and touch.
        private void HandlePointer()
        {
            Pointer pointer = Pointer.current;
            if (pointer == null || _camera == null)
                return;

            Vector3 world = _camera.ScreenToWorldPoint(pointer.position.ReadValue());

            if (pointer.press.wasPressedThisFrame)
            {
                GridPos cell = BoardView.WorldToCell(world);
                if (TryMoveToDot(cell))
                    return;
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

        // Fits the board (plus room for the banner above it) into the screen area between the overlay card
        // on the left and the side panel on the right.
        private void FrameCamera()
        {
            if (_camera == null)
                return;

            float width = Session.Round.Grid.Width;
            float height = Session.Round.Grid.Height;
            // Extra room above the board for the banner (see BoardOverlay).
            const float margin = 0.6f;
            const float bottom = 0.8f;
            const float top = 2f;

            float screenWidth = _camera.pixelWidth;
            float left = _overlay != null ? _overlay.LeftInsetPixels : 0f;
            float right = SidePanel != null && SidePanel.gameObject.activeInHierarchy ? screenWidth - PanelLeftPixels() : 0f;
            // In a very narrow window, let the card overlap the board rather than shrink the board to nothing.
            if (screenWidth - left - right < screenWidth * 0.45f)
                left = 0f;
            float free = Mathf.Max(1f, screenWidth - left - right);
            float pixelsPerUnit = Mathf.Min(free / (width + 2f * margin), _camera.pixelHeight / (height + top + bottom));

            _camera.orthographic = true;
            _camera.orthographicSize = _camera.pixelHeight / pixelsPerUnit / 2f;
            // Centre the board in the free area instead of the screen.
            float offset = (left - right) / 2f / pixelsPerUnit;
            _camera.transform.position = new Vector3(width / 2f - offset, height / 2f + (top - bottom) / 2f, -10f);
        }

        // The panel's left edge in screen pixels (the canvas is screen-space overlay).
        private float PanelLeftPixels()
        {
            var corners = new Vector3[4];
            SidePanel.GetWorldCorners(corners);
            return corners[0].x - 16f;
        }
    }
}
