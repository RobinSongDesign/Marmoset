using System;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Marmoset.Games.Snake;

namespace Marmoset.Components.RL
{
    /// <summary>
    /// Assembles a SnakeAgent RL environment instance. The instance is cached and only
    /// recreated when the construction parameters change, so downstream components
    /// (Training Server / Policy Runner) keep working with a stable object identity.
    /// </summary>
    public class SnakeEnvComponent : GH_Component
    {
        private SnakeAgent _agent;
        private int _lastWidth = -1;
        private int _lastHeight = -1;
        private bool _lastWrap;

        public SnakeEnvComponent()
          : base("Snake Env", "SnakeEnv",
            "Creates a Snake reinforcement-learning environment (SnakeAgent).",
            "Marmoset", "RL")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("Width", "W", "Board width (min 4).", GH_ParamAccess.item, 12);
            pManager.AddIntegerParameter("Height", "H", "Board height (min 4).", GH_ParamAccess.item, 12);
            pManager.AddBooleanParameter("Wrap", "Wrap", "Wrap around board edges instead of dying at the wall.", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Agent", "A", "SnakeAgent instance. Wire into a Training Server or Policy Runner component.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int width = 12;
            int height = 12;
            bool wrap = false;

            DA.GetData(0, ref width);
            DA.GetData(1, ref height);
            DA.GetData(2, ref wrap);

            width = Math.Max(4, width);
            height = Math.Max(4, height);

            if (_agent == null || width != _lastWidth || height != _lastHeight || wrap != _lastWrap)
            {
                _agent = new SnakeAgent(width, height, wrap);
                _lastWidth = width;
                _lastHeight = height;
                _lastWrap = wrap;
            }

            DA.SetData(0, new GH_ObjectWrapper(_agent));
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("e79f8173-1e70-4d8c-8538-57253421865d");
    }
}
