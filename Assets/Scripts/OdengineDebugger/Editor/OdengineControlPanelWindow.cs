using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Odengine.Core;
using OdengineDebugger;

namespace OdengineDebugger.Editor
{
    /// <summary>
    /// Odengine Control Panel — interactive sim controls as a standalone window.
    ///
    /// Open via: Odengine → Control Panel   (Ctrl+Alt+C / Cmd+Alt+C)
    ///
    /// Keep this open beside the Field Debugger. Clicking buttons/sliders fires
    /// directly into the running simulation; the Field Debugger repaints
    /// automatically after each tick so you see the effect immediately.
    /// </summary>
    public sealed class OdengineControlPanelWindow : EditorWindow
    {
        // ── Lifecycle ─────────────────────────────────────────────────────────

        [MenuItem("Odengine/Control Panel %&c")]
        public static void Open()
        {
            var win = GetWindow<OdengineControlPanelWindow>("Odengine Controls");
            win.minSize = new Vector2(300f, 360f);
        }

        private void OnEnable()  => DimensionProvider.OnTick += HandleTick;
        private void OnDisable() => DimensionProvider.OnTick -= HandleTick;

        private void HandleTick(Dimension _, ulong __) => Repaint();

        // ── State ─────────────────────────────────────────────────────────────

        // Keyed by "group:label"
        private readonly Dictionary<string, int>   _nodeIdx = new();
        private readonly Dictionary<string, float> _slider  = new();
        private Vector2 _scroll;

        // ── OnGUI ─────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            DrawToolbar();
            DrawControls(new Rect(0, ToolbarH, position.width, position.height - ToolbarH));
        }

        // ── Toolbar ───────────────────────────────────────────────────────────

        private const float ToolbarH = 21f;

        private void DrawToolbar()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            bool live = DimensionProvider.Current != null;
            var statusStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = live ? new Color(0.35f, 1f, 0.45f) : new Color(0.55f, 0.55f, 0.55f) }
            };
            GUILayout.Label(
                live ? $"● Live — Tick {DimensionProvider.CurrentTick}" : "○ No dimension",
                statusStyle);

            GUILayout.FlexibleSpace();

            int count = SimulationControls.All.Count;
            GUILayout.Label($"{count} control{(count == 1 ? "" : "s")}", EditorStyles.miniLabel);
            GUILayout.Space(4);

            GUILayout.EndHorizontal();
        }

        // ── Controls ──────────────────────────────────────────────────────────

        private void DrawControls(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.14f, 0.15f, 0.17f));

            var nodeIds = DimensionProvider.Current?.Graph.GetNodeIdsSorted()
                              .ToArray() ?? Array.Empty<string>();

            GUILayout.BeginArea(rect);
            _scroll = GUILayout.BeginScrollView(_scroll);

            if (SimulationControls.All.Count == 0)
            {
                GUILayout.Space(20);
                GUILayout.Label(
                    "No controls registered.\n\nPress Play and make sure a\nSimulationRunner is in the scene.",
                    EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                string currentGroup = null;

                foreach (var (group, ctrl) in SimulationControls.All)
                {
                    // ── Group header ──────────────────────────────────────────
                    if (group != currentGroup)
                    {
                        if (currentGroup != null) GUILayout.Space(8);

                        GUILayout.Label(group, GroupStyle);

                        var sep = GUILayoutUtility.GetRect(
                            GUIContent.none, GUIStyle.none,
                            GUILayout.ExpandWidth(true), GUILayout.Height(1));
                        EditorGUI.DrawRect(sep, new Color(0.28f, 0.30f, 0.35f));
                        GUILayout.Space(3);
                        currentGroup = group;
                    }

                    string key = $"{group}:{ctrl.Label}";

                    switch (ctrl)
                    {
                        // ── Button ────────────────────────────────────────────
                        case ButtonControl btn:
                            GUILayout.BeginHorizontal();
                            GUILayout.Space(10);
                            if (GUILayout.Button(btn.Label, GUILayout.Height(24)))
                                btn.OnClick();
                            GUILayout.Space(10);
                            GUILayout.EndHorizontal();
                            GUILayout.Space(2);
                            break;

                        // ── Node picker + button ──────────────────────────────
                        case NodeButtonControl nc:
                            if (!_nodeIdx.ContainsKey(key)) _nodeIdx[key] = 0;

                            GUILayout.BeginHorizontal();
                            GUILayout.Space(10);
                            GUILayout.Label(nc.Label, LabelStyle, GUILayout.Width(120));

                            if (nodeIds.Length > 0)
                                _nodeIdx[key] = EditorGUILayout.Popup(
                                    _nodeIdx[key], nodeIds, GUILayout.MinWidth(60), GUILayout.ExpandWidth(true));
                            else
                                GUILayout.Label("—", GUILayout.MinWidth(60));

                            GUILayout.Space(4);
                            using (new EditorGUI.DisabledScope(nodeIds.Length == 0))
                                if (GUILayout.Button(nc.Verb, GUILayout.Width(72), GUILayout.Height(20)))
                                    nc.OnClick(nodeIds[_nodeIdx[key]]);
                            GUILayout.Space(10);
                            GUILayout.EndHorizontal();
                            GUILayout.Space(2);
                            break;

                        // ── Node picker + slider + apply ──────────────────────
                        case NodeSliderControl ns:
                            if (!_nodeIdx.ContainsKey(key)) _nodeIdx[key] = 0;
                            if (!_slider.ContainsKey(key))  _slider[key]  = ns.Default;

                            // Row 1: label + node picker
                            GUILayout.BeginHorizontal();
                            GUILayout.Space(10);
                            GUILayout.Label(ns.Label, LabelStyle, GUILayout.Width(120));
                            if (nodeIds.Length > 0)
                                _nodeIdx[key] = EditorGUILayout.Popup(
                                    _nodeIdx[key], nodeIds, GUILayout.MinWidth(60), GUILayout.ExpandWidth(true));
                            else
                                GUILayout.Label("—", GUILayout.MinWidth(60));
                            GUILayout.Space(10);
                            GUILayout.EndHorizontal();

                            // Row 2: slider + value + Apply
                            GUILayout.BeginHorizontal();
                            GUILayout.Space(10);
                            _slider[key] = GUILayout.HorizontalSlider(
                                _slider[key], ns.Min, ns.Max,
                                GUILayout.ExpandWidth(true), GUILayout.Height(16));
                            GUILayout.Label(
                                $"{_slider[key]:F2} {ns.Unit}",
                                ValueStyle, GUILayout.Width(72));
                            using (new EditorGUI.DisabledScope(nodeIds.Length == 0))
                                if (GUILayout.Button("Apply", GUILayout.Width(52), GUILayout.Height(18)))
                                    ns.OnClick(nodeIds[_nodeIdx[key]], _slider[key]);
                            GUILayout.Space(10);
                            GUILayout.EndHorizontal();
                            GUILayout.Space(3);
                            break;

                        // ── Slider + set ──────────────────────────────────────
                        case SliderControl sc:
                            if (!_slider.ContainsKey(key)) _slider[key] = sc.Default;

                            GUILayout.BeginHorizontal();
                            GUILayout.Space(10);
                            GUILayout.Label(sc.Label, LabelStyle, GUILayout.Width(120));
                            _slider[key] = GUILayout.HorizontalSlider(
                                _slider[key], sc.Min, sc.Max,
                                GUILayout.ExpandWidth(true), GUILayout.Height(16));
                            GUILayout.Label(
                                $"{_slider[key]:F2} {sc.Unit}",
                                ValueStyle, GUILayout.Width(72));
                            if (GUILayout.Button("Set", GUILayout.Width(42), GUILayout.Height(18)))
                                sc.OnClick(_slider[key]);
                            GUILayout.Space(10);
                            GUILayout.EndHorizontal();
                            GUILayout.Space(3);
                            break;
                    }
                }

                GUILayout.Space(12);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ── Styles ────────────────────────────────────────────────────────────

        private static GUIStyle _groupStyle;
        private static GUIStyle GroupStyle => _groupStyle ??= new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 12,
            padding  = new RectOffset(10, 0, 10, 2),
            normal   = { textColor = new Color(0.86f, 0.89f, 0.96f) }
        };

        private static GUIStyle _labelStyle;
        private static GUIStyle LabelStyle => _labelStyle ??= new GUIStyle(EditorStyles.label)
        {
            fontSize = 10,
            normal   = { textColor = new Color(0.75f, 0.78f, 0.83f) }
        };

        private static GUIStyle _valueStyle;
        private static GUIStyle ValueStyle => _valueStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleRight,
            normal    = { textColor = new Color(0.88f, 0.88f, 0.55f) }
        };
    }
}
