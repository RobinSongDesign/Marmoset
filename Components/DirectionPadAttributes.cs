using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using System.Drawing;
using System.Windows.Forms;

namespace Marmoset.Components
{
    internal class DirectionPadAttributes : GH_ComponentAttributes
    {
        private RectangleF _center;
        private RectangleF _down;
        private RectangleF _left;
        private RectangleF _right;
        private RectangleF _up;

        public DirectionPadAttributes(DirectionPadComponent owner)
            : base(owner)
        {
        }

        private DirectionPadComponent PadOwner => (DirectionPadComponent)Owner;

        protected override void Layout()
        {
            base.Layout();

            const float buttonSize = 24f;
            const float gap = 4f;
            const float padHeight = buttonSize * 3f + gap * 2f;
            const float topPadding = 8f;

            var bounds = Bounds;
            bounds.Height += padHeight + topPadding + 6f;
            bounds.Width = System.Math.Max(bounds.Width, 100f);
            Bounds = bounds;

            float startX = bounds.Left + (bounds.Width - buttonSize * 3f - gap * 2f) * 0.5f;
            float startY = bounds.Bottom - padHeight - 6f;

            _up = new RectangleF(startX + buttonSize + gap, startY, buttonSize, buttonSize);
            _left = new RectangleF(startX, startY + buttonSize + gap, buttonSize, buttonSize);
            _center = new RectangleF(startX + buttonSize + gap, startY + buttonSize + gap, buttonSize, buttonSize);
            _right = new RectangleF(startX + (buttonSize + gap) * 2f, startY + buttonSize + gap, buttonSize, buttonSize);
            _down = new RectangleF(startX + buttonSize + gap, startY + (buttonSize + gap) * 2f, buttonSize, buttonSize);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            if (channel != GH_CanvasChannel.Objects)
                return;

            DrawButton(graphics, _up, "↑", 0);
            DrawButton(graphics, _down, "↓", 1);
            DrawButton(graphics, _left, "←", 2);
            DrawButton(graphics, _right, "→", 3);
            DrawButton(graphics, _center, "○", 4);
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button != System.Windows.Forms.MouseButtons.Left)
                return base.RespondToMouseDown(sender, e);

            if (_up.Contains(e.CanvasLocation))
                return SetDirection(0);

            if (_down.Contains(e.CanvasLocation))
                return SetDirection(1);

            if (_left.Contains(e.CanvasLocation))
                return SetDirection(2);

            if (_right.Contains(e.CanvasLocation))
                return SetDirection(3);

            if (_center.Contains(e.CanvasLocation))
                return SetDirection(4);

            return base.RespondToMouseDown(sender, e);
        }

        private void DrawButton(Graphics graphics, RectangleF bounds, string label, int value)
        {
            bool selected = PadOwner.Direction == value;

            var palette = selected
                ? GH_Palette.Black
                : GH_Palette.Normal;

            using (var capsule = GH_Capsule.CreateTextCapsule(
                 bounds,
                 bounds,
                 palette,
                 label,
                 2,
                 0))
            {
                capsule.Render(graphics, Selected, Owner.Locked, false);
            }
         }

        private GH_ObjectResponse SetDirection(int direction)
        {
            PadOwner.SetDirection(direction);
            return GH_ObjectResponse.Handled;
        }
    }
}
