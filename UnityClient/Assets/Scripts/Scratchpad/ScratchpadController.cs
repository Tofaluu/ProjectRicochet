using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ricochet.Client.Board;
using SharedLogic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Ricochet.Client.Scratchpad
{
    /// <summary>
    /// Local single-player board for trying out moves. Every move goes through
    /// <see cref="GameRules"/>, and the recorded move list is exactly what a player
    /// would later submit to the server.
    /// </summary>
    /// <remarks>
    /// Controls: click a robot (or 1-4 / Tab) to select it, arrows / WASD or drag to move,
    /// Z / Backspace to undo, R to reset, N for a new board.
    /// </remarks>
    public class ScratchpadController : MonoBehaviour
    {
        private const float MinDragDistance = 0.4f;

        [SerializeField] private BoardView _boardView;
        [SerializeField] private Camera _camera;
        [Tooltip("0 picks a random seed each board.")]
        [SerializeField] private int _seed;

        private RoundSetup _round;
        private RobotPositions _robots;
        private readonly List<Move> _moves = new List<Move>();
        private readonly List<RobotPositions> _history = new List<RobotPositions>();
        private RobotColor _selected = RobotColor.Red;
        private bool _solved;
        private int _currentSeed;

        private bool _dragging;
        private Vector3 _dragStart;
        private Coroutine _replay;

        public IReadOnlyList<Move> Moves => _moves;
        public bool IsSolved => _solved;

        /// <summary>True from <see cref="PlayReplay"/> until the next board loads; player input is ignored.</summary>
        public bool IsReplaying { get; private set; }
        /// <summary>How many replay moves have been shown so far.</summary>
        public int ReplayStep { get; private set; }

        /// <summary>False while playing online, where the server decides the board.</summary>
        public bool AllowNewBoard { get; set; } = true;

        private void Start()
        {
            if (_camera == null)
                _camera = Camera.main;
            if (_round == null)
                NewBoard();
        }

        /// <summary>Shows a board from elsewhere (e.g. the server) and clears the move list.</summary>
        public void LoadRound(RoundSetup round)
        {
            StopReplay();
            HideCountdown();
            _currentSeed = 0;
            ShowRound(round);
        }

        /// <summary>Back to offline practice (e.g. after leaving a lobby): a fresh random board, N enabled.</summary>
        public void PlayOffline()
        {
            StopReplay();
            HideCountdown();
            AllowNewBoard = true;
            NewBoard();
        }

        /// <summary>
        /// Puts the robots back at the round's start and plays <paramref name="moves"/> one every
        /// <paramref name="stepSeconds"/>, e.g. the winning solution at the end of an online round.
        /// With <paramref name="lockUntilNextBoard"/> the board stays locked on the final position until
        /// the next <see cref="LoadRound"/>; otherwise the player can carry on (R to retry) afterwards.
        /// </summary>
        public void PlayReplay(IReadOnlyList<Move> moves, float stepSeconds, bool lockUntilNextBoard = true)
        {
            // A short beat on the starting position so the first move is easy to follow.
            PlayReplays(new[] { new ReplaySegment("Shortest solution", moves, stepSeconds, isShortest: true) }, Mathf.Min(0.5f, stepSeconds), lockUntilNextBoard);
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

        /// <summary>The replay currently playing (null when none).</summary>
        public ReplaySegment CurrentReplay { get; private set; }

        private IEnumerator ReplayRoutine(IReadOnlyList<ReplaySegment> segments, float pauseSeconds, bool lockUntilNextBoard)
        {
            IsReplaying = true;
            _dragging = false;
            foreach (ReplaySegment segment in segments)
            {
                CurrentReplay = segment;
                ResetBoard();
                ReplayStep = 0;
                yield return new WaitForSeconds(pauseSeconds);
                foreach (Move move in segment.Moves)
                {
                    Select(move.Robot);
                    if (GameRules.TryMove(_round.Grid, _robots, move, out RobotPositions next))
                    {
                        _moves.Add(move);
                        _robots = next;
                        _boardView.ShowRobots(_robots, instant: false);
                    }
                    ReplayStep++;
                    yield return new WaitForSeconds(segment.StepSeconds);
                }
            }
            _replay = null;
            if (!lockUntilNextBoard)
            {
                IsReplaying = false;
                CurrentReplay = null;
            }
        }

        // ---- Offline hint (H): solve in the background, then replay the shortest solution ----

        private const float HintStepSeconds = 0.6f;
        // Generated boards solve in well under a second; this only guards against a pathological one.
        private static readonly System.TimeSpan HintTimeLimit = System.TimeSpan.FromSeconds(30);
        private Task<SolverResult> _hintTask;
        private RoundSetup _hintRound;
        private string _hintText;

        private void RequestHint()
        {
            if (_hintTask != null)
                return;
            _hintRound = _round;
            _hintText = "Finding the shortest solution...";
            RoundSetup round = _round;
            _hintTask = Task.Run(() => Solver.Solve(round, timeLimit: HintTimeLimit));
        }

        // Called every frame; applies the hint once the background solve finishes.
        private void CheckHint()
        {
            if (_hintTask == null || !_hintTask.IsCompleted)
                return;

            Task<SolverResult> task = _hintTask;
            _hintTask = null;
            if (_hintRound != _round)
                return; // the board changed while solving

            if (task.IsFaulted)
            {
                _hintText = "Couldn't solve this board.";
                Debug.LogException(task.Exception);
                return;
            }
            SolverResult result = task.Result;
            if (!result.Solved)
            {
                _hintText = result.HitLimit
                    ? $"Gave up after {result.Elapsed.TotalSeconds:0} s. This board is extremely hard."
                    : "This board has no solution.";
                return;
            }
            _hintText = $"Shortest solution: {result.Moves.Length} moves (found in {result.Elapsed.TotalSeconds:0.0} s). " +
                "R to try it yourself.";
            PlayReplay(result.Moves, HintStepSeconds, lockUntilNextBoard: false);
        }

        private void StopReplay()
        {
            if (_replay != null)
                StopCoroutine(_replay);
            _replay = null;
            IsReplaying = false;
            CurrentReplay = null;
            ReplayStep = 0;
        }

        private void NewBoard()
        {
            _currentSeed = _seed != 0 ? _seed : Random.Range(1, int.MaxValue);
            RoundSetup generated = BoardGenerator.GenerateRound(new System.Random(_currentSeed));
            // Go through the wire format so the scratchpad sees exactly what a networked client would.
            ShowRound(BoardCodec.Decode(BoardCodec.Encode(generated)));
        }

        private void ShowRound(RoundSetup round)
        {
            _round = round;
            _hintText = null;
            _boardView.Build(_round, _round.Robots);
            FrameCamera();
            ResetBoard();
        }

        /// <summary>Puts the robots back at the round's start and clears the move list (same as pressing R).</summary>
        public void ResetToStart() => ResetBoard();

        private void ResetBoard()
        {
            _robots = _round.Robots;
            _moves.Clear();
            _history.Clear();
            _solved = false;
            _boardView.ShowRobots(_robots, instant: true);
            Select(_round.Target.Robot ?? RobotColor.Red);
        }

        private void Select(RobotColor robot)
        {
            _selected = robot;
            _boardView.ShowSelection(robot);
        }

        private void TryMove(Direction direction)
        {
            if (_solved)
                return;

            var move = new Move(_selected, direction);
            if (!GameRules.TryMove(_round.Grid, _robots, move, out RobotPositions next))
                return;

            _history.Add(_robots);
            _moves.Add(move);
            _robots = next;
            _boardView.ShowRobots(_robots, instant: false);
            _solved = GameRules.ValidateSolution(_round.Grid, _round.Robots, _round.Target, _moves) == SolutionResult.Valid;
        }

        private void Undo()
        {
            if (_moves.Count == 0)
                return;

            _robots = _history[_history.Count - 1];
            _history.RemoveAt(_history.Count - 1);
            _moves.RemoveAt(_moves.Count - 1);
            _solved = false;
            _boardView.ShowRobots(_robots, instant: false);
        }

        private void Update()
        {
            CheckHint();
            UpdateFrame();
            if (IsReplaying || IsCountingDown)
                return;
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
            if (keyboard.tabKey.wasPressedThisFrame) Select((RobotColor)(((int)_selected + 1) % Robot.Count));

            if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame) TryMove(Direction.Up);
            if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame) TryMove(Direction.Right);
            if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame) TryMove(Direction.Down);
            if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame) TryMove(Direction.Left);

            if (keyboard.zKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame) Undo();
            if (keyboard.rKey.wasPressedThisFrame) ResetBoard();
            if (keyboard.nKey.wasPressedThisFrame && AllowNewBoard) NewBoard();
            // Offline practice only; online it would give the answer away.
            if (keyboard.hKey.wasPressedThisFrame && AllowNewBoard) RequestHint();
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
                    if (_robots[robot] == cell)
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

            float width = _round.Grid.Width;
            float height = _round.Grid.Height;
            // Extra room above the board for the banner (see OnGUI).
            const float margin = 1f;
            const float bottom = 0.8f;
            const float top = 2f;
            _camera.orthographic = true;
            _camera.transform.position = new Vector3(width / 2f, height / 2f + (top - bottom) / 2f, -10f);
            _camera.orthographicSize = Mathf.Max((height + top + bottom) / 2f, width / 2f / _camera.aspect + margin);
        }

        // ---- What the board is showing: the player's own attempt, a player's replay, or the shortest ----

        private static readonly Color OwnColor = new Color(0.10f, 0.65f, 0.75f);
        private static readonly Color PlayerReplayColor = new Color(1.00f, 0.72f, 0.15f);
        private static readonly Color ShortestColor = new Color(0.55f, 0.57f, 0.62f);

        private float _countdownEnd = -1f;
        private string _countdownCaption;

        private bool IsCountingDown => _countdownEnd >= 0f;

        /// <summary>Dims the board under a big countdown until the next board arrives (<see cref="LoadRound"/>).</summary>
        public void ShowCountdown(float seconds, string caption)
        {
            _countdownEnd = Time.time + seconds;
            _countdownCaption = caption;
            _dragging = false;
        }

        public void HideCountdown() => _countdownEnd = -1f;

        private Color FrameColor()
        {
            if (IsReplaying && CurrentReplay != null)
                return CurrentReplay.IsShortest ? ShortestColor : PlayerReplayColor;
            return OwnColor;
        }

        private void UpdateFrame()
        {
            if (_round != null)
                _boardView.SetFrameColor(FrameColor());
        }

        private string BannerText()
        {
            ReplaySegment replay = CurrentReplay;
            if (IsReplaying && replay != null)
            {
                string who = replay.IsShortest ? "SHORTEST POSSIBLE" : "REPLAY: " + replay.Label;
                if (_replay == null)
                    return $"{who}  ·  {replay.Moves.Count} moves  ·  finished";
                return ReplayStep == 0
                    ? $"{who}  ·  {replay.Moves.Count} moves"
                    : $"{who}  ·  move {ReplayStep} of {replay.Moves.Count}";
            }
            string board = AllowNewBoard ? "PRACTICE BOARD" : "YOUR BOARD";
            return _solved ? $"{board}  ·  solved in {_moves.Count} moves!" : $"{board}  ·  {_moves.Count} moves";
        }

        // The board's corners in IMGUI coordinates (pixels, y down), including the frame.
        private Rect BoardScreenRect()
        {
            float pad = BoardView.FrameThickness + 0.06f;
            Vector3 min = _camera.WorldToScreenPoint(new Vector3(-pad, -pad));
            Vector3 max = _camera.WorldToScreenPoint(new Vector3(_round.Grid.Width + pad, _round.Grid.Height + pad));
            return Rect.MinMaxRect(min.x, Screen.height - max.y, max.x, Screen.height - min.y);
        }

        private void DrawBannerAndCountdown()
        {
            if (_camera == null)
                return;
            Rect board = BoardScreenRect();
            Color previous = GUI.color;

            // Banner in the frame's colour, just above the board.
            const float bannerHeight = 34f;
            var banner = new Rect(board.x, board.y - bannerHeight - 4f, board.width, bannerHeight);
            GUI.color = FrameColor();
            GUI.DrawTexture(banner, Texture2D.whiteTexture);
            GUI.color = previous;
            var bannerStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.black },
            };
            GUI.Label(banner, BannerText(), bannerStyle);

            if (!IsCountingDown)
                return;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(board, Texture2D.whiteTexture);
            GUI.color = previous;
            float remaining = _countdownEnd - Time.time;
            var numberStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(board.height * 0.3f),
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
            };
            var captionStyle = new GUIStyle(numberStyle) { fontSize = Mathf.RoundToInt(board.height * 0.06f) };
            float captionHeight = board.height * 0.12f;
            GUI.Label(new Rect(board.x, board.center.y - board.height * 0.28f, board.width, captionHeight),
                _countdownCaption, captionStyle);
            GUI.Label(board, remaining > 0f ? Mathf.CeilToInt(remaining).ToString() : "Go!", numberStyle);
        }

        // Placeholder HUD until there's a real UI.
        private void OnGUI()
        {
            if (_round == null)
                return;
            DrawBannerAndCountdown();

            Target target = _round.Target;
            string goal = target.Robot.HasValue ? target.Robot.Value + " robot" : "any robot";
            string status = IsReplaying ? $"Replaying solution ({_moves.Count} moves)"
                : _solved ? $"SOLVED in {_moves.Count} moves!" : $"Moves: {_moves.Count}";
            string text =
                $"Get {goal} to the big diamond\n" +
                $"{status}\n" +
                $"Selected: {_selected}\n" +
                (_currentSeed != 0 ? $"Seed: {_currentSeed}\n" : "Online board\n") +
                (AllowNewBoard && _hintText != null ? _hintText + "\n" : "") + "\n" +
                "Click / 1-4 / Tab: select\nArrows / WASD / drag: move\nZ: undo   R: reset" +
                (AllowNewBoard ? "\nN: new board   H: show shortest solution" : "");

            var style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 16, wordWrap = true };
            GUI.Box(new Rect(10, 10, 320, 240), text, style);
        }
    }
}
