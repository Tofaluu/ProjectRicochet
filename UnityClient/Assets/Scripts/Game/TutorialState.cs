using SharedLogic;

namespace Ricochet.Client.Game
{
    public enum TutorialStep
    {
        /// <summary>Nothing moved yet: try moving green right.</summary>
        Slide,
        /// <summary>Green slid past the cell above its target, to the edge.</summary>
        Overshot,
        /// <summary>Back at the start after overshooting: move red up first.</summary>
        Blocker,
        /// <summary>Red is in place: now green right.</summary>
        MoveRight,
        /// <summary>Green stopped right above the target: now down into it.</summary>
        MoveDown,
        /// <summary>Some other position: reset and try again.</summary>
        Astray,
        Solved,
    }

    /// <summary>
    /// The first-time tutorial: a hand-made board solved in 3 moves. Like every target, green's sits in an L of two walls
    /// (left and bottom), so it has to come in from above. Green going right slides past; red going up first stops under a
    /// wall right where green needs to stop, then green right and down. Which prompt to show is worked out from where the
    /// robots are. Plain C#: AppUI shows it.
    /// </summary>
    public sealed class TutorialState
    {
        public static readonly GridPos GreenStart = new GridPos(2, 14);
        public static readonly GridPos TargetCell = new GridPos(11, 11);
        public static readonly GridPos Overshoot = new GridPos(15, 14);
        public static readonly GridPos AboveTarget = new GridPos(11, 14);
        public static readonly GridPos RedStart = new GridPos(12, 6);
        public static readonly GridPos RedBlock = new GridPos(12, 14);
        public const int BestMoves = 3;

        private bool _overshot;

        public bool IsActive { get; private set; }
        /// <summary>The tutorial's board while it runs (another board on screen means the player left it).</summary>
        public RoundSetup Round { get; private set; }
        public TutorialStep Step { get; private set; }

        /// <summary>
        /// The board: green near the top left, its target below and to the right in a corner of walls, red below the wall
        /// that stops it beside the cell above the target. The rest is decoration away from those paths; everything that
        /// matters sits above the bottom rows, which the prompt card covers on a computer.
        /// </summary>
        public static RoundSetup Board()
        {
            var grid = new GridState();
            grid.AddBlockedArea(new GridPos(7, 7), 2, 2);
            grid.AddTarget(new Target(TargetCell, RobotColor.Green));
            grid.AddWall(TargetCell, Direction.Left);
            grid.AddWall(TargetCell, Direction.Down);
            grid.AddWall(RedBlock, Direction.Up); // red going up stops beside the cell above the target
            // Decoration: other targets in their corners, a few edge stubs.
            grid.AddTarget(new Target(new GridPos(4, 3), RobotColor.Red));
            grid.AddWall(new GridPos(4, 3), Direction.Down);
            grid.AddWall(new GridPos(4, 3), Direction.Left);
            grid.AddTarget(new Target(new GridPos(13, 12), RobotColor.Blue));
            grid.AddWall(new GridPos(13, 12), Direction.Down);
            grid.AddWall(new GridPos(13, 12), Direction.Right);
            grid.AddTarget(new Target(new GridPos(5, 12), RobotColor.Yellow));
            grid.AddWall(new GridPos(5, 12), Direction.Down);
            grid.AddWall(new GridPos(5, 12), Direction.Right);
            grid.AddTarget(new Target(new GridPos(10, 2), RobotColor.Green));
            grid.AddWall(new GridPos(10, 2), Direction.Down);
            grid.AddWall(new GridPos(10, 2), Direction.Right);
            grid.AddWall(new GridPos(0, 5), Direction.Up);
            grid.AddWall(new GridPos(15, 4), Direction.Up);
            grid.AddWall(new GridPos(6, 0), Direction.Right);
            var robots = new RobotPositions(RedStart, new GridPos(5, 9), GreenStart, new GridPos(14, 9));
            return new RoundSetup(grid, robots, 0);
        }

        public void Start()
        {
            IsActive = true;
            _overshot = false;
            Step = TutorialStep.Slide;
            Round = Board();
        }

        public void End()
        {
            IsActive = false;
            Round = null;
        }

        /// <summary>Works out the step from the board; true if it changed.</summary>
        public bool Update(RobotPositions robots, int moves, bool solved)
        {
            TutorialStep before = Step;
            GridPos green = robots[RobotColor.Green], red = robots[RobotColor.Red];
            bool othersHome = Round == null || (robots[RobotColor.Blue] == Round.Robots[RobotColor.Blue]
                && robots[RobotColor.Yellow] == Round.Robots[RobotColor.Yellow]);
            if (solved)
                Step = TutorialStep.Solved;
            else if (moves == 0)
                Step = _overshot ? TutorialStep.Blocker : TutorialStep.Slide;
            else if (othersHome && green == Overshoot && red == RedStart)
            {
                Step = TutorialStep.Overshot;
                _overshot = true;
            }
            else if (othersHome && red == RedBlock && green == GreenStart)
                Step = TutorialStep.MoveRight;
            else if (othersHome && red == RedBlock && green == AboveTarget)
                Step = TutorialStep.MoveDown;
            else
                Step = TutorialStep.Astray;
            return Step != before;
        }

        /// <summary>Which of the 4 progress dots is lit (0-3).</summary>
        public int Progress => Step switch
        {
            TutorialStep.Slide => 0,
            TutorialStep.Overshot => 1,
            TutorialStep.Solved => 3,
            _ => 2,
        };

        /// <summary>The robot the step is about, selected for the player when the step starts.</summary>
        public RobotColor? RobotToSelect => Step switch
        {
            TutorialStep.Slide => RobotColor.Green,
            TutorialStep.Blocker => RobotColor.Red,
            TutorialStep.MoveRight => RobotColor.Green,
            TutorialStep.MoveDown => RobotColor.Green,
            _ => (RobotColor?)null,
        };

        public string Title(int moves) => Step switch
        {
            TutorialStep.Slide => "Robots slide until they hit something",
            TutorialStep.Overshot => "It slid right past",
            TutorialStep.Blocker => "Use another robot as a blocker",
            TutorialStep.MoveRight => "Now move green right",
            TutorialStep.MoveDown => "Now down into the target",
            TutorialStep.Astray => "Not quite",
            _ => moves <= BestMoves ? $"Solved in {moves} moves!" : $"Solved in {moves} moves",
        };

        /// <summary>The prompt, worded for a touch screen (tap, swipe) or a mouse and keyboard.</summary>
        public string Body(bool touch, int moves) => Step switch
        {
            TutorialStep.Slide => "Get the green robot onto the green triangle. Try moving it right: " +
                (touch ? "swipe it or tap the dot." : "press the right arrow key or click the dot."),
            TutorialStep.Overshot => "Robots only stop at walls, the edge or another robot, and nothing stopped it above the target. " +
                (touch ? "Tap Undo" : "Press Z to undo") + " and find something to stop it.",
            TutorialStep.Blocker => "Move the red robot up first. It stops under the wall, right where green needs to stop.",
            TutorialStep.MoveRight => "This time the red robot stops it right above the target.",
            TutorialStep.MoveDown => "Targets sit in a corner of two walls. Coming in from above, the bottom wall stops it right on the triangle.",
            TutorialStep.Astray => (touch ? "Tap Reset" : "Press R") + " to put the robots back and try again.",
            _ => (moves <= BestMoves ? "" : $"The best is {BestMoves}. ") +
                "That's the game: get the robot to its target in as few moves as you can. Fewest moves wins.",
        };
    }
}
