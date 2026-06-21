using System;
using System.Collections.Generic;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// One terrain tile handed to the clusterer: a grid position and a <see cref="Kind"/> id (the
    /// engine layer's <c>SpecialMapObject</c> cast to int — water, mud, lava, ...). Pure data so the
    /// flood fill stays in Core and is unit-tested off-engine; the engine layer turns
    /// <c>isTerrainTile</c> destructibles on explored/visible tiles into these.
    /// </summary>
    public readonly struct TerrainCell {
        public readonly int X;
        public readonly int Y;
        public readonly int Kind;

        public TerrainCell(int x, int y, int kind) {
            X = x;
            Y = y;
            Kind = kind;
        }
    }

    /// <summary>
    /// A connected region of one terrain kind, as found by <see cref="TerrainClusterer.Cluster"/>.
    /// Holds the member cells (static — terrain does not move) and its bounding box; the hero-relative
    /// bits (<see cref="NearestCellTo"/>) are computed on demand at speak time so they track the live
    /// hero, never cached. <see cref="FillFraction"/> is members over bounding-box area — how solidly
    /// the box is filled (a ragged pool reads well under 1).
    /// </summary>
    public sealed class TerrainCluster {
        public readonly int Kind;
        public readonly IReadOnlyList<TerrainCell> Cells;
        public readonly int MinX;
        public readonly int MinY;
        public readonly int MaxX;
        public readonly int MaxY;

        public TerrainCluster(int kind, IReadOnlyList<TerrainCell> cells, int minX, int minY, int maxX, int maxY) {
            Kind = kind;
            Cells = cells;
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        public int Width => MaxX - MinX + 1;
        public int Height => MaxY - MinY + 1;
        public int CellCount => Cells.Count;

        /// <summary>Members divided by bounding-box area, in (0, 1]. 1 is a solid rectangle.</summary>
        public double FillFraction => (double)CellCount / (Width * Height);

        /// <summary>
        /// A canonical member cell — the lexicographically smallest by x then y. It is always an actual
        /// member of the cluster (unlike a bounding-box corner, which on a ragged region may be empty)
        /// and unique across clusters (regions are disjoint, so no cell belongs to two), so two pools
        /// whose bounding boxes overlap — one filling the bottom-left, the other the top-right of the
        /// same box — still get distinct ids. Used as the stable half of a cluster's scanner identity.
        /// </summary>
        public TerrainCell CanonicalCell {
            get {
                TerrainCell best = Cells[0];
                foreach (TerrainCell c in Cells) {
                    if (c.X < best.X || (c.X == best.X && c.Y < best.Y)) {
                        best = c;
                    }
                }

                return best;
            }
        }

        /// <summary>
        /// Like <see cref="NearestCellTo"/> but considering only members the caller's
        /// <paramref name="visible"/> predicate accepts (cells currently in sight), so a pool only
        /// partly in view is represented by the nearest part actually visible. <paramref name="any"/>
        /// reports whether <em>any</em> member was visible; when false the returned cell is meaningless
        /// and the caller falls back to <see cref="NearestCellTo"/>. Same king-move (Chebyshev)
        /// tie-break as <see cref="NearestCellTo"/>.
        /// </summary>
        public TerrainCell NearestVisibleCellTo(int hx, int hy, Func<int, int, bool> visible, out bool any) {
            TerrainCell best = Cells[0];
            int bestCheb = int.MaxValue;
            int bestManh = int.MaxValue;
            any = false;
            foreach (TerrainCell c in Cells) {
                if (!visible(c.X, c.Y)) {
                    continue;
                }

                int adx = Math.Abs(c.X - hx);
                int ady = Math.Abs(c.Y - hy);
                int cheb = Math.Max(adx, ady);
                int manh = adx + ady;
                if (!any
                    || cheb < bestCheb
                    || (cheb == bestCheb && manh < bestManh)
                    || (cheb == bestCheb && manh == bestManh && c.X < best.X)
                    || (cheb == bestCheb && manh == bestManh && c.X == best.X && c.Y < best.Y)) {
                    best = c;
                    bestCheb = cheb;
                    bestManh = manh;
                    any = true;
                }
            }

            return best;
        }

        /// <summary>
        /// The member cell closest to (<paramref name="hx"/>, <paramref name="hy"/>) by king-move
        /// (Chebyshev) distance — the real step count in 8-direction movement — ties broken by the
        /// smaller Manhattan distance, then by x, then y for a stable choice. This is the point the
        /// scanner reports and Goto travels to. Never empty: a cluster always has at least one cell.
        /// </summary>
        public TerrainCell NearestCellTo(int hx, int hy) {
            TerrainCell best = Cells[0];
            int bestCheb = int.MaxValue;
            int bestManh = int.MaxValue;
            foreach (TerrainCell c in Cells) {
                int adx = Math.Abs(c.X - hx);
                int ady = Math.Abs(c.Y - hy);
                int cheb = Math.Max(adx, ady);
                int manh = adx + ady;
                if (cheb < bestCheb
                    || (cheb == bestCheb && manh < bestManh)
                    || (cheb == bestCheb && manh == bestManh && c.X < best.X)
                    || (cheb == bestCheb && manh == bestManh && c.X == best.X && c.Y < best.Y)) {
                    best = c;
                    bestCheb = cheb;
                    bestManh = manh;
                }
            }

            return best;
        }
    }

    /// <summary>
    /// Groups terrain cells into maximal connected regions of a single kind — the load-bearing step
    /// behind the scanner's Terrain category and the F2 radar's terrain pings. Two cells join the same
    /// cluster when they share a <see cref="TerrainCell.Kind"/> and are 8-connected (orthogonal or
    /// diagonal neighbors), so a pool reads as one feature rather than dozens of tiles.
    ///
    /// <para>The caller decides membership: it passes only the cells it wants considered (explored
    /// tiles for the scanner, the strictly smaller in-sight set for the F2 radar), so "never cluster
    /// across unexplored" is enforced by what is handed in, not by this code. Duplicate positions are
    /// tolerated (first kind wins); the result order is deterministic given the input order.</para>
    ///
    /// <para>Linear in the cell count (each cell is visited once); on Tangledeep's small floors this is
    /// trivial. Pure (BCL only) → lives in Core, unit-tested off-engine.</para>
    /// </summary>
    public static class TerrainClusterer {
        // 8-connected neighbor offsets.
        private static readonly (int Dx, int Dy)[] Neighbors = {
            (0, 1), (1, 1), (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1),
        };

        public static List<TerrainCluster> Cluster(IEnumerable<TerrainCell> cells) {
            // Index by position so neighbor lookup is O(1); first kind wins on a duplicate tile.
            var byPos = new Dictionary<(int, int), int>();
            foreach (TerrainCell c in cells) {
                var key = (c.X, c.Y);
                if (!byPos.ContainsKey(key)) {
                    byPos[key] = c.Kind;
                }
            }

            var clusters = new List<TerrainCluster>();
            var visited = new HashSet<(int, int)>();
            var stack = new Stack<(int X, int Y)>();

            foreach (KeyValuePair<(int X, int Y), int> seed in byPos) {
                if (visited.Contains(seed.Key)) {
                    continue;
                }

                int kind = seed.Value;
                var members = new List<TerrainCell>();
                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

                stack.Push(seed.Key);
                visited.Add(seed.Key);
                while (stack.Count > 0) {
                    (int x, int y) = stack.Pop();
                    members.Add(new TerrainCell(x, y, kind));
                    if (x < minX) {
                        minX = x;
                    }
                    if (x > maxX) {
                        maxX = x;
                    }
                    if (y < minY) {
                        minY = y;
                    }
                    if (y > maxY) {
                        maxY = y;
                    }

                    foreach ((int dx, int dy) in Neighbors) {
                        var np = (x + dx, y + dy);
                        if (visited.Contains(np)) {
                            continue;
                        }
                        if (byPos.TryGetValue(np, out int nkind) && nkind == kind) {
                            visited.Add(np);
                            stack.Push(np);
                        }
                    }
                }

                clusters.Add(new TerrainCluster(kind, members, minX, minY, maxX, maxY));
            }

            return clusters;
        }
    }
}
