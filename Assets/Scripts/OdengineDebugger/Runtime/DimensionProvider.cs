using System;
using Odengine.Core;

namespace OdengineDebugger
{
    /// <summary>
    /// Static bridge between a live simulation and the Odengine debugger editor window.
    ///
    /// Odengine core has zero knowledge of this class — it lives in a separate assembly.
    /// The game layer writes here; the EditorWindow reads here. Odengine stays agnostic.
    ///
    /// Usage (game bootstrap):
    ///   DimensionProvider.Register(dimension);
    ///   // after each tick:
    ///   DimensionProvider.NotifyTick(currentTick);
    ///   // on shutdown:
    ///   DimensionProvider.Unregister();
    /// </summary>
    public static class DimensionProvider
    {
        /// <summary>The currently registered Dimension, or null when not running.</summary>
        public static Dimension Current { get; private set; }

        /// <summary>Tick number from the last NotifyTick call.</summary>
        public static ulong CurrentTick { get; private set; }

        /// <summary>True when a Dimension is registered and the simulation is running.</summary>
        public static bool IsLive => Current != null;

        /// <summary>
        /// Raised after every <see cref="NotifyTick"/> call, and once (with null) on
        /// <see cref="Unregister"/>. Subscribers must be null-safe.
        /// Fires on the calling thread — editor subscribers should use
        /// EditorApplication.delayCall if they touch Unity objects.
        /// </summary>
        public static event Action<Dimension, ulong> OnTick;

        /// <summary>Register the active Dimension. Call once at sim start before the first tick.</summary>
        public static void Register(Dimension dimension)
        {
            Current     = dimension ?? throw new ArgumentNullException(nameof(dimension));
            CurrentTick = 0;
        }

        /// <summary>
        /// Notify the debugger that one tick completed.
        /// Call after each Propagator.Step / simulation update.
        /// </summary>
        public static void NotifyTick(ulong tick)
        {
            CurrentTick = tick;
            OnTick?.Invoke(Current, tick);
        }

        /// <summary>Unregister on sim teardown. The debugger window handles null gracefully.</summary>
        public static void Unregister()
        {
            Current     = null;
            CurrentTick = 0;
            OnTick?.Invoke(null, 0);
        }
    }
}
