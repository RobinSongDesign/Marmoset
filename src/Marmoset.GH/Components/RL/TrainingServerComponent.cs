using System;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Marmoset.Core;
using Rhino;

namespace Marmoset.Components.RL
{
    /// <summary>
    /// Hosts the TCP training server. The Grasshopper solution only assembles the pieces
    /// (Agent -> TrainingSession -> TrainingServer); the training loop itself is driven by
    /// the Core background thread and never passes through the GH solver. The periodic
    /// ScheduleSolution below merely refreshes the numeric stats outputs.
    /// </summary>
    public class TrainingServerComponent : GH_Component
    {
        private TrainingSession _session;
        private TrainingServer _server;
        private Agent _currentAgent;
        private int _currentPort = -1;
        private bool _statsScheduled;
        private string _lastStartError;

        public TrainingServerComponent()
          : base("Training Server", "TrainServer",
            "Starts a TCP training server that lets a Python client drive the agent (gym semantics).",
            "Marmoset", "RL")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Agent", "A", "Marmoset agent instance (e.g. from Snake Env).", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Port", "P", "TCP port to listen on.", GH_ParamAccess.item, TrainingServer.DefaultPort);
            pManager.AddBooleanParameter("Run", "R", "Start/stop the training server.", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Session", "S", "TrainingSession. Wire into a viewer component for live visualization.", GH_ParamAccess.item);
            pManager.AddTextParameter("Status", "St", "Server status text.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("TotalSteps", "TS", "Total simulation steps across all episodes.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Episodes", "E", "Completed episode count.", GH_ParamAccess.item);
            pManager.AddNumberParameter("EpisodeReward", "ER", "Cumulative reward of the current episode.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            object agentRaw = null;
            int port = TrainingServer.DefaultPort;
            bool run = false;

            DA.GetData(0, ref agentRaw);
            DA.GetData(1, ref port);
            DA.GetData(2, ref run);

            var agent = RLComponentUtil.Unwrap<Agent>(agentRaw);

            if (run)
            {
                if (agent == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        "Agent input is missing or is not a Marmoset.Core.Agent.");
                    StopServer();
                }
                else if (port < 1 || port > 65535)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Port must be in [1, 65535].");
                    StopServer();
                }
                else
                {
                    bool restart = _server == null
                        || !ReferenceEquals(agent, _currentAgent)
                        || port != _currentPort;
                    if (restart)
                    {
                        StopServer();
                        StartServer(agent, port);
                    }
                }
            }
            else
            {
                StopServer();
            }

            if (!string.IsNullOrEmpty(_lastStartError))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, _lastStartError);

            long totalSteps = 0;
            int episodes = 0;
            double episodeReward = 0.0;
            if (_session != null)
            {
                lock (_session.SyncRoot)
                {
                    totalSteps = _session.TotalSteps;
                    episodes = _session.EpisodeCount;
                    episodeReward = _session.CurrentEpisodeReward;
                }
            }

            if (_session != null)
                DA.SetData(0, new GH_ObjectWrapper(_session));
            DA.SetData(1, SafeStatusText());
            DA.SetData(2, (int)Math.Min(int.MaxValue, totalSteps));
            DA.SetData(3, episodes);
            DA.SetData(4, episodeReward);

            // While the server is running, keep the stats outputs fresh at ~2 Hz.
            // This does NOT drive training; it only re-solves this component.
            if (SafeIsRunning())
                ScheduleStatsRefresh();
        }

        private void StartServer(Agent agent, int port)
        {
            _session = new TrainingSession(agent);
            _server = new TrainingServer(_session, port);
            _server.StatusChanged += OnServerStatusChanged;
            _currentAgent = agent;
            _currentPort = port;
            _lastStartError = null;
            try
            {
                _server.Start();
            }
            catch (NotImplementedException)
            {
                _lastStartError = "TrainingServer core is not implemented yet (stub).";
            }
            catch (Exception ex)
            {
                _lastStartError = "Failed to start server: " + ex.Message;
            }
        }

        private void StopServer()
        {
            if (_server == null)
                return;

            _server.StatusChanged -= OnServerStatusChanged;
            try { _server.Dispose(); }
            catch (NotImplementedException) { }
            catch (Exception) { }
            _server = null;
            _currentAgent = null;
            _currentPort = -1;
            _lastStartError = null;
            // Keep _session so downstream viewers can still show the last state.
        }

        /// <summary>Raised by the server, possibly from a background thread.</summary>
        private void OnServerStatusChanged()
        {
            RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                var doc = OnPingDocument();
                doc?.ScheduleSolution(1, d => ExpireSolution(false));
            }));
        }

        private void ScheduleStatsRefresh()
        {
            if (_statsScheduled)
                return;

            var doc = OnPingDocument();
            if (doc == null)
                return;

            _statsScheduled = true;
            doc.ScheduleSolution(500, d =>
            {
                _statsScheduled = false;
                ExpireSolution(false);
            });
        }

        private bool SafeIsRunning()
        {
            if (_server == null)
                return false;
            try { return _server.IsRunning; }
            catch (Exception) { return false; }
        }

        private string SafeStatusText()
        {
            if (_server == null)
                return "Stopped";
            try { return _server.StatusText; }
            catch (NotImplementedException) { return _lastStartError ?? "Server core not implemented (stub)."; }
            catch (Exception ex) { return ex.Message; }
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            StopServer();
            base.RemovedFromDocument(document);
        }

        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            if (context == GH_DocumentContext.Close || context == GH_DocumentContext.Unloaded)
                StopServer();
            base.DocumentContextChanged(document, context);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("325357ac-78d4-4636-b3bd-6119b44d813e");
    }
}
