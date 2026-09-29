# Knead

[![Unity 6](https://img.shields.io/badge/Unity-6000.0%2B-000000?logo=unity&logoColor=white)](https://unity.com/releases/unity-6)
[![UPM](https://img.shields.io/badge/UPM-git%20URL-2ea44f)](#install)
[![Made by Meowarts](https://img.shields.io/badge/made%20by-Meowarts-E4405F?logo=instagram&logoColor=white)](https://www.instagram.com/meowartsgames/)

**Squash, stretch, twist and bend any mesh in Unity.** Add a Knead Box to an object, drag the handles of the
cage around it, and the mesh follows. Save poses, tween between them in one line of code, or let physics
push it around like jelly, rubber, cardboard or clay.

```csharp
box.Bounce(KneadTween.Squish);   // squish and spring back
box.TweenToPose("Happy");        // go to a pose you made in the editor, and stay
```

## Workflow

1. **Add Component → Knead Box** on any object with a mesh. The box fits the mesh on its own.
2. **Edit Handles** in the inspector, then drag handles in the Scene view. Click, Shift-click or drag a
   rectangle to select; W / E / R to move, rotate or scale them.
3. **Save Pose**, give it a name, and call `box.TweenToPose("name")` from your scripts.

Want it to react to collisions? Add **Knead Physics Box** and pick a material.

## Features

- **One component.** It deforms every renderer on the object and its children (`MeshFilter` and
  `SkinnedMeshRenderer`, blend shapes kept), and follows the object when it moves.
- **Smooth deformation.** Cubic (B-spline), Linear Smooth or Linear Sharp, 2 to 16 handles per axis.
  Normals and tangents are transformed exactly, so lighting stays right.
- **Tweens.** `Bounce` with Squish, Stretch, Expand, Shrink, Twist, Bend and Wobble presets, with an
  elasticity from one clean squish to a jelly wobble. `TweenToPose` and `TweenToRest` with six eases.
  Tweens are awaitable, and bounces stack on top of poses.
- **Poses.** Save, apply, overwrite, rename, preview. They survive resizing the box and changing its
  resolution.
- **Try it in the editor.** Play buttons for every effect and pose run in the Scene view, without entering
  play mode.
- **Physics.** Knead Physics Box turns the handles into springs. Rigidbodies dent it when they hit it,
  heavy ones keep it pressed while they rest on it, and they sink into the dent instead of floating on
  the flat collider. It also jiggles when the object itself starts, stops or lands. Presets: Jelly,
  Rubber, Pillow, Cardboard, Clay, Metal, Rock. A **Drop Test** button tunes a material without playing.
- **Fast.** The deformation runs in a compute shader for regular meshes (with a CPU fallback for WebGL and
  old mobile), and costs nothing while nothing moves. Physics boxes sleep when they settle.
- **Static bake.** Tick **Bake** (or mark the object Batching Static) and the build gets the deformed mesh
  with no component at all. **Save Deformed Mesh...** in the component menu writes it to an asset.
- **Safe.** The original mesh is what's saved in your scenes and prefabs; the deformed copy never is.
  Every edit is one undo step.

## Requirements

- Unity 6 (developed and tested on **Unity 6.6**). Built-in, URP and HDRP.
- Meshes need **Read/Write** enabled to deform in builds. The inspector warns you and fixes it in one click.

## Install

In Unity: **Window → Package Manager → + → Install package from git URL…** and paste:

```
https://github.com/ValDesu/Knead.git?path=/Packages/com.meowarts.knead
```

## Scripting

```csharp
using Meowarts.Knead;

public class Landing : MonoBehaviour
{
    public KneadBox box;

    async void OnCollisionEnter(Collision c)
    {
        await box.Bounce(KneadTween.Squish.With(amount: c.relativeVelocity.magnitude * 0.05f));
        box.TweenToPose("Relieved", 0.4f, KneadEase.Spring);
    }
}
```

| API | What it does |
|---|---|
| `Bounce(KneadTween.Squish)` | Goes to the effect and springs back. Returns a handle: `await` it, `.OnComplete(...)`, `.Stop()` |
| `KneadTween.X.With(amount, duration, elasticity, delay, unscaledTime)` | Tweaks a preset. Amount is a fraction of the box (0.3 = 30%), or degrees for Twist and Bend |
| `TweenToPose(name, duration, ease)` / `TweenToRest(...)` | Goes to a pose (or rest) and stays |
| `SetPose(name, weight)` | Sets a pose right away, scaled by weight |
| `SavePose(name)`, `DeletePose`, `RenamePose`, `Poses` | Manage poses from code |
| `GetHandlePosition(i)`, `SetHandlePosition(i, world)` | Drive handles yourself (grab, wiggle) |
| `Deform(worldPoint)` | Where a point ends up: snap a prop to the bent surface |
| `Axis` | The axis effects act along (Y by default) |
| `KneadPhysicsBox.Preset`, `.Material` | Pick a preset, or set every setting |
| `KneadPhysicsBox.AddImpulse(point, impulse)` | A hit without a rigidbody: bullets, punches |
| `KneadPhysicsBox.ResetDents()` | Clears permanent dents |

## Physics materials

Measured on a 1 m box at 4×4×4 with 1 kg:

| Preset | Resting 1 kg sinks | Dropped from 1 m | Dent left |
|---|---|---|---|
| Jelly | 11% | 34%, wobbles | none |
| Rubber | 3% | 12%, bouncy | none |
| Pillow | 19% | 46%, no bounce | none |
| Cardboard | 0.5% | 3% | 2% |
| Clay | 1% | 6% | 3% |
| Metal | 0% | 0.1% | none (big hits only) |
| Rock | 0% | 0% | none |

Stiffness is per handle, so a higher resolution feels softer locally. Stiffer materials settle faster.

## Limitations (v1)

- Skinned meshes are deformed in their bind pose, before skinning. A box on the character's root squishes
  the whole character; a box in world space that the character walks through isn't supported yet.
- One box per renderer. Boxes don't stack yet.
- The Animation window can't key handles one by one (Unity doesn't animate array elements). Animate with
  poses and tweens from code, or move the whole object.
- The collider doesn't change shape: bodies sink into dents through contact modification, but a deep
  bend doesn't make new surfaces to collide with.

## Working on Knead

This repository is a Unity 6.6 project that embeds the package in `Packages/com.meowarts.knead`:

- `Assets/Scenes/Knead.unity`: a demo scene (1–7 bounce the props, P / L / R poses, click a box in the back
  row to drop a ball). **Knead → Build Demo Scene** rebuilds it.
- `Packages/com.meowarts.knead/Runtime`: the components and the solver.
- `Packages/com.meowarts.knead/Editor`: inspector, Scene view tool, previews, save hooks and the build bake.
- `Packages/com.meowarts.knead/Tests`: EditMode tests (**Window → General → Test Runner**).

To run the tests from the command line:

```
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml
```

Don't pass `-nographics`: the GPU test needs a graphics device.
