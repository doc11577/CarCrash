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
| Git remote | not yet configured |

`Documents` is confirmed **not** OneDrive-redirected, so the project path is safe.
Do not move it under `C:\Users\ethan\OneDrive\` — OneDrive sync corrupts Unity's `Library/`.

## Deploy pipeline

Proven approach, copied from the reference game that already runs on these Chromebooks:

1. Unity **Web** build, Brotli compression **with decompression fallback**
   (required — a plain CDN can't serve `Content-Encoding: br`)
2. Commit build output to a folder in this GitHub repo
3. Serve the build files via **jsDelivr** (`https://cdn.jsdelivr.net/gh/<user>/<repo>@<commit>/<path>`)
   - 20 MB per-file limit; current reference build's largest file is ~7.6 MB
   - Pin to a commit hash, not a branch, so the CDN doesn't serve stale files
4. Paste the Unity `index.html` (with URLs pointed at jsDelivr) into
   **Google Sites → Insert → Embed → Embed code**
5. Google Sites renders it in a sandboxed `*-atari-embeds.googleusercontent.com` iframe

It works at school because `sites.google.com` is Workspace-whitelisted and `cdn.jsdelivr.net`
is a generic CDN that filters don't block. There is no trick beyond that.

**Test the full pipeline on a real school Chromebook before building anything worth losing.**

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
