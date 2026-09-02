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

## Car Kit (v3.1) — debris only

- **Author:** Kenney — https://kenney.nl
- **Source:** https://kenney.nl/assets/car-kit
- **License:** [CC0 1.0 Universal](http://creativecommons.org/publicdomain/zero/1.0/) (public domain)
- **Attribution required:** No. Credited here voluntarily.
- **Location:** `Assets/Art/Vehicles/`

**Trimmed to four models on 2026-09-02.** Only `debris-bumper`, `debris-door`,
`debris-plate-small-a` and `debris-tire` remain, plus the shared `colormap.png` atlas — those
are what the `Debries/` prefabs spawn as generic panel props. The ten vehicle bodies, three
wheel styles and ten other debris pieces were deleted: nothing had referenced them since the
player car became the split E30 and traffic moved onto the same real prefabs.

Structure notes, measured from the source meshes rather than the pack description, kept because
they explain why the kit is used the way it is:

- Each vehicle was a welded `body` mesh plus four separate `wheel-*` nodes. Wheels detach
  for free; **doors, hood and bumpers are not separate geometry.** That is the whole reason
  `tools/blender/split_car.py` exists.
- The `debris-*` models are standalone generic props. Traffic still fakes panel detachment by
  spawning these rather than by removing geometry from the body — see `CarDamage.debrisPrefab`.
- Everything shares the single `colormap` material, so the debris batches with itself and costs
  no extra texture memory.
- Kenney meshes import at **Scale Factor 1.6**; the split cars are 1.0. Mixing the two up
  spawns debris at the wrong size.

---

## Low Poly Cars (free tier) — REMOVED 2026-09-02

- **Author:** Cosmo — https://cosmo-art.itch.io
- **Source:** https://cosmo-art.itch.io/low-poly-cars
- **License:** [CC0 1.0 Universal](http://creativecommons.org/publicdomain/zero/1.0/) (public domain)

**Deleted from the repo.** The 12 free-tier vehicles and `coupe-split.fbx` were imported
2026-08-29 as candidate player cars, then superseded the same day by the BMW E30, which reads as
more realistic. Nothing referenced any of them afterwards, so they shipped in no build and were
removed during the 2026-09-02 cleanup.

Recorded rather than deleted outright because the reasoning is still useful: the pack was picked
over Kenney's for **proportion, not triangle count** — proper greenhouse, thin pillars, correctly
sized wheels — which is the same judgement that later chose the E30. CC0, so no obligation
survives their removal.
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

---

## Everest course texture

- **Author:** Rob Tuytel / Poly Haven — https://polyhaven.com
- **License:** [CC0 1.0 Universal](http://creativecommons.org/publicdomain/zero/1.0/) (public domain)
- **Attribution required:** No. Credited voluntarily.
- **Location:** `Assets/Art/Tracks/Everest/Textures/`
- **Retrieved:** 2026-08-31

| File | Poly Haven asset | Used for |
| --- | --- | --- |
| `everest_snow_diff_1k.jpg` | `snow_03` | `CourseGround` — the drivable face |

Base colour only at 1K, 0.57 MB. The rock material reuses Quarry's `quarry_rock_diff_1k` with a
cold tint rather than downloading a second one — the download budget is the reason, and a tinted
grey rock is indistinguishable from a cold one at the distance the walls are seen.

Chosen over `snow_05`, which is patterned with **tractor tyre tracks**: they would tile across the
whole mountain as repeating tread marks. `snow_03` is patchy snow over dark ground with no
directional features, which also reads better here — snow clinging to a rock face rather than a
clean white sheet, so the mountain shows through it.

---

## LCT 3000 '95 — box truck

- **Author:** Daniel Zhabotinsky
- **Source:** https://sketchfab.com/3d-models/lct-3000-95-low-poly-model-663a0953c038434a918cb85725c88ffa
- **License:** [CC Attribution 4.0](https://creativecommons.org/licenses/by/4.0/)
- **Attribution required:** **YES.** Credit "LCT 3000 '95 by Daniel Zhabotinsky (CC-BY)"
  in-game, on the car select screen alongside the two ROH3D cars.
- **Location:** `Assets/Art/Vehicles/LCT3000/`
- **Retrieved:** 2026-08-31

Measured, not quoted: 18,947 tris / 11,865 verts across **27 mesh objects**, authored in
**metres at real-world scale** — 6.02 × 2.61 × 2.84 m, so no `--scale` is needed. It is
also already grounded, with the tyres resting within a millimetre of z = 0.

**This is the first model in the project that arrives pre-split**, and it changes the job.
Where the E30 and P72 are welded shells that had to be carved, this one ships separate
bumpers, rear box doors (with inner panels), a full cargo-bay liner, a cab interior, the
suspension, and all four wheels **already named per corner** as `WheelStock_FL/FR/RL/RR`.
The artist's cuts follow the real panel gaps, which no region box can match, so they are
kept rather than re-carved — hence `--keep` in `split_car.py`.

Two objects are worth knowing about because they replace things this project normally has
to generate:

- **`Bottom` (5,772 tris)** is not just an underside. It is the full dark inner surface set:
  cargo-bay walls, roof and floor, chassis rails, mudguards, tail-lift frame, and the cab's
  inner shell. It is also where the **mirrors** live, which is why the truck measures 2.61 m
  wide against a 2.18 m body.
- **`Interior` (3,253 tris)** is a real cab interior — seats, dash, door cards.

Between them they do the job `InteriorShell` exists for, with real geometry instead of a
shrunken copy, so the truck is split with **`--no-shell`** and has no `InteriorShell` at all.
This is exactly the "detached panels over a dark interior" the reference footage shows.

Dropped on import: `Body_Badges` (26 tris) and both number plates (160 tris). Between them
they dragged in two extra materials and a **1.6 MB** badge texture, which is not a defensible
trade for 186 triangles nobody will read at speed. That takes the truck from 7 materials to 5.

Textures were **5.2 MB** of PBR sets (dif/height/met/rough for eight materials). Reduced to
**916 KB of base colour only**, following the E30 and P72 precedent — normal, metallic,
roughness and height dropped entirely:

| File | Source | Size |
| --- | --- | --- |
| `lct3000_body.png` | `Generic_bodymat_dif` 2048² | 1024² |
| `lct3000_lights.png` | `UCB_Lights_and_Glass_Dif` 2048² | 512² |
| `lct3000_interior.png` | `UCB_Interiors_2_Dif` 2048² | 512² |
| `lct3000_bottom.png` | `UCB_BOTTOM_DIF` 1024² | 512² |
| `lct3000_tire.png` | `RB1c_Tire_1k_Dif` 1024² | 512² |

`lct3000-split.fbx` is derived from `Source/lct3000.fbx` by `tools/blender/split_car.py`.
CC-BY permits modification provided attribution is kept.

---

## Lamborghini Aventador — fastest car

- **Author:** Arion Digital (@andrewswihart)
- **Source:** https://sketchfab.com/3d-models/lamborghini-aventador-888e37a3641d4f7b94bc1a39396e2441
- **License:** [CC Attribution 4.0](https://creativecommons.org/licenses/by/4.0/)
- **Attribution required:** **YES.** Credit "Lamborghini Aventador by Arion Digital (CC-BY)"
  in-game, on the car select screen alongside the other three.
- **Location:** `Assets/Art/Vehicles/Aventador/`
- **Retrieved:** 2026-09-01 (model published 2019-12-16)

Licence confirmed through the Sketchfab API rather than the download, which carried no licence
file — the same check the LCT 3000 needed. The FBX's own timestamp (2019-12-16) matches the
model's publish date exactly, which is what identified WHICH of the many free Aventador uploads
this actually is.

Measured, not quoted: **10,374 tris across 7 objects**, authored in **centimetres** (4.89 m long
at cm scale). Sketchfab reports 10,252 faces; the difference is n-gon triangulation. Structure is
unusually good for a free model:

- `Body`, `Glass` and four separately named wheels — `Wheel_FL/FR/RL/RR`, already per corner.
- Wheelbase **2.75 m** and track **1.70 m**, against the real car's 2.70 and 1.72. Front wheels
  are 0.69 m across and rears 0.73, which is correct for the car and is also how the nose
  direction was identified.
- A 122-triangle `Collider` proxy, **dropped** — this project builds its own three collision
  boxes and a mesh collider on a car is the wrong shape anyway.
- Three materials: `Lamborginhi_base_phong`, `Lamborginhi_glass_phong`, and the collider's.
  Glass being its own material is what lets `CarGlass` work on this car, unlike the P72.

**⚠ THE DOWNLOAD IS AN ASCII FBX, WHICH BLENDER CANNOT OPEN AT ALL.** Unity imports it happily,
so the model looks fine while the entire Blender pipeline — inspection, splitting, previews — is
unavailable. `tools/blender/fbx_ascii_to_binary.py` was written for it. See CLAUDE.md.

Textures were **3.0 MB** of diffuse/spec/gloss. Reduced to a single **0.57 MB
`aventador_body.png`** — base colour only, 2048² down to 1024², spec and gloss dropped entirely,
following the E30, P72 and LCT 3000 precedent.

Build, in two steps because of the ASCII format:

```bash
"$BL" --background --python tools/blender/fbx_ascii_to_binary.py -- \
  --input Assets/Art/Vehicles/Aventador/Source/aventador.fbx \
  --output /tmp/aventador-bin.fbx --scale 0.01

"$BL" --background --python tools/blender/split_car.py -- \
  --input /tmp/aventador-bin.fbx \
  --output Assets/Art/Vehicles/Aventador/aventador-split.fbx \
  --tris 11000 --profile midengine --nose +z --up y --drop "Collider"
```

`aventador-split.fbx` is derived from `Source/aventador.fbx`. CC-BY permits modification
provided attribution is kept.

**Trademark note, for completeness:** Lamborghini is a trademarked marque, as are BMW and De
Tomaso, both already in this game. The CC-BY licence covers the MODEL, not the brand. For a
non-commercial school project that is the same position the other three cars are already in; it
would need looking at properly before anything is ever sold.
