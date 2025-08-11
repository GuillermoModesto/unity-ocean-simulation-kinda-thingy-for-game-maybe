# Dynamic Water & Boat Simulation

A Unity project for fast, good‑looking water with float physics, a controllable boat, and GPU wake trails. It mixes a procedurally generated mesh, multi‑octave waves (Burst/Jobs), and a lightweight water shader that can ingest wind/current and a live wake texture.

---

## Highlights

- **Multi‑octave waves (CPU Jobs + Burst):** per‑vertex heights computed in parallel with environmental wind/current influence and optional Perlin blend.
- **One‑slider sea state:** quickly morphs from calm to storm; drives wind/current, amplitude, choppiness, wavelengths, and optional move speeds.
- **Physically‑motivated floating:** stable multi‑point buoyancy + drag/orientation.
- **Simple boat controller:** WASD steering & throttle with force‑based motion.
- **GPU wake trails:** a tiny world‑space RenderTexture you “paint” into as the boat moves; the water shader samples it for foam/wake effects.
- **Live shader driving:** material properties (wind/current vectors, time, sea level, color ramp, wake map/UV) are pushed every frame.

---

## Folders & Unity Version

- **Unity:** 2021.3 LTS or newer recommended.
- **Scripts:** `Assets/Scripts/`  
- **Input asset:** `Assets/Prefabs/PlayerInputActions.inputactions` (auto‑generates `PlayerInputActions.cs`)
- **Shaders:** `Assets/Shaders/` (includes the water surface and the two hidden wake shaders)

---

## Quick Start

1. **Create the water:**
   - Add a GameObject and put **`Waves`** on it (this generates/animates the mesh).
   - On the same object’s Renderer, assign the **Water** material that uses the water shader (see *Shaders*).
   - Add **`WaterShaderDriver`** to that Renderer object. If left empty, it will auto‑find `Waves`.
   - (Optional) Add **`SeaStateController`** (same GameObject is fine) and tweak **_Sea State_** to preview calm ↔ storm.

2. **Make a boat:**
   - Create a boat GameObject with a **Rigidbody**.
   - Add **`WaterFloat`** and place 1–4 (or more) float points as children for stability.
   - Add **`WaterBoat`** and assign its **motor** Transform (an empty at the stern works well).

3. **Wake trails (optional but fun):**
   - Add **`WakePainter`** to the boat (or any moving object) and, if not set, point its **waves** reference to your `Waves` object.
   - Leave **Show In Scene As Global** on to broadcast `_WakeMap`/`_WakeUV` globally.
   - Ensure **`WaterShaderDriver`** is on the water Renderer so the shader samples the wake. You can also drag the boat’s `WakePainter` into WaterShaderDriver’s **Wake Source** to bind explicitly.

4. **Camera & Input:**
   - Add **`CameraController`** to your Camera and set **player** to the boat to get a simple follow camera.
   - Project Settings → Player → **Active Input Handling**: “Input System Package” (or “Both”).
   - The generated **`PlayerInputActions`** maps WASD to a 2D vector for movement by default.

Press Play and drive!

---

## Scripts

### Waves.cs
Procedurally builds a grid mesh and animates heights from multiple octaves. Exposes wind/current (direction & strength) and offers `GetHeightAt(Vector3)` for buoyancy queries.

### WavesJob.cs
Burst‑compiled `IJobParallelFor` that computes per‑vertex height for all active octaves. Each octave can use sine/cosine + Perlin blend, frequency boosts, and reacts to wind/current. Environmental flow influences amplitude more on specific bands to keep a natural “swell + detail” feel.

### OctaveData.cs
Compact data for a single octave:
- `direction` (normalized), `moveSpeed` for its self‑propagation,
- `scale`, `height`, `perlinBlend`, `baseScaleMultiplier`, `active`,
- environment coupling: `windResponse`, `currentResponse`,
- `scaleFrequencyBoost` to sharpen/soften spatial frequency.

### SeaStateController.cs
“**One slider**” ocean control. Given `seaState ∈ [0,1]`, it:
- Sets **wind/current** direction & strength on `Waves`.
- Scales **amplitude**, **choppiness** (Perlin blend), **wavelength** (longer swell, slightly tighter detail), and optional **moveSpeed** per octave.
- Does **not** overwrite per‑octave travel **direction** (that remains wind/current‑driven in the job).

### WaterShaderDriver.cs
Pushes dynamic values into the water material (e.g., shader *Custom/Water_SimpleZWrite*):
- **Wind/Current** vectors (dir.xy normalized, w = strength),
- **_WaveTime** with optional external‑time toggle,
- **_SeaLevel** from the water object’s Y,
- **_HeightRange** auto‑fit to the current sea amplitude (with smoothing),
- Binds **_WakeMap** and **_WakeUV** (either from an assigned `WakePainter`, an explicit texture, or the global values).

### WakePainter.cs
GPU painter for a small world‑space **RenderTexture** that accumulates wake intensity:
- Two‑pass **decay+blur** then **splat** based on speed and contact depth with the water.
- Publishes `_WakeMap` + `_WakeUV` as globals for the water shader, or can be read explicitly.
- Tunables: UV mapping (`uvScale`, `uvOffset`), decay/blur, splat radius/intensity/hardness, contact gating via `Waves.GetHeightAt`.

### WaterFloat.cs
Multi‑point buoyancy that samples the water height, applies drag, and orients the body to the surface normal. Use more points for stability.

### WaterBoat.cs
Simple force‑based controller. Applies steering at the motor position and drives forward velocity towards a target based on throttle. Updates a motor transform for visual steering yaw and can gate a ParticleSystem on throttle.

### PhysicsHelper.cs
Small helpers for centers/normals of point sets and force application toward a target velocity.

### CameraController.cs
Tiny follow‑camera using a stored offset; updates position in `LateUpdate`.

### PlayerInputActions.cs
Auto‑generated class from `PlayerInputActions.inputactions` providing a `BoatControl/Move` action (WASD → 2D vector).

---

## Shaders

- **Water surface:** `Custom/Water_SimpleZWrite` (source `Waves2.shader`). Reads `_WindDir`, `_CurrentDir`, `_WaveTime`, `_UseExternalTime`, `_SeaLevel`, `_HeightRange`, and optional `_WakeMap`/`_WakeUV` supplied by `WaterShaderDriver`.
- **Wake passes (does not work):**
  - `Hidden/WakeWrite` – draws the current splat onto the wake RT.
  - `Hidden/WakeDecay` – fades and blurs the previous frame.

> Tip: Start with a neutral water color gradient, then enable **Auto Color Range** on `WaterShaderDriver` so the ramp adapts to small and large seas automatically.

---

## Tuning & Tips

- **Performance:** prefer lower mesh resolution and fewer active octaves first; Burst/Jobs scales well but vertex count still matters.
- **Natural direction:** keep per‑octave `direction` conservative and let **wind/current** dominate via `SeaStateController` for cohesive flow.
- **Wake scale:** begin with `uvScale ≈ 0.02` (≈50 m per UV) and adjust until the wake footprint looks right for your scene size.
- **Buoyancy:** distribute float points wide and low; add more points for large or oddly shaped hulls.

---

## Dependencies

- `Unity.Mathematics`, `Unity.Collections`, `Unity.Jobs`, `Unity.Burst`
- **Unity Input System** (for the boat controller)

---

## Known Limitations

- The water surface uses **vertex displacement** only (no true 3D volume), so extreme breaking waves aren’t represented.
- The wake texture is a **2D mask** in world UVs; sampling/parallax is intentionally simple for speed.