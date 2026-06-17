using System.Collections.Generic;
using TangledeepAccess.Ui;
using Xunit;

namespace TangledeepAccess.Tests.Ui
{
    public class DispatcherTests
    {
        // An overlay whose nodes are backed by real objects, so game-focus sync (tier-1)
        // can target them. Each node speaks its name.
        private sealed class RefOverlay : IUiOverlay
        {
            public OverlayId Id { get; }
            public readonly List<(object Obj, string Name)> Items = new();

            public RefOverlay(OverlayId id) => Id = id;

            public void Build(IOverlayBuilder builder)
            {
                foreach (var (obj, name) in Items)
                {
                    string n = name;
                    builder.AddLabel(ControlId.ForObject(obj), ctx => ctx.Message.Fragment(n));
                }
            }
        }

        [Fact]
        public void TopOfStackWins()
        {
            var bottom = new RefOverlay(OverlayId.GenericGameFocus);
            bottom.Items.Add((new object(), "bottom"));
            var top = new RefOverlay(OverlayId.Inventory);
            top.Items.Add((new object(), "top"));

            var d = new OverlayDispatcher();
            d.Register(() => OverlayResult.Active(bottom)); // registered first => lowest
            d.Register(() => OverlayResult.Active(top)); // last => top of stack

            Assert.Equal("top", d.Tick());
        }

        [Fact]
        public void InactiveHandlersAreSkipped()
        {
            var overlay = new RefOverlay(OverlayId.GenericGameFocus);
            overlay.Items.Add((new object(), "generic"));

            var d = new OverlayDispatcher();
            d.Register(() => OverlayResult.Active(overlay));
            d.Register(() => OverlayResult.Inactive);
            d.Register(() => null); // null also means inactive

            Assert.Equal("generic", d.Tick());
        }

        [Fact]
        public void GameFocusMovesCursorAndSpeaks()
        {
            object a = new object(),
                b = new object();
            var overlay = new RefOverlay(OverlayId.Inventory);
            overlay.Items.Add((a, "alpha"));
            overlay.Items.Add((b, "beta"));

            var d = new OverlayDispatcher();
            d.Register(() => OverlayResult.Active(overlay));

            Assert.Equal("alpha", d.Tick()); // starts at first item
            d.RecordGameFocus(b);
            Assert.Equal("beta", d.Tick()); // synced to focused object
            Assert.Null(d.Tick()); // unchanged focus => no repeat (dedupe + cache persisted)
        }

        [Fact]
        public void InactiveClearsCacheSoFocusResets()
        {
            object a = new object(),
                b = new object();
            var overlay = new RefOverlay(OverlayId.Inventory);
            overlay.Items.Add((a, "alpha"));
            overlay.Items.Add((b, "beta"));

            bool active = true;
            var d = new OverlayDispatcher();
            d.Register(() => active ? OverlayResult.Active(overlay) : OverlayResult.Inactive);

            Assert.Equal("alpha", d.Tick());
            d.RecordGameFocus(b);
            Assert.Equal("beta", d.Tick());

            active = false;
            Assert.Null(d.Tick()); // deactivated => cache cleared

            active = true;
            Assert.Equal("alpha", d.Tick()); // focus reset to start, re-spoken
        }

        [Fact]
        public void SleepingPreservesCache()
        {
            object a = new object(),
                b = new object();
            var overlay = new RefOverlay(OverlayId.Inventory);
            overlay.Items.Add((a, "alpha"));
            overlay.Items.Add((b, "beta"));

            OverlayResultKind mode = OverlayResultKind.Active;
            var d = new OverlayDispatcher();
            d.Register(() =>
                mode == OverlayResultKind.Active
                    ? OverlayResult.Active(overlay)
                    : OverlayResult.Sleeping(OverlayId.Inventory)
            );

            Assert.Equal("alpha", d.Tick());
            d.RecordGameFocus(b);
            Assert.Equal("beta", d.Tick());

            mode = OverlayResultKind.Sleeping;
            Assert.Null(d.Tick()); // sleeping: nothing built/spoken, cache kept

            mode = OverlayResultKind.Active;
            Assert.Null(d.Tick()); // focus still on beta => no repeat (cache survived)
        }

        [Fact]
        public void SwitchingActiveIdClearsOldCache()
        {
            object a = new object(),
                b = new object();
            var inv = new RefOverlay(OverlayId.Inventory);
            inv.Items.Add((a, "inv-a"));
            inv.Items.Add((b, "inv-b"));
            var equip = new RefOverlay(OverlayId.Equipment);
            equip.Items.Add((new object(), "equip"));

            bool showEquip = false;
            var d = new OverlayDispatcher();
            d.Register(() => OverlayResult.Active(inv));
            d.Register(() => showEquip ? OverlayResult.Active(equip) : OverlayResult.Inactive);

            Assert.Equal("inv-a", d.Tick());
            d.RecordGameFocus(b);
            Assert.Equal("inv-b", d.Tick()); // inventory focus on b

            showEquip = true;
            Assert.Equal("equip", d.Tick()); // equipment takes over

            showEquip = false;
            // Inventory cache was cleared when equipment took over, so focus resets to start.
            Assert.Equal("inv-a", d.Tick());
        }
    }
}
