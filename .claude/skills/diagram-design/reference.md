# Diagram Design — Reference

Deep detail behind `SKILL.md`. Read the section for the diagram type you're drawing.

## Table of contents
- [C4 levels](#c4-levels)
- [Palette](#palette)
- [Per-type anti-patterns](#per-type-anti-patterns)
- [Renderer notes](#renderer-notes)

## C4 levels

C4 (Context, Container, Component, Code) is a way to draw architecture at a chosen zoom
level. Pick the level that matches your audience and message, and **don't mix levels in
one diagram** — mixing is the most common reason an architecture diagram confuses people.

- **L1 — Context.** The system as one box, surrounded by its users and the external
  systems it talks to. Audience: anyone, including non-technical. Message: "what is this
  system and what does it interact with." Show people and neighboring systems; hide all
  internals.
- **L2 — Container.** Zoom into the system: the deployable/runnable things inside it
  (web app, API, worker, database, queue) and how they communicate (protocols). Audience:
  technical people who need the shape of the system. This is the **default** and the most
  useful level. Containers ≈ processes/datastores, not Docker specifically.
- **L3 — Component.** Zoom into one container: its major internal parts and their
  responsibilities. Audience: developers working in that container. Only draw this for a
  container that's complex enough to warrant it.
- **L4 — Code.** Class-level. Almost never worth hand-drawing — generate it if needed.

**Supplemental views:**
- **Deployment** — maps containers onto infrastructure (nodes, regions, networks). Use the
  icon-cloud (`cloud.d2`) mode. Orthogonal to L1–L3.
- **Dynamic** — numbered steps (①②③…) over a container diagram to trace one flow through
  the structure. A labeling technique, not a separate diagram. Good when a sequence
  diagram would lose the spatial structure people already know.

## Palette

The soft house palette (fill / stroke / text). Defined as `classDef`s in the mermaid
templates; reuse these so diagrams look consistent.

| Role | Fill | Stroke | Text |
|---|---|---|---|
| Layer / zone band | `#fdf6ec` | `#e7d9c4` | `#5b4636` |
| Actor / external / start-end | `#ede9fe` | `#6d28d9` | `#2e1065` |
| UI / process | `#e0e7ff` | `#4338ca` | `#1e1b4b` |
| Service / datastore | `#dcfce7` | `#15803d` | `#14532d` |
| Infra / queue / decision | `#fef9c3` | `#a16207` | `#422006` |
| Return / error edge | `#ef4444` | — | `#ef4444` |

Principle: low-saturation fills, darker same-hue stroke and text. Color should encode a
*category* (layer, node type) or *semantics* (call vs return) — never decoration.

## Per-type anti-patterns

**Architecture**
- Mixing C4 levels (a container next to a class next to a whole external system).
- Logical structure and physical deployment in one diagram — split them.
- Boxes with no relationship labels — say *what* flows and *how* (protocol/verb).
- A wall of 20+ boxes — go up a level or split by subsystem.

**Sequence**
- Unlabeled messages, or messages that don't say what's sent.
- No return arrows where the response matters (use dashed `-->>`).
- Implying a time order that isn't real — sequence reads top-to-bottom as time.
- Over-nested `alt`/`loop`/`opt` frames — keep nesting ≤ 1 level; split complex flows.
- Too many participants (> ~6) — the eye can't track the lifelines.

**State machine**
- Using literal `start`/`end` nodes instead of `[*]` for initial/final pseudo-states.
- Unlabeled transitions — every arrow needs the event/condition that triggers it.
- Unreachable or dead-end states (no way in, or no way out where there should be).
- Modeling data instead of states — states are modes of behavior, not field values.

**ER**
- Missing cardinality (is it one-to-many or many-to-many?).
- Many-to-many drawn as a bare line — introduce a junction entity, especially if the
  relationship carries its own attributes (e.g. `ORDER_ITEM.quantity`).
- Unlabeled relationships — name the verb (`places`, `contains`).
- Attributes drawn as separate boxes instead of fields inside the entity.

**Flowchart**
- Decision diamonds whose branches aren't labeled (yes/no, or the condition).
- More than one start node, or no clear start.
- Crossing/spaghetti flows — reorder nodes or change direction.
- Using a flowchart for something that's really a sequence (interactions between parties)
  or a state machine (lifecycle) — re-route to the right type.

## Actor / user icon

A clean filled-person icon reads better than a plain box or pill for the human actor. A
person icon is bundled at `assets/user.png` (the source of truth).

- **mermaid** — embed it as a **data URI** in an image node (a file path won't resolve in
  mermaid-cli; a data URI is self-contained and portable):
  ```
  user@{ img: "data:image/png;base64,…", label: "User", w: 64, h: 64, pos: "b", constraint: "on" }
  style user fill:transparent,stroke:transparent
  ```
  `constraint: "on"` keeps the aspect ratio; the transparent `style` hides the node box so
  only the icon shows. The ready-made data URI is in `templates/layered.mmd` — copy it. To
  regenerate from the asset (kept small): `sips -z 64 64 assets/user.png --out /tmp/u.png`
  then `base64 -i /tmp/u.png`.
- **D2** — `user: User { shape: image; icon: <absolute path to assets/user.png, or a data URI> }`.
- **Sequence diagrams** keep the standard `actor` keyword (stick figure) — mermaid
  `sequenceDiagram` can't use a custom actor image, and the stick figure is conventional.

## Renderer notes

**mermaid**
- Frontmatter title (`---\ntitle: "…"\n---`) must be the **first lines** of the file —
  no comment or blank line before it, or you get "Parse error on line 1".
- `flowchart`: `linkStyle <index> stroke:#…` colors a specific edge (0-based, in source
  order) — used for the red return edge.
- `sequenceDiagram`: arrows can't be individually colored; `->>` solid = call,
  `-->>` dashed = return. `autonumber` adds step numbers.
- `stateDiagram-v2`: `[*]` is both initial and final. Label transitions with `: event`.
- `erDiagram`: auto-lays out — no direction. Cardinality: `||--o{` (one-to-many),
  `||--|{` (one-to-one-or-many), `}o--o{` (many-to-many).

**D2 (icon-cloud)**
- `shape: image` + `icon: <url>` makes a service node (icon with label beneath).
- Group services with containers (`gcp: Google Cloud { … }`); nest freely.
- **Icons fail silently.** Verify each URL returns HTTP 200 before using it, and Read the
  rendered PNG to confirm icons appear. Catalog: `https://icons.terrastruct.com`.
  Encode spaces as `%20` and path separators as `%2F` in the URL.
- Title: a `shape: text` node with `near: top-center` (a markdown `|md|` title block can
  clip at the canvas top).
- Verified-good icon URLs (examples): GitHub `dev%2Fgithub.svg`; GCP Cloud Run
  `gcp%2FProducts%20and%20services%2FCompute%2FCloud%20Run.svg`; Cloud SQL
  `…%2FDatabases%2FCloud%20SQL.svg`; Pub/Sub `…%2FData%20Analytics%2FCloud%20PubSub.svg`.
