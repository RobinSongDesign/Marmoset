namespace Marmoset.Games.Tetris
{
    using Marmoset.Games.Common;
    using System.Collections.Generic;

    public class TetrisBoard
    {
        private int[,] _board;

        public int Height { get; private set; }

        public int Width { get; private set; }

        public TetrisBoard(int width, int height)
        {
            Width = width;
            Height = height;
            _board = new int[width, height];
        }

        public bool IsCellEmpty(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
                return false;

            return _board[x, y] == 0;
        }

        public bool CanPlace(TetrisPiece piece)
        {
            foreach (var cell in piece.Cells())
            {
                if (!IsCellEmpty(cell.X, cell.Y))
                    return false;
            }

            return true;
        }

        public void PlacePiece(TetrisPiece piece)
        {
            foreach (var cell in piece.Cells())
            {
                if (cell.X >= 0 && cell.X < Width && cell.Y >= 0 && cell.Y < Height)
                    _board[cell.X, cell.Y] = 1;
            }
        }

        public int ClearLines()
        {
            int cleared = 0;

            for (int y = Height - 1; y >= 0; y--)
            {
                if (!IsLineFull(y))
                    continue;

                ClearLine(y);
                cleared++;
                y++;
            }

            return cleared;
        }

        public List<GridPoint> LockedCells()
        {
            var cells = new List<GridPoint>();

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    if (_board[x, y] != 0)
                        cells.Add(new GridPoint(x, y));
                }
            }

            return cells;
        }

        private bool IsLineFull(int y)
        {
            for (int x = 0; x < Width; x++)
            {
                if (_board[x, y] == 0)
                    return false;
            }

            return true;
        }

        private void ClearLine(int lineY)
        {
            for (int y = lineY; y > 0; y--)
            {
                for (int x = 0; x < Width; x++)
                {
                    _board[x, y] = _board[x, y - 1];
                }
            }

            for (int x = 0; x < Width; x++)
            {
                _board[x, 0] = 0;
            }
        }
    }
}
