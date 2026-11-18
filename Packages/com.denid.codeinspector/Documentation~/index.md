# Code Inspector

Code Inspector maps your scripts and scenes as an interactive graph: script
connections, events, triggers, live runtime activity and parameter tweaking.

Full documentation is in the package [README](../README.md) and online at
https://github.com/DenIlchy/Unity-Code-Inspector

## Quick reference

- Open **Window > Code Inspector**; switch between **Architecture** and
  **Scene** views in the toolbar.
- **Architecture** shows type-level connections discovered by reflection + IL
  analysis; **Scene** shows concrete GameObject/asset connections in open
  scenes. Dashed edges are approximate.
- Select a node to tweak component parameters in the **Parameters** panel
  (Edit Mode: normal Undo; Play Mode: logged, keep-changes dialog on exit).
- In Play Mode (`● LIVE`): nodes pulse on value changes, UnityEvent edges
  flash with fire counters, and trigger/collision contacts appear as
  transient edges.

Made by Denid.
