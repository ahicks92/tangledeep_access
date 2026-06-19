using TangledeepAccess.Speech;
using Xunit;

namespace TangledeepAccess.Tests.Speech {
    public class ModStringsTests {
        // Pin the "JP" abbreviation (not "job points") and the one composition point it flows through,
        // so a future wording change is a deliberate edit here, not an accidental drift in an overlay.
        [Fact]
        public void JpUsesTheAbbreviation() {
            Assert.Equal("250 JP", ModStrings.Jp(250));
        }

        [Fact]
        public void CostsAndRemainingComposeThroughJp() {
            Assert.Equal("costs 75 JP", ModStrings.Costs(75));
            Assert.Equal("can master for 150 JP", ModStrings.CanMasterFor(150));
            Assert.Equal("175 JP remaining", ModStrings.JpRemaining(175));
        }
    }
}
