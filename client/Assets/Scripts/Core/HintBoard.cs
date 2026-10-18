using System.Collections.Generic;

namespace Gomoku.Core
{
    /// <summary>
    /// Minimal 15x15 board used by the pure C# hint/analysis code. Cells store
    /// the colour that participates in five-in-a-row: normal stone first, then
    /// phantom; -1 = empty. This mirrors game.LineColorAt on the server.
    /// </summary>
    public sealed class HintBoard
    {
        public const int Size = 15;
        public const int Empty = -1;

        readonly int[,] _line = new int[Size, Size];
        readonly bool[,] _placeable = new bool[Size, Size];

        public HintBoard()
        {
            for (int x = 0; x < Size; x++)
            for (int y = 0; y < Size; y++)
            {
                _line[x, y] = Empty;
                _placeable[x, y] = true;
            }
        }

        public static bool InBounds(int x, int y) =>
            x >= 0 && y >= 0 && x < Size && y < Size;

        /// <summary>Line colour at a point: normal stone, then phantom, else empty.</summary>
        public int LineAt(int x, int y) => InBounds(x, y) ? _line[x, y] : Empty;

        /// <summary>Whether a normal stone could be placed here (no stone/phantom/mine).</summary>
        public bool CanPlace(int x, int y) => InBounds(x, y) && _placeable[x, y];

        public void AddStone(int x, int y, int color)
        {
            if (!InBounds(x, y)) return;
            _line[x, y] = color;
            _placeable[x, y] = false;
        }

        public void AddPhantom(int x, int y, int color)
        {
            if (!InBounds(x, y)) return;
            if (_line[x, y] == Empty) _line[x, y] = color;
            _placeable[x, y] = false;
        }

        public void AddMine(int x, int y)
        {
            if (InBounds(x, y)) _placeable[x, y] = false;
        }

        public struct GridPoint
        {
            public int X, Y;
            public GridPoint(int x, int y) { X = x; Y = y; }
        }

        public IEnumerable<GridPoint> EmptyPoints()
        {
            for (int x = 0; x < Size; x++)
            for (int y = 0; y < Size; y++)
                if (_placeable[x, y]) yield return new GridPoint(x, y);
        }
    }
}
