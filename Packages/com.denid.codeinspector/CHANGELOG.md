# Changelog

All notable changes to Code Inspector are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [0.1.0] - Unreleased

### Added

- Package skeleton and `Window > Code Inspector` editor window.
- Architecture analysis core: `TypeScanner`, `ILCallScanner` and `GraphModel` producing a `CodeGraph` of script types and their relationships (field references, method calls, `GetComponent` lookups, inheritance, type usage, C# event subscriptions and `SendMessage` calls).
- Custom UI Toolkit graph canvas (no experimental graph APIs): node/edge rendering, pan/zoom, selection, double-click to open scripts, approximate (dashed) edges, and persistent layouts.
- Scene view: scans open scenes for GameObjects, components, asset references, UnityEvent bindings, triggers, possible calls, missing scripts and inactive objects.
- Filtering: search, edge-kind and category filters, focus mode, "show all", selection sync, and throttled auto-refresh.
- Parameters panel: per-component InspectorElement editing for selected nodes, with lazy foldouts, "only my scripts" filter and instance selection for script types.
- Change visualization: nodes pulse when their values change (including edits made in Unity's own Inspector).
- Live Play Mode view: live value tracking, UnityEvent edge flashing with fire counters, trigger/collision contact probes with transient edges, and `● LIVE` indicator.
- Play Mode tweak log: records Inspector edits during play and offers a keep-changes dialog on exit (Ask / Always Keep / Always Discard preference).

