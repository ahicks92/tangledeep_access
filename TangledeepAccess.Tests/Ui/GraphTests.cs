using System;
using System.Collections.Generic;
using System.Linq;
using TangledeepAccess.Speech;
using TangledeepAccess.Ui;
using TangledeepAccess.Ui.Graph;
using Xunit;

namespace TangledeepAccess.Tests.Ui {
    public class GraphTests {
        private static OverlayCtx Ctx() => new OverlayCtx(new MessageBuilder(), Modifiers.None);

        // A vertical menu whose nodes are keyed by, and speak, the given strings.
        private static GraphRender Menu(params string[] keys) {
            var b = new GraphBuilder();
            foreach (string k in keys) {
                string key = k;
                b.AddLabel(ControlId.Structural(key), ctx => ctx.Message.Fragment(key));
            }
            return b.Build();
        }

        private static List<string> OrderKeys(GraphRender r) =>
            KeyGraph.ComputeOrder(r).Select(c => (string)c.StructuralKey).ToList();

        [Fact]
        public void VerticalMenuOrderIsTopToBottom() {
            Assert.Equal(new[] { "a", "b", "c" }, OrderKeys(Menu("a", "b", "c")));
        }

        [Fact]
        public void GridOrderIsRowMajor() {
            // Two column-linked rows: [a b] over [c d].
            var b = new GraphBuilder();
            b.StartRow("g")
                .AddLabel(ControlId.Structural("a"), c => c.Message.Fragment("a"))
                .AddLabel(ControlId.Structural("b"), c => c.Message.Fragment("b"))
                .EndRow();
            b.StartRow("g")
                .AddLabel(ControlId.Structural("c"), c => c.Message.Fragment("c"))
                .AddLabel(ControlId.Structural("d"), c => c.Message.Fragment("d"))
                .EndRow();
            Assert.Equal(new[] { "a", "b", "c", "d" }, OrderKeys(b.Build()));
        }

        [Fact]
        public void DeletingFocusedNodeLandsOnPreviousInOrder() {
            GraphRender render = Menu("a", "b", "c", "d");
            var state = new GraphState();
            var g = new KeyGraph(_ => render, state);
            g.Rerender(Ctx()); // focus at start "a"

            // Focus "c", then rebuild without it.
            state.CurKey = ControlId.Structural("c");
            render = Menu("a", "b", "d");
            g.Rerender(Ctx());

            Assert.Equal("b", state.CurKey.StructuralKey);
        }

        [Fact]
        public void DeletingFirstNodeFallsBackToStart() {
            GraphRender render = Menu("a", "b", "c");
            var state = new GraphState();
            var g = new KeyGraph(_ => render, state);
            g.Rerender(Ctx());
            state.CurKey = ControlId.Structural("a");

            render = Menu("b", "c");
            g.Rerender(Ctx());
            Assert.Equal("b", state.CurKey.StructuralKey);
        }

        [Fact]
        public void Tier1FollowsMovedObjectWhenStructuralKeyChanged() {
            object item = new object();
            var b1 = new GraphBuilder();
            b1.AddLabel(ControlId.Structural("filler"), c => c.Message.Fragment("filler"));
            b1.AddLabel(ControlId.Referenced(item, "slot-1"), c => c.Message.Fragment("item"));
            GraphRender render = b1.Build();

            var state = new GraphState();
            var g = new KeyGraph(_ => render, state);
            g.Rerender(Ctx());
            state.CurKey = ControlId.Referenced(item, "slot-1");

            // Same object, now in slot 2 (structural key changed).
            var b2 = new GraphBuilder();
            b2.AddLabel(ControlId.Structural("filler"), c => c.Message.Fragment("filler"));
            b2.AddLabel(ControlId.Referenced(item, "slot-2"), c => c.Message.Fragment("item"));
            render = b2.Build();
            g.Rerender(Ctx());

            Assert.Equal("slot-2", state.CurKey.StructuralKey);
            Assert.True(state.CurKey.ReferenceMatches(item));
        }

        [Fact]
        public void Tier2FollowsStructuralKeyWhenObjectRebuilt() {
            object refA = new object();
            var b1 = new GraphBuilder();
            b1.AddLabel(ControlId.Referenced(refA, "k"), c => c.Message.Fragment("x"));
            b1.AddLabel(ControlId.Structural("other"), c => c.Message.Fragment("other"));
            GraphRender render = b1.Build();

            var state = new GraphState();
            var g = new KeyGraph(_ => render, state);
            g.Rerender(Ctx());
            state.CurKey = ControlId.Referenced(refA, "k");

            // Backing object rebuilt (new instance) but the structural key is stable.
            object refB = new object();
            var b2 = new GraphBuilder();
            b2.AddLabel(ControlId.Referenced(refB, "k"), c => c.Message.Fragment("x"));
            b2.AddLabel(ControlId.Structural("other"), c => c.Message.Fragment("other"));
            render = b2.Build();
            g.Rerender(Ctx());

            Assert.Equal("k", state.CurKey.StructuralKey);
            Assert.True(state.CurKey.ReferenceMatches(refB));
        }

        [Fact]
        public void MoveDownSpeaksDestinationAndAdvancesCursor() {
            GraphRender render = Menu("a", "b", "c");
            var state = new GraphState();
            var g = new KeyGraph(_ => render, state);
            g.Rerender(Ctx()); // cur = a

            var ctx = Ctx();
            g.Move(ctx, GraphDir.Down);
            Assert.Equal("b", ctx.Message.Build());
            Assert.Equal("b", state.CurKey.StructuralKey);
        }

        [Fact]
        public void MoveAtEdgeRereadsCurrent() {
            GraphRender render = Menu("a", "b");
            var state = new GraphState();
            var g = new KeyGraph(_ => render, state);
            g.Rerender(Ctx());
            g.Move(Ctx(), GraphDir.Down); // a -> b
            Assert.Equal("b", state.CurKey.StructuralKey);

            var ctx = Ctx();
            g.Move(ctx, GraphDir.Down); // at bottom edge
            Assert.Equal("b", ctx.Message.Build()); // re-read current
            Assert.Equal("b", state.CurKey.StructuralKey);
        }

        [Fact]
        public void MoveToEdgeJumpsToBottom() {
            GraphRender render = Menu("a", "b", "c", "d");
            var state = new GraphState();
            var g = new KeyGraph(_ => render, state);
            g.Rerender(Ctx());

            var ctx = Ctx();
            g.MoveToEdge(ctx, GraphDir.Down);
            Assert.Equal("d", state.CurKey.StructuralKey);
            Assert.Equal("d", ctx.Message.Build());
        }

        [Fact]
        public void SuggestMoveIsHonoredOnNextRender() {
            GraphRender render = Menu("a", "b", "c");
            var state = new GraphState();
            var g = new KeyGraph(_ => render, state);
            g.Rerender(Ctx());

            state.NextSuggestedMove = ControlId.Structural("c");
            g.Rerender(Ctx());
            Assert.Equal("c", state.CurKey.StructuralKey);
            Assert.Null(state.NextSuggestedMove);
        }

        [Fact]
        public void RawGraphBuildsArbitraryTransitions() {
            // Mirror a 2x2 grid via the raw API (as the generic overlay does from neighbors).
            var b = new GraphBuilder();
            ControlId a = ControlId.Structural("a"),
                bb = ControlId.Structural("b"),
                c = ControlId.Structural("c"),
                d = ControlId.Structural("d");
            b.AddNode(a, new NodeVtable { Label = ctx => ctx.Message.Fragment("a") });
            b.AddNode(bb, new NodeVtable { Label = ctx => ctx.Message.Fragment("b") });
            b.AddNode(c, new NodeVtable { Label = ctx => ctx.Message.Fragment("c") });
            b.AddNode(d, new NodeVtable { Label = ctx => ctx.Message.Fragment("d") });
            b.Connect(a, GraphDir.Right, bb).Connect(bb, GraphDir.Left, a);
            b.Connect(a, GraphDir.Down, c).Connect(c, GraphDir.Up, a);
            b.Connect(bb, GraphDir.Down, d).Connect(d, GraphDir.Up, bb);
            b.Connect(c, GraphDir.Right, d).Connect(d, GraphDir.Left, c);
            b.SetStart(a);

            GraphRender render = b.Build();
            Assert.Equal(new[] { "a", "b", "c", "d" }, OrderKeys(render));

            var state = new GraphState();
            var g = new KeyGraph(_ => render, state);
            g.Rerender(Ctx());
            var ctx = Ctx();
            g.Move(ctx, GraphDir.Right); // a -> b
            Assert.Equal("b", ctx.Message.Build());
            Assert.Equal("b", state.CurKey.StructuralKey);
        }

        [Fact]
        public void RawStartDefaultsToFirstNode() {
            var b = new GraphBuilder();
            b.AddNode(
                ControlId.Structural("x"),
                new NodeVtable { Label = c => c.Message.Fragment("x") }
            );
            b.AddNode(
                ControlId.Structural("y"),
                new NodeVtable { Label = c => c.Message.Fragment("y") }
            );
            GraphRender render = b.Build();
            Assert.Equal("x", render.StartKey.StructuralKey);
        }

        [Fact]
        public void MixingRawAndMenuThrows() {
            var b = new GraphBuilder();
            b.AddNode(
                ControlId.Structural("x"),
                new NodeVtable { Label = c => c.Message.Fragment("x") }
            );
            b.AddLabel(ControlId.Structural("y"), c => c.Message.Fragment("y"));
            Assert.Throws<System.InvalidOperationException>(() => b.Build());
        }

        [Fact]
        public void EmptyBuildClosesGraph() {
            var state = new GraphState();
            var g = new KeyGraph(_ => new GraphBuilder().Build(), state); // Build() => null
            Assert.False(g.Rerender(Ctx()));
        }

        [Fact]
        public void CloseFromCallbackClosesGraph() {
            var state = new GraphState();
            var g = new KeyGraph(
                ctx => {
                    ctx.Controller.Close();
                    return Menu("a");
                },
                state
            );
            Assert.False(g.Rerender(Ctx()));
            Assert.True(g.Closed);
        }
    }
}
