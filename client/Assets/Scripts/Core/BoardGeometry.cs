using System;

namespace Gomoku.Core
{
    /// <summary>
    /// Pure (UnityEngine-free) board coordinate math so it can be unit tested
    /// outside the editor. Grid indices are 0..size-1 on both axes; local space
    /// is centred on the board with +y pointing up (row 0 at the bottom).
    /// </summary>
    public static class BoardGeometry
    {
        public static bool InBoard(int size, int x, int y) =>
            x >= 0 && y >= 0 && x < size && y < size;

        /// <summary>Distance between two adjacent intersections in UI pixels.</summary>
        public static float Step(float boardPx, int size) => boardPx / size;

        public static float ToLocalX(int x, int size, float step) =>
            (x - (size - 1) * 0.5f) * step;

        public static float ToLocalY(int y, int size, float step) =>
            (y - (size - 1) * 0.5f) * step;

        /// <summary>
        /// Maps a local point to the nearest intersection. Returns null when
        /// the point is not within the tap tolerance (half a cell) of the grid.
        /// </summary>
        public static Tuple<int, int> ToGrid(
            float localX, float localY, int size, float step)
        {
            float gx = localX / step + (size - 1) * 0.5f;
            float gy = localY / step + (size - 1) * 0.5f;
            int x = (int)Math.Round(gx);
            int y = (int)Math.Round(gy);
            const float tolerance = 0.5f;
            if (Math.Abs(gx - x) > tolerance || Math.Abs(gy - y) > tolerance)
                return null;
            if (!InBoard(size, x, y)) return null;
            return Tuple.Create(x, y);
        }
    }
}
