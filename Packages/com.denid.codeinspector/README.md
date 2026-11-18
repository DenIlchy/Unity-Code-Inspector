# Code Inspector

Code Inspector is a Unity 6 editor tool that maps your scripts and scenes as an
interactive graph. It draws how scripts connect — field references, method
calls, `GetComponent` lookups, inheritance, UnityEvents, triggers and C# event
subscriptions — and lets you watch live runtime activity and tweak parameters
directly on the graph.

- **Architecture view** — one node per script type; edges show static
  relationships discovered by reflection + IL analysis.
- **Scene view** — one node per GameObject/asset; edges show concrete
  references, UnityEvent bindings and possible message calls.
- **Live view** — in Play Mode nodes pulse on value changes, UnityEvent edges
  flash with fire counters, and trigger/collision contacts appear as transient
  edges.
- **Parameter tweaking** — edit any component's inspector in the side panel.
  Edit Mode changes use normal Undo; Play Mode changes are logged and can be
  kept or reverted on exit.

## Requirements

- Unity **6000.0** or newer
- No third-party dependencies; UI Toolkit only (no experimental graph APIs)

## Install

Package Manager → `+` → **Add package from git URL…**:

```
https://github.com/DenIlchy/Unity-Code-Inspector.git?path=/Packages/com.denid.codeinspector
```

Pin a version:

```
https://github.com/DenIlchy/Unity-Code-Inspector.git?path=/Packages/com.denid.codeinspector#v0.1.0
```

Or manual: copy `Packages/com.denid.codeinspector` into your project's
`Packages/` folder.

## Quick start

1. Open **Window > Code Inspector**.
2. Import the demo: Package Manager → Code Inspector → **Samples** → *Demo* →
   Import, then open `Assets/Samples/Code Inspector/0.1.0/Demo/Demo.unity`.
3. Switch between **Architecture** and **Scene** with the toolbar toggles.

## Views

**Architecture** scans your assemblies (`TypeScanner` + raw IL decoding) and
shows type-level connections. **Scene** scans open scenes and shows concrete
object-level connections.

| Edge kind | Meaning | How it's detected |
|---|---|---|
| FieldReference | Field of one component references another object | Serialized field scan / IL field refs |
| MethodCall | Direct method call | IL call/callvirt instructions |
| GetComponent | `GetComponent`-style lookup | IL analysis |
| Inheritance | Base type / interface | Reflection |
| TypeUsage | Type used in signature | Reflection |
| UnityEventBinding | Serialized UnityEvent listener | SerializedObject property scan |
| CSharpEventSubscription | `+=`/`-=` on a C# event | IL add/remove event analysis |
| UnityMessage | Unity message (e.g. `OnTriggerEnter`) handled | IL + conventions |
| SendMessage | `SendMessage`/`BroadcastMessage` call | IL string-literal analysis |
| FieldAccess | Reads/writes a field on another component | IL field access |

Dashed edges are **approximate**: the connection is likely but not guaranteed
(reflection, `SendMessage`, singleton access like `GameManager.Instance`,
virtual dispatch to unknown implementers, "possible call" heuristics).

## Badges

Component badges shown on nodes:

| Badge | Meaning |
|---|---|
| Update / FixedUpdate / LateUpdate | Implements that Unity message |
| Trigger | Implements `OnTrigger*` handlers |
| Collision | Implements `OnCollision*` handlers |
| Mouse | Implements `OnMouse*` handlers |
| HasEvents | Has serialized UnityEvent fields |
| TriggerCollider | Has a trigger collider |
| Rigidbody | Has a Rigidbody |
| BrokenEvent | UnityEvent references a missing/mismatched method |

## Tweaking

Select a node → the **Parameters** section at the top of the side panel shows
one foldout per component with the full Inspector (custom editors, property
drawers, prefab overrides) via `InspectorElement`. "Only my scripts" hides
non-user components. Architecture script nodes offer an "Edit instance"
dropdown.

- **Edit Mode**: normal Unity editing — Undo/Redo, scene dirty, prefab
  overrides all work as usual.
- **Play Mode**: edits are logged (`TweakLog`). On exit the keep-changes dialog
  lists every tweak with original → final values: keep selected, discard all,
  or revert individually. Preference (**⋮ menu**): *Ask* (default),
  *Always Discard*, *Always Keep*. Asset edits persist in Play Mode and can be
  reverted explicitly.

## Live view

While playing (toolbar shows `● LIVE`):

- Nodes pulse when watched serialized values change; the side panel lists
  "Live values" (`field old → new · t s ago`) and "Events fired". A Console
  docked at the bottom (toolbar toggle) lists the selected GameObject's
  trigger/collision contacts; click an entry to select the other object.
- UnityEvent edges flash bright for ~600 ms with a `×N` fire counter at the
  edge midpoint (counters survive graph rebuilds).
- Trigger/collision contacts draw a transient flashing edge `other → self`
  (auto-removed after 2 s) or increment a contact counter on the node.
- Runtime-spawned objects appear via throttled rescans.

## Navigation, filters & selection

- **Pan**: drag empty space, or middle-drag.
- **Zoom**: mouse wheel.
- **Drag** nodes to arrange; layouts persist per project.
- **Double-click** a node to open its script (or ping the asset).
- Toolbar: **search** field, **Frame** button, edge-kind and category filter
  menus, **Focus** (neighborhood only), **Auto refresh**, **Sync selection**,
  **Show all**, and **⋮** for Play Mode tweak preference.

## Limitations

- Static analysis uses reflection + IL decoding: it finds **direct** calls
  only. Calls through reflection, `SendMessage` (resolved from string literals
  where possible), interfaces or virtual dispatch to unknown implementers are
  shown as **approximate** (dashed).
- C# events are shown as static edges but are **not** flashed live.
- Scene "possible call" heuristics need a single unambiguous host.
- Generic type definitions are skipped.
- Runtime probes are added only in Play Mode with `DontSave` hide flags — never
  saved into scenes.
- UnityEvent flashing requires a serialized `UnityEvent` field on the source
  component.

## Troubleshooting

- **Empty graph** — enable *Include package/editor scripts* in the Types menu
  if your scripts live in packages.
- **Diagnostics** — scanner warnings (missing scripts, truncated scans,
  inactive objects) appear in the status bar / diagnostics list.

## Screenshots

<!-- TODO: add screenshots (docs/images/) -->

## License

MIT — see [LICENSE.md](LICENSE.md).
