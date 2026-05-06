using Grasshopper;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Special;
using System.ComponentModel;
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
            const float padHeight = buttonSize * 3f + gap * 3f;

            var bounds = Bounds;
            bounds.Height += padHeight;
            bounds.Width = System.Math.Max(bounds.Width, 100f);
            Bounds = bounds;

            var pivot = new PointF(
                Bounds.Right,
                Bounds.Top + Bounds.Height * 0.5f
            );

            Owner.Params.Output[0].Attributes.Pivot = pivot;
            Owner.Params.Output[0].Attributes.Bounds = new RectangleF(
                pivot.X - 12f,
                pivot.Y - 12f,
                12f,
                12f
            );

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
            if (channel != GH_CanvasChannel.Objects)
            {
                base.Render(canvas, graphics, channel);
                return;
            }

            var palette = GH_Palette.Normal;

            using (var capsule = GH_Capsule.CreateCapsule(Bounds, palette))
            {
                DrawOutputGrip(graphics);
                capsule.Render(graphics, Selected, Owner.Locked, false);
            }

            DrawButton(graphics, _up, "↑", 0);
            DrawButton(graphics, _down, "↓", 1);
            DrawButton(graphics, _left, "←", 2);
            DrawButton(graphics, _right, "→", 3);
            DrawButton(graphics, _center, "○", 4);
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button != MouseButtons.Left)
                return base.RespondToMouseDown(sender, e);

            int direction;
            if (!TryHitButton(e.CanvasLocation, out direction))
                return base.RespondToMouseDown(sender, e);
            
            PadOwner.SetPressedDirection(direction);
            PadOwner.SetDirection(direction);
            return GH_ObjectResponse.Capture;
        }

        public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (PadOwner.PressedDirection == -1)
                return base.RespondToMouseUp(sender, e);

            PadOwner.SetPressedDirection(-1);
            PadOwner.SetDirection(4);
            return GH_ObjectResponse.Release;
        }

        private void DrawButton(Graphics graphics, RectangleF bounds, string label, int value)
        {
            
            bool pressed = PadOwner.PressedDirection == value;

            var palette = pressed
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

        private void DrawOutputGrip(Graphics graphics)
        {
            if (Owner.Params.Output.Count == 0)
                return;

            var pivot = Owner.Params.Output[0].Attributes.Pivot;

            var bounds = new RectangleF(
                pivot.X - 4f,
                pivot.Y - 10f,
                8f,
                8f
            );

            using (var brush = new SolidBrush(Color.White))
            using (var pen = new Pen(Color.Black))
            {
                graphics.FillEllipse(brush, bounds);
                graphics.DrawEllipse(pen, bounds);
            }
        }

        private bool TryHitButton(PointF point, out int direction)
        {
            if (_up.Contains(point))
            {
                direction = 0;
                return true;
            }

            if (_down.Contains(point))
            {
                direction = 1;
                return true;
            }

            if (_left.Contains(point))
            {
                direction = 2;
                return true;
            }

            if (_right.Contains(point))
            {
                direction = 3;
                return true;
            }

            if (_center.Contains(point))
            {
                direction = 4;
                return true;
            }

            direction = -1;
            return false;
        }

        private GH_ObjectResponse SetDirection(int direction)
        {
            PadOwner.SetDirection(direction);
            return GH_ObjectResponse.Handled;
        }
    }
}
