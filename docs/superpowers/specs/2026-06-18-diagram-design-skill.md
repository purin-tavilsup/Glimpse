# Diagram-Design Skill — Design

> Status: **Design / spec** (brainstormed + approved 2026-06-18)
> Author: Pond + Claude
> Pairs with: the `glimpse` skill / render-loop harness (this repo). Glimpse lets the
> agent *see* a rendered diagram; this skill encodes *what good looks like* and *which
> diagram to draw*, then drives the render → judge → iterate loop through glimpse.

## 1. Problem & Motivation

Code agents can write diagram source, but without principles they produce diagrams that
are technically valid yet hard to understand: wrong diagram *type* for the message, mixed
abstraction levels, unlabeled arrows, box-walls, decorative-but-meaningless color. The
goal is a **skill that makes the agent produce diagrams that help people understand
systems quickly** — by routing to the right diagram type, applying a consistent house
style, and verifying the result visually via glimpse.

The reference diagrams the user likes (layered soft-fill architecture, icon-rich cloud
architecture) define the target aesthetic; "authentication flow" established that the
skill must cover **more than architecture** — flows, states, data models, processes.

## 2. Goals / Non-Goals

### Goals
- An **agent skill** (`diagram-design`) that triggers whenever the agent is asked to
  create or improve a diagram of a system, flow, or model.
- **Route by intent** to the right diagram *type* before drawing.
- A consistent **house style** matching the user's references (soft-fill labeled
  containers, rounded boxes, datastore cylinders, meaningful arrow colors, numbered
  step badges, one flow direction, a title).
- **Bundled templates** per type so output looks right by default.
- **Composes with `glimpse`** for the render → Read → judge → iterate loop.
- An **anti-patterns** catalog (general + per-type) the agent checks against.

### Non-Goals (YAGNI)
- **First-class support for class / Gantt / timeline / mindmap / git-graph diagrams.**
  Mermaid supports them; the skill notes they exist for stretch use but does not template
  or teach them — they are not "understand a system" tools.
- **Committing the user's reference images** (Miro/AWS marketing PNGs) — copyright. The
  skill reproduces the *look* via its own templates and ships agent-authored exemplars.
- **Building new rendering infrastructure** — rendering is delegated to `glimpse`
  (mermaid + d2 renderers already exist there).
- **A human-facing reference guide** — this is an agent skill (a `reference.md` exists for
  progressive disclosure, but the audience is the agent).

## 3. The Workflow (skill spine)

The skill makes the agent follow this loop:

1. **Audience & message** — decide who reads it and the *one* thing they must grasp. One
   diagram = one message. Ask the user if genuinely unclear.
2. **Route to a diagram type** by intent (see §4).
3. **Pick the renderer/notation** for that type, and (for architecture) the C4 level and
   the layered-vs-icon-cloud mode.
4. **Draft from the bundled template** for that type — fill, don't start blank.
5. **Render** via the `glimpse` skill → PNG.
6. **Judge** by Reading the PNG against the universal checklist + that type's
   anti-patterns. Glimpse's mechanical checks (blank/single-color) catch pipeline breaks.
7. **Iterate** (edit source, re-render).
8. **Stop** when the message lands at a glance and checks are clean. Cap at 5 iterations;
   if not converging, stop and report what's stuck.

## 4. Diagram-Type Router

| Intent / signal | Type | Renderer | Template |
|---|---|---|---|
| System structure: components, services, layers, "architecture" | **Architecture** (C4) | mermaid (layered) or D2 (icon-cloud) | `layered.mmd` / `cloud.d2` |
| Something happening over time between parties: auth flow, OAuth handshake, API request lifecycle, "flow between X and Y" | **Sequence** | mermaid `sequenceDiagram` | `sequence.mmd` |
| Lifecycle / status transitions: order states, connection states, workflow status | **State machine** | mermaid `stateDiagram-v2` | `state.mmd` |
| Data model: entities, tables, relationships | **ER** | mermaid `erDiagram` | `er.mmd` |
| Process / decision logic: branching steps, algorithms | **Flowchart** | mermaid `flowchart` | `flowchart.mmd` |

**Architecture sub-modes:**
- **Layered (mermaid):** soft-fill `subgraph` bands/zones, rounded component boxes,
  `[(cylinder)]` datastores, meaningful arrow colors (e.g. solid/black = call,
  red = return). Reproduces the user's #1 and #3.
- **Icon-cloud (D2):** nested containers, official cloud icons via
  `icon: https://icons.terrastruct.com/…`, title/footer. Reproduces the user's #2.
  Requires `brew install d2`; icons fetch over the network at render time.
- **Deployment view:** a use of icon-cloud mode showing *where things run* (nodes,
  networks, regions/zones).
- **Dynamic view:** numbered step badges (①②③…) overlaid on a container diagram to trace
  a flow through the static structure — the user's #2 numbered-badge technique. Use when
  "show how a request flows through the architecture." Works in either mermaid layered or
  D2 — it is a labeling technique, not a separate diagram.

> **Deployment and Dynamic are *modes/uses* of the layered (`layered.mmd`) or icon-cloud
> (`cloud.d2`) templates — they need no separate template files.** Deployment is also
> orthogonal to C4 levels L1–L3 (it maps containers to infrastructure); folding it into
> icon-cloud mode is a pragmatic choice in this skill, not canonical C4.

**Renderer selection rule:** cloud services / "AWS"/"GCP"/"Azure" / wants logos → D2
icon-cloud; logical layers / components / processes → mermaid.

## 5. House Style (derived from the reference diagrams)

Defaults baked into the templates so output matches the user's taste:

- **Labeled container grouping** as the backbone — named layers/zones (rounded rects with
  a clear title and soft fill).
- **Soft, light palette + dark text** — cream/beige bands; light lavender / green / blue
  component fills. Defined once as mermaid `classDef`s; never harsh.
- **Consistent shapes:** rounded rects = components/processes; `[(cylinder)]` =
  datastores; icons = cloud services.
- **One flow direction** per diagram (TB or LR), kept consistent.
- **Color carries meaning** — e.g. arrow color = call vs return; node fill = layer or
  type. A legend when color encodes anything non-obvious.
- **Label every box and every edge** — no bare arrows.
- **Numbered step badges** available for dynamic/sequence-over-structure.
- **Title** (and footer where useful) for framing.

**Per-renderer style caveats (don't apply a convention a renderer can't honor):**
- **Arrow color (call vs return)** works only in mermaid `flowchart`/architecture and D2.
  In mermaid `sequenceDiagram`, arrows cannot be individually colored — use **solid
  (`->>`) = call / dashed (`-->>`) = return** instead.
- **`erDiagram` ignores flow direction** (no `TB`/`LR`) — the "one flow direction" rule
  does not apply to ER; don't add a direction specifier (it breaks the render).
- **`stateDiagram-v2`** uses `[*]` for both the initial and final pseudo-state — never
  literal `start`/`end` node names.

## 6. Knowledge Carried

**C4 levels** (architecture only — pick one per diagram, never mix):
- **L1 Context** — system + users + external systems. Non-technical / big picture.
- **L2 Container** — apps, services, data stores, and how they communicate. The workhorse.
- **L3 Component** — inside one container, for devs on that part.
- *(L4 Code — skip unless explicitly asked.)*

**Universal principles:** one message; ≤ ~9 boxes per zone (split if more — for sequence
diagrams this limit is on *participants*, not messages); label every box and edge; legend
when color/shape encodes meaning; consistent shapes; one flow direction (except ER, which
auto-lays out); meaningful (not decorative) color; annotate the non-obvious; spell out
acronyms; give it a title.

**Anti-patterns catalog** (general): wrong diagram type for the message; mixed abstraction
levels; the box-wall (too many nodes); unlabeled arrows; no legend / inconsistent
notation; spaghetti edge crossings; kitchen-sink (more than one message); acronym soup;
decorative color with no meaning; no clear entry point or flow direction.

**Per-type anti-patterns** (examples; the full catalog is authored into `reference.md`
during implementation):
- *Sequence:* missing actor; messages without labels; no return arrows where they matter;
  implying time order that isn't there; over-nesting `alt`/`loop`/`opt` frames (use `alt`
  for conditions, `loop` for retries/polling — keep nesting ≤ 1 level deep).
- *State:* missing initial/final state (use `[*]`); unlabeled transitions; unreachable
  states.
- *ER:* unlabeled relationships; missing cardinality; attributes as separate boxes;
  many-to-many without a junction entity (surface the junction when it has its own
  attributes).
- *Flowchart:* decision nodes without yes/no labels; no single start; crossing flows.
- *Architecture:* mixing C4 levels; logical and deployment concerns in one diagram.

## 7. Structure & Files

```
.claude/skills/diagram-design/
  SKILL.md            # frontmatter (name + triggers), the §3 loop, §4 router,
                      # §5 house style, §6 principles + anti-patterns (concise),
                      # how to render via glimpse, renderer requirements
  templates/
    layered.mmd       # mermaid soft-fill layered architecture skeleton
    cloud.d2          # D2 icon-cloud architecture skeleton (Terrastruct icons + numbering)
    sequence.mmd      # mermaid sequenceDiagram skeleton (auth-flow shaped)
    state.mmd         # mermaid stateDiagram-v2 skeleton
    er.mmd            # mermaid erDiagram skeleton
    flowchart.mmd     # mermaid flowchart skeleton
  reference.md        # C4 detail, full per-type anti-pattern catalog, palette hex values
  examples/           # agent-authored exemplar PNGs (rendered via glimpse) — visual proof
```

- **SKILL.md** stays concise (progressive disclosure): the loop, the router table, the
  house-style summary, a short principles + anti-pattern list, and the glimpse hand-off.
  Deep detail lives in `reference.md`.
- **Templates** are copy-and-fill starting points carrying the house style (palette
  classDefs, container skeletons, edge-label/colour conventions).
- **Examples** are rendered by running the templates through glimpse — they double as the
  skill's own test that every template actually renders clean.

## 8. Integration with glimpse

The skill does not render; it calls the `glimpse` skill. Exact command forms:
- **mermaid** (sequence/state/ER/flowchart/layered):
  `dotnet run --project tools/Glimpse.Capture -- <file>.mmd --name <n>`
- **D2** (icon-cloud architecture):
  `dotnet run --project tools/Glimpse.Capture -- <file>.d2 --name <n>`
  (renderer inferred from the `.d2` extension; requires `brew install d2`)
- Rendering exemplars into the skill's `examples/` dir adds
  `--out .claude/skills/diagram-design/examples --no-manifest` (keeps that dir clean).
- Read the printed PNG path, judge against the §6 checklist, iterate.

## 9. Renderer Requirements

- **mermaid** (`mmdc`) — already installed; covers sequence/state/ER/flowchart/layered.
- **d2** — `brew install d2`; only needed for icon-cloud architecture. If d2 is absent,
  the skill informs the user, skips icon rendering, and uses `layered.mmd` instead.
- **D2 icons fail silently** — a wrong `icon:` URL renders nothing (no error), so the
  agent must verify icons appear when it Reads the PNG, and confirm icon URLs before
  templating.

## 10. Acceptance / "Done"

- `diagram-design` skill triggers on diagram requests and routes to the correct type.
- An authentication-flow request produces a **sequence diagram** (not an architecture
  box diagram) — the motivating cross-check.
- All template files (6: `layered.mmd`, `cloud.d2`, `sequence.mmd`, `state.mmd`,
  `er.mmd`, `flowchart.mmd` — covering the 5 diagram types) render clean through glimpse
  (no blank/single-color warning), each proven by a committed exemplar PNG in `examples/`.
- Output visibly matches the house style (soft-fill labeled containers, cylinders,
  meaningful colors, title).

## 11. Build Order (for implementation)

1. Skill scaffold: `SKILL.md` frontmatter + the §3 loop + §4 router (skeleton).
2. The 6 template files, each rendered + visually checked via glimpse against the §6
   checklist → exemplar PNG in `examples/`.
3. `reference.md` (C4 detail + full anti-pattern catalog + palette hex).
4. Fill SKILL.md body (house style, principles, anti-patterns, glimpse hand-off).
5. End-to-end self-test: run an "auth flow" and an "architecture" request through the
   skill and confirm correct type + clean render.
