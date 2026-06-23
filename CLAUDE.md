# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**VR Now** is a Unity 6000.4.4f1 project (v0.1.0) targeting Windows 64-bit with multi-platform support (Windows, Android, iOS) and VR capability. The codebase is in early stages — only tutorial scaffolding scripts exist under `Assets/TutorialInfo/`.

- **Engine**: Unity 6000.4.4f1
- **Render Pipeline**: Universal Render Pipeline (URP 17.4.0)
- **Code**: C# (.NET 4.7.1, C# 9.0)
- **VR/XR**: `com.unity.modules.vr` and `com.unity.modules.xr` enabled

## Key Dependencies (Packages/manifest.json)

- **com.unity.inputsystem** 1.19.0 — New Input System (all input must go through this, not legacy `Input.*` / `KeyCode`)
- **com.unity.render-pipelines.universal** 17.4.0 — URP; all materials/shaders must be URP-compatible
- **com.unity.ai.navigation** 2.0.12 — NavMesh-based pathfinding
- **com.unity.multiplayer.center** 1.0.1 — Multiplayer tooling
- **com.unity.visualscripting** 1.9.11 — Visual scripting support
- **com.unity.timeline** 1.8.12 — Timeline animation

## Input System

Input is configured in `Assets/InputSystem_Actions.inputactions` with a single **"Player"** action map. Modifying this file in the Unity editor regenerates `Assets/InputSystem_Actions.cs`. Always reference input through the generated C# class — never use `Input.GetKey` / `KeyCode`.

Player actions: **Move** (Vector2), **Look** (Vector2), **Attack**, **Interact** (Hold interaction), **Crouch**, **Jump**, **Sprint**, **Previous**, **Next**.

## Rendering Configuration

Two URP renderer presets live in `Assets/Settings/`:
- `Mobile_Renderer.asset` — for mobile/standalone VR headsets
- `PC_Renderer.asset` — for desktop/workstation

The active renderer is set in `ProjectSettings/URPProjectSettings.asset`. Switch it via **Edit > Project Settings > Graphics** in the Unity editor, not by editing YAML directly.

Post-processing volumes: `DefaultVolumeProfile.asset` (global) and `SampleSceneProfile.asset` (scene-level override).

## VR / XR

VR modules are installed but `ProjectSettings/XRSettings.asset` has VR device support disabled by default. For headset-specific support (Meta, HTC, Valve), the corresponding XR plugin package must be added to `Packages/manifest.json`. URP handles stereo rendering automatically when a headset is active. Target ≥90 FPS for VR comfort.

## Command-Line Builds

```
# Windows PC build
"C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Unity.exe" -projectPath . -buildTarget StandaloneWindows64 -executeMethod BuildScript.BuildPC -quit

# Android build
"C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Unity.exe" -projectPath . -buildTarget Android -executeMethod BuildScript.BuildAndroid -quit
```

Note: `BuildScript` does not yet exist — it needs to be created as a static editor class in `Assets/Editor/`.

## Research Papers (Paper/)

The `Paper/` directory contains academic references that likely inform planned features:
- Signed Distance Fields — 3D shape representation and collision
- Adaptively Sampled Geometry — mesh optimization
- Cosserat Rods — physics-based deformable objects (ropes, cables, hair)

## ProjectSettings

Never edit files in `ProjectSettings/` by hand — use the Unity Editor GUI (**Edit > Project Settings**). Incorrect YAML edits can silently break the project.
