using System;
using System.Threading;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Marmoset.Core;

namespace Marmoset.Components.RL
{
    /// <summary>
    /// Self-play demo: loads an ONNX policy exported from training and lets it drive the
    /// agent on a background thread (Reset -> Predict -> Step), throttled to StepsPerSecond
    /// and auto-resetting between episodes. No Python required. The GH solver only
    /// assembles and starts/stops the loop; wire the Session output into a viewer to watch.
    /// </summary>
    public class PolicyRunnerComponent : GH_Component
    {
        private TrainingSession _session;
        private Agent _currentAgent;
        private string _currentModelPath;
        private Thread _worker;
        private CancellationTokenSource _cts;
        private volatile int _stepsPerSecond = 10;
        private volatile string _statusText = "Stopped";
        private bool _refreshScheduled;

        public PolicyRunnerComponent()
          : base("Policy Runner", "PolicyRun",
            "Runs a trained ONNX policy against an agent on a background thread (AI self-play demo).",
            "Marmoset", "RL")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Agent", "A", "Marmoset agent instance (e.g. from Snake Env).", GH_ParamAccess.item);
            pManager.AddTextParameter("ModelPath", "M", "Path to the ONNX policy model.", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Run", "R", "Start/stop the self-play loop.", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("StepsPerSecond", "SPS", "Simulation steps per second (throttle).", GH_ParamAccess.item, 10);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Session", "S", "TrainingSession. Wire into a viewer component for live visualization.", GH_ParamAccess.item);
            pManager.AddTextParameter("Status", "St", "Runner status text.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            object agentRaw = null;
            string modelPath = null;
            bool run = false;
            int stepsPerSecond = 10;

            DA.GetData(0, ref agentRaw);
            DA.GetData(1, ref modelPath);
            DA.GetData(2, ref run);
            DA.GetData(3, ref stepsPerSecond);

            _stepsPerSecond = Math.Max(1, stepsPerSecond); // picked up live by the worker, no restart needed

            var agent = RLComponentUtil.Unwrap<Agent>(agentRaw);

            if (run)
            {
                if (agent == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        "Agent input is missing or is not a Marmoset.Core.Agent.");
                    StopRunner();
                }
                else if (string.IsNullOrWhiteSpace(modelPath))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "ModelPath is empty.");
                    StopRunner();
                }
                else
                {
                    bool restart = _worker == null
                        || !_worker.IsAlive
                        || !ReferenceEquals(agent, _currentAgent)
                        || !string.Equals(modelPath, _currentModelPath, StringComparison.Ordinal);
                    if (restart)
                    {
                        StopRunner();
                        StartRunner(agent, modelPath);
                    }
                }
            }
            else
            {
                StopRunner();
            }

            if (_session != null)
                DA.SetData(0, new GH_ObjectWrapper(_session));
            DA.SetData(1, _statusText);

            // Refresh the status output at ~2 Hz while the loop runs (does not drive stepping).
            if (_worker != null && _worker.IsAlive)
                ScheduleRefresh();
        }

        private void StartRunner(Agent agent, string modelPath)
        {
            _session = new TrainingSession(agent);
            _currentAgent = agent;
            _currentModelPath = modelPath;
            _cts = new CancellationTokenSource();
            _statusText = "Starting";

            var session = _session;
            var token = _cts.Token;
            _worker = new Thread(() => RunLoop(session, modelPath, token))
            {
                IsBackground = true,
                Name = "Marmoset.PolicyRunner",
            };
            _worker.Start();
        }

        private void StopRunner()
        {
            if (_worker == null)
                return;

            try { _cts?.Cancel(); }
            catch (ObjectDisposedException) { }

            if (_worker.IsAlive)
                _worker.Join(2000); // background thread; if it overruns it dies with the process

            _cts?.Dispose();
            _cts = null;
            _worker = null;
            _currentAgent = null;
            _currentModelPath = null;
            _statusText = "Stopped";
            // Keep _session so downstream viewers can still show the last state.
        }

        /// <summary>Background worker. Owns the OnnxPolicy for its whole lifetime.</summary>
        private void RunLoop(TrainingSession session, string modelPath, CancellationToken token)
        {
            OnnxPolicy policy = null;
            try
            {
                _statusText = "Loading model";
                policy = new OnnxPolicy(modelPath);

                var observation = session.Reset();
                _statusText = "Running";

                while (!token.IsCancellationRequested)
                {
                    var actions = policy.Predict(observation);
                    var result = session.Step(actions);
                    observation = result.Observation;

                    if (result.Terminated || result.Truncated)
                        observation = session.Reset(); // auto-continue to the next episode

                    int delayMs = Math.Max(1, 1000 / Math.Max(1, _stepsPerSecond));
                    if (token.WaitHandle.WaitOne(delayMs))
                        break;
                }

                _statusText = "Stopped";
            }
            catch (OperationCanceledException)
            {
                _statusText = "Stopped";
            }
            catch (NotImplementedException ex)
            {
                _statusText = "Core stub not implemented yet: " + ex.Message;
            }
            catch (Exception ex)
            {
                _statusText = "Error: " + ex.Message;
            }
            finally
            {
                policy?.Dispose();
            }
        }

        private void ScheduleRefresh()
        {
            if (_refreshScheduled)
                return;

            var doc = OnPingDocument();
            if (doc == null)
                return;

            _refreshScheduled = true;
            doc.ScheduleSolution(500, d =>
            {
                _refreshScheduled = false;
                ExpireSolution(false);
            });
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            StopRunner();
            base.RemovedFromDocument(document);
        }

        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            if (context == GH_DocumentContext.Close || context == GH_DocumentContext.Unloaded)
                StopRunner();
            base.DocumentContextChanged(document, context);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("b7227496-225f-4f3e-98a3-ebe1fa41f8e2");
    }
}
