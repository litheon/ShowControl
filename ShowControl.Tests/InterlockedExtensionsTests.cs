using ShowControl.Utilities;
using Xunit;

namespace ShowControl.Tests
{
    public class InterlockedExtensionsTests
    {
        [Fact]
        public void InterlockedIncrement_MidRange_Increments()
        {
            int value = 2;
            Assert.Equal(3, value.InterlockedIncrement(0, 5));
            Assert.Equal(3, value);
        }

        [Fact]
        public void InterlockedIncrement_AboveMax_WrapsToMin()
        {
            int value = 5;
            Assert.Equal(0, value.InterlockedIncrement(0, 5));
            Assert.Equal(0, value);
        }

        [Fact]
        public void InterlockedDecrement_MidRange_Decrements()
        {
            int value = 3;
            Assert.Equal(2, value.InterlockedDecrement(0, 5));
            Assert.Equal(2, value);
        }

        [Fact]
        public void InterlockedDecrement_BelowMin_WrapsToMax()
        {
            int value = 0;
            Assert.Equal(5, value.InterlockedDecrement(0, 5));
            Assert.Equal(5, value);
        }

        [Fact]
        public void InterlockedGet_ReturnsCurrentValue()
        {
            int value = 42;
            Assert.Equal(42, value.InterlockedGet());
        }
    }
}
