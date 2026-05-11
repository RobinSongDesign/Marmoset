using System;
using System.Collections.Generic;
using Marmoset.Games.Common;
using Rhino.Geometry;

namespace Marmoset.Games.Tetris
{
    public class TetrisGame
    {
        private TetrisBoard _board;
        private TetrisPiece _currentPiece;
        private Random _random;
        private bool _started = false;

        
        public TetrisGame(int width, int height, int seed)
        {
            Start(width, height, seed);
        }

        public int Score { get; private set; }

        public bool GameOver { get; private set; }

        public string Status
        {
            get
            {
                if (GameOver)
                    return "Game Over";

                return _started ? "Running" : "Ready";
            }
        }

        public void Start(int width, int height, int seed)
        {
            _started = true;
            GameOver = false;
            Score = 0;
            _random = seed == 0 ? new Random() : new Random(seed);
            _board = new TetrisBoard(width, height);
            SpawnNewPiece();
        }

        public void Step()
        {
            if (!_started || GameOver)
                return;

            if (!TryMove(0, -1))
                LockCurrentPiece();
        }

        public void ApplyAction(TetrisAction action)
        {
            if (!_started || GameOver)
                return;

            switch (action)
            {
                case TetrisAction.RotateClockwise:
                    TryRotate();
                    break;
                case TetrisAction.SoftDrop:
                    if (!TryMove(0, -1))
                        LockCurrentPiece();
                    break;
                case TetrisAction.MoveLeft:
                    TryMove(-1, 0);
                    break;
                case TetrisAction.MoveRight:
                    TryMove(1, 0);
                    break;
                case TetrisAction.HardDrop:
                    while (TryMove(0, 1))
                    {
                    }
                    LockCurrentPiece();
                    break;
            }
        }

        public List<GridPoint> ActiveCells()
        {
            return _currentPiece == null ? new List<GridPoint>() : _currentPiece.Cells();
        }

        public List<GridPoint> LockedCells()
        {
            return _board.LockedCells();
        }

        public Rectangle3d GetBoundary()
        {
            return new Rectangle3d(Plane.WorldXY, _board.Width, _board.Height);
        }

        private void SpawnNewPiece()
        {
            var pieceType = (TetrominoType)_random.Next(0, 7);
            _currentPiece = new TetrisPiece(pieceType, new GridPoint(_board.Width / 2, _board.Height - 1));

            if (!_board.CanPlace(_currentPiece))
                GameOver = true;
        }

        private bool TryMove(int dx, int dy)
        {
            var moved = _currentPiece.Move(dx, dy);
            if (!_board.CanPlace(moved))
                return false;

            _currentPiece = moved;
            return true;
        }

        private bool TryRotate()
        {
            var rotated = _currentPiece.RotateClockwise();
            if (!_board.CanPlace(rotated))
                return false;

            _currentPiece = rotated;
            return true;
        }

        private void LockCurrentPiece()
        {
            _board.PlacePiece(_currentPiece);
            Score += _board.ClearLines();
            SpawnNewPiece();
        }
    }
}
