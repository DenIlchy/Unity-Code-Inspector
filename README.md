# Unity Code Inspector

*Made by **Denid** — [github.com/DenIlchy](https://github.com/DenIlchy)*

Code Inspector is a Unity 6 editor tool that maps your scripts and scenes as an
interactive graph — script connections, events, triggers, live runtime activity
and parameter tweaking — all inside the editor.

## Features

- **Architecture view** — one node per script type; edges show static
  relationships discovered by reflection + IL analysis.
- **Scene view** — one node per GameObject/asset; edges show concrete
  references, UnityEvent bindings and possible message calls in open scenes.
- **Live view** — in Play Mode nodes pulse on value changes, UnityEvent edges
  flash with fire counters, and trigger/collision contacts appear as transient
  edges.
- **Parameter tweaking** — edit any component's inspector in the side panel;
  Play Mode changes are logged and can be kept or reverted on exit.

## Requirements

- Unity **6000.0** or newer
- No third-party dependencies

## Installation

**a) Package Manager** — `+` → **Add package from git URL…**:

```
https://github.com/DenIlchy/Unity-Code-Inspector.git?path=/Packages/com.denid.codeinspector
```

Pin a version:

```
https://github.com/DenIlchy/Unity-Code-Inspector.git?path=/Packages/com.denid.codeinspector#v0.1.0
```

**b) Edit `Packages/manifest.json`** — add to `dependencies`:

```json
"com.denid.codeinspector": "https://github.com/DenIlchy/Unity-Code-Inspector.git?path=/Packages/com.denid.codeinspector"
```

**c) Manual** — copy the `Packages/com.denid.codeinspector` folder into your
project's `Packages/` folder.

## Getting started

1. Install the package (see [Installation](#installation)).
2. Open **Window > Code Inspector**.
3. Open any scene with your scripts.
4. Use the **Architecture** toggle for a type-level graph or **Scene** for
   objects in open scenes.
5. Select a node to see its connections and tweak its Parameters in the side
   panel.
6. Press Play to see the Live view in action.

See the [package README](Packages/com.denid.codeinspector/README.md) for full
documentation: views, edge kinds, badges, tweaking, live view and limitations.

## Repository layout

- `Packages/com.denid.codeinspector` — the package itself (UPM).
- `Assets/` — dev host project used for package development.

## Author

Made by **Denid** ([@DenIlchy](https://github.com/DenIlchy)). Issues and
suggestions: https://github.com/DenIlchy/Unity-Code-Inspector/issues

## License

MIT — see [Packages/com.denid.codeinspector/LICENSE.md](Packages/com.denid.codeinspector/LICENSE.md).
