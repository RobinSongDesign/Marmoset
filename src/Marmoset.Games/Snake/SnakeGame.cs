using Rhino.Geometry;
using System.Collections.Generic;
using Marmoset.Games.Common;


namespace Marmoset.Games.Snake
{
    public class SnakeGame
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

        /// <summary>蛇头当前格子。（RL 封装新增，GH 组件不依赖）</summary>
        public GridPoint HeadPosition => _snake.Head;

        /// <summary>蛇当前实际朝向（非 pending 输入）。（RL 封装新增）</summary>
        public Direction CurrentDirection => _snake.Direction;

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

            Advance();
        }

        /// <summary>
        /// RL 驱动入口（新增）：直接设定朝向并立即推进一步，绕过 pendingDirection /
        /// 未 started 时 Step 无效的键盘输入语义。180° 回头输入仍被忽略（保持原样朝向前进）。
        /// </summary>
        public void StepIn(Direction direction)
        {
            if (GameOver)
                return;

            if (direction != Direction.None)
                _snake.SetDirection(direction);

            _pendingDirection = Direction.None;
            _started = true;
            Advance();
        }

        /// <summary>
        /// 预判（新增）：若下一步朝 direction 移动一格，是否会死（撞墙或撞身体）。
        /// 与 Advance 的碰撞判定完全一致；不改变任何状态。GameOver 时恒返回 true。
        /// </summary>
        public bool WouldDie(Direction direction)
        {
            if (GameOver)
                return true;

            var head = _snake.Head;
            GridPoint next;
            switch (direction)
            {
                case Direction.Up: next = new GridPoint(head.X, head.Y + 1); break;
                case Direction.Down: next = new GridPoint(head.X, head.Y - 1); break;
                case Direction.Left: next = new GridPoint(head.X - 1, head.Y); break;
                case Direction.Right: next = new GridPoint(head.X + 1, head.Y); break;
                default: return true;
            }

            if (_wrap)
                next = Wrap(next);
            else if (IsOutside(next))
                return true;

            bool grow = next == Food;
            return _snake.Contains(next, grow);
        }

        private void Advance()
        {
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
