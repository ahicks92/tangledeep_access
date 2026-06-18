using System.Text;

namespace TangledeepAccess.Gameplay {
    /// <summary>
    /// Spoken descriptions of a grid offset, in Tangledeep's convention: +x is east, +y is
    /// north (verified from <c>MapMasterScript.xDirections</c>). Pure integer math with no
    /// engine dependency, so it lives in Core and is unit-tested off-engine. The engine layer
    /// computes <c>(target - hero)</c> as ints and calls in.
    /// </summary>
    public static class GridDirection {
        /// <summary>
        /// Component description of an offset in screen-relative terms, e.g. "3 right, 2 up",
        /// "4 left", or "here" when both components are zero. The grid's +x is east (spoken
        /// "right") and +y is north (spoken "up"); the x component is spoken before the y.
        /// </summary>
        public static string Offset(int dx, int dy) {
            if (dx == 0 && dy == 0) {
                return "here";
            }

            var sb = new StringBuilder();
            if (dx > 0) {
                sb.Append(dx).Append(" right");
            } else if (dx < 0) {
                sb.Append(-dx).Append(" left");
            }

            if (dy != 0) {
                if (sb.Length > 0) {
                    sb.Append(", ");
                }

                sb.Append(dy > 0 ? dy + " up" : -dy + " down");
            }

            return sb.ToString();
        }

        /// <summary>
        /// The 8-way compass name of an offset by component sign (any non-zero pair is a
        /// diagonal), or "here". Used for single-step cursor moves and adjacency.
        /// </summary>
        public static string Compass(int dx, int dy) {
            bool n = dy > 0,
                s = dy < 0,
                e = dx > 0,
                w = dx < 0;
            if (n && e) {
                return "northeast";
            }
            if (n && w) {
                return "northwest";
            }
            if (s && e) {
                return "southeast";
            }
            if (s && w) {
                return "southwest";
            }
            if (n) {
                return "north";
            }
            if (s) {
                return "south";
            }
            if (e) {
                return "east";
            }
            if (w) {
                return "west";
            }

            return "here";
        }

        /// <summary>King-move (Chebyshev) distance in tiles — the number of steps to walk there.</summary>
        public static int Steps(int dx, int dy) {
            int ax = dx < 0 ? -dx : dx;
            int ay = dy < 0 ? -dy : dy;
            return ax > ay ? ax : ay;
        }
    }
}
