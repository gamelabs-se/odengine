using System;
using System.Collections.Generic;

namespace OdengineDebugger
{
    // ── Control types ─────────────────────────────────────────────────────────

    public abstract class SimControl
    {
        public readonly string Label;
        protected SimControl(string label) => Label = label;
    }

    /// <summary>Simple push button.</summary>
    public sealed class ButtonControl : SimControl
    {
        public readonly Action OnClick;
        public ButtonControl(string label, Action onClick) : base(label) => OnClick = onClick;
    }

    /// <summary>Button with a node-ID dropdown picked from the live graph.</summary>
    public sealed class NodeButtonControl : SimControl
    {
        public readonly string Verb;           // shown on button
        public readonly Action<string> OnClick;
        public NodeButtonControl(string label, string verb, Action<string> onClick)
            : base(label) { Verb = verb; OnClick = onClick; }
    }

    /// <summary>Node-ID dropdown + float slider + button.</summary>
    public sealed class NodeSliderControl : SimControl
    {
        public readonly float Min, Max, Default;
        public readonly string Unit;
        public readonly Action<string, float> OnClick;
        public NodeSliderControl(string label, float min, float max, float @default,
            string unit, Action<string, float> onClick)
            : base(label) { Min = min; Max = max; Default = @default; Unit = unit; OnClick = onClick; }
    }

    /// <summary>Float slider + button, no node picker.</summary>
    public sealed class SliderControl : SimControl
    {
        public readonly float Min, Max, Default;
        public readonly string Unit;
        public readonly Action<float> OnClick;
        public SliderControl(string label, float min, float max, float @default,
            string unit, Action<float> onClick)
            : base(label) { Min = min; Max = max; Default = @default; Unit = unit; OnClick = onClick; }
    }

    // ── Registry ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Static bridge between a running simulation and the Controls panel in the
    /// Odengine Field Debugger window.
    ///
    /// The game layer (SimulationRunner) registers named controls at startup.
    /// The editor window reads them and renders interactive IMGUI for each one.
    /// Odengine core has zero knowledge of this class.
    ///
    /// Usage:
    ///   SimulationControls.RegisterNodeSlider("War", "Strike", 0.1f, 3f, 0.5f, "logAmp",
    ///       (nodeId, value) => warSystem.Exposure.AddLogAmp(nodeId, "x", value));
    /// </summary>
    public static class SimulationControls
    {
        private static readonly List<(string group, SimControl control)> _all = new();

        /// <summary>All registered controls in registration order.</summary>
        public static IReadOnlyList<(string group, SimControl control)> All => _all;

        public static void RegisterButton(string group, string label, Action onClick)
            => _all.Add((group, new ButtonControl(label, onClick)));

        public static void RegisterNodeButton(string group, string label, string verb,
            Action<string> onClick)
            => _all.Add((group, new NodeButtonControl(label, verb, onClick)));

        public static void RegisterNodeSlider(string group, string label,
            float min, float max, float @default, string unit,
            Action<string, float> onClick)
            => _all.Add((group, new NodeSliderControl(label, min, max, @default, unit, onClick)));

        public static void RegisterSlider(string group, string label,
            float min, float max, float @default, string unit,
            Action<float> onClick)
            => _all.Add((group, new SliderControl(label, min, max, @default, unit, onClick)));

        /// <summary>Remove all registrations. Call before re-registering after a reset.</summary>
        public static void Clear() => _all.Clear();
    }
}
