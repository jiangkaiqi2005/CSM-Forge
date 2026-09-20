using System;
using CsmForge.Core;

namespace CsmForge.Tests
{
    /// <summary>
    /// Regression for the "cannot pause after a fence" bug. The pause/speed Harmony prefixes used
    /// to allow-list only Offline/StartingHost/ConnectingClient and intercept only Hosting/
    /// ClientLive - so Faulted and ClientCatchingUp fell into a branch that suppressed the vanilla
    /// setter without queueing anything, permanently swallowing pause input.
    ///
    /// The runtime policy type lives in the Cities1 assembly (no game assembly here), so this test
    /// pins the same mode table against the Core enum and documents the required behaviour.
    /// </summary>
    public static class SimulationClockModeTests
    {

        [Case] public static void OnlyAnActiveSessionRoutesClockToTheHost()
        {
            Assert.True(SimulationClockRouting.RoutesToHost(MultiplayerSessionMode.Hosting));
            Assert.True(SimulationClockRouting.RoutesToHost(MultiplayerSessionMode.ClientLive));
        }

        [Case] public static void FaultedAndCatchingUpMustApplyLocally()
        {
            // The bug: both of these were neither allow-listed nor intercepted, so the setter was
            // suppressed with nothing queued - the game could never be paused again.
            Assert.True(!SimulationClockRouting.RoutesToHost(MultiplayerSessionMode.Faulted));
            Assert.True(!SimulationClockRouting.RoutesToHost(MultiplayerSessionMode.ClientCatchingUp));
        }

        [Case] public static void EveryNonActiveModeAppliesLocally()
        {
            MultiplayerSessionMode[] localModes =
            {
                MultiplayerSessionMode.Offline,
                MultiplayerSessionMode.StartingHost,
                MultiplayerSessionMode.ConnectingClient,
                MultiplayerSessionMode.ClientCatchingUp,
                MultiplayerSessionMode.Faulted
            };
            foreach (MultiplayerSessionMode mode in localModes)
                Assert.True(!SimulationClockRouting.RoutesToHost(mode));
        }

        [Case] public static void EveryDefinedModeIsClassified()
        {
            // exhaustiveness guard: adding a mode must be a deliberate decision, not a silent
            // fall-through into the suppressing branch
            foreach (MultiplayerSessionMode mode in
                Enum.GetValues(typeof(MultiplayerSessionMode)))
            {
                bool routed = SimulationClockRouting.RoutesToHost(mode);
                bool isActive = mode == MultiplayerSessionMode.Hosting ||
                    mode == MultiplayerSessionMode.ClientLive;
                Assert.True(routed == isActive);
            }
        }
    }
}
