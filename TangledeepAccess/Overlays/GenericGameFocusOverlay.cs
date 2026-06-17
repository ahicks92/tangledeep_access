using System.Collections.Generic;
using TangledeepAccess.Focus;
using TangledeepAccess.Ui;
using TangledeepAccess.Ui.Graph;

namespace TangledeepAccess.Overlays
{
    /// <summary>
    /// The bottom-of-stack fallback overlay. It mirrors the game's own legacy menu graph as
    /// a real tree: starting from the focused control, it walks Tangledeep's 8-direction
    /// <c>UIObject.neighbors</c> graph (orthogonal links only) and builds one node per
    /// reachable control, wiring up/right/down/left transitions. Each node is identified by
    /// its backing <c>UIObject</c> (<see cref="ControlId.ForObject"/>) and reads its label
    /// via <see cref="GameLabelReader"/>.
    ///
    /// <para>Two reasons this is a full tree, not a single focused node:</para>
    /// <list type="bullet">
    /// <item>It is the fallback for every screen we have not written a bespoke overlay for,
    /// so it must represent the whole navigable surface, and it exercises the graph machinery
    /// (ordering, reconciliation) against real game data.</item>
    /// <item>The game's input loop never stops while a menu is open and moves
    /// <c>uiObjectFocus</c> through <c>ChangeUIFocus</c> (including odd key combos). Following
    /// that focus into our tree — via the dispatcher's tier-1 reference sync — is permanent
    /// infrastructure, not a temporary measure: without it our cursor desyncs from the game.</item>
    /// </list>
    ///
    /// Image-only controls (e.g. character-creation job buttons) have no widget text, so they
    /// remain silent here — that is by design; their bespoke overlay reads the structured data
    /// instead. Higher-priority overlays registered later override this for specific screens.
    /// </summary>
    internal sealed class GenericGameFocusOverlay : IUiOverlay
    {
        // The game's UIObject.neighbors is an 8-slot compass; we mirror the orthogonals.
        private const int NeighborUp = 0;
        private const int NeighborRight = 2;
        private const int NeighborDown = 4;
        private const int NeighborLeft = 6;

        public OverlayId Id => OverlayId.GenericGameFocus;

        /// <summary>Active whenever the game reports a focused UI element.</summary>
        public OverlayResult Handler()
        {
            return UIManagerScript.uiObjectFocus != null
                ? OverlayResult.Active(this)
                : OverlayResult.Inactive;
        }

        public void Build(IOverlayBuilder builder)
        {
            UIManagerScript.UIObject focus = UIManagerScript.uiObjectFocus;
            if (focus == null)
                return; // empty build => dispatcher treats as closed

            // Discover the navigable component by BFS over orthogonal neighbors.
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

            // Nodes: each control reads its own label lazily at speak time.
            foreach (UIManagerScript.UIObject uo in order)
            {
                UIManagerScript.UIObject captured = uo;
                builder.AddNode(
                    ControlId.ForObject(uo),
                    new NodeVtable
                    {
                        Label = ctx =>
                        {
                            string label = GameLabelReader.ReadLabel(captured);
                            if (!string.IsNullOrEmpty(label))
                                ctx.Message.Fragment(label);
                        },
                    }
                );
            }

            // Edges: only between controls we actually built.
            foreach (UIManagerScript.UIObject uo in order)
            {
                Connect(builder, seen, uo, NeighborUp, GraphDir.Up);
                Connect(builder, seen, uo, NeighborRight, GraphDir.Right);
                Connect(builder, seen, uo, NeighborDown, GraphDir.Down);
                Connect(builder, seen, uo, NeighborLeft, GraphDir.Left);
            }

            // Start at the currently focused control: this overlay follows the game's focus,
            // so a fresh activation should sit exactly where the game is, not at a corner.
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
