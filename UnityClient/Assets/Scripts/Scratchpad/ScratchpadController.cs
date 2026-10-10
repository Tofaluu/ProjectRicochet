using System.Collections.Generic;
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

        public IReadOnlyList<Move> Moves => _moves;
        public bool IsSolved => _solved;

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
            _currentSeed = 0;
            ShowRound(round);
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
            _boardView.Build(_round, _round.Robots);
            FrameCamera();
            ResetBoard();
        }

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
            const float margin = 1f;
            _camera.orthographic = true;
            _camera.transform.position = new Vector3(width / 2f, height / 2f, -10f);
            _camera.orthographicSize = Mathf.Max(height / 2f, width / 2f / _camera.aspect) + margin;
        }

        // Placeholder HUD until there's a real UI.
        private void OnGUI()
        {
            if (_round == null)
                return;

            Target target = _round.Target;
            string goal = target.Robot.HasValue ? target.Robot.Value + " robot" : "any robot";
            string status = _solved ? $"SOLVED in {_moves.Count} moves!" : $"Moves: {_moves.Count}";
            string text =
                $"Get {goal} to the big diamond\n" +
                $"{status}\n" +
                $"Selected: {_selected}\n" +
                (_currentSeed != 0 ? $"Seed: {_currentSeed}\n\n" : "Online board\n\n") +
                "Click / 1-4 / Tab: select\nArrows / WASD / drag: move\nZ: undo   R: reset" +
                (AllowNewBoard ? "   N: new board" : "");

            var style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 16 };
            GUI.Box(new Rect(10, 10, 300, 200), text, style);
        }
    }
}
