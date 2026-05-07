using System;
using System.Collections.Generic;
using System.Diagnostics;
using Eto.Forms;
using Marmoset.Games.Common;

namespace Marmoset.Games.Tetris
{
    public class TetrisPiece
    {
        private TetrominoType Type { get; }
        public GridPoint Position {get; private set;}
        public int Rotation { get; private set; }

        public List<GridPoint> Cells()
        {
            var cells = new List<GridPoint>();
            var offsets = GetOffsetsForType(Type);
            for(int i = 0; i < offsets.Length; i++)
            {
                var offset = offsets[i];
                for(int r = 0; r < Rotation; r++)
                {
                    offset = RotateClockwise(offset);
                }
                cells.Add(new GridPoint(Position.X + offset.X, Position.Y + offset.Y));
            }
            return cells;
        }

        public TetrisPiece(TetrominoType type, GridPoint midpoint)
        {
            Type = type;
            Position = midpoint;
            Rotation = 0;
        }

        private GridPoint[] GetOffsetsForType(TetrominoType type)
        {
            switch (type)
            {
                case TetrominoType.I:
                    return new GridPoint[] { 
                        new GridPoint(-1, 0), new GridPoint(0, 0), new GridPoint(1, 0), new GridPoint(2, 0) };
                case TetrominoType.O:
                    return new GridPoint[] {
                         new GridPoint(0, 0), new GridPoint(1, 0), new GridPoint(0, 1), new GridPoint(1, 1) };
                case TetrominoType.T:
                    return new GridPoint[] { 
                        new GridPoint(0, -1), new GridPoint(-1, 0), new GridPoint(0, 0), new GridPoint(1, 0) };
                case TetrominoType.S:
                    return new GridPoint[] { 
                        new GridPoint(-1, 0), new GridPoint(0, 0), new GridPoint(0, 1), new GridPoint(1, 1) };
                case TetrominoType.Z:
                    return new GridPoint[] { 
                        new GridPoint(0, -1), new GridPoint(1, -1), new GridPoint(-1, 0), new GridPoint(0, 0) };
                case TetrominoType.J:
                    return new GridPoint[] { 
                        new GridPoint(-1, -1), new GridPoint(-1, 0), new GridPoint(0, 0), new GridPoint(1, 0) };
                case TetrominoType.L:
                    return new GridPoint[] { 
                        new GridPoint(-1, 0), new GridPoint(0, 0), new GridPoint(1, 0), new GridPoint(1, -1) };
                default:
                    return new GridPoint[0];
            }
        }

        private GridPoint RotateClockwise(GridPoint p)
        {
            if (Type == TetrominoType.O)
                return p;
            return new GridPoint(p.Y, -p.X);
        }
    }
}