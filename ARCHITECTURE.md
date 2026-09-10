# Architecture notes

Rules that were learned the hard way. Each one records the bug that produced it, because
a rule without its failure is easy to talk yourself out of.

---

## 1. Serialized data flows one way: into runtime state, never back

**An `[ExecuteAlways]` component may push serialized data into runtime state. It must
never write back into the scene or into a project asset.**

Anything that repairs data at load hides the fact that the data is wrong. The saved file
stops being the source of truth, tests that inspect the loaded scene silently measure
the repair rather than the data, and a hand edit in the Inspector gets overwritten the
next time the scene opens.

**The bug.** `NodeView` was `[ExecuteAlways]` and its `OnEnable` called `RestLabel()`,
which set the label pivot's local position. Every node's label looked correctly placed in
the Editor and in every screenshot. The scene file recorded **every pivot at `z = 0`** —
buried inside its bubble. The scene test opened the file, but by the time it measured the
transform, `OnEnable` had already moved it, so the test could never fail. The offset now
lives in `Node.prefab`, where instances inherit it and a hand edit sticks.

### What is allowed

- Reading a serialized field and pushing it into a `MaterialPropertyBlock`, a
  `CommandBuffer`, a cached array — anything that is rebuilt from scratch each session
  and is not saved. `NodeView` and `EdgeView` do exactly this and are correct.
- Writing scene data from an **explicitly invoked** action: a `[ContextMenu]`, a
  `[MenuItem]`, an Inspector button. The developer asked for it, so it is an edit, not a
  repair.

### What is not

- `OnEnable`, `OnValidate`, `Awake` or `Update` assigning to another component's
  serialized properties, to a `Transform`, or to a shared `Material` asset.

### How it is enforced

`Assets/_Project/Tests/SceneFileTests.cs` reads the `.unity` and `.prefab` files as text.
Nothing runs between the file and the assertion, so a component cannot repair its way to
a green test. It carries a **negative control** that feeds the parsers deliberately broken
data and asserts they report it — otherwise the tests could pass by finding nothing.

### Audit, 2026-09-10

| Component | Verdict |
|---|---|
| `NodeView` | Clean. Pushes `_colour` into a property block only. |
| `EdgeView` | Clean. Same pattern. |
| `LightRig` | **Violated.** `OnEnable` and `OnValidate` both called `Apply()`, which wrote `localRotation`, `color`, `intensity` and shadow settings onto the two `Light` components. Rotating the key light by hand was undone on the next load. Now a `[ContextMenu]`. |
| `EnvironmentController` | **Violated, worse.** `OnValidate` pushed theme colours into the `Skybox_Dusk` **material asset**, so a hand tweak to the material was overwritten whenever the scene opened. `Start()` did it at runtime too, which in the Editor persists past Play. Now a `[ContextMenu]`; the material's colours are authored data. |

---

## 2. Dev tools read `Keyboard.current` directly; gameplay goes through the actions asset

`PerfOverlay` (F3), `RenderTuner` (F4–F6) and `BubbleTuner` (F7–F10) are stripped at
release — see `RELEASE.md`. Their keys are deliberately **not** in
`PivotInput.inputactions`, so they never appear in the rebinding UI and cannot collide
with a binding the user chose.

Everything the player actually does goes through the actions asset, because rebinding and
the left-handed toggle both depend on bindings being data rather than code.

---

## 3. Nothing branches on which rig is active

`RigManager` enables exactly one rig at boot and logs which. After that, no gameplay code
asks. Grabbables are plain `XRGrabInteractable`; the desktop rig drives them through a
`MouseRayInteractor : XRRayInteractor`, so both rigs speak the same protocol to the same
components.

If something downstream needs to know the mode, that is a signal the abstraction is
wrong and is worth raising rather than working around with a branch.
