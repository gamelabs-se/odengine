using System.Collections.Generic;
using UnityEngine;
using Odengine.Fields;

namespace OdengineDebugger.Editor
{
    /// <summary>
    /// Renders a ScalarField's logAmp values into a Texture2D heat map.
    ///
    /// Layout:  rows = nodeIds (top to bottom), columns = channelIds (left to right).
    /// Palette: blue (negative logAmp) → dark grey (neutral) → orange (positive logAmp).
    ///
    /// The returned Texture2D is owned by the caller; call DestroyImmediate when done.
    /// Pass the previous texture back on each call so it is reused/resized in place.
    /// </summary>
    internal static class FieldHeatMapDrawer
    {
        // Palette endpoints
        private static readonly Color NeutralColor  = new Color(0.18f, 0.19f, 0.21f);
        private static readonly Color NegativeColor = new Color(0.15f, 0.42f, 0.90f);
        private static readonly Color PositiveColor = new Color(0.95f, 0.45f, 0.10f);

        /// logAmp magnitude at which the palette reaches full saturation.
        private const float LogAmpRange = 3f;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Update or create a heat map texture for <paramref name="field"/>.
        /// </summary>
        /// <param name="field">Field to read.</param>
        /// <param name="nodeIds">Row labels — must not be empty.</param>
        /// <param name="channelIds">Column labels — must not be empty.</param>
        /// <param name="existing">Previous texture to reuse; may be null.</param>
        /// <returns>The updated (or newly created) texture.</returns>
        public static Texture2D Render(
            ScalarField              field,
            IReadOnlyList<string>    nodeIds,
            IReadOnlyList<string>    channelIds,
            Texture2D                existing)
        {
            int w = channelIds.Count;
            int h = nodeIds.Count;

            // Rebuild only when dimensions change
            if (existing == null || existing.width != w || existing.height != h)
            {
                if (existing != null) Object.DestroyImmediate(existing);
                existing = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode   = TextureWrapMode.Clamp,
                    name       = $"HeatMap_{field.FieldId}"
                };
            }

            var pixels = new Color[w * h];

            for (int row = 0; row < h; row++)
            {
                string nodeId = nodeIds[row];
                for (int col = 0; col < w; col++)
                {
                    float logAmp = field.GetLogAmp(nodeId, channelIds[col]);
                    // Unity Texture2D row 0 = bottom; flip so row 0 of our list is at the top
                    pixels[(h - 1 - row) * w + col] = LogAmpToColor(logAmp);
                }
            }

            existing.SetPixels(pixels);
            existing.Apply(updateMipmaps: false);
            return existing;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        internal static Color LogAmpToColor(float logAmp)
        {
            if (logAmp is > -1e-4f and < 1e-4f) return NeutralColor;

            float t = Mathf.Clamp01(Mathf.Abs(logAmp) / LogAmpRange);
            return logAmp < 0
                ? Color.Lerp(NeutralColor, NegativeColor, t)
                : Color.Lerp(NeutralColor, PositiveColor, t);
        }
    }
}
