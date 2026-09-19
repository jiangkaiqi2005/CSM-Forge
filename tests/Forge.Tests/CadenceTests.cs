using System;
using CsmForge.Core;

namespace CsmForge.Tests
{
    /// <summary>
    /// WP-1.1 contract for the grid-verification cadence: the expensive district/zone reconciles
    /// must run once per window (not per tick), must fire immediately after Force() (safe points
    /// and authority commits), and must consume the window when they fire.
    /// </summary>
    public static class CadenceTests
    {
        [Case] public static void CadenceDoesNotFireInsideWindow()
        {
            VerificationCadence cadence = new VerificationCadence(1000, 5000);
            Assert.True(!cadence.ShouldVerify(1000));
            Assert.True(!cadence.ShouldVerify(5000));
            Assert.True(!cadence.ShouldVerify(5999));
        }

        [Case] public static void CadenceFiresAtWindowEndAndResets()
        {
            VerificationCadence cadence = new VerificationCadence(1000, 5000);
            Assert.True(cadence.ShouldVerify(6000));
            Assert.True(!cadence.ShouldVerify(6001));
            Assert.True(!cadence.ShouldVerify(10999));
            Assert.True(cadence.ShouldVerify(11000));
            Assert.True(!cadence.ShouldVerify(11001));
        }

        [Case] public static void ForceFiresNextCheckRegardlessOfWindow()
        {
            VerificationCadence cadence = new VerificationCadence(1000, 5000);
            cadence.Force();
            Assert.True(cadence.ShouldVerify(1001));
        }

        [Case] public static void ForceIsConsumedByASingleFire()
        {
            VerificationCadence cadence = new VerificationCadence(1000, 5000);
            cadence.Force();
            cadence.Force();
            Assert.True(cadence.ShouldVerify(1001));
            Assert.True(!cadence.ShouldVerify(1002));
        }

        [Case] public static void ForceFollowedByWindowEndFiresOnceAndStartsFreshWindow()
        {
            VerificationCadence cadence = new VerificationCadence(1000, 5000);
            cadence.Force();
            Assert.True(cadence.ShouldVerify(7000));
            Assert.True(!cadence.ShouldVerify(7001));
            Assert.True(cadence.ShouldVerify(12000));
        }

        [Case] public static void CadenceRejectsNonMonotonicClock()
        {
            VerificationCadence cadence = new VerificationCadence(1000, 5000);
            cadence.Force();
            Assert.True(cadence.ShouldVerify(2000));
            Assert.Throws<ArgumentOutOfRangeException>(delegate { cadence.ShouldVerify(1999); });
        }

        [Case] public static void CadenceRejectsInvalidConstruction()
        {
            Assert.Throws<ArgumentOutOfRangeException>(delegate { new VerificationCadence(-1, 5000); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate { new VerificationCadence(0, 0); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate { new VerificationCadence(0, -5000); });
        }
    }
}
