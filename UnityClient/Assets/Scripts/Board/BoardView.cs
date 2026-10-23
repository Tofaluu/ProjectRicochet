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
    /// corner is the world origin. Only displays state; the rules live in <see cref="GameRules"/>. The player's
    /// <see cref="BoardStyle"/> decides how the board and its pieces are drawn (wood, ink or paper).
    /// </summary>
    public class BoardView : MonoBehaviour
    {
        /// <summary>Width of the coloured frame around the board, in cells.</summary>
        public const float FrameThickness = 0.3f;

        // Back to front.
        private const int PlateShadowOrder = -8;
        private const int FrameOrder = -7;
        private const int GroutOrder = -6;
        private const int TileOrder = -5;
        private const int GrainOrder = -4;
        private const int TargetTintOrder = -3;
        private const int BlockShadowOrder = -2;
        private const int BlockOrder = -1;
        private const int BlockTopOrder = 0;
        private const int TargetGlowOrder = 1;
        private const int TargetOutlineOrder = 2;
        private const int TargetOrder = 3;
        private const int WallShadowOrder = 4;
        private const int WallOrder = 5;
        private const int WallTopOrder = 6;
        private const int GhostOrder = 7;
        private const int RobotShadowOrder = 8;
        private const int HaloOrder = 9;
        private const int SelectionOrder = 10;
        private const int RimOrder = 11;
        private const int BodyOrder = 12;
        private const int BodyTopOrder = 13;
        private const int BandOrder = 14;
        private const int HeadOrder = 15;
        private const int EyeRimOrder = 16;
        private const int EyeOrder = 17;
        private const int PupilOrder = 18;
        private const int EffectOrder = 19;

        // Target symbols' outlines grow them by this much of their half-size.
        private const float OutlineGrow = 0.22f;
        // The selection ring's turning speed, in degrees per second (a turn every 6 s).
        private const float SelectionTurnSpeed = 60f;
        // How much bigger an Ink eye's outline is than the eye.
        private const float EyeRimGrow = 0.06f;

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
        private SpriteRenderer _frame, _plateShadow;
        private Transform _content;
        private SpriteRenderer _selectionRing;
        private SpriteRenderer _targetGlow;
        private float _targetGlowFrom;
        // A new board's target glows brighter and wider for a few seconds (a few slow beats, fading back to the usual glow),
        // so the eye finds it before the player starts thinking.
        private const float IntroGlowSeconds = 3f;
        private const int IntroGlowBeats = 3;
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
        private static BoardStyle Style => BoardStyle.Current;
        private static float WallThickness => Style.Kind == BoardKind.Paper ? 0.15f : 0.2f;
        // The board shown and where its robots stand, to redraw it in another style (Restyle).
        private RoundSetup _round;
        private RobotPositions _shownRobots;
        /// <summary>The board's colours (for UI that matches it, e.g. the frame colour of a mode).</summary>
        public GameTheme Theme => Colors;

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
            _pendingSounds.Clear();
            _hasSelection = false;
            _round = round;

            if (Camera.main != null)
                Camera.main.backgroundColor = Palette.Current.Ground;

            GridState grid = round.Grid;
            _target = round.Target;
            Color activeColor = Colors.TargetColor(round.Target);
            int width = grid.Width, height = grid.Height;
            var centre = new Vector3(width / 2f, height / 2f);
            BoardStyle style = Style;

            // The board: its shadow, the coloured frame (what the board shows), then the grout the tiles sit in.
            float frameSize = 2f * (FrameThickness + WallThickness / 2f);
            var plate = new Vector2(width + frameSize, height + frameSize);
            _plateShadow = style.Kind == BoardKind.Ink
                // Ink: a hard press-down shadow straight below, like the Toybox buttons'.
                ? CreateRounded("Plate Shadow", 0.45f, PlateShadowColor, centre + new Vector3(0f, -0.35f), plate, PlateShadowOrder)
                : CreateRounded("Plate Shadow", 0.6f, PlateShadowColor, centre + new Vector3(0.15f, -0.3f), plate + new Vector2(0.3f, 0.3f), PlateShadowOrder);
            _frame = CreateRounded("Frame", 0.45f, Colors.FrameColor(_mode, _target), centre, plate, FrameOrder);
            CreateRounded("Grout", 0.12f, style.Grout, centre, new Vector2(width + WallThickness, height + WallThickness), GroutOrder);

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
                        // Tiles fill their cell but for a hairline, so the grout shows as thin lines (wood: grooves).
                        Color tile = (x + y) % 2 == 0 ? style.TileA : style.TileB;
                        CreateRounded("Tile", 0.03f, tile, CellCenter(pos), new Vector2(0.96f, 0.96f), TileOrder);
                        if (style.Kind == BoardKind.Wood)
                        {
                            // Grain; one-tone wood turns it every other tile like parquet, so the board isn't one flat sheet.
                            SpriteRenderer grain = CreateSprite("Grain", ShapeSprites.Grain, new Color(0.43f, 0.24f, 0.08f, 0.1f),
                                CellCenter(pos), new Vector2(0.96f, 0.96f), GrainOrder);
                            if (style.TileA == style.TileB && (x + y) % 2 == 1)
                                grain.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                        }
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
                Vector3 at = CellCenter(target.Position);
                if (i == round.TargetIndex)
                {
                    Color glow = color;
                    glow.a = 0.55f;
                    _targetGlow = CreateSprite("Target Glow", ShapeSprites.SoftCircle, glow, at, new Vector2(1.5f, 1.5f), TargetGlowOrder);
                    _targetGlowFrom = Time.time;
                    // Its tile takes on the target's colour.
                    Color tint = color;
                    tint.a = style.Kind == BoardKind.Wood ? 0.28f : style.Kind == BoardKind.Ink ? 0.22f : 0.16f;
                    CreateRounded("Target Tile", 0.08f, tint, at, new Vector2(0.96f, 0.96f), TargetTintOrder);
                    Color outline = style.Kind == BoardKind.Wood ? new Color(0.25f, 0.13f, 0.05f)
                        : style.Kind == BoardKind.Ink ? BoardStyle.Ink : Color.white;
                    CreateSymbol(target, color, outline, at, 0.66f);
                }
                else
                {
                    // The others: smaller and faded into the tile, so the active one stands out.
                    Color faded = Color.Lerp(color, style.TileA, style.Kind == BoardKind.Ink ? 0.3f : 0.4f);
                    Color ink = BoardStyle.Ink;
                    Color? outline = style.Kind == BoardKind.Wood ? new Color(0.27f, 0.15f, 0.05f, 0.5f)
                        : style.Kind == BoardKind.Ink ? new Color(ink.r, ink.g, ink.b, 0.45f) : (Color?)null;
                    CreateSymbol(target, faded, outline, at, 0.44f);
                }
            }

            for (int i = 0; i < _moveDots.Length; i++)
            {
                _moveDots[i] = CreateSprite("Move Dot", ShapeSprites.Circle, Color.white, Vector3.zero, new Vector2(0.34f, 0.34f), GhostOrder);
                _moveDots[i].enabled = false;
            }
            _selectionRing = CreateSprite("Selection", ShapeSprites.DashedRing(0.09f, 18), Colors.Selection, Vector3.zero, new Vector2(1f, 1f), SelectionOrder);

            foreach (RobotColor color in AllRobots)
                _robots[color] = CreateRobot(color, robots[color], round.Target.Robot == color);
            ShowRobots(robots, instant: true);
        }

        /// <summary>
        /// Slides robots towards their new cells (or jumps there if <paramref name="instant"/>). A
        /// <paramref name="quiet"/> slide (undo, a replay stepped back) ends without the bump: no thud, dust,
        /// squash or target burst. An instant jump with <paramref name="popIn"/> (a reset) pops the robots that move
        /// in at their new cells, one after another, instead of having them just appear there.
        /// </summary>
        public void ShowRobots(RobotPositions robots, bool instant, bool quiet = false, bool popIn = false)
        {
            _shownRobots = robots;
            int popped = 0;
            foreach (KeyValuePair<RobotColor, RobotVisual> pair in _robots)
            {
                RobotVisual robot = pair.Value;
                // A rewind still playing is cut short, like a slide: the robot is where it was going.
                if (robot.LastRewind != null)
                    FinishRewind(robot);
                // A robot still sliding from the previous move finishes it instantly first; otherwise
                // it would cut diagonally from mid-slide to the new destination.
                robot.Root.localPosition = robot.Destination;

                Vector3 destination = CellCenter(robots[pair.Key]);
                Vector3 delta = destination - robot.Destination;
                // A step back (undo, a replay stepped back) from where this robot last landed rewinds that landing.
                Landing rewind = null;
                if (quiet && !instant && delta != Vector3.zero)
                {
                    if (robot.Landings.Count > 0 && robot.Landings.Peek().At == robot.Destination)
                        rewind = robot.Landings.Pop();
                    else
                        robot.Landings.Clear(); // out of step with the board's history: nothing to rewind
                }
                if (robot.Exploded && rewind == null)
                {
                    // A reset, undo or replay brings a blown-up robot back, popping in where it now stands.
                    robot.Exploded = false;
                    robot.PoppedInAt = Time.time;
                }
                robot.Destination = destination;
                if (instant || delta == Vector3.zero)
                {
                    robot.Moving = false;
                    robot.Root.localPosition = destination;
                    if (instant && delta != Vector3.zero)
                    {
                        robot.Landings.Clear(); // a jump (reset, a new replay) leaves no landings to rewind
                        if (popIn)
                            robot.PoppedInAt = Time.time + PopInStagger * popped++;
                    }
                    continue;
                }
                robot.Moving = true;
                robot.Quiet = quiet;
                robot.Heading = delta.normalized;
                robot.GhostDistance = 0f;
                // A forward slide is recorded for its landing (trail, look before); a step back plays one in reverse.
                robot.Trail = quiet ? null : new List<(ParticleSpec, float)>();
                robot.SlideStartedAt = Time.time;
                robot.LookAtSlideStart = robot.Look;
                if (rewind != null)
                    StartRewind(robot, rewind);
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
            Color color = Style.Wall;
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

        /// <summary>Redraws the board in the current <see cref="BoardStyle"/> (the player picked another), keeping where things are.</summary>
        public void Restyle()
        {
            if (_round == null)
                return;
            float glowFrom = _targetGlowFrom; // a new board's intro glow doesn't start over
            Build(_round, _shownRobots);
            _targetGlowFrom = glowFrom;
        }

        /// <summary>Re-colours the frame and the board's shadow (the palette changed).</summary>
        public void RefreshFrame()
        {
            ShowMode(_mode);
            if (_plateShadow != null)
                _plateShadow.color = PlateShadowColor;
        }

        // The shadow under the board: fainter on light palettes, where the full one reads as a grey slab (Ink: a hard black one).
        private Color PlateShadowColor
        {
            get
            {
                if (Style.Kind == BoardKind.Ink)
                    return new Color(0f, 0f, 0f, Palette.Current.IsLight ? 0.85f : 1f);
                Color shadow = Colors.Shadow;
                if (Palette.Current.IsLight)
                    shadow.a *= 0.4f;
                return shadow;
            }
        }

        private void Update()
        {
            float time = Time.time;
            foreach (KeyValuePair<RobotColor, RobotVisual> pair in _robots)
                AnimateRobot(pair.Key, pair.Value, time);

            if (_selectionRing != null && _robots.TryGetValue(_selected, out RobotVisual selected))
            {
                // A slowly turning dashed ring in a slightly darker shade of the selected robot's colour (Ink: in ink), with a
                // gap of board around the robot, centred on the piece as drawn (a wooden pawn's edge sits a little low).
                bool ink = Style.Kind == BoardKind.Ink;
                Transform ring = _selectionRing.transform;
                ring.localPosition = selected.Root.localPosition + new Vector3(0f, Style.Kind == BoardKind.Wood ? -0.0225f : 0f);
                float size = (ink ? 1.18f : 1.12f) * BurstScale(selected, time);
                ring.localScale = new Vector3(size, size, 1f);
                ring.localRotation = Quaternion.Euler(0f, 0f, -SelectionTurnSpeed * time);
                _selectionRing.color = ink ? BoardStyle.Ink : Color.Lerp(selected.Color, Color.black, 0.15f);
            }
            if (_targetGlow != null)
            {
                float pulse = 1f + 0.12f * Mathf.Sin(time * 3f);
                float age = time - _targetGlowFrom, boost = 0f;
                if (age < IntroGlowSeconds)
                {
                    // Beats from 0 up to 1 and back, each a little weaker than the last.
                    float beat = 0.5f - 0.5f * Mathf.Cos(age / IntroGlowSeconds * IntroGlowBeats * 2f * Mathf.PI);
                    boost = beat * Mathf.SmoothStep(0f, 1f, 1f - age / IntroGlowSeconds * 0.6f);
                }
                float size = 1.5f * pulse + 2.6f * boost;
                _targetGlow.transform.localScale = new Vector3(size, size, 1f);
                Color glow = _targetGlow.color;
                glow.a = 0.55f + 0.45f * boost;
                _targetGlow.color = glow;
            }
            UpdateParticles(time);
            for (int i = _pendingSounds.Count - 1; i >= 0; i--)
            {
                if (time < _pendingSounds[i].At)
                    continue;
                Play(_pendingSounds[i].Clip, _pendingSounds[i].Volume);
                _pendingSounds.RemoveAt(i);
            }

            // The big burst shakes the board for a moment (rewound: the shake builds up to the moment it started).
            float shake = time < _shakeUntil ? (_shakeUntil - time) / ShakeSeconds
                : time >= _rewindShakeStart && time < _rewindShakeEnd ? (time - _rewindShakeStart) / (_rewindShakeEnd - _rewindShakeStart)
                : 0f;
            if (_content != null)
                _content.localPosition = shake > 0f ? (Vector3)(Random.insideUnitCircle * 0.14f * shake) : Vector3.zero;
        }

        // ---- Robots ----

        private sealed class RobotVisual
        {
            public Transform Root;
            public Transform Body;
            public Transform[] Eyes;
            public Transform[] Pupils;
            // Ink outlines around the eyes (null = none), and the face's layout for the style.
            public Transform[] EyeRims;
            public Vector3[] EyeAt;
            public Vector2 EyeSize, PupilSize, LookShift;
            public SpriteRenderer Halo;
            public Color Color;
            public Vector3 Destination;
            public Vector3 Heading;
            public Vector2 Look;
            public bool Moving;
            // The current slide is a step back (undo): no bump when it stops.
            public bool Quiet;
            public float GhostDistance;
            public float ImpactTime = -10f;
            public float NextBlink;
            // The big burst blows the robot up: it swells and vanishes in the flash, and stays gone until the
            // board moves it again (undo, reset, the next replay), when it pops back in.
            public bool Exploded;
            public float ExplodedAt = -10f;
            public float PoppedInAt = -10f;
            // Its landings so far, newest on top, for rewinding them one by one.
            public readonly Stack<Landing> Landings = new Stack<Landing>();
            // A landing playing backwards before the slide back (the celebration, or just the squash), until RewindEnd
            // (null = none); LastRewind stays until the slide back arrives (its reversed trail is still playing).
            public Landing Rewinding, LastRewind;
            public float RewindEnd, SquashRewindEnd = -10f;
            // Where the eyes looked before the rewound move, restored when the slide back arrives.
            public Vector2? LookAfterRewind;
            // The forward slide being recorded for its landing: trail ghosts (spawn time from the slide's start), the look before.
            public List<(ParticleSpec Spec, float At)> Trail;
            public float SlideStartedAt;
            public Vector2 LookAtSlideStart;
        }

        private const float SwellSeconds = 0.1f, PopInSeconds = 0.3f, PopInStagger = 0.06f;

        // The robot's overall size around the big burst (1 = normal).
        private static float BurstScale(RobotVisual robot, float time)
        {
            if (robot.Exploded && robot.Rewinding != null)
            {
                // The swell in reverse: gone until the end of the rewind, then back from a little too big.
                float window = SwellSeconds / RewindSpeed;
                float left = robot.RewindEnd - time;
                return left > window ? 0f : 1f + 0.3f * Mathf.Max(0f, left) / window;
            }
            if (robot.Exploded)
            {
                float t = time - robot.ExplodedAt;
                return t < SwellSeconds ? 1f + 0.3f * t / SwellSeconds : 0f;
            }
            float p = (time - robot.PoppedInAt) / PopInSeconds;
            if (p >= 1f)
                return 1f;
            if (p <= 0f)
                return 0f; // waiting for its turn to pop in
            // Back in with a little overshoot (ease-out-back).
            p -= 1f;
            return 1f + 2.70158f * p * p * p + 1.70158f * p * p;
        }

        private static readonly Vector3[] EyeOffsets = { new Vector3(-0.13f, 0.03f), new Vector3(0.13f, 0.03f) };

        // A round piece with a face, in the board style's look: shadow, (halo if it's the target robot), body, eyes.
        private RobotVisual CreateRobot(RobotColor color, GridPos cell, bool isTargetRobot)
        {
            Color fill = Colors.RobotColor(color);
            var robot = new RobotVisual
            {
                Root = new GameObject(color + " Robot").transform,
                Color = fill,
                Destination = CellCenter(cell),
                NextBlink = Time.time + Random.Range(1.5f, 5f),
                EyeAt = EyeOffsets,
                EyeSize = new Vector2(0.22f, 0.25f),
                PupilSize = new Vector2(0.11f, 0.13f),
                LookShift = new Vector2(0.05f, 0.055f),
            };
            robot.Root.SetParent(_content, false);
            robot.Root.localPosition = robot.Destination;

            BoardKind kind = Style.Kind;
            Color ink = BoardStyle.Ink;
            if (kind == BoardKind.Ink)
                CreateSprite("Shadow", ShapeSprites.Circle, ink, new Vector3(0f, -0.08f), new Vector2(0.9f, 0.9f), RobotShadowOrder, robot.Root);
            else
                CreateSprite("Shadow", ShapeSprites.SoftCircle, kind == BoardKind.Paper ? new Color(0.14f, 0.19f, 0.35f, 0.35f) : Colors.Shadow,
                    new Vector3(0.05f, -0.16f), new Vector2(0.95f, 0.6f), RobotShadowOrder, robot.Root);
            if (isTargetRobot)
            {
                Color halo = fill;
                halo.a = 0.5f;
                robot.Halo = CreateSprite("Target Halo", ShapeSprites.SoftCircle, halo, Vector3.zero, new Vector2(1.45f, 1.45f), HaloOrder, robot.Root);
            }

            robot.Body = new GameObject("Body").transform;
            robot.Body.SetParent(robot.Root, false);
            Color pupil = new Color(0.12f, 0.1f, 0.18f);
            switch (kind)
            {
                case BoardKind.Wood:
                    // A painted wooden pawn seen from above: its dark edge (the thickness), the base, a band, a lighter head with the face.
                    CreateSprite("Edge", ShapeSprites.Circle, Color.Lerp(fill, Color.black, 0.35f), new Vector3(0f, -0.045f), new Vector2(0.9f, 0.9f), RimOrder, robot.Body);
                    CreateSprite("Fill", ShapeSprites.Circle, fill, Vector3.zero, new Vector2(0.9f, 0.9f), BodyOrder, robot.Body);
                    CreateSprite("Band", ShapeSprites.Circle, Color.Lerp(fill, Color.black, 0.2f), new Vector3(0f, 0.03f), new Vector2(0.7f, 0.7f), BandOrder, robot.Body);
                    CreateSprite("Head", ShapeSprites.Circle, Color.Lerp(fill, Color.white, 0.18f), new Vector3(0f, 0.03f), new Vector2(0.6f, 0.6f), HeadOrder, robot.Body);
                    robot.EyeAt = new[] { new Vector3(-0.115f, 0.05f), new Vector3(0.115f, 0.05f) };
                    robot.EyeSize = new Vector2(0.2f, 0.235f);
                    robot.PupilSize = new Vector2(0.1f, 0.12f);
                    robot.LookShift = new Vector2(0.045f, 0.05f);
                    pupil = new Color(0.16f, 0.09f, 0.03f);
                    break;
                case BoardKind.Ink:
                    // Flat colour in a thick ink outline, shaded along the bottom, on a hard ink shadow (the Toybox buttons' look).
                    CreateSprite("Rim", ShapeSprites.Circle, ink, Vector3.zero, new Vector2(0.9f, 0.9f), RimOrder, robot.Body);
                    CreateSprite("Shade", ShapeSprites.Circle, Color.Lerp(fill, Color.black, 0.22f), Vector3.zero, new Vector2(0.76f, 0.76f), BodyOrder, robot.Body);
                    CreateSprite("Fill", ShapeSprites.Circle, fill, new Vector3(0f, 0.035f), new Vector2(0.7f, 0.7f), BodyTopOrder, robot.Body);
                    robot.EyeRims = new Transform[2];
                    pupil = ink;
                    break;
                default:
                    // A sticker: the colour inside a white border.
                    CreateSprite("Rim", ShapeSprites.Circle, Color.white, Vector3.zero, new Vector2(0.9f, 0.9f), RimOrder, robot.Body);
                    CreateSprite("Fill", ShapeSprites.Circle, fill, Vector3.zero, new Vector2(0.72f, 0.72f), BodyOrder, robot.Body);
                    pupil = Style.Wall;
                    break;
            }

            robot.Eyes = new Transform[2];
            robot.Pupils = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                if (robot.EyeRims != null)
                    robot.EyeRims[i] = CreateSprite("Eye Rim", ShapeSprites.Circle, ink, robot.EyeAt[i], robot.EyeSize, EyeRimOrder, robot.Body).transform;
                robot.Eyes[i] = CreateSprite("Eye", ShapeSprites.Circle, Color.white, robot.EyeAt[i], robot.EyeSize, EyeOrder, robot.Body).transform;
                robot.Pupils[i] = CreateSprite("Pupil", ShapeSprites.Circle, pupil, robot.EyeAt[i], robot.PupilSize, PupilOrder, robot.Body).transform;
            }
            return robot;
        }

        private void AnimateRobot(RobotColor color, RobotVisual robot, float time)
        {
            Vector3 along = robot.Heading;
            float stretch = 0f;
            if (robot.Rewinding != null && time >= robot.RewindEnd)
            {
                // The celebration has played backwards: whole again, now slide back.
                robot.Rewinding = null;
                robot.Exploded = false;
                robot.Moving = robot.Root.localPosition != robot.Destination;
            }
            if (robot.Moving)
            {
                Vector3 from = robot.Root.localPosition;
                float speed = _slideSpeed * (robot.LastRewind != null ? RewindSlideSpeed : 1f);
                Vector3 to = Vector3.MoveTowards(from, robot.Destination, speed * Time.deltaTime);
                SpawnGhosts(robot, from, to);
                robot.Root.localPosition = to;
                // Rewound, the robot keeps looking the way it went (a true rewind is a moonwalk) until it is back.
                if (robot.LastRewind == null)
                    robot.Look = new Vector2(along.x, along.y);
                stretch = 0.12f;
                if (to == robot.Destination)
                {
                    robot.Moving = false;
                    if (!robot.Quiet)
                    {
                        robot.ImpactTime = time;
                        // Record what the landing sets off, so it can be rewound.
                        var landing = new Landing { At = robot.Destination };
                        _recording = landing;
                        _celebrated = false;
                        Impact(color, robot);
                        _recording = null;
                        landing.Celebrated = _celebrated;
                        landing.Exploded = robot.Exploded;
                        landing.Trail = robot.Trail;
                        landing.SlideSeconds = time - robot.SlideStartedAt;
                        landing.LookBefore = robot.LookAtSlideStart;
                        robot.Trail = null;
                        robot.Landings.Push(landing);
                    }
                    else if (robot.LastRewind != null)
                    {
                        // Back where the rewound move started: the eyes look where they did before it.
                        if (robot.LookAfterRewind.HasValue)
                            robot.Look = robot.LookAfterRewind.Value;
                        robot.LookAfterRewind = null;
                        robot.LastRewind = null;
                    }
                }
            }
            else
            {
                // Squash against the wall, then a little wobble back (rewound: the wobble backwards, before the slide back).
                float t = time < robot.SquashRewindEnd ? (robot.SquashRewindEnd - time) * RewindSpeed / 0.25f
                    : (time - robot.ImpactTime) / 0.25f;
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
            float eyeHeight = blinking ? robot.EyeSize.y * 0.12f : robot.EyeSize.y;
            for (int i = 0; i < 2; i++)
            {
                robot.Eyes[i].localScale = new Vector3(robot.EyeSize.x, eyeHeight, 1f);
                robot.Pupils[i].localScale = new Vector3(robot.PupilSize.x, blinking ? robot.PupilSize.y * 0.08f : robot.PupilSize.y, 1f);
                robot.Pupils[i].localPosition = robot.EyeAt[i] + new Vector3(robot.Look.x * robot.LookShift.x, robot.Look.y * robot.LookShift.y);
                if (robot.EyeRims != null)
                    robot.EyeRims[i].localScale = new Vector3(robot.EyeSize.x + EyeRimGrow, eyeHeight + EyeRimGrow, 1f);
            }
        }

        // Faint copies left along the slide, so fast moves read as motion instead of a jump.
        private void SpawnGhosts(RobotVisual robot, Vector3 from, Vector3 to)
        {
            if (robot.Quiet && robot.LastRewind?.Trail != null)
                return; // sliding back over a recorded trail: it plays in reverse instead (StartRewind)
            const float spacing = 0.35f;
            float distance = Vector3.Distance(from, to);
            robot.GhostDistance += distance;
            while (robot.GhostDistance >= spacing)
            {
                robot.GhostDistance -= spacing;
                Vector3 at = to - robot.Heading * robot.GhostDistance;
                Color ghost = robot.Color;
                ghost.a = 0.28f;
                ParticleSpec spec = Spawn(ShapeSprites.Circle, ghost, at, Vector3.zero, 0.62f, 0.4f, 0.2f, GhostOrder, "Ghost");
                robot.Trail?.Add((spec, Time.time - robot.SlideStartedAt));
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
            _celebrated = true;
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

        // One effect sprite as spawned: enough to play it again, forwards or backwards.
        private sealed class ParticleSpec
        {
            public Sprite Sprite;
            public Color Color;
            public Vector3 Start, Velocity;
            public float StartSize, EndSize, Life;
            public int Order;
            public string Name;
        }

        private sealed class Particle
        {
            public SpriteRenderer Renderer;
            public ParticleSpec Spec;
            // Shown from Born (hidden before: a rewound trail ghost waits for the robot), starting at StartAge.
            public float Born, StartAge;
            // Played backwards towards age 0, Speed times as fast (an undone landing's rewind).
            public bool Reverse;
            public float Speed = 1f;
            // The landing it belongs to (if any), so a rewind can take over from it.
            public Landing Landing;
        }

        // Everything a robot's landing set off (dust, a celebration), kept per robot so undo / a replay stepped back can
        // play it backwards (RobotVisual.Landings).
        private sealed class Landing
        {
            public Vector3 At;
            public readonly List<ParticleSpec> Specs = new List<ParticleSpec>();
            // It reached the target with a celebration (rewound before the robot slides back), and blew the robot up.
            public bool Celebrated, Exploded;
            // The slide that led here: its trail ghosts, how long it took, and where the robot looked before it.
            public List<(ParticleSpec Spec, float At)> Trail;
            public float SlideSeconds;
            public Vector2 LookBefore;
            // The sounds it played (thud, success, boom), played reversed when it's rewound.
            public readonly List<(AudioClip Clip, float Volume)> Sounds = new List<(AudioClip, float)>();
        }

        // The landing being recorded (set around Impact), or null.
        private Landing _recording;
        // Reversed landing sounds scheduled to end as the robot sets off back (dropped if the rewind is cut short).
        private readonly List<(float At, AudioClip Clip, float Volume, Landing Landing)> _pendingSounds =
            new List<(float, AudioClip, float, Landing)>();
        private bool _celebrated;
        private const float RewindSpeed = 1.6f;
        // The slide back (and its reversed trail) relative to a normal slide; 1 = as fast as the move it undoes.
        private const float RewindSlideSpeed = 1f;
        private float _rewindShakeStart = -10f, _rewindShakeEnd = -10f;

        // Drag: speed falls to 4% per second, so a particle starting at v has moved v * Drift(age) after age seconds
        // (the exact distance, so the same particle can be placed at any age - which a rewind needs).
        private static readonly float DragRate = -Mathf.Log(0.04f);
        private static float Drift(float age) => (1f - Mathf.Exp(-DragRate * age)) / DragRate;

        private ParticleSpec Spawn(Sprite sprite, Color color, Vector3 at, Vector3 velocity, float startSize, float endSize, float life, int order,
            string name = "Effect")
        {
            var spec = new ParticleSpec
            {
                Sprite = sprite, Color = color, Start = at, Velocity = velocity,
                StartSize = startSize, EndSize = endSize, Life = life, Order = order, Name = name,
            };
            _recording?.Specs.Add(spec);
            AddParticle(spec, reverse: false, 1f, _recording);
            return spec;
        }

        private void AddParticle(ParticleSpec spec, bool reverse, float speed, Landing landing, float delay = 0f, float? startAge = null)
        {
            SpriteRenderer renderer = CreateSprite(spec.Name, spec.Sprite, spec.Color, spec.Start,
                new Vector2(spec.StartSize, spec.StartSize), spec.Order);
            var particle = new Particle
            {
                Renderer = renderer, Spec = spec, Born = Time.time + delay, StartAge = startAge ?? (reverse ? spec.Life : 0f),
                Reverse = reverse, Speed = speed, Landing = landing,
            };
            _particles.Add(particle);
            renderer.enabled = delay <= 0f;
            PlaceParticle(particle, particle.StartAge);
        }

        private void UpdateParticles(float time)
        {
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                Particle p = _particles[i];
                if (p.Renderer == null)
                {
                    RemoveParticle(i);
                    continue;
                }
                if (time < p.Born)
                    continue; // not yet
                p.Renderer.enabled = true;
                float elapsed = (time - p.Born) * p.Speed;
                float age = p.Reverse ? p.StartAge - elapsed : p.StartAge + elapsed;
                if (p.Reverse ? age <= 0f : age >= p.Spec.Life)
                {
                    RemoveParticle(i);
                    continue;
                }
                PlaceParticle(p, age);
            }
        }

        private static void PlaceParticle(Particle p, float age)
        {
            ParticleSpec s = p.Spec;
            float t = Mathf.Clamp01(age / s.Life);
            Transform tr = p.Renderer.transform;
            tr.localPosition = s.Start + s.Velocity * Drift(age);
            float size = Mathf.Lerp(s.StartSize, s.EndSize, t);
            tr.localScale = new Vector3(size, size, 1f);
            Color c = s.Color;
            c.a *= 1f - t * t;
            p.Renderer.color = c;
        }

        private void RemoveParticle(int index)
        {
            if (_particles[index].Renderer != null)
                Destroy(_particles[index].Renderer.gameObject);
            _particles.RemoveAt(index);
        }

        private void RemoveParticles(Landing landing, bool reverse)
        {
            for (int i = _particles.Count - 1; i >= 0; i--)
                if (_particles[i].Landing == landing && _particles[i].Reverse == reverse)
                    RemoveParticle(i);
        }

        // ---- Rewind (undo / a replay stepped back) ----

        // Plays the robot's last landing backwards: its dust (and celebration: the burst flies back in, the shake builds
        // up, the boom sucks in, the robot reforms) - a celebration first, then the slide back; plain dust while it slides.
        private void StartRewind(RobotVisual robot, Landing landing)
        {
            RemoveParticles(landing, reverse: false); // still flying from the landing itself: the rewind takes over
            float time = Time.time;
            float effects = 0f;
            foreach (ParticleSpec spec in landing.Specs)
            {
                AddParticle(spec, reverse: true, RewindSpeed, landing);
                effects = Mathf.Max(effects, spec.Life / RewindSpeed);
            }
            // Held before sliding back: through the whole celebration, or just the squash wobble played backwards.
            float squash = 0.25f / RewindSpeed;
            float hold = landing.Celebrated ? Mathf.Max(effects, squash) : squash;
            robot.Rewinding = landing;
            robot.LastRewind = landing;
            robot.RewindEnd = time + hold;
            robot.Moving = false; // until RewindEnd (AnimateRobot)
            robot.LookAfterRewind = landing.LookBefore;
            if (!landing.Exploded)
                robot.SquashRewindEnd = time + squash;
            else
            {
                robot.Exploded = true;
                _rewindShakeStart = time + hold - ShakeSeconds / RewindSpeed;
                _rewindShakeEnd = time + hold;
            }
            // Its sounds backwards, at the rewind's speed, each ending as the robot sets off back (where it began).
            foreach ((AudioClip clip, float volume) in landing.Sounds)
            {
                if (clip == BoardSounds.Success)
                    continue; // the reversed boom already carries an undone win; a backwards chime is just noise
                AudioClip reversed = BoardSounds.Reversed(clip, RewindSpeed);
                if (reversed != null)
                    _pendingSounds.Add((time + Mathf.Max(0f, hold - reversed.length), reversed, volume, landing));
            }

            // The slide's trail in reverse once the robot sets off back (at its normal speed, so it takes as long): a
            // ghost left `at` seconds into a slide lasting `slide` shows while the robot is still on its way to it,
            // brightening, and vanishes as the robot passes it.
            if (landing.Trail == null)
                return;
            float slide = landing.SlideSeconds;
            foreach ((ParticleSpec spec, float at) in landing.Trail)
            {
                float age = Mathf.Min(spec.Life, slide - at);
                if (age > 0f)
                    AddParticle(spec, reverse: true, RewindSlideSpeed, landing,
                        delay: hold + Mathf.Max(0f, slide - at - spec.Life) / RewindSlideSpeed, startAge: age);
            }
        }

        // Cuts a rewind short (the player moved on): its effects vanish and the robot is whole, wherever it was going.
        private void FinishRewind(RobotVisual robot)
        {
            RemoveParticles(robot.LastRewind, reverse: true);
            _pendingSounds.RemoveAll(sound => sound.Landing == robot.LastRewind);
            if (robot.Rewinding != null)
            {
                robot.Exploded = false;
                _rewindShakeEnd = -10f;
            }
            if (robot.LookAfterRewind.HasValue)
                robot.Look = robot.LookAfterRewind.Value;
            robot.Rewinding = null;
            robot.LastRewind = null;
            robot.LookAfterRewind = null;
            robot.SquashRewindEnd = -10f;
        }

        /// <summary>Another player locked in a new best solution.</summary>
        public void PlayOpponentSolved() => Play(BoardSounds.OpponentSolved, 0.8f);

        /// <summary>A skip/done vote, or the board was skipped.</summary>
        public void PlaySkip() => Play(BoardSounds.Skip, 0.8f);

        /// <summary>The solve that's sliding in doesn't beat the best: no celebration when it lands, just a "nope".</summary>
        public void MarkNotShorter() => _notShorter = true;

        private void Play(AudioClip clip, float volume)
        {
            _recording?.Sounds.Add((clip, volume));
            if (_audio != null && _volume > 0f && !PlayerSettings.Muted)
                _audio.PlayOneShot(clip, volume * _volume);
        }

        // ---- Board pieces ----

        private static bool IsInsideBlock(GridState grid, GridPos pos, Direction side)
        {
            GridPos neighbour = pos.Step(side);
            return grid.IsInBounds(neighbour) && grid.IsEnclosed(pos) && grid.IsEnclosed(neighbour);
        }

        // Wood: a lighter top face on the walnut, so walls read as raised fences.
        private static readonly Color WalnutTop = new Color(0.49f, 0.3f, 0.15f);

        private void CreateWall(Vector3 center, Vector2 size)
        {
            float radius = WallThickness / 2f;
            switch (Style.Kind)
            {
                case BoardKind.Wood:
                    Color shadow = Colors.Shadow;
                    shadow.a = 0.45f;
                    CreateRounded("Wall Shadow", radius, shadow, center + new Vector3(0.03f, -0.08f), size, WallShadowOrder);
                    CreateRounded("Wall", radius * 0.6f, Style.Wall, center, size, WallOrder);
                    CreateRounded("Wall Top", radius * 0.5f, WalnutTop, center + new Vector3(0f, 0.025f), size - new Vector2(0.07f, 0.07f), WallTopOrder);
                    break;
                case BoardKind.Ink:
                    Color ink = BoardStyle.Ink;
                    CreateRounded("Wall Shadow", radius, new Color(ink.r, ink.g, ink.b, 0.25f), center + new Vector3(0f, -0.08f), size, WallShadowOrder);
                    CreateRounded("Wall", radius, ink, center, size, WallOrder);
                    break;
                default:
                    CreateRounded("Wall", radius, Style.Wall, center, size, WallOrder);
                    break;
            }
        }

        // The walled-off centre: a raised block showing the active target's symbol (wood: walnut with the symbol in its
        // colour; ink and paper: a block in its colour with a white symbol).
        private void CreateCentreBlock(GridPos min, GridPos max, Target target, Color color)
        {
            var size = new Vector2(max.X - min.X + 1, max.Y - min.Y + 1);
            var centre = new Vector3(min.X + size.x / 2f, min.Y + size.y / 2f);
            Color ink = BoardStyle.Ink;
            switch (Style.Kind)
            {
                case BoardKind.Wood:
                    CreateRounded("Block Shadow", 0.25f, Colors.Shadow, centre + new Vector3(0.06f, -0.12f), size - new Vector2(0.1f, 0.1f), BlockShadowOrder);
                    CreateRounded("Block", 0.25f, Style.Wall, centre, size - new Vector2(0.14f, 0.14f), BlockOrder);
                    CreateRounded("Block Top", 0.2f, WalnutTop, centre + new Vector3(0f, 0.04f), size - new Vector2(0.3f, 0.3f), BlockTopOrder);
                    CreateSymbol(target, color, null, centre + new Vector3(0f, 0.04f), 0.9f);
                    break;
                case BoardKind.Ink:
                    CreateRounded("Block Shadow", 0.25f, ink, centre + new Vector3(0f, -0.12f), size - new Vector2(0.14f, 0.14f), BlockShadowOrder);
                    CreateRounded("Block", 0.25f, ink, centre, size - new Vector2(0.14f, 0.14f), BlockOrder);
                    CreateRounded("Block Top", 0.18f, color, centre, size - new Vector2(0.38f, 0.38f), BlockTopOrder);
                    CreateSymbol(target, Color.white, ink, centre, 0.9f);
                    break;
                default:
                    CreateRounded("Block", 0.2f, Style.Wall, centre, size - new Vector2(0.14f, 0.14f), BlockOrder);
                    CreateRounded("Block Top", 0.14f, color, centre, size - new Vector2(0.3f, 0.3f), BlockTopOrder);
                    CreateSymbol(target, Color.white, null, centre, 0.9f);
                    break;
            }
        }

        // A target symbol, with an outline under it when given.
        private void CreateSymbol(Target target, Color fill, Color? outline, Vector3 at, float size)
        {
            if (outline.HasValue)
                CreateSprite("Target Outline", ShapeSprites.TargetOutline(target.Robot, OutlineGrow), outline.Value, at,
                    Vector2.one * size * (1f + OutlineGrow), TargetOutlineOrder);
            CreateSprite("Target", ShapeSprites.TargetSymbol(target.Robot), fill, at, Vector2.one * size, TargetOrder);
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
