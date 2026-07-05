using Grasshopper.Kernel;
using Marmoset.Games.Snake;
using Marmoset.Games.Common;
using System;

namespace Marmoset.Components
{
    public class SnakeComponent : GH_Component
    {
        private SnakeGame _game;
        private int _lastHeight = -1;
        private int _lastSeed = -1;
        private int _lastWidth = -1;
        private bool _lastWrap;
        private Direction _lastDirection = Direction.None;
        private bool _scheduled;
        private bool _scheduledFrame;

        public SnakeComponent()
          : base("Snake", "Snake",
            "A simple grid-based Snake game state component.",
            "Marmoset", "Games")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("Width", "W", "Board width.", GH_ParamAccess.item, 20);
            pManager.AddIntegerParameter("Height", "H", "Board height.", GH_ParamAccess.item, 20);
            pManager.AddIntegerParameter("Direction", "D", "Move direction: -1 None, 0 Up, 1 Down, 2 Left, 3 Right.", GH_ParamAccess.item, -1);
            pManager.AddBooleanParameter("Start", "S", "Start the game", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Reset", "R", "Reset the game.", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Seed", "Seed", "Random seed. Use 0 for a different random sequence each reset.", GH_ParamAccess.item, 0);
            pManager.AddBooleanParameter("Wrap", "Wrap", "Wrap around board edges instead of dying at the wall.", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("Snake", "Snake", "Snake body points from tail to head.", GH_ParamAccess.list);
            pManager.AddPointParameter("Food", "Food", "Food point.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Score", "Score", "Current score.", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Alive", "Alive", "True while the snake is alive.", GH_ParamAccess.item);
            pManager.AddTextParameter("Status", "Status", "Current game status.", GH_ParamAccess.item);
            pManager.AddRectangleParameter("Boundary", "Boundary", "Just a boundary", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int width = 20;
            int height = 20;
            int directionValue = -1;
            bool start = false;
            bool reset = false;
            int seed = 0;
            bool wrap = true;

            DA.GetData(0, ref width);
            DA.GetData(1, ref height);
            DA.GetData(2, ref directionValue);
            DA.GetData(3, ref start);
            DA.GetData(4, ref reset);
            DA.GetData(5, ref seed);
            DA.GetData(6, ref wrap);

            width = Math.Max(4, width);
            height = Math.Max(4, height);

            var direction = ToDirection(directionValue);
            bool directionChanged = direction != _lastDirection;

            bool settingsChanged = width != _lastWidth || height != _lastHeight || seed != _lastSeed || wrap != _lastWrap;
            if (_game == null || reset || settingsChanged)
            {
                _game = new SnakeGame(width, height, seed, wrap);
                _lastWidth = width;
                _lastHeight = height;
                _lastSeed = seed;
                _lastWrap = wrap;
            }

            _game.SetDirection(direction);

            if (start)
            {
                if (_scheduledFrame && !directionChanged)
                {
                    _game.Step();
                    _scheduledFrame = false;
                }
                else if (directionChanged)
                {
                    bool clockTickWasDue = _scheduledFrame;
                    _scheduledFrame = false;

                    if (clockTickWasDue)
                        ScheduleNextFrame(1);
                }

                if (!_game.GameOver)
                    ScheduleNextFrame();
            }
            else
            {
                _scheduledFrame = false;
            }

            _lastDirection = direction;

            DA.SetDataList(0, _game.SnakePoints());
            DA.SetData(1, _game.FoodPoint());
            DA.SetData(2, _game.Score);
            DA.SetData(3, !_game.GameOver);
            DA.SetData(4, _game.Status);
            DA.SetData(5, _game.GetBoundary());
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("4e93f382-4e4b-4d87-8f1e-711682882e69");

        private static Direction ToDirection(int value)
        {
            switch (value)
            {
                case -1:
                    return Direction.None;
                case 0:
                    return Direction.Up;
                case 1:
                    return Direction.Down;
                case 2:
                    return Direction.Left;
                case 3:
                    return Direction.Right;
                default:
                    return Direction.None;
            }
        }

        private void ScheduleNextFrame(int delay = 500)
        {
            if (_scheduled)
                return;

            var doc = OnPingDocument();
            if (doc == null)
                return;

            _scheduled = true;

            doc.ScheduleSolution(delay, d =>
            {
                _scheduled = false;
                _scheduledFrame = true;
                ExpireSolution(false);
            });
        }
    }
}
