using System;
using System.Collections.Generic;
using TangledeepAccess.Controls;
using TangledeepAccess.Ui;
using TangledeepAccess.Ui.Graph;
using Xunit;

namespace TangledeepAccess.Tests.Ui {
    public class DispatcherNavTests {
        // Game-backed nodes (labels only, no mod handler) identified by real objects.
        private sealed class RefOverlay : IUiOverlay {
            public OverlayId Id => OverlayId.Inventory;
            public readonly List<(object Obj, string Name)> Items = new();

            public void Build(IOverlayBuilder builder) {
                foreach (var (obj, name) in Items) {
                    string n = name;
                    builder.AddNode(
                        ControlId.ForObject(obj),
                        new NodeVtable { Label = ctx => ctx.Message.Fragment(n) }
                    );
                }
                // Wire a simple vertical chain so navigation has somewhere to go.
                for (int i = 0; i + 1 < Items.Count; i++) {
                    ControlId a = ControlId.ForObject(Items[i].Obj);
                    ControlId b = ControlId.ForObject(Items[i + 1].Obj);
                    builder.Connect(a, TangledeepAccess.Ui.Graph.GraphDir.Down, b);
                    builder.Connect(b, TangledeepAccess.Ui.Graph.GraphDir.Up, a);
                }
                builder.SetStart(ControlId.ForObject(Items[0].Obj));
            }
        }

        private static RefOverlay TwoItem(out object a, out object b) {
            a = new object();
            b = new object();
            var o = new RefOverlay();
            o.Items.Add((a, "alpha"));
            o.Items.Add((b, "beta"));
            return o;
        }

        [Fact]
        public void NavigateMovesCursorReportsMoveAndFocus() {
            object a,
                b;
            var overlay = TwoItem(out a, out b);
            var d = new OverlayDispatcher();
            d.Register(() => OverlayResult.Active(overlay));

            d.Tick(); // settle at start "alpha"
            TickResult r = d.Tick(ModInputAction.Move(0, -1)); // down

            Assert.Equal("beta", r.Speak);
            Assert.True(r.Moved);
            Assert.Same(b, r.FocusReference);
        }

        [Fact]
        public void NavigateAtEdgeDoesNotReportMove() {
            object a,
                b;
            var overlay = TwoItem(out a, out b);
            var d = new OverlayDispatcher();
            d.Register(() => OverlayResult.Active(overlay));

            d.Tick(); // at "alpha", top edge
            TickResult r = d.Tick(ModInputAction.Move(0, 1)); // up

            Assert.False(r.Moved); // nothing above
            Assert.Equal("alpha", r.Speak); // re-read current
        }

        [Fact]
        public void ActivateGameBackedNodeReportsActivated() {
            object a,
                b;
            var overlay = TwoItem(out a, out b);
            var d = new OverlayDispatcher();
            d.Register(() => OverlayResult.Active(overlay));

            d.Tick(); // at "alpha"
            TickResult r = d.Tick(ModInputAction.Of(ModInputKind.Confirm));

            Assert.True(r.Activated);
            Assert.Same(a, r.FocusReference);
        }

        [Fact]
        public void ActivateModNodeRunsHandlerNotGame() {
            bool clicked = false;
            var overlay = new ClickOverlay(() => clicked = true);
            var d = new OverlayDispatcher();
            d.Register(() => OverlayResult.Active(overlay));

            d.Tick();
            TickResult r = d.Tick(ModInputAction.Of(ModInputKind.Confirm));

            Assert.True(clicked);
            Assert.False(r.Activated); // mod handled it; the game is not involved
        }

        // An overlay that declares it owns input, with a single node.
        private sealed class CaptureOverlay : IUiOverlay {
            public OverlayId Id => OverlayId.JobGrid;

            public void Build(IOverlayBuilder builder) {
                builder.AddLabel(ControlId.Structural("only"), ctx => ctx.Message.Fragment("only"));
                builder.CaptureInput();
            }
        }

        [Fact]
        public void CapturesInputReflectsDeclarationNotNodeCount() {
            // A multi-node overlay that does NOT declare capture: we do not own input. Node count
            // no longer implies ownership.
            object a,
                b;
            var twoItem = TwoItem(out a, out b);
            var d = new OverlayDispatcher();
            d.Register(() => OverlayResult.Active(twoItem));
            d.Tick();
            Assert.False(d.CapturesInput);

            // A single-node overlay that calls CaptureInput() owns input.
            var capturing = new CaptureOverlay();
            var d2 = new OverlayDispatcher();
            d2.Register(() => OverlayResult.Active(capturing));
            d2.Tick();
            Assert.True(d2.CapturesInput);
        }

        // One mod-side clickable node (has an OnClick handler).
        private sealed class ClickOverlay : IUiOverlay {
            private readonly Action _onClick;

            public ClickOverlay(Action onClick) => _onClick = onClick;

            public OverlayId Id => OverlayId.JobGrid;

            public void Build(IOverlayBuilder builder) {
                builder.AddNode(
                    ControlId.Structural("only"),
                    new NodeVtable {
                        Label = ctx => ctx.Message.Fragment("only"),
                        OnClick = (ctx, mods) => _onClick(),
                    }
                );
            }
        }
    }
}
