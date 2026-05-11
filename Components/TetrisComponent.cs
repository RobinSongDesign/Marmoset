using Grasshopper.Kernel;
using Marmoset.Games.Common;
using Marmoset.Games.Tetris;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace Marmoset.Components
{
    public class TetrisComponent : GH_Component
    {
        private TetrisGame _game;
        private int _lastActionValue = -1;
        private int _lastHeight = -1;
        private int _lastSeed = -1;
        private int _lastWidth = -1;
        private bool _scheduled;
        private bool _scheduledFrame;

        public TetrisComponent()
          : base("Tetris", "Tetris",
            "A simple grid-based Tetris game state component.",
            "Marmoset", "Games")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("Width", "W", "Board width.", GH_ParamAccess.item, 10);
            pManager.AddIntegerParameter("Height", "H", "Board height.", GH_ParamAccess.item, 20);
            pManager.AddIntegerParameter("Action", "A", "Action: 0 Rotate, 1 SoftDrop, 2 Left, 3 Right, 4 None.", GH_ParamAccess.item, 4);
            pManager.AddBooleanParameter("Start", "S", "Start the game.", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Reset", "R", "Reset the game.", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Seed", "Seed", "Random seed. Use 0 for a different random sequence each reset.", GH_ParamAccess.item, 0);
            pManager.AddIntegerParameter("Interval", "I", "Gravity interval in milliseconds.", GH_ParamAccess.item, 500);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("Active", "Active", "Current falling piece cells.", GH_ParamAccess.list);
            pManager.AddPointParameter("Locked", "Locked", "Locked board cells.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Score", "Score", "Current score.", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Alive", "Alive", "True while the game is not over.", GH_ParamAccess.item);
            pManager.AddTextParameter("Status", "Status", "Current game status.", GH_ParamAccess.item);
            pManager.AddRectangleParameter("Boundary", "Boundary", "Board boundary.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int width = 10;
            int height = 20;
            int actionValue = -1;
            bool start = false;
            bool reset = false;
            int seed = 0;
            int interval = 500;

            DA.GetData(0, ref width);
            DA.GetData(1, ref height);
            DA.GetData(2, ref actionValue);
            DA.GetData(3, ref start);
            DA.GetData(4, ref reset);
            DA.GetData(5, ref seed);
            DA.GetData(6, ref interval);

            width = Math.Max(4, width);
            height = Math.Max(4, height);
            interval = Math.Max(1, interval);

            bool settingsChanged = width != _lastWidth || height != _lastHeight || seed != _lastSeed;
            if (_game == null || reset || settingsChanged)
            {
                _game = new TetrisGame(width, height, seed);
                _lastWidth = width;
                _lastHeight = height;
                _lastSeed = seed;
                _lastActionValue = -1;
                _scheduledFrame = false;
            }

            if (actionValue != _lastActionValue)
            {
                _game.ApplyAction(ToAction(actionValue));
                _lastActionValue = actionValue;
            }

            if (start)
            {
                if (_scheduledFrame)
                {
                    _game.Step();
                    _scheduledFrame = false;
                }

                if (!_game.GameOver && actionValue != -1)
                    ScheduleNextFrame(interval);
            }
            else
            {
                _scheduledFrame = false;
            }

            DA.SetDataList(0, ToPointList(_game.ActiveCells()));
            DA.SetDataList(1, ToPointList(_game.LockedCells()));
            DA.SetData(2, _game.Score);
            DA.SetData(3, !_game.GameOver);
            DA.SetData(4, _game.Status);
            DA.SetData(5, _game.GetBoundary());
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("af6d4b46-6ae0-41cc-ae25-866a3990d2c9");

        private static TetrisAction ToAction(int value)
        {
            switch (value)
            {
                case 0:
                    return TetrisAction.RotateClockwise;
                case 1:
                    return TetrisAction.SoftDrop;
                case 2:
                    return TetrisAction.MoveLeft;
                case 3:
                    return TetrisAction.MoveRight;
                case 4:
                    return TetrisAction.HardDrop;
                default:
                    return TetrisAction.None;
            }
        }

        private static List<Point2d> ToPointList(IReadOnlyList<GridPoint> cells)
        {
            var points = new List<Point2d>(cells.Count);
            for (int i = 0; i < cells.Count; i++)
            {
                points.Add(new Point2d(cells[i].X, cells[i].Y));
            }

            return points;
        }

        private void ScheduleNextFrame(int delay)
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
