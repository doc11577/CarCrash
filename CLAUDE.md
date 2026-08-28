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

## Roadmap

Live kanban board (add/move/delete cards, saves itself):
https://claude.ai/code/artifact/1f62aafd-5b63-417b-b9b7-8d035c0a909a

Build order — expensive unknowns first, content last:

1. **Done** — deploy pipeline, repo, player settings, size baseline
2. **Now** — chase camera rig · arcade vehicle controller · greybox downhill track
3. **Next** — detachable parts · panel deformation · cheap traffic · scoring/gears ·
   garage & buy · persistence
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
- **The camera takes no player input, ever.** Target platform is a school Chromebook
  trackpad; any camera the player has to steer is a failure. `ChaseCamera` is fully
  automatic.
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

Car models must have **doors, hood, bumpers, and wheels as separate meshes** with their own
transforms. A single welded body mesh cannot support detachable damage and will have to be
split in Blender.
