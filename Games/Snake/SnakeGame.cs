using Rhino.Geometry;
using System.Collections.Generic;
using Marmoset.Games.Common;


namespace Marmoset.Games.Snake
{
    internal class SnakeGame
    {
        private FoodSpawner _foodSpawner;
        private SnakeBody _snake;
        private int _height;
        private Direction _pendingDirection = Direction.None;
        private int _width;
        private bool _started;
        private bool _wrap;

        public SnakeGame(int width, int height, int seed, bool wrap)
        {
            Reset(width, height, seed, wrap);
        }

        public int Score { get; private set; }

        public bool GameOver { get; private set; }

        public GridPoint Food { get; private set; }

        public IReadOnlyList<GridPoint> SnakeSegments => _snake.Segments;

        public string Status
        {
            get
            {
                if (GameOver)
                    return "Game Over";

                return _started ? "Running" : "Ready";
            }
        }

        public void Reset(int width, int height, int seed, bool wrap)
        {
            _width = width < 4 ? 4 : width;
            _height = height < 4 ? 4 : height;
            _wrap = wrap;
            _pendingDirection = Direction.None;
            _started = false;
            Score = 0;
            GameOver = false;

            var start = new GridPoint(_width / 2, _height / 2);
            _snake = new SnakeBody(start, 3, Direction.Right);
            _foodSpawner = new FoodSpawner(seed);
            Food = _foodSpawner.GetFoodPoint(_width, _height, _snake.Segments);
        }

        public void SetDirection(Direction direction)
        {
            if (direction == Direction.None)
                return;

            _pendingDirection = direction;
        }

        public void Step()
        {
            if (GameOver)
                return;

            if (!_started)
            {
                if (_pendingDirection == Direction.None)
                    return;

                _snake.SetDirection(_pendingDirection);
                _pendingDirection = Direction.None;
                _started = true;
                return;
            }

            var nextHead = _snake.GetNextHead();

            if (_wrap)
                nextHead = Wrap(nextHead);
            else if (IsOutside(nextHead))
            {
                GameOver = true;
                return;
            }

            bool grow = nextHead == Food;
            if (_snake.Contains(nextHead, grow))
            {
                GameOver = true;
                return;
            }

            _snake.MoveTo(nextHead, grow);

            if (_pendingDirection != Direction.None)
            {
                _snake.SetDirection(_pendingDirection);
                _pendingDirection = Direction.None;
            }

            if (grow)
            {
                Score++;
                Food = _foodSpawner.GetFoodPoint(_width, _height, _snake.Segments);
            }
        }

        public List<Point2d> SnakePoints()
        {
            return _snake.ToPoint2dList();
        }

        public Rectangle3d GetBoundary()
        {
            return new Rectangle3d(Plane.WorldXY, _width, _height);
        }

        public Point2d FoodPoint()
        {
            return new Point2d(Food.X, Food.Y);
        }

        private GridPoint Wrap(GridPoint point)
        {
            return new GridPoint(Mod(point.X, _width), Mod(point.Y, _height));
        }

        private bool IsOutside(GridPoint point)
        {
            return point.X < 0 || point.X >= _width || point.Y < 0 || point.Y >= _height;
        }

        private static int Mod(int value, int divisor)
        {
            return (value % divisor + divisor) % divisor;
        }
    }
}
