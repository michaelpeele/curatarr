# Plan — Sprint 6 library-grid UI polish

**Status: DONE (2026-11-19), including follow-up.** Group B and its follow-up were implemented, sandbox-verified, manually verified, and promoted to prod. Final served bundles: `index-UEE7VSba.js` / `index-fENfh4zq.css` (previous promoted baseline `index-R4dyCEyO.js` / `index-fENfh4zq.css`). ROADMAP items were marked done only after the original promotion; no ROADMAP change was needed for the follow-up.

Source of truth: `.project_documents/ROADMAP.md`. Group definition and order: `.project_documents/REMAINING-WORK-GROUPS.md` (Group B). Workflow: `/home/michaelpeele/ai-projects/workflow.md` (sandbox-first, build only via Docker, promote only after green).

## Scope

Three ROADMAP items, in this order:

1. `ROADMAP.md:564` — **UI — Loading state with animation instead of blank cards.**
2. `ROADMAP.md:607` — **Show quality variants in the text label when multiple copies exist.**
3. `ROADMAP.md:176` — **Mobile & Tablet Responsive Views** — *narrowed by user decision 2026-11-18*: keep both the current **3-column mobile grid** and the current tablet grid columns **unchanged**; all column-count changes are retired. Retained work is the shared breakpoint hook, 44px touch targets, full-screen touch modals, and a compact tablet sidebar.

Explicitly **out of scope** (do not fold in): server-side paging, loading animation for other views, quality-variant *filtering*/badges beyond the text label, responsive redesign of non-library views beyond BottomBar/Sidebar touch behaviour, **any change to mobile poster column counts**, infra/deployment work, metadata enrichment, watch state, or any other remaining group.

## Why these are one group

All three touch `Curatarr.Web/src/components/PosterGrid.tsx`, its card/skeleton components, and the same loading/paging surface. Quality variants additionally introduce a shared quality parser that must be derived from existing path data (there is no stored quality column). Keeping them in one batch avoids three rebuild/promote cycles on the same files.

Dependency order rationale: the loading animation establishes a shared, shape-matching skeleton primitive; quality variants add a text-label transform on the same card; the responsive sweep then adjusts touch targets and modals using those finished components. All column counts (mobile and tablet) are unchanged (user decision 2026-11-18).

---

## Item 1 — Loading state with animation (`ROADMAP.md:564`)

### Requirement (verbatim intent)

While a library page is loading — both the initial fetch and subsequent page appends — show a visible **Loading** indicator with an animation in place of the current blank/skeleton cards. Logged as a follow-up to Bug #2 (oversized posters) but explicitly **not** part of that fix.

### Current state (code findings)

- `PosterGrid.tsx` state: `loading` (~line 1014), `loadingMore` (~1048); page fetch at `fetchPage` (~1119), `loadMore` (~1162).
- Non-paged path returns plain `<p>Loading library...</p>` (~1729).
- Paged path returns `<FirstPageSkeleton showActions={!!libraryId} />` when `loading && itemCount === 0` (~1732). The skeleton is **static** — no animation, no visible "Loading" text.
- Page appends show plain `Loading more…` text (~2421).
- `LazyMount` (~33-108) unmounts offscreen cards (IntersectionObserver, `rootMargin: 1200px`) to avoid Safari memory reloads, and renders a shape-matching skeleton: 2px border, `aspect-ratio: 2/3` poster block, 34px title bar, plus `CARD_ACTION_ROW_STYLE` (30px fixed row, ~161-169). `reconcileStatus()` at ~171-177.

### Proposed implementation

- Add one small animated indicator built from existing CSS variables: a `Loading` label plus a CSS `@keyframes` pulse/spinner in `theme/tokens.css` (the file already has one `@keyframes slideIn`). Use `--text-muted` / `--color-accent` and `prefers-reduced-motion` to disable animation for users who opt out.
- Initial load (both paged and non-paged paths): render `FirstPageSkeleton` **with** the animated indicator overlaid/above the grid, so layout shift is unchanged. Replace the plain `<p>Loading library...</p>`.
- Page appends: replace `Loading more…` with the same indicator component at the grid footer.
- Reuse `CARD_ACTION_ROW_STYLE` so skeleton height matches real cards (preserves the no-layout-shift property that Bug #2 relied on).
- Keep `LazyMount` behaviour untouched.

#### Follow-up requested 2026-11-19 (completed 2026-11-19)

- Initial load **only**: replace the blank `FirstPageSkeleton` cards with a gray card-area panel and a centered, modal-style `Loading library…` status. The panel occupies the normal card area, has no placeholder cards, and uses existing surface/border/text tokens. Library-switch refetches use the same `loading` state and therefore get the same treatment.
- Page-append loading (`Loading more…`) and the non-paged plain-text loading path are unchanged.
- `CARD_ACTION_ROW_STYLE` and `LazyMount` remain untouched; this is not a layout redesign.

#### Quality-token correction requested 2026-11-19 (completed 2026-11-19)

- User clarification: `A Charlie Brown Thanksgiving (1973)` has two separate files (`1080p` and `720p`). The intended UI is **two cards, each showing its own resolution** (`— 1080p` and `— 720p`), not one card showing both markers together.
- Movies must therefore use a per-card quality suffix, shown only when the title has more than one loaded file. A single-file title still shows no suffix. Show grouping is unchanged by this follow-up.
- `A Quiet Place Part II` (and similar `1080p.UHD.BluRay` names, including the earlier `The Dark Knight` example) use **UHD as a source/master tag on a 1080p encode**. Treat any explicit `1080p`/`720p`/`480p` token as authoritative and ignore both bare `4k` and `uhd` in that case. Keep `2160p` as an explicit 4K token.
- This refines the client-only parser; no API, schema, or stored quality column is introduced.

### Decisions to confirm

- Indicator style: text label + subtle pulse vs. spinner. Recommend text + pulse (cheapest, matches existing design tokens).
- Whether the animation should also appear during library-switch refetch. Recommend yes — same `loading` state already covers it.

---

## Item 2 — Quality variants in text label (`ROADMAP.md:607`)

### Requirement (verbatim intent)

When the catalog has more than one quality version of the same title (e.g. 1080p and 4K), indicate the quality in human-readable text on each file's own card, e.g. `Groundhog Day (1993) — 1080p` and `Groundhog Day (1993) — 4K`. When only one version exists, show no suffix. ROADMAP says the exact surface is **to be confirmed in Sprint 6 planning**; the 2026-11-19 follow-up resolves this as per-card quality text for movies (show grouping stays unchanged).

### Current state (code findings)

- `SourceItem` (`Curatarr.Engine/Data/Models.cs:39-113`) has `RelativePath` and computed `SourcePath`, but **no Quality/Resolution/Source column**.
- `client.ts` `LibraryEntry` (from ~line 11) has `relativePath?: string | null` (~26, ~168) and **no** quality/resolution/codec fields.
- `PosterGrid.tsx` already reads `entry.relativePath` at ~718, 824, 827, 832 for `buildShowGroups` and labels; `showGroups.ts:69-70` keys on `relativePath`.
- `RELEASE_TOKENS` in `PosterGrid.tsx` (~115-121) mirrors `TitleQueryParser.ReleaseTokens` in `Curatarr.Engine/TitleQueryParser.cs:13-20` (2160p/1080p/720p/480p/4k/uhd etc.). `normalizeTitleQuery()` (~123-151) strips release tags for the Match modal. This is the natural seed for a shared parser.
- `buildShowGroups` / `representativeId` are imported at `PosterGrid.tsx:13-17` and already group duplicate copies of a title — the right hook for collecting variants.

### Proposed implementation

- Add a pure client-side helper (e.g. `parseQualities(path: string | null | undefined): string[]`) near the existing `RELEASE_TOKENS`, using the same token set. Canonicalise `4k`/`uhd` → `4K`, keep `1080p`/`720p`/`2160p` as-is.
- For movies, detect when a title has more than one loaded file, then append each file's own detected quality to its card label (`— 1080p` / `— 720p`); if the title has only one file, append nothing. For shows, keep the existing grouped union of episode paths.
- Mirror the token set in `TitleQueryParser.cs` only if a server consumer is needed; **prefer client-only** to avoid an API contract change in this batch.
- Surface: apply to the library card's text label (with the existing title/year line). Do **not** change poster images, and do not add a filter.

### Decisions to confirm (must resolve before coding)

1. **Which surface exactly** — card text label only, or also unmatched/match modal rows? ROADMAP leaves this open. Recommendation: card text label only for the first pass; match-modal rows can reuse the same helper later without a new parser.
2. **Quality order** — recommend highest-first (`4K`, `2160p`, `1080p`, `720p`, `480p`).
3. **Unknown quality** — titles with a path but no recognisable token should not contribute a label; a title with one known + one unknown quality still shows no suffix (only one *distinct known* quality).

---

## Item 3 — Mobile & Tablet Responsive Views (`ROADMAP.md:176`)

### Requirement (verbatim intent, narrowed by user decision 2026-11-18)

- Mobile (320px–576px): **retired** — the 1–2 poster column change is **not** implemented. Keep the current 3-column grid (`PosterGrid.tsx:2334`) exactly as-is. Bottom navigation (`BottomBar.tsx`), full-screen touch modals, zero poster clipping, and 44px targets still apply.
- Tablet / iPad (768px–1024px): **retired** — the 2–4 adaptive poster column change is **not** implemented; keep the current `repeat(auto-fill, minmax(150px, 1fr))` grid (`PosterGrid.tsx:2334`) unchanged. Compact drawer/sidebar, full-screen touch modals, touch padding, and 44px targets still apply.

### Current state (code findings)

- No `@media` queries anywhere in `Curatarr.Web/src/**/*.css`. `theme/tokens.css` has palette, semantic roles (`--surface-page`, `--surface-card`, `--surface-raised`, `--border`, `--border-strong`, `--text-*`, `--color-*`), typography, `--radius*`, one `@keyframes slideIn`, and dark theme via `[data-theme="dark"]`. No breakpoints.
- Components are flat in `Curatarr.Web/src/components/` (28 `.tsx` files) and use **inline styles** almost exclusively.
- Existing ad-hoc breakpoints: `PosterGrid.tsx:1033` `isMobile = window.innerWidth < 640`; grid at `:2334` uses `isMobile ? "repeat(3, minmax(0, 1fr))" : "repeat(auto-fill, minmax(150px, 1fr))"`. `FirstPageSkeleton` (~774) always uses `repeat(auto-fill, minmax(150px, 1fr))`. `BottomBar.tsx:71`, `Sidebar.tsx:53`, `App.tsx:256` each have their own `isMobile`.
- `flexWrap` at `PosterGrid.tsx` ~942, 2056, 2216.
- Calendar slice 2 already uses 44px touch targets and CSS variables in `CalendarItemModal.tsx` — reuse that pattern.

### Proposed implementation

- Introduce a **single shared breakpoint hook/constant** (e.g. `useViewport()` returning `'mobile' | 'tablet' | 'desktop'`) so PosterGrid, Sidebar, BottomBar and App stop disagreeing on `isMobile`. Keep it small; do not add a CSS framework. Retain the existing `<640` mobile boundary so the current mobile grid is unchanged.
- **Mobile poster columns are unchanged (3).** Keep `PosterGrid.tsx:2334` (`isMobile ? "repeat(3, minmax(0, 1fr))" : ...`) and `FirstPageSkeleton` as-is. The 1–2 column change is retired by user decision 2026-11-18.
- **Tablet columns are unchanged.** Keep `repeat(auto-fill, minmax(150px, 1fr))` at `PosterGrid.tsx:2334` and in `FirstPageSkeleton` exactly as-is; the 2–4 column change is retired by user decision 2026-11-18.
- Full-screen touch modals: add a mobile-only wrapper style (inset 0, `--surface-page`, scrollable body) to the card action/detail modals. Preserve Escape-to-close and `role="dialog"`/`aria-modal` semantics from `CalendarItemModal`.
- Touch padding/targets: minimum 44px on card action rows and modal controls (already 30px in `CARD_ACTION_ROW_STYLE` — raise only at mobile/tablet breakpoints via the shared style helper, not globally).
- Sidebar compact drawer at tablet; BottomBar remains the mobile navigation (already exists).
- Use existing CSS variables throughout; no new colour tokens.

### Decisions to confirm

1. **Column counts — resolved 2026-11-18:** keep the current 3-column mobile grid and the current tablet grid unchanged; all column-count changes are retired. No further decision needed.
2. **Compact tablet sidebar — approved:** include it in this group.
3. **Full-screen touch modals — approved for all library-grid modals** for consistency.

---

## Data-path impact

| Item | API / data changes | New state |
|---|---|---|
| Loading animation | none | none (reuses `loading` / `loadingMore`) |
| Quality variants | prefer none (client-only from `relativePath`); optional server token mirror | none (derive during render) |
| Responsive | none | one shared viewport hook/constant |

No migrations, no new endpoints, no new hidden/approved state. Calendar and its slice-2 modal remain untouched.

## Verification plan (mandatory, per workflow.md)

1. `docker compose build api web` (with `--no-cache web` if the bundle looks stale).
2. `./scripts/promote.sh --sandbox-only`; wait for `:5001/api/bootstrapped`; run smoke checks and `BASE=http://localhost:5001 WIZARD_ONLY=1 ./test-api.sh`.
3. Manual sandbox verification at `:5174` with `admin@example.com` / `Passw0rd!123`, covering:
   - Loading indicator visible and animated on initial load, library switch, and page append; no layout shift; `prefers-reduced-motion` disables animation.
   - Quality suffix appears only when a grouped title has >1 distinct quality; correct order; no suffix for single/unknown quality; poster images unchanged.
   - Mobile 320/375/576px: **3 columns unchanged**, no clipping, BottomBar visible, full-screen modals, 44px targets.
   - Tablet 768/1024px: existing grid columns unchanged, compact sidebar, full-screen modals, touch padding.
   - Calendar slice-2 behaviour and `Sync applies to all libraries.` still work; month arrows/date jump/library filter/media-type lock unchanged.
   - Browser console: zero errors.
4. Record manual checks in `.project_documents/PROGRESS.md`.
5. Only after all green: `./scripts/promote.sh --prod-only` (or rerun sandbox if code changed after verification). Hard-refresh prod and compare served bundle hashes against previous `index-C4TfM9WN.js` / `index-CoR6XILo.css`.

## Risks

- **Layout shift** — skeleton and real cards must stay the same height; reuse `CARD_ACTION_ROW_STYLE`.
- **Safari memory reloads** — do not change `LazyMount` unmount behaviour; grid columns are unchanged by user decision.
- **Inline-style sprawl** — adding three responsive behaviours could fragment breakpoints further; the shared viewport hook is the mitigation.
- **Quality parsing false positives** — release tokens in arbitrary path segments; keep the parser pure and test it with representative paths before wiring it in.
- **Scope creep** — quality variants must remain a text label only; responsive must not become a redesign.

## Acceptance criteria

- An animated `Loading` indicator is visible during initial library load and page appends, replacing blank/static skeletons, with no layout shift.
- Grouped titles with multiple distinct qualities show e.g. `— 1080p / 4K`; single-quality and unknown-quality titles show no suffix.
- Mobile 320–576px keeps the current 3 non-clipping columns unchanged, with BottomBar, full-screen modals, and 44px targets; tablet 768–1024px keeps its existing grid columns unchanged, with a compact sidebar, full-screen modals, and touch padding.
- Calendar slice 2, shared BottomBar/Sync behaviour, and `Sync applies to all libraries.` are unchanged.
- Sandbox-first verification and promotion logged; prod bundle hash changed; no console errors; no other group started.
