using System;
using TangledeepAccess.Speech;
using Xunit;

namespace TangledeepAccess.Tests.Ui
{
    public class MessageBuilderTests
    {
        [Fact]
        public void FragmentsAreSpaceJoinedListItemsAreCommaJoined()
        {
            string msg = new MessageBuilder()
                .Fragment("the thing with")
                .ListItem("a")
                .ListItem("b")
                .ListItem("c")
                .Build();
            Assert.Equal("the thing with a, b, c", msg);
        }

        [Fact]
        public void NoLeadingSpaceOnFirstFragment()
        {
            Assert.Equal("x", new MessageBuilder().Fragment("x").Build());
        }

        [Fact]
        public void FragmentsWithinAListItemAreSpaceJoined()
        {
            string msg = new MessageBuilder()
                .ListItem("hello")
                .Fragment("world")
                .ListItem("again")
                .Build();
            Assert.Equal("hello world, again", msg);
        }

        [Fact]
        public void ForcedCommaSeparatesFromPrecedingFragment()
        {
            string msg = new MessageBuilder()
                .Fragment("grid")
                .ListItemForcedComma("3 by 3")
                .Build();
            Assert.Equal("grid, 3 by 3", msg);
        }

        [Fact]
        public void NullAndEmptyFragmentsAreIgnored()
        {
            string msg = new MessageBuilder()
                .Fragment("a")
                .Fragment(null)
                .Fragment("")
                .Fragment("b")
                .Build();
            Assert.Equal("a b", msg);
        }

        [Fact]
        public void EmptyBuilderBuildsNull()
        {
            Assert.Null(new MessageBuilder().Build());
        }

        [Fact]
        public void ReuseAfterBuildThrows()
        {
            var b = new MessageBuilder().Fragment("x");
            b.Build();
            Assert.Throws<InvalidOperationException>(() => b.Fragment("y"));
        }

        [Fact]
        public void ExplicitSpaceFragmentThrows()
        {
            Assert.Throws<ArgumentException>(() => new MessageBuilder().Fragment(" "));
        }
    }
}
