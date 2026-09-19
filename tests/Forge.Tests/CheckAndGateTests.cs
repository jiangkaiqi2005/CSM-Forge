using System;
using CsmForge.Core;

namespace CsmForge.Tests
{
    /// <summary>WP-2.2 contract: Check.* helpers preserve the exact exception types of the inline
    /// guards they replaced, and GateCounter provides saturating per-category gate evidence.</summary>
    public static class CheckAndGateTests
    {
        [Case] public static void NotNullThrowsArgumentNullWithParameterName()
        {
            object value = null;
            try
            {
                Check.NotNull(value, "value");
                throw new Exception("Expected ArgumentNullException.");
            }
            catch (ArgumentNullException thrown)
            {
                Assert.Equal("value", thrown.ParamName);
            }
        }

        [Case] public static void NotNullPassesNonNull()
        {
            Check.NotNull(new object(), "value");
            Check.NotNull("x", "value");
        }

        [Case] public static void ConditionThrowsArgumentErrorWithMessage()
        {
            // WP-2 contract: the argument is the VIOLATION condition (true = the guard trips).
            try
            {
                Check.Condition(true, "port", "端口必须是 1–65535。");
                throw new Exception("Expected ArgumentException.");
            }
            catch (ArgumentException thrown)
            {
                // ArgumentException.Message embeds the parameter name after the custom message.
                Assert.True(thrown.Message.StartsWith("端口必须是 1–65535。", StringComparison.Ordinal));
                Assert.Equal("port", thrown.ParamName);
            }
            Check.Condition(false, "port", "unused");
        }

        [Case] public static void CanonicalIdAcceptsLowercaseAsciiAndRejectsTheRest()
        {
            Check.CanonicalId("mod:1234:net_manager", 128, "id", "Invalid component identity.");
            Check.CanonicalId("a-b_c.d:e", 128, "id", "Invalid component identity.");
            Assert.Throws<ArgumentException>(delegate { Check.CanonicalId("Bad-Uppercase", 128, "id", "Invalid component identity."); });
            Assert.Throws<ArgumentException>(delegate { Check.CanonicalId("空格 inside", 128, "id", "Invalid component identity."); });
            Assert.Throws<ArgumentException>(delegate { Check.CanonicalId(new string('a', 129), 128, "id", "Invalid component identity."); });
            Assert.Throws<ArgumentException>(delegate { Check.CanonicalId(null, 128, "id", "Invalid component identity."); });
        }

        [Case] public static void GateCounterCountsPerCategoryAndResets()
        {
            GateCounter.Reset();
            GateCounter.Record(GateCategory.Wire);
            GateCounter.Record(GateCategory.Wire);
            GateCounter.Record(GateCategory.Invariant);
            Assert.Equal(2L, GateCounter.Count(GateCategory.Wire));
            Assert.Equal(1L, GateCounter.Count(GateCategory.Invariant));
            Assert.Equal(0L, GateCounter.Count(GateCategory.Thread));
            GateCounter.Reset();
            Assert.Equal(0L, GateCounter.Count(GateCategory.Wire));
        }

        [Case] public static void GateCounterIgnoresUndefinedCategories()
        {
            GateCounter.Reset();
            GateCounter.Record((GateCategory)99);
            GateCounter.Record((GateCategory)(-1));
            Assert.Equal(0L, GateCounter.Count((GateCategory)99));
        }

        [Case] public static void OutOfRangeThrowsWithParameterNameOnViolation()
        {
            // WP-2 contract: violation-style, same ArgumentOutOfRangeException(name) as inline.
            try
            {
                Check.OutOfRange(true, "capacity");
                throw new Exception("Expected ArgumentOutOfRangeException.");
            }
            catch (ArgumentOutOfRangeException thrown)
            {
                Assert.Equal("capacity", thrown.ParamName);
            }
            Check.OutOfRange(false, "capacity");
        }

        [Case] public static void InRangeThrowsOnlyOutsideBounds()
        {
            Check.InRange(5, 1, 8, "value");
            Assert.Throws<ArgumentOutOfRangeException>(delegate { Check.InRange(9, 1, 8, "value"); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate { Check.InRange(0, 1, 8, "value"); });
        }
    }
}
