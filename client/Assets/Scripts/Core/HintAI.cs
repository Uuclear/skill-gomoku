using System.Collections.Generic;

namespace Gomoku.Core
{
    /// <summary>Summary of one side's current shapes (mirrors game.LineSummary).</summary>
    public struct LineSummary
    {
        public int Longest;
        public int Open3;
        public int Open4;
    }

    /// <summary>
    /// Pure C# mirror of server/internal/game/analyze.go: oracle hint scoring,
    /// immediate-win point detection and line statistics. Kept UnityEngine-free
    /// for EditMode/offline testing. The server still sends authoritative
    /// oracle/radar/analyze payloads; this local copy is used defensively and
    /// validated by tests against the same inputs.
    /// </summary>
    public static class HintAI
    {
        static readonly (int dx, int dy)[] Dirs =
            { (1, 0), (0, 1), (1, 1), (1, -1) };

        public struct Point
        {
            public int X, Y;
            public Point(int x, int y) { X = x; Y = y; }
        }

        /// <summary>Up to 3 best attack/defense points for color c (神谕).</summary>
        public static List<Point> HintPoints(HintBoard b, int c)
        {
            int opp = 1 - c;
            var best = new List<(Point p, int score)>(4);
            for (int x = 0; x < HintBoard.Size; x++)
            for (int y = 0; y < HintBoard.Size; y++)
            {
                // Mirror the server: candidates only need no stone/phantom.
                if (b.LineAt(x, y) != HintBoard.Empty) continue;
                int s = PointScore(b, x, y, c) * 2 + PointScore(b, x, y, opp);
                s -= (Abs(x - 7) + Abs(y - 7)) / 8;
                Insert(best, new Point(x, y), s, 3);
            }
            var outp = new List<Point>(best.Count);
            foreach (var t in best) outp.Add(t.p);
            return outp;
        }

        /// <summary>Every empty point where opp can complete five next (威胁雷达).</summary>
        public static List<Point> ImmediateWinPoints(HintBoard b, int color)
        {
            var outp = new List<Point>();
            for (int x = 0; x < HintBoard.Size; x++)
            for (int y = 0; y < HintBoard.Size; y++)
            {
                if (b.LineAt(x, y) != HintBoard.Empty) continue;
                foreach (var d in Dirs)
                {
                    int n = 1;
                    for (int i = 1; i < 5; i++)
                    {
                        if (b.LineAt(x + d.dx * i, y + d.dy * i) != color) break;
                        n++;
                    }
                    for (int i = 1; i < 5; i++)
                    {
                        if (b.LineAt(x - d.dx * i, y - d.dy * i) != color) break;
                        n++;
                    }
                    if (n >= 5) { outp.Add(new Point(x, y)); break; }
                }
            }
            return outp;
        }

        public static void AnalyzeLines(HintBoard b, out LineSummary black, out LineSummary white)
        {
            black = default;
            white = default;
            var seen = new HashSet<(int, int, int, int)>();
            for (int x = 0; x < HintBoard.Size; x++)
            for (int y = 0; y < HintBoard.Size; y++)
            for (int di = 0; di < Dirs.Length; di++)
            {
                var d = Dirs[di];
                int col = b.LineAt(x, y);
                if (col == HintBoard.Empty) continue;
                // only the start of a maximal run
                if (b.LineAt(x - d.dx, y - d.dy) == col) continue;
                int n = 1;
                while (b.LineAt(x + d.dx * n, y + d.dy * n) == col) n++;
                var key = (x, y, di, n);
                if (!seen.Add(key)) continue;

                ref LineSummary sum = ref col == 0 ? ref black : ref white;
                if (n > sum.Longest) sum.Longest = n;
                bool openBefore = OpenAt(b, x - d.dx, y - d.dy);
                bool openAfter = OpenAt(b, x + d.dx * n, y + d.dy * n);
                if (n == 3 && openBefore && openAfter) sum.Open3++;
                if (n == 4 && openBefore && openAfter) sum.Open4++;
            }
        }

        static bool OpenAt(HintBoard b, int x, int y) =>
            HintBoard.InBounds(x, y) && b.LineAt(x, y) == HintBoard.Empty;

        static int PointScore(HintBoard b, int x, int y, int c)
        {
            int total = 0;
            foreach (var d in Dirs)
            {
                int n = 1;
                bool openA = false, openB = false;
                for (int i = 1; i < 5; i++)
                {
                    int v = b.LineAt(x + d.dx * i, y + d.dy * i);
                    if (v != c)
                    {
                        int nx = x + d.dx * i, ny = y + d.dy * i;
                        openA = HintBoard.InBounds(nx, ny) && v == HintBoard.Empty;
                        break;
                    }
                    n++;
                }
                for (int i = 1; i < 5; i++)
                {
                    int v = b.LineAt(x - d.dx * i, y - d.dy * i);
                    if (v != c)
                    {
                        int nx = x - d.dx * i, ny = y - d.dy * i;
                        openB = HintBoard.InBounds(nx, ny) && v == HintBoard.Empty;
                        break;
                    }
                    n++;
                }
                switch (n)
                {
                    case >= 5: total += 100000; break;
                    case 4 when openA && openB: total += 10000; break;
                    case 4: total += 1000; break;
                    case 3 when openA && openB: total += 1000; break;
                    case 3: total += 100; break;
                    case 2: total += 10; break;
                    default: total += 1; break;
                }
            }
            return total;
        }

        static void Insert(List<(Point p, int score)> best, Point p, int s, int cap)
        {
            if (best.Count >= cap && s <= best[best.Count - 1].score) return;
            best.Add((p, s));
            for (int i = best.Count - 1; i > 0 && best[i].score > best[i - 1].score; i--)
                (best[i], best[i - 1]) = (best[i - 1], best[i]);
            if (best.Count > cap) best.RemoveAt(best.Count - 1);
        }

        static int Abs(int v) => v < 0 ? -v : v;
    }
}
