# CarCrash

Arcade crash-driving game. Unity, deployed as a Web build embedded in a Google Site so it
runs on locked-down school Chromebooks.

## Working rules

These are standing instructions. Follow them without being reminded.

### 1. Always give the Unity wiring steps

Ethan is not a C# developer. Writing the script is only half the job — a script sitting in
`Assets/` does nothing. Every time code is added or changed, spell out the Editor steps:

- Which GameObject to create, and its exact hierarchy position
- Which component to add to which object
- Every Inspector field to set, with the value
- What to drag into which slot
- Which layer / tag / physics-layer settings matter
- How to verify it worked, and what "working" looks like on screen

Never say "hook this up in the Inspector" and stop. Enumerate it. If a step is ambiguous,
say what the default should be rather than leaving it open.

### 2. Optimize aggressively — the target is a bad Chromebook

This is a hard constraint, not a nice-to-have. Assume the worst realistic school device:
4 GB RAM, integrated graphics, a weak mobile-class CPU, and a browser tab that dies if the
WASM heap spikes. Performance is a design input from the first line of code, not a cleanup
pass at the end.

Standing budget:

- **60 FPS target, 30 FPS floor** on integrated graphics at 720p
- **Draw calls:** keep low; batch and atlas aggressively. Static geometry must be marked static.
- **Triangles:** low-poly on purpose. The art style is the optimization.
- **Textures:** small, atlased, crunch-compressed. No 2K textures anywhere.
- **Physics:** the expensive system in this game. Simplify colliders, cap rigidbody counts,
  sleep and despawn detached parts, keep `Fixed Timestep` sane, minimize contact pairs.
- **No realtime shadows** unless proven affordable. Bake lighting where possible.
- **No post-processing stack** beyond what's measured and justified.
- **Build size matters** — every MB is download time on school Wi-Fi.

Before adding any feature, state its performance cost. If a cheaper approach gets 90% of the
look or feel, take the cheaper approach and say why. Profile claims, don't assert them.

### 3. Total honesty. No flattery, no softening.

- Do not open with praise. Do not call ideas great.
- If something won't work, say it won't work and why, immediately.
- If a request is a bad idea, say so plainly before doing it — then do it if Ethan confirms.
- If something is broken, unverified, or was skipped, say so. Never imply work is done or
  tested when it isn't.
- Distinguish clearly between "I verified this" and "this should work."
- Don't hedge to be polite. Don't pad with reassurance. Blunt and correct beats gentle and vague.

### 4. Keep this file current

Update `CLAUDE.md` periodically without being asked — after any architectural decision,
new convention, changed constraint, or resolved gotcha. Do not wait to be prompted. If a
decision was made in conversation and it isn't written down here, it will be lost.

## Environment

| Thing | Value |
| --- | --- |
| Project root | `C:\Users\ethan\Documents\GitHub\CarCrash` |
| Unity | `6000.3.8f1` (Unity 6.3 LTS, supported to Dec 2027) |
| Render pipeline | URP 17.3.0 |
| Build target | **Web** (renamed from WebGL in the Unity 6 line) |
| Graphics API | **WebGL2 only.** WebGPU stays off — unreliable on Chromebooks. |
| Input | Input System package (`com.unity.inputsystem`) |
| Git remote | `https://github.com/doc11577/CarCrash.git` (public — required by jsDelivr) |
| Default branch | `master` |

`Documents` is confirmed **not** OneDrive-redirected, so the project path is safe.
Do not move it under `C:\Users\ethan\OneDrive\` — OneDrive sync corrupts Unity's `Library/`.

## Deploy pipeline

Copied from the reference game that already runs on these Chromebooks.

**Build output goes to `WebBuild/carcrash/`** (gitignored). The folder name sets the output
filenames, so it must stay `carcrash`. Only the four payload files are copied into
**`prod/`**, which *is* tracked, because jsDelivr serves from the repo.

Release steps:

```bash
bash tools/publish.sh          # WebBuild/carcrash/Build -> prod/
git add prod && git commit -m "..." && git push
git rev-parse HEAD             # copy this hash
# paste hash into BUILD_BASE in tools/embed.html
# re-paste tools/embed.html into Google Sites -> Insert -> Embed -> Embed code
```

`tools/embed.html` is the page pasted into Google Sites. It is *not* Unity's generated
`index.html` — it's a hand-written replacement with a progress bar, on-screen error
reporting (there's no console on a school Chromebook), iframe focus handling, and
arrow-key scroll suppression.

Non-obvious things that will bite:

- **Pin jsDelivr to a commit hash, never a branch.** Branch URLs are cached hard and will
  serve a stale build for hours.
- **Decompression Fallback must stay ON.** jsDelivr can't send `Content-Encoding: br`, so
  Unity has to decompress in JS. Output named `.unityweb` = fallback on. `.br` = it's off
  and the game will hang forever at 0%.
- **The repo must stay public.** jsDelivr cannot read private repos.
- **jsDelivr caps files at 20 MB.** Watch `carcrash.wasm.unityweb` as the project grows.
- **Keyboard focus.** Google Sites nests the game two iframes deep; without an explicit
  focus grab on pointerdown, key input silently does nothing.

It works at school because `sites.google.com` is Workspace-whitelisted and `cdn.jsdelivr.net`
is a generic CDN that filters don't block. There is no trick beyond that.

**Test the full pipeline on a real school Chromebook before building anything worth losing.**

### Build baseline (smoke test, 2026-08-27)

Brotli, ~10.4 MB total download:

| File | Compressed | Raw |
| --- | --- | --- |
| `carcrash.wasm.unityweb` | 6.49 MB | 34.79 MB |
| `carcrash.data.unityweb` | 3.71 MB | 9.24 MB |
| `carcrash.framework.js.unityweb` | 0.07 MB | 0.32 MB |
| `carcrash.loader.js` | 0.11 MB | — |

Reference game for comparison: ~14.7 MB. Watch for regressions against this table.

### Web player settings (verified on disk)

`webGLDecompressionFallback: 1`, `webGLMaximumMemorySize: 512`, `webWasm2023: 1`,
`managedStrippingLevel: WebGL: 3` (High), `webGLCompressionFormat: 0` (**0 = Brotli** —
the enum is Brotli, Gzip, Disabled, so 0 is correct), `webGLThreadsSupport: 0` (must stay
off — threads need COOP/COEP headers Google Sites will never send), `webGLDataCaching: 1`.

`webGLExceptionSupport: 1` (Explicitly Thrown Only) is kept for development stack traces.
**Drop it to None before shipping** for the speed.

Untested lever if the Chromebook is slow: capping `devicePixelRatio` to 1. HiDPI Chromebooks
render far more pixels than the GPU can afford. Measure before reaching for it.

## NEXT SESSION — pick up here (rewritten 2026-08-29, late)

The E30 is imported, wired and **driving**. Everything in the old step-by-step wiring block
is done and has been deleted; what follows is only what is still open.

### State on disk

Nothing is committed. Uncommitted: `tools/blender/` (3 scripts),
`Assets/Art/Vehicles/CosmoCars/`, `Assets/Art/Vehicles/BMW-E30/`,
`Assets/Settings/CarBodySlip.physicsMaterial.physicMaterial`, **three new scripts
(`Damage/CarDeformation.cs`, `Damage/CarGlass.cs`, `Damage/CarInteriorProps.cs`)**, and edits to
`CarController.cs`, `CarDamage.cs`, `DebrisPool.cs`, `ChaseCamera.cs`, `CLAUDE.md`,
`CREDITS.md`. The E30 FBX meta was changed to `isReadable: 1`, which deformation requires.
`SampleScene.unity` carries the E30 wiring plus a small **pre-existing, unrelated** loading-bar
transform change from an earlier session.

### Confirmed working in play mode

Car sits correctly on its tyres, drives, steers, wheels spin the right way and ride the
suspension. Four fixes got it there, all in `CarController.cs`, all documented under
*Architecture calls already made*: `wheelVisualEuler`, the cast overshoot, lateral grip at
the contact patch, and the bump stop / anti-roll bar pair.

**Deformation reads correctly** after a long tuning fight -- panels dent and cave in, no spikes,
no bulges, no black patches. Roof crush and sustained grinding are implemented but were added
after the last play test.

### Never run in Unity — still inspection-clean only

- **Panel detachment against real geometry.** Rammed once, 2026-08-29: the whole shell came off
  at once and was fired sideways. Both causes found and fixed (`maxDamagePerImpact`,
  `detachGrace` -- see the damage model). NOT re-tested since the fix.
- **`Repair()`.** Both of its wheel bugs are fixed but unrun. `BoltBackOn` is still the code
  to suspect if a panel stays on the ground or comes back wrong.
- **Detached wheels as debris.** The fix is reasoned, not observed.
- **The camera look-around** (hold left mouse while stopped).
- **`CarDeformation` dents correctly but the depth was wrong.** Played 2026-08-29: dents worked,
  but at `maxDisplacement 0.15` they punched the body through the InteriorShell and the car went
  near-black, and the flat depth cap made dents read as the whole panel being shoved. Fixed with a
  per-vertex shaped budget, `crumple`, a smoothstep profile, and separating per-hit bite from the
  eventual cap. Then dents vanished, because mesh-local units are x100 on this model. **Measured
  after that: impulse ~16,500, damage ~702, 308 verts moved, 280 cm requested against a 9 cm cap.**
  Depth was then capped by the `InteriorShell`, so the shell is now **deformed along with the
  paint** and that ceiling is gone. `crush` added for a caved-in look, `crumple` made one-sided and
  `rimBulge` zeroed to kill spikes, and `crush` rewritten twice -- the second time because an
  unbounded sideways gather hauled bodywork up over the roof. **Confirmed reading well in play.**
  Added since that test and NOT run: multi-contact denting (roof flattening) and `OnCollisionStay`
  sustained crushing.

  **Re-watched the reference footage: it does almost no deformation at all -- its damage is
  detached panels, missing glass and dark interiors.** That is why `CarGlass` and
  `CarInteriorProps` exist, and why dent depth is not worth pushing further.

### Open, in rough priority order

1. **Play-test the suspension numbers.** `bumpStopStrength` 60000 and `antiRollStrength` 4000
   are reasoned starting points, never driven. Inside wheels lifting mid-corner → drop
   `antiRollStrength` to ~2000. Still sitting on its belly at speed → cut `downforce` from
   0.9 to ~0.35 (see the load budget under *Architecture calls*), not `springStrength`.
2. **Wire the new components and the trunk in the Editor.** Add `CarGlass` and
   `CarInteriorProps` to `Car`, and add a 12th part -- name `trunk`, Visual `PartTrunk` (under
   `e30-split`), Anchor None, Health 120, Wheel Index -1. `PartTrunk` exists in the mesh and has
   never been wired, so the boot lid still cannot come off.
3. **Play-test glass, interior props, roof crush and sustained grinding.** All four are written
   and none has been run.
4. **Damage thresholds are only half-calibrated.** `lastImpulse` **measured ~16,500** on a wall
   hit, so `minimumImpulse` 900 gates out almost nothing and `maxDamagePerImpact` 60 is what
   actually makes damage progressive. Read `lastImpulse` at other speeds before changing either.
5. **In-game attribution for the E30 is not built.** CC-BY makes it mandatory. Garage screen.
6. **Rebuild and re-measure download size** against the baseline table above.
7. **Get a real frame-rate number off a school Chromebook.** Still the largest unknown in the
   whole project — every performance claim in this file is reasoning, not measurement.

### Known latent, not yet worth fixing

- **`Wheel.spin` grows without bound.** At `topSpeed` it gains ~6,100 deg/s, so after roughly
  ten minutes of driving it passes 3.7M degrees and float precision degrades to about half a
  degree — visible as wheel jitter. One `% 360f` fixes it whenever a run gets that long.
- **`split_car.py` does not reorient geometry**, which is what forced `wheelVisualEuler` to
  exist. Fixing it at source means a re-export and a full re-wire; not worth it for one car.

## Roadmap

Live kanban board (add/move/delete cards, saves itself):
https://claude.ai/code/artifact/1f62aafd-5b63-417b-b9b7-8d035c0a909a

Build order — expensive unknowns first, content last:

1. **Done** — deploy pipeline · repo · player settings · size baseline · chase camera ·
   vehicle controller · greybox track · car model · R-to-restart · detachable parts ·
   BMW E30 picked, split, imported and **driving** · suspension made stable
   (cast overshoot · bump stop · anti-roll bars · roll direction)
2. **Now** — **play-test the suspension numbers** · tune damage thresholds ·
   ram a wall and confirm panels dent, and panels and wheels detach · add in-game CC-BY attribution
   for the E30 · rebuild and re-measure download size ·
   **get a real frame-rate number off a school Chromebook**
3. **Next** — cheap traffic · scoring & gears · garage and buy · persistence
4. **Later** — split-screen 2P · juice · audio · more content · ship pass

### Architecture calls already made

- **Custom raycast suspension, not `WheelCollider`.** Four raycasts per car per physics
  step instead of PhysX's wheel solver. Cheaper, far more predictable, and arcade feel is
  the goal rather than simulation.
- **Traffic is kinematic until struck**, then promoted to a full rigidbody. "Physics on
  demand" is what makes ~20 cars affordable on integrated graphics.
- **Mesh deformation is player-car only**, on a low-poly proxy, with a budgeted vertex
  count per hit. Traffic gets detachable parts but keeps its mesh intact.
- **Detached debris is pooled and capped.** Lifetime, then sleep, then back to the pool.
  This is the first system that will tank the frame rate if left unbounded.
- **The camera takes no player input the player has to manage.** Target platform is a
  school Chromebook trackpad; any camera you *must* steer is a failure. `ChaseCamera` is
  automatic. Revised 2026-08-29: there is now **one optional exception** — hold left mouse
  and drag to look around **while stopped**. It obeys the spirit of the rule rather than
  breaking it, and all three guards are load-bearing: driving above `lookCancelSpeed`
  (2 m/s) ignores the drag outright, the offset decays back to zero after
  `lookReturnDelay`, and it is an offset *on* the live automatic rig rather than a
  replacement for it — so letting go returns to framing that is already correct. You
  cannot leave the camera pointing somewhere useless and have to fix it mid-run.

- **Wheel ground contact is a SphereCast, not a Raycast.** A ray is infinitely thin, so it
  drops into cracks, misses rock edges by a centimetre and stabs through polygon seams,
  each of which loses a corner's support for a frame and reads as the car catching on
  nothing. A sphere of `wheelRadius` is what a real contact patch does: it cannot fall into
  a gap narrower than the wheel and it rides up over edges. ~2-3x a raycast, four per
  physics step — invisible in a profile. `wheelSphereCast` toggles it for A/B comparison.
  Note the two casts report distance differently: the sphere sweep already excludes the
  radius, the ray does not.
  **The sweep must start ABOVE the anchor, not at it.** Unity does not report colliders a
  sphere is already overlapping at the start of a sweep, so a cast from the anchor goes blind
  as soon as the body sinks within one `wheelRadius` of the road — killing the spring at the
  exact moment it is needed, so the corner drops further and stays blind. A runaway with no
  exit, and it reads as "the wheels sink through the floor and it will not drive". `ApplyWheel`
  now starts the sweep at `anchor + up * wheelRadius`, extends the length by the same
  `overshoot`, and subtracts it back off. Two consequences worth knowing: sink margin before
  the cast can fail goes from `0.20 m` to `0.50 m` — the anchor's whole rest height,
  `wheelRadius + 2/3 x travel` — and `centreTravel` may now be **negative**, which `Clamp01`
  turns into compression 1: maximum push-back, which is what levers a bottomed-out car back
  up. Droop headroom is unchanged at `0.10 m`. Do not "simplify" this back to casting from
  the anchor.
- **Lateral grip is applied at the CONTACT PATCH, not at the wheel anchor.** The height a
  tyre's grip is applied at decides which way the body rolls, and the anchor is the wrong
  height. The effective centre of mass is at **y ≈ 0.24**, not `-0.6`: `Awake` does
  `rb.centerOfMass += centreOfMassOffset`, so the offset is a *nudge* on Unity's
  collider-derived centre (the three boxes average to **y ≈ 0.837** by volume), giving
  `0.837 - 0.6 = 0.237`. The anchor at `y = 0.500` is **above** that, so pushing there tipped
  the car **into** the corner — inside wheel dipping, like a motorbike. From the contact patch
  at `y = 0` the couple reverses and the car leans out of the turn correctly.
  Drive and brake forces deliberately **stay at the anchor**; the squat and dive they produce
  is good arcade feel. `OnDrawGizmosSelected` was drawing the raw offset, putting the yellow
  centre-of-mass marker 0.6 m *under the road*; it now draws `centerOfMass + offset`.
- **Bottoming out is fixed with a bump stop and an anti-roll bar, not with a stiffer spring.**
  A linear spring gives its travel away evenly — the last centimetre costs no more than the
  first — so downforce plus cornering load walks a corner to full compression and the body
  sits on its belly. Two targeted parts instead of one blunt one:
  - `bumpStopStart` (0.70) / `bumpStopStrength` (60000): past 70% compression the rate climbs
    **quadratically**. Small bumps keep the supple ride the SphereCast exists to provide;
    the bottom of the travel resists hard. Raising `springStrength` instead would stiffen
    everything *and* change the rest ride height the wheel anchors were derived for.
  - `antiRollStrength` (4000, per axle): acts only on the **difference** between the two sides,
    so straight-line ride, single-wheel bumps and landings are untouched. Requires **both**
    corners grounded — a bar levering off an airborne wheel invents force and flips the car.
    Too high lifts the inside wheels mid-corner. Costs no casts and no allocations.

  **Load budget, for whoever tunes this next.** Static is `2943 N` per corner (1200 kg / 4).
  Rest compression is `2943 / 9000 = 0.33`, which is what put the anchors at
  `wheelRadius + 2/3 x travel`. That leaves only `(1 - 0.33) x 0.30 = 0.20 m` of downward
  travel, and **`downforce = 0.9` spends 8.6 cm of it — 43% — before you touch anything**:
  at `topSpeed` it adds `0.9 x 1200 x 9.81 = 10,595 N`, taking each corner to
  `(11,772 + 10,595) / 4 = 5,592 N` and compression to `0.62`. Cornering load transfer then
  goes on top. **If it still sits low at speed, `downforce` is the first number to cut, not
  `springStrength`** — it is serialized in `SampleScene` at 0.9, try **0.35** (sag falls to
  ~3.2 cm) and it does not disturb rest ride height at all.
- **Wheel mesh orientation is a per-model Inspector value, `CarController.wheelVisualEuler`.**
  `UpdateVisual` sets `visual.rotation` **absolutely**, so it must know which mesh axis is
  the axle. Zero assumes the Unity convention, axle along local X. The split E30 is authored
  axle-along-**Y** and needs **`(0, 0, -90)`**, which is the code default. The correction is
  composed **last** (`LookRotation * Euler(spin,0,0) * Euler(wheelVisualEuler)`) so it acts in
  the mesh's own space and changes neither the steering direction nor the sign of the roll.
  Wrong value → wheels sit sideways and tumble end-over-end instead of rolling.
  Not fixable in the Inspector by rotating the mesh, and **not** fixable by parenting the mesh
  under a rotated empty: `CarDamage` needs `Part.visual` to be the object that actually carries
  the `MeshFilter`, for both `PartPosition()` and `ThrowRealPart()`'s collider sizing.
- **The camera tracks the car's VELOCITY, not its facing.** In a game built around spins
  and broadside hits, a facing-locked camera whips around and makes people ill. Below
  `velocityYawThreshold` it falls back to facing so parking still reads correctly, and
  reversing is special-cased so the view doesn't swing to the boot.
- **The camera aims at the road ahead, not at the car** (`aimAheadWeight`). This is the
  actual fix for the reference game's downhill problem — the aim point is ground-projected,
  so on a descent it sits below the car and the camera pitches down on its own.

### Open calls

- **Colour space** — recommendation: stay **Linear**. Bottleneck will be CPU physics, not
  fragment shading. Revisit only if the GPU proves to be the wall. Decide before lighting
  the first real scene; changing it later re-breaks every lighting value.
- **Device pixel ratio cap** — measure before capping. One-line change, costs sharpness.

### Provisional performance budget

60 FPS target / 30 floor · ≤ 20 MB download · ≤ 40 live rigidbodies · no realtime shadows ·
no post FX until measured. Revise once there are real Chromebook numbers.

## Scripts

| Script | Job |
| --- | --- |
| `Camera/ChaseCamera.cs` | Automatic chase cam. No player input, ever. |
| `Vehicle/CarInput.cs` | Keys → throttle / steer / handbrake. Has a `Scheme` for split-screen. |
| `Vehicle/CarController.cs` | SphereCast suspension with bump stops and anti-roll bars, drive, grip, steering, air control. |
| `Damage/CarDamage.cs` | Impacts → part damage → detachment. Fires `Damaged` and `PartLost`. |
| `Damage/CarDeformation.cs` | Per-vertex denting on impact. Player car only. |
| `Damage/CarGlass.cs` | Empties the glass submeshes past a damage threshold. |
| `Damage/CarInteriorProps.cs` | Generates the dark engine bay / cabin a missing panel reveals. |
| `Damage/DebrisPool.cs` | Pools, caps, expires and sleeps detached parts. `Track()` adopts real panels. |
| `Game/RunRestart.cs` | R reloads the scene behind a loading bar. |
| `Debug/PerfReadout.cs` | On-screen FPS/device readout. **Delete before shipping.** |

### Scene setup (SampleScene)

- **Car** — Rigidbody (1200 kg, Interpolate, Continuous), layer `Car`, with `CarInput`,
  `CarController`, `CarDamage`. The `coupe-split` FBX is a child at origin, **Scale Factor 1.0**.
- **Wheel anchors** `WheelFL/FR/RL/RR` at local `(±0.877, 0.61, +1.776 / -1.345)`, with
  `wheelRadius = 0.41`. The `0.61` is derived, not guessed: `wheelRadius + ⅔ × suspensionTravel`
  puts the suspension at ⅓ compression when parked. `springStrength` 9000 is unchanged —
  compression is normalised 0–1, so 9000 × ⅓ ≈ the 2943 N per corner a 1200 kg car needs.

#### Car collision — three boxes, not one

A single body-sized box makes the car unable to climb ramps the tyres could easily take.
Measured on the one-box setup: **14.4° approach, 12.6° departure, 17.5° breakover** — the
box bottom sat 0.24 m up but overhung the front wheel by 0.94 m, so its front-bottom edge
caught every slope over 14°. Raycast suspension will climb almost anything the ray can
find; the overhanging collider edge was the whole limit.

Three `BoxCollider` components on the **same** GameObject (Unity allows this — no child
objects, no extra transforms):

| Box | Center | Size |
| --- | --- | --- |
| Core (floorpan, between axles) | `0, 1.02, 0.216` | `1.98, 1.44, 3.12` |
| Nose (raised for approach) | `0, 1.0, 2.245` | `1.9, 0.9, 0.94` |
| Tail (raised for departure) | `0, 1.025, -1.88` | `1.9, 0.95, 1.07` |

Result: **30.4° approach, 27.2° departure, 21.8° breakover.**

All three carry `CarBodySlip` (dynamic/static friction 0.15, **Friction Combine Minimum**,
bounciness 0.05). This matters as much as the shape — at Unity's default 0.6 friction the
body grabs whatever it brushes and a glancing hit on a rock stops the car dead. Low friction
applies to the body only; wheel grip is `CarController`'s own values and is untouched.

Known trade-off: below `y 0.55` there is no collider ahead of the front wheel, so a kerb
under ~0.5 m passes under the nose until the wheel raycast finds its top and lifts the car.
That is the intended behaviour. The cost is that a low wall lets the nose clip in by up to
0.94 m before contact. Lower the Nose Center Y toward `0.85` if that reads badly.
- **Part anchors** `PartHood`, `PartBumperF/R`, `PartDoorL/R`. Wheel anchors double as
  the wheel part anchors.
- **GameManager** — `RunRestart`, `DebrisPool`.
- **Main Camera** — `ChaseCamera` (target = Car), `PerfReadout`.

Layer discipline, because every mask bug in this project looks like a physics bug:
the car is on `Car`; `ChaseCamera` ground/collision masks, `CarController` ground mask
and `CarDamage` damaging layers are all **Default only**. Debris is on `Default` so it
can hit the car that shed it.

Vehicle FBXs import at **Scale Factor 1.6** — car and debris must match or debris spawns
the wrong size.

## Damage model

Impacts are matched to the nearest part anchor within `partReach`; damage is
`(impulse - minimumImpulse) * damagePerImpulse`. At zero health the part detaches,
inheriting the car's velocity at that point.

**Two detachment modes, per part.** `CarDamage.Part` takes either:

- `visual` — **real geometry**. The panel is unparented, given a `BoxCollider` sized from
  the mesh bounds and a Rigidbody, thrown, and handed to `DebrisPool.Track()`. Leaves a
  genuine hole with the `InteriorShell` behind it. Used by the split player car.
- `debrisPrefab` — **a generic prop**, body geometry untouched. Kenney bodies are welded, so
  traffic keeps using this.

Real wins if both are set. `Repair()` strips the Rigidbody and collider and bolts the panel
back onto its remembered parent and local pose.

A `BoxCollider` from mesh bounds is deliberate over a convex `MeshCollider`: cooking a
convex hull at runtime costs a frame hitch, and a tumbling door does not need a faithful
hull. Detached parts move to `detachedLayer` (Default) so they can hit the car that shed them.

**Impact matching uses the panel's mesh centre, not its transform position.** `split_car.py`
puts each panel's origin on its *hinge* so it swings correctly, which makes the origin a bad
answer to "where is this part" — a door's origin sits on its front edge, about a metre from
the door. Measured on `coupe-split`: a hit to the middle of `PartDoorL` is 1.01 m from the
door's origin but only 0.65 m from the mirror's, so the mirror stole door hits and snapped
off on a scrape. `CarDamage.PartPosition()` reads `MeshFilter.sharedMesh.bounds.center`
instead. **Leave `Part.anchor` empty** unless you deliberately want to hand-place one; it
overrides the mesh centre when set.

**A lost wheel is a handling change, not a visual.** `CarController.DetachWheel` sets a
flag that skips that wheel's spring, drive force and lateral grip, so the corner drops onto
the body collider and drags. This falls out of raycast suspension for free and would have
been a fight with `WheelCollider`.

**A wheel is the one part that two systems both own, and that is where both of its bugs came
from.** `CarDamage.Part.visual` and `CarController.Wheel.visual` point at the *same* transform
for the four corners, so detaching one has to satisfy both. Two rules now enforce that:

- **`DetachWheel(index, hideVisual)`.** `CarDamage` passes `hideVisual: part.visual == null`.
  It used to hide unconditionally, and since it runs *before* `ThrowRealPart`, the panel was
  unparented, given a Rigidbody and thrown **while inactive** — which succeeds silently and
  does nothing. Detached wheels vanished instead of tumbling, and `DebrisPool` was handed a
  dead object. Only hide the mesh when nobody else is taking it.
- **`Repair()` must call `ReattachWheel`, not just `BoltBackOn`.** `BoltBackOn` restores the
  mesh; `CarController.Wheel.detached` is a **separate** flag and gates the corner's spring,
  drive force and lateral grip. Clearing one without the other gives a car that looks whole
  and permanently drags a corner. Hidden until now only because `RunRestart` reloads the whole
  scene — any in-place repair (the garage) would have hit it immediately.
  `BoltBackOn` also sets `isKinematic` / `collider.enabled = false` **before** `Destroy`,
  because `Destroy` is deferred to end of frame and `CarController` starts writing that
  transform again the instant the flag clears.

### Soft-body deformation — ruled out, do not revisit

BeamNG-style node/beam soft body is **not possible** here. It runs hundreds of mass-spring
nodes per car across multiple desktop cores; we have one WASM thread (`webGLThreadsSupport`
must stay off — threads need COOP/COEP headers Google Sites will never send) and physics is
already the most expensive system in the game.

What replaces it, and what actually reads as realistic on camera:

1. **Real separate panels that detach** — doors, hood, trunk, bumpers, mirrors, wheels.
2. **Per-vertex denting on impact** — `Damage/CarDeformation.cs`, built 2026-08-29.
3. Weight, camera lag and shake.

Splitting the body into panels makes denting **cheaper**, not more expensive: a hit to a
separate 800-vert door deforms 800 verts instead of touching a mesh containing the whole
car. The split is a prerequisite for affordable deformation, not a competing feature.

#### `CarDeformation` — how the denting works

`CarDamage.OnCollisionEnter` calls `Dent(contact, collision.impulse, damage)` **before** it
decides whether the part comes off, so a fatal hit still leaves its dent on the piece that
flies away. Vertices within `radius` of the impact are pushed along the impact direction with
a **quadratic** falloff — linear leaves a visible cone, squaring rounds the rim off so it
reads as sheet metal.

Decisions that are load-bearing, do not undo them:

- **Meshes are cloned at Awake.** Denting `sharedMesh` writes into the imported asset and the
  damage persists across play sessions — in the Editor that permanently wrecks the FBX.
- **The E30 FBX needs `Read/Write Enabled` ON** (`isReadable: 1`). Without it `mesh.vertices`
  returns empty and every dent silently does nothing, with no other symptom — so `Awake`
  `Debug.LogError`s on a non-readable mesh rather than failing quietly. Costs a CPU-side copy
  of the mesh, well under 1 MB for this car.
- **Clamp against the ORIGINAL vertex pose, never the previous value.** Clamping incrementally
  lets a long scrape walk a vertex arbitrarily far in many individually legal steps.
- **The direction argument is guarded.** `collision.impulse`'s sign convention depends on which
  body Unity considers first, so anything pointing away from the car is replaced with a
  straight-inward push. A flipped convention costs directionality, never panels blown outward.
- **Wheels are excluded by reading `CarController.wheels`, not by name.** Name matching is how
  this project has been bitten before — `trim` contains `rim`. `InteriorShell` is excluded by
  exact name because it is the dark surface a missing panel reveals: denting it inward opens
  gaps around the holes, outward erupts through the paint.
- **Colliders are never re-cooked.** Denting is visual only; the three body boxes are unchanged.
  Building a collider from a deformed mesh at runtime costs a frame hitch and nobody can tell.
- **Bounds are not recalculated.** Dents only move vertices inward, so stale bounds stay
  conservative and culling stays correct. Normals *are* recalculated (`recalculateNormals`,
  ~0.05-0.2 ms per panel at impact time) or the dent is lit as though it were still flat.
  Tangents are not — no material on this car uses a normal map.

**`maxDisplacement` MUST stay below the InteriorShell clearance. This is the constraint that
governs how deep a dent can ever be.** Found the hard way 2026-08-29: at `maxDisplacement 0.15`
the car turned **near-black** wherever it was hit. The shell is the body scaled by
`SHELL_SCALE = 0.93` about the body centre, so its clearance behind the paint is *proportional
to distance from that centre*, not uniform:

| Surface | Distance from body centre | Clearance |
| --- | --- | --- |
| **Roof** | 0.605 m | **4.2 cm** ← the binding constraint |
| **Flanks** | 0.84 m | 5.9 cm |
| Nose / tail | 2.08 m | 14.6 cm |

Dent deeper than that and the panel passes *through* the shell. Because the shell has flipped
normals, you then see its far inner wall — near-black, and you can see the seats through the
roof. The giveaway that it is this and not lost geometry: the car still has every panel and
wheel, and the black areas are the tight-clearance ones (roof, wings, quarters) while the nose,
tail and doors stay painted.

**MEASURED, 2026-08-29 — the first real numbers off a wall hit. Use these, do not re-derive.**

| Quantity | Measured |
| --- | --- |
| `collision.impulse.magnitude` on contact | **~16,500** |
| Damage it produces, `(impulse - 900) x 0.045` | **~702** |
| Depth that requests at `strengthPerDamage 0.004` | **280 cm** |
| Vertices moved, `radius 0.28` | **308** |

Two things this settled, both of which had been argued from first principles and got it wrong:

- **The original impulse arithmetic was right.** 1200 kg losing 20 m/s ≈ 24,000 N·s is the right
  order, and `OnCollisionEnter` does *not* report a uselessly small first-step fraction. A brief
  detour claiming it did led to `strengthPerDamage` being raised 16x to 0.004; that was wrong and
  is reverted to **0.00013**, sized so a 702-damage hit just reaches `maxDisplacement`.
- **`maxDisplacement` is the binding constraint on visible damage, and normally the ONLY one.**
  The requested depth was 280 cm against a 9 cm cap — a 31x overshoot — so every vertex saturates
  and `strengthPerDamage` changes nothing at all. Raising it "to make dents deeper" is wasted
  effort; it only controls gradation between light and heavy hits, and only while the request
  stays below the cap.

**308 vertices moved also cleared the mesh density worry.** A `radius 0.28` patch bites plenty of
geometry on this model; coarseness is not what limits dent quality here, depth is.

**Second measurement, after raising the cap to 0.15 and the shell to scale 90:** damage 748,
requested **9.73 cm**, applied **7.81 cm**, 370 verts. The binding constraint had moved --
`maxDisplacement` was no longer the limit, `strengthPerDamage` was, and the result was a 7.8 cm
bowl spread over a **56 cm diameter** which is too shallow and too wide to catch the light on a
flat white panel. **Always read `lastRequestedCm` against `lastAppliedCm` against `maxDisplacement`
to see which of the three is actually binding before changing any of them** -- they take turns, and
changing the wrong one does literally nothing.

**A dent needs to be DEEP AND NARROW to read on screen, not deep and wide.** `radius` is what
controls that, and it is the least obvious of the three. Halving it concentrates the same depth
into a quarter of the area.

**The `InteriorShell` scale is the real ceiling on damage, and depth has to be raised WITH it.**
Raising `maxDisplacement` alone just turns the car black; shrinking the shell alone changes
nothing. They move together, roughly **5 points of shell scale per 0.05 m of depth**:

| Shell scale | Usable `maxDisplacement` | Reads as |
| --- | --- | --- |
| 95 | ~0.09 | barely visible |
| 90 | ~0.15 | minor |
| 85 | ~0.22 | clear arcade damage |
| 80 | ~0.30 | heavy |

Below about 75 the shell starts sitting so far inside that a detached panel shows an obviously
hollow cavity rather than an interior. That, not the deformation code, is where this tops out
without re-running `split_car.py` at a smaller `SHELL_SCALE`.

**The depth ceiling is gone: `InteriorShell` is now deformed too, and that supersedes the shell
scale table above.** While the shell stayed still, every extra centimetre of dent had to be paid
for by shrinking it, and past about scale 75 a detached panel starts showing a hollow cavity
instead of an interior — so damage was capped by the shell's geometry, not by anything to do with
damage. The shell now takes the same dent **wider and deeper** than the paint
(`shellRadiusScale 1.6`, `shellDepthScale 1.4`), so it retreats ahead of the body at any depth and
can never be caught by it.

Both multipliers are load-bearing. `shellDepthScale` must stay **above 1** or the paint catches the
shell and panels render near-black again. `shellRadiusScale` is above 1 because the shell is
heavily decimated (960 tris against the body's 2,910): its dent is coarse, and denting it over a
wider area stops the corners of that coarse dent poking back through the paint at the crater rim.

The cost is that a panel which has **both** detached and been dented shows a slightly deeper
cavity. That is a much smaller problem than dents nobody can see. **With this in place the
shell's transform scale no longer needs shrinking at all — put it back to 100.**

Depth is now limited only by `maxDisplacement`. Current defaults, tuned deliberately extreme:
`radius 0.55`, `strengthPerDamage 0.0022`, `maxDisplacement 1.0`, `crumple 0.5`,
`crumpleScale 0.22`, `crush 0.75`, `rimBulge 0`, `shellDepthScale 1.25`, and `sustainedScale 0.6`
on `CarDamage`. At `maxDisplacement 1.0` a vertex can travel most of the length of the engine bay,
so the front genuinely folds. **1.4 was tried and is past the useful end** -- deeper than the car
is tall, so the front punches out through the back and the shell inverts.

`shellDepthScale` was pulled back from 1.5 to 1.25 at the same time: it multiplies
`maxDisplacement`, so at these depths 1.5 was moving shell vertices further than the whole car is
tall. It only has to stay slightly ahead of the paint, not race it.

What still limits how good this can look, in order: the **collider never deforms**, so a heavily
crushed car floats off whatever it is resting on; and the body **runs out of vertices to fold**
(2,910 tris) somewhere past half a metre, after which it stretches rather than crumples.

**`rimBulge` does more for readability than depth does.** Displaced metal has to go
somewhere, so a ring just outside the crater is pushed OUTWARD, and the bright ridge it makes
beside the dark crater gives the eye an edge to catch. A smooth bowl on a flat, pale, low-poly
panel reads as almost nothing however deep it is — which is exactly the trap this component fell
into for several rounds of tuning. The ring goes to zero at both ends so the profile stays
continuous, and the per-vertex budget uses `Mathf.Abs(shaped)` because the ring is deliberately
negative.

**`crumple` is hashed from vertex POSITION, not index.** A hard edge stores several vertices at the
same position with different indices; hashing the index gives them different jitter and tears the
mesh open along every seam. The position is scaled to millimetres of world space before rounding,
so co-located duplicates hash identically.

**Crumple noise must be COARSER than the vertex spacing, or it spikes the mesh.** `crumple` was
first hashed at millimetre precision, which gives every vertex an independent jitter value. On the
E30 body — 2,910 tris, neighbours 5-15 cm apart — neighbouring vertices then get displaced by very
different amounts and the dent comes out as spikes sticking out of the panel rather than folded
metal. High-frequency noise needs a dense mesh; this one is nowhere near dense enough.

`crumpleScale` (0.22 m) fixes it by hashing per CELL instead of per vertex, so everything inside a
cell shares one value and the roughness reads as broad folds. **It must stay well above the vertex
spacing** — under about 0.15 on this model it degenerates back to per-vertex noise and spikes
again. It also still makes co-located vertices at a hard edge hash identically, which is what keeps
seams from tearing open.


**Nothing in the dent profile may move geometry OUTWARD on a mesh this coarse. That is the whole
spike story.** Two things were doing it and both are now off or one-sided:

- **`crumple` is one-sided.** It used to be a symmetric jitter around 1.0, so an individual vertex
  could travel *further* than the smooth profile. With neighbours 5-15 cm apart there is nothing
  nearby to blend an overshoot with, so it read as a spike. It is now `1 - crumple * |hash|`:
  the jitter can only ever make a vertex travel LESS. Roughness with a guarantee.
- **`rimBulge` defaults to 0.** Physically correct -- displaced metal does pile up around a dent --
  but it is the only part of the profile that pushes geometry outward, so it is the only thing that
  can spike a panel. Worth having only if the panels are ever subdivided.

**`crush` is what makes damage read as caved in rather than dimpled, but it must be applied as a
SEPARATE bounded move, not as a blended direction.** It slides surrounding metal sideways toward
the impact axis so material gathers into the crater instead of the whole region sliding along one
axis. This does more for the *character* of the damage than depth does -- a bowl of any depth still
reads as a bowl.

**The bound is the whole trick, and getting it wrong threw bodywork over the roof.** The first
version blended `localDir` and "toward the impact point" into a unit direction and travelled
`depth` along it. That is unbounded: a vertex 20 cm from the impact gets dragged the full depth
sideways, overshoots the impact point and carries on out the far side -- and for a vertex BELOW the
impact, "toward the impact" is upward, so bodywork was hauled up above the roofline. The fix is to
scale the sideways move by each vertex own tangential distance (`tangent * crush * profile`), so it
can only ever close a FRACTION of the gap to the axis and can never reach or pass it. The
along-axis component is stripped out of the gather first, or vertices deeper in the car than the
contact point get pushed back out through the paint as bulges.

**Dent shape follows the shape of the IMPACT, because all the contact points are used.** The roof
caves in for free -- direction comes from the collision, so landing upside down pushes the roof
down -- but with only `collision.GetContact(0)` every impact became one round dip regardless of how
it landed, so a flat roof landing punched a single crater in the middle instead of flattening the
greenhouse.

`CarDamage.GatherContacts` now samples the contact patch: it keeps points at least
`contactSpacing` (0.25 m) apart, up to `CarDeformation.MaxContactPoints` (8), and hands the set to
`Dent`. PhysX reports many contacts clustered within millimetres of each other, and denting all of
those would just hit the same spot repeatedly, which is why the spacing filter exists. Both buffers
are reused, so a crash allocates nothing however many contacts are reported.

Per vertex the deformation takes the **nearest** of the points, not the sum. Summing makes
overlapping craters twice as deep as either, so a flat landing would gouge a trench exactly where it
ought to be flattest. All the points share one vertex-buffer upload per panel, so a broad impact
costs the same as a narrow one apart from the distance tests.

**`OnCollisionEnter` alone gives a roof landing exactly ONE dent, so sustained crushing needs
`OnCollisionStay`.** Enter fires when contact *begins*; a car that lands upside down and slides
never stops touching, so every metre of grinding after the first frame did nothing. Same for
scraping along a wall.

`CarDamage.OnCollisionStay` routes through the same `HandleImpact`, and it is safe to do so because
the gate is already an impulse threshold: a car merely *resting* on its roof transmits about
`mass * g * fixedDeltaTime` per step -- roughly **235** for this car -- which is far under
`minimumImpulse` (900). Only contact violent enough to clear the bar a real hit clears gets through.

Two guards make it survivable, because unlike Enter it fires every physics step:

- **`sustainedScale` (0.35)** scales the damage down.
- **`sustainedInterval` (0.08 s)** rate-limits it. Without one, a grind lands ~50 impacts a second
  and strips the car in well under a second.

Set `sustainedScale` to 0 to turn sustained damage off entirely and go back to impact-only.

**Known limitation, and it shows up worst on the roof: the colliders never deform.** A car whose
roof has been flattened 55 cm still collides on its original box, so upside down it rests on an
invisible full-height roof and appears to float. Re-cooking a collider at runtime costs a frame
hitch, which is why this is the accepted trade -- but the roof is where a player is most likely to
notice it, because the car sits still on its roof rather than sliding past a wall.

**RE-WATCHED THE REFERENCE FOOTAGE, 2026-08-29, and it does almost NO mesh deformation.** Frames
extracted from `~/Downloads/CarCrashReference.mp4` at 185 and 204 gears of damage — near the end of
a long destructive run — show a car whose **silhouette is still stock**. The roof, rear quarter,
boot lid and flanks are geometrically intact and cleanly painted. What sells the damage is
entirely subtractive:

- **Hood gone**, showing a dark engine bay with visible engine detail.
- **Both doors gone**, showing a dark cabin you can see straight through.
- **Glass gone** — the greenhouse is simply open.
- Flipped on its roof, a **detailed dark undercarriage**: floor pan, exhaust, suspension.

This confirms and sharpens the note already in the Assets section: the reference's damage is
detached panels over a dark interior, not deformation. **Chasing "crazy crushing" through
`CarDeformation` is chasing something the reference never had.** Deformation is worth keeping as
seasoning — it makes hits register before a panel is ready to come off — but the visual payoff per
unit of effort is far higher in:

1. **Making glass disappear** — done, `CarGlass`.
2. **Interior and engine-bay detail** — done, `CarInteriorProps`.
3. **More panels that can detach** — `PartTrunk` exists in the mesh and must be wired by hand.

Do not spend more effort raising dent depth before those three are done.

#### Subtractive damage — `CarGlass` and `CarInteriorProps`, built 2026-08-29

Both exist because of the reference finding above: what reads as destruction is holes and what is
behind them, not crushed metal.

**`CarGlass` empties the glass SUBMESH rather than fading it out.** The E30 has no glass object to
detach — `split_car.py` keeps glass as a material *slot* on the body and the doors — so the
component finds every submesh whose material name starts with `Glass` and sets its triangle list to
empty. Emptying beats swapping in a transparent material: transparent glass still costs a draw call
and full-screen overdraw for something the player is meant to read as gone. It caches the triangles
so `Repair()` can put them back.

The ordering rule matters: **if `CarDeformation` is present the meshes are already its clones and
`CarGlass` must write into those**, or deformation ends up writing to a mesh nothing renders. With
no `CarDeformation` the meshes are still the imported ASSETS, and emptying their triangles would
permanently gut the FBX in the Editor — so it clones first in that case only.

**`CarInteriorProps` generates the interior, because the E30 has none to reveal.** The source OBJ
contains exactly four objects — body, glass and two wheel clusters — so there is nothing for
`split_car.py` to keep, and `InteriorShell` on its own is a smooth shrunken copy that reads as a
flat black void. The component welds seven boxes (engine block, dash, two seats, rear bench, floor
pan, boot floor) into **one mesh with one material: 84 triangles, one draw call**, borrowing the
`CarInterior` material off the shell if none is assigned.

Two non-obvious details. It builds in **`Start()`, not `Awake()`**, so `CarDeformation` has already
taken its panel list and will not try to dent a solid engine block. And shadow casting and
receiving are both off — the props are only ever seen through a hole, and realtime shadows are off
project-wide anyway.

Positions are in CAR-local space with the ground at `y = 0`, proportioned for the E30 (body spans
`y 0..1.21`, `z -2.41..+1.45`, `x +/-0.84`). Select the car to see them as gizmos while adjusting.

**The readouts exist so this stays measured.** In play mode:

| Component | Field | Read it for |
| --- | --- | --- |
| `CarDamage` | `lastImpulse` | What PhysX actually reports on contact |
| `CarDamage` | `lastDamage` | That impulse through the formula; compare to part health 100-160 |
| `CarDeformation` | `lastRequestedCm` | Depth asked for, before the clamp |
| `CarDeformation` | `lastAppliedCm` | Depth actually applied, after it. **This is the one that decides whether you can see anything** |
| `CarDeformation` | `lastVertsMoved` | 0 = the hit matched no panel; a handful = radius biting too little mesh |
| `CarDeformation` | `lastPanelHit` | Which mesh took the deepest dent. Not the one you hit = a matching problem, not a depth problem |
| `CarDeformation` | `lastPanelStillRendered` | False = dents are going into a mesh nothing draws, which looks identical to no dents |

`OnDrawGizmos` also draws a red wire sphere at the last impact, at `radius`. If it is not sitting
where the car touched the wall, stop tuning depth -- the impact is landing somewhere else.

`lastRequestedCm` far above `lastAppliedCm` means the cap is doing all the work.
`lastVertsMoved` separates "nothing matched" from "the dent is real but too small to see" — the
two failure modes that look identical on screen.

**Dent profile is smoothstep, not quadratic.** Quadratic is only 0.25 deep at half the radius, so
most vertices in range barely move and the dent stays invisible however deep the centre goes.
Smoothstep is 0.5 at half radius: a broad crater floor with a soft rim, which is both more visible
and closer to what dented sheet metal looks like.

**MESH-LOCAL UNITS ARE NOT METRES ON THIS MODEL. Check `lossyScale` before comparing any tunable
against vertex positions.** Every FBX child of `e30-split` is serialized at
**`m_LocalScale: 100`** — Blender wrote the mesh in centimetres and Unity compensates on the node
— so one mesh-local unit is 100 m and the whole 4.16 m car spans about **0.042 local units**.

This produced the worst bug `CarDeformation` has had. `radius = 0.28` "metres", compared raw
against local units, covers the entire car six times over: every vertex lands inside the falloff
at ~1.0, so the mesh **translates bodily instead of denting**. And `maxDisplacement = 0.08` meant
eight *metres*. The only value that looked sane was `0.0005`, which is 5 cm once multiplied by
100 — a correct dent depth reached through a wrong number, which is why it read as "I have to
make it super small and then it does not dent".

`Dent()` now converts `radius`, the per-hit depth and `maxDisplacement` into each panel's own
units via `transform.lossyScale`, so **every field on the component stays in real metres for any
model at any import scale**. `renderer.bounds` is world-space, so the cheap AABB reject keeps
using the unconverted world radius; only the vertex loop works in local units.

Two consequences worth remembering:

- **Anything that scales a child of `e30-split` is typed relative to 100, not 1.** Shrinking the
  `InteriorShell` by 5% to buy dent clearance means typing **95**, not 0.95.
- The same trap is waiting for any future code that measures against vertex positions —
  deformation, vertex-based decals, damage masks. `Part.anchor`, `PartPosition()` and the wheel
  code are safe because they work in world space via `TransformPoint`.

**A flat depth cap turns dents into slabs, so the cap is now per-vertex and shaped.** Second
finding, same day, from the same play test: with one `maxDisplacement` for every vertex,
everything inside the radius saturates at the same depth after a couple of hits and the crater
flattens out. That reads as the whole panel being bodily shoved rather than dented — it is what
"the whole body mesh just gets displaced" was. Three changes fixed it:

- **Per-vertex budget** (`Panel.budget`). Each vertex earns a permanent allowance *shaped by the
  falloff*, and the allowance only ever grows. Rim vertices keep a small allowance forever, so
  the bowl keeps its shape however many times it is hit.
- **`strengthPerDamage` is the per-hit bite; `maxDisplacement` is the eventual limit; they are no
  longer clamped together.** The first hit used to max a panel out instantly. Now damage
  accumulates over several impacts, which is what makes it read as progressive damage at all.
- **`crumple` (0.35)** jitters each vertex by a value hashed from its INDEX — stable across every
  hit, zero storage. Without it a dent is a perfectly smooth bowl and reads as a beach-ball
  print. It must be a hash and never `Random`: a second hit in the same place has to deepen the
  first, not fight it.

Two smaller ones that matter as much in practice. The falloff distance is measured from each
vertex's **original** position, not its deformed one — measuring from the deformed position lets
the affected set drift with every hit, so a caved-in panel keeps recruiting new vertices and the
damage smears across the whole panel instead of deepening one crater. And **`radius` is the
single biggest control over dent-versus-shove**: at 0.45 the crater is 0.9 m across on a 1.68 m
wide car, which is most of the panel. Now 0.28.

**Vertex density is the floor on how sharp a dent can look, and this car is near it.** The E30
`Body` is 2,910 tris for the entire shell and a door is 931. A 0.28 m dent patch therefore
contains only a few dozen vertices, so dents will always read as soft folds rather than creases.
That is the price of an 11.5k-tri car and it is the right trade for the Chromebook budget — games
that show crisp creases are running 50-100k-tri cars. Do not chase it by subdividing panels
without measuring the triangle and draw-call cost first.

**To buy dent depth, scale the `InteriorShell` transform down in the Inspector.** It is a plain
child GameObject, so a uniform scale of **0.95** shrinks it toward the model origin and takes the
roof clearance from 4.2 cm to about **10 cm** — no re-export, no re-wire, one field. **The nodes
sit at scale 100, so that is typed as `95`, not `0.95`.** `SHELL_SCALE`
in `split_car.py` is the same lever at source and only worth touching when the roster grows. Do
**not** "fix" this by adding `InteriorShell` to the deformed set — denting the shell inward opens
gaps around the holes that a missing panel is supposed to reveal, which is the problem the shell
exists to solve.

**The "few dozen verts per hit" budget was wrong, and is retired.** The cost is not the vertex
loop: it is `SetVertices` (a whole-buffer upload) plus `RecalculateNormals` (whole mesh), both
per *mesh*, not per vertex touched. Touching 30 vertices instead of 300 in a 1,346-vertex
bumper saves nothing measurable. Budget by **meshes dented per hit** and **hits per step**
instead — hence `maxDentsPerStep` (2), which exists because a multi-contact scrape fires
several collisions in a single step and each is a buffer upload.

**Two things caused "the whole shell flew off the car", found 2026-08-29. Both are fixed.**

1. **A single impact one-shot every panel.** `damage = (impulse - minimumImpulse) x damagePerImpulse`
   is linear and unbounded. A 1200 kg car stopping against a wall at 20 m/s reports an impulse
   near **24,000**, which is **~1,040 damage** against parts holding 100–160 health. Every
   collision event in the crash killed whatever part it matched, so within a few frames the
   hood, both bumpers, both doors and both mirrors had all detached at once and drifted away
   still in formation — which reads exactly like the body shell leaving the car in one piece.
   Fixed with **`maxDamagePerImpact` (60)**, a ceiling on what one impact can take off one
   part. `TotalDamage` is deliberately *not* capped: the score should still reflect the hit.
2. **Detached panels were fired away, not dropped.** `ThrowRealPart` builds the BoxCollider from
   the mesh bounds *while the panel is still inside the bodywork* — a door box overlaps the Core
   box almost entirely — and then makes it dynamic. PhysX resolves that overlap by pushing the
   pair apart at the depenetration limit, and at 18 kg against 1200 kg the panel absorbs all of
   it. Fixed with **`detachGrace` (0.4 s)**: `Physics.IgnoreCollision` against the car's own
   colliders, released on a timer in `Update`. The release matters — debris is *meant* to be
   able to hit the car that shed it, it just must not be born inside it. The two guards in that
   loop are load-bearing: `IgnoreCollision` errors on a collider whose GameObject is inactive,
   and `DebrisPool` deactivates spent debris.

**`Part.anchor` is set on 9 of the 11 parts in `SampleScene`, and it should not be.** Those are
the leftover Kenney-era empties under `Car`, and they are in the wrong place for the E30:
`PartDoorL` sits at `x -1.2` when the body half-width is ~0.84 — **0.36 m outside the car** —
and `PartBumperF` at `z 2.0` when the nose face is at `z ~1.67`. A set anchor **overrides**
`PartPosition()`'s mesh-centre matching, which is the thing that exists to stop mirrors stealing
door hits. Clear all nine to None so the mesh centre is used.

**Open tuning — now partly measured.** A wall hit reports impulse **~16,500**, so
`(16500 - 900) x 0.045` = **~702 damage** against parts holding 100-160 health. `minimumImpulse`
(900) is therefore doing almost nothing -- it gates out only the gentlest taps -- and
`maxDamagePerImpact` (60) is what actually makes damage progressive. Read `CarDamage.lastImpulse`
at other speeds before changing either. Nothing falling off → lower the minimum to ~400 and raise
damage to ~0.1. Panels coming off too readily → raise `minimumImpulse` or lower
`maxDamagePerImpact`, which is the more direct lever.

## Game design

Arcade crash-driving, third-person chase cam.

- **Garage** — carousel of cars on a podium, `<` `>` to cycle, gear currency, buy/select, GO
- **Run** — spawn at the top of a long downhill mountain road, AI traffic ahead
- **Goal** — bomb downhill and destroy the car; damage earns gears; gears buy more cars
- **Damage** — deformable panels plus detachable parts: doors, hood, bumpers, wheels

### Known improvements over the reference game

- **Camera.** The reference rig only yaws — it tracks side to side but never pitches, so on a
  steep downhill you stare at asphalt. Ours pitches with terrain slope and pulls back with speed.
- **Damage depth.** More granular part detachment than the reference.

### Multiplayer

Decision: **local split-screen first.** No networking, no server, nothing for school IT to block.

A true listen server is **impossible** in a Web build — browsers cannot open listening sockets
(no TCP accept, no UDP). Networked play would require either a hosted WebSocket server or a
hand-written WebRTC JS interop plugin. Both are out of scope unless explicitly revisited.

## Assets

Prefer **CC0** (Kenney, Quaternius, Poly Pizza, ambientCG). Non-commercial licenses are
acceptable for this personal project but are a second choice.

Maintain `CREDITS.md` from the first third-party asset onward — record source, author, and
license for everything, as it is added. Do not reconstruct it later.

### Vehicle meshes — revised 2026-08-29

Kenney's Car Kit bodies are a **single welded mesh**; only wheels are separate nodes.
Re-checked 2026-08-29 across Kenney, Quaternius, RgsDev and the Sketchfab CC0 tags:
**no free pack ships doors, hood, trunk or mirrors as separate objects.** Every one of
them separates wheels only. Paid packs that do exist (Maker Games Studios "Real Car N
Separate Parts") are 43–54k tris at LOD0 with a usable ~11k LOD2.

Even a model advertised as "separate parts" is usually split **by material, not by panel** —
the three.js Ferrari has `body`, `chrome`, `glass`, `interior`, and no `door` anywhere.
So the panel cut is ours to make regardless of the base. That is what `tools/blender/`
is for.

**Split the model ourselves, in a script.** The earlier "~1 hour per car in Blender, ten
cars, not worth it" estimate assumed hand-modelling. Blender 5.2 is installed
(`C:\Program Files (x86)\Steam\steamapps\common\Blender\blender.exe`) and drives headless
fine, so the split is a one-run job across the roster and is re-runnable when regions change.

**Only the player car gets the detailed split model. Traffic stays on the Kenney kit.**
The chase camera lives on the player car; traffic is seen at speed, at distance, briefly,
and mostly while being destroyed. Paying 11k tris and five materials across ~20 traffic
cars would cost ~100 draw calls for geometry nobody looks at. This keeps the shared
`colormap` batching intact for everything except the one car on screen.

Kenney vehicles and debris still share one `colormap` material, so the traffic roster
batches and each extra traffic car costs ~3,000 verts and **no extra texture memory**.

### The player car — decided 2026-08-29

**Cosmo "Low Poly Cars" free tier, CC0**, in `Assets/Art/Vehicles/CosmoCars/`. 12 vehicles,
~1,000–1,400 tris per body, four separate wheel objects, one shared 1000×720 palette atlas
at 8.6 KB. Whole pack is 1.1 MB on disk.

Picked after watching the reference footage (`~/Downloads/CarCrashReference.mp4`). The
reference cars are **not** high-poly — they are boxy 90s sedans at maybe 3–5k tris. What
separates them from Kenney is **proportion and detail placement, not triangle count**:
proper greenhouse, thin pillars, correctly sized wheels, number plates, tail-light detail.
That is a far easier target than a PBR supercar, and Cosmo's pack hits it.

**Import Scale Factor 1.0** — these are authored at real-world scale (coupe is 5.14 m long).
The Kenney kit still needs 1.6. Do not mix them up or debris spawns the wrong size.

**Superseded 2026-08-29 for the player car — the Cosmo coupe read as not quite realistic
enough.** The player car is now the **BMW E30**, `Assets/Art/Vehicles/BMW-E30/e30-split.fbx`
(CC-BY, ROH3D — **attribution required in-game**, see CREDITS.md). Cosmo's 12 cars stay for
traffic and the garage roster.

E30 after splitting: **11,588 tris** — Body 2,910, InteriorShell 960, four wheels ~900 each,
and eight detachable panels (BumperF 1,346, BumperR 738, DoorL 931, DoorR 583, Hood 168,
Trunk 146, MirrorL/R ~103). It has a real `Glass` material, so the windows are actual glass
rather than a painted-on swatch.

Build command:

```bash
"$BL" --background --python tools/blender/split_car.py -- \
  --input Assets/Art/Vehicles/BMW-E30/Source/e30-FullBody.obj \
  --output Assets/Art/Vehicles/BMW-E30/e30-split.fbx --tris 12000 --scale 0.001
```

The Cosmo coupe's split (`CosmoCars/coupe-split.fbx`, 3,170 tris) is kept and still valid.

Also confirmed from the footage: the reference game's damage is **detached panels over a
dark interior**, not deformation. Hood gone shows a dark engine bay; door gone shows a dark
cabin; glass simply disappears. That is exactly what `InteriorShell` plus real panel
detachment produces, which is why this approach was chosen over faking it.

### Measured: decimation does not rescue a high-poly base

Tested on a 358k-tri showcase model (three.js Ferrari, dev rig only — **not shipped**,
it is a trademarked car and a rendering demo):

| Triangle budget | Result |
| --- | --- |
| 11,000 | Shredded. Thin overlapping material shells (chrome, glass, grille, trim) collapse into spikes. Unusable. |
| 40,000 | Clean silhouette, reads as a real car. |

**Decimation preserves shape at 50–70% reduction, not at 95%.** So the base model must
already be near budget — target **10–25k tris natively** — not a showcase mesh crushed to
fit. This is the single most important constraint when picking the base.

### Blender pipeline (`tools/blender/`)

| Script | Job |
| --- | --- |
| `inspect_model.py` | Print objects, verts, tris, dimensions, materials. Run before anything else. |
| `split_car.py` | Join → decimate → carve panels by region → set hinge origins → interior shell → export FBX. |
| `preview_split.py` | Render the split FBX with each panel colour-coded. **Always look at this**; the triangle report cannot tell you a region cut a door in half. |

```bash
BL="/c/Program Files (x86)/Steam/steamapps/common/Blender/blender.exe"
"$BL" --background --python tools/blender/inspect_model.py -- in.glb
"$BL" --background --python tools/blender/split_car.py -- --input in.glb --output out.fbx --tris 11000
"$BL" --background --python tools/blender/preview_split.py -- --input out.fbx --output preview.png
```

Panels are carved by **region test**: a face joins a panel if its median point falls inside
that panel's normalised bounding box, expressed as fractions of the body bounds. So `REGIONS`
in `split_car.py` works on any roughly car-shaped mesh. Tune, re-run, look at the preview.

`split_car.py` also does three things that exist purely to stop hand-derived numbers going
wrong:

- **Wheels are grouped per corner, joined, and given origins at their own centres**, then
  named `WheelFL / FR / RL / RR`. Packs split a wheel across tyre/rim/brake/nut objects, and
  every one of them arrives with its origin at the *model* origin. A transform whose origin
  is elsewhere cannot be positioned or spun, so `Wheel.visual` is unusable. Symptoms of
  leaving this unfixed: wheels don't spin, **and** wheels sink through the road, because
  with no usable visual they stay welded into the body and cannot rise with the suspension.
  Under `downforce` at speed that is ~9 cm of tyre through the tarmac. One defect, two bugs.
- **The model is auto-grounded** so the lowest point of the tyres sits exactly at y = 0.
  The Cosmo coupe was authored 4.5 mm low.
- **It prints a `=== UNITY SETUP ===` block**: measured `wheelRadius`, the four wheel anchor
  positions in Unity coordinates, the three collider boxes with their approach / departure /
  breakover angles, and the body's material list. **Use those numbers, do not re-derive them.**
  Nose and tail box heights are measured *per region*, so the nose box is bonnet-high rather
  than roof-high — sized globally it makes an invisible slab above the bonnet that collides
  with overhangs the car should duck under. Mirrors are excluded from collision bounds; they
  are fragile protrusions and letting them set the width makes the car 0.37 m too wide.

Gotchas already paid for, do not rediscover them:

- **Classify by whole name token, never substring.** `trim` contains `rim`, which quietly
  put 13k triangles of body trim in the wheel bucket and skipped decimation on all of it.
- **Check DROP before WHEEL.** `steering_centre` is dashboard trim, and the wheel test
  claims it otherwise.
- **Region order matters** — first match wins. Bumpers must claim the nose and tail before
  the hood and trunk regions, which run to the ends of the car and swallow them.
- **Glass is excluded from every region except the doors** (`GLASS_HINTS`, and the
  `allow_glass` flag on each region). Without it the trunk region takes the rear
  **windscreen** instead of the boot lid — both sit high and just behind the rear axle, so
  position alone cannot separate them, but a boot lid is never glass. Doors *do* take
  glass, so the side window leaves with the door.
- **Classify by CamelCase-split tokens.** 3ds Max exports names like `LowTire001`, which
  without splitting reads as one token `lowtire`, misses the `tire` hint, and files the
  wheels as bodywork.
- **Wheels arrive welded in pairs.** The E30 ships both fronts as one object and both rears
  as another, so `split_loose()` runs before corner grouping.
- **Check the units.** The E30 OBJ is in millimetres (4,317.92 long) and needs
  `--scale 0.001`. `sanity_check_size()` shouts if the result is not 2–8 m, because wrong
  units silently corrupt every derived number rather than failing.
- **Suspension anchors are symmetrised.** Models are rarely perfectly mirrored — the E30's
  wheel centres are at −0.698 and +0.740 — and asymmetric anchors make the car pull to one
  side for no visible reason.
- **Build the interior shell from the intact body, before carving.** A shell copied from an
  already-carved body has exactly the holes it exists to hide.
- **Shrink the shell by uniform scale toward the centre, not by normal offset.** A decimated
  shell deviates from the body by more than any sane offset distance and erupts through the
  paint. `SHELL_SCALE = 0.93`.
- Small objects are exempt from group decimation (`DECIMATE_FLOOR = 250`); a shared ratio
  otherwise reduces a 56-tri wheel centre cap to 2 triangles of noise.
- **`split_car.py` reports in Unity axes but exports in Blender axes. They can disagree.**
  `report_unity_setup()`'s `to_unity()` maps by *detected role* — Unity X ← width,
  Y ← height, Z ← −length — so the printed anchors and collider boxes are always right.
  `export_fbx()` applies a **fixed** `axis_forward="-Z", axis_up="Y"`, which assumes the
  model runs along Blender **Y** and is **Z-up**. The E30 OBJ is **Y-up running along X**
  (`4317.92 × 1210.45 × 1712.99` mm = length X, height Y, width Z), so the two disagree and
  the geometry arrives rotated relative to the numbers. Measured in the exported FBX: the
  wheel mesh local bbox is `0.600 × 0.230 × 0.600` — thin on **local Y**, so the axle is
  authored **along Y**, not the X that Unity expects.
  `split_car.py` never rotates geometry into a canonical orientation; fixing that at source
  (reorient to length-along-Y before export, then `bake_space_transform=True`) is the real
  fix, and is deferred because it forces a re-export and a full re-wire.
- **Blender leaves the axis conversion in the node transforms, not the vertex data.**
  `bake_space_transform` defaults to `False` and `split_car.py` does not pass it, so every
  node in `e30-split.fbx` carries `rotation (-90, 0, 0)` and the mesh data stays Blender-native.
  This is invisible for anything Unity just renders — the body and all eight panels stand up
  correctly *because* of that node rotation. It is only a problem for a transform whose
  rotation code **overwrites absolutely**, which is the four wheels and nothing else. Hence
  the giveaway symptom: body correct, wheels wrong.
