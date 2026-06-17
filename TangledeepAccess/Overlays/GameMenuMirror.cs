using System;
using System.Collections.Generic;
using TangledeepAccess.Ui;
using TangledeepAccess.Ui.Graph;

namespace TangledeepAccess.Overlays
{
    /// <summary>
    /// Mirrors the game's legacy menu graph into our overlay tree. Starting from the focused
    /// control, it walks Tangledeep's 8-direction <c>UIObject.neighbors</c> graph (orthogonal
    /// links only), builds one node per reachable control (identified by its backing
    /// <c>UIObject</c>), and wires up/right/down/left transitions. Each node's spoken label is
    /// produced by a caller-supplied provider, so different overlays can read the same graph
    /// differently (raw widget text for the generic fallback, structured data for a bespoke
    /// screen).
    /// </summary>
    internal static class GameMenuMirror
    {
        // The game's UIObject.neighbors is an 8-slot compass; we mirror the orthogonals.
        private const int NeighborUp = 0;
        private const int NeighborRight = 2;
        private const int NeighborDown = 4;
        private const int NeighborLeft = 6;

        /// <summary>
        /// Build the tree from the currently focused control. <paramref name="labelProvider"/>
        /// maps a control to its spoken text (null/empty = silent). Builds nothing if no
        /// control has focus.
        /// </summary>
        public static void Build(
            IOverlayBuilder builder,
            Func<UIManagerScript.UIObject, string> labelProvider
        )
        {
            UIManagerScript.UIObject focus = UIManagerScript.uiObjectFocus;
            if (focus == null)
                return;

            var seen = new HashSet<UIManagerScript.UIObject>();
            var order = new List<UIManagerScript.UIObject>();
            var queue = new Queue<UIManagerScript.UIObject>();
            queue.Enqueue(focus);
            while (queue.Count > 0)
            {
                UIManagerScript.UIObject uo = queue.Dequeue();
                if (uo == null || !seen.Add(uo))
                    continue;
                order.Add(uo);
                Enqueue(queue, uo, NeighborUp);
                Enqueue(queue, uo, NeighborRight);
                Enqueue(queue, uo, NeighborDown);
                Enqueue(queue, uo, NeighborLeft);
            }

            foreach (UIManagerScript.UIObject uo in order)
            {
                UIManagerScript.UIObject captured = uo;
                builder.AddNode(
                    ControlId.ForObject(uo),
                    new NodeVtable
                    {
                        Label = ctx =>
                        {
                            string label = labelProvider(captured);
                            if (!string.IsNullOrEmpty(label))
                                ctx.Message.Fragment(label);
                        },
                    }
                );
            }

            foreach (UIManagerScript.UIObject uo in order)
            {
                Connect(builder, seen, uo, NeighborUp, GraphDir.Up);
                Connect(builder, seen, uo, NeighborRight, GraphDir.Right);
                Connect(builder, seen, uo, NeighborDown, GraphDir.Down);
                Connect(builder, seen, uo, NeighborLeft, GraphDir.Left);
            }

            // Start at the focused control: these overlays follow the game's focus, so a fresh
            // activation should sit exactly where the game is, not at a corner.
            builder.SetStart(ControlId.ForObject(focus));
        }

        private static void Enqueue(
            Queue<UIManagerScript.UIObject> queue,
            UIManagerScript.UIObject uo,
            int slot
        )
        {
            UIManagerScript.UIObject neighbor = NeighborAt(uo, slot);
            if (neighbor != null)
                queue.Enqueue(neighbor);
        }

        private static void Connect(
            IOverlayBuilder builder,
            HashSet<UIManagerScript.UIObject> built,
            UIManagerScript.UIObject from,
            int slot,
            GraphDir dir
        )
        {
            UIManagerScript.UIObject to = NeighborAt(from, slot);
            if (to != null && built.Contains(to))
                builder.Connect(ControlId.ForObject(from), dir, ControlId.ForObject(to));
        }

        private static UIManagerScript.UIObject NeighborAt(UIManagerScript.UIObject uo, int slot)
        {
            UIManagerScript.UIObject[] neighbors = uo.neighbors;
            return neighbors != null && slot < neighbors.Length ? neighbors[slot] : null;
        }
    }
}
