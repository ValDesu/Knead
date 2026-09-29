# Changelog

## [0.1.0] - 2026-09-29

First release.

- Knead Box: a lattice cage that deforms every renderer on its object and children (MeshFilter and
  SkinnedMeshRenderer, blend shapes kept). Cubic, Linear Smooth and Linear Sharp, 2 to 16 handles per axis,
  exact normals and tangents.
- Scene view tool: click, Shift-click and rectangle selection, move / rotate / scale (W / E / R), right-click
  for Select All, Expand / Shrink Selection, Reset Selected, Mirror X / Y / Z.
- Poses: Save, Apply, Overwrite, Rename, Delete, preview. They survive resizing and resolution changes.
- Tweens: `Bounce` with Squish, Stretch, Expand, Shrink, Twist, Bend and Wobble presets, `TweenToPose`,
  `TweenToRest`, six eases, awaitable handles. Play buttons preview them in edit mode.
- Knead Physics Box: spring handles pushed by collisions and resting weight, bodies sink into dents
  (contact modification), jiggle from the object's own motion, sleep when settled. Presets: Jelly, Rubber,
  Pillow, Cardboard, Clay, Metal, Rock. Drop Test in the inspector.
- Compute shader deformation for MeshFilter targets, CPU fallback everywhere else. Nothing runs while
  nothing moves.
- Static bake at build time (Bake, or Batching Static), and Save Deformed Mesh... in the component menu.
- Original meshes are what's saved in scenes and prefabs, never the deformed copy.
