using Rhino.Geometry;
using System.Collections.Generic;
using Marmoset.Games.Common;

namespace Marmoset.Games.Snake
{
    internal class SnakeBody
    {
        private readonly List<GridPoint> _segments = new List<GridPoint>();

        public Direction Direction { get; set; }

        public GridPoint Head => _segments[_segments.Count - 1];

        public IReadOnlyList<GridPoint> Segments => _segments;

        public SnakeBody(GridPoint head, int length, Direction direction)
        {
            Direction = direction;

            for (int i = length - 1; i >= 0; i--)
            {
                _segments.Add(new GridPoint(head.X - i, head.Y));
            }
        }

        public void SetDirection(Direction direction)
        {
            if (IsOpposite(Direction, direction))
                return;

            Direction = direction;
        }

        public GridPoint GetNextHead()
        {
            var head = Head;
            switch (Direction)
            {
                case Direction.Up:
                    return new GridPoint(head.X, head.Y + 1);
                case Direction.Down:
                    return new GridPoint(head.X, head.Y - 1);
                case Direction.Left:
                    return new GridPoint(head.X - 1, head.Y);
                case Direction.Right:
                    return new GridPoint(head.X + 1, head.Y);
                default:
                    return head;
            }
        }

        public void MoveTo(GridPoint nextHead, bool grow)
        {
            _segments.Add(nextHead);
            if (!grow)
                _segments.RemoveAt(0);
        }

        public bool Contains(GridPoint point, bool includeTail)
        {
            int count = includeTail ? _segments.Count : _segments.Count - 1;
            for (int i = 0; i < count; i++)
            {
                if (_segments[i] == point)
                    return true;
            }

            return false;
        }

        public List<Point2d> ToPoint2dList()
        {
            var points = new List<Point2d>(_segments.Count);
            for (int i = 0; i < _segments.Count; i++)
            {
                points.Add(new Point2d(_segments[i].X, _segments[i].Y));
            }

            return points;
        }

        private static bool IsOpposite(Direction current, Direction next)
        {
            return current == Direction.Up && next == Direction.Down
                || current == Direction.Down && next == Direction.Up
                || current == Direction.Left && next == Direction.Right
                || current == Direction.Right && next == Direction.Left;
        }
    }
}
