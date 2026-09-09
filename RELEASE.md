# Release hardening checklist

Things this project does on purpose during development that **must not ship**. Each
entry says what it is, why it exists, and exactly what to change. Work top to bottom
before cutting a store build.

Nothing here is a bug. Every item is a measurement or authoring affordance that earns
its place right now and stops earning it at release.

---

## 1. Null `postProcessData` on `Mobile_Renderer`

**File:** `Assets/Settings/Mobile_Renderer.asset` → Inspector → **Post-processing → Enabled** (uncheck)

Unity's own Meta Quest guidance says to disable post-processing on the Universal
Renderer Data for passthrough. We deliberately leave it populated during development
so the runtime F4 A/B (post off / mobile profile / desktop profile) works on device
without a rebuild.

At release the A/B is gone, so take the blanket advice: null it out. Post is already
driven off at the camera by `EnvironmentController` and `RenderTuner`, so this is
belt-and-braces rather than a behaviour change — but it removes the possibility of a
stray camera turning it back on.

**Verify:** passthrough still composites the room correctly, and `_CameraColorTexture`
alpha survives to the compositor.

---

## 2. Strip `RenderTuner` from release builds

**File:** `Assets/_Project/Scripts/VFX/RenderTuner.cs`

Binds F4 / F5 / F6 and **writes to the URP Asset at runtime** (`supportsCameraDepthTexture`,
`msaaSampleCount`). It restores originals on disable and on quit, and refuses to touch
the pipeline in the Editor unless `_allowPipelineEditsInEditor` is ticked — but none of
that belongs in a shipping build.

Remove the component from every scene, or wrap the class body in
`#if DEVELOPMENT_BUILD || UNITY_EDITOR`.

**Do not** simply rely on the Editor guard flag. That flag protects the *project asset*,
not the shipping player.

---

## 3. Strip `BubbleTuner` from release builds

**File:** `Assets/_Project/Scripts/VFX/BubbleTuner.cs`

Binds F7-F10 and writes to the **shared** `Bubble.mat` and the node label material.
Restores originals on disable and on quit, and refuses to touch materials in the Editor
unless `_allowMaterialEditsInEditor` is ticked — none of which belongs in a shipping
build. It also draws an `OnGUI` overlay.

Remove from scenes or wrap in `#if DEVELOPMENT_BUILD || UNITY_EDITOR`.

---

## 4. Strip `PerfOverlay` from release builds

**File:** `Assets/_Project/Scripts/Utils/PerfOverlay.cs`

F3 overlay. Uses `ProfilerRecorder` and `OnGUI`, both of which cost something even when
the overlay is hidden — `OnGUI` in particular forces the legacy IMGUI path to run every
frame on any object that declares it.

Remove from scenes or wrap in `#if DEVELOPMENT_BUILD || UNITY_EDITOR`.

---

## 5. Development Build flags off

**Location:** Build Profiles → Android → **Build Settings**

- [ ] **Development Build** — off
- [ ] **Autoconnect Profiler** — off
- [ ] **Deep Profiling Support** — off
- [ ] **Script Debugging** — off
- [ ] **Wait For Managed Debugger** — off

Development builds disable some IL2CPP optimisations and keep symbol data. Leaving this
on is the single easiest way to ship a build that is quietly slower than the one that
was measured.

---

## 6. Player settings for release

**Location:** Project Settings → Player → Android

- [ ] **Scripting Backend** — IL2CPP
- [ ] **Target Architectures** — ARM64 only (ARMv7 off)
- [ ] **Managed Stripping Level** — at least Low; verify nothing reflection-loaded breaks
- [ ] **Graphics APIs** — Vulkan only, `Auto Graphics API` off
      (`RenderSettingsGuardTests.AndroidBuildsVulkanOnly` covers this)
- [ ] **Texture compression** — ASTC
- [ ] **Static / Dynamic Batching** — as measured, not as defaulted

---

## 7. Remove authoring-only editor tooling

Temporary `[MenuItem]` scene-authoring tools are deleted as soon as they have run — the
scene file is the source of truth, not the generator. Before release, confirm
`Assets/_Project/Editor/` contains only tooling that is genuinely still needed, and that
nothing under `Pivot.Editor` is referenced from runtime code.

---

## 8. Confirm the deferred passthrough install

`com.unity.xr.meta-openxr` is **not installed** during early development, by choice.
Before any mixed-reality release it has to go in, along with:

- `ARSession` + `ARCameraManager` + `ARCameraBackground` on the rig
- Camera clear verified as solid black with **alpha 0** in passthrough mode
- Mobile tier HDR confirmed still **off** (R11G11B10 has no alpha channel, and
  passthrough composites on alpha — `RenderSettingsGuardTests` covers this)

---

## 9. The look has not had a device pass — two of them are outstanding

**This is a correctness item, not a polish one.** Every value in `Bubble.shader`,
`SkyboxDusk.shader` and `ThemeSO` was tuned against sRGB screenshots on a desktop
monitor. A Quest panel is dimmer and lower in contrast than a monitor, so a rim, a
glint and a guard that read correctly in a PNG will not necessarily read correctly
through the lenses. **Treat the committed values as a starting point.**

Two separate passes are needed, and the second is not a touch-up of the first:

- [ ] **Skybox pass.** Bubbles over the authored dusk gradient. This is closest to what
      was tuned, but on panel rather than monitor.
- [ ] **Passthrough pass.** Bubbles over a *lit room*, which is a completely different
      and uncontrolled background: brighter, warmer, and varying with the user's
      lighting. The edge darkening that separates overlapping orbs against a dark sky
      may separate them poorly against a pale wall, and the legibility guard has to be
      re-checked against light backgrounds. Expect a genuinely different value set —
      budget for `ThemeSO` carrying two profiles rather than one.

Use `BubbleTuner` (F7-F10) to do both in-headset in one session each, then `F9` to dump
the values and paste them back. The dump is also written to
`Application.persistentDataPath/bubble-tuning.txt`, so it can be pulled with
`adb pull` rather than read off a log.

Only after both passes should the values in the shader Properties block be considered
authored rather than provisional.

---

## Quick audit

```bash
grep -rn "RenderTuner\|BubbleTuner\|PerfOverlay" Assets/_Project/Scenes/ Assets/_Project/Prefabs/
```

Should return nothing once items 2, 3 and 4 are done.
