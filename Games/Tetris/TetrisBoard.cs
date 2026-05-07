namespace Marmoset.Games.Tetris
{
    public class TetrisBoard
    {
        private int[,] _board;
        private int _height;
        private int _width;

        public TetrisBoard(int width, int height)
        {
            _width = width;
            _height = height;
            _board = new int[width, height];
        }

        public bool IsCellEmpty(int x, int y)
        {
            return _board[x, y] == 0;
        }

        public void PlacePiece(TetrisPiece piece, int x, int y)
        {
            // Logic to place the piece on the board
        }

        public void ClearLines()
        {
            // Logic to clear completed lines
        }
    }
}