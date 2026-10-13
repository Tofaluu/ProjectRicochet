using System.Collections.Generic;
using Ricochet.Client.Game;
using Ricochet.Client.Theme;
using SharedLogic;
using UnityEngine;

namespace Ricochet.Client.Board
{
    /// <summary>
    /// Draws a <see cref="RoundSetup"/> with placeholder shapes. Cell (x, y) covers world
    /// space [x, x+1] x [y, y+1], so the board's bottom-left corner is the world origin.
    /// Only displays state; the rules live in <see cref="GameRules"/>.
    /// </summary>
    public class BoardView : MonoBehaviour
    {
        private const float WallThickness = 0.12f;
        /// <summary>Width of the coloured frame around the board, in cells.</summary>
        public const float FrameThickness = 0.3f;

        private const int TileOrder = 0;
        private const int TargetOrder = 1;
        private const int WallOrder = 2;
        private const int SelectionOrder = 3;
        private const int RobotOrder = 4;

        [Tooltip("Cells per second. 50 crosses the whole board in about 0.3 s.")]
        [SerializeField] private float _slideSpeed = 50f;
        [SerializeField] private GameTheme _theme;

        private readonly Dictionary<RobotColor, Transform> _robots = new Dictionary<RobotColor, Transform>();
        private readonly Dictionary<RobotColor, Vector3> _robotDestinations = new Dictionary<RobotColor, Vector3>();
        private readonly List<SpriteRenderer> _frame = new List<SpriteRenderer>();
        private Color _frameColor = Color.clear;
        private Transform _content;
        private Transform _selectionRing;
        private RobotColor _selected;

        public static Vector3 CellCenter(GridPos pos) => new Vector3(pos.X + 0.5f, pos.Y + 0.5f, 0f);

        public static GridPos WorldToCell(Vector3 world) => new GridPos(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y));

        private GameTheme Colors => GameTheme.OrDefault(_theme);

        public void Build(RoundSetup round, RobotPositions robots)
        {
            if (_content != null)
                Destroy(_content.gameObject);
            _content = new GameObject("Board").transform;
            _content.SetParent(transform, false);
            _robots.Clear();
            _robotDestinations.Clear();

            GridState grid = round.Grid;
            Color activeColor = Colors.TargetColor(round.Target);

            for (int x = 0; x < grid.Width; x++)
            {
                for (int y = 0; y < grid.Height; y++)
                {
                    var pos = new GridPos(x, y);
                    // Fully walled-in cells are the centre block; it shows the active target's colour.
                    Color tile = grid.IsEnclosed(pos) ? Color.Lerp(activeColor, Color.black, 0.3f)
                        : (x + y) % 2 == 0 ? Colors.TileA : Colors.TileB;
                    CreateSprite("Tile", PlaceholderSprites.Square, tile, CellCenter(pos), Vector2.one, TileOrder);

                    Walls walls = grid.GetWalls(pos);
                    // South and West walls are drawn by the neighbouring cell's North and East.
                    // Walls inside the centre block (between two enclosed cells) are skipped.
                    if ((walls & Walls.North) != 0 && !IsInsideBlock(grid, pos, Direction.Up))
                        CreateWall(new Vector3(x + 0.5f, y + 1f), horizontal: true);
                    if ((walls & Walls.East) != 0 && !IsInsideBlock(grid, pos, Direction.Right))
                        CreateWall(new Vector3(x + 1f, y + 0.5f), horizontal: false);
                }
            }

            CreateBorder(grid.Width, grid.Height);
            CreateFrame(grid.Width, grid.Height);

            for (int i = 0; i < grid.Targets.Count; i++)
            {
                Target target = grid.Targets[i];
                bool active = i == round.TargetIndex;
                Color color = Colors.TargetColor(target);
                if (!active)
                    color.a = 0.45f;
                float size = active ? 0.6f : 0.4f;
                SpriteRenderer marker = CreateSprite("Target", PlaceholderSprites.Square, color,
                    CellCenter(target.Position), new Vector2(size, size), TargetOrder);
                marker.transform.localRotation = Quaternion.Euler(0, 0, 45);
            }

            _selectionRing = CreateSprite("Selection", PlaceholderSprites.Circle, Colors.Selection,
                Vector3.zero, new Vector2(0.95f, 0.95f), SelectionOrder).transform;

            foreach (RobotColor color in new[] { RobotColor.Red, RobotColor.Blue, RobotColor.Green, RobotColor.Yellow })
            {
                SpriteRenderer robot = CreateSprite(color + " Robot", PlaceholderSprites.Circle, Colors.RobotColor(color),
                    CellCenter(robots[color]), new Vector2(0.75f, 0.75f), RobotOrder);
                _robots[color] = robot.transform;
            }
            ShowRobots(robots, instant: true);
        }

        /// <summary>Slides robots towards their new cells (or jumps there if <paramref name="instant"/>).</summary>
        public void ShowRobots(RobotPositions robots, bool instant)
        {
            foreach (KeyValuePair<RobotColor, Transform> robot in _robots)
            {
                // A robot still sliding from the previous move finishes it instantly first; otherwise
                // it would cut diagonally from mid-slide to the new destination.
                if (_robotDestinations.TryGetValue(robot.Key, out Vector3 previous))
                    robot.Value.localPosition = previous;

                Vector3 destination = CellCenter(robots[robot.Key]);
                _robotDestinations[robot.Key] = destination;
                if (instant)
                    robot.Value.localPosition = destination;
            }
        }

        public void ShowSelection(RobotColor robot)
        {
            _selected = robot;
        }

        private void Update()
        {
            foreach (KeyValuePair<RobotColor, Transform> robot in _robots)
            {
                Vector3 destination = _robotDestinations[robot.Key];
                robot.Value.localPosition = Vector3.MoveTowards(robot.Value.localPosition, destination, _slideSpeed * Time.deltaTime);
            }

            if (_selectionRing != null && _robots.TryGetValue(_selected, out Transform selected))
                _selectionRing.localPosition = selected.localPosition;
        }

        private static bool IsInsideBlock(GridState grid, GridPos pos, Direction side)
        {
            GridPos neighbour = pos.Step(side);
            return grid.IsInBounds(neighbour) && grid.IsEnclosed(pos) && grid.IsEnclosed(neighbour);
        }

        private void CreateWall(Vector3 center, bool horizontal)
        {
            Vector2 size = horizontal ? new Vector2(1f + WallThickness, WallThickness) : new Vector2(WallThickness, 1f + WallThickness);
            CreateSprite("Wall", PlaceholderSprites.Square, Colors.Wall, center, size, WallOrder);
        }

        private void CreateBorder(int width, int height)
        {
            float t = WallThickness;
            CreateSprite("Border", PlaceholderSprites.Square, Colors.Wall, new Vector3(width / 2f, 0f), new Vector2(width + t, t), WallOrder);
            CreateSprite("Border", PlaceholderSprites.Square, Colors.Wall, new Vector3(width / 2f, height), new Vector2(width + t, t), WallOrder);
            CreateSprite("Border", PlaceholderSprites.Square, Colors.Wall, new Vector3(0f, height / 2f), new Vector2(t, height + t), WallOrder);
            CreateSprite("Border", PlaceholderSprites.Square, Colors.Wall, new Vector3(width, height / 2f), new Vector2(t, height + t), WallOrder);
        }

        // A thick band just outside the border whose colour says what the board is showing
        // (the player's own attempt, a player's replay, the shortest solution); see ShowMode.
        private void CreateFrame(int width, int height)
        {
            _frame.Clear();
            float t = WallThickness;
            float f = FrameThickness;
            float outer = t / 2f + f / 2f;
            _frame.Add(CreateSprite("Frame", PlaceholderSprites.Square, _frameColor, new Vector3(width / 2f, -outer), new Vector2(width + t + 2 * f, f), WallOrder));
            _frame.Add(CreateSprite("Frame", PlaceholderSprites.Square, _frameColor, new Vector3(width / 2f, height + outer), new Vector2(width + t + 2 * f, f), WallOrder));
            _frame.Add(CreateSprite("Frame", PlaceholderSprites.Square, _frameColor, new Vector3(-outer, height / 2f), new Vector2(f, height + t), WallOrder));
            _frame.Add(CreateSprite("Frame", PlaceholderSprites.Square, _frameColor, new Vector3(width + outer, height / 2f), new Vector2(f, height + t), WallOrder));
        }

        /// <summary>Colours the frame for what the board is showing (see <see cref="BoardMode"/>).</summary>
        public void ShowMode(BoardMode mode)
        {
            Color color = Colors.FrameColor(mode);
            if (color == _frameColor)
                return;
            _frameColor = color;
            foreach (SpriteRenderer part in _frame)
                part.color = color;
        }

        private SpriteRenderer CreateSprite(string name, Sprite sprite, Color color, Vector3 position, Vector2 size, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_content, false);
            go.transform.localPosition = position;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            return renderer;
        }
    }
}
