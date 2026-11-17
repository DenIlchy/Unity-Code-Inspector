# Code Inspector Demo

A small demo scene showing a player, two enemies, a game manager, a spawner, a
trigger zone, a HUD script and a `GameConfig` ScriptableObject.

## What to look at

1. Open **Window > Code Inspector**, switch to the **Scene** view, then open
   `Demo.unity`.
2. Inspect the edges: `Enemy → Player` (field reference), the trigger zone's
   UnityEvent bindings, and `Spawner → GameManager` (approximate singleton call,
   dashed).
3. Select a node to see its **Parameters** panel and tweak values.
4. Press **Play**: watch live pulses, UnityEvent edge flashes with fire
   counters, and trigger contacts on the trigger zone.
5. While playing, tweak a value (e.g. enemy speed), exit Play Mode, and choose
   which changes to keep in the keep-changes dialog.
