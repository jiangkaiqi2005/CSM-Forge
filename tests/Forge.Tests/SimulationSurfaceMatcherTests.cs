using System;
using System.Collections.Generic;
using CsmForge.Core;

namespace CsmForge.Tests
{
    /// <summary>WP-3.1 contract: the presentation-only detector is deliberately one-sided —
    /// anything that names a manager/tool/simulation/AI type counts as simulation-touching
    /// (falls back to exact-match classification), pure UI naming never does.</summary>
    public static class SimulationSurfaceMatcherTests
    {
        [Case] public static void PureUiTypeNamesDoNotTouchSimulation()
        {
            Assert.True(!SimulationSurfaceMatcher.TouchesSimulationSurface(new[]
            {
                "FPSCameraMod", "CameraPatcher", "UIViewEx", "FreeCamera", "ScreenshotPanel"
            }));
        }

        [Case] public static void ManagerSuffixTriggersSimulationSurface()
        {
            Assert.True(SimulationSurfaceMatcher.TouchesSimulationSurface(new[] { "MyMod", "BuildingManager" }));
            Assert.True(SimulationSurfaceMatcher.TouchesSimulationSurface(new[] { "SimulationManager" }));
            Assert.True(SimulationSurfaceMatcher.TouchesSimulationSurface(new[] { "NetTool" }));
            Assert.True(SimulationSurfaceMatcher.TouchesSimulationSurface(new[] { "CarAI" }));
            Assert.True(SimulationSurfaceMatcher.TouchesSimulationSurface(new[] { "WaterSimulation" }));
        }

        [Case] public static void ModOwnControllerSuffixIsNotASimulationSignal()
        {
            // "Controller" is common in UI-layer naming and must not neuter the heuristic;
            // a UI controller that mutated simulation would still surface a Manager/Tool type.
            Assert.True(!SimulationSurfaceMatcher.TouchesSimulationSurface(new[] { "FreeCameraController", "PanelController" }));
        }

        [Case] public static void EmptyOrNullNameListFailsClosed()
        {
            Assert.True(SimulationSurfaceMatcher.TouchesSimulationSurface(null));
            Assert.True(!SimulationSurfaceMatcher.TouchesSimulationSurface(new string[0]));
            Assert.True(!SimulationSurfaceMatcher.TouchesSimulationSurface(new[] { "", "  " }));
        }
    }
}
