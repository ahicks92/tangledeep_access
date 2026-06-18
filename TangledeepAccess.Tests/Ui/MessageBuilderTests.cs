using System;
using TangledeepAccess.Speech;
using Xunit;

namespace TangledeepAccess.Tests.Ui {
    public class MessageBuilderTests {
        [Fact]
        public void FragmentsAreSpaceJoinedListItemsAreCommaJoined() {
            string msg = new MessageBuilder()
                .Fragment("the thing with")
                .ListItem("a")
                .ListItem("b")
                .ListItem("c")
                .Build();
            Assert.Equal("the thing with a, b, c", msg);
        }

        [Fact]
        public void NoLeadingSpaceOnFirstFragment() {
            Assert.Equal("x", new MessageBuilder().Fragment("x").Build());
        }

        [Fact]
        public void FragmentsWithinAListItemAreSpaceJoined() {
            string msg = new MessageBuilder()
                .ListItem("hello")
                .Fragment("world")
                .ListItem("again")
                .Build();
            Assert.Equal("hello world, again", msg);
        }

        [Fact]
        public void ForcedCommaSeparatesFromPrecedingFragment() {
            string msg = new MessageBuilder()
                .Fragment("grid")
                .ListItemForcedComma("3 by 3")
                .Build();
            Assert.Equal("grid, 3 by 3", msg);
        }

        [Fact]
        public void NullAndEmptyFragmentsAreIgnored() {
            string msg = new MessageBuilder()
                .Fragment("a")
                .Fragment(null)
                .Fragment("")
                .Fragment("b")
                .Build();
            Assert.Equal("a b", msg);
        }

        [Fact]
        public void FractionReadsNumOfDenom() {
            Assert.Equal("5 of 20", new MessageBuilder().PushFraction(5, 20).Build());
        }

        [Fact]
        public void FractionWithUnitAppendsUnit() {
            Assert.Equal("3 of 5 charges", new MessageBuilder().PushFraction(3, 5, "charges").Build());
        }

        [Fact]
        public void FractionFollowsFragmentSpacingAndListBoundaries() {
            // The fraction space-joins after its label; list items comma-join (except the first,
            // which space-joins to the preceding fragment) — the status-readout shape.
            string msg = new MessageBuilder()
                .Fragment("Health")
                .PushFraction(5, 20)
                .ListItem("Stamina")
                .PushFraction(8, 8)
                .ListItem("Energy")
                .PushFraction(3, 10)
                .Build();
            Assert.Equal("Health 5 of 20 Stamina 8 of 8, Energy 3 of 10", msg);
        }

        [Fact]
        public void UniformListItemFractionsCommaJoinWithNoLeadingComma() {
            // The status-readout shape: each stat is a fraction with its name as unit in its own
            // list item. Commas fall between stats; none leads (plain ListItem, not forced).
            string msg = new MessageBuilder()
                .ListItem()
                .PushFraction(5, 20, "health")
                .ListItem()
                .PushFraction(8, 8, "stamina")
                .ListItem("Level 3")
                .Build();
            Assert.Equal("5 of 20 health, 8 of 8 stamina, Level 3", msg);
        }

        [Fact]
        public void EmptyBuilderBuildsNull() {
            Assert.Null(new MessageBuilder().Build());
        }

        [Fact]
        public void ReuseAfterBuildThrows() {
            var b = new MessageBuilder().Fragment("x");
            b.Build();
            Assert.Throws<InvalidOperationException>(() => b.Fragment("y"));
        }

        [Fact]
        public void ExplicitSpaceFragmentThrows() {
            Assert.Throws<ArgumentException>(() => new MessageBuilder().Fragment(" "));
        }
    }
}
