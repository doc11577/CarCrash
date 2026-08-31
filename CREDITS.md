# Credits

Third-party assets used in CarCrash. Recorded as each one is added, not reconstructed later.

> **Attribution is REQUIRED for the BMW E30 (CC-BY).** Everything else in this project is
> CC0 and credited voluntarily. The E30 is not: the licence obliges us to credit ROH3D
> visibly, not just in this file. It must appear in-game — the garage screen is the place.
> Do not ship without it.

---

## Low Poly Car — BMW E30 1985 (player car)

- **Author:** ROH3D
- **Source:** https://sketchfab.com/3d-models/low-poly-car-bmw-e30-1985-white-9dea494b447e442fafbddfc7eccbf158
- **License:** [CC Attribution 4.0](https://creativecommons.org/licenses/by/4.0/)
- **Attribution required:** **YES.** Credit "BMW E30 1985 by ROH3D (CC-BY)" in-game.
- **Location:** `Assets/Art/Vehicles/BMW-E30/`
- **Retrieved:** 2026-08-29 (model published 2022-01-15)

Sketchfab "source" download: a 3ds Max OBJ plus a 4K Substance PBR set. Measured, not quoted:

- 13,076 tris / 6,993 verts as downloaded. Four objects: `LowBody` (6,196), `LowGlass`
  (832, its own `Glass` material), `LowTire` and `LowTire001` (3,024 each).
- **Authored in millimetres** — 4,317.92 mm long. Needs `--scale 0.001`. The real E30 is
  4,325 mm, so the model is accurate.
- **Each `LowTire` object is a PAIR of wheels**, not one. Four wheels live in two objects
  and have to be separated by loose parts before they can be used.
- Wheel radius 0.300 m, wheelbase 2.57 m — the real E30's wheelbase is 2,570 mm.
- The body is asymmetric by 0.021 m across; suspension anchors are symmetrised.

Textures were **4K and 43 MB**, against a 20 MB whole-game download budget. Reduced to
**3.0 MB**: body base colour 4096² → 1024², tyre 1024² → 512², glass 2048² → 256². The
normal, metallic and roughness maps are **dropped entirely** — 24 MB for a normal map on a
low-poly car read by a chase camera on integrated graphics is not defensible, and the
Substance base colour already carries the panel detail. Use material constants instead.

`e30-split.fbx` is derived from `Source/e30-FullBody.obj` by `tools/blender/split_car.py`.
CC-BY permits modification provided attribution is kept.

---

## Car Kit (v3.1)

- **Author:** Kenney — https://kenney.nl
- **Source:** https://kenney.nl/assets/car-kit
- **License:** [CC0 1.0 Universal](http://creativecommons.org/publicdomain/zero/1.0/) (public domain)
- **Attribution required:** No. Credited here voluntarily.
- **Location:** `Assets/Art/Vehicles/`

Subset in use: 10 vehicle bodies, 3 wheel styles, 14 debris pieces, and the shared
`colormap.png` texture atlas.

Structure notes, measured from the source meshes rather than the pack description:

- Each vehicle is a welded `body` mesh plus four separate `wheel-*` nodes. Wheels detach
  for free; **doors, hood and bumpers are not separate geometry.**
- The `debris-*` models are standalone generic props (door, bumper, tire, spoiler, plates,
  drivetrain, bolts). Panel detachment is faked by spawning these rather than by removing
  geometry from the body.
- Every vehicle and debris piece shares the single `colormap` material, so the whole
  vehicle roster batches together and adding another car costs no extra texture memory.
- `sedan.glb` reference dimensions at import scale 1.0: body 1.50 × 1.15 × 2.54 m,
  wheels radius 0.30 m at local (±0.30, 0.30, ±0.66). 3,184 verts total, 1,072 in the body.

---

## Low Poly Cars (free tier)

- **Author:** Cosmo — https://cosmo-art.itch.io
- **Source:** https://cosmo-art.itch.io/low-poly-cars
- **License:** [CC0 1.0 Universal](http://creativecommons.org/publicdomain/zero/1.0/) (public domain)
- **Attribution required:** No. Credited here voluntarily.
- **Location:** `Assets/Art/Vehicles/CosmoCars/`
- **Retrieved:** 2026-08-29 (pack published 2024-10-20)

The free tier is 12 vehicles: `armor, coupe, fenyr, ghini, italia, jeep, kamaro, lamb,
mobil, police, rally, van`. The 11-car premium tier was **not** bought and is not in the repo.

Chosen over Kenney for the **player car** because the proportions are realistic rather than
toy-like, which is what the reference footage looks like. Measured, not quoted:

- `coupe`: body 1,010 tris / 1,599 verts, 5.14 × 2.35 × 1.50 m. Four separate wheel objects
  at 320 tris each. **2,290 tris complete.**
- `police`: body 1,102 tris, 5.72 m. Same four-wheel split.
- Authored at **real-world scale**, so Unity import Scale Factor is **1.0** — *not* the 1.6
  the Kenney kit needs. Mixing the two up will spawn debris at the wrong size.
- Every vehicle shares one `texture-palette.png` (1000 × 720, 8.6 KB), so the whole pack
  batches and colour comes from UVs pointing at palette swatches rather than per-car
  textures. Same property that makes the Kenney roster affordable.
- Body materials are `metallic / light / glass / texture`; wheels are `metallic / texture`.

`coupe-split.fbx` is derived from `Source/coupe.fbx` by `tools/blender/split_car.py` — see
CLAUDE.md. CC0 permits modification and redistribution without restriction.

---

## TextMesh Pro essential resources

- **Author:** Unity Technologies, bundled inside `com.unity.ugui` 2.0.0
- **Source:** Window → TextMeshPro → Import TMP Essential Resources
- **Attribution required:** No, for the Unity-authored shaders and settings.
- **Location:** `Assets/TextMesh Pro/`
- **Added:** 2026-08-30, because `ScoreHud` draws nothing without the default font asset.

Committed to the repo rather than left to each machine's import, so the GUIDs stay stable.
Two third-party pieces come with it and carry their own terms:

- **Liberation Sans** — Digitized data © 2010 Google Corporation, © 2012 Red Hat Inc.
  **SIL Open Font License 1.1**, full text at `Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt`.
  This is the font the HUD actually renders with. OFL permits embedding in a game without
  attribution in the product; the licence file must travel with the source, which it does.
- **EmojiOne sample sprites** — `Assets/TextMesh Pro/Sprites/EmojiOne.png`, terms at
  `EmojiOne Attribution.txt`. **Unused by this project.** It is TMP's default sprite asset and
  arrives whether wanted or not. If it is ever actually drawn, check EmojiOne's licensing
  first — the bundled note points at their site rather than granting anything outright.

Build-size note, because `Resources/` folders are force-included: `LiberationSans SDF.asset` is
**2.2 MB** and ships whether referenced or not. See CLAUDE.md for the trimmed-font-asset fix.

---

## Quarry01 course textures

- **Author:** Rob Tuytel / Poly Haven — https://polyhaven.com
- **License:** [CC0 1.0 Universal](http://creativecommons.org/publicdomain/zero/1.0/) (public domain)
- **Attribution required:** No. Poly Haven is entirely CC0; credited here voluntarily.
- **Location:** `Assets/Art/Tracks/Quarry01/Textures/`
- **Retrieved:** 2026-08-30, via `https://api.polyhaven.com/files/<asset>`

| File | Poly Haven asset | Used for |
| --- | --- | --- |
| `quarry_ground_diff_1k.jpg` | `rock_ground` | `CourseGround` — corridor and bowl floor |
| `quarry_rock_diff_1k.jpg` | `rock_face_03` | `CourseRock` — walls, benches, boulders |

**Base colour only, at 1K.** The normal, roughness, displacement and ARM maps Poly Haven ships
alongside these are deliberately NOT downloaded — the same call already made for the E30, where
24 MB of normal map on a low-poly car read by a chase camera was not defensible. Use material
constants for smoothness instead.

Two rejected candidates, recorded so they are not tried again:

- `rock_wall_09` — mortared masonry. It is a castle wall, not a quarry face, and its contrast is
  high enough that tiling would be obvious over a 1,800 m course.
- `rock_boulder_dry` — tiles beautifully but is washed out and structureless. It reads as a
  smooth boulder rather than a cut rock face, which is wrong next to flat-shaded terraced walls.

---

## Low Poly Car — De Tomaso P72 2020

- **Author:** ROH3D — the same author as the E30 above
- **Source:** https://sketchfab.com/3d-models/low-poly-car-de-tomaso-p72-2020-ab85c302e652492196b600ee1eb7106a
- **License:** [CC Attribution 4.0](https://creativecommons.org/licenses/by/4.0/)
- **Attribution required:** **YES.** Credit "De Tomaso P72 by ROH3D (CC-BY)" in-game, on the
  car select screen alongside the E30.
- **Location:** `Assets/Art/Vehicles/DeTomasoP72/`
- **Retrieved:** 2026-08-30

Measured, not quoted: 21,496 tris / 12,173 verts as downloaded, in **millimetres**
(4,738 mm long, against the real car's 4,566) so it needs `--scale 0.001`.

**One welded mesh, one material.** The whole car is a single object `s_0070` with material
`Standard32B531` — no separate wheels and, unlike the E30, **no separate `Glass` material**.
Two consequences:

- Wheels had to be recovered geometrically. See `find_wheels_by_shape` in `split_car.py`.
- **`CarGlass` cannot work on this car.** It empties submeshes whose material name starts with
  `Glass`, and there is no such material to empty. The windows are part of the body texture.
  Either leave the component off this car, or split the glass by hand in Blender first.

Textures were **44 MB** as downloaded, dominated by a 26.7 MB normal map. Reduced to a single
**1.9 MB `p72_body.png`** — base colour only, 4096² down to 1024², with normal, metallic and
roughness dropped entirely, following the E30 precedent.

`p72-split.fbx` is derived from `Source/FullBody.obj` by `tools/blender/split_car.py`.
CC-BY permits modification provided attribution is kept.
