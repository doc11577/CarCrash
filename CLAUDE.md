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

- **60 FPS target, 30 FPS floor** on integrated graphics at 720p — **met, measured 2026-09-01
  on Jasper Lake Intel UHD at 1041×670, both maps, ~1 ms of frame jitter.** See the measured
  section for what that does and does not license. Memory is NOT the scarce resource it was
  assumed to be here (154 MB of a 512 MB heap); the download cap is.
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
bash tools/publish.sh          # WebBuild/carcrash/Build -> prod/  (refuses a stale build)
git add -A && git commit -m "..." && git push
bash tools/pin.sh              # pin embed.html to HEAD, after checking jsDelivr has it
# re-paste tools/embed.html into Google Sites -> Insert -> Embed -> Embed code
```

**Two guards were added 2026-08-31, both aimed at failures that have already cost a release.**
Neither replaces reading the section below; they just make the two silent failures loud:

- **`publish.sh` now refuses a build older than the project files.** It only ever copied, so it
  would happily ship last week's build and print "Copied to prod". `--force` overrides.
- **`tools/pin.sh` rewrites `BUILD_BASE` for you**, and refuses if jsDelivr cannot yet serve the
  commit — which is what happens when the hash is taken before `git push`. Copying a
  40-character hash into an HTML file by hand is exactly the task to give a script.

`tools/embed.html` is the page pasted into Google Sites. It is *not* Unity's generated
`index.html` — it's a hand-written replacement with a progress bar, on-screen error
reporting (there's no console on a school Chromebook), iframe focus handling, and
arrow-key scroll suppression.

Non-obvious things that will bite:

- **Pin jsDelivr to a commit hash, never a branch.** Branch URLs are cached hard and will
  serve a stale build for hours.

- **THE HASH IS THE STEP THAT GETS MISSED, AND IT FAILS SILENTLY.** Pinning is correct, but it
  means `BUILD_BASE` must be edited on *every* release. Happened 2026-08-30: a full build was
  made and published, the Google Sites page still served the 2026-08-27 smoke test, and nothing
  anywhere reported an error — the old build loads perfectly, it is just the wrong game.

  **If the page shows an old build, check `BUILD_BASE` before checking anything else.** The
  giveaway is that it loads fine and looks like an earlier version, rather than failing.
  Diagnose from the repo rather than the browser:

  ```bash
  grep -A2 "var BUILD_BASE" tools/embed.html   # which commit is the page pinned to?
  ls -l prod/                                  # are these files actually new?
  ls -l WebBuild/carcrash/Build/               # did Unity write a build at all?
  ```

  All three timestamps must be from this build. `publish.sh` will happily copy a stale build
  without complaint, because copying is all it does.

- **Verify jsDelivr has the commit before pasting into Sites.** It can only serve what is on
  GitHub, so a hash taken before `git push` points at a commit the CDN cannot fetch:

  ```bash
  curl -s -o /dev/null -w "%{http_code}\n" -r 0-0 \
    "https://cdn.jsdelivr.net/gh/doc11577/CarCrash@<hash>/prod/carcrash.loader.js"
  ```

  200 or 206 means it is live. 404 means push first.

- **`git push` cannot be run for you.** The credential helper is Git Credential Manager, which
  needs a GUI prompt and hangs forever in a non-interactive shell. Ethan runs the push.
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

### Build size — measured 2026-09-02, Update 2

Brotli, **25.04 MB** total download, **18.20 MB** in the data file. Two new maps (Bullseye, The
Dam), the Aventador, falling boulders, airtime scoring, the podium garage and the turning fix.

| File | Update 2 (09-02) | Update 1 (08-31) | First build (08-30) | Change this release |
| --- | --- | --- | --- | --- |
| `carcrash.data.unityweb` | **18.20 MB** | 14.83 MB | 8.15 MB | **+23%** |
| `carcrash.wasm.unityweb` | 6.66 MB | 6.62 MB | 6.63 MB | — |
| `carcrash.framework.js.unityweb` | 0.07 MB | 0.07 MB | 0.07 MB | — |
| `carcrash.loader.js` | 0.11 MB | 0.11 MB | 0.11 MB | — |
| **Total** | **25.04 MB** | 21.63 MB | 14.96 MB | **+16%** |

**This release went over the cap and had to be cut back, twice.** The first build measured
**35.32 MB in the data file — 15.32 MB over** — see the section below for what caused it. Two
passes brought it down:

| Pass | Change | Data file |
| --- | --- | --- |
| — | first build, as imported | 35.32 MB |
| 1 | 21 dam textures 2048 → 512, crunch on | 21.90 MB |
| 2 | `glendam.fbx` mesh compression Medium, animation/blendshapes/cameras/lights off; crunch on the last 15 uncrunched textures across all vehicles, Everest and Quarry | **18.20 MB** |

**Headroom is 1.80 MB, and every texture in the project is now crunched.** The **TMP font trim
landed 2026-09-02** and takes roughly 1.5 MB more off the next build, which is not reflected in
the table above — that was measured before it. The cheap wins are now spent; the next lever is
splitting the files, which retires the cap outright.

**Mesh compression is ON for The Dam and nothing else.** It quantises vertex positions, and that
map's colliders were already troublesome, so if the road there ever feels bumpy or the wheels
catch on nothing, that setting is the first suspect — `meshCompression: 2` in
`glendam.fbx.meta`, set back to `0` to rule it out.

### Build size — measured 2026-08-31, Update 1

Brotli, **21.63 MB** total download. Commit `1380215`: Everest, the LCT 3000 truck, the smarter
AI, feats, patch notes and the reset button.

| File | Update 1 (08-31) | First build (08-30) | Smoke test (08-27) | Change this release |
| --- | --- | --- | --- | --- |
| `carcrash.data.unityweb` | **14.83 MB** | 8.15 MB | 3.71 MB | **+82%** |
| `carcrash.wasm.unityweb` | 6.62 MB | 6.63 MB | 6.49 MB | — |
| `carcrash.framework.js.unityweb` | 0.07 MB | 0.07 MB | 0.07 MB | — |
| `carcrash.loader.js` | 0.11 MB | 0.11 MB | 0.11 MB | — |
| **Total** | **21.63 MB** | 14.96 MB | 10.4 MB | **+45%** |

**⚠ THE HEADROOM IS NEARLY GONE, AND THE FAILURE IS TOTAL.** `carcrash.data.unityweb` is
**14.83 MB against jsDelivr's hard 20 MB per-file cap** — about **5.2 MB left**. Everest and the
truck cost 6.7 MB between them, so **one more map of Everest's size breaks the cap**, and when a
file exceeds it jsDelivr serves a 404: the game does not load at all. This is now the binding
constraint on new content, ahead of frame rate.

It has also crossed the **≤ 20 MB download** budget written into the performance budget below,
which matters on school Wi-Fi rather than on the GPU. The budget line is left as-is deliberately,
so the overrun stays visible rather than being defined away.

All content growth lands in the data file. Cheapest reductions, in order, and **the first one
should be done before the next map**:

1. **A trimmed TMP font asset — ~2.1 MB, about twenty minutes.** The stock `LiberationSans SDF`
   is 2.2 MB and force-included from a `Resources/` folder; the game draws digits, a few words
   and some part names. Font Asset Creator with a custom character set gives roughly 100 KB.
   This is 10% of the whole download for one afternoon's work and nothing on screen changes.
2. **`--mountain-cell 24`** on the course generator. The mountainsides are the bulk of the
   geometry and are seen only at distance. Applies to both courses.
3. **Drop `Source/` FBXs from the repo** if it ever matters — they do not ship (nothing
   references them, so Unity excludes them from the build) but they do cost clone time.

### A DOWNLOADED MAP BLEW THE CDN CAP — 2026-09-02

**The Dam build came out at 35.32 MB in the data file against jsDelivr's 20 MB per-file cap.**
It would not have loaded at all: the CDN 404s the file, the loader hangs, and nothing on screen
says why. Caught by the size check after the build and before the push.

**The cause was 21 textures at 2048 with crunch off — 42 MB of source PNG.** Nothing generated
by this project has ever done that; Quarry and Everest are procedural and their two Poly Haven
textures are 1K crunched. **A downloaded map arrives with the ORIGINAL artist's import settings,
and those are sized for a desktop game, not for a 20 MB CDN budget.**

Fixed by editing the `.meta` files directly — `maxTextureSize: 2048` to `512` and
`crunchedCompression: 0` to `1` across all 21, in the default block and every platform override.
Unity reimports on focus. Editing metas in bulk beats clicking 21 inspectors and is reviewable
in a diff.

**Standing rule, now with a reason attached: every imported asset gets its import settings
checked BEFORE it is wired into a scene, not after the build.** The check is one line:

```bash
grep -rl 'maxTextureSize: 2048' Assets --include='*.meta'
```

Anything that comes back is a 2K texture, which this project's budget has never allowed. A 2048
DXT texture with mipmaps is ~2.7 MB of build each, so twenty of them is the entire download
budget spent on one map's surfaces.

**The size check is the only thing standing between a bad import and a dead build.** It is the
second time a silent CDN failure has been caught by a script rather than by looking — the first
was the `BUILD_BASE` hash. Both fail with a game that looks perfectly fine locally.

Known and deliberately NOT changed at the same time: `glendam.fbx` is a 47 MB source FBX with
`meshCompression: 0`. Mesh compression quantises vertex positions, and this map's colliders were
already troublesome, so it is left alone — a lever to reach for only if textures alone do not get
under the cap.

### THE 20 MB CAP CAN BE DEFEATED BY SPLITTING THE FILE — found 2026-09-02

**jsDelivr's 20 MB limit is PER FILE, and a file can be cut into pieces that are reassembled in
the browser.** Found by reading the page source of another Unity game on a Google Site
(ZooPickle's Ultrakill 3D), which had been offered as evidence that "bigger games fit". It is on
**the same CDN and the same kind of public GitHub repo this project uses** — it just does not
serve its data file whole:

```js
const dataParts = 4;
const wasmParts = 3;

async function mergeParts(baseName, totalParts, progressWeight) {
  const parts = [];
  for (let i = 0; i < totalParts; i++) {
    const url = `https://cdn.jsdelivr.net/gh/<user>/<repo>@main/Build/${baseName}.part${i}`;
    parts.push(await (await fetch(url)).arrayBuffer());
    progressBar.style.width = ((i + 1) / totalParts) * progressWeight + "%";
  }
  return URL.createObjectURL(new Blob(parts, { type: "application/octet-stream" }));
}
```

The merged `blob:` URL is then passed to `createUnityInstance` as `dataUrl` / `codeUrl`. Every
part on the CDN is comfortably under the cap, so the CDN never sees a file it will refuse.

**What this changes: the per-file cap stops being the binding constraint on content.** The
build-size sections above are written on the assumption that 20 MB is a wall. It is not — it is
a chunk size.

What it does NOT change, and these are the real limits now:

- **Download TIME.** 25 MB is 25 MB whether it arrives as one file or four, and school Wi-Fi is
  the same either way. The TMP font trim and sane import settings are still worth doing.
- **Streaming is lost.** Unity currently streams its own download and reports progress. Merging
  means every part must land before Unity starts, and the loading bar has to be driven from the
  fetch loop instead (which is what their code does).
- **The whole file is held as a Blob** before Unity reads it. At this size that is fine — the
  heap measured 154 MB of 512 — but it is not free, and it scales with the file.

**If this is implemented here, keep the commit pin.** That page uses `@main`, which is exactly
what the deploy section warns against: branch URLs are cached hard and serve stale builds for
hours. Splitting and pinning are independent — do both.

Sketch of the work: a chunking step in `publish.sh` that cuts any `prod/` file over ~18 MB into
`name.partN`, and a `mergeParts` loop in `tools/embed.html` feeding the existing progress bar.
`embed.html` already has the bar and the on-screen error pane, so it is a modest change to a
file that is already hand-written for this job.

### Save to a `.crash` FILE — built 2026-09-02

Papa's-style: SAVE TO FILE downloads `carcrash-progress.crash`, LOAD FROM FILE picks one and
applies it. `Game/SaveFile.cs` + `Plugins/WebGL/FileIO.jslib`, two buttons on the Options screen.

**The file is the save CODE in a wrapper — there is no second format.** `SaveCode` already owns
the encoding, the checksum and the validation, and it has already shipped one format bug (the
nested delimiter). A second serialiser would be a second thing to get wrong and a second thing to
keep in step. This only adds a way to MOVE that string.

**Why any of it needs JavaScript, and why that is the whole plugin.** A browser permits a download
or a file picker only from a real user gesture, and Unity's C# cannot originate one — the click
has to be made by the page. So exactly two functions live in the `.jslib` and nothing else does.

- **The result comes back through `SendMessage`, not a return value.** Reading a file is
  asynchronous: the picker returns the instant it opens and `FileReader` fires whenever the player
  chooses something, which may be never. `SendMessage` addresses a GameObject BY NAME, hence the
  `CarCrashSaveFileReceiver` object, which exists for no other reason.
- **The receiver is `DontDestroyOnLoad`.** The menu scene can unload while a file dialog is open,
  and a `SendMessage` to a destroyed object is a silent no-op — indistinguishable from the player
  having cancelled.
- **Both jslib functions swallow their exceptions and log.** An exception crossing back into WASM
  takes the whole game down, and a save button that fails must not do that.
- **The object URL is revoked on a 10 s timer, not immediately.** Some browsers begin the write
  asynchronously, and a revoked URL there produces an empty file.
- **`accept=".crash,.txt"` is a hint, not a restriction.** Every browser still offers "all files",
  and a save renamed by a file manager has to stay loadable.
- Nothing appears on screen between opening the picker and the read completing. A "loading…"
  message over a dialog the game cannot see would be a guess about what the player is doing.

**No change was needed to `tools/embed.html`.** It renders the canvas directly — there is no
iframe of ours to carry a `sandbox` attribute — and Google Sites' own embed sandbox already
includes `allow-downloads`. Worth recording because the opposite was assumed first: a download
blocked by a missing sandbox token fails *silently*, which would have been the obvious suspect.

**Outside the Web build there is no browser**, so the Editor writes to and reads from
`Application.persistentDataPath` and logs the path. Enough to test the round trip without a build.
`SaveFile.Supported` says which world it is in, and the on-screen message says so too rather than
claiming a download that did not happen.

### The paint shop — built 2026-09-02

Five free colours and four bought ones, chosen per car in the garage. **No wiring: it is all
code-built like the rest of the front end.** `Game/CarColours.cs` owns the palette and the
persistence; `MenuUI` builds the panel; `PlayerCarSpawner` applies the choice on spawn.

| Paint | Price |
| --- | --- |
| White, Red, Navy, Racing Green, Graphite | free |
| Silver | 50,000 |
| Gold | 100,000 |
| Platinum | 200,000 |
| Phantom Black | 500,000 |

- **The palette is a static table, not an Inspector list.** Prices and unlocks are content two
  places must agree on — the garage that sells them and the spawner that applies them — and
  `CarRoster` already taught this project that two copies of a list drift silently.
- **Colour is stored PER CAR, ownership is GLOBAL.** Painting the truck must not repaint the
  Aventador, but buying phantom black four times over is a wall rather than a progression curve.
- **Ownership by ID, never by index**, so reordering the palette cannot hand out an unbought
  colour. Free paints are never written to prefs — owned by definition, and storing them means one
  can be lost by clearing storage. `For()` also falls back to the default if the stored paint is
  no longer owned, so a reset or another profile's save code cannot leave a car wearing something
  unpaid for.
- **The paid four are a different KIND of finish, not a brighter hue.** Something costing 500,000
  gears has to look like a different material or the purchase reads as a con. The free five stay
  muted for the reason under `CarPaint`: these MULTIPLY a near-white body texture, so anything
  saturated comes out as flat poster colour.
- **Clicking a swatch only HIGHLIGHTS it; the action button selects or buys.** One misclick must
  not spend 500,000 gears. Owned paints could safely apply on click, but one rule for all nine is
  easier to trust than a rule that changes with what you can afford.
- **The preview goes through the car's own `CarPaint`**, the same component the spawner uses, so
  the podium and the car that drives out cannot disagree. Judging a paint on a swatch is not
  possible — the swatch is always brighter than the car.
- **`CarPodium.StageOffset` slides the plinth and car, never the rig.** The backdrop quad is a
  child of the rig and deliberately overfills the frustum; sliding it would drag the lattice off
  one edge. The shift is computed from the CAMERA'S right vector and converted into rig space,
  because the rig is rotated to face back at the camera so its local X is screen-LEFT — exactly
  the sign that gets guessed wrong once and hard-coded around.
- The panel closes when the garage is left or the car is changed: it shows what THIS car wears.

#### Why the paint did nothing at first — 2026-09-02

**Three of the four player prefabs had no `CarPaint` component at all.** `CarAventador` had one;
`CarE30`, `CarP72` and `CarTruck` did not. Every traffic prefab did, because traffic has been
tinted since it existed — **the player's car had simply never needed painting before**, so nothing
had ever noticed. `GetComponent<CarPaint>()` returned null and both the podium preview and the
spawner skipped in silence.

`CarPaint.Ensure(GameObject)` adds one when it is missing, rather than the component being added
by hand across four prefabs — being paintable is now a fact about how the game uses ANY car, not
a per-car setting. A prefab that needs a non-default material name still overrides by carrying its
own component, which is exactly what the Aventador does.

**The material names were the other half of the worry, and they were fine.** E30, P72 and the
truck all have a `Body.mat`, which is the `paintMaterialName` default; the Aventador's is
`Lamborginhi_base_phong` and its prefab already said so. Worth checking before adding a component
whose default might match nothing — `Collect` logs every material on the model when it matches
none, which is the only reason the P72's old `Standard32B531` was ever found.

**PAINT is offered on EVERY car, owned or not.** Choosing a colour for a car you have not bought
is harmless — the choice is stored against the car id and worn the moment it is owned — and
hiding the button on locked cars made the feature look absent.

**The car sitting slightly off-centre after the panel closed was fixed by making the placement a
pure function rather than hunting the frame that dropped a write.** The stage offset is now
recomputed from `stageShift` every frame unconditionally, the lerp SNAPS once within a millimetre
(`Mathf.Lerp` toward a target only ever approaches it), and the car's local position is
re-asserted from a stored lift each frame. Anything that nudges it is corrected on the next frame
instead of persisting until the car is swapped.

#### Clicked buttons went grey and would not light up again

**uGUI ranks SELECTED above HIGHLIGHTED**, and a clicked button holds the selection until
something else takes it. So after one click a button draws `selectedColor` even with the pointer
still on it — grey, and no amount of hovering brings the gold back. Worst on the garage arrows,
which are meant to be clicked repeatedly without moving the mouse.

**This is the other half of a fix already in this file.** Matching `selectedColor` to `normalColor`
(recorded under the garage carousel) stopped every clicked button staying LIT; it could not
restore the hover, because the state machine never reaches Highlighted while the button holds the
selection. `UiKit` now clears the EventSystem selection after every button click. Both halves are
needed: the colour for when the mouse leaves, the deselect for when it stays.

Text fields are not routed through it, and clearing the selection on a button click is what should
happen anyway — it blurs the field, which is what commits a typed value in the dev tuner.

#### The paid four are real METALS, not brighter greys — 2026-09-02

A paint is a colour AND a surface. Each entry carries `metallic` and `smoothness`, written into
the same MaterialPropertyBlock as the tint (`_Metallic`, plus `_Smoothness` and `_Glossiness` for
the URP and Standard paths).

| Paint | Reflectance | Metallic | Smoothness |
| --- | --- | --- | --- |
| the free five | as picked | 0 | 0.45–0.50 |
| Silver | 248, 245, 233 | 1.0 | 0.80 |
| Gold | 255, 195, 86 | 1.0 | 0.86 |
| Platinum | 173, 164, 150 | 1.0 | 0.93 |
| Phantom Black | 14, 15, 19 | 0.55 | 0.97 |

- **On a metal the base colour stops being albedo and becomes the tint of the REFLECTION**, so the
  RGB has to be the metal's measured F0 or it reads as shiny plastic. These are the standard
  values, not eyeballed ones — which is why **platinum is DARKER than silver**. Real platinum is
  greyer, and on the car it still reads as the more expensive finish because it is smoother.
- **Obsidian is volcanic glass, not metal.** Phantom black keeps some diffuse (0.55) and takes the
  highest smoothness in the palette. At full metallic it would be a black mirror with no depth.
- **`Apply(colour)` still exists and leaves the finish alone**, which is what traffic uses —
  negative means "do not touch", because 0 is a legitimate value for both and traffic must keep
  the finish its material was authored with.

**⚠ METAL WITH NOTHING TO REFLECT RENDERS BLACK, and nothing will say why.** The mesh, the
material and the colour are all still correct; the car just looks like tar. Both scenes are fine
as they stand — `MainMenu` has `m_AmbientMode: 0` (Skybox) and `m_DefaultReflectionMode: 0`, so
the skybox supplies an environment even though the backdrop quad hides it from view, and every map
has a real sky. **A new scene without a skybox, or ambient switched to a flat colour, breaks every
metallic paint at once.** Check those two settings before blaming the paint.

**The UI swatch is DERIVED, not the raw colour.** A flat square cannot show shine, so drawn
honestly the 200,000 platinum looks like a duller version of the 50,000 silver. `Paint.Swatch`
lifts the colour toward white in proportion to `metallic` — the reflected light, approximated —
and is computed from the paint rather than hand-picked, so a tenth colour needs no second
decision. **The value applied to the CAR is untouched.**

#### Save code v2 — and the delimiter lesson, applied properly this time

v1 was `version|gears|best|owned…|checksum` where the owned list was **itself** pipe-delimited, so
the field count was variable and the parser had to take the fixed fields off the front and the
checksum off the back. That worked, but it only works for ONE inner list — and paints need three
(cars, paints, car-to-paint choices).

**v2 re-joins the inner lists with COMMAS and keeps the top level a fixed seven fields.** That is
the real fix for the bug v1 shipped with: *a delimited list inside a delimited format needs a
different delimiter, not a cleverer parser.*

**C1 codes are still READ, never written.** A code someone saved yesterday has to keep working; a
save system that loses progress to a format change is worse than no save system. A v1 code simply
restores no paints.

### Four bugs from one play session — 2026-09-02

Reported together, and two of them turned out to be the same bug.

#### Traffic self-damage paid the player, AND that is why WRECKER never appeared

`CarDamage.PartLost` carried only the part — **no source, no `byPlayer`**. `Damaged` had been
given both when car-on-car scoring went in; this event was simply missed. So with
`scoreTrafficDamage` on, every traffic car that wrecked itself on a rock paid the player and
threw a popup.

**The second symptom followed from the first, and that is the part worth remembering.** There are
only **eight popup slots**, recycled round-robin. Traffic destroying itself filled all eight
continuously, so the WRECKER popup — which was firing correctly the whole time — was recycled
before it could be read. **"PvP is not working" and "I get popups for cars I never touched" were
one fault, not two.** Worst on Everest, because `obstacleAvoidance` is 0 there by design and the
field ploughs straight into the scenery, which is why it looked map-specific.

`PartLost` now carries `(CarDamage source, Part part, bool byPlayer)` and `RunScore.OnPartLost`
applies the same rule `OnDamaged` always did: your own parts always pay, someone else's only when
you knocked them off — at the PvP rate, and with a WRECKER caption in `pvpColour`.

**A popup budget is a scoring rule in disguise.** Anything that can fire faster than eight
concurrent popups will silently starve everything else on screen.

#### Car-on-car should CRUMPLE 3x, not SCORE 3x

`carVsCarDamage` multiplied the damage number, which drives the score *and* panel health *and*
deformation together. The ask was visual — hits that look worth watching — so the multiplier now
applies to the dent alone:

| Field | Value | What it multiplies |
| --- | --- | --- |
| `carVsCarCrumple` | **3** | deformation on a car-to-car hit, nothing else |
| `carVsCarDamage` | **1** | the damage NUMBER: score and panel health |

`carVsCarDamage` was 3 on all four player prefabs and is now 1 — **edited in the prefabs, since a
changed code default does not touch a serialized component.** It stays as a field because
"panels come off more readily in a collision" is a real thing to want; it is just not what was
asked for, and amplifying it double-counts against `gearsPerPvpDamage`.

#### The airtime timer restarted mid-flight

It ended the jump on the FIRST frame of contact, so a single graze — a wingtip on a rock face
mid-flight — banked the jump and restarted the counter from zero while the car was still in the
air. On Everest, a jagged 70-degree face, that happens constantly.

`landingGrace` (0.2 s) requires contact to PERSIST before it counts as landing. A graze is contact
for a frame or two; a landing is contact that stays.

**Two serialized values were also wrong, and one of them contradicted this file.** `minAirTime`
was **0.45 in every scene** while CLAUDE.md has said 0.8 since 2026-09-01 — the exact
"changing a default in C# does not change the scene" trap documented above, sitting undetected in
four scenes. Now 0.8 everywhere. `maxAirTime` was 9 on three maps, which caps the payout at
`9 x 26 = 234` gears — the number that was seen freezing. Raised to 20 on those three; **Quarry
is left at 200**, which was set by hand.

#### The podium sometimes showed no car, and it never recovered

Made concrete rather than diagnosed, because the failure is silent and total — nothing errors,
the plinth is simply bare.

The reason an empty podium STAYED empty is `MenuUI.shownPrefab`: it caches the prefab last shown
and skips the rebuild when it has not changed. Correct for the common case (a price label must
not respawn an 11,000-triangle car) and wrong the moment the car goes missing, because the cache
then says "already showing that one" for the rest of the session.

- `CarPodium.HasCar` — callers can ask instead of assuming.
- `RefreshCars` rebuilds when the prefab changed **or there is no car**, so any cause recovers on
  the next arrow press, page visit or purchase.
- `CarPodium.Update` self-heals: if the mount is visible and the car is gone, it rebuilds the
  remembered prefab **and logs a warning**. `cannotShow` stops it retrying a prefab with no
  meshes. A self-heal that hides the problem is worse than the problem, so it is loud.

### Unused-asset cleanup — 2026-09-02, and the check that made it safe

Removed 89 files: the whole **CosmoCars** pack (12 vehicles + `coupe-split.fbx`, superseded by
the E30 the same day it was imported and referenced by nothing since) and **20 loose Kenney
FBXs** — the ten vehicle bodies, three wheel styles and ten debris pieces nothing pointed at.
2.76 MB off the repo.

**It does NOT shrink the download.** Unity already excludes unreferenced assets from a build, so
this is clone time, import time and Inspector clutter. **The font is the one that actually
ships**, because `Resources/` is force-included — see the trim section above. Do not confuse the
two: deleting art feels productive and moves the download number not at all.

**A flat "is this GUID mentioned anywhere" grep is NOT a safe test, and it says the opposite of
the truth in both directions.** Two ways it lies:

- **An unused asset referencing another makes the second look used.** `texture-palette.png`
  appeared referenced — by CosmoCars' own materials, which are themselves dead. The test has to
  be REACHABILITY from the build-list scenes, walked transitively, not mere mention.
- **⚠ AND REACHABILITY MISSES EMBEDDED-MATERIAL MODELS, WHICH IS THE DANGEROUS DIRECTION.**
  Every FBX in this project imports with `materialLocation: 1` (Use Embedded Materials) and
  `materialSearch: 1`, with `externalObjects: {}`. That means **Unity binds their textures BY
  FILENAME at import time — there is no GUID reference to find.** The walk reported all 21
  CanyonTrack textures as unreachable. Deleting them would have silently untextured The Dam.

  The same applies to Everest, Quarry01 and all four split car FBXs. **Before deleting any
  texture, check whether an embedded-material model in the same folder wants it by name:**

  ```bash
  grep -l 'materialLocation: 1' $(find Assets -name '*.fbx.meta' -o -name '*.obj.meta')
  ```

What was deliberately KEPT:

- **`Source/` folders** (`aventador.fbx`, `e30-FullBody.obj`, `FullBody.obj`, `lct3000.fbx`,
  `glendam.fbx`). Unreferenced and they do not ship, but they are the inputs `split_car.py` needs
  to regenerate a car. Gitignore them if clone time ever matters; do not delete them.
- **Four Kenney debris models** — `debris-bumper`, `debris-door`, `debris-plate-small-a`,
  `debris-tire` — reached through the `Debries/` prefabs, which traffic still spawns.
- **All CanyonTrack textures**, for the name-binding reason above.

**Found while doing this and NOT fixed: `TrafficAventador.prefab` is an orphan.** It exists,
it is wired, and no scene's `TrafficSpawner.carPrefabs` lists it — so the Aventador never appears
in traffic on any map. Left alone because that is a design call, not a cleanup one.

### The TMP font is TRIMMED — done 2026-09-02, ~1.5 MB off every download

`LiberationSans SDF.asset` was 2.15 MB and shipped in every build whether or not anything used
it, because **everything under a `Resources/` folder is force-included**. It is gone, replaced by
`Assets/TextMesh Pro/Fonts/GameFontSDF.asset` — **654 KB, 250 glyphs, 512 × 512 atlas, Static
population mode.** TMP Resources went 2.19 MB → 0.02 MB; the new asset ships because TMP Settings
references it, so the **net saving is about 1.5 MB.**

**The character set is DERIVED, and re-derive it before adding UI text.** Every string literal in
the UI scripts, plus `CarRoster.asset` and all five scenes, was scanned for codepoints above
U+007F. The entire game needs ASCII plus exactly two characters:

| Character | Codepoint | Where |
| --- | --- | --- |
| `·` | **U+00B7** | separators — "0 gears banked · best run 0", the patch-note bullets |
| `—` | **U+2014** | em dash — "New map — Bullseye", most menu subtitles |

**Font Asset Creator custom range: `32-126,183,8212`.** Both were verified present in the shipped
asset, along with all 95 ASCII codepoints, before the stock font was deleted.

**A missing glyph renders as a BLANK BOX, not as an error.** Any new UI string with a `°`, `×`,
`→`, an accented letter or a curly quote adds a codepoint the atlas has not got, and nothing
anywhere will say so. Re-scan before assuming the range still holds:

```bash
# every non-ASCII codepoint in the UI scripts
perl -ne 'for (split //) { $s{$_}++ if ord > 127 } END { printf("%s U+%04X\n", $_, ord) for sort keys %s }' \
  Assets/Scripts/Menu/*.cs Assets/Scripts/Game/*.cs
```

**What had to happen together, and why the order matters.** Generating, retargeting and deleting
are one operation. Delete the stock asset before the replacement is assigned and the project has
no default font — which **does not error**, it renders every label as nothing. The safe order:

1. Generate the asset **outside any `Resources/` folder** (the whole point — referenced assets
   ship, unreferenced ones in `Resources/` ship anyway, which is the waste).
2. Verify the glyph table: full ASCII plus 183 and 8212.
3. Point `TMP Settings.m_defaultFontAsset` at it. The field is private, so from a script it needs
   `SerializedObject`; by hand it is the Default Font Asset slot on the TMP Settings asset.
4. Only then delete `LiberationSans SDF.asset`, its `- Fallback`, and the `- Drop Shadow` and
   `- Outline` materials — all four are in `Resources/` and the last two reference the atlas
   inside the asset, so leaving them behind leaves broken material references in the build.

**Nothing outside TMP's own folder referenced the stock font** — checked by GUID across every
scene, prefab, material and asset — so step 3 was the entire rewiring. The HUD and menu are built
in code and take `TMP_Settings.defaultFontAsset` implicitly, which is why no scene needed
touching.

An `Assets/Editor/FontTrim.cs` was written to do all four steps as one menu item, then **deleted
unused** — Ethan had already generated the asset by hand in Font Asset Creator while it was being
written. Kept out of the repo rather than left lying around: a script that regenerates a font
nobody needs is a trap for whoever runs it next. The steps above are what it did.


### Web player settings (verified on disk)

`webGLDecompressionFallback: 1`, `webGLMaximumMemorySize: 512`, `webWasm2023: 1`,
`managedStrippingLevel: WebGL: 3` (High), `webGLCompressionFormat: 0` (**0 = Brotli** —
the enum is Brotli, Gzip, Disabled, so 0 is correct), `webGLThreadsSupport: 0` (must stay
off — threads need COOP/COEP headers Google Sites will never send), `webGLDataCaching: 1`.

`webGLExceptionSupport: 1` (Explicitly Thrown Only) is kept for development stack traces.
**Drop it to None before shipping** for the speed.

Untested lever if the Chromebook is slow: capping `devicePixelRatio` to 1. HiDPI Chromebooks
render far more pixels than the GPU can afford. Measure before reaching for it.

## NEXT SESSION — pick up here (updated 2026-09-02, Update 2 shipped)

**Update 2 is BUILT, PUSHED AND LIVE on Google Sites** — commit `b888188`, embed pinned by
`6d2aa86`, **25.04 MB total, 18.20 MB in the data file.** Four maps (Quarry01, Everest,
Bullseye, The Dam), four cars (E30, P72, LCT 3000 truck, Aventador), falling boulders, airtime
scoring, the podium garage, save codes, the turn-assist handling fix, and a dev tuner that takes
typed values and remembers them per car.

### DO THIS FIRST NEXT TIME: split the build files

**Agreed 2026-09-02 and deferred deliberately — it is the first thing to build next session.**

`publish.sh` should cut any `prod/` file over ~18 MB into `name.partN`, and `tools/embed.html`
should fetch the parts, merge them into a Blob and hand Unity the `blob:` URL. **See "THE 20 MB
CAP CAN BE DEFEATED BY SPLITTING THE FILE" in the build-size section for the working code**,
read off another Unity game running from the same CDN on a Google Site.

Why it goes first: **it retires the constraint that has shaped every content decision in this
project.** Two releases in a row have been fights with the per-file cap — Update 2's first build
came out at 35.32 MB and would not have loaded at all. Splitting makes that class of failure
impossible, and it does it without moving off jsDelivr, which is the one host known to get
through the school filter.

Keep the commit pin when doing it. The game that revealed the technique serves from `@main`,
which is the stale-cache trap documented in the deploy section.

### State on disk

**Everything is committed as of 2026-09-02.** `b888188` is Update 2 in full; `6d2aa86` pins the
embed to it and may still be unpushed — check `git log origin/master..HEAD`. Both split FBX
metas carry `isReadable: 1`, which deformation requires.

### Size, in proportion

The last build measured 18.20 MB of a 20 MB per-file cap, **but the cap is about to stop
mattering** (see above) and **the TMP font trim has since landed** — 2.15 MB of stock font
replaced by 654 KB, roughly 1.5 MB off the download. The next build should come in near
16.7 MB without any content changing. What still matters is download TIME on school Wi-Fi, and
the cheap wins there are now spent: every texture is crunched, nothing is over 1024, and the
font is trimmed.

Every texture in the project is now crunched and none is over 1024, so that well is dry.

#### Compile-checking without opening Unity

Worth knowing, because it caught an obsolete API before it reached the Editor:

```bash
UD="/c/Program Files/Unity/Hub/Editor/6000.3.8f1/Editor/Data"
# Build a response file (paths have spaces, so plain -r: args get split):
#   -r: every $UD/Managed/UnityEngine/UnityEngine*Module.dll
#   -r: Library/ScriptAssemblies/{UnityEngine.UI,Unity.TextMeshPro,Unity.InputSystem}.dll
#   -r: $UD/NetStandard/ref/2.1.0/netstandard.dll
#   then every Assets/Scripts/**/*.cs
dotnet "$UD/DotNetSdkRoslyn/csc.dll" @args.rsp
```

Needs `Library/ScriptAssemblies/` to exist, so the project must have been opened at least once.

**Editor scripts need a DIFFERENT reference set, and mixing the two produces pages of errors
that are all artifacts.** Anything under `Assets/Editor/` compiles against the modular runtime
assemblies **plus the modular EDITOR assemblies**, and against neither facade:

```bash
#   -r: every $UD/Managed/UnityEngine/UnityEngine*Module.dll     (as above)
#   -r: every $UD/Managed/UnityEngine/UnityEditor*Module.dll     (the extra bit)
#   -r: Library/ScriptAssemblies/Unity.TextMeshPro.dll
#   -r: $UD/NetStandard/ref/2.1.0/netstandard.dll
#   then Assets/Editor/*.cs -- and ONLY those, not the game scripts
```

**Do not add `Managed/UnityEngine.dll` or `Managed/UnityEditor.dll`.** They are monolithic
facades that redeclare what the modules already define, and Roslyn reports every use as
`CS0433: the type exists in both`. Adding `UnityEditor.dll` alone instead fails the other way —
it references `UnityEngine.dll` by name, so every `UnityEngine.Object` becomes
`CS0012: defined in an assembly that is not referenced`. Both look like real bugs in the script
and neither is. Compile Editor scripts SEPARATELY from the game scripts for the same reason.
Catches syntax, signature and obsolete-API errors in seconds. It cannot catch anything about
behaviour, serialization or the Inspector.

### Confirmed working in play mode

Car sits correctly on its tyres, drives, steers, wheels spin the right way and ride the
suspension. Four fixes got it there, all in `CarController.cs`, all documented under
*Architecture calls already made*: `wheelVisualEuler`, the cast overshoot, lateral grip at
the contact patch, and the bump stop / anti-roll bar pair.

**Deformation reads correctly** after a long tuning fight -- panels dent and cave in, no spikes,
no bulges, no black patches. Roof crush and sustained grinding are implemented but were added
after the last play test.

**Scoring runs (2026-08-30).** The HUD draws, `PlayerCar` registers itself with `RunScore`, damage
events arrive and the gear counter climbs. The combo multiplier and the floating popups were
confirmed later the same day on Quarry01.

**Banking to `PlayerWallet` is confirmed (2026-08-31).** The garage shows a six-figure balance
accumulated across runs, which can only have come through `RunScore.Bank()` on scene unload, so
the whole earn-bank-spend loop is closed and observed end to end.

**The LCT 3000 truck drives (2026-08-31).** That also confirms the entire `--nose` reorientation
path: the truck reached Unity upright, facing forward, with `wheelVisualEuler (0, 0, 0)` at Scale
Factor 1.0, exactly as the P72-layout argument predicted. `--nose` is now the recommended flag
for every future model, and the wheel-axis table in the Blender section is proven, not reasoned.

**The Update 1 menu works (2026-08-31), on the live Web build.** The patch-notes panel renders on
the main screen, the garage no longer clips at three cars, and RESET PROGRESS arms on the first
press and erases on the second. That also confirms `UiKit.Band`'s adaptive layout, the
`fontSize` parameter on `UiKit.Button`, and `Show()` repainting Main and Cars on arrival --
which is what makes the wallet display correct immediately after a reset rather than stale.

**The front end works (2026-08-30).** Main menu, map select, car select, GO, TAB pause, resume and
return to menu all confirmed. That also confirms the code-built canvases, the
`InputSystemUIInputModule` created by `UiKit.EnsureEventSystem`, `RestartOverlay`'s load-by-name
path, and the pause menu running on unscaled time at `timeScale 0`.

**Quarry01 drives, and the traffic races it (2026-08-30).** The course was imported, textured with
the CC0 Poly Haven pair, and driven. Three AI cars run it flat out. Confirmed along the way: the
scoring popups, sustained damage not building combo, the combo multiplier, the SphereCast ground
filter that stopped wheels climbing ledges, and `CarPaint` tinting only the body once it painted
per submesh instead of per renderer.

It did not work first time, and the failure mode is worth remembering: `RunScore.playerCar` was
left empty in the scene, so nothing ever registered, and the HUD rendered a counter that sat at 0
forever. **A frozen readout looks identical to a broken formula, a broken event and a broken
subscription.** That is what the read-only fields on `RunScore` and `ScoreHud` are for, and the
Console warning naming the missing component was on screen the whole time.

### Never run in Unity — still inspection-clean only

**These are the oldest unverified things in the project and they are now cheap to clear.** The
truck has **12 detachable parts at deliberately low health**, so a single run in it exercises
panel detachment, the part bonus popup, `Part.displayName` captions, detached wheels as debris
and the wheel/mirror feats all at once. Nothing below needs a special test build — just drive the
truck into things and watch. Do this before adding more damage code.

- **The part bonus popup.** Needs a panel to actually come off, which is itself unrun (below).
  `Part.displayName` was added so the caption reads "Wheel Front Right" rather than `WHEEL-FR`;
  whether those captions appear at all is untested.
- **Feats.** Written 2026-08-31, wired, and **never observed firing.** "Boat" needs all four
  wheels gone in one run and "Mirror Mirror" both mirrors, which again is easiest in the truck.
  If they never appear, check the `Group` string on the mirror parts first — a group that
  matches nothing is guarded against firing vacuously, so the symptom is silence, not a wrong
  popup.

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

**Closed since this list was written**, so they are not re-opened by mistake: the front end
(2026-08-30), the E30/P72/truck CC-BY attribution on the car-select screen, the download
re-measure, the Chromebook frame rate, and banking to `PlayerWallet`. All are recorded under
*Confirmed working* or in the measured sections above.

1. **Trim the TMP font.** ~2.1 MB. The reason it is first is the per-file cap, not the size
   budget — see the roadmap.
2. **Check the two scoring conversion values.** `Quarry` once carried `gearsPerDamage: 1` and
   `gearsPerPartHealth: 5` from debugging — **50x and 20x the intended `0.02` and `0.25`**. It
   has never been explicitly confirmed that they were put back, and a six-figure wallet cannot
   distinguish "correct rates over many runs" from "50x rates" because dev mode grants
   1,000,000 outright. **Read the two fields on `RunScore` in the scene before trusting any
   scoring number**, and re-tune now that there are three cars and traffic to hit.
3. **`PartTrunk` on the E30 has still never been wired.** The mesh has it (146 tris); add a 12th
   part — name `trunk`, Visual `PartTrunk` under `e30-split`, Anchor None, Health 120, Wheel
   Index -1 — or the boot lid cannot come off. Note the truck already has 12 parts and does not
   need this.
4. **Glass, interior props, roof crush and sustained grinding on the E30 are still unverified.**
   Written, wired, never deliberately tested. The truck does not use `CarInteriorProps` at all
   (it has a real interior) and needs `glassMaterialName` set to its own material, so a result
   on one car does not carry to the other.
5. **Damage thresholds are only half-calibrated, and now differ per car.** `lastImpulse` measured
   ~16,500 on an E30 wall hit; the 3,000 kg truck should report ~2.5x that. Read `lastImpulse`
   on each car before changing `minimumImpulse` or `maxDamagePerImpact` — and see the heavy-
   vehicle note under the truck, because the two scale in opposite directions with mass.
6. **Decide whether hitting traffic pays** (`TrafficSpawner.scoreTrafficDamage`, still off).

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
   (cast overshoot · bump stop · anti-roll bars · roll direction · ledge-climb filter) ·
   **scoring wired and counting** (gears · combo · HUD · wallet · `PlayerCar` self-registration) ·
   **Quarry01 built, textured and driven** (generator · kickers · bays · mountainsides · CC0 textures) ·
   **traffic AI racing the hill** (descent-seeking · obstacle avoidance · destructible · painted)
   · **the front end** (menu · map select · garage · TAB pause · fullscreen)
   · **the garage** (buy cars with gears · roster asset · spawned player car)
   · **dev mode** · **shipped to Google Sites, 14.96 MB, commit `f87f071`**
   · **60 FPS measured on a real school Chromebook** · **Everest, the second map**
   · **feats** · **the LCT 3000 box truck — split, wired and driving (third car, 12 parts)**
   · **the truck in the traffic mix** · **patch notes and RESET PROGRESS on the front end**
   · **release tooling that catches a stale build and an unpinned hash**
   · **UPDATE 1 shipped to Google Sites, 21.63 MB, commit `1380215`**
   · **falling boulders on Quarry** · **airtime scoring** · **air rotation and the airborne camera**
   · **Bullseye, the ramp-and-target map** · **The Dam, the first imported map**
   · **the Aventador, the fourth car** · **the podium garage with its animated backdrop**
   · **save codes** · **an MPH speedometer** · **turn assist with an understeer gradient**
   · **points for wrecking other cars** · **a dev tuner that takes typed values and saves them**
   · **UPDATE 2 shipped to Google Sites, 25.04 MB, commit `b888188`**

2. **Now — SPLIT THE BUILD FILES.** Cut any `prod/` file over ~18 MB into `name.partN` in
   `publish.sh`, fetch and Blob-merge them in `tools/embed.html`, hand Unity the `blob:` URL.
   Working code is in the build-size section under "THE 20 MB CAP CAN BE DEFEATED BY SPLITTING
   THE FILE". **This retires the per-file cap**, which has been the binding constraint on
   content for two releases and has twice produced a build that would not have loaded at all.
   Roughly an hour, in two files that are already hand-written for this job. Keep the commit pin.

   **The TMP font trim is DONE** — 2026-09-02, 2.15 MB stock asset replaced by a 654 KB
   250-glyph one, about 1.5 MB off every download. See the font section for the derived
   character range and why a missing glyph fails silently.

   The frame-rate question that used to sit here is **answered**: 60 FPS on a real school
   Chromebook, 2026-08-31. See the measured section above. Re-measure after anything that
   changes scene scale — including the boulders, which postdate that measurement — but it is no
   longer a blocker.
3. **Next** — in rough order:
   - **Re-tune damage and scoring now there are FOUR cars and FOUR maps**, and decide whether
     hitting traffic should pay (`TrafficSpawner.scoreTrafficDamage`, still off). Sanity-check
     the P72's 50,000, the truck's 20,000 and the Aventador's 100,000 against a real run.
   - **A fifth map.** Pick numbers that change how it
     DRIVES, not just how it looks: corridor width and curviness do that, a new seed alone does
     not. Everest proved the point by initially reading as Quarry with different textures.
   - **Panel seams on the P72.** Region boxes cut straight lines through a curved body; fixing
     it properly needs carving that snaps to edge loops. The truck showed the better answer for
     any future car — `--keep` an artist's own cuts and never carve at all.
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
- **`maxGripForce` set to 26 instead of 26,000 makes the car drive on ICE, and nothing says so.**
  Happened 2026-09-02 across three prefabs, from a table that wrote the values with comma
  separators. 26 N on a 1,200 kg car is **0.009 g** of lateral grip — the tyres cannot act at
  all, so the car points into a corner and slides straight on. It looked like a physics bug, a
  map bug and a turn-assist bug in turn; it was a data entry error.

  The tell was that TRAFFIC cornered normally while the player car slid: traffic prefabs still
  had their old values. **When one car misbehaves and another does not, compare their prefabs
  before reading any code.**

  `CarController.CheckGrip` now logs an error at Awake naming the field and the value in G, so
  the silent version of this cannot recur.

- **Turn-in is ASSISTED toward a real yaw rate, not left to tyre force alone.** Reported
  2026-09-02: turning "sucks". Yaw came only from lateral tyre force, which is honest and feels
  vague — the car must build a slip angle before the fronts bite, and `maxGripForce` caps how
  fast that force can arrive, so turn-in lags the wheel and the car ploughs on first.

  `ApplyTurnAssist` drives the yaw rate toward what the car's own geometry implies, using the
  steady-state bicycle model **with an understeer gradient**:

      yaw = v x tan(steer) / (wheelbase + understeer x v^2)

  **The understeer term is not optional.** Raw Ackermann assumes the tyres never slip, and asks
  the E30 for a NINE METRE turn radius at 108 km/h — the assist would sit permanently saturated
  delivering it and the car would corner like a slot car. With `understeer` 0.006 the targets are
  5 m at 36 km/h, 28 m at 108 and 64 m at 162, which is a car.

  What is arcade about it is only that the yaw is helped along directly instead of waiting for
  the tyres. The steering angle still means what it says.

  Three guards, all load-bearing: GROUNDED only, or it fights air control and lets you steer on
  nothing; above `turnAssistMinSpeed`, or it spins a parked car on the spot; and the correction
  is CLAMPED by `maxTurnAssist`, which is what keeps a slide a slide rather than quietly
  straightening the car out.

  `wheelbase` is MEASURED from the wheel anchors at Awake rather than exposed as a field — it is
  a fact already in the scene, and a second copy is a thing to get wrong on one car only.

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
  `springStrength`** — it is serialized in `Quarry` at 0.9, try **0.35** (sag falls to
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
- **A SphereCast hit steeper than `maxGroundAngle` (55°) is a wall, not ground, and falls back
  to a Raycast.** Reported from play 2026-08-30: scraping past a tilted surface made the wheels
  beside it visibly climb onto it. A sphere sweep reports whatever the sphere **touches**, which
  includes anything beside the wheel as well as under it — graze a tilted face and the hit comes
  back well above the ground the tyre is on, the spring reads that as compression, and it lifts
  the corner onto the ledge.

  The fix keeps the sphere for what it is actually there for. Reject the steep hit, then cast a
  straight ray, which **can only ever report what is directly beneath** and so cannot be fooled
  sideways; the sphere still handles seams and edges everywhere it is genuinely looking at
  ground. Costs one extra raycast, and only in frames where a wheel is near a wall.

  Note `centreTravel` now keys off whether the SPHERE hit, not off `wheelSphereCast` — the two
  casts report distance differently and using the toggle would misread the fallback by a whole
  `wheelRadius`. Raising `maxGroundAngle` brings ledge-climbing back; much below ~45° and
  legitimate ramps stop counting as ground.

- **Camera framing is PER-VEHICLE, and the offsets are ADDITIVE.** `ChaseCamera`'s numbers were
  tuned on the E30 (4.16 m long, 1.36 m to the roof) and suit the P72, which is smaller again.
  The LCT 3000 broke them: **2.1x taller and 1.4x longer**, so the rig sat level with the box body
  and the truck filled the screen with the road hidden behind it.

  `Camera/CarCamera.cs` goes on a car prefab and supplies `extraDistance` / `extraHeight` /
  `extraLookHeight`. Three decisions worth keeping:

  - **Additive, never absolute.** A car that already frames correctly needs no component at all —
    absent means zero — so the E30 and P72 keep exactly the behaviour they have and cannot be
    disturbed by a change made for the truck. Each vehicle states only how it DIFFERS, so
    retuning the base rig still moves every car together instead of being silently overridden
    per prefab.
  - **It lives on the CAR**, not on the camera and not in `CarRoster`. Framing is a property of
    the vehicle's shape; the camera is one object that follows whatever spawns, and the roster is
    about price and ownership. `ChaseCamera` reads it through the same lazy `PlayerCar.Current`
    path it already uses, so a garage swap swaps the framing with it and nothing is wired.
  - **`RigLookHeight` is used by BOTH the driving aim point and the look-around blend.** Adjusting
    only one leaves a truck correctly positioned but still aimed at the middle of its box body,
    which reads as the camera being wrong in a way the position numbers cannot explain.

  **Derive the numbers, do not eyeball them.** Take the body bounds from `car_bounds.py` and keep
  the E30's proportions — roof 1.355, rear overhang 2.578:

  ```
  extraHeight     = roofHeight - 1.355                              (same clearance over the roof)
  extraDistance   = (rearOverhang - 2.578) + (roofHeight - 1.355) x 0.5
  extraLookHeight = roofHeight - 1.355
  ```

  The truck measures roof 2.84, rear overhang 3.01, giving **+1.5 / +1.2 / +1.5**.

  **Height is the lever that matters; distance barely is.** Framing arithmetic at FOV 60 says
  pulling back changes the screen fraction of a *long* vehicle very little — the truck is 6 m
  long, so most of the extra distance is spent on the length it already occupies. Raising the
  camera over the roof is what actually puts the road back on screen, which is also the first
  thing you notice is wrong. Reach for `extraHeight` first on any future big vehicle.

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
no post FX until measured.

**Two of these are now knowingly exceeded and both need re-measuring on the device:**

| Line | Budget | Actual | Status |
| --- | --- | --- | --- |
| Download | ≤ 20 MB | **25.04 MB** | over, and the budget line is the one that still bites — see below |
| Live rigidbodies | ≤ 40 | **up to 44** | 16 boulders + 4 cars + 24 debris, unmeasured |
| Frame rate | 60 / 30 floor | 60, ~1 ms jitter | measured 2026-09-01, but **before the boulders** |

The frame-rate figure is the one that licenses the other two, and it predates the change most
likely to break it. Read `FPS`, `worst` and `N boulders live` off the dev readout on a real
Chromebook before treating any of this as settled.

**On the download line: the 20 MB PER-FILE cap and the 20 MB BUDGET are different things, and
only the second one survives.** Splitting the build files (see the build-size section) retires
the cap entirely — it is a chunk size, not a wall. The budget is about how long a download takes
on school Wi-Fi, and splitting does nothing for that, so **≤ 20 MB stays the target and 25.04 MB
stays over it.** Argue new content down on download time and rigidbody count, not on the cap.

## Scripts

| Script | Job |
| --- | --- |
| `Camera/ChaseCamera.cs` | Automatic chase cam. No player input, ever. |
| `Camera/CarCamera.cs` | Per-vehicle camera offsets, on the car prefab. Additive; absent = unchanged. |
| `Vehicle/CarInput.cs` | Keys → throttle / steer / handbrake. Has a `Scheme` for split-screen. |
| `Vehicle/PlayerCar.cs` | Marks the player's car and announces it. `PlayerCar.Current` is how anything finds it. |
| `Vehicle/CarController.cs` | SphereCast suspension with bump stops and anti-roll bars, drive, grip, steering, air control. |
| `Damage/CarDamage.cs` | Impacts → part damage → detachment. Fires `Damaged` and `PartLost`. |
| `Damage/CarDeformation.cs` | Per-vertex denting on impact. Player car only. |
| `Damage/CarGlass.cs` | Empties the glass submeshes past a damage threshold. |
| `Damage/CarInteriorProps.cs` | Generates the dark engine bay / cabin a missing panel reveals. |
| `Damage/DebrisPool.cs` | Pools, caps, expires and sleeps detached parts. `Track()` adopts real panels. |
| `Game/RunRestart.cs` | R reloads the scene behind a loading bar. |
| `Game/RunScore.cs` | Damage, lost parts and airtime → gears. Combo multiplier. Feats. Banks the run on scene unload. |
| `Track/FallingBoulders.cs` | Boulders down the valley sides. Generated meshes, own pool, spawned ahead of the player. |
| `Track/DartboardScore.cs` | Scores where the car lands on Bullseye. Archery rings, 10 in the gold down to 1. |
| `Game/ScoreHud.cs` | Gear counter, combo bar, airtime, MPH speedometer and floating popups. Builds its own canvases in code. |
| `Game/PlayerWallet.cs` | Persistent gear balance, best run and owned cars, in PlayerPrefs. |
| `Game/SaveCode.cs` | Progress as a copy-paste text code. Works when browser storage does not. |
| `Game/SaveFile.cs` | Progress as a downloadable `.crash` file. Wraps SaveCode; needs Plugins/WebGL/FileIO.jslib. |
| `Game/CarColours.cs` | The paint shop: palette, prices, what is owned, and which car wears which. |
| `Game/SaveHealth.cs` | Counts launches to prove whether this browser actually persists anything. |
| `AI/TrafficDriver.cs` | Traffic AI. Steers at the biggest ground drop ahead. See the known limitation. |
| `AI/TrafficSpawner.cs` | Spawns the traffic grid, paints it, optionally registers it for scoring. |
| `Vehicle/ICarDriver.cs` | Throttle / steer / handbrake. Implemented by `CarInput` and `TrafficDriver`. |
| `Vehicle/CarPaint.cs` | Tints the body submesh via a MaterialPropertyBlock. |
| `Menu/MenuUI.cs` | Main menu, patch notes, map select, garage, options, reset progress. Carries the three CC-BY credits. |
| `Menu/CarPodium.cs` | Garage carousel: rotating car on a lit podium, animated backdrop, arrow sweeps. |
| `Menu/UiKit.cs` | Canvas, button and label builders shared by the menu and the pause screen. |
| `Game/PauseMenu.cs` | TAB pauses. Resume and return to menu. |
| `Game/GameSelection.cs` | Chosen map and car, by string id, in PlayerPrefs. |
| `Game/CarRoster.cs` | ScriptableObject: every car, its price and prefab. Shared by menu and spawner. |
| `Game/PlayerCarSpawner.cs` | Spawns the car the player owns and selected. |
| `Game/DevMode.cs` | Code-gated dev mode: gears, car tuner, perf readout. |
| `Menu/Fullscreen.cs` | **Unused since 2026-09-02.** Fullscreen cannot work inside Google Sites' nested iframes; every button was removed. Kept only to document that. |
| `Debug/PerfReadout.cs` | On-screen FPS/device readout. Only drawn in dev mode. |

### Changing a default in C# does NOT change a component already in the scene

Learned the hard way 2026-08-30 and it will happen again, so it is a standing rule.

Unity **serializes every public field into the scene** the moment a component is added. From then
on the scene is the source of truth and the C# initializer is only used for *new* instances.
Fixing `CarInteriorProps.props` in code left `Quarry` still holding the old boxes, and the
dash carried on poking through the bonnet with the bug "fixed".

**To adopt new code defaults: select the object, click the ⋮ menu at the top-right of the
component, choose Reset.** That re-runs the initializers. It resets *every* field on that
component, so check nothing else on it was hand-tuned first.

This applies to anything with a serialized default worth changing — `CarInteriorProps.props`,
`CarDamage.parts`, `RunScore`'s conversion rates, `CarController`'s suspension numbers. When a
fix is a changed default rather than changed logic, **say so explicitly and say Reset**, because
otherwise the fix looks like it did nothing.

### Scene setup (Quarry)

- **Car** — Rigidbody (1200 kg, Interpolate, Continuous), layer `Car`, with `CarInput`,
  `CarController`, `CarDamage`, `PlayerCar`. The FBX is a child at origin, **Scale Factor 1.0**.
- **Wheel anchors** `WheelFL/FR/RL/RR` at local `(±0.719, 0.50, +0.909 / -1.661)` — **measured from the scene, 2026-08-30; the previously documented `(±0.877, 0.61, +1.776/-1.345)` was wrong**. Wheelbase 2.57 m, track 1.44 m, both matching the real E30. With
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
- **GameManager** — `RunRestart`, `DebrisPool`, `RunScore` (Player Car empty — the car finds it),
  `ScoreHud`, `PauseMenu`, `TrafficSpawner` (+ a child `TrafficGrid`), `PlayerCarSpawner`
  (Roster = the CarRoster asset, Spawn Point = `PlayerSpawn`).
- **PlayerSpawn** — an empty marking where the player car appears. Just a marker: putting a
  second `PlayerCarSpawner` on it is what caused two cars to spawn.
- The player car is **NOT in the scene**. It is a prefab, spawned from the roster.
- **Main Camera** — `ChaseCamera` and `PerfReadout`, both with their car reference left **empty**:
  they find the spawned car through `PlayerCar.Current`. A stale reference to a deleted object
  beats the fallback and gives you a camera following nothing.
- **TrafficCar prefab** (`Assets/Art/Vehicles/`) — a copy of `Car` with **`PlayerCar` and
  `CarInput` removed** and `TrafficDriver` + `CarPaint` added. Not in the scene; `TrafficSpawner`
  instantiates it. See the traffic section for why `PlayerCar` must be absent rather than stripped.

### Scene setup (MainMenu)

- **Main Camera** — the default one. A Screen Space Overlay canvas draws without a camera, but
  Unity complains and the Game view goes black otherwise.
- **Menu** — an empty GameObject with `MenuUI` (Roster = the CarRoster asset). It builds its own
  canvas, pages and EventSystem.

**Both scenes must be in File > Build Profiles > Scene List.** `LoadSceneAsync` returns null for
an unlisted scene, which is the single most likely reason the front end does nothing.

**Scene List order, verified on disk 2026-08-31: `MainMenu` 0, `Quarry` 1, `Everest` 2.** That is
correct for shipping — index 0 is where a build starts, so the front end is reachable.

It was the other way round while the menu was being built, because starting in the game is
convenient in the Editor. **Check this before every release**: index 0 decides what a player
sees first, nothing in the Editor warns if it is wrong, and the failure is a build that drops
straight into a run with no menu, no map select and no garage.

The scene was renamed `SampleScene` → `Quarry` on 2026-08-30. `MenuUI`'s map list and its code
default both say `Quarry`; anything still referring to `SampleScene` is stale.

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

Positions are in CAR-local space with the ground at `y = 0` and `+z` forward. Select the car to
see them as gizmos while adjusting.

**MEASURED bounds, 2026-08-30 — the figures previously written here (`y 0..1.21`, `z -2.41..+1.45`,
`x ±0.84`) were WRONG, and they are what put the dash box through the bonnet.** Get them from
`tools/blender/car_bounds.py`, never by hand:

| Object | x | y | z |
| --- | --- | --- | --- |
| Body | −0.820 … 0.863 | 0.145 … 1.355 | −2.578 … 1.586 |
| InteriorShell | −0.763 … 0.805 | 0.187 … 1.306 | −2.499 … 1.524 |
| **PartHood** | −0.761 … 0.785 | **0.671 … 0.912** | **0.240 … 1.346** |
| PartTrunk | −0.765 … 0.797 | 0.733 … 1.284 | −2.386 … −1.519 |
| PartDoorL / R | −0.801…−0.168 / 0.219…0.843 | 0.271 … 1.082 | −1.248 … 0.306 |

**The hood is a SLOPE, not a ceiling, and that is the trap.** Its surface falls from `0.912` at
the cowl to `0.671` at the nose — about **0.218 m of headroom lost per metre forward**. A box with
a flat top therefore has least clearance at its *front* edge, which is exactly where both the dash
and the engine broke through. Headroom at a given z is roughly `0.912 - 0.218 * (z - 0.240)`.

Reported from play 2026-08-30: the dash topped out at `0.96` against a hood surface of `0.912`, so
it protruded where the windscreen meets the bonnet. The engine block topped out at `0.775`, which
clears the cowl but breaks through past the halfway point — the same bug, not yet noticed. Both
are fixed and both were found by measuring rather than by looking.

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

**`Part.anchor` is set on 9 of the 11 parts in `Quarry`, and it should not be.** Those are
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

## MEASURED ON A SCHOOL CHROMEBOOK — 2026-08-31

**60 FPS, no lag, fast load**, playing the live Google Sites build for a day.

This closes the largest open question in the project. Every performance claim written here before
this date was reasoning, not measurement — and the reasoning turned out to be right, on the real
device, with the real build:

- 100k-triangle course, chunked at 100 m
- Four cars, each a rigidbody with four sphere casts a physics step
- Traffic AI probes, ~2 raycasts per step per car
- Two code-built canvases, TMP text, deformation, detachable panels
- 14.96 MB download over school Wi-Fi

**What this licenses:** the budget has real headroom, so the next feature does not have to be
argued down on performance grounds before it is tried. Spend it on things that make the game
better and measure again afterwards.

**What it does not license:** the numbers still get re-measured after anything that changes scene
scale — more traffic, a bigger map, realtime shadows, post FX. 60 FPS on *this* content is not a
blanket permission. And it is one device on one day; a different Chromebook model may be slower.

### Hard numbers off the readout — 2026-09-01, Update 1, both maps

The first time the actual device specs were read rather than assumed. **Both maps, 60 FPS.**

| | Quarry | Everest |
| --- | --- | --- |
| FPS | **60** | **60** |
| Worst frame in the sample window | **17.0 ms** | **18.0 ms** |
| Resolution | 1041 × 670 | 1041 × 670 |
| API | OpenGLES3 (WebGL2 via ANGLE) | same |
| GPU | **Intel UHD Graphics (JSL)** — Jasper Lake, Mesa | same |
| Heap | **154 MB** of the 512 MB cap | same |
| State when sampled | 22 km/h, grounded | 143 km/h, airborne |

**Read `worst` correctly or it looks like a warning when it is the opposite.** `PerfReadout`
resets `worstFrame` every sample window, so 17-18 ms is the worst single frame **in each second**,
against a 16.67 ms vsync budget. That is about a millisecond of jitter on a locked 60 — i.e.
effectively no dropped frames. A device in trouble shows 30-50 ms spikes. This is as clean as a
vsynced 60 gets.

**Jasper Lake UHD is genuinely low-end** — a Celeron/Pentium-class Chromebook part — which makes
the result stronger, not weaker. **The original framing in Working Rule 2 ("a browser tab that
dies if the WASM heap spikes") was too pessimistic**, and Ethan was right to push back: the heap
sits at 154 MB against a 512 MB ceiling, so there is roughly 3.5x memory headroom, and the frame
budget is being met with room to spare on the worst frames rather than scraped.

**The two caveats that are real, and are not pessimism:**

1. **This is 1041 × 670 — 0.70 megapixels.** That is the Google Sites iframe, not fullscreen.
   Fullscreen at 1920 × 1080 is **2.07 MP, about 3x the fragment load**, and *that* number has
   never been measured. Anything GPU-bound (post FX, realtime shadows, a heavier shader) must be
   judged at fullscreen, not in the iframe. There is a fullscreen button; use it and re-read.
2. **`1 cores` is not the device's core count.** `SystemInfo.processorCount` returns 1 on Web
   because `webGLThreadsSupport` is off. The device has more; we cannot use them. Physics stays
   single-threaded, so **CPU cost is still the thing to watch, and rigidbody count is still the
   first thing to blow it** — that part of the budget is unchanged.

**Revised standing budget:** keep 60 FPS target / 30 floor and the no-realtime-shadows rule, but
stop treating memory as scarce. The binding constraint on new content is now the **20 MB
per-file download cap**, not the frame rate — see the build-size section. When something has to
be argued down, argue it down on download size first and rigidbody count second.

**A comparison worth keeping in proportion:** Papa's games and similar run fine on these devices,
but they are 2D sprite games with no physics solver. They are evidence the *device* is capable,
not that a 3D rigidbody game is cheap on it. The right conclusion is the measured one above.

## Ideas for later — not started

Ethan's, recorded so they are not lost. Nothing here is designed or scheduled.

- **Police chase mode.** Pursuit AI that hunts the player rather than racing to the bottom. The
  probe fan is reusable for avoidance; the direction source becomes "toward the player" instead
  of "steepest descent", which is exactly the swap the traffic AI's known limitation already
  anticipates.
- **Race mode.** The traffic AI already races to the bottom, so this is mostly scoring,
  positions and a finish line rather than new driving code.
- **Destruction derby mode.** An arena rather than a descent, last car running wins. Note this
  breaks descent-seeking completely — there is no downhill in an arena — so it is the mode that
  forces the AI's direction source to be replaced properly.
- ~~**Falling rocks on Quarry.**~~ **BUILT 2026-09-01** — `Track/FallingBoulders.cs`. See the
  section under *Scoring* for how it works and what the three pre-flight concerns turned into.

## Game design

Arcade crash-driving, third-person chase cam.

- **Garage** — carousel of cars on a podium, `<` `>` to cycle, gear currency, buy/select, GO
- **Run** — spawn at the top of a long downhill mountain road, AI traffic ahead
- **Goal** — bomb downhill and destroy the car; damage earns gears; gears buy more cars
- **Damage** — deformable panels plus detachable parts: doors, hood, bumpers, wheels

### Maps — first course built 2026-08-30

**Maps are GENERATED, not downloaded.** Searched properly first: no CC0 or CC-BY asset exists
that is a drivable downhill destruction course. The good-looking terrain is photogrammetry scale
(a Sketchfab canyon at **229k tris**, against an 11.6k-tri car), the CC0 modular kits (Kenney
Racing Kit, Fertile Soil) are toy low-poly, and the open-source racing games ship **GPL**
(Stunt Rally) or **CC-BY-SA** (SuperTuxKart) track data, neither of which belongs in this repo.

**RIPPED COMMERCIAL GAME ASSETS ARE RULED OUT, and this is the strictest of the asset rules.**
Offered 2026-09-02: `nfs-iii-hot-pursuit-country-woods/track5.fbx`, a Need for Speed III track.
The geometry and textures are EA's copyrighted work, and **a Sketchfab uploader cannot license
someone else's content under CC-BY** — the licence shown on such a page does not attach to data
the uploader does not own.

This is materially different from the Lamborghini, where the MODEL is an original CC-BY work by
its author that happens to depict a trademarked car. Here the asset itself is the rights holder's.

The consequence is specific rather than abstract: **the repo has to stay public for jsDelivr**, so
infringing content sits in public, and a takedown against the repo takes the GAME OFFLINE because
jsDelivr serves the build from it. Renaming the file does not change any of that.

**Unity Asset Store assets are also ruled out permanently** — the EULA forbids redistributing
them, and this repo must stay public for jsDelivr. That eliminates most search results for
"free Unity track".

The reference maps (BeamNG *Downhill Destruction*, *Slant of Death*) are a **sculpted hillside
plus placed obstacles**. Realism is in the texture and the silhouette, not the triangle count —
the same trade the E30 already makes. And the obstacle layout is game design, which nothing
downloadable could supply.

`tools/blender/build_course.py` sweeps a descending, snaking centreline into a corridor with
terraced walls, then scatters obstacles. **Quarry01**, the first map:

| | |
| --- | --- |
| Length / drop | 1,800 m, 270 m (15% average, 23.1% steepest) |
| Drive time | ~90 s at the 20 m/s a crash course averages (`topSpeed` is 32) |
| Tightest turn | 99 m radius |
| Corridor | 26 m drivable + 5 m shoulder |
| Sides | 22 m benched quarry face, then 95 m of mountainside over 120 m (38°) |
| Ridge | **88–146 m above the corridor**, varying along the course. Total width 308 m |
| Triangles | **100,156** — 18 chunks × 5,200, bays 4,956, boulders 1,600 |

Decisions worth keeping:

- **Chunked at 100 m, not one mesh.** One mesh is a single draw call with **no frustum culling**
  — all 66k triangles submitted every frame. Chunked, 4–6 are ever visible: ~17k rendered. It
  also cooks colliders faster and lets the PhysX broadphase reject most of the world.
- **Terraced quarry benches are what make it read as excavated rock.** A smooth wall reads as a
  sand dune however much noise is on it; that is exactly how the first two renders came out.

- **The sides go BENCHED FACE first, then MOUNTAINSIDE.** 22 m of quarry cut, then 95 m of
  natural slope, so it reads as a quarry worked into a mountain rather than either one alone.
  Added 2026-08-30 because a 22 m wall read as a trench and was low enough to launch out of.

  **The crest wanders along the course and the two flanks use different noise phases.** A
  constant height reads as an embankment however tall it is — an uneven skyline, with the two
  sides disagreeing, is what reads as mountains. Sampled from one function they rise and fall
  together and it reads as a channel.

  **Climbing out is arithmetic, not hope.** The 54° bench face needs `1200 × 9.81 × sin 54° =
  9,530 N` against `2 × enginePower = 6,400 N` of drive. Note this is only safe because the
  face is steeper than the car can climb — `CarController.maxGroundAngle` is 55°, so the face
  still counts as ground and would be drivable if it were any shallower.

  The mountainside is sampled at **16 m** against the corridor's 2 m. Nothing drives on it and
  it is mostly seen at distance; at corridor resolution it more than doubles the whole course
  for detail nobody can resolve.
- **Rock is FLAT-shaded, the corridor smooth.** Free, and the single biggest look change —
  smooth-shading a faceted wall averages its normals into draped fabric.
- **Terrace the height ABOVE the shoulder, never the absolute height.** Quantising absolute
  height put the first bench tread *below* the shoulder top (measured: 3.96 m dropping to
  2.72 m), which is a **ditch down both corridor edges** that would snatch a wheel. The report
  prints the cross-section and flags any non-monotonic step.
- **THE TERRAIN IS A SURFACE, NOT A SOLID, AND THAT HAS BITTEN TWICE.** A heightfield has a top
  and no underside, so anywhere you can get outside or beneath it, every polygon is
  backface-culled and you see straight through. It showed up first as see-through ramp lips, then
  again from beyond the wall, where the whole map read as floating bench ribbons with sky between
  them. Two defences, and both are load-bearing:

  - **A skirt** drops from the outermost column to `--skirt` (9 m) below the corridor floor, with
    caps on the first and last cross-sections and the same treatment round the bowl. ~200 tris a
    chunk, about 9% on the total, and without it any exterior view looks broken.
  - **No feature face steeper than ~60°**, so there is nowhere on the drivable surface to tuck
    under. The build prints the measured worst face and warns past 60.

  It is still open at the very bottom — look from directly beneath and you see in. Nothing in the
  game ever does.

- **The bowl is a true HALF disc.** Its flat edge meets the corridor end, so nothing overlaps the
  last chunk into coplanar z-fighting. The **start bay** is the same builder with `forward`
  flipped, sitting behind the first station and opening down the course, and the player spawns in
  it rather than on the first metre of the descent.

  **Flipping `forward` mirrors the parameterisation and reverses the handedness of every quad
  built from it**, so the winding is reversed to match. Without that the entire bay renders
  face-down and is invisible from above — the inside-out ramp bug a third time. Any future
  mirrored geometry needs the same care.
- **Obstacles ARE the terrain, not objects standing on it.** Kickers and humps are folds in the
  height function. This started as separate wedge and box meshes and was wrong twice: they read
  as popcorn scattered on the track, and the hand-written wedge winding was **backwards** — the
  bottom face `(0,1,3,2)` winds counter-clockwise seen from above so its normal pointed up into
  the solid — so Unity culled the outside and the ramps rendered **inside out**. Terrain
  features have neither failure mode: the same mesh cannot be inverted relative to itself,
  cannot z-fight against itself, and cannot look placed.

  Overlapping features take the **MAX, never the sum**. Summing two that overlap builds a spike
  at the intersection, which is a launch ramp nobody designed.

- **Boulders stay real geometry, but their centre sits BELOW the surface.** A terrain dome is
  smooth and reads as another dune; the faceted silhouette is the whole point of a rock. What
  stops it looking dropped on is the outcrop swell raised under it plus sinking the centre by
  0.28 of its radius, so the ground line cuts across it. **Normals are recalculated, never
  trusted** — that is precisely what the ramp got wrong, and a rock rendered inside out looks
  like a hole in the world.

- Everything is **static**. Knockable barriers would suit a crash game but compete with car
  debris for the 40-rigidbody budget.
- **Surface noise stops at ~4 m wavelength.** A 2 m cell cannot represent finer without
  aliasing, so rock detail belongs in the texture and in scattered boulders, not in the grid.
- **`bake_space_transform=True` on export**, which `split_car.py` never did. Every chunk arrives
  in Unity with an identity transform instead of the `(-90, 0, 0)` that made the car's wheels
  unusable.

Gotchas paid for already:

- **Blender's default camera far clip is 100 m.** On a 1,800 m course the overview renders as an
  empty frame and the road shot gets a black band that reads convincingly as night sky.
- **Look ACROSS the course, not down it.** Down the corridor the walls are edge-on and all you
  see is their top edge receding, which looks like rolling hills whatever the profile is.
- **Do not put the preview camera on the centreline at eye height** — it ends up inside whichever
  obstacle is there and renders one grey slab.
- **Render every distinct STRUCTURE, not just the typical view.** A broken end cap shipped twice
  because the overview, road and wall cameras all look at the middle of the course and none of
  them looks anywhere near either bay. There is now a `_start` camera. Add one for anything new
  that is not a chunk.

**When a strip of geometry needs a bottom edge, give it its own vertex per column.** The end cap
reused the two *outer* skirt corners for every quad across the cross-section, so each 2 m segment
of the profile was stretched down to the same pair of points 68 m apart — a fan of overlapping
non-planar slabs that read as a giant wall beside the start bay. The comment claimed it
interpolated between the corners; it did not, and the comment is why it survived review.

**Only cap an end that has no bay.** A bay's disc already covers that opening and brings its own
skirt, and capping as well builds a wall straight across the mouth the player must drive through.

**Where two generated meshes meet, matching HEIGHTS is not enough — the VERTICES have to
coincide.** The bay blends to the corridor's own cross-section at its mouth, evaluating the
chunk's height function at the same station so the shared edge agrees exactly. That alone still
leaves cracks unless the two edges are sampled at the same coordinates.

The bay's radius is therefore **snapped to a whole number of `--cell`**. Unsnapped it half-worked,
which is the worst kind of bug: `42 / 2` gives a radial step of exactly 2 so the start bay lined
up perfectly, while `55 / 2` rounds to 28 rings and a step of **1.964**, putting every vertex along
the finish bowl's mouth between two chunk columns — a T-junction and a hairline crack at each one.
The report prints the snapped radius alongside the requested one so the difference is visible.

### Garage, dev mode and fullscreen — built 2026-08-30

**The loop closes here.** Damage earns gears, gears buy cars, the car you bought is the one that
spawns. The E30 is the free starter; the De Tomaso P72 costs 50,000.

- **`CarRoster` is a ScriptableObject asset, not a list on a component.** Two scenes need the
  roster — the menu shows it, the run spawns from it — and two separate lists would drift
  silently: you buy one car and a different one appears on the grid.
- **`PlayerCarSpawner` replaced the scene-placed car.** A car placed in the scene is one car
  forever, which is precisely the thing a garage is not. It spawns in `Awake` so the car exists
  before anything's `Start` runs, since `RunScore.Start` reads `PlayerCar.Current`.
- **`ChaseCamera` and `PerfReadout` acquire the car LAZILY**, not in `Awake`. The car is spawned
  now, so at Awake there may be nothing to follow, and a camera that gave up once would follow
  nothing for the whole run. `CLAUDE.md` predicted both would break the moment a car was spawned
  rather than placed; both would have.
- **Ownership is stored by id, never by index**, so reordering the roster cannot hand the player
  a different car. Starter cars are not written to prefs at all — owned by definition, and
  storing them would mean one could be lost by clearing prefs.
- The garage has **one action button that changes meaning** rather than a BUY and a GO with one
  always dead, and it says how many gears short you are instead of doing nothing, because a dead
  button reads as a bug.

**Two `PlayerCarSpawner`s spawn two cars, and `[DisallowMultipleComponent]` does not stop it.**
That attribute only prevents two copies on ONE object; the natural mistake is one on the manager
and another on the spawn point, which is exactly what happened. Two `PlayerCar`s then fight over
`PlayerCar.Current` and the camera follows whichever won. There is now a static guard that stands
the second one down and names both objects in the error.

#### Dev mode

**Tuner values are SAVED, per car, and cleared by RESET PROGRESS** (2026-09-02). `CarTuning`
owns the table, because two things need it: the tuner that edits the values and
`PlayerCarSpawner`, which puts them back on a freshly spawned car. Two copies of that list would
drift and the failure would be silent — tune a spring rate, restart, quietly get the prefab's.

Saved **per car id**, because the truck's spring rate is 2.5x the E30's by design and one shared
set of numbers would be wrong for every car but the last one tuned. PlayerPrefs cannot be
enumerated, so an INDEX of tuned ids is kept alongside — the same approach PlayerWallet uses for
owned cars, for the same reason. A field with nothing saved is skipped individually, so an
untuned car keeps its prefab values field by field rather than all or nothing.

Applied in the SPAWNER, not `CarController.Awake`: the car should carry no notion of saved state,
and the spawner is the one thing that knows which roster entry it is putting down.

The car tuner takes TYPED VALUES, not just +/- nudges (2026-09-02). Nudging a spring rate from
9,000 to the truck's 22,500 at 500 a click is 27 clicks, and the numbers this project actually
needs — a 2.5x spring scaling, an exact downforce — are worked out on paper and then entered. The
+/- buttons stay for feeling a value out by ear, which is the other half of what the screen is for.

Three things that had to be right:

- **Committed on `onEndEdit`, never `onValueChanged`.** Per-keystroke would apply 9, then 90,
  then 900 while "9000" is still being typed — and at `timeScale 0` a spring rate of 9 is
  invisible until you resume into a car sitting on its bump stops.
- **`RefreshTuner` skips a focused field and writes with `SetTextWithoutNotify`.** Writing `.text`
  on a focused field fires `onEndEdit`, which calls Commit, which calls RefreshTuner — and
  refreshing an unfocused one mid-entry snaps the value back while it is being typed.
- **`UiKit.Typing()` now gates every global hotkey.** `R` restarts the run and `TAB` resumes, and
  the keypress reaches `Keyboard.current` whether or not a numeric field accepts the character —
  so typing a value and catching R would have thrown away the run being tuned. It asks the
  EventSystem what is selected, so it covers every text box in the game including future ones.

Parsed with `InvariantCulture`: a machine set to a comma decimal would read "0,85" as 85, turning
a grip value into a hundred-fold error.

Unlocked with a code on the menu's Options screen. Grants **1,000,000 gears rather than infinite**,
deliberately: the garage, the wallet and the purchase path all still run their real code, whereas
an "everything is free" flag would mean the buying path is never exercised until a player without
dev mode hits it.

It is **a convenience, not a security boundary** — the code ships in the build and the PlayerPrefs
key can be set from devtools. That is fine; there is no leaderboard and nothing to cheat anyone
out of. It exists because the target device is a Chromebook running a Web build with no Inspector
and no console.

Dev mode also gates the **pause-screen car tuner** (top speed, power, grip, downforce, spring,
damper, anti-roll, steer angle) and the **`PerfReadout`**, which otherwise has no business in the
corner of a shipped build. Tuner changes apply on resume and are **not persisted** — a restart
restores the prefab, which is what makes experimenting safe.

#### Fullscreen is GONE — removed 2026-09-02

Confirmed dead on the live site: the button rendered as
`Fullscreen blocked — use the Chromebook fullscreen key`, which is the disabled state
`tools/embed.html` falls back to when `document.fullscreenEnabled` is false.

**It was never going to work.** Google Sites nests the game TWO iframes deep and we control
neither outer frame. A frame that was not granted fullscreen refuses the request however it is
asked, so `Screen.fullScreen` from Unity and `requestFullscreen` from the page fail identically.
Detecting it and saying so was the right call at the time, but a permanently disabled control is
still a dead control, and this project's own rule — the garage action button changes MEANING
rather than greying out — says a dead button reads as a bug.

Removed from all three places it existed:

- `tools/embed.html` — the button, its CSS and the whole fullscreen block (193 → 145 lines)
- `MenuUI.cs` — the FULLSCREEN button on the Options page
- `PauseMenu.cs` — the FULLSCREEN button on the pause screen

`Menu/Fullscreen.cs` is KEPT but is now referenced by nothing. It costs nothing in the build
(managed stripping is High) and documents the decision. Delete it only if a native build ever
happens, where fullscreen would genuinely work.

**The Chromebook fullscreen key does the job and nothing can block it.** That is the answer to
give a player who asks.

#### ~~Fullscreen has two controls, and the reliable one is in the HTML~~ — SUPERSEDED, see above

`Screen.fullScreen` from the game obeys the browser Fullscreen API, which refuses anything not
triggered by a real user gesture — hence buttons only, never automatic.

**The one in `tools/embed.html` matters more.** The game sits two iframes deep in Google Sites and
we do not control the outer iframe, so if it was not granted fullscreen the request is refused
however the game asks. Only the page can check `document.fullscreenEnabled` and *say so*, which it
does — it disables the button and points at the Chromebook fullscreen key, which nothing can
block, instead of leaving a control that silently does nothing.

It requests on `#wrap` rather than the canvas so the loading bar and error pane stay inside the
fullscreen view, and refocuses the canvas afterwards, since clicking the button steals focus and
keyboard input dies without it.

### Traffic AI — built 2026-08-30

Three cars racing the player to the bottom. Real physics, real `CarDamage`, tinted paint.

**Steering is one rule: go where the ground drops most.** A fan of seven probes casts DOWNWARD
ahead of the car and it steers at whichever finds the biggest drop. That single test covers the
whole map — the valley floor descends so following the drop follows the course, a wall goes up so
it is avoided for free, and kickers, humps and boulders also go up so obstacle avoidance falls out
of the same test rather than a second system that has to agree with the first.

Probes cast **down, not forward**, because a forward ray only reports *that* something is in the
way while a downward one reports *how high it is*, which is the number the rule is built on.

**⚠ KNOWN LIMITATION, flagged by Ethan 2026-08-30 and deliberately kept for now: descent-seeking
will not generalise.** It works because Quarry01 is a valley whose floor is the fastest way down.
It will fail on a course with a flat section, a climb, a fork where the shallower branch is the
correct line, or any map where "downhill" and "forward" come apart. When that happens the
replacement is a **coarse spline or waypoint chain for direction, keeping the probe fan for local
avoidance** — the fan is the half that is genuinely reusable. Do not spend effort making
descent-seeking cleverer; replace the direction source and keep the avoidance.

Other decisions:

- **`CarController` drives through `ICarDriver`, not `CarInput`.** The AI uses exactly the same
  physics as the player. Traffic that moves by its own rules always ends up feeling like it is on
  rails next to a car that does not, and every handling fix would have to be made twice.
- **`TrafficDriver.Awake` disables any `CarInput` it finds.** A traffic car built from the
  player's prefab would otherwise read the same keyboard and mirror the player's steering.
- **The traffic prefab must NOT have `PlayerCar`.** It claims `PlayerCar.Current` in `OnEnable`,
  so a traffic car would become "the player's car" on spawn. Stripping it at runtime is worse —
  destroying it fires `OnDisable`, which clears `Current` and unregisters the real player too.
  `TrafficSpawner` checks and refuses with an explicit error.
- **These are NOT the "kinematic until struck" traffic** in *Architecture calls*. That scheme is
  sized for ~20 background cars; these three race over the same kickers as the player and need
  real physics. Both schemes should coexist once the roster grows.
- **Damaging OTHER cars now pays, and traffic wrecking itself still pays nothing.** That
  distinction is the whole reason `scoreTrafficDamage` sat switched off: the design says the
  score is damage to YOUR car, and paying for a traffic car hitting a wall rewards the player for
  doing nothing.

  `CarDamage.Damaged` now carries the SOURCE and a `byPlayer` flag, which is what finally makes it
  answerable — a listener could previously tell neither whose car was hurt nor who did it.
  `RunScore.OnDamaged` pays for damage to your own car always, and for damage to anyone else's
  only when you caused it. `gearsPerPvpDamage` (0.06) is triple the rate for your own, because
  wrecking traffic is a deliberate act where taking damage yourself is mostly just what happens.
  The popup reads WRECKER and is its own colour, so the two are told apart on screen.

- **`carVsCarCrumple` (3x) amplifies car-on-car impacts only — DEFORMATION only.** Environment
  damage is untouched. Two vehicles meeting is the moment this game is about, and at the shared
  rate it read as no more eventful than brushing a rock. It applies to BOTH cars, since each runs
  its own collision callback for the same impact — so a big hit is mutually destructive rather
  than one-sided.

  **It was `carVsCarDamage` at 3x until 2026-09-02, and that multiplied the wrong thing** — the
  damage NUMBER, which drives the score and panel health as well as the dent. The ask was for
  hits that LOOK better, so the multiplier moved to the dent and `carVsCarDamage` went to 1. See
  the bug section above.

#### Making them fast — lookahead is TIME, not distance

Tuned and **confirmed in play 2026-08-30**: they book it down the hill.

The first version was slow, and throttle was never the reason — `cruiseThrottle` was already 1.0.
**A fixed lookahead is a fixed distance but a shrinking amount of time.** 26 m at 30 m/s is
**0.87 s of warning**, which is not enough to turn a 1200 kg car, so driving slowly was the only
safe behaviour available to it. The AI was not being timid; it could not see far enough to be fast.

`lookAheadPerSpeed` (0.9 m per m/s) makes the reach grow with speed, so the car always has roughly
the same *time* to react — about 1.6 s at pace — and can carry full throttle into a corner it can
already see. **This is the fix to reach for whenever an AI seems too cautious**; raising throttle
or lowering caution without it just moves the crash earlier.

`straightBias` scales with the live reach for the same reason: a longer probe finds bigger drops,
so a fixed per-degree penalty gets swamped and the car weaves exactly when it can least afford to.

Supporting values: `cornerLift` 0.28, `steerRate` 6.5, decisions 14 Hz.

**`speedBoost` (1.15) multiplies the INSTANCE's `topSpeed` and `enginePower`**, so traffic is
quicker than the player without touching the shared vehicle tuning. Power is scaled with top speed
or the car merely takes longer to reach a higher ceiling and is no faster where it matters.
**Do not push far past 1.2** — traffic that outruns the player vanishes down the hill in the first
ten seconds and leaves nothing to crash into, which for a crash game is worse than slightly slow.

#### Keeping control, and turning round — 2026-08-31

Two faults reported after a day of play, both with a single shared cause worth naming: **the AI
knew where it wanted to go and nothing about what the car was actually doing.**

**It oversteered round obstacles and spun.** Two contributions:

- **The steering signal was a staircase.** Seven probes over 110° are 18° apart, so the demand
  could only ever be one of seven values. Rounding an obstacle meant an 18° step change, and on a
  chassis with less rear grip than front that is how it spins. The best direction is now
  **interpolated between probes** by fitting a parabola through the winner and its two
  neighbours — a continuous angle for the same seven casts.
- **It could not tell it was sliding.** It now measures **sideslip**, the angle between where the
  car points and where it is actually going, and past `slipLimit` steers INTO the slide and lifts
  off. Without that it kept demanding the turn that started the slide, which tightens it.
  Steering slew is also divided by up to `steerRateAtSpeed` at speed, because a rate that feels
  responsive at 10 m/s is a flick of the wrist at 40.

**It drove backwards down the hill and never corrected.** The chain is worth remembering because
every step of it was individually working as designed:

> The forward fan sees only 110°. A car spun round by a crash finds every direction ahead rising,
> picks the least-bad, and crawls. The stuck timer fires and reverses it — which finally goes
> downhill. It is now moving fast, so it is never "stuck" again, and it settles into reversing
> down the mountain indefinitely.

Fixed with a **12-ray full-circle scan at 3 Hz** that answers "which way is downhill" regardless
of facing, so the car can **know** it is facing the wrong way instead of inferring it from
failure. Past `wrongWayAngle` for `wrongWayTime` it commits to a turnaround — full lock toward
downhill at part throttle, which a 26 m corridor has ample room for.

**That scan also fixed arrival**, which was being decided from the forward fan: *"no descent ahead
of me"* is also true of a car facing a wall, whereas *"no descent in any direction"* is what being
at the bottom actually means.

#### Dodging obstacles took three attempts, and the first two were the wrong TOOL

Worth reading as a sequence, because each fix looked reasonable and none of the first two worked.

1. **"They drive into rocks."** The probes sampled ONE point, at the far end of the ray. At speed
   that ray is ~49 m, so the car read the ground 49 m away and was blind to a boulder at 10 m.
   Fixed by sampling along the ray as well. **Changed nothing.**
2. **"Does the AI account for the hill going down?"** No — and that was why. `rise = sample −
   groundUnderCar` only works on the flat. On a 15% descent the ground 20 m ahead is already 3 m
   lower, so a 1.5 m boulder read as 1.5 m *below* the car and scored as nothing; it would have
   needed to be over 4 m tall to trip a 1.1 m threshold. **The descent hid every obstacle on it.**
   Fixed by measuring against the slope to the far sample. **Still drove into the same rock.**
3. **"The hazard readout only rose after the impact, while it was in the air."** That is the tell
   that it was not seeing the rock *at all*, rather than seeing it and reacting weakly. Point
   samples have **gaps**: at reach 49 m they landed at 4, 12, 23 and 35 m — an 8 m gap in the
   near field — and a boulder is up to 7 m wide. Rocks fell cleanly between samples.

**The fix is a swept sphere, which cannot have gaps**, aimed ALONG the hillside toward the far
sample rather than horizontally. It is also *cheaper* than the thing that did not work: one cast
per direction instead of four.

**The lesson, and it generalises: point sampling answers "how high is the ground there", and that
is the wrong question for "is something in my way".** Reach for a sweep the moment the question is
about obstruction. Two rounds were spent tuning numbers on a test that could not have worked at
any setting.

`hazardHeight` (1.4) and `hazardRadius` (0.7) define the sphere: it spans roughly the body of the
car, above the course's 0.55 m surface noise and low enough to catch a boulder that only just
protrudes.

**Diagnosing it again:** watch `hazardReadout` *as a car approaches* an obstacle. Rising only on
contact means the sweep is not hitting it — and the first thing to check then is whether the
`CourseRock*` objects actually have their Mesh Colliders, because a rock with no collider is
invisible to a sweep and a raycast alike and looks exactly like this bug.

Cost: the fan (one downward ray plus one sphere sweep each) at 14 Hz plus the full-circle scan at
3 Hz is **~4 casts per physics step per car**, against the 4 the car itself already does. **The AI
is still the cheap half** — the rigidbodies and their suspension are the real expense.

#### A truck in the traffic mix needs its own probe size — 2026-08-31

`TrafficSpawner.carPrefabs` picks at random per car, so adding `TrafficTruck` to the array is the
whole job. Two values on `TrafficDriver` are **sized for a car and must be changed per prefab**,
because they describe the vehicle's own body rather than the course:

- **`hazardRadius` 0.7 → 1.1.** This is the radius of the swept sphere that answers "is something
  in my way", and it should be roughly the half-width of the vehicle. A truck 2.18 m wide probing
  with a car's 1.4 m sphere drives its outer 40 cm into rocks the sweep never saw — the same
  class of bug as the point-sampling gaps, and it will look identical: the hazard readout rises
  only on contact.
- **`hazardHeight` 1.4 → 1.6**, so the sweep sits in the middle of a taller body.

**Do not raise `speedBoost` to stop the truck being left behind.** Its `topSpeed` is 24 against
the E30's 32 and that is the point — a slow heavy thing to catch and hit is worth more to a crash
game than another car that vanishes down the hill. `speedBoost` multiplies the instance's own
`topSpeed`, so the truck stays slower than the cars whatever it is set to, and pushing it just
makes the truck worse at the corners it is already bad at.

#### Obstacle avoidance is a per-MAP setting — 2026-09-02

`TrafficSpawner.obstacleAvoidance` scales `TrafficDriver.hazardWeight` on every car it spawns.
Set on the SPAWNER rather than the prefab because the same traffic prefabs are used on every
course, and the right answer differs per map:

- **Quarry: 1.** Real boulders on an otherwise clear floor — exactly the case the swept-sphere
  hazard test was built for, and it took three attempts to get right.
- **Everest: 0.** The whole face is jagged rock, so the sweep sees an obstacle everywhere and the
  cars pick their way down a mountain that is meant to be bombed straight off. The hazard test is
  not wrong; the question it answers is meaningless when the entire surface is the hazard.

#### `CarPaint` — tint per SUBMESH, never per renderer

`renderer.SetPropertyBlock(block)` applies to **every submesh that renderer draws**, and the E30's
body mesh carries `[Body, Glass]` in one renderer. Matching on the Body material therefore found
the right renderer and tinted the windows to match it, which is what made the cars read as moulded
toys. Use the **per-material-index overload**, and record which index matched.

A MaterialPropertyBlock rather than `renderer.material`, which would instantiate a copy of the
material per car per panel and turn one shared body material into thirty. MPBs do break SRP
batching; with four cars that is not worth caring about, and it would be if traffic reached twenty.

**The palette is deliberately muted.** These colours multiply a near-white body texture, so a
saturated primary comes out as flat poster colour. Real car paint is darker and greyer than people
expect. Detached panels keep their tint, because the block lives on the renderer and unparenting
does not disturb it — a red car sheds red doors.

### Scoring — built 2026-08-30

**Gears are the score and the currency at once.** `RunScore` converts damage into gears live;
`PlayerWallet` banks the run into PlayerPrefs when the scene unloads. The reference game shows
totals around **200 gears at the end of a long run**, which is what `gearsPerDamage = 0.02`
is sized against: a measured wall hit is ~700 damage, so one big hit is worth ~14 gears at x1.

Two payouts, deliberately different in feel:

- **Damage** — every qualifying impact, scaled by the combo multiplier. The steady drip that
  keeps the counter moving.
- **Parts** — a lump sum on detachment, `startingHealth * gearsPerPartHealth`. **Derived from
  health rather than a per-part Inspector field**, so there is nothing to wire per part and it
  is already proportional to how hard the part was to remove: a 160-health bumper pays 40, a
  60-health mirror pays 15. Add an override field only if a part ever needs to break that rule.

Decisions that are load-bearing:

- **`CarDamage.Damaged` carries a `sustained` flag, and the combo ignores sustained hits.**
  `OnCollisionStay` fires every `sustainedInterval` (0.08 s), so a grind down a wall raises
  ~12 damage events a second. Feeding those to the combo takes it from x1 to the x5 cap in
  under half a second of scraping and makes the entire mechanic free. Sustained damage still
  **scores** — it just cannot build a combo. `CarGlass` also subscribes to this event and
  ignores the flag.
- **`comboRearmInterval` (0.2 s) exists because the car has THREE box colliders.** One wall can
  raise three `OnCollisionEnter` events in the same frame, which would triple-count the combo.
  Note the damage itself is still counted three times — that is pre-existing behaviour and
  arguably correct, since three colliders touching is a bigger hit.
- **The popup shows the multiplier in force when the hit landed**, not the one after the combo
  climbs, or it prints a number that was never added to the score.
- **`RunScore.Bank()` runs from `OnDestroy`, not from `RunRestart`.** Every exit path — restart,
  quit, returning to a menu — unloads the scene, so banking there catches all of them. Guarded
  so it cannot double-pay.
- **Nothing searches for the player car. The car announces itself.** `PlayerCar` is a marker
  component that registers with `RunScore` in `OnEnable` and withdraws in `OnDisable`, and
  publishes `PlayerCar.Current`. Decided 2026-08-30 after `RunScore.playerCar` was left empty in
  the scene and scoring silently did nothing all session.

  Both obvious alternatives are wrong. A **scene reference** cannot point at a car the garage
  spawns mid-session, which is the whole point of having a garage. A **blind
  `FindFirstObjectByType<CarDamage>()`** returns an arbitrary car the moment traffic exists, so
  scoring would attach to a random NPC and look like a tuning problem. Matching on the `Player`
  tag or on "whichever car has a `CarInput`" is implicit matching — the pattern that has already
  cost this project three bugs (`trim` contains `rim`, `steering_centre` classified as a wheel,
  mirrors stealing door hits). A component that means exactly one thing cannot be matched by
  accident.

  **Ordering is handled from both directions**, because component init order across GameObjects
  is undefined: `PlayerCar.OnEnable` registers if `RunScore.Instance` already exists, and
  `RunScore.Start` reads `PlayerCar.Current` if it does not. `Register()` is idempotent, so both
  firing is harmless. `OnDisable` only clears `Current` if it is still the car on duty, or a swap
  that enables the new car before disabling the old would clear the incoming one.

  **Traffic registers the same way.** `Register()` / `Unregister()` are public for the spawner.
  Do not add traffic cars to an Inspector list.

  `PlayerCar.Current` is also the right hook for the garage, and for `ChaseCamera.target` and
  `PerfReadout.car`, both of which are still hard scene references and will break the same way
  the moment a car is spawned rather than placed.

#### Saving — it already worked, the problem was that failure is invisible (2026-09-01) — CONFIRMED IN PLAY

**The game has always persisted.** `PlayerWallet` uses PlayerPrefs, which on a Web build lands in
**IndexedDB**, and `PlayerPrefs.Save()` is called on every change. Close the tab, reopen it, the
gears are there. There was never a missing save system.

**The actual risk is that it can fail in total silence.** The game runs in a sandboxed
third-party iframe two levels down inside Google Sites. If Chrome's storage partitioning, an
incognito window or a school policy denies storage to that frame, every PlayerPrefs write still
succeeds *in memory*, `Save()` throws nothing, and the entire balance vanishes when the tab
closes. The player is told nothing and blames the game.

**A same-session self-test CANNOT detect this, and that is the part worth remembering.** Writing
a value and reading it straight back always succeeds, because PlayerPrefs answers from its
in-memory copy whether or not the flush reached IndexedDB. Any check shaped like "set it, get it,
compare" proves only that memory works. `SaveHealth` therefore counts **launches**: a counter
that comes back from a previous session is the only thing that can prove persistence, so the
answer is honestly "unknown" on a first visit and evidence-backed from the second on.

**`SaveCode` is the fix, not the detector.** Progress encodes to a short text string the player
copies and can paste back. It is immune to storage being blocked because it never touches
storage, and it is the only way to carry a balance from the school Chromebook to another machine.

**Google account sign-in was considered and does not fit**, for three independent reasons, any
one of which is fatal: OAuth needs a redirect flow a sandboxed cross-origin iframe cannot do
cleanly; it needs a server to hold saves, and avoiding a server is the whole reason this game
gets through school IT at all; and managed school accounts routinely refuse third-party OAuth.
A text code needs none of it and there is nothing for a filter to block.

**The save code is not encrypted, deliberately.** Anyone can already grant themselves gears from
devtools — `PlayerWallet` says as much. The checksum catches a TRUNCATED or mistyped code, not a
dishonest one, and it exists because silently loading garbage would overwrite real progress. A
bad paste must change nothing, which is why `TryImport` validates fully before touching the
wallet.

**THE BUG THIS ALMOST SHIPPED WITH, caught by a round-trip test and invisible to inspection:**
the owned-car list is itself pipe-delimited (`p72|lct3000`) and `|` was also the code's field
delimiter, so `Split('|').Length != 5` **rejected the save code of every player owning two or
more cars** — precisely the saves worth keeping. It reads as correct. The fix is to take the
fixed fields off the front, the checksum off the back, and treat everything between as the owned
list. **Any format that embeds one delimited list inside another needs a round-trip test with a
multi-item list, not a single-item one.** The standalone test covers empty, single, multi-item
and `int.MaxValue`, plus truncation, junk and all 43 single-character corruptions.

#### Patch notes and reset progress — 2026-08-31

**Patch notes are drawn on the main screen, not behind a button.** Nobody clicks a "what's new"
link, and the point is that a returning player sees what changed without going looking. They sit
between the banked-gears line and the tagline.

**`MenuUI.PatchNotes` is a `const`, and that is deliberate.** A public string would be serialized
into `MainMenu.unity` the moment the component was added, and from then on editing it in code
would change nothing on screen — the same trap that left `CarInteriorProps` poking through the
bonnet with the bug "fixed". Release notes are edited once per release by the person making the
release, so an Inspector field buys nothing and costs a silent failure. **Edit `PatchTitle` and
`PatchNotes` at the top of `MenuUI.cs` each release.** There is room for about eight lines before
it collides with the tagline, and nothing measures that for you.

**RESET PROGRESS arms on the first press and fires on the second.** It is one of the few
genuinely irreversible things in the game. A two-press button beats a confirm dialog, which
would be a second canvas, a modal state and another place for input focus to get lost in a
double-nested Google Sites iframe — for a control used about once a year. It disarms whenever
the Options page is left, so a stray press cannot sit armed waiting for an unrelated click.

**Reset turns dev mode OFF as well as clearing the wallet.** Two reasons, and the second is the
practical one: leaving it on puts the game in a state no real player can be in, and the main
reason to want a reset button at all is checking what a new player actually sees.
`DevMode.TryUnlock` grants gears **only at unlock**, so a wipe that left dev mode on would give a
dev-mode player zero gears and no route back short of re-entering the code anyway.

**Pages now repaint on arrival, not only at build time.** All four pages are built once in
`Awake`, so the main screen's gears line was a build-time snapshot. That was invisible only
because returning from a run reloads the whole menu scene — but a reset changes the wallet,
ownership and dev mode while every page already exists. `Show()` refreshes Main and Cars.

#### The garage carousel — built 2026-09-01, CONFIRMED IN PLAY

Replaced the list of car buttons with the reference game's layout: **one car at a time on a
rotating podium, cycled with `<` `>`**, over an animated backdrop. `Menu/CarPodium.cs` +
`Art/Menu/GarageBackdrop.shader`.

- **The car on the podium is the REAL roster prefab, neutralised.** Using anything else — a
  separate display model, a screenshot — reintroduces at the display layer exactly the drift the
  roster ScriptableObject exists to prevent: the garage showing one car and the grid spawning
  another. But a roster prefab is a live vehicle, so it has to be stripped.

- **⚠ IT MUST BE INSTANTIATED INTO AN INACTIVE PARENT, AND THIS IS NOT TIDINESS.** The first
  version spawned it live and then destroyed its components, which ran
  **`CarDeformation.OnDestroy` — and that destroys the mesh instances the component cloned in its
  own Awake.** Every MeshFilter it had cloned was left pointing at a destroyed mesh, so the body
  and all eight panels rendered nothing. **The wheels are the one thing excluded from the
  deformation panel list, so the tyres were the only part of the car that appeared** — a car
  reduced to four floating wheels, with no error anywhere.

  The fix is a permanently inactive `PodiumLimbo` child: `Instantiate` into it, so no `Awake` or
  `OnEnable` ever runs, strip, then reparent to the live mount. That avoids the whole class of
  problem rather than unpicking one instance of it — no cloned meshes to lose, no
  `PlayerCar.Current` claimed, no physics on a prop.

  Stripping uses **`DestroyImmediate`, not `Destroy`**: `Destroy` is deferred to end of frame and
  the car is reparented into an active hierarchy on the very next line, which would wake every
  component merely queued for destruction. This is the case that API is actually for — an object
  created this instant, before it is ever activated.

  **The general lesson: a component that owns a runtime-created asset will free it in OnDestroy,
  so destroying that component is not a neutral act.** Check what `OnDestroy` does before
  stripping anything.
- **Rendered in the world, not into a RenderTexture.** A RenderTexture is a second camera pass
  every frame for something already on screen. The UI is Screen Space Overlay, so the layering is
  free: backdrop quad, podium, car, buttons on top.
- **THE OPAQUE PAGE BACKDROP HAD TO BE SWITCHED OFF.** `UiKit.Backdrop` paints a full-screen
  image on the canvas, so with it on the podium renders perfectly and invisibly behind a black
  rectangle. `Show()` disables it on the garage page only. Worth remembering as a shape: the
  symptom of this class of bug is that everything logs as working and the screen shows nothing.
- **`Mouse.current`, NEVER `Input.mousePosition`.** This project uses the Input System package,
  so the legacy `Input` class is switched off and reading it throws. Same reason
  `UiKit.EnsureEventSystem` has to create an `InputSystemUIInputModule`. This is the second time
  the legacy input class has been the trap in this project.
- **The podium builds its own lights.** A menu scene is a camera and a canvas — there is no light
  in it — so a car dropped in renders black. Two directional lights, shadows off.
- **The car is seated from its RENDERER bounds, not its transform.** A roster prefab's origin is
  wherever the model was authored and is not reliably at the tyre contact patch, so seating it by
  transform sinks some cars into the podium and floats others.
- **The model is only respawned when the car actually changes.** `RefreshCars` runs on every visit
  and after every purchase; rebuilding an 11,000-triangle prefab because a price label changed
  would be a visible hitch for nothing.
- **Arrows wrap.** A disabled arrow at the end of the roster reads as a bug, the same reason the
  action button changes meaning rather than greying out.
- **All of it runs on unscaled time**, because the menu is reachable from a run paused at
  `timeScale 0` and a podium that stops turning there looks broken.

**⚠ `Shader.Find` FAILED IN THE BUILD AND WORKED PERFECTLY IN THE EDITOR — 2026-09-02.** Shipped
in Update 2: the garage, and every other menu page, showed the SKYBOX instead of the backdrop.

**A shader only reaches a build if something at BUILD TIME depends on it** — a material in a
scene or in `Resources/`, or an entry in Always Included Shaders. `CarPodium` builds its material
at RUNTIME (`new Material(Shader.Find("CarCrash/Garage Backdrop"))`), so nothing referenced the
shader when the build was made, Unity stripped it, and `Shader.Find` returned null. In the Editor
every shader in the project is loaded, so `Shader.Find` always succeeds there and the bug cannot
be reproduced by playing the scene.

The component already logged `"Garage Backdrop shader not found, so the background will be
whatever the camera clears to"` — which is exactly what was on screen, and which nobody sees
because **there is no console on a Chromebook**. That is the same reason `tools/embed.html` has
an on-screen error pane.

Fixed twice over, deliberately:

- **`backdropShader` is now assigned in `MainMenu.unity`** (it was `{fileID: 0}`). A serialized
  reference is a real build dependency, so the shader cannot be stripped. This is the fix that
  matters.
- **Added to Project Settings → Graphics → Always Included Shaders**, so `Shader.Find` also works
  from any future scene that has no wired reference.

**The general rule: anything created with `new Material(Shader.Find(...))` at runtime needs one
of those two, or it works in the Editor and is invisible in the build.** `CarPodium` also does
`Shader.Find("Universal Render Pipeline/Lit")` for the podium — that one survives only because
URP materials in the scenes already pull Lit in, which is luck rather than design.

The backdrop shader is fragment maths on one quad. **No textures, no particles, no post FX,
nothing that touches the download**, which matters with the data file at 14.83 MB against a 20 MB
cap. Three calls made after seeing it in play:

- **The surface is BLACK, and the lattice only exists where light falls on it.** `_Ambient` and
  `_Pulse` are zero on purpose: a permanently visible pattern reads as busy wallpaper, whereas
  lines that appear under the pointer and under a passing sweep read as a surface being lit.
- **THREE line families at 60°, not two at 90°**, slanted ~24° so they run down-left. A square
  grid reads as graph paper. Three axes give a triangular lattice whose lines trace out hexagons
  without any cell being a hexagon. Combined with `max` rather than `sum` where families cross —
  summing doubles the brightness and studs every intersection with a bright dot.
- **uv.x is scaled by the quad's real aspect**, fed from C#, or the cells stretch into lozenges on
  a backdrop three times wider than it is tall. `fwidth` keeps the lines a constant width on
  screen so a large quad does not alias into moire.

**TWO SEPARATE THINGS MADE THE POINTER GLOW AN OVAL, and both are the same underlying mistake:
treating the quad's uv as if it were screen space.** Worth writing down because any future
full-screen effect on a world quad will hit both.

1. **The distance was not aspect-corrected.** The quad is ~2.4:1, so `length(uv - pointer)`
   measures over twice as far vertically as horizontally per unit of screen, drawing a wide
   ellipse. Multiplying x by the aspect puts both axes in quad-heights; because the quad is
   parallel to the near plane, a circle there is a circle on screen.
2. **The quad deliberately OVERFILLS the frustum**, so quad uv is not screen uv at all. At a 16:9
   view the quad is 110 x 46 m against a 71 x 40 m frustum, so the glow was travelling only
   **64% as far as the cursor horizontally** and never reached the sides. `viewScale` maps screen
   uv into quad uv before the pointer is handed over.

`_PointerGlow` is therefore in QUAD HEIGHTS, not uv — 0.10 is about 11% of screen height, which is
a far smaller number than it looks. The idle lattice is a dark grey (0.26); the gold only ever
arrives with the pointer or a sweep.

The podium is plain black with no rim. A lit rim was tried and simply competed with the backdrop.

**The menu camera is PITCHED DOWN onto the podium** (`pitch`, 18 deg), for a showroom
three-quarter that shows the roof and the shape of the car rather than a flat side elevation.
Two things make this work:

- **CarPodium owns the menu camera's rotation**, and that is safe because nothing else in the
  front end uses it — every menu canvas is Screen Space Overlay, which does not go through a
  camera at all. Yaw is left alone.
- **The camera is rotated BEFORE anything is placed.** The backdrop quad and the rig are both
  positioned from `cam.forward` and `cam.up`, so pitching afterwards would leave the backdrop
  hanging at an angle off the side of the frame and the podium out of shot.

A horizontal disc seen from above is heavily foreshortened — at 18 deg the 6.2 m podium is only
1.9 m tall on screen — so it takes far less vertical room than its diameter suggests, which is
what leaves space for the name and buttons underneath. `drop` accepts NEGATIVE values to raise
the rig, which is what is wanted once the camera looks down: the podium otherwise sits on the
view axis, dead centre.

**INTERMITTENT EMPTY PODIUM: `Renderer.bounds` is not safe to read the frame you build a car.**
Reported 2026-09-01 — switching with the arrows sometimes left the plinth bare. The car was
spawning correctly every time; it was being thrown out of frame by the code that seats it.

`Renderer.bounds` is world-space and Unity refreshes it on its own schedule. Reading it in the
same frame the car is reparented — the frame in which `CarDeformation.Awake` also swaps every
panel's mesh for a clone — can hand back stale bounds sitting at the world ORIGIN. `min.y` then
comes out near zero, so `lift = mount.y - min.y` becomes the podium's full height and the car is
launched several metres up. Intermittent, because it depends on what Unity had got round to
updating.

`MeasureCar` transforms the MESH's own local bounds instead, which is immediate, deterministic,
and works on an inactive hierarchy — so the seating no longer depends on Unity's update order or
on whether the showcase happens to be switched on yet. **Reach for mesh bounds over renderer
bounds any time the object was created or reparented this frame.**

Two supporting fixes found while chasing it: `MenuUI.Show` now calls `SetShowcase` **before**
`RefreshCars`, so a car is never assembled inside a mount that is still switched off from the
last visit; and a roster entry with no prefab now logs a warning instead of silently emptying the
podium, which looks identical to the spawn failing. `lastLift` and `lastShown` are on the
component for next time — a lift of several metres is the signature of this bug.

**A uGUI button raises onClick on pointer UP, and that broke the first two attempts at
suppressing the arrow shockwave.** The ring starts on pointer DOWN, so by the time `Sweep` runs
it is already several frames old — every guard that compared FRAME numbers missed it, and the
ring kept appearing. Cancelling the most recent ring if it started within a 0.6 s press window
catches a real click however slowly it is made. Worth remembering generally: **do not pair a
pointer-down effect with a pointer-up callback using frame equality.**

**Hover goes GOLD, and the LABEL has to move with it.** A `ColorBlock` tints the button's target
graphic only, so near-white Ink text over a gold hover is a contrast ratio of **1.40 : 1** —
legible in a screenshot, unreadable in motion. Dark text on the same gold is **12.65 : 1**. uGUI
has no way to drive anything but the target graphic from a ColorBlock, so `UiKit.LabelTint` is a
tiny per-button component that swaps the label on pointer enter and exit. Added once and updated
in place, since the garage repaints its buttons on every purchase.

**The arrow shockwave now starts on RELEASE, not press.** Cancelling it when the button finally
reported its click was correct but showed a ring that started and then vanished, because a ring
started on press is visible for as long as the button is held. Release is the same moment uGUI
raises onClick, so whichever runs first the ring is either never started or is cancelled before
anything is drawn.

**`ColorBlock.selectedColor` must match `normalColor`, not the highlight.** Unity leaves a clicked
button SELECTED in the EventSystem, and a selected button keeps drawing `selectedColor` whether
or not the pointer is still over it — so every button ever clicked stayed lit after the mouse
moved away, clearing only when something else was clicked. Matching normal keeps the highlight
belonging to hover alone.

**Clicking anywhere sends a shockwave out from the cursor.** An expanding ring of accent light,
two slots used alternately so a quick second click starts another rather than teleporting the
first back to the cursor — which is what one slot does, and it reads as a glitch rather than a
second click. Radius grows linearly while brightness falls off squared: the ring keeps its speed
and thins out, which is what a shockwave looks like, where a linear fade visibly reaches zero and
reads as being switched off. `rippleReach` is 1.6 QUAD HEIGHTS so it carries off every edge even
from a corner click. Aspect-corrected like the pointer glow, for the same reason — without it a
shockwave is an ellipse.

The per-frame uniforms go through cached `Shader.PropertyToID` handles rather than the string
overloads, which hash the name on every call.

**The backdrop belongs to the WHOLE front end, not to the garage.** `MenuUI.Show` calls
`podium.SetShowcase()` to hide the plinth and the car off the garage page, and never disables the
`CarPodium` component itself — doing that took the backdrop, the lights and the pointer tracking
down with it and left every other page as a flat black rectangle. `UiKit.Backdrop`'s opaque image
is switched off entirely whenever a podium exists, since the world backdrop is drawn behind it.

#### Airborne camera — hold the yaw, pull back. CONFIRMED IN PLAY 2026-09-01

Reported 2026-09-01, once rolls became possible: the camera "goes kinda crazy" mid-spin. Two
causes, and the first is the interesting one.

**`UpdateYaw` falls back to the car's FACING below `velocityYawThreshold`.** That fallback is
right for a parked car and catastrophic in the air: at the apex of a jump the flat velocity drops
below the threshold, so the camera starts tracking the facing of a car that is barrel-rolling
through a full circle every second — and the whole world spins. **While airborne it now holds the
yaw instead**, which is what makes a roll read as the CAR rotating rather than the camera.

The rest is framing: `airDistance` +5 m, `airHeight` +2.2 m and `airFov` +7°, eased in on an
`airBlend`. Pulling back is what turns a spin into something readable — the car stays whole in
frame instead of filling it and thrashing. `airYawFactor` 0.12 keeps a trace of tracking so a
genuine change of direction is still followed, gently.

**`airBlendSharpness` is deliberately LOW (1.8).** The blend must not be able to complete in the
time the wheels are off over a crest, or ordinary rough ground lurches the camera in and out.
And it keys off `Touching`, not `Grounded`, so a car sliding on its roof gets the grounded framing
back rather than staying zoomed out while it grinds along.

Air rotation was calmed at the same time: `maxAirSpin` 420 → **190 °/s** (about half a rotation a
second — enough to roll, calm enough to watch) and `airAngularDamping` 0.04 → **0.30**. At 0.04 a
spin was preserved so faithfully that a knock on a ramp lip had the car tumbling for the whole
flight; 0.3 keeps the momentum and lets it bleed, which reads as driven rather than thrown.

#### A code-built menu list must be laid out from a BAND, not a fixed step

Found 2026-08-31, when the truck made three cars: the garage's blurb, OWNED line and CC-BY credit
all drew straight through the third car button. Both list pages were written as
`y = top - i * step` with everything below them at hand-picked coordinates, which is correct
only for the number of entries that existed when it was written. **Map select has the identical
bug waiting at four maps** and was fixed at the same time rather than left to happen.

`UiKit.Band` gives the rows a fixed vertical band and divides it. The slot is **capped** at the
comfortable size, so nothing moves until the list would otherwise overflow and the two-map page
looks exactly as it did; past that point the rows and their labels shrink together. The label
size has to shrink with the button — a 30pt label in a 34pt button spills out of it, which looks
exactly like the clipping the compression was there to prevent, so `UiKit.Button` takes a
`fontSize`.

It is **not** a scroll view. Past roughly eight rows the text is too small to read and a real
`ScrollRect` is the answer — viewport, mask and its own raycaster. Not worth building for a
roster this size, but that is the point at which it becomes worth it.

The general rule, because this project builds all its UI in code: **anything driven by an array
whose length can change needs a layout that reads that length.** A hand-placed y is a promise
that the array will never grow.

**IT HAPPENED AGAIN, in the dev tuner — 2026-09-02, and that is the point of writing rules down.**
The rule above was written for the garage and map select, and the pause screen's `BuildTuner` was
not changed at the time because it was not broken *yet*. It was written with eight tunables at
`y = 240 - i * 56`, with the two footer lines hand-placed at `-240` and `-268`. Adding `Grip
force`, `Steer @ speed` and `Turn assist` took it to **twelve rows**, whose ninth and tenth land
at `-264` and `-320` — straight through both lines of footer text.

Same fix, same helper: `UiKit.Band(top: 265, bottom: -395, count: Tunables.Length, ...)`, rows at
`band.Centre(i)`, footers at `band.BottomOf(count)`. At twelve rows the slot comes out at 55
against the old fixed 56, so **nothing on screen moved except the text that was being overdrawn** —
which is what a correct layout fix should look like.

`UiKit.ListBand` already had `BottomOf(count)` for exactly this, and it went unused for a year.
**When a fixed-step list is found, convert the whole screen, including whatever sits below the
list.** Fixing only the rows leaves the footer as a hand-placed y, which is the half that actually
broke here.

### Bullseye — the dartboard map, built 2026-09-01 — CONFIRMED IN PLAY

Ethan's design, from a side-view sketch: bomb down a long ramp, launch off a kicker, fly, and land
on a giant archery target dished to catch you. `tools/blender/build_dartboard.py` +
`Track/DartboardScore.cs`. **20,380 triangles** for the whole map.

**Looked online first, as asked, and it had to be generated.** Every dartboard model published is
a ~45 cm wall prop: built to be seen from two metres with a texture doing the work, licensed
CC-BY at best (Sketchfab) or explicitly non-redistributable (TurboSquid, CGTrader), which is fatal
for a public repo. But the licence is the least of it — **the game needs the rings as NUMBERS.**
Scoring by where a car lands needs ring radii and segment angles, and a downloaded mesh only ever
gives triangles. Generation is not the fallback here, it is the only thing that answers the brief.

**The face is a standard 5-colour ARCHERY target, not a dartboard.** Ten equal concentric rings,
gold in the middle, scoring 10 down to 1, no radial divisions. It began as a real dartboard and
that was worse in both directions: a dartboard's single band covers most of the disc, so scoring
needed 20 numbered segments layered on top of the rings just to spread results out, and the face
is visually busy at 200 m across. Ten graded rings do the same job with a third of the machinery,
and a ring simply IS a score rather than a multiplier on one.

Thin boundary lines are not decoration either — rings 10/9 are both gold and 8/7 are both red, so
without a line each pair reads as one band. Real targets print black lines, and **white lines
inside the black rings**, because a black line on black is not a line.

**The layout is arithmetic, not eyeballing.** `flight_range()` is plain projectile motion, and the
report prints where a car lands for a range of exit speeds. The gap is then set from that table:

| Exit speed | Lands | Score |
| --- | --- | --- |
| 26 m/s | 98 m | 2 — white |
| 32 m/s | 129 m | 5 — blue |
| 36 m/s | 151 m | 8 — red |
| ~40 m/s | ~178 m | **10 — GOLD** |
| 44 m/s | 200 m | 8 — red |

**The ramp produces far more speed than `topSpeed` suggests, and that is the whole point.** 230 m
of drop over 430 m is a **28° average, 39° peak** — a black-run gradient. `topSpeed` caps ENGINE
power, not gravity, so the car keeps accelerating all the way down: `m·g·sin(28°) − coastDrag`
over 430 m is roughly **58 m/s** for the E30, against its nominal 32.

That makes **braking on the runout the actual skill of the map.** The board is sized so every
speed from ~26 m/s up lands somewhere on it, and the gold needs deliberate throttle control
rather than just holding it down. **Change `--kicker-angle`, `--gap` or `--drop` and re-read the
table before believing anything.**

Decisions worth keeping:

- **The kicker is a PARABOLA, `z = rise * u²`.** Slope zero where it meets the flat runout, so
  there is no crease to unsettle the suspension just before launch, and steepest exactly at the
  lip. The rise follows from the exit angle rather than being guessed.
- **The board is one mesh PER COLOUR, not a texture.** A ring edge has to be exact on a 196 m
  disc; a texture large enough to keep it crisp would be enormous and would still blur under a
  camera that ends up a few metres from it. Seven colours, seven draw calls, exact at any size.
- **THE SKIRTS ARE NOT DECORATION.** First render had the ramp and the board as floating ribbons:
  a heightfield has a top and no underside, and this map is seen from below and from the side for
  the whole flight. The board gets a skirt plus a **downward-wound bottom cap** — an unwound cap
  is invisible from underneath, which defeats the point of adding one. The ramp's skirt gets its
  own vertex per row, never a shared pair, because reusing two corners is what built the giant
  slab beside Quarry's start bay. **Third time this has come up; do not build generated terrain
  without it.**
- **`RunScore.Award()` was added for this**, so a rule that belongs to ONE map does not have to
  live inside `RunScore`. Damage, parts, airtime and feats apply to every run and belong there; a
  dartboard does not, and putting it there would make every map pay to check a board it has not
  got. The combo multiplier is deliberately not applied — a bull is worth what it is worth.
- **Landing is detected separately from airtime, deliberately.** Sharing `RunScore`'s airborne
  edge would let a bump on the run-up score a dartboard hit. `minLandingSpeed` and `rearmDelay`
  together make a car that lands, bounces and lands again count as ONE dart.
- **The ring table is duplicated across Python and C#** with no sane way to share it. The
  generator therefore PRINTS its table in metres, `DartboardScore.lastDistance` reports in metres,
  and `OnDrawGizmosSelected` draws the C# rings over the geometry. If a landing scores the wrong
  ring, that mismatch is the first thing to check.

### Falling boulders — built and CONFIRMED IN PLAY 2026-09-01

`Track/FallingBoulders.cs`. Boulders come down the valley sides and roll across the track.

- **Spawned relative to the PLAYER, not from hand-placed points.** A 1,800 m course would need
  dozens of markers, they would need re-placing for every map, and each is a thing to get wrong
  in a scene file. Spawning around the car works on any course and costs nothing while nobody
  is there.
- **BIG AND FEW BEATS SMALL AND MANY**, and it is the rare change that improves the look and the
  cost at the same time. `radiusRange` 2.4-5.2 m (up to **10.4 m across, 40% of the corridor**),
  `maxLive` **16**, `interval` 0.9 s. One boulder that blocks half the road is a bigger event than
  six that bounce past, and it is five fewer rigidbodies. **Reach for size before count.**

- **Dropped ahead, up the side, and AIMED.** `aheadRange` 65-175 m, `sideRange` 32-95 m,
  `launchSpeed` 20-40 m/s. Two earlier attempts were wrong in opposite directions and both were
  found by playing it: a full 360° ring put rocks where they could not matter and half of them
  behind the car; 130-330 m ahead was so far down the track that the fall was over before arrival.

- **THE SPEED IS SOLVED FOR, NOT PICKED — this is what stopped them sailing over the track.**
  Reported 2026-09-01: "they usually aim too high". A boulder launched horizontally at a fixed
  speed from up the mountainside is in the air for however long the FALL takes, and that was
  never in the arithmetic:

  | Spawn height | Fall time | Lands at a fixed 30 m/s | Speed needed to land at 60 m |
  | --- | --- | --- | --- |
  | 15 m | 1.75 s | 52 m | 34.3 m/s |
  | 30 m | 2.47 s | 74 m | 24.3 m/s |
  | 50 m | 3.19 s | **96 m** | 18.8 m/s |
  | 80 m | 4.04 s | **121 m** | 14.9 m/s |

  Against a 26 m corridor, a rock from 50 m up overshot by nearly four road widths. `AimedLaunch`
  now takes the fall time from the height difference and derives the horizontal speed as
  distance over that time, clamped into `launchSpeed` — so the band is what it is ALLOWED to
  land in rather than a value chosen independently of where it is aimed.

  Two smaller corrections went with it: the intercept uses **HORIZONTAL** distance, because the
  vertical part of the trip is gravity's job and is already in the fall time, and including it
  inflated the estimate and led the player too far. Free fall is still an approximation — a
  boulder bounces down a slope rather than dropping — but it is the right shape of answer.

- **They LEAD the player, and that is what made the hazard work.** Aiming at where the car IS
  always lands behind a moving target — at 25 m/s a four second flight arrives 100 m late, which
  is why the first version read as scenery however much of it there was.

  `AimedLaunch` is a **two-pass fixed-point intercept**: guess the flight time from the current
  distance, move the target along the car's velocity by that much, re-time against the new
  target. That is the whole algorithm. The closed-form quadratic would be false precision, because
  a boulder bouncing down a rock face does not travel in a straight line anyway.

  **`aimSpread` (16 m) is load-bearing, not polish.** At 0 every boulder is a homing missile and
  the hazard becomes a scripted death that arrives however well the player drives. The spread is
  what makes it something to read: most miss, some connect, and the near misses are the good ones.
  It is applied across and along the course, never vertically — a boulder that misses upward is
  just wrong. `lead` above 1 over-leads and lands in front, which is the one that makes you brake.

  The player's Rigidbody is **cached and refreshed when the car changes**. Holding one forever
  would leave the aim reading a destroyed car's velocity — zero — which silently disables the
  lead and looks exactly like the feature never working.

- **BOULDERS ON THE ROAD: the fix is a SECOND raycast, and the obvious test does not work.**
  Reported after the first play. The tempting check is "is the spawn point higher than the car" —
  and it is wrong on a descending course, because a point 300 m ahead is ~45 m BELOW the car
  whether it is on the road or high up the wall. That test rejects every forward spawn.

  `TrySpawn` therefore casts twice: once straight ahead of the car at the same distance along the
  course, to get the height of the TRACK at that station, and once at the candidate point. The
  spawn is accepted only if it stands `minRise` (10 m) above the track. Splitting the offset into
  along-course and across-course components is what makes this possible, and it removes the
  descent from the question entirely — leaving only "is this up the side".

  This needs no knowledge of the corridor's width, no layer or name matching, and works on any
  map. `lastResult` reports `"on the track (rise N m)"` when it rejects, so the threshold can be
  tuned by watching rather than guessing.
- **The rim is found by RAYCASTING, not assumed.** A cast down from high above a point off to the
  side reports whatever the wall is at that station, so this knows nothing about how the course
  was generated and cannot go stale when the generator changes. No ground found = skip, because a
  boulder spawned in mid-air just falls forever.
- **Meshes are GENERATED at Awake, not imported.** A subdivided icosahedron with hashed per-vertex
  displacement: 80 triangles, flat shaded, which is the faceted silhouette that makes a rock read
  as a rock rather than a dune — the same finding as the course boulders. It **adds nothing to the
  download**, which matters more than usual with the data file at 14.83 MB against a 20 MB cap.
- **Displacement is INWARD only** (`1 - amount * hash`). Pushing vertices outward can invert a
  face on a mesh this coarse. Same one-sided rule that stopped `CarDeformation.crumple` spiking
  panels, for the same reason.
- **The lump is hashed from the DIRECTION, quantised.** Hashing per face would move the three
  corners meeting at a vertex to three different places and tear the rock into 80 loose triangles
  — exactly the co-located-vertex problem `crumple` has, in a different guise.
- **Convex hulls are cooked ONCE at Awake for four variants, then pooled.** Cooking at runtime is
  a frame hitch, which is why detached panels get a BoxCollider instead; here it is paid at load
  and never again because instances are recycled.
- **Its own pool, deliberately NOT `DebrisPool`.** Sharing one cap would mean a big crash evicts
  every boulder, or a rockfall evicts the panels just knocked off the car. Two hazards with
  different lifetimes competing for one cap is a bug that would be misdiagnosed as either system
  misbehaving. The rigidbody BUDGET is still shared — see below.

**Rigidbody budget: 16 boulders, and it went UP to 40 and back down again.** Briefly set to
`maxLive` 40 at `interval` 0.35 s, then cut to **16 at 0.9 s** once the boulders got big — bigger
rocks do the same job with fewer bodies. Steady state is 16 boulders plus four cars and up to 24
pieces of debris: **up to 44 against a documented budget of 40**, which is a rounding error rather
than the 68 it briefly was.

Still worth re-measuring: the 60 FPS Jasper Lake result was taken **without any of this**, and the
standing rule is that the numbers get re-measured after anything that changes scene scale.

**`FallingBoulders.Live` is published to `PerfReadout` for exactly this reason.** The Inspector
does not exist on a Chromebook, and the live count is the one number that decides whether this is
affordable there. Turn on dev mode, read `FPS` / `worst` / `N boulders live` together, and note
that **`worst` is the number that will move first** — a physics spike shows up as one long frame
long before the average FPS drops.

Levers if it does drop, in the order worth trying:

1. **`maxLive`** — linear in physics cost and the biggest single lever.
2. **`collisionDetectionMode`** — `ContinuousDynamic` sweeps every step and is the most expensive
   per-boulder setting. `ContinuousSpeculative` is materially cheaper; the risk is a fast boulder
   tunnelling through the road, which is why it is not the default.
3. **Tick "Enable GPU Instancing" on the rock material.** 40 boulders sharing four meshes and one
   material collapse to four instanced draw calls instead of forty. Free, and worth doing whether
   or not the frame rate is a problem.
4. `lifetime` and `restDelay` — shorten to raise turnover rather than the live count.
- **Mass scales with radius SQUARED, not cubed, and is capped. The realistic number does not
  work and this is the reason.** True volume scaling at rock's 2,700 kg/m³ makes a 3.5 m radius
  boulder **485 tonnes** — a 404:1 mass ratio against the 1,200 kg car. **PhysX solves a contact
  badly past roughly 10:1**: the car jitters, gets squeezed through the ground, or is launched,
  and the boulder becomes literally immovable, which reads as a moving wall rather than a rock.
  Even a generously light 900 kg/m³ is still 32:1 at the SMALL end of the range.

  `massAtOneMetre * r²`, capped by `maxMass` 9,000, keeps a big boulder decisively heavier than a
  small one while staying solvable:

  | Radius | Across | Mass | Ratio vs car |
  | --- | --- | --- | --- |
  | 1.5 m | 3.0 m | 2,475 kg | 2.1:1 |
  | 2.5 m | 5.0 m | 6,875 kg | 5.7:1 |
  | 3.5 m | 7.0 m | 9,000 kg (capped) | 7.5:1 |

  **Do not "fix" this back to cubic.** It is a case where the physically correct answer and the
  working answer differ and the working one wins. The same 10:1 rule applies to anything heavy
  added later.
- **`behindDistance` no longer needs clamping**, now that every boulder spawns ahead. While the
  ring version was in, it did: a boulder dropped 170 m behind was outside a 140 m cull the
  instant it appeared, so it was recycled on its first frame every time — wasting roughly half of
  all spawns while the readout cheerfully reported them as dropped. Worth remembering as a shape
  of bug rather than a live one: **a spawn radius and a cull radius set in different Inspector
  sections will silently fight each other**, and the symptom is "it says it is working and
  nothing appears".
- **`inwardSpeed` is the range that makes it not repetitive.** Low end stops on the shoulder, high
  end carries right across the track. This is the "some go far in, some go shallow" requirement
  and it is one field.
- **`ContinuousDynamic` collision detection.** A 2 m rock at 20 m/s moves 40 cm per physics step;
  discrete detection tunnels it straight through the road and the car.
- **Layer Default**, so `CarDamage` treats it as damaging and the wheel SphereCasts see it — a
  settled boulder is a real obstacle you can drive over or hit.

Two of the three pre-flight concerns held up; the third did not survive contact:

1. **Rigidbody count** — handled by the pool and `maxLive`.
2. **Seen coming** — `aheadRange` (70-150 m) is the telegraph. No dust or sound yet.
3. **"The AI cannot see it"** — true, and it turns out not to matter. `TrafficDriver`'s hazard
   sweep runs against whatever colliders exist at the moment it casts, and a boulder is on
   Default like everything else, so traffic WILL sometimes swerve for one. It will also
   sometimes be flattened by one it never saw. Both are good.

### Air rotation — why rolls were impossible, fixed 2026-09-01

Reported: cars "kinda just don't rotate" in the air, hold whatever attitude they launched at, and
have to be turned by hand. **The cause was `m_AngularDamping: 2` on the Rigidbody**, applying in
the air exactly as it does on the ground:

| | damping 2 | damping 0.04 |
| --- | --- | --- |
| 360°/s spin after 0.25 s | 216°/s | 356°/s |
| after 0.50 s | **135°/s** | 353°/s |
| after 1.00 s | **51°/s** | 346°/s |

A roll needs ~360°/s sustained for a second. At damping 2 the car got through about a third of one
before the spin was eaten, so **any rotation carried off a ramp was gone before it could become
anything** — which is precisely what "you have to rotate them manually" describes.

**The damping is now SWITCHED, not constant**: `groundedAngularDamping` (2, unchanged — it is what
stops the body wobbling on its springs) and `airAngularDamping` (0.04). Two details:

- **It keys off `Touching`, not `Grounded`.** The question is "is this in free flight". A car
  sliding along on its roof has no wheel down but is very much in contact, and air damping there
  would leave it spinning freely against the scenery.
- **`maxAirSpin` caps what INPUT can build, never the spin itself.** A crash or a ramp lip can
  legitimately throw the car faster than the cap and that rotation is kept; air control just stops
  adding to an axis already over it. Clamping `rb.angularVelocity` instead would throw away
  exactly the momentum this change exists to preserve.

**`CarController.Touching` is new, and it is not the same question as `Grounded`.** `Grounded` is a
WHEEL test — correct for suspension, drive and grip, and wrong for "has the car landed". A car that
comes down on its roof, its side, or across a boulder has landed and has no wheel touching
anything, so airtime scored on `Grounded` ran through the entire crash and only paid out if the car
happened to settle on its tyres. `Touching` is `Grounded || bodywork in contact`, driven from
`OnCollisionStay` with a **0.15 s expiry rather than a Stay/Exit pair** — `OnCollisionExit` is easy
to miss when a collider is disabled, destroyed or teleported, and a stuck flag would end airtime
permanently.

`minAirTime` also went 0.45 → **0.8 s**: at 0.45 the counter still flickered on rough ground, and
0.8 is about the shortest thing that reads as a jump rather than a bump.

### Airtime scoring — built 2026-09-01, CONFIRMED IN PLAY

Time off the ground earns gears, and the counter climbs live while the car is in the air.

- **Paying only on LANDING is the whole design.** A jump that ends in a ravine, or with the car
  falling out of the world, pays nothing — otherwise the best way to farm gears is to drive off
  the map and wait. It also gives the counter somewhere to go: it climbs while the outcome is in
  doubt and banks at the moment the risk resolves.
- **`minAirTime` (0.8 s) is what separates a jump from a bump.** The wheels leave the ground over
  every crest and kerb on these courses, so without a floor the counter flickers the entire way
  down the hill. 0.45 was not enough on rough ground — and **0.45 was still what all four scenes
  actually held until 2026-09-02**, because raising the code default never touched them. Fixed.
- **It ends on `Touching`, not `Grounded`** — see the air-rotation section. Landing upside down is
  landing.
- **`landingGrace` (0.2 s) is what makes it ONE timer until you land.** Ending the jump on the
  first frame of contact meant a single graze mid-flight banked the jump and restarted the
  counter from zero while the car was still in the air — reported on Everest, which is a jagged
  70-degree face and grazes constantly. Contact must PERSIST to count as a landing: a graze is
  contact for a frame or two, a landing is contact that stays. Too high and a real landing that
  bounces reads as one long jump.
- **`maxAirTime` (20 s) exists because "not grounded" is also true** of a car wedged on a rock,
  resting on its roof, or falling out of the world. Without a cap those pay unboundedly. It caps
  the PAYOUT, not the timer — at `gearsPerAirSecond` 26 the old 9 s froze the counter at 234
  gears, which is the number that was seen sticking. Quarry is at 200 by hand; the rest are 20.
- **The live counter is on the DYNAMIC canvas.** It changes every frame while airborne, and a
  uGUI canvas rebuilds its whole batch when anything on it changes — putting it beside the gear
  counter would rebuild the counter every frame of every jump.
- **It is fixed to the screen, unlike the score popups, which are pinned in world space.** A
  jumping car crosses most of the screen and rotates while it does it, so a label stuck to it is
  the one thing you cannot read at the moment you want to.
- **The size EASES to its target rather than snapping**, so crossing `airGoldAt` reads as the
  number swelling rather than as a different label appearing.
- **`SetText("AIRTIME\n+{0}", n)`**, the composing overload, never concatenation — this runs every
  frame of every jump and string concatenation would allocate on all of them.
- The combo multiplier **does** apply, so a jump taken mid-rampage is worth more. Consistent with
  damage, and it makes chaining a crash into a jump pay.

#### The HUD is built in code, and that is deliberate

`ScoreHud` creates its canvases, text and bar in `Awake`. A hand-built Canvas is a dozen
GameObjects whose anchors, pivots and font sizes can all be silently wrong in a scene file and
cannot be reviewed in a diff. Building it in code makes the Editor wiring a single component
add. The cost is that art-directing it means editing numbers in the file.

- **TWO canvases, not one.** A uGUI canvas rebuilds its whole batch when any element changes.
  The counter updates a few times a second; popups move every frame. On one canvas the popups
  would rebuild the counter every frame. Static canvas: label + counter. Dynamic canvas:
  multiplier, combo bar, popups.
- **No `GraphicRaycaster`** on either — nothing is clickable, and a raycaster costs a hit test
  per pointer event for nothing. Add one only when the garage needs buttons.
- **Popups are a fixed pooled array** (`popupCount`, 8), reused round-robin. A pile-up recycles
  the oldest rather than instantiating a ninth.
- **The counter only pushes a string when its INTEGER changes**, via `SetText("{0}", n)`, which
  is the allocation-free overload. Assigning `n.ToString()` would allocate on most frames of a
  crash.
- **Popups carry no outline.** A TMP outline is a material property, so enabling one
  instantiates a material per popup and turns one draw call into eight. The cost is that a
  popup over a pale road is harder to read; revisit with a single shared outlined material if
  it turns out to matter.
- **Popups rise in WORLD space**, so they stay pinned to the spot on the road where the hit
  happened rather than sliding with the camera. `screen.z <= 0` is checked because
  `WorldToScreenPoint` mirrors points behind the camera — without it, a hit you have driven
  past reappears on the wrong side of the screen.

**TMP essentials cost 2.2 MB of build size, and it is not optional-by-default.** The import
drops `LiberationSans SDF.asset` into `Assets/TextMesh Pro/Resources/`, and **everything under a
`Resources/` folder is force-included in a build whether anything references it or not** — so the
full Latin atlas ships even though this game draws digits, `+`, `x` and a handful of part names.
Against the 10.4 MB baseline and the 20 MB budget that is affordable but not free.

The fix, when size matters: generate a font asset containing only the glyphs actually used
(Window → TextMeshPro → Font Asset Creator, custom character set) and delete the stock one. That
is a ~100 KB atlas instead of 2.2 MB. Not worth doing until the menu and garage have settled what
text exists. The HDRP shadergraphs in `Assets/TextMesh Pro/Shaders/` do **not** ship — wrong
folder, and nothing references them. **Re-measure at the next build regardless.**

**`ScoreHud` needs TMP Essential Resources imported.** TMP's *code* ships inside
`com.unity.ugui` 2.0.0 so everything compiles, but the default font asset lives in
`Assets/Resources` and is created by **Window → TextMeshPro → Import TMP Essential
Resources**, run once. Without it the HUD draws nothing at all — `Awake` `Debug.LogError`s
rather than failing silently. Costs roughly 0.3–0.6 MB in the build; **re-measure against the
size baseline at the next build.**

IMGUI was considered and rejected for the HUD. `PerfReadout` and `RestartOverlay` use it and
should keep doing so — a debug readout and a one-off loading screen do not care. A permanent
HUD does: IMGUI allocates every frame and costs a few percent CPU, and the next three roadmap
items (map, menu, garage) are all text-heavy enough that building them on IMGUI would mean
rewriting the lot.

**Scoring will need re-tuning once traffic exists**, because traffic will be the biggest source
of points in the finished game. Do not over-tune these numbers before there are cars to hit.

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

### Fourth car — Lamborghini Aventador, added 2026-09-01. The fastest in the game.

`Assets/Art/Vehicles/Aventador/aventador-split.fbx`, **CC-BY, Arion Digital — attribution
required in-game**, see CREDITS.md. 10,374 tris in, **11,131 after splitting** (the shell gains an
InteriorShell). Authored in **centimetres**, so `--scale 0.01` on conversion.

**⚠ THE DOWNLOAD IS AN ASCII FBX, AND BLENDER CANNOT OPEN ONE.** `import_scene.fbx` refuses with
"ASCII FBX files are not supported" and always has. **Unity imports them happily**, which is the
trap: the model looks completely fine in the Editor while the entire Blender pipeline —
inspection, splitting, previews, the Unity-setup numbers — is unavailable.

`tools/blender/fbx_ascii_to_binary.py` was written for it. The alternatives were all worse: no FBX
converter exists on this machine (Autodesk's was discontinued years ago), and re-exporting through
a second Unity in batch mode fights the project lock whenever the Editor is open. ASCII FBX is a
plain regular text format and the parts that matter are four arrays per mesh, so parsing it
directly is the cheap answer. It reads meshes, names, local transforms, UVs and **material names**
— the last of those matters because `split_car.py` decides what is glass from the material name
and `CarGlass` empties submeshes by it. **Run it first whenever Blender refuses a file.**

The split went unusually well because the model is well built:

| | |
| --- | --- |
| Wheelbase / track | **2.75 m / 1.70 m** (real car: 2.70 / 1.72) |
| Front / rear wheels | 0.69 m / 0.73 m across — **which is how the nose direction was found** |
| Objects | Body, Glass, four per-corner wheels, and a Collider proxy |
| Layout | `--nose +z --up y`, giving the P72's proven arrangement |

Two decisions worth keeping:

- **The 122-triangle `Collider` proxy is DROPPED.** This project builds its own three collision
  boxes, and a mesh collider on a car is the wrong shape regardless.
- **Glass is its own MATERIAL**, so `CarGlass` works on this car — unlike the P72, where the
  windows are painted into the body texture and the component has nothing to empty.

**This is where the face-size guard finally earned its place.** It shipped as precautionary and
sat unused across three cars; the Aventador's front bumper region rejected 2 oversized faces on
the first run. `split_car.py`'s comment is updated to say so.

**Angles: approach 24.9°, departure 29.7°, breakover 24.6°** — a low front, as a supercar should
have. It will ground its nose on things the E30 clears.

### Third car — LCT 3000 '95 box truck, added 2026-08-31 — **WIRED AND DRIVING**

`Assets/Art/Vehicles/LCT3000/lct3000-split.fbx`, **CC-BY, Daniel Zhabotinsky — attribution
required in-game**, see CREDITS.md. 18,947 tris as downloaded, **11,640 after splitting**.

**Confirmed in play 2026-08-31: it drives.** That also confirms the whole `--nose` reorientation
path — the truck reached Unity upright, facing forward, with `wheelVisualEuler (0, 0, 0)` and
Scale Factor 1.0, exactly as the P72-layout argument predicted. `--nose` is therefore the
recommended flag for every future model, and the wheel-axis table below is now proven rather
than reasoned.
Authored in metres at real-world scale (6.02 × 2.61 × 2.84 m), so `--scale` is not needed and
Unity **Scale Factor stays 1.0**. It also arrives already grounded.

**It is the first pre-split model in the project, and that is the interesting part.** 27 mesh
objects with the artist's own panel cuts: separate bumpers, rear box doors with inner panels, a
full cargo-bay liner, a cab interior, suspension, and all four wheels already named per corner.
`--keep` exists because of it — see the Blender pipeline section.

Two consequences that break this project's usual pattern:

- **No `InteriorShell`.** `Bottom` (5,772 tris) is a real cargo-bay liner — inner walls, roof,
  floor, chassis rails, mudguards, tail-lift frame and the cab's inner shell — and `Interior`
  (3,253 tris) is a real cab. Between them they do the shell's job with real geometry, so the
  truck is split `--no-shell`. Verified by rendering those two objects alone.
- **The mirrors live inside `Bottom`**, not the body, which is why the truck measures 2.61 m
  wide against a 2.18 m body.

Twelve detachable parts, more than either car: `PartBumperF/R`, `PartBoxDoorL/R` (the box's
rear doors — the signature truck damage, and they open onto a real cargo bay), `PartDoorL/R`
(cab), `PartMirrorL/R`, and four wheels. Five materials after dropping the badges and plates.

Build command:

```bash
"$BL" --background --python tools/blender/split_car.py -- \
  --input Assets/Art/Vehicles/LCT3000/Source/lct3000.fbx \
  --output Assets/Art/Vehicles/LCT3000/lct3000-split.fbx \
  --tris 12000 --profile truck --nose +x --up z --keep-interior --no-shell \
  --drop "Body_Badges,Numberplate_front,Numberplate_rear" \
  --keep "Bumper_front=PartBumperF,Bumper_rear=PartBumperR,\
Reardoor_Left=PartBoxDoorL:outer,Reardoor_Left_Inner=PartBoxDoorL:outer,\
Reardoor_Right=PartBoxDoorR:outer,Reardoor_Right_Inner=PartBoxDoorR:outer"
```

**A truck is not a heavy car, and the numbers have to scale together.** Static load per corner
is `mass x 9.81 / 4`, and `springStrength` was sized so rest compression lands at ⅓ — so at
3,000 kg every load-bearing spring number scales by **2.5x** off the E30's, or the truck sits on
its bump stops from the first frame. `centreOfMassOffset` is the one that is *not* a scale:
Unity derives the centre from the three collider boxes at **y ≈ 1.571** on this body, which is
half the truck's height, and it has to be pulled down explicitly or the thing falls over
leaving the start bay.

**Known trade-off: approach 24.3°, departure 20.7°, breakover 20.3°**, against the E30's
30.4 / 27.2 / 21.8. The truck will belly out on quarry rocks the E30 clears. That is correct for
a box truck and is left as character, not fixed.

#### Deformation on a big vehicle — the defaults are tuned to the E30's SIZE and DENSITY

Reported 2026-09-01: the truck "barely gets dented". It is not a bug and nothing is broken —
`CarDeformation`'s defaults are sized for a 4.16 m car and every one of them is a length in world
metres, so they shrink in relative terms as the vehicle grows. Measured, not guessed:

| | E30 Body | Truck Body | Ratio |
| --- | --- | --- | --- |
| Vertex density | **90.6 /m²** | **30.7 /m²** | 0.34x |
| Average edge length | **0.104 m** | **0.219 m** | 2.1x |
| Surface area | 21.5 m² | 134.8 m² | 6.3x |
| Height | 1.21 m | 2.84 m | 2.3x |

Three separate things follow, and only the second is obvious:

1. **`maxDisplacement` is the binding constraint, and it is a fixed 1.0 m.** That is 83% of the
   E30's height but only 35% of the truck's, so the same number reads as a wrecked car and a
   lightly scuffed truck. Confirm before changing anything else: `Dent()` receives the **uncapped**
   damage — `maxDamagePerImpact` gates detachment only — so at 3,000 kg the truck already asks for
   `1777 x 0.0022 = 3.9 m` and is clamped to 1.0. **The request is 4x the cap, so
   `strengthPerDamage` does literally nothing here.** Raising `maxDisplacement` is the whole fix.
2. **`radius` is also absolute**, so a 0.55 m crater spans 26% of the E30 and 18% of the truck.
3. **`crumpleScale` 0.22 is a latent BUG on this model.** The rule is that crumple noise must be
   coarser than the vertex spacing or it degenerates to per-vertex jitter and spikes the mesh.
   The E30's spacing is 0.104, so 0.22 is a safe 2.1x. **The truck's spacing is 0.219 — 0.22 is
   1.0x, exactly the failure case.** It needs ~0.46 to keep the same margin.

**The truck cannot punch through its interior the way the E30 could**, because it has no separate
`InteriorShell` — its cargo liner and cab are welded into the same `Body` mesh and deform with the
paint. So `shellRadiusScale` / `shellDepthScale` do nothing on it, and the depth ceiling that
governs the E30 simply does not apply.

**What still limits it, and the trade being made:** at 30.7 verts/m² a dent has roughly a third
the geometry to fold, so it reads as a few broad facets rather than crumpled metal. The real fix
is subdividing the body at split time — and it was **deliberately not done**, because at the time
`carcrash.data.unityweb` was 14.83 MB against what was believed to be a hard 20 MB wall.
**That reason has expired** (2026-09-02): splitting the build files retires the per-file cap, so
the question is now only whether the triangles are worth the download TIME. Revisit once
splitting lands, and measure the download afterwards.

**General rule for the next vehicle: every `CarDeformation` length scales with the vehicle, and
`crumpleScale` scales with its MESH, not its size.** Take `maxDisplacement` and `radius` off the
height ratio against the E30, and `crumpleScale` off ~2x the measured average edge length.

**CONFIRMED IN PLAY 2026-09-01: the truck crumples.** Values that did it, on the truck's
`CarDeformation` only — the E30 and P72 are untouched:

| Field | E30 | Truck | Derived from |
| --- | --- | --- | --- |
| `maxDisplacement` | 1.0 | **1.8** | height ratio 2.84 / 1.21 |
| `radius` | 0.55 | **1.0** | same |
| `crumpleScale` | 0.22 | **0.46** | 2.1x the truck's 0.219 m average edge |
| `crush` | 0.75 | **0.85** | taste |

Part health went back UP at the same time (bumpers 160/140, doors 140, box doors 130, mirrors 60,
wheels 150) with `maxDamagePerImpact` back to 60, so the truck holds its panels and folds instead
of shedding them. That combination — high health, high displacement — is what a big vehicle
should look like, and it is the opposite of the first attempt.

#### Damage on a heavy vehicle — mass changes the impulse, not just the feel

**A 3,000 kg truck reports ~2.5x the collision impulse of the 1,200 kg E30 at the same speed**,
because impulse is a change in momentum. The E30's measured wall hit is ~16,500, so the truck's
is around **41,000**, which at `damagePerImpulse 0.045` is ~1,800 damage from one contact against
parts holding 60-160. Two consequences, and they pull in opposite directions:

- **`maxDamagePerImpact` is doing ALL the work**, even more than on the E30. Raising it is the
  direct lever for "more destructible"; `damagePerImpulse` and `minimumImpulse` barely register
  at these impulses.
- **`minimumImpulse` has to go UP, not down, on a heavy vehicle.** The `OnCollisionStay` gate is
  an impulse threshold, and a vehicle merely *resting* transmits `mass x g x fixedDeltaTime` per
  step: **235 for the E30, but 589 for the truck**. The E30's 900 leaves a 3.8x margin over
  resting; the same 900 on the truck leaves 1.5x, which is close enough that settling on its roof
  starts to register as sustained damage. 1500 restores a sane margin.

This is the general rule for any future vehicle: **scale `minimumImpulse` with mass to keep the
resting margin, then set destructibility with `maxDamagePerImpact` and part health.** Getting it
backwards — lowering `minimumImpulse` to make something more destructible — makes a heavy vehicle
take phantom damage from standing still.

**"More destructible" is two unrelated systems, and they are tuned in different components.**
Worth stating because it was got wrong once: *panels coming OFF* is `CarDamage` — part health and
`maxDamagePerImpact`. *Panels CRUMPLING* is `CarDeformation` — `maxDisplacement` and `radius`.
They share only the damage number. On the truck the answer was the second: parts back to high
health so they stay on, and deformation turned right up so the body folds instead. Ask which one
is meant before touching either, because turning up the wrong one produces a car that sheds its
panels while remaining perfectly straight.

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
| `fbx_ascii_to_binary.py` | Convert an ASCII FBX into one Blender can open. Run FIRST if import_scene.fbx refuses the file. |
| `inspect_model.py` | Print objects, verts, tris, dimensions, materials. Run before anything else. |
| `split_car.py` | Join → decimate → carve panels by region → set hinge origins → interior shell → export FBX. |
| `car_bounds.py` | Print every part's bounding box in the CAR's local space. Run before placing anything inside the bodywork. |
| `preview_split.py` | Render the split FBX with each panel colour-coded. **Always look at this**; the triangle report cannot tell you a region cut a door in half. |
| `build_course.py` | Generate a downhill crash course: descending corridor, terraced quarry walls, rollers, obstacles, stopping bowl. Renders three previews. |
| `build_dartboard.py` | Generate Bullseye: ramp, kicker, and a 180 m dished dartboard. Prints the flight table and the ring radii. Renders three previews. |

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

#### Pre-split models: `--keep`, `--drop`, `--nose`, and the `truck` profile (2026-08-31)

Added for the LCT 3000 box truck, which is the **first model in the project that arrives
already split by object**. Region carving is a fallback for welded shells, not the goal — an
artist's cut follows the real panel gap and no bounding box will ever match it.

- **`--keep "Source=PartName[:hinge]"`** preserves an existing object as a named part instead
  of welding it into `Body`. Several sources may map to one part and are joined, which is how
  the truck's `Reardoor_Left` + `Reardoor_Left_Inner` become one `PartBoxDoorL`. Matching is on
  the **end** of the object name, so a pack prefix like `LCT300095_` is ignored. New hinge
  `outer` is the mirror image of `inner`, for box-van rear doors that swing outward.
- **`--drop "Name,Name"`** deletes objects outright, before classification. The truck's badges
  and number plates are **186 triangles that dragged in two materials and a 1.6 MB texture**.
  Dropping them took it from 7 materials to 5. Decorative extras with their own material are
  almost always a bad trade on this budget.
- **`--nose {+x,-x,+y,-y,+z,-z}` with `--up`** reorients the model so the nose runs along
  Blender −Y and up is +Z, which is what `export_fbx`'s fixed axis conversion assumes. It also
  **switches the axis detection off entirely**, which is the more important half — see below.
- **`--keep-interior`** now keeps interior meshes as *bodywork* rather than skipping the drop
  test. It used to skip the branch, which routed `Steering_Wheel` straight into the **wheel**
  test and gave the car a fifth wheel. Exactly the failure the DROP-before-WHEEL rule exists to
  prevent, reintroduced by a flag.
- **`truck` region profile** carves only the cab doors and mirrors, because everything else a
  truck can shed already exists as an object.

**`axis_order`'s "wider than it is tall" rule is FALSE for a box truck, and it fails silently
and expensively.** The LCT 3000 is 2.84 m tall and 2.61 m wide, so height was detected as
width. Nothing errors. What happens instead: both left and right wheels get the same corner
name from `corner_name` and are **joined into one object**, so the car ends up with two wheels;
`wheelRadius` comes back as **1.099 m instead of 0.356**; grounding drops the model along its
own width; and every right-hand region hunts for its panel in the vertical, so `PartDoorR` and
`PartMirrorR` are reported as `SKIPPED — region matched no faces`. `--length-axis` does **not**
rescue this: it forces only the length and then re-sorts the remaining two by size, picking the
same wrong answer. `--nose` is the fix, because after it the layout is known and not guessed.

**Fit region fractions to the body's frame AT CARVE TIME, not to the raw model bounds.**
`--keep` pulls parts out *before* the body is joined, so the bounds the regions normalise
against are not the ones `inspect_model.py` prints. On the truck that was a 0.12 m shift, which
was enough to slice the cab door diagonally across its front edge.

**Measure geometry per-polygon, not from `bound_box`.** A probe script reading
`ob.matrix_world @ ob.bound_box` reported `PartMirrorL` spanning 2.25 m of the truck while the
identical region on the right side was correct. The geometry was fine both sides; the cached
`bound_box` was stale after import. That cost a round of chasing a bug that did not exist —
iterate `mesh.polygons` and the vertices themselves when it matters.

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

**`wheelVisualEuler` is decided by ONE measurable fact: which local axis the wheel mesh is thin
on. Measure it, do not reason about export conventions.** Settled 2026-08-31 by measuring all
three split cars, after a long and useless detour trying to derive it from Blender's axis
conversion — that argument is unresolvable from Blender alone and is not worth having again:

| Car | Wheel mesh local dims | Thin on | Body length on | `wheelVisualEuler` |
| --- | --- | --- | --- | --- |
| E30 | 0.600 × 0.230 × 0.600 | **Y** | X | `(0, 0, -90)` |
| P72 | 0.301 × 0.684 × 0.681 | **X** | Y | `(0, 0, 0)` |
| LCT 3000 | 0.251 × 0.713 × 0.713 | **X** | Y | `(0, 0, 0)` |

`UpdateVisual` spins about the visual's **local X**, so a mesh whose axle is already on X needs
no correction and one authored on Y needs the −90 that rotates Y onto X. The E30 is the odd one
out; the P72 is the proven zero-correction case, and `--nose` puts a new model onto exactly the
P72's layout. Read the wheel's local bbox with a per-polygon probe before wiring a new car and
the value is known in advance rather than discovered by watching wheels tumble end-over-end.
