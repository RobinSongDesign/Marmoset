using GH_IO.Serialization;
using Grasshopper.Kernel;
using System;

namespace Marmoset.Components
{
    public class DirectionPadComponent : GH_Component
    {
        private int _direction = 4;

        public DirectionPadComponent()
          : base("Direction Pad", "DPad",
            "A five-button direction pad that outputs an integer direction.",
            "Marmoset", "Games")
        {
        }

        public int Direction => _direction;

        public void SetDirection(int direction)
        {
            if (_direction == direction)
                return;

            RecordUndoEvent("Direction");
            _direction = direction;
            ExpireSolution(true);
        }

        public override void CreateAttributes()
        {
            m_attributes = new DirectionPadAttributes(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddIntegerParameter("Direction", "D", "Direction value: 0 Up, 1 Down, 2 Left, 3 Right, 4 Center.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            DA.SetData(0, _direction);
        }

        public override bool Write(GH_IWriter writer)
        {
            writer.SetInt32("Direction", _direction);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            _direction = reader.GetInt32("Direction");
            return base.Read(reader);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("826d35ed-3582-4a1a-9934-8d1c206f312e");
    }
}
