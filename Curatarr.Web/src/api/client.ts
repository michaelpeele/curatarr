// Thin typed wrapper over the Curatarr.Api endpoints we already built and
// tested via curl. Uses cookie-based auth (credentials: "include"), so no
// token handling needed -- the Identity cookie set by /api/auth/login
// rides along automatically.

export type MediaKind = "Movie" | "Show" | "Anime";
export type MembershipRole = "Owner" | "Curator" | "Viewer";
export type LibraryView = "all" | "library" | "unmarked" | "hidden";

/** One already-grouped card from GET /api/library-entries (Sprint 5 #8). */
export interface LibraryEntry {
  kind: "movie" | "show";
  /** Movies only. */
  id?: string;
  /** Show groups only: "Show:tmdb:1234" / "Show:title:name" / "Show:id:...". */
  key?: string;
  title: string;
  mediaType: MediaKind;
  posterUrl: string | null;
  overview?: string | null;
  year?: number | null;
  showTitle?: string | null;
  seasonNumber?: number | null;
  episodeNumber?: number | null;
  tmdbId?: number | null;
  relativePath?: string | null;
  /** TMDB ids (array on the wire; the legacy SourceItem carries a CSV string). */
  genreIds: number[] | null;
  rating: string | null;
  productionCompanies?: string | null;
  networks?: string | null;
  director?: string | null;
  creators?: string | null;
  cast?: string | null;
  /** Show groups: approved = ANY episode approved, hidden = ALL episodes hidden. */
  approved: boolean;
  approvedAt: string | null;
  hidden: boolean;
  /** Current hide/unhide request stamp (newest episode stamp for show groups). */
  hiddenAt?: string | null;
  /** Set while an unhide waits for Sync; hidden stays true in that window. */
  unhideRequestedAt?: string | null;
  seasonCount?: number | null;
  episodeCount?: number | null;
  latestSeason?: number | null;
  latestEpisode?: number | null;
  representativeId?: string | null;
  episodeIds?: string[] | null;
}

export interface LibraryEntriesPage {
  total: number;
  offset: number;
  limit: number;
  hasMore: boolean;
  mediaType: MediaKind | null;
  entries: LibraryEntry[];
}

export interface LibraryFacets {
  ratings: string[];
  studios: string[];
  networks: string[];
  genres: { id: number; count: number }[];
}

export interface LibraryEntriesParams {
  libraryId?: string | null;
  mediaType?: MediaKind | null;
  q?: string | null;
  /** Repeatable on the wire; an array here emits multiple ?genre= params. */
  genre?: number | number[] | null;
  rating?: string | null;
  studio?: string | null;
  network?: string | null;
  letter?: string | null;
  view?: LibraryView | null;
  /** Admin-only: show only rows with no (or a non-positive) TMDB id. */
  unmatched?: boolean;
  offset?: number;
  limit?: number;
}

export interface LibraryFacetsParams {
  libraryId?: string | null;
  mediaType?: MediaKind | null;
  view?: LibraryView | null;
  unmatched?: boolean;
}

/** One dated title on the Calendar. */
export interface CalendarEvent {
  id: string;
  mediaType: MediaKind;
  title: string;
  showTitle: string | null;
  /** yyyy-MM-dd, grouped in the configured server timezone. */
  date: string;
  posterUrl: string | null;
  seasonNumber: number | null;
  episodeNumber: number | null;
  isSeasonPremiere: boolean;
  tmdbId: number | null;
  /** Every SourceItem represented by this event (all episodes for a show). */
  sourceItemIds: string[];
  /** The item carrying the season/episode metadata used for Mark actions. */
  representativeSourceItemId: string | null;
}

export interface CalendarEventsResponse {
  timeZone: string;
  /** yyyy-MM-dd in the configured server timezone. */
  today: string;
  /** yyyy-MM, the month the events belong to. */
  month: string;
  events: CalendarEvent[];
}

export interface CalendarEventsParams {
  month?: string | null;
  libraryId?: string | null;
  mediaType?: MediaKind | null;
}

/** One TMDB candidate shown in the admin Match modal. */
export interface TitleSearchResult {
  mediaType: "Movie" | "Show";
  tmdbId: number;
  title: string;
  year: number | null;
  posterUrl: string | null;
  overview: string | null;
}

export interface MetadataSearchResponse {
  available: boolean;
  results: TitleSearchResult[];
}

export interface ApplyMatchRequest {
  sourceItemId?: string;
  representativeId?: string;
  tmdbId: number;
  mediaType: MediaKind;
  title: string;
  year?: number | null;
  posterUrl?: string | null;
  overview?: string | null;
}

function addQueryParam(qs: URLSearchParams, key: string, value: string | number | null | undefined) {
  if (value === null || value === undefined || value === "") return;
  qs.append(key, String(value));
}

export interface SourceItem {
  id: string;
  mediaType: MediaKind;
  title: string | null;
  year: number | null;
  showTitle: string | null;
  seasonNumber: number | null;
  episodeNumber: number | null;
  tmdbId: number | null;
  posterUrl: string | null;
  overview: string | null;
  /** Present on the list DTO; used to dedupe unnumbered episodes across roots. */
  relativePath?: string | null;
  /** TMDB genre IDs (comma-separated string in DB, parsed to array for client) */
  genreIds: string | null;
  /**
   * US certification ("G".."NC-17" for movies, "TV-Y".."TV-MA" for shows).
   * "" = fetched, none found; null = not fetched yet.
   */
  rating: string | null;
  /** Movies: "|" -separated production company names. "" none, null not fetched. */
  productionCompanies: string | null;
  /** Shows/anime: "|" -separated network names. "" none, null not fetched. */
  networks: string | null;
  /** Movies: director name (single). "" none, null not fetched. */
  director: string | null;
  /** Shows/anime: "|" -separated creator names. "" none, null not fetched. */
  creators: string | null;
  /** Movies + shows: "|" -separated top-30 cast names (billing order). */
  cast: string | null;
  /** Grid flags, present only when /api/source-items?libraryId=… was used. */
  approved?: boolean;
  approvedAt?: string | null;
  hidden?: boolean;
  hiddenAt?: string | null;
  unhideRequestedAt?: string | null;
}

export interface VirtualLibrary {
  id: string;
  name: string;
  mediaType: MediaKind;
  outputPath: string;
  /** Last successful reconcile; used to tell "marked" from "in library". */
  lastReconciledAt?: string | null;
}

export interface SourceRoot {
  id: string;
  name: string;
  path: string;
  mediaType: MediaKind;
}

export interface AppUser {
  id: string;
  email: string;
  displayName: string | null;
  /** Global Identity roles; an Admin has "Admin" in this array. */
  roles: string[];
}

export interface LibraryMember {
  userId: string;
  email: string;
  role: MembershipRole;
}

export interface PathMapping {
  id: string;
  fromPrefix: string;
  toPrefix: string;
}

export interface PlexStatus {
  linked: boolean;
  accountName: string | null;
  serverName: string | null;
  serverUrl: string | null;
  refreshOnChange: boolean;
  lastRefreshAt: string | null;
  lastRefreshStatus: string | null;
}

export interface LibrarySchedule {
  id: string;
  virtualLibraryId: string;
  name: string | null;
  enabled: boolean;
  /** 0=Sunday .. 6=Saturday; empty = every day. */
  daysOfWeek: number[];
  startTime: string;
  endTime: string;
  startMonth: number | null;
  startDay: number | null;
  endMonth: number | null;
  endDay: number | null;
  createdAt: string;
  updatedAt: string;
}

export interface ScheduleStatus {
  libraryId: string;
  isActive: boolean;
  alwaysOn: boolean;
  timeZone: string;
  evaluatedAt: string;
  nextTransitionAt: string | null;
  nextIsActivation: boolean | null;
  scheduleCount: number;
  enabledCount: number;
}

// Sprint 6 #1 — automatic approve/hide rules. Field metadata comes from the
// shared backend registry via /api/library-fields, so no field list is
// duplicated here.
export type RuleOperator = "equals" | "contains" | "in" | "gte" | "lte";
export type RuleAction = "approve" | "hide";

export interface RuleField {
  value: string;
  label: string;
  kind: "genre" | "text" | "number" | "enum";
  operators: RuleOperator[];
  mediaTypes: MediaKind[];
  hint: string | null;
}

export interface LibraryRule {
  id: string;
  field: string;
  operator: string;
  value: string;
  action: RuleAction;
  priority: number;
  enabled: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface LibraryRuleInput {
  id?: string;
  field: string;
  operator: string;
  value: string;
  action: RuleAction;
  priority: number;
  enabled: boolean;
}

export interface RulePreview {
  toApprove: number;
  toHide: number;
  samples: string[];
}

export interface RuleApplyResult {
  approved: number;
  hidden: number;
  unapproved: number;
  unhidden: number;
  logId: string;
}

export interface BrowseEntry {
  name: string;
  path: string;
}
export interface BrowseResult {
  path: string;
  parent: string | null;
  entries: BrowseEntry[];
}

export interface TitleDetails {
  available: boolean;
  title?: string;
  overview?: string | null;
  posterUrl?: string | null;
  director?: string | null;
  creators?: string[] | null;
  cast?: string[];
}

export interface Me {
  email: string;
  roles: string[];
}

// ─── Activity + Logs (Sprint 5 #1/#2) ────────────────────────────────────
export type ActivityCategory =
  | "Scheduling" | "User" | "Plex" | "Scan" | "Source" | "Path";

export interface ActivityEvent {
  id: string;
  occurredAt: string;
  category: ActivityCategory;
  type: string;
  actor: string;
  detail: string;
  metadata: string | null;
}

export interface ActivityPage {
  items: ActivityEvent[];
  nextCursor: string | null;
}

export interface ScanStatus {
  running: boolean;
  startedAt: string | null;
  completed: number;
  total: number;
  paused: boolean;
  stopped: boolean;
  elapsedSeconds: number;
  etaSeconds: number | null;
}

export interface LogEntry {
  seq: number;
  timestamp: string;
  level: string;
  sourceContext: string;
  message: string;
  exception: string | null;
}

export interface LogPage {
  entries: LogEntry[];
  nextSeq: number | null;
}

export interface LogConfig {
  level: string;
  retentionDays: number;
  fileSizeLimitMB: number;
  archiveEnabled: boolean;
}

// The Identity cookie can expire or be invalidated by a server-side key
// rotation/restart. When that happens every API call starts returning 401
// with no UI reaction (raw "Request failed (401)" toasts). App registers a
// handler here so the first 401 drops the stale session and shows the login
// screen with an explanation instead.
let unauthorizedHandler: (() => void) | null = null;

export function setUnauthorizedHandler(handler: (() => void) | null): void {
  unauthorizedHandler = handler;
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(path, {
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    ...init,
  });
  if (!res.ok) {
    // Auth endpoints are excluded: a failed login is a normal wrong-password
    // flow, not an expired session, and must not bounce the login screen.
    if (res.status === 401 && !path.startsWith("/api/auth/")) {
      unauthorizedHandler?.();
    }
    throw new Error(await extractErrorMessage(res));
  }
  // some endpoints (e.g. logout, approve) return 200 with no body
  const text = await res.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

// The server returns errors in a few different shapes depending on the
// endpoint: an array of Identity errors ([{code, description}, ...]),
// a single object with a "message" field, or nothing parseable at all.
// Without this, every failure just showed "POST /x failed: 400" with no
// indication of what actually went wrong (e.g. a password policy violation).
async function extractErrorMessage(res: Response): Promise<string> {
  const fallback = `Request failed (${res.status})`;
  try {
    const text = await res.text();
    if (!text) return fallback;
    const body = JSON.parse(text);
    if (Array.isArray(body)) {
      const descriptions = body.map((e) => e?.description ?? e?.code).filter(Boolean);
      if (descriptions.length > 0) return descriptions.join(" ");
    }
    if (typeof body?.message === "string") return body.message;
    if (typeof body?.error === "string") return body.error;
    if (typeof body?.title === "string") return body.title;
    return fallback;
  } catch {
    return fallback;
  }
}

export const api = {
  register: (email: string, password: string, displayName?: string) =>
    request<{ id: string; email: string; role: string }>("/api/auth/register", {
      method: "POST",
      body: JSON.stringify({ email, password, displayName }),
    }),

  login: (email: string, password: string) =>
    request<{ message: string }>("/api/auth/login", {
      method: "POST",
      body: JSON.stringify({ email, password }),
    }),

  logout: () => request<void>("/api/auth/logout", { method: "POST" }),

  me: () => request<Me>("/api/me"),

  listSourceItems: (libraryId?: string | null) =>
    request<SourceItem[]>(
      libraryId ? `/api/source-items?libraryId=${encodeURIComponent(libraryId)}` : "/api/source-items"
    ),

  // Admin-only match tooling (Sprint 5 #5).
  searchMetadata: (query: string, year?: number | null, mediaType?: MediaKind | null) => {
    const qs = new URLSearchParams();
    addQueryParam(qs, "query", query);
    addQueryParam(qs, "year", year);
    addQueryParam(qs, "mediaType", mediaType);
    return request<MetadataSearchResponse>(`/api/metadata/search?${qs.toString()}`);
  },
  matchMetadata: (body: ApplyMatchRequest) =>
    request<{ affected: number }>("/api/metadata/match", {
      method: "POST",
      body: JSON.stringify(body),
    }),

  // Paged library grid (VITE_LIBRARY_PAGING). Uses the same request helper so
  // 401 handling and {error} messages stay identical to the legacy endpoint.
  listLibraryEntries: (params: LibraryEntriesParams) => {
    const qs = new URLSearchParams();
    addQueryParam(qs, "libraryId", params.libraryId);
    addQueryParam(qs, "mediaType", params.mediaType);
    addQueryParam(qs, "q", params.q);
    if (Array.isArray(params.genre)) {
      for (const id of params.genre) addQueryParam(qs, "genre", id);
    } else {
      addQueryParam(qs, "genre", params.genre);
    }
    addQueryParam(qs, "rating", params.rating);
    addQueryParam(qs, "studio", params.studio);
    addQueryParam(qs, "network", params.network);
    addQueryParam(qs, "letter", params.letter);
    addQueryParam(qs, "view", params.view && params.view !== "all" ? params.view : null);
    if (params.unmatched) addQueryParam(qs, "unmatched", "true");
    addQueryParam(qs, "offset", params.offset);
    addQueryParam(qs, "limit", params.limit);
    return request<LibraryEntriesPage>(`/api/library-entries?${qs.toString()}`);
  },

  listLibraryFacets: (params: LibraryFacetsParams = {}) => {
    const qs = new URLSearchParams();
    addQueryParam(qs, "libraryId", params.libraryId);
    addQueryParam(qs, "mediaType", params.mediaType);
    addQueryParam(qs, "view", params.view && params.view !== "all" ? params.view : null);
    if (params.unmatched) addQueryParam(qs, "unmatched", "true");
    return request<LibraryFacets>(`/api/library-facets?${qs.toString()}`);
  },

  // Read-only Calendar events, grouped in the configured server timezone.
  listCalendarEvents: (params: CalendarEventsParams = {}) => {
    const qs = new URLSearchParams();
    addQueryParam(qs, "month", params.month);
    addQueryParam(qs, "libraryId", params.libraryId);
    addQueryParam(qs, "mediaType", params.mediaType);
    return request<CalendarEventsResponse>(`/api/calendar/events?${qs.toString()}`);
  },

  // Per-library hide state. Membership-gated on the server (Admin bypass).
  listHiddenItemIds: (libraryId: string) =>
    request<string[]>(`/api/virtual-libraries/${libraryId}/hidden-items`),
  hideItems: (libraryId: string, sourceItemIds: string[]) =>
    request<{ hidden: number }>(`/api/virtual-libraries/${libraryId}/hidden-items`, {
      method: "POST",
      body: JSON.stringify({ sourceItemIds }),
    }),
  unhideItems: (libraryId: string, sourceItemIds: string[]) =>
    request<{ removed: number; hidden: number }>(
      `/api/virtual-libraries/${libraryId}/hidden-items`,
      { method: "DELETE", body: JSON.stringify({ sourceItemIds }) }
    ),

  listMyLibraries: () =>
    request<(VirtualLibrary & { myRole: MembershipRole })[]>(
      "/api/virtual-libraries/mine"
    ),

  // admin-only
  listAllLibraries: () => request<VirtualLibrary[]>("/api/virtual-libraries"),
  createLibrary: (name: string, mediaType: MediaKind, outputPath: string) =>
    request<VirtualLibrary>("/api/virtual-libraries", {
      method: "POST",
      body: JSON.stringify({ name, mediaType, outputPath }),
    }),
  deleteLibrary: (id: string) =>
    request<void>(`/api/virtual-libraries/${id}`, { method: "DELETE" }),
  updateLibrary: (id: string, name: string, mediaType: MediaKind, outputPath: string) =>
    request<VirtualLibrary>(`/api/virtual-libraries/${id}`, {
      method: "PUT",
      body: JSON.stringify({ name, mediaType, outputPath }),
    }),

  listSourceRoots: () => request<SourceRoot[]>("/api/source-roots"),
  createSourceRoot: (name: string, path: string, mediaType: MediaKind) =>
    request<SourceRoot>("/api/source-roots", {
      method: "POST",
      body: JSON.stringify({ name, path, mediaType }),
    }),
  updateSourceRoot: (id: string, name: string, path: string, mediaType: MediaKind) =>
    request<SourceRoot>(`/api/source-roots/${id}`, {
      method: "PUT",
      body: JSON.stringify({ name, path, mediaType }),
    }),
  deleteSourceRoot: (id: string) =>
    request<void>(`/api/source-roots/${id}`, { method: "DELETE" }),

  listUsers: () => request<AppUser[]>("/api/users"),
  updateUser: (id: string, displayName: string, email: string) =>
    request<AppUser>(`/api/users/${id}`, {
      method: "PUT",
      body: JSON.stringify({ displayName, email }),
    }),
  setUserRole: (id: string, roles: string[]) =>
    request<{ id: string; roles: string[] }>(`/api/users/${id}/roles`, {
      method: "PUT",
      body: JSON.stringify({ roles }),
    }),
  changePassword: (currentPassword: string, newPassword: string) =>
    request<void>("/api/auth/change-password", {
      method: "POST",
      body: JSON.stringify({ currentPassword, newPassword }),
    }),
  adminSetPassword: (userId: string, newPassword: string) =>
    request<void>(`/api/users/${userId}/password`, {
      method: "PUT",
      body: JSON.stringify({ newPassword }),
    }),
  forgotPassword: (email: string) =>
    request<void>("/api/auth/forgot-password", {
      method: "POST",
      body: JSON.stringify({ email }),
    }),
  resetPassword: (email: string, token: string, newPassword: string) =>
    request<void>("/api/auth/reset-password", {
      method: "POST",
      body: JSON.stringify({ email, token, newPassword }),
    }),
  inviteUser: (email: string) =>
    request<{ id: string; email: string; message?: string }>("/api/users/invite", {
      method: "POST",
      body: JSON.stringify({ email }),
    }),
  deleteUser: (id: string) => request<void>(`/api/users/${id}`, { method: "DELETE" }),
  acceptInvite: (userId: string, token: string, password: string, displayName: string, alternateEmail?: string) =>
    request<{ message: string; email: string }>("/api/auth/accept-invite", {
      method: "POST",
      body: JSON.stringify({ userId, token, password, displayName, alternateEmail: alternateEmail || null }),
    }),
  confirmEmailChange: (userId: string, newEmail: string, token: string) =>
    request<void>("/api/auth/confirm-email-change", {
      method: "POST",
      body: JSON.stringify({ userId, newEmail, token }),
    }),
  listLibraryMembers: (libraryId: string) =>
    request<LibraryMember[]>(`/api/virtual-libraries/${libraryId}/members`),
  addMember: (libraryId: string, userId: string, role: MembershipRole) =>
    request<void>(`/api/virtual-libraries/${libraryId}/members`, {
      method: "POST",
      body: JSON.stringify({ userId, role }),
    }),
  updateMemberRole: (libraryId: string, userId: string, role: MembershipRole) =>
    request<void>(`/api/virtual-libraries/${libraryId}/members/${userId}`, {
      method: "PUT",
      body: JSON.stringify({ userId, role }),
    }),
  removeMember: (libraryId: string, userId: string) =>
    request<void>(`/api/virtual-libraries/${libraryId}/members/${userId}`, {
      method: "DELETE",
    }),

  browseFilesystem: (path?: string) =>
    request<BrowseResult>(`/api/filesystem/browse${path ? `?path=${encodeURIComponent(path)}` : ""}`),

  listPathMappings: () => request<PathMapping[]>("/api/path-mappings"),
  createPathMapping: (fromPrefix: string, toPrefix: string) =>
    request<PathMapping>("/api/path-mappings", {
      method: "POST",
      body: JSON.stringify({ fromPrefix, toPrefix }),
    }),
  deletePathMapping: (id: string) =>
    request<void>(`/api/path-mappings/${id}`, { method: "DELETE" }),

  scan: (libraryId?: string) =>
    request<{ scanned: number; created: number; updated: number; stopped: boolean }>(
      "/api/scan",
      { method: "POST", body: JSON.stringify({ libraryId: libraryId ?? null }) }
    ),
  pauseScan: () => request<void>("/api/scan/pause", { method: "POST" }),
  resumeScan: () => request<void>("/api/scan/resume", { method: "POST" }),
  stopScan: () => request<void>("/api/scan/stop", { method: "POST" }),
  reconcile: () =>
    request<{ created: string[]; removed: string[]; broken: string[]; unchanged: string[] }>(
      "/api/reconcile",
      { method: "POST" }
    ),

  // Admin-only Plex integration
  getPlexStatus: () => request<PlexStatus>("/api/plex/status"),
  startPlexOAuth: () =>
    request<{ pinId: number; authUrl: string }>("/api/plex/oauth/start", {
      method: "POST",
    }),
  pollPlexOAuth: (pinId: number) =>
    request<{
      status: "pending" | "linked";
      accountName?: string | null;
      serverName?: string | null;
      serverUrl?: string | null;
    }>(`/api/plex/oauth/poll/${pinId}`),
  updatePlexSettings: (settings: { serverUrl?: string; refreshOnChange?: boolean }) =>
    request<PlexStatus>("/api/plex/settings", {
      method: "PUT",
      body: JSON.stringify(settings),
    }),
  unlinkPlex: () => request<PlexStatus>("/api/plex/link", { method: "DELETE" }),

  getScheduleSettings: () =>
    request<{
      timeZone: string;
      isConfigured: boolean;
      nowInZone: string;
      zones: string[];
    }>(
      "/api/schedules/settings"
    ),

  setScheduleSettings: (timeZone: string) =>
    request<{ timeZone: string; message: string }>(
      "/api/schedules/settings",
      {
        method: "PUT",
        body: JSON.stringify({ timeZone }),
      }
    ),

  listSchedules: (libraryId: string) =>
    request<LibrarySchedule[]>(`/api/virtual-libraries/${libraryId}/schedules`),

  createSchedule: (libraryId: string, schedule: Partial<LibrarySchedule>) =>
    request<LibrarySchedule>(`/api/virtual-libraries/${libraryId}/schedules`, {
      method: "POST",
      body: JSON.stringify(schedule),
    }),

  updateSchedule: (
    libraryId: string,
    scheduleId: string,
    schedule: Partial<LibrarySchedule>
  ) =>
    request<LibrarySchedule>(
      `/api/virtual-libraries/${libraryId}/schedules/${scheduleId}`,
      {
        method: "PUT",
        body: JSON.stringify(schedule),
      }
    ),

  deleteSchedule: (libraryId: string, scheduleId: string) =>
    request<void>(
      `/api/virtual-libraries/${libraryId}/schedules/${scheduleId}`,
      { method: "DELETE" }
    ),

  getScheduleStatus: (libraryId: string) =>
    request<ScheduleStatus>(
      `/api/virtual-libraries/${libraryId}/schedules/status`
    ),

  evaluateSchedule: (libraryId: string) =>
    request<ScheduleStatus & { created: string[]; removed: string[]; broken: string[] }>(
      `/api/virtual-libraries/${libraryId}/schedules/evaluate`,
      { method: "POST" }
    ),

  approve: (libraryId: string, sourceItemId: string) =>
    request<void>(`/api/virtual-libraries/${libraryId}/approve`, {
      method: "POST",
      body: JSON.stringify({ sourceItemId }),
    }),

  unapprove: (libraryId: string, sourceItemId: string) =>
    request<void>(`/api/virtual-libraries/${libraryId}/unapprove`, {
      method: "POST",
      body: JSON.stringify({ sourceItemId }),
    }),

  getApprovedItemIds: (libraryId: string) =>
    request<string[]>(`/api/virtual-libraries/${libraryId}/approved-item-ids`),

  // Per-library automatic rules (Sprint 6 #1). Preview/apply deliberately do
  // not reconcile symlinks -- Sync is still required after Apply.
  listRuleFields: () => request<RuleField[]>("/api/library-fields"),
  listRules: (libraryId: string) =>
    request<LibraryRule[]>(`/api/virtual-libraries/${libraryId}/rules`),
  createRule: (libraryId: string, rule: LibraryRuleInput) =>
    request<LibraryRule>(`/api/virtual-libraries/${libraryId}/rules`, {
      method: "POST",
      body: JSON.stringify(rule),
    }),
  updateRule: (libraryId: string, rule: LibraryRuleInput) =>
    request<LibraryRule>(`/api/virtual-libraries/${libraryId}/rules`, {
      method: "PUT",
      body: JSON.stringify(rule),
    }),
  deleteRule: (libraryId: string, ruleId: string) =>
    request<{ removed: number }>(`/api/virtual-libraries/${libraryId}/rules`, {
      method: "DELETE",
      body: JSON.stringify({ id: ruleId }),
    }),
  previewRules: (libraryId: string, ruleIds?: string[]) =>
    request<RulePreview>(`/api/virtual-libraries/${libraryId}/rules/preview`, {
      method: "POST",
      body: JSON.stringify({ ruleIds: ruleIds ?? null }),
    }),
  applyRules: (libraryId: string, ruleIds?: string[]) =>
    request<RuleApplyResult>(`/api/virtual-libraries/${libraryId}/rules/apply`, {
      method: "POST",
      body: JSON.stringify({ ruleIds: ruleIds ?? null }),
    }),

  getItemDetails: (id: string) =>
    request<TitleDetails>(`/api/source-item-details?id=${encodeURIComponent(id)}`),

  backfillDetails: () =>
    request<{ processed: number; updated: number; failed: number; pending: number }>(
      '/api/backfill-genres',
      { method: 'POST' },
    ),

  // First-boot wizard
  bootstrap: (data: Record<string, unknown>) =>
    request<{ message: string }>('/api/bootstrap', {
      method: 'POST',
      body: JSON.stringify(data),
    }),
  isBootstrapped: () =>
    request<{ isBootstrapped: boolean }>('/api/bootstrapped'),

  // Activity + Logs (Sprint 5 #1/#2)
  listActivity: (params: {
    limit?: number;
    before?: string | null;
    category?: ActivityCategory | null;
    type?: string | null;
  } = {}) => {
    const q = new URLSearchParams();
    if (params.limit != null) q.set("limit", String(params.limit));
    if (params.before) q.set("before", params.before);
    if (params.category) q.set("category", params.category);
    if (params.type) q.set("type", params.type);
    const qs = q.toString();
    return request<ActivityPage>(`/api/activity${qs ? `?${qs}` : ""}`);
  },
  getScanStatus: () => request<ScanStatus>("/api/activity/scan-status"),

  listLogs: (after = 0, limit = 200) =>
    request<LogPage>(`/api/logs?after=${after}&limit=${limit}`),
  getLogConfig: () => request<LogConfig>("/api/logs/config"),
  setLogLevel: (level: string) =>
    request<LogConfig>("/api/logs/level", {
      method: "PUT",
      body: JSON.stringify({ level }),
    }),
  setLogConfig: (config: {
    retentionDays: number;
    fileSizeLimitMB: number;
    archiveEnabled: boolean;
  }) =>
    request<LogConfig>("/api/logs/config", {
      method: "PUT",
      body: JSON.stringify(config),
    }),
};
