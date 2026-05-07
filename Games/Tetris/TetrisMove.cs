namespace Games.Tetris
{
    public class TetrisMove
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Rotation { get; set; }

        public TetrisMove(int x, int y, int rotation)
        {
            X = x;
            Y = y;
            Rotation = rotation;
        }
    }
}