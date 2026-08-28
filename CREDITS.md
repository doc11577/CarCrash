# Credits

Third-party assets used in CarCrash. Recorded as each one is added, not reconstructed later.

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
