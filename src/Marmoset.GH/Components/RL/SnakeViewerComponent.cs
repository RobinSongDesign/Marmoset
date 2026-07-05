using System;
using System.Threading;
using Grasshopper.Kernel;
using Marmoset.Core;
using Marmoset.Games.Snake;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

namespace Marmoset.Components.RL
{
    /// <summary>
    /// Live viewport visualization for a Snake training session, using a Rhino
    /// DisplayConduit. It never creates document objects and never re-triggers the GH
    /// solver: the simulation thread flags a dirty bit on StepCompleted, a ~30 Hz timer
    /// copies a snapshot under session.SyncRoot, and the conduit draws the snapshot at
    /// render frame rate.
    /// </summary>
    public class SnakeViewerComponent : GH_Component
    {
        private const int TimerIntervalMs = 33; // ~30 Hz

        private readonly SnakeConduit _conduit = new SnakeConduit();
        private TrainingSession _session;
        private SnakeAgent _agent;
        private Timer _timer;
        private int _dirty;        // 1 = simulation state changed since last snapshot
        private int _redrawQueued; // 1 = a viewport redraw is already queued on the UI thread

        public SnakeViewerComponent()
          : base("Snake Viewer", "SnakeView",
            "Draws the live state of a Snake training session in the Rhino viewport (display conduit, no document objects).",
            "Marmoset", "RL")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Session", "S", "TrainingSession whose agent is a SnakeAgent (from Training Server or Policy Runner).", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Enabled", "E", "Enable viewport drawing.", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            // Pure viewer: no outputs.
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            object sessionRaw = null;
            bool enabled = true;

            DA.GetData(0, ref sessionRaw);
            DA.GetData(1, ref enabled);

            var session = RLComponentUtil.Unwrap<TrainingSession>(sessionRaw);
            if (session == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Session input is missing or is not a TrainingSession.");
                Deactivate();
                return;
            }

            var agent = session.Agent as SnakeAgent;
            if (agent == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "The session's agent is not a SnakeAgent; this viewer only draws Snake environments.");
                Deactivate();
                return;
            }

            if (!ReferenceEquals(session, _session))
            {
                if (_session != null)
                    _session.StepCompleted -= OnStepCompleted;
                _session = session;
                _agent = agent;
                _session.StepCompleted += OnStepCompleted;
            }

            if (enabled)
            {
                Activate();
                // Draw the current (possibly idle) state right away.
                Interlocked.Exchange(ref _dirty, 1);
            }
            else
            {
                Deactivate();
            }
        }

        private void Activate()
        {
            _conduit.Enabled = true;
            if (_timer == null)
                _timer = new Timer(OnTimerTick, null, TimerIntervalMs, TimerIntervalMs);
        }

        private void Deactivate()
        {
            if (_timer != null)
            {
                _timer.Dispose();
                _timer = null;
            }

            if (_conduit.Enabled)
            {
                _conduit.Enabled = false;
                _conduit.Snapshot = null;
                QueueRedraw(); // erase the last frame from the viewport
            }
        }

        /// <summary>Raised on the simulation thread after every step. Must return fast.</summary>
        private void OnStepCompleted()
        {
            Interlocked.Exchange(ref _dirty, 1);
        }

        /// <summary>Timer thread: copy a snapshot under the session lock, then ask for one redraw.</summary>
        private void OnTimerTick(object state)
        {
            if (Interlocked.Exchange(ref _dirty, 0) == 0)
                return;

            var session = _session;
            var agent = _agent;
            if (session == null || agent == null || !_conduit.Enabled)
                return;

            SnakeSnapshot snapshot;
            lock (session.SyncRoot)
            {
                var game = agent.Game;
                var points = game.SnakePoints();
                var body = new Point3d[points.Count];
                for (int i = 0; i < points.Count; i++)
                    body[i] = new Point3d(points[i].X + 0.5, points[i].Y + 0.5, 0.0);

                var foodPoint = game.FoodPoint();
                var boundary = game.GetBoundary();

                snapshot = new SnakeSnapshot
                {
                    Body = body,
                    Food = new Point3d(foodPoint.X + 0.5, foodPoint.Y + 0.5, 0.0),
                    BoundaryCorners = new[]
                    {
                        boundary.Corner(0), boundary.Corner(1),
                        boundary.Corner(2), boundary.Corner(3),
                        boundary.Corner(0),
                    },
                    Bounds = boundary.BoundingBox,
                    Score = game.Score,
                    Status = game.Status,
                };
            }

            _conduit.Snapshot = snapshot;
            QueueRedraw();
        }

        private void QueueRedraw()
        {
            if (Interlocked.Exchange(ref _redrawQueued, 1) == 1)
                return;

            RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                Interlocked.Exchange(ref _redrawQueued, 0);
                RhinoDoc.ActiveDoc?.Views.Redraw();
            }));
        }

        private void Cleanup()
        {
            Deactivate();
            if (_session != null)
            {
                _session.StepCompleted -= OnStepCompleted;
                _session = null;
                _agent = null;
            }
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            Cleanup();
            base.RemovedFromDocument(document);
        }

        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            if (context == GH_DocumentContext.Close || context == GH_DocumentContext.Unloaded)
                Cleanup();
            base.DocumentContextChanged(document, context);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("2fc7a6fa-ce9f-4ce9-82ef-ec6c72c18092");

        /// <summary>Immutable-by-convention state snapshot handed from the timer thread to the conduit.</summary>
        private sealed class SnakeSnapshot
        {
            public Point3d[] Body;
            public Point3d Food;
            public Point3d[] BoundaryCorners;
            public BoundingBox Bounds;
            public int Score;
            public string Status;
        }

        private sealed class SnakeConduit : DisplayConduit
        {
            private static readonly System.Drawing.Color BoundaryColor = System.Drawing.Color.DimGray;
            private static readonly System.Drawing.Color BodyColor = System.Drawing.Color.ForestGreen;
            private static readonly System.Drawing.Color HeadColor = System.Drawing.Color.LimeGreen;
            private static readonly System.Drawing.Color FoodColor = System.Drawing.Color.OrangeRed;
            private static readonly System.Drawing.Color TextColor = System.Drawing.Color.DarkOrange;

            /// <summary>Latest snapshot; written by the timer thread, read by the render thread.</summary>
            public volatile SnakeSnapshot Snapshot;

            protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
            {
                base.CalculateBoundingBox(e);
                var s = Snapshot;
                if (s != null)
                    e.IncludeBoundingBox(s.Bounds);
            }

            protected override void PostDrawObjects(DrawEventArgs e)
            {
                base.PostDrawObjects(e);
                var s = Snapshot;
                if (s == null)
                    return;

                e.Display.DrawPolyline(s.BoundaryCorners, BoundaryColor, 2);

                for (int i = 0; i < s.Body.Length; i++)
                {
                    bool isHead = i == s.Body.Length - 1; // body is ordered tail -> head
                    DrawCell(e, s.Body[i], isHead ? HeadColor : BodyColor);
                }

                DrawCell(e, s.Food, FoodColor);
            }

            protected override void DrawForeground(DrawEventArgs e)
            {
                base.DrawForeground(e);
                var s = Snapshot;
                if (s == null)
                    return;

                e.Display.Draw2dText(
                    $"Snake  |  Score: {s.Score}  |  {s.Status}",
                    TextColor,
                    new Point2d(20, 40),
                    false,
                    20);
            }

            private static void DrawCell(DrawEventArgs e, Point3d center, System.Drawing.Color color)
            {
                const double half = 0.4;
                var corners = new[]
                {
                    new Point3d(center.X - half, center.Y - half, 0.0),
                    new Point3d(center.X + half, center.Y - half, 0.0),
                    new Point3d(center.X + half, center.Y + half, 0.0),
                    new Point3d(center.X - half, center.Y + half, 0.0),
                };
                e.Display.DrawPolygon(corners, color, true);
            }
        }
    }
}
