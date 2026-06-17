using System;
using System.Collections.Generic;
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
            TickResult r = d.Tick(NavCommand.Down);

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
            TickResult r = d.Tick(NavCommand.Up);

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
            TickResult r = d.Tick(NavCommand.Activate);

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
            TickResult r = d.Tick(NavCommand.Activate);

            Assert.True(clicked);
            Assert.False(r.Activated); // mod handled it; the game is not involved
        }

        [Fact]
        public void WantsInputCaptureReflectsTreeSize() {
            object a,
                b;
            var twoItem = TwoItem(out a, out b);
            var d = new OverlayDispatcher();
            d.Register(() => OverlayResult.Active(twoItem));
            d.Tick();
            Assert.True(d.WantsInputCapture);

            var single = new ClickOverlay(() => { });
            var d2 = new OverlayDispatcher();
            d2.Register(() => OverlayResult.Active(single));
            d2.Tick();
            Assert.False(d2.WantsInputCapture); // single node => fall through to the game
        }

        // One mod-side clickable node (has an OnClick handler).
        private sealed class ClickOverlay : IUiOverlay {
            private readonly Action _onClick;

            public ClickOverlay(Action onClick) => _onClick = onClick;

            public OverlayId Id => OverlayId.CharCreation;

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
