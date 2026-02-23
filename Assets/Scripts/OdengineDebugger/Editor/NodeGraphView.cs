using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Odengine.Fields;
using Odengine.Graph;

namespace OdengineDebugger.Editor
{
    /// <summary>
    /// Draws a NodeGraph as a draggable 2D visualization inside an EditorWindow panel.
    ///
    /// Features:
    ///   - Nodes arranged in a circle on first draw; drag to reposition.
    ///   - Edges drawn as anti-aliased lines; thickness ∝ 1/resistance.
    ///   - Edge tags shown as a small label at the midpoint on hover.
    ///   - Node fill color reflects the dominant logAmp channel in the selected field.
    ///   - Click a node to pin/unpin its info in the debug window.
    ///
    /// Ownership: one instance per EditorWindow. Call Reset() when the Dimension changes.
    /// </summary>
    internal sealed class NodeGraphView
    {
        // Node layout state — persists across repaints so the user can drag nodes
        private readonly Dictionary<string, Vector2> _positions = new();
        private string  _draggingNode;
        private Vector2 _dragOffset;

        // ── Palette ──────────────────────────────────────────────────────────

        private static readonly Color BgColor       = new(0.12f, 0.13f, 0.15f);
        private static readonly Color EdgeColor      = new(0.42f, 0.44f, 0.48f);
        private static readonly Color NodeFill       = new(0.26f, 0.30f, 0.38f);
        private static readonly Color NodeBorder     = new(0.50f, 0.55f, 0.65f);
        private static readonly Color NodeHover      = new(0.38f, 0.44f, 0.56f);
        private static readonly Color PositiveColor  = new(0.90f, 0.45f, 0.12f);
        private static readonly Color NegativeColor  = new(0.18f, 0.44f, 0.88f);

        private const float NodeRadius = 26f;

        // Callback raised when user clicks a node (nodeId)
        public System.Action<string> OnNodeClicked;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Draw the node graph inside <paramref name="rect"/> and handle input.
        /// Must be called from OnGUI.
        /// </summary>
        public void Draw(Rect rect, NodeGraph graph, ScalarField field)
        {
            if (graph == null) return;

            EditorGUI.DrawRect(rect, BgColor);

            var nodeIds = graph.GetNodeIdsSorted();
            EnsureLayout(nodeIds, rect);
            HandleInput(rect, nodeIds);

            if (Event.current.type == EventType.Repaint)
            {
                DrawEdges(graph, nodeIds);
                DrawNodes(graph, nodeIds, field);
            }
        }

        /// <summary>Clear cached positions. Call when the simulation resets.</summary>
        public void Reset() => _positions.Clear();

        // ── Layout ───────────────────────────────────────────────────────────

        private void EnsureLayout(IReadOnlyList<string> nodeIds, Rect rect)
        {
            var missing = nodeIds.Where(id => !_positions.ContainsKey(id)).ToList();
            if (missing.Count == 0) return;

            float cx = rect.x + rect.width  * 0.5f;
            float cy = rect.y + rect.height * 0.5f;
            float r  = Mathf.Min(rect.width, rect.height) * 0.36f;
            int   n  = nodeIds.Count;

            for (int i = 0; i < n; i++)
            {
                if (_positions.ContainsKey(nodeIds[i])) continue;
                float angle = 2f * Mathf.PI * i / Mathf.Max(1, n) - Mathf.PI * 0.5f;
                _positions[nodeIds[i]] = new Vector2(
                    cx + r * Mathf.Cos(angle),
                    cy + r * Mathf.Sin(angle));
            }
        }

        // ── Input ─────────────────────────────────────────────────────────────

        private void HandleInput(Rect rect, IReadOnlyList<string> nodeIds)
        {
            var e = Event.current;
            if (!rect.Contains(e.mousePosition)) return;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                foreach (var id in nodeIds)
                {
                    if (!_positions.TryGetValue(id, out var pos)) continue;
                    if (Vector2.Distance(e.mousePosition, pos) > NodeRadius) continue;

                    _draggingNode = id;
                    _dragOffset   = pos - e.mousePosition;
                    e.Use();
                    return;
                }
            }
            else if (e.type == EventType.MouseDrag && _draggingNode != null)
            {
                _positions[_draggingNode] = e.mousePosition + _dragOffset;
                GUI.changed = true;
                e.Use();
            }
            else if (e.type == EventType.MouseUp)
            {
                // Click (no drag) → fire callback
                if (_draggingNode != null && e.button == 0)
                {
                    var pos = _positions[_draggingNode];
                    if (Vector2.Distance(e.mousePosition + _dragOffset, pos + _dragOffset) < 3f)
                        OnNodeClicked?.Invoke(_draggingNode);
                }
                _draggingNode = null;
            }
        }

        // ── Drawing ───────────────────────────────────────────────────────────

        private void DrawEdges(NodeGraph graph, IReadOnlyList<string> nodeIds)
        {
            Handles.BeginGUI();

            foreach (var fromId in nodeIds)
            {
                if (!_positions.TryGetValue(fromId, out var fromPos)) continue;

                foreach (var edge in graph.GetOutEdgesSorted(fromId))
                {
                    if (!_positions.TryGetValue(edge.ToId, out var toPos)) continue;

                    float width = Mathf.Clamp(2.5f / Mathf.Max(0.01f, edge.Resistance), 0.5f, 5f);
                    Handles.color = EdgeColor;
                    Handles.DrawAAPolyLine(
                        width,
                        new Vector3(fromPos.x, fromPos.y),
                        new Vector3(toPos.x,   toPos.y));

                    DrawArrowhead(fromPos, toPos, EdgeColor);

                    // Tag label at midpoint
                    if (edge.Tags.Count > 0)
                    {
                        var mid = (fromPos + toPos) * 0.5f;
                        var tag = string.Join(",", edge.Tags.OrderBy(t => t));
                        GUI.Label(new Rect(mid.x + 3, mid.y - 7, 80, 14), tag, MiniLabelStyle);
                    }
                }
            }

            Handles.EndGUI();
        }

        private static void DrawArrowhead(Vector2 from, Vector2 to, Color color)
        {
            Vector2 dir  = (to - from).normalized;
            Vector2 tip  = to - dir * NodeRadius;
            Vector2 perp = new Vector2(-dir.y, dir.x) * 5f;

            Handles.color = color;
            Handles.DrawAAPolyLine(1.5f,
                new Vector3(tip.x, tip.y),
                new Vector3(tip.x - dir.x * 10f + perp.x, tip.y - dir.y * 10f + perp.y));
            Handles.DrawAAPolyLine(1.5f,
                new Vector3(tip.x, tip.y),
                new Vector3(tip.x - dir.x * 10f - perp.x, tip.y - dir.y * 10f - perp.y));
        }

        private void DrawNodes(NodeGraph graph, IReadOnlyList<string> nodeIds, ScalarField field)
        {
            var mousePos = Event.current.mousePosition;

            foreach (var id in nodeIds)
            {
                if (!_positions.TryGetValue(id, out var pos)) continue;

                bool hover = Vector2.Distance(mousePos, pos) <= NodeRadius;

                // Compute fill color from dominant channel in selected field
                Color fill = hover ? NodeHover : NodeFill;
                if (field != null)
                {
                    string dom = field.GetDominantChannel(id);
                    if (dom != null)
                    {
                        float logAmp = field.GetLogAmp(id, dom);
                        float t      = Mathf.Clamp01(Mathf.Abs(logAmp) / 3f);
                        var   tint   = logAmp >= 0f ? PositiveColor : NegativeColor;
                        fill = Color.Lerp(fill, tint, t * 0.75f);
                    }
                }

                Handles.BeginGUI();

                Handles.color = fill;
                Handles.DrawSolidDisc(new Vector3(pos.x, pos.y), Vector3.forward, NodeRadius);

                Handles.color = hover ? Color.white : NodeBorder;
                Handles.DrawWireDisc(new Vector3(pos.x, pos.y), Vector3.forward, NodeRadius);

                Handles.EndGUI();

                // Node label
                graph.Nodes.TryGetValue(id, out var node);
                string label = node?.Name ?? id;
                GUI.Label(
                    new Rect(pos.x - NodeRadius, pos.y - 8f, NodeRadius * 2f, 16f),
                    label,
                    NodeLabelStyle);

                // logAmp summary below the node (if field selected)
                if (field != null)
                {
                    string dom = field.GetDominantChannel(id);
                    if (dom != null)
                    {
                        float logAmp = field.GetLogAmp(id, dom);
                        string val   = $"{logAmp:+0.00;-0.00;0}";
                        GUI.Label(
                            new Rect(pos.x - NodeRadius, pos.y + NodeRadius + 2f, NodeRadius * 2f, 14f),
                            val,
                            MiniLabelStyle);
                    }
                }
            }
        }

        // ── Styles ────────────────────────────────────────────────────────────

        private static GUIStyle _nodeLabelStyle;
        private static GUIStyle NodeLabelStyle => _nodeLabelStyle ??= new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize  = 10,
            normal    = { textColor = Color.white }
        };

        private static GUIStyle _miniLabelStyle;
        private static GUIStyle MiniLabelStyle => _miniLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize  = 9,
            normal    = { textColor = new Color(0.7f, 0.7f, 0.7f) }
        };
    }
}
