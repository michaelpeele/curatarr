# New-session prompt — first remaining item

Copy everything in the block below into a new OpenCode session in `/home/michaelpeele/ai-projects/Curatarr`.

```text
Work on the first remaining grouped item: Calendar from metadata.

Start by reading:
1. The AGENTS.md pointer at the project root and /home/michaelpeele/ai-projects/workflow.md.
2. .project_documents/REMAINING-WORK-GROUPS.md.
3. .project_documents/ROADMAP.md, especially the Calendar items at lines 179 and 356-363 and the Show-level metadata item at line 162.
4. The current Calendar placeholder in Curatarr.Web/src/App.tsx (around lines 243-255 and 359-363) and Curatarr.Web/src/components/Sidebar.tsx (line 27).
5. The metadata/raw-storage paths in Curatarr.Engine/MetadataProvider.cs, Curatarr.Engine/Data/Models.cs, and Curatarr.Engine/Data/Migrations.
6. Existing approve/hide client/API code used by PosterGrid so the Calendar modal reuses the same semantics.

Do not start any other remaining group: no loading animation, no quality-variant labels, no responsive redesign, no infra work.

Before editing code, give me a short current-state summary and ask clarifying questions. At minimum ask:
1. Date source: add explicit SourceItem date columns/backfill, parse existing TmdbRawDetails.Json, or call TMDB live from a new endpoint?
2. TV dates: use next_episode_to_air, season air_date values, or episode-level dates from /tv/{id}/seasons/{n}? Should past episodes/seasons be included, or only upcoming?
3. Should Calendar be admin-only or available to all authenticated users? The current App.tsx access guard allows calendar for non-admins and Sidebar has no adminOnly flag.
4. What timezone should drive "current month" and date grouping — browser local time or UTC?
5. In "All" view, when an entry is linked to a library, should the modal call the normal Mark for library endpoint, and should Hide from Library call the existing hide endpoint with the same Sync warning?
6. Should the first slice include link/hide actions, or ship a read-only calendar first and add those actions in a second pass?

After I answer, update the plan/documentation with the decisions first, then implement.

Implementation scope:
- Replace the ComingSoonPage Calendar placeholder with a real CalendarPage component and remove the comingSoon flag from the sidebar.
- Add a month grid defaulting to the current month, with ‹/› month arrows and a clickable "Month YYYY" heading that opens a date picker/jump.
- Cells list movie release dates and show air dates with poster + title.
- Add library and Movie/Show/Anime filters. "All" view entries should open a modal to link the item to an existing library plus Mark for library / Hide from Library actions using existing APIs and wording.
- Reuse completed per-library hide and media-type view-filter semantics; do not reinvent approval/hide state.
- Keep the UI mobile/iOS Safari-friendly, but do not expand into the separate responsive-view item.

Mandatory workflow:
- One shared tree at /home/michaelpeele/ai-projects/Curatarr.
- No dotnet on the host; build only through Docker.
- Build and verify with scripts/promote.sh --sandbox-only: wait for :5001/api/bootstrapped, smoke checks, WIZARD_ONLY=1 ./test-api.sh, then manual browser verification on :5174.
- Do not touch prod until sandbox checks and manual verification pass. If no code changes after sandbox verification, use scripts/promote.sh --prod-only.
- Current pre-work bundle to compare against: assets/index-CFYEqZWo.js and assets/index-CoR6XILo.css. After promotion, hard-refresh/cache-bust and verify the served bundle hash.
- Admin credentials: admin@example.com / Passw0rd!123. Cookie jars: /tmp/ck.txt (prod), /tmp/ck-sandbox.txt (sandbox).
- Record manual verification in .project_documents/PROGRESS.md.
```
