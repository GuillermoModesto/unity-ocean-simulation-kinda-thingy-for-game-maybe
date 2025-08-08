# Dynamic Water Simulation Project

## Overview

This Unity project simulates dynamic water surfaces and floating physics using a custom mesh, multi-octave wave generation, and job-based parallel computation. It includes interactive boat controls, floating objects, and utility scripts for physics calculations. The system is designed for extensibility and performance, leveraging Unity's Job System and Burst Compiler.

---

## Features

- **Dynamic Water Mesh:**  
  - Procedurally generated grid mesh with configurable resolution and scale.
  - Animated surface using multiple wave octaves, each with customizable speed, scale, height, and blend between sine/cosine and Perlin noise.
  - Environmental influences: wind and water current, both direction and strength.

- **Floating Physics:**  
  - Objects float and orient themselves based on sampled water heights.
  - Supports multiple float points for stable buoyancy.
  - Adjustable drag and surface attachment options.

- **Boat Controller:**  
  - Player-controlled boat with steering and throttle.
  - Physics-based movement and force application.
  - Visual feedback via motor rotation and optional particle effects.

- **Parallelized Wave Calculation:**  
  - Uses Unity's Job System and Burst Compiler for efficient mesh vertex updates.
  - Each vertex's height is calculated in parallel, considering all active octaves and environmental factors.

- **Utility Scripts:**  
  - PhysicsHelper for geometric calculations and force application.
  - Editor scripts for displaying and managing project documentation.

---

## Main Components

### `Waves.cs`
- Generates and animates the water mesh.
- Manages environmental influences and wave octaves.
- Provides `GetHeightAt(Vector3 position)` for sampling water height at any world position.

### `WavesJob.cs`
- Burst-compiled job struct for parallel vertex height calculation.
- Uses `OctaveData` for each wave layer.

### `OctaveData.cs`
- Struct storing parameters for a single wave octave:
  - Speed, scale, height, blend, base scale multiplier, active flag, wind/current response.

### `WaterFloat.cs`
- Simulates floating physics for objects.
- Calculates waterline, applies drag, and adjusts orientation based on water normals.

### `WaterBoat.cs`
- Handles player input for boat movement and steering.
- Applies forces for propulsion and steering.
- Manages motor rotation and optional particle effects.

### `PhysicsHelper.cs`
- Utility class for physics-related calculations:
  - Center and normal of point sets.
  - Force application to reach target velocity.

### `ReadmeEditor.cs` & `Readme.cs`
- Editor scripts for displaying project documentation in Unity Inspector.

---

## Setup & Usage

1. **Unity Version:**  
   - Recommended: Unity 2021.3 or newer (compatible with .NET Framework 4.7.1 and C# 9.0).

2. **Project Structure:**  
   - Scripts are located in `Assets/Scripts/`.
   - Editor and documentation scripts are in `Assets/TutorialInfo/Scripts/Editor/`.

3. **Scene Setup:**  
   - Add a GameObject with the `Waves` component to your scene.
   - Configure mesh resolution, scale, wind/current, and octaves in the Inspector.
   - Add floating objects with the `WaterFloat` component and assign float points.
   - Add a boat with the `WaterBoat` component and assign a motor transform.

4. **Player Input:**  
   - Uses Unity's Input System (`PlayerInputActions`).  
   - Configure input actions for boat control as needed.

5. **Extending Waves:**  
   - Adjust or add octaves for different wave behaviors.
   - Modify environmental parameters for wind/current effects.

---

## Customization

- **Wave Octaves:**  
  - Each octave can be tuned for speed, scale, height, blend, and environmental response.
  - Octaves can be enabled/disabled individually.

- **Floating Objects:**  
  - Number and position of float points affect stability and realism.
  - Drag and surface attachment can be adjusted for different behaviors.

- **Boat Controller:**  
  - Steering power, propulsion, and max speed are configurable.
  - Particle system can be added for visual effects.

---

## Dependencies

- **Unity.Mathematics**
- **Unity.Collections**
- **Unity.Jobs**
- **Unity.Burst**
- **Unity Input System** (for boat control)