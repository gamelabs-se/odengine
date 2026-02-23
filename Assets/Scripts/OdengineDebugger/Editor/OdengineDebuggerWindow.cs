using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Odengine.Core;
using Odengine.Fields;

namespace OdengineDebugger.Editor
{
    /// <summary>
    /// Odengine Field Debugger — live view of Dimension state.
    ///
    /// Open via: Odengine → Field Debugger   (shortcut: Ctrl+Alt+D / Cmd+Alt+D)
    ///
    /// Layout
    /// ┌────────────────────────────────────────────────────┐
    /// │ Toolbar: status | Heat Map / Graph toggle          │
    /// ├──────────────┬─────────────────────────────────────┤
    /// │ Field list   │  Heat map or Node graph             │
    /// ├──────────────┴─────────────────────────────────────┤
    /// │ Pinned time series (shown only when channels pinned)│
    /// └────────────────────────────────────────────────────┘
    ///
    /// Heat map:  click a cell to pin that (nodeId × channelId) to the time series.
    /// Graph:     drag nodes to reposition; fill color = dominant logAmp channel.
    ///
    /// This window is view-only. It reads DimensionProvider.Current — the game layer
    /// writes there. Odengine core has no knowledge of this window.
    /// </summary>
    public sealed class OdengineDebuggerWindow : EditorWindow
    {
        // ── View mode ─────────────────────────────────────────────────────────

        private enum ViewMode { HeatMap, Graph }
        private ViewMode _viewMode = ViewMode.HeatMap;

        // ── State ─────────────────────────────────────────────────────────────

        private string    _selectedFieldId;
        private Vector2   _fieldListScroll;
        private Texture2D _heatMapTex;

        private readonly NodeGraphView _graphView = new();

        // Pinned channels → time series buffers
        // key = "fieldId\0nodeId\0channelId"
        private readonly Dictionary<string, TimeSeriesBuffer> _pinned = new();
        private Vector2 _pinnedScroll;
        private readonly float[] _scratch = new float[TimeSeriesCapacity];

        // Hover tooltip state (heat map)
        private string _hoverInfo;

        // ── Constants ─────────────────────────────────────────────────────────

        private const int   TimeSeriesCapacity = 200;
        private const float SidebarWidth       = 165f;
        private const float PinnedPanelHeight  = 130f;
        private const float ToolbarHeight      = 21f;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        [MenuItem("Odengine/Field Debugger %&d")]
        public static void Open() =>
            GetWindow<OdengineDebuggerWindow>("Odengine Debugger");

        private void OnEnable()
        {
            DimensionProvider.OnTick += HandleTick;
            minSize = new Vector2(620f, 420f);
            _graphView.OnNodeClicked = OnNodeClicked;
        }

        private void OnDisable()
        {
            DimensionProvider.OnTick -= HandleTick;
            DestroyHeatMapTex();
        }

        // Called from game thread after each tick
        private void HandleTick(Dimension dim, ulong tick)
        {
            // Push pinned values on the main editor thread (safe for Unity objects)
            EditorApplication.delayCall += () => PushPinnedValues(dim);
            Repaint();
        }

        // ── OnGUI ─────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            var dim = DimensionProvider.Current;

            // Auto-select first field when nothing is selected
            if (_selectedFieldId == null && dim?.Fields.Count > 0)
                _selectedFieldId = dim.Fields.Keys.OrderBy(k => k, StringComparer.Ordinal).First();

            DrawToolbar(dim);

            float usedHeight = ToolbarHeight + (_pinned.Count > 0 ? PinnedPanelHeight : 0f);
            var mainRect = new Rect(0, ToolbarHeight, position.width, position.height - usedHeight);
            DrawMain(mainRect, dim);

            if (_pinned.Count > 0)
                DrawPinnedPanel(new Rect(0, mainRect.yMax, position.width, PinnedPanelHeight), dim);
        }

        // ── Toolbar ───────────────────────────────────────────────────────────

        private void DrawToolbar(Dimension dim)
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            // Status indicator
            bool live       = dim != null;
            var  statusText = live
                ? $"● Live — Tick {DimensionProvider.CurrentTick}  |  "
                  + $"{dim.Fields.Count} field(s)  {dim.Graph.Nodes.Count} node(s)"
                : "○ No dimension registered";

            var statusStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                normal   = { textColor = live ? new Color(0.35f, 1f, 0.45f) : new Color(0.6f, 0.6f, 0.6f) }
            };
            GUILayout.Label(statusText, statusStyle);

            GUILayout.FlexibleSpace();

            // View toggles
            EditorGUI.BeginChangeCheck();
            bool heatMap = GUILayout.Toggle(_viewMode == ViewMode.HeatMap, "Heat Map", EditorStyles.toolbarButton, GUILayout.Width(72));
            bool graph   = GUILayout.Toggle(_viewMode == ViewMode.Graph,   "Graph",    EditorStyles.toolbarButton, GUILayout.Width(52));
            if (EditorGUI.EndChangeCheck())
            {
                if (heatMap) _viewMode = ViewMode.HeatMap;
                if (graph)   _viewMode = ViewMode.Graph;
            }

            GUILayout.Space(4);
            GUILayout.EndHorizontal();
        }

        // ── Main area ─────────────────────────────────────────────────────────

        private void DrawMain(Rect rect, Dimension dim)
        {
            // Vertical separator between sidebar and content
            EditorGUI.DrawRect(new Rect(SidebarWidth, rect.y, 1f, rect.height), new Color(0.08f, 0.08f, 0.10f));

            DrawFieldSidebar(new Rect(rect.x, rect.y, SidebarWidth, rect.height), dim);

            var content = new Rect(SidebarWidth + 1f, rect.y, rect.width - SidebarWidth - 1f, rect.height);
            if (_viewMode == ViewMode.HeatMap) DrawHeatMap(content, dim);
            else                               DrawGraph(content, dim);
        }

        // ── Field sidebar ─────────────────────────────────────────────────────

        private void DrawFieldSidebar(Rect rect, Dimension dim)
        {
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.17f, 0.19f));

            GUILayout.BeginArea(rect);
            GUILayout.Label("Fields", EditorStyles.boldLabel);

            _fieldListScroll = GUILayout.BeginScrollView(_fieldListScroll, GUIStyle.none, GUI.skin.verticalScrollbar);

            if (dim != null)
            {
                foreach (var fieldId in dim.Fields.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    bool selected = fieldId == _selectedFieldId;
                    var  bg       = new Rect(0, GUILayoutUtility.GetLastRect().yMax, rect.width, 20f);

                    var style = new GUIStyle(EditorStyles.label)
                    {
                        padding = new RectOffset(8, 0, 2, 2),
                        normal  = { textColor = selected ? new Color(1f, 0.75f, 0.28f) : Color.white }
                    };

                    if (selected)
                        EditorGUI.DrawRect(bg, new Color(0.22f, 0.24f, 0.28f));

                    if (GUILayout.Button(fieldId, style))
                    {
                        if (_selectedFieldId != fieldId)
                        {
                            _selectedFieldId = fieldId;
                            DestroyHeatMapTex(); // force rebuild for new field
                        }
                    }
                }
            }
            else
            {
                GUILayout.Label("—", EditorStyles.centeredGreyMiniLabel);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ── Heat Map ──────────────────────────────────────────────────────────

        private void DrawHeatMap(Rect rect, Dimension dim)
        {
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.13f, 0.15f));

            if (dim == null)         { DrawCentered(rect, "No dimension registered");      return; }
            if (_selectedFieldId == null) { DrawCentered(rect, "Select a field →");         return; }

            var field = dim.GetField(_selectedFieldId);
            if (field == null) { _selectedFieldId = null; return; }

            var nodeIds    = dim.Graph.GetNodeIdsSorted();
            var channelIds = field.GetActiveChannelIdsSorted();

            if (nodeIds.Count == 0 || channelIds.Count == 0)
            {
                DrawCentered(rect, "No active field entries");
                return;
            }

            // Rebuild texture on repaint
            if (Event.current.type == EventType.Repaint)
                _heatMapTex = FieldHeatMapDrawer.Render(field, nodeIds, channelIds, _heatMapTex);

            // ── Layout ────────────────────────────────────────────────────────
            const float headerH  = 20f;
            const float legendH  = 18f;
            const float rowLabelW = 90f;
            const float minCell  = 16f;

            float bodyW = rect.width  - rowLabelW;
            float bodyH = rect.height - headerH - legendH - 4f;

            float cellW = Mathf.Max(minCell, bodyW  / channelIds.Count);
            float cellH = Mathf.Max(minCell, bodyH  / nodeIds.Count);

            float texW = cellW * channelIds.Count;
            float texH = cellH * nodeIds.Count;

            float texX = rect.x + rowLabelW;
            float texY = rect.y + headerH;

            // ── Channel headers ───────────────────────────────────────────────
            for (int c = 0; c < channelIds.Count; c++)
            {
                GUI.Label(
                    new Rect(texX + c * cellW, rect.y, cellW, headerH),
                    channelIds[c], ColHeaderStyle);
            }

            // ── Node row labels ───────────────────────────────────────────────
            for (int r = 0; r < nodeIds.Count; r++)
            {
                GUI.Label(
                    new Rect(rect.x, texY + r * cellH, rowLabelW - 4f, cellH),
                    nodeIds[r], RowLabelStyle);
            }

            // ── Texture ───────────────────────────────────────────────────────
            var texRect = new Rect(texX, texY, texW, texH);
            if (_heatMapTex != null)
                GUI.DrawTexture(texRect, _heatMapTex, ScaleMode.StretchToFill);

            // ── Mouse interaction ─────────────────────────────────────────────
            var e = Event.current;
            _hoverInfo = null;

            if (texRect.Contains(e.mousePosition))
            {
                int col = Mathf.Clamp((int)((e.mousePosition.x - texRect.x) / cellW), 0, channelIds.Count - 1);
                int row = Mathf.Clamp((int)((e.mousePosition.y - texRect.y) / cellH), 0, nodeIds.Count  - 1);

                float logAmp = field.GetLogAmp(nodeIds[row], channelIds[col]);
                float mult   = MathF.Exp(logAmp);
                _hoverInfo   = $"{nodeIds[row]}  ×  {channelIds[col]}" +
                               $"\nlogAmp = {logAmp:+0.0000;-0.0000;0}   mult = {mult:F4}";

                if (e.type == EventType.MouseMove) Repaint();

                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    TogglePin(_selectedFieldId, nodeIds[row], channelIds[col]);
                    e.Use();
                }
            }

            // Hover tooltip box
            if (_hoverInfo != null)
            {
                var tip = new Rect(e.mousePosition.x + 14f, e.mousePosition.y - 34f, 290f, 42f);
                // Keep inside window
                if (tip.xMax > position.width)  tip.x = e.mousePosition.x - tip.width - 4f;
                if (tip.yMax > position.height) tip.y = e.mousePosition.y - tip.height - 4f;

                EditorGUI.DrawRect(tip, new Color(0.05f, 0.05f, 0.08f, 0.96f));
                GUI.Label(tip, "  " + _hoverInfo.Replace("\n", "\n  "), TooltipStyle);
            }

            // ── Legend ────────────────────────────────────────────────────────
            DrawLegend(new Rect(rect.x + rowLabelW, texY + texH + 4f, bodyW, legendH - 4f));
        }

        // ── Graph ─────────────────────────────────────────────────────────────

        private void DrawGraph(Rect rect, Dimension dim)
        {
            if (dim == null) { DrawCentered(rect, "No dimension registered"); return; }

            var field = _selectedFieldId != null ? dim.GetField(_selectedFieldId) : null;
            _graphView.Draw(rect, dim.Graph, field);
        }

        private void OnNodeClicked(string nodeId)
        {
            // Future: open node detail popover
        }

        // ── Pinned time series ────────────────────────────────────────────────

        private void PushPinnedValues(Dimension dim)
        {
            if (dim == null || _pinned.Count == 0) return;

            foreach (var key in _pinned.Keys.ToList())
            {
                ParsePinKey(key, out var fId, out var nId, out var cId);
                float v = dim.GetField(fId)?.GetLogAmp(nId, cId) ?? 0f;
                _pinned[key].Push(v);
            }
        }

        private void DrawPinnedPanel(Rect rect, Dimension dim)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), new Color(0.08f, 0.08f, 0.10f));
            EditorGUI.DrawRect(rect, new Color(0.14f, 0.15f, 0.17f));

            GUILayout.BeginArea(rect);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Pinned channels", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(42)))
                _pinned.Clear();
            GUILayout.Space(4);
            GUILayout.EndHorizontal();

            _pinnedScroll = GUILayout.BeginScrollView(_pinnedScroll, GUILayout.ExpandHeight(true));

            string toRemove = null;
            foreach (var kvp in _pinned)
            {
                ParsePinKey(kvp.Key, out var fId, out var nId, out var cId);

                GUILayout.BeginHorizontal();

                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(18))) { toRemove = kvp.Key; }

                GUILayout.Label($"{fId}  /  {nId}  /  {cId}", PinLabelStyle, GUILayout.Width(310));

                var sparkRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                    GUILayout.Width(160), GUILayout.Height(22));
                DrawSparkline(sparkRect, kvp.Value);

                float latest = kvp.Value.Latest;
                GUILayout.Label($"{latest:+0.0000;-0.0000;0}", MonoStyle, GUILayout.Width(80));

                GUILayout.EndHorizontal();
            }

            if (toRemove != null) _pinned.Remove(toRemove);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawSparkline(Rect rect, TimeSeriesBuffer buf)
        {
            EditorGUI.DrawRect(rect, new Color(0.09f, 0.09f, 0.11f));

            buf.CopyTo(_scratch);

            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < TimeSeriesCapacity; i++)
            {
                if (_scratch[i] < min) min = _scratch[i];
                if (_scratch[i] > max) max = _scratch[i];
            }

            float range = max - min;
            if (range < 1e-5f) { min -= 0.5f; range = 1f; }

            Handles.BeginGUI();

            // Zero line
            float zeroT = Mathf.Clamp01((0f - min) / range);
            float zeroY = rect.y + rect.height * (1f - zeroT);
            Handles.color = new Color(0.5f, 0.5f, 0.5f, 0.25f);
            Handles.DrawLine(new Vector3(rect.x, zeroY), new Vector3(rect.xMax, zeroY));

            // Data line
            var pts = new Vector3[TimeSeriesCapacity];
            for (int i = 0; i < TimeSeriesCapacity; i++)
            {
                float x = rect.x + rect.width  * i / (TimeSeriesCapacity - 1);
                float t = Mathf.Clamp01((_scratch[i] - min) / range);
                float y = rect.y + rect.height * (1f - t);
                pts[i]  = new Vector3(x, y);
            }

            Handles.color = new Color(0.38f, 0.76f, 1f);
            Handles.DrawAAPolyLine(1.5f, pts);

            Handles.EndGUI();
        }

        // ── Legend ────────────────────────────────────────────────────────────

        private static void DrawLegend(Rect rect)
        {
            int steps = Mathf.Max(2, (int)rect.width);
            for (int i = 0; i < steps; i++)
            {
                float t      = i / (float)(steps - 1); // 0 = min, 1 = max
                float logAmp = (t - 0.5f) * 6f;        // maps to [-3, +3]
                EditorGUI.DrawRect(new Rect(rect.x + i, rect.y, 1f, rect.height - 10f),
                    FieldHeatMapDrawer.LogAmpToColor(logAmp));
            }

            float labelY = rect.y + rect.height - 10f;
            GUI.Label(new Rect(rect.x,                    labelY, 30f, 10f), "−3", LegendLabelStyle);
            GUI.Label(new Rect(rect.x + rect.width * 0.5f - 5f, labelY, 20f, 10f), "0",  LegendLabelStyle);
            GUI.Label(new Rect(rect.xMax - 20f,            labelY, 20f, 10f), "+3", LegendLabelStyle);
        }

        // ── Pin helpers ───────────────────────────────────────────────────────

        private void TogglePin(string fieldId, string nodeId, string channelId)
        {
            string key = PinKey(fieldId, nodeId, channelId);
            if (_pinned.ContainsKey(key)) _pinned.Remove(key);
            else                          _pinned[key] = new TimeSeriesBuffer(TimeSeriesCapacity);
        }

        private static string PinKey(string f, string n, string c)   => $"{f}\0{n}\0{c}";
        private static void   ParsePinKey(string key, out string f, out string n, out string c)
        {
            var p = key.Split('\0');
            f = p[0]; n = p[1]; c = p[2];
        }

        // ── Misc helpers ──────────────────────────────────────────────────────

        private void DestroyHeatMapTex()
        {
            if (_heatMapTex != null) { DestroyImmediate(_heatMapTex); _heatMapTex = null; }
        }

        private static void DrawCentered(Rect rect, string text) =>
            GUI.Label(rect, text, CenteredGreyStyle);

        // ── Styles (lazy, static) ─────────────────────────────────────────────

        private static GUIStyle _colHeaderStyle;
        private static GUIStyle ColHeaderStyle => _colHeaderStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.LowerLeft,
            fontSize  = 9,
            normal    = { textColor = new Color(0.70f, 0.72f, 0.75f) }
        };

        private static GUIStyle _rowLabelStyle;
        private static GUIStyle RowLabelStyle => _rowLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleRight,
            fontSize  = 9,
            normal    = { textColor = new Color(0.70f, 0.72f, 0.75f) }
        };

        private static GUIStyle _tooltipStyle;
        private static GUIStyle TooltipStyle => _tooltipStyle ??= new GUIStyle(EditorStyles.label)
        {
            fontSize  = 10,
            richText  = true,
            normal    = { textColor = new Color(0.88f, 0.90f, 0.93f) }
        };

        private static GUIStyle _pinLabelStyle;
        private static GUIStyle PinLabelStyle => _pinLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = new Color(0.78f, 0.80f, 0.85f) }
        };

        private static GUIStyle _monoStyle;
        private static GUIStyle MonoStyle => _monoStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            font      = EditorStyles.boldFont,
            alignment = TextAnchor.MiddleRight,
            normal    = { textColor = new Color(0.85f, 0.85f, 0.60f) }
        };

        private static GUIStyle _legendLabelStyle;
        private static GUIStyle LegendLabelStyle => _legendLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            fontSize  = 8,
            normal    = { textColor = new Color(0.60f, 0.62f, 0.65f) }
        };

        private static GUIStyle _centeredGreyStyle;
        private static GUIStyle CenteredGreyStyle => _centeredGreyStyle ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel)
        {
            fontSize  = 13,
            alignment = TextAnchor.MiddleCenter
        };
    }
}
