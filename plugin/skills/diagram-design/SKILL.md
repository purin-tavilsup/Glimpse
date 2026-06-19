---
name: diagram-design
description: >-
  Create clear architecture and system diagrams that help people understand a system at a
  glance. Use this whenever the user wants to draw, design, sketch, or improve a diagram of
  a system, service, or flow — architecture/component diagrams, authentication or API
  request flows, state machines / lifecycles, data models (ER), or decision flowcharts —
  even if they don't say the word "diagram" (e.g. "show how login works", "map out our
  services", "visualize the order lifecycle"). It gives diagrams a clean, consistent house
  style — labeled soft-fill containers, proper datastore/actor shapes, meaningful colors —
  on a sensible diagram type, then renders + visually verifies the result via the glimpse
  skill. Prefer this over free-handing a diagram.
---

# Diagram Design

A valid diagram and a *clear* diagram are different things. This skill produces the clear
kind — the picture someone grasps in a few seconds.

**Where the leverage is (and isn't):** picking the diagram *type* usually isn't the hard
part — the right type is normally obvious from the ask, and a capable model gets it. What's
easy to get wrong, and what this skill is really for, is **(1) a consistent, readable
visual style** — labeled containers, a soft palette, the right shapes — so diagrams look
like they belong together and communicate at a glance; and **(2) trusting the source
instead of the render** — you can't judge a diagram from its text, so render it and read
the PNG.

## The loop

1. **Audience & message.** Decide who reads this and the *one* thing they must take away.
   One diagram = one message. If the intent is genuinely ambiguous, ask the user.
2. **Confirm the diagram type** — usually obvious from the ask; glance at the table below
   only if you're unsure or it's genuinely ambiguous.
3. **Pick notation/renderer** for that type. For architecture, also pick the C4 level and
   layered-vs-icon-cloud mode.
4. **Draft from the matching template** in `templates/` — copy and fill, don't start blank.
   This is where most of the value is: the templates carry the house style, so the output
   looks consistent and readable by default. Keep that style (don't drift to defaults).
5. **Render** it via the `glimpse` skill (see "Rendering" below) → PNG.
6. **Read the PNG and judge it** against the checklist below + that type's anti-patterns.
   Glimpse's `single-color`/blank warnings flag a broken render (bad syntax, missing
   icon) — fix the pipeline before judging design.
7. **Iterate** — edit source, re-render.
8. **Stop** when the message lands at a glance and the render is clean. Cap at 5
   iterations; if it isn't converging, stop and tell the user what's stuck.

## Route by intent (quick sanity check)

The right type is usually clear from the ask, so treat this as a quick check, not a
decision you have to agonize over — it mainly earns its keep on genuinely ambiguous
requests. The one mismatch actually worth watching for is below the table.

| The user wants to show… | Type | Renderer | Template |
|---|---|---|---|
| The **structure** of a system: components, services, layers | **Architecture (C4)** | mermaid (layered) or D2 (icon-cloud) | `layered.mmd` / `cloud.d2` |
| Something happening **over time between parties**: auth/login flow, OAuth handshake, API request lifecycle | **Sequence** | mermaid | `sequence.mmd` |
| A **lifecycle / status transitions**: order states, connection states, workflow status | **State machine** | mermaid | `state.mmd` |
| A **data model**: entities, tables, relationships | **ER** | mermaid | `er.mmd` |
| **Process / decision logic**: branching steps, an algorithm | **Flowchart** | mermaid | `flowchart.mmd` |

An **authentication flow is a sequence diagram, not architecture** — it's messages
exchanged over time between a user, app, and provider. Forcing it into a layered box
diagram is itself an anti-pattern.

### Architecture: which mode

- **Layered (`layered.mmd`, mermaid)** — soft-fill `subgraph` bands/zones, rounded boxes,
  cylinders for datastores, colored/labeled edges. The default for logical structure.
- **Icon-cloud (`cloud.d2`, D2)** — official cloud/vendor icons (GCP, GitHub, AWS, Azure)
  via `icon:` URLs, nested containers, a title. Use when the system is described in named
  cloud services and the user wants the logos.
- **Deployment** — a *use* of icon-cloud mode showing where things run (nodes, regions).
- **Dynamic** — numbered step badges (①②③…) over a structure diagram to trace a flow
  through it. A labeling technique on either template, not a separate file.

Pick the C4 **level** and don't mix levels in one diagram: **Context** (system + users +
external systems), **Container** (apps/services/datastores inside + how they talk — the
usual choice), **Component** (inside one container). Detail in `reference.md`.

## House style

These defaults make a diagram readable and are baked into the templates:

- **Group with labeled containers** — named layers/zones with a soft fill and a clear title.
- **Soft, light palette, dark text** — cream/lavender/green/blue fills (hex in
  `reference.md`); never harsh, full-saturation colors.
- **Consistent shapes** — rounded rects = components/processes, cylinders = datastores,
  icons = cloud services.
- **One flow direction** (top-down or left-right), kept consistent. *(Exception: `erDiagram`
  auto-lays out — don't set a direction.)*
- **Color carries meaning, never decoration** — and add a legend when it does. *(In
  `sequenceDiagram` you can't color arrows; use solid `->>` = call, dashed `-->>` = return.)*
- **Label every box and every edge.** A bare arrow makes the reader guess.
- **A title**, and a footer when it adds context.
- **Keep it small** — aim for ≤ ~9 nodes per zone; split or go up a C4 level if it bloats.
  (For sequence diagrams this applies to *participants*, not messages.)
- **Human actors** use the bundled person icon (`assets/user.png`), not a plain box/pill —
  see "Actor / user icon" in `reference.md` for the mermaid (data-URI) and D2 syntax.
  Sequence diagrams keep the standard `actor` stick figure.

## Anti-patterns to avoid

General: wrong type for the message; **mixed abstraction levels**; the box-wall (too many
nodes); **unlabeled arrows**; inconsistent notation / missing legend; spaghetti crossings;
kitchen-sink (more than one message in one diagram); acronym soup; decorative color; no
clear entry point or flow direction.

Per-type anti-patterns (and fuller guidance) are in `reference.md` — read it when drawing a
type you're less sure about. The high-frequency ones:
- **Sequence:** unlabeled messages; no return arrows where they matter; over-nested
  `alt`/`loop` frames. Use `alt` for conditions, `loop` for retries — nest ≤ 1 deep.
- **State:** use `[*]` for the initial/final pseudo-state (not literal `start`/`end`);
  label every transition.
- **ER:** label relationships; show cardinality; use a junction entity for many-to-many.
- **Flowchart:** label decision branches yes/no; one start node.

## Rendering (via the glimpse skill)

Don't judge a diagram from its source — render and look. The `glimpse` skill turns a
source file into a PNG you can Read:

```
# Fast wrapper (preferred — runs the built CLI directly, no rebuild each call):
./scripts/glimpse <file>.mmd --name <name>     # mermaid: sequence/state/ER/flowchart/layered
./scripts/glimpse <file>.d2  --name <name>     # D2 icon-cloud (renderer inferred from .d2)

# Equivalent, slower (rebuilds every call):
dotnet run --project tools/Glimpse.Capture -- <file>.mmd --name <name>
```

Read the printed `PNG:` path, judge it, iterate. When saving reference exemplars into this
skill's `examples/` dir, add `--out .claude/skills/diagram-design/examples --no-manifest`
to keep that dir clean.

## Requirements

- **mermaid** (`mmdc`) for everything except icon-cloud — usually already installed.
- **D2** (`brew install d2`) only for icon-cloud architecture. If D2 is missing, tell the
  user, then fall back to `layered.mmd` (no icons). **D2 icons fail silently** — a wrong
  `icon:` URL renders nothing with no error. So for any `.d2` with icons:
  **before rendering, run `./scripts/check-d2-icons.sh <file>.d2`** — it HTTP-checks every
  icon URL and fails on any that don't resolve (deterministic). Then still Read the PNG to
  confirm the icons appear.
