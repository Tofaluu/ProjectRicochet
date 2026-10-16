using System.Collections.Generic;
using Ricochet.Client.Game;
using Ricochet.Client.Theme;
using SharedLogic;
using UnityEngine;

namespace Ricochet.Client.Board
{
    /// <summary>
    /// Draws a <see cref="RoundSetup"/> with code-drawn shapes (<see cref="ShapeSprites"/>) and animates it:
    /// robots slide, squash against walls with a puff of dust, and the target robot reaching the target
    /// bursts and chimes. Cell (x, y) covers world space [x, x+1] x [y, y+1], so the board's bottom-left
    /// corner is the world origin. Only displays state; the rules live in <see cref="GameRules"/>.
    /// </summary>
    public class BoardView : MonoBehaviour
    {
        private const float WallThickness = 0.16f;
        /// <summary>Width of the coloured frame around the board, in cells.</summary>
        public const float FrameThickness = 0.3f;

        // Back to front.
        private const int PlateShadowOrder = -5;
        private const int FrameOrder = -4;
        private const int GroutOrder = -3;
        private const int TileOrder = -2;
        private const int BlockOrder = 0;
        private const int TargetGlowOrder = 1;
        private const int TargetOrder = 2;
        private const int WallShadowOrder = 3;
        private const int WallOrder = 4;
        private const int GhostOrder = 5;
        private const int RobotShadowOrder = 6;
        private const int HaloOrder = 7;
        private const int SelectionOrder = 8;
        private const int RimOrder = 9;
        private const int BodyOrder = 10;
        private const int HighlightOrder = 11;
        private const int EyeOrder = 12;
        private const int PupilOrder = 13;
        private const int EffectOrder = 14;

        private const float ShakeSeconds = 0.3f;

        private static readonly RobotColor[] AllRobots = { RobotColor.Red, RobotColor.Blue, RobotColor.Green, RobotColor.Yellow };

        [Tooltip("Cells per second. 50 crosses the whole board in about 0.3 s.")]
        [SerializeField] private float _slideSpeed = 50f;
        [SerializeField] private GameTheme _theme;
        [Tooltip("Sound effects volume (0 = silent).")]
        [Range(0f, 1f)]
        [SerializeField] private float _volume = 0.5f;

        private readonly Dictionary<RobotColor, RobotVisual> _robots = new Dictionary<RobotColor, RobotVisual>();
        private readonly List<Particle> _particles = new List<Particle>();
        private BoardMode _mode;
        private SpriteRenderer _frame;
        private Transform _content;
        private SpriteRenderer _selectionRing;
        private SpriteRenderer _targetGlow;
        private Target _target;
        private RobotColor _selected;
        private bool _hasSelection;
        private AudioSource _audio;
        private bool _notShorter;
        private float _shakeUntil = -1f;
        private readonly SpriteRenderer[] _moveDots = new SpriteRenderer[4];

        public static Vector3 CellCenter(GridPos pos) => new Vector3(pos.X + 0.5f, pos.Y + 0.5f, 0f);

        public static GridPos WorldToCell(Vector3 world) => new GridPos(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y));

        private GameTheme Colors => GameTheme.OrDefault(_theme);

        private void Awake()
        {
            // Nothing is audible without a listener; the scene's camera doesn't have one.
            if (FindAnyObjectByType<AudioListener>() == null)
            {
                Camera camera = Camera.main;
                (camera != null ? camera.gameObject : gameObject).AddComponent<AudioListener>();
            }
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
        }

        public void Build(RoundSetup round, RobotPositions robots)
        {
            _notShorter = false;
            if (_content != null)
                Destroy(_content.gameObject);
            _content = new GameObject("Board").transform;
            _content.SetParent(transform, false);
            _robots.Clear();
            _particles.Clear();
            _hasSelection = false;

            if (Camera.main != null)
                Camera.main.backgroundColor = Colors.Background;

            GridState grid = round.Grid;
            _target = round.Target;
            Color activeColor = Colors.TargetColor(round.Target);
            int width = grid.Width, height = grid.Height;
            var centre = new Vector3(width / 2f, height / 2f);

            // The board: a soft shadow, the coloured frame (what the board shows), then the grout tiles sit in.
            float frameSize = 2f * (FrameThickness + WallThickness / 2f);
            CreateRounded("Plate Shadow", 0.6f, Colors.Shadow, centre + new Vector3(0.15f, -0.3f),
                new Vector2(width + frameSize + 0.3f, height + frameSize + 0.3f), PlateShadowOrder);
            _frame = CreateRounded("Frame", 0.45f, Colors.FrameColor(_mode, _target), centre, new Vector2(width + frameSize, height + frameSize), FrameOrder);
            CreateRounded("Grout", 0.12f, Colors.Grout, centre, new Vector2(width + WallThickness, height + WallThickness), GroutOrder);

            GridPos blockMin = new GridPos(width, height), blockMax = new GridPos(-1, -1);
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    var pos = new GridPos(x, y);
                    if (grid.IsEnclosed(pos))
                    {
                        blockMin = new GridPos(Mathf.Min(blockMin.X, x), Mathf.Min(blockMin.Y, y));
                        blockMax = new GridPos(Mathf.Max(blockMax.X, x), Mathf.Max(blockMax.Y, y));
                    }
                    else
                    {
                        Color tile = (x + y) % 2 == 0 ? Colors.TileA : Colors.TileB;
                        CreateRounded("Tile", 0.1f, tile, CellCenter(pos), new Vector2(0.9f, 0.9f), TileOrder);
                    }

                    Walls walls = grid.GetWalls(pos);
                    // South and West walls are drawn by the neighbouring cell's North and East.
                    // Walls inside the centre block (between two enclosed cells) are skipped.
                    if ((walls & Walls.North) != 0 && !IsInsideBlock(grid, pos, Direction.Up))
                        CreateWall(new Vector3(x + 0.5f, y + 1f), new Vector2(1f + WallThickness, WallThickness));
                    if ((walls & Walls.East) != 0 && !IsInsideBlock(grid, pos, Direction.Right))
                        CreateWall(new Vector3(x + 1f, y + 0.5f), new Vector2(WallThickness, 1f + WallThickness));
                }
            }
            CreateWall(new Vector3(width / 2f, 0f), new Vector2(width + WallThickness, WallThickness));
            CreateWall(new Vector3(width / 2f, height), new Vector2(width + WallThickness, WallThickness));
            CreateWall(new Vector3(0f, height / 2f), new Vector2(WallThickness, height + WallThickness));
            CreateWall(new Vector3(width, height / 2f), new Vector2(WallThickness, height + WallThickness));

            if (blockMax.X >= 0)
                CreateCentreBlock(blockMin, blockMax, round.Target, activeColor);

            for (int i = 0; i < grid.Targets.Count; i++)
            {
                Target target = grid.Targets[i];
                Color color = Colors.TargetColor(target);
                Sprite symbol = ShapeSprites.TargetSymbol(target.Robot);
                if (i == round.TargetIndex)
                {
                    Color glow = color;
                    glow.a = 0.55f;
                    _targetGlow = CreateSprite("Target Glow", ShapeSprites.SoftCircle, glow, CellCenter(target.Position), new Vector2(1.5f, 1.5f), TargetGlowOrder);
                    CreateSprite("Active Target", symbol, color, CellCenter(target.Position), new Vector2(0.66f, 0.66f), TargetOrder);
                }
                else
                {
                    color.a = 0.5f;
                    CreateSprite("Target", symbol, color, CellCenter(target.Position), new Vector2(0.4f, 0.4f), TargetOrder);
                }
            }

            for (int i = 0; i < _moveDots.Length; i++)
            {
                _moveDots[i] = CreateSprite("Move Dot", ShapeSprites.Circle, Color.white, Vector3.zero, new Vector2(0.34f, 0.34f), GhostOrder);
                _moveDots[i].enabled = false;
            }
            _selectionRing = CreateSprite("Selection", ShapeSprites.Ring(0.16f), Colors.Selection, Vector3.zero, new Vector2(1f, 1f), SelectionOrder);

            foreach (RobotColor color in AllRobots)
                _robots[color] = CreateRobot(color, robots[color], round.Target.Robot == color);
            ShowRobots(robots, instant: true);
        }

        /// <summary>Slides robots towards their new cells (or jumps there if <paramref name="instant"/>).</summary>
        public void ShowRobots(RobotPositions robots, bool instant)
        {
            foreach (KeyValuePair<RobotColor, RobotVisual> pair in _robots)
            {
                RobotVisual robot = pair.Value;
                if (robot.Exploded)
                {
                    // A reset, undo or replay brings a blown-up robot back, popping in where it now stands.
                    robot.Exploded = false;
                    robot.PoppedInAt = Time.time;
                }
                // A robot still sliding from the previous move finishes it instantly first; otherwise
                // it would cut diagonally from mid-slide to the new destination.
                robot.Root.localPosition = robot.Destination;

                Vector3 destination = CellCenter(robots[pair.Key]);
                Vector3 delta = destination - robot.Destination;
                robot.Destination = destination;
                if (instant || delta == Vector3.zero)
                {
                    robot.Moving = false;
                    robot.Root.localPosition = destination;
                    continue;
                }
                robot.Moving = true;
                robot.Heading = delta.normalized;
                robot.GhostDistance = 0f;
            }
        }

        public void ShowSelection(RobotColor robot)
        {
            // The blip is for the player's own picks; replays switch robots silently.
            if (_hasSelection && robot != _selected && (_mode == BoardMode.Practice || _mode == BoardMode.Round))
                Play(BoardSounds.Select, 0.6f);
            _selected = robot;
            _hasSelection = true;
        }

        /// <summary>
        /// Dots where <paramref name="robot"/> would stop moving in each direction (indexed by <see cref="Direction"/>;
        /// null = no dot), or none if <paramref name="stops"/> is null.
        /// </summary>
        public void ShowMovePreview(RobotColor robot, GridPos?[] stops)
        {
            // Neutral and dark (like chess.com's): a robot-coloured dot would look like a target (blue = circle).
            Color color = Colors.Wall;
            color.a = 0.3f;
            for (int i = 0; i < _moveDots.Length; i++)
            {
                SpriteRenderer dot = _moveDots[i];
                if (dot == null)
                    continue;
                dot.enabled = stops != null && stops[i].HasValue;
                if (!dot.enabled)
                    continue;
                dot.transform.localPosition = CellCenter(stops[i].Value);
                dot.color = color;
            }
        }

        /// <summary>Colours the frame for what the board is showing (see <see cref="GameTheme.FrameColor"/>).</summary>
        public void ShowMode(BoardMode mode)
        {
            _mode = mode;
            if (_frame != null)
                _frame.color = Colors.FrameColor(mode, _target);
        }

        private void Update()
        {
            float time = Time.time;
            foreach (KeyValuePair<RobotColor, RobotVisual> pair in _robots)
                AnimateRobot(pair.Key, pair.Value, time);

            if (_selectionRing != null && _robots.TryGetValue(_selected, out RobotVisual selected))
            {
                // A slightly darker shade of the selected robot's own colour, gently pulsing.
                Transform ring = _selectionRing.transform;
                ring.localPosition = selected.Root.localPosition;
                float pulse = (1f + 0.05f * Mathf.Sin(time * 6f)) * BurstScale(selected, time);
                ring.localScale = new Vector3(pulse, pulse, 1f);
                _selectionRing.color = Color.Lerp(selected.Color, Colors.Selection, 0.2f);
            }
            if (_targetGlow != null)
            {
                float pulse = 1f + 0.12f * Mathf.Sin(time * 3f);
                _targetGlow.transform.localScale = new Vector3(1.5f * pulse, 1.5f * pulse, 1f);
            }
            UpdateParticles(time);

            // The big burst shakes the board for a moment.
            if (_content != null)
                _content.localPosition = time < _shakeUntil
                    ? (Vector3)(Random.insideUnitCircle * 0.14f * ((_shakeUntil - time) / ShakeSeconds))
                    : Vector3.zero;
        }

        // ---- Robots ----

        private sealed class RobotVisual
        {
            public Transform Root;
            public Transform Body;
            public Transform[] Eyes;
            public Transform[] Pupils;
            public SpriteRenderer Halo;
            public Color Color;
            public Vector3 Destination;
            public Vector3 Heading;
            public Vector2 Look;
            public bool Moving;
            public float GhostDistance;
            public float ImpactTime = -10f;
            public float NextBlink;
            // The big burst blows the robot up: it swells and vanishes in the flash, and stays gone until the
            // board moves it again (undo, reset, the next replay), when it pops back in.
            public bool Exploded;
            public float ExplodedAt = -10f;
            public float PoppedInAt = -10f;
        }

        private const float SwellSeconds = 0.1f, PopInSeconds = 0.3f;

        // The robot's overall size around the big burst (1 = normal).
        private static float BurstScale(RobotVisual robot, float time)
        {
            if (robot.Exploded)
            {
                float t = time - robot.ExplodedAt;
                return t < SwellSeconds ? 1f + 0.3f * t / SwellSeconds : 0f;
            }
            float p = (time - robot.PoppedInAt) / PopInSeconds;
            if (p >= 1f)
                return 1f;
            // Back in with a little overshoot (ease-out-back).
            p -= 1f;
            return 1f + 2.70158f * p * p * p + 1.70158f * p * p;
        }

        private static readonly Vector3[] EyeOffsets = { new Vector3(-0.13f, 0.03f), new Vector3(0.13f, 0.03f) };

        // A glossy round piece with a face: shadow, (halo if it's the target robot), rim, body, shine, eyes.
        private RobotVisual CreateRobot(RobotColor color, GridPos cell, bool isTargetRobot)
        {
            Color fill = Colors.RobotColor(color);
            var robot = new RobotVisual
            {
                Root = new GameObject(color + " Robot").transform,
                Color = fill,
                Destination = CellCenter(cell),
                NextBlink = Time.time + Random.Range(1.5f, 5f),
            };
            robot.Root.SetParent(_content, false);
            robot.Root.localPosition = robot.Destination;

            CreateSprite("Shadow", ShapeSprites.SoftCircle, Colors.Shadow, new Vector3(0.05f, -0.16f), new Vector2(0.95f, 0.6f), RobotShadowOrder, robot.Root);
            if (isTargetRobot)
            {
                Color halo = fill;
                halo.a = 0.5f;
                robot.Halo = CreateSprite("Target Halo", ShapeSprites.SoftCircle, halo, Vector3.zero, new Vector2(1.45f, 1.45f), HaloOrder, robot.Root);
            }

            robot.Body = new GameObject("Body").transform;
            robot.Body.SetParent(robot.Root, false);
            CreateSprite("Rim", ShapeSprites.Circle, Color.Lerp(fill, Color.black, 0.35f), Vector3.zero, new Vector2(0.86f, 0.86f), RimOrder, robot.Body);
            CreateSprite("Fill", ShapeSprites.Circle, fill, new Vector3(0f, 0.015f), new Vector2(0.76f, 0.76f), BodyOrder, robot.Body);
            CreateSprite("Shine", ShapeSprites.Circle, new Color(1f, 1f, 1f, 0.45f), new Vector3(-0.17f, 0.2f), new Vector2(0.26f, 0.16f), HighlightOrder, robot.Body)
                .transform.localRotation = Quaternion.Euler(0, 0, 35);

            robot.Eyes = new Transform[2];
            robot.Pupils = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                robot.Eyes[i] = CreateSprite("Eye", ShapeSprites.Circle, Color.white, EyeOffsets[i], new Vector2(0.18f, 0.2f), EyeOrder, robot.Body).transform;
                robot.Pupils[i] = CreateSprite("Pupil", ShapeSprites.Circle, new Color(0.12f, 0.1f, 0.18f), EyeOffsets[i], new Vector2(0.09f, 0.1f), PupilOrder, robot.Body).transform;
            }
            return robot;
        }

        private void AnimateRobot(RobotColor color, RobotVisual robot, float time)
        {
            Vector3 along = robot.Heading;
            float stretch = 0f;
            if (robot.Moving)
            {
                Vector3 from = robot.Root.localPosition;
                Vector3 to = Vector3.MoveTowards(from, robot.Destination, _slideSpeed * Time.deltaTime);
                SpawnGhosts(robot, from, to);
                robot.Root.localPosition = to;
                robot.Look = new Vector2(along.x, along.y);
                stretch = 0.12f;
                if (to == robot.Destination)
                {
                    robot.Moving = false;
                    robot.ImpactTime = time;
                    Impact(color, robot);
                }
            }
            else
            {
                // Squash against the wall, then a little wobble back.
                float t = (time - robot.ImpactTime) / 0.25f;
                if (t < 1f)
                    stretch = -0.3f * Mathf.Sin(t * Mathf.PI * 1.5f) * (1f - t);
            }
            float burst = BurstScale(robot, time);
            robot.Root.localScale = new Vector3(burst, burst, 1f);
            float alongScale = 1f + stretch, acrossScale = 1f - stretch * 0.7f;
            robot.Body.localScale = Mathf.Abs(along.x) > 0.5f
                ? new Vector3(alongScale, acrossScale, 1f)
                : new Vector3(acrossScale, alongScale, 1f);

            if (robot.Halo != null)
            {
                Color halo = robot.Halo.color;
                halo.a = 0.4f + 0.15f * Mathf.Sin(time * 2.5f);
                robot.Halo.color = halo;
            }

            // Eyes look the way the robot last moved, and blink now and then.
            bool blinking = time > robot.NextBlink;
            if (time > robot.NextBlink + 0.12f)
                robot.NextBlink = time + Random.Range(2.5f, 6f);
            for (int i = 0; i < 2; i++)
            {
                robot.Eyes[i].localScale = new Vector3(0.22f, blinking ? 0.03f : 0.25f, 1f);
                robot.Pupils[i].localScale = new Vector3(0.11f, blinking ? 0.01f : 0.13f, 1f);
                robot.Pupils[i].localPosition = EyeOffsets[i] + new Vector3(robot.Look.x * 0.05f, robot.Look.y * 0.055f);
            }
        }

        // Faint copies left along the slide, so fast moves read as motion instead of a jump.
        private void SpawnGhosts(RobotVisual robot, Vector3 from, Vector3 to)
        {
            const float spacing = 0.35f;
            float distance = Vector3.Distance(from, to);
            robot.GhostDistance += distance;
            while (robot.GhostDistance >= spacing)
            {
                robot.GhostDistance -= spacing;
                Vector3 at = to - robot.Heading * robot.GhostDistance;
                Color ghost = robot.Color;
                ghost.a = 0.28f;
                Spawn(ShapeSprites.Circle, ghost, at, Vector3.zero, 0.62f, 0.4f, 0.2f, GhostOrder, "Ghost");
            }
        }

        private void Impact(RobotColor color, RobotVisual robot)
        {
            Vector3 contact = robot.Destination + robot.Heading * 0.3f;
            Vector3 side = new Vector3(-robot.Heading.y, robot.Heading.x);
            for (int i = 0; i < 6; i++)
            {
                float spread = (i % 2 == 0 ? 1f : -1f) * Random.Range(0.8f, 1.8f);
                Vector3 velocity = side * spread - robot.Heading * Random.Range(0.3f, 0.8f);
                Spawn(ShapeSprites.SoftCircle, Colors.Dust, contact + side * spread * 0.08f, velocity, 0.3f, 0.6f, 0.4f, EffectOrder);
            }
            Play(BoardSounds.Thud, 0.8f);

            if (WorldToCell(robot.Destination) == _target.Position && _target.AcceptsRobot(color))
                Celebrate(robot, Colors.TargetColor(_target));
        }

        private void Celebrate(RobotVisual robot, Color color)
        {
            Vector3 at = robot.Destination;
            if (_notShorter)
            {
                _notShorter = false;
                Spawn(ShapeSprites.Ring(0.12f), Colors.MutedText, at, Vector3.zero, 0.9f, 1.6f, 0.35f, EffectOrder);
                Play(BoardSounds.NotShorter, 0.8f);
                return;
            }
            bool ownSolve = _mode == BoardMode.Practice || _mode == BoardMode.Round;
            if (PlayerSettings.BigBurst)
            {
                // Replays too (with the boom); the success chime is only for the player's own solve.
                Explode(at, color, ownSolve);
                robot.Exploded = true;
                robot.ExplodedAt = Time.time;
                return;
            }
            // An expanding ring, then stars and sparks flying out from the robot's edge.
            Spawn(ShapeSprites.Ring(0.12f), color, at, Vector3.zero, 0.9f, 2.8f, 0.5f, EffectOrder);
            Color light = Color.Lerp(color, Color.white, 0.35f);
            Sprite star = ShapeSprites.TargetSymbol(RobotColor.Yellow);
            for (int i = 0; i < 18; i++)
            {
                float angle = i / 18f * Mathf.PI * 2f + Random.Range(-0.15f, 0.15f);
                var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle));
                bool spark = i % 3 == 0;
                Spawn(spark ? ShapeSprites.Circle : star, spark ? Color.white : light, at + direction * 0.4f,
                    direction * Random.Range(4.5f, 7.5f), spark ? 0.14f : 0.32f, 0.04f, Random.Range(0.55f, 0.85f), EffectOrder);
            }
            // Only for the player's own solve; replays (theirs or the shortest) just show the burst.
            if (_mode == BoardMode.Practice || _mode == BoardMode.Round)
                Play(BoardSounds.Success, 0.7f);
        }

        // Unless turned off (PlayerSettings.BigBurst), reaching the target (own solve or a replay): a flash, two
        // shockwaves, a shower of stars, sparks and confetti in the robots' colours, smoke, a boom and a short shake.
        private void Explode(Vector3 at, Color color, bool ownSolve)
        {
            Color light = Color.Lerp(color, Color.white, 0.35f);
            Spawn(ShapeSprites.SoftCircle, new Color(1f, 1f, 1f, 0.9f), at, Vector3.zero, 0.8f, 4.5f, 0.35f, EffectOrder);
            Spawn(ShapeSprites.Ring(0.1f), color, at, Vector3.zero, 0.9f, 6f, 0.6f, EffectOrder);
            Spawn(ShapeSprites.Ring(0.06f), light, at, Vector3.zero, 0.6f, 9f, 0.85f, EffectOrder);

            Sprite star = ShapeSprites.TargetSymbol(RobotColor.Yellow);
            for (int i = 0; i < 44; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle));
                int kind = i % 4;
                Sprite sprite = kind == 0 ? ShapeSprites.Circle : star;
                Color piece = kind == 0 ? Color.white : kind == 1 ? color : kind == 2 ? light : Colors.RobotColor(AllRobots[i / 4 % 4]);
                float size = kind == 0 ? 0.16f : Random.Range(0.28f, 0.5f);
                Spawn(sprite, piece, at + direction * 0.3f, direction * Random.Range(6f, 15f), size, 0.04f,
                    Random.Range(0.6f, 1.1f), EffectOrder);
            }
            for (int i = 0; i < 10; i++)
            {
                Vector3 drift = Random.insideUnitCircle * 2.2f;
                Spawn(ShapeSprites.SoftCircle, Colors.Dust, at + drift * 0.15f, drift, 0.6f, 1.8f, Random.Range(0.7f, 1f), EffectOrder);
            }
            _shakeUntil = Time.time + ShakeSeconds;
            if (ownSolve)
                Play(BoardSounds.Success, 0.7f);
            Play(BoardSounds.Boom, 0.8f);
        }

        /// <summary>A countdown second (3, 2, 1).</summary>
        public void PlayTick() => Play(BoardSounds.Tick, 0.8f);

        /// <summary>The board arrived after a countdown.</summary>
        public void PlayStart() => Play(BoardSounds.Start, 0.8f);

        // ---- Short-lived effect sprites ----

        private sealed class Particle
        {
            public SpriteRenderer Renderer;
            public Color Color;
            public Vector3 Velocity;
            public float Born, Life, StartSize, EndSize;
        }

        private void Spawn(Sprite sprite, Color color, Vector3 at, Vector3 velocity, float startSize, float endSize, float life, int order,
            string name = "Effect")
        {
            SpriteRenderer renderer = CreateSprite(name, sprite, color, at, new Vector2(startSize, startSize), order);
            _particles.Add(new Particle
            {
                Renderer = renderer, Color = color, Velocity = velocity,
                Born = Time.time, Life = life, StartSize = startSize, EndSize = endSize,
            });
        }

        private void UpdateParticles(float time)
        {
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                Particle p = _particles[i];
                float t = (time - p.Born) / p.Life;
                if (t >= 1f || p.Renderer == null)
                {
                    if (p.Renderer != null)
                        Destroy(p.Renderer.gameObject);
                    _particles.RemoveAt(i);
                    continue;
                }
                Transform tr = p.Renderer.transform;
                tr.localPosition += p.Velocity * Time.deltaTime;
                p.Velocity *= Mathf.Pow(0.04f, Time.deltaTime); // drag
                float size = Mathf.Lerp(p.StartSize, p.EndSize, t);
                tr.localScale = new Vector3(size, size, 1f);
                Color c = p.Color;
                c.a *= 1f - t * t;
                p.Renderer.color = c;
            }
        }

        /// <summary>Another player locked in a new best solution.</summary>
        public void PlayOpponentSolved() => Play(BoardSounds.OpponentSolved, 0.8f);

        /// <summary>A skip/done vote, or the board was skipped.</summary>
        public void PlaySkip() => Play(BoardSounds.Skip, 0.8f);

        /// <summary>The solve that's sliding in doesn't beat the best: no celebration when it lands, just a "nope".</summary>
        public void MarkNotShorter() => _notShorter = true;

        private void Play(AudioClip clip, float volume)
        {
            if (_audio != null && _volume > 0f && !PlayerSettings.Muted)
                _audio.PlayOneShot(clip, volume * _volume);
        }

        // ---- Board pieces ----

        private static bool IsInsideBlock(GridState grid, GridPos pos, Direction side)
        {
            GridPos neighbour = pos.Step(side);
            return grid.IsInBounds(neighbour) && grid.IsEnclosed(pos) && grid.IsEnclosed(neighbour);
        }

        private void CreateWall(Vector3 center, Vector2 size)
        {
            CreateRounded("Wall Shadow", WallThickness / 2f, Colors.Shadow, center + new Vector3(0.03f, -0.06f), size, WallShadowOrder);
            CreateRounded("Wall", WallThickness / 2f, Colors.Wall, center, size, WallOrder);
        }

        // The walled-off centre: a raised block in the active target's colour showing its symbol.
        private void CreateCentreBlock(GridPos min, GridPos max, Target target, Color color)
        {
            var size = new Vector2(max.X - min.X + 1, max.Y - min.Y + 1);
            var centre = new Vector3(min.X + size.x / 2f, min.Y + size.y / 2f);
            CreateRounded("Block Shadow", 0.25f, Colors.Shadow, centre + new Vector3(0.06f, -0.12f), size - new Vector2(0.1f, 0.1f), BlockOrder);
            CreateRounded("Block", 0.25f, Color.Lerp(color, Color.black, 0.25f), centre, size - new Vector2(0.14f, 0.14f), BlockOrder);
            CreateRounded("Block Top", 0.2f, color, centre + new Vector3(0f, 0.04f), size - new Vector2(0.34f, 0.34f), BlockOrder);
            CreateSprite("Block Symbol", ShapeSprites.TargetSymbol(target.Robot), new Color(1f, 1f, 1f, 0.9f),
                centre + new Vector3(0f, 0.04f), new Vector2(0.9f, 0.9f), TargetOrder);
        }

        private SpriteRenderer CreateRounded(string name, float radius, Color color, Vector3 position, Vector2 size, int order)
        {
            SpriteRenderer renderer = CreateSprite(name, ShapeSprites.RoundedRect(radius), color, position, Vector2.one, order);
            renderer.drawMode = SpriteDrawMode.Sliced;
            renderer.size = size;
            return renderer;
        }

        private SpriteRenderer CreateSprite(string name, Sprite sprite, Color color, Vector3 position, Vector2 size, int order, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : _content, false);
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
