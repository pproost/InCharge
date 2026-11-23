using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InCharge.Mobile.Services;

// Sync settings and state. The device token is not in here: it lives in SecureStorage.
public class CoachSyncState
{
    public bool Enabled { get; set; }
    public string ServerUrl { get; set; } = "";

    // Set on a 401; sync stays stopped until the URL or token changes.
    public bool AuthFailed { get; set; }

    public DateTimeOffset? LastSyncAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? LastErrorAt { get; set; }

    // Log entries waiting to be uploaded, at most one per id (the latest change wins).
    public List<OutboxItem> Outbox { get; set; } = new();

    // Opaque cursor from GET /plans, sent back as ?since=.
    public string? PlanCursor { get; set; }

    // Every planId ever imported. The app wins: a plan on this list is never imported again,
    // not even after it was deleted locally.
    public List<string> ImportedPlanIds { get; set; } = new();

    // Imported plans whose delivery still has to be confirmed to the server.
    public List<string> PendingDelivered { get; set; } = new();

    // Set once the whole local log was queued for upload (step 5, one-time history upload).
    public DateTimeOffset? HistoryQueuedAt { get; set; }

    public int LastImportedCount { get; set; }
    public DateTimeOffset? LastImportedAt { get; set; }
}

public class OutboxItem
{
    public string Id { get; set; } = "";
    public bool Deleted { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum ConnectionCheck
{
    Ok,
    NotConfigured,
    Unauthorized,
    Unreachable,
    Failed
}

// Talks to the HealthCoach server (docs/contract.md). Offline-first: every change to the activity
// log goes into an outbox; a sync round uploads it, imports new coach plans as Advanced sessions
// and confirms their delivery. Without URL and token the app behaves exactly as before.
public class CoachSyncService
{
    public const int ContractVersion = 1;
    private const string StateKey = "incharge.coachSync";
    private const string TokenKey = "incharge.coachToken";
    private const int BatchSize = 50;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ActivityLogService _log;
    private readonly ActivityTypeService _types;
    private readonly AdvancedSessionService _sessions;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateLock = new();
    private CoachSyncState? _state;
    private string? _token;
    private bool _tokenLoaded;
    private bool _roundHadError;

    public CoachSyncService(ActivityLogService log, ActivityTypeService types, AdvancedSessionService sessions)
    {
        _log = log;
        _types = types;
        _sessions = sessions;
        _log.EntryChanged += OnEntryChanged;
        Connectivity.Current.ConnectivityChanged += (_, e) =>
        {
            if (e.NetworkAccess == NetworkAccess.Internet)
            {
                SyncInBackground();
            }
        };
    }

    public event Action? StatusChanged;

    public bool IsSyncing { get; private set; }

    public CoachSyncState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state ??= LoadState();
            }
        }
    }

    public int PendingCount => State.Outbox.Count;

    public async Task<bool> HasTokenAsync() => !string.IsNullOrEmpty(await GetTokenAsync());

    public void SetEnabled(bool enabled)
    {
        var wasEnabled = State.Enabled;
        Update(s => s.Enabled = enabled);

        // While sync was off nothing was queued. Once the history went up, turning sync back on
        // re-sends the whole log, so the gap reaches the server too (uploads are idempotent).
        if (enabled && !wasEnabled && State.HistoryQueuedAt is not null)
        {
            QueueHistory();
            return;
        }
        SyncInBackground();
    }

    public void SetServerUrl(string url)
    {
        Update(s =>
        {
            s.ServerUrl = url.Trim().TrimEnd('/');
            s.AuthFailed = false;
        });
        SyncInBackground();
    }

    public async Task SetTokenAsync(string? token)
    {
        token = token?.Trim();
        if (string.IsNullOrEmpty(token))
        {
            SecureStorage.Default.Remove(TokenKey);
            _token = null;
        }
        else
        {
            await SecureStorage.Default.SetAsync(TokenKey, token);
            _token = token;
        }
        _tokenLoaded = true;
        Update(s => s.AuthFailed = false);
        SyncInBackground();
    }

    // Checks URL and token without changing anything: a GET of the plans, whose answer is ignored.
    public async Task<ConnectionCheck> TestConnectionAsync()
    {
        var token = await GetTokenAsync();
        if (!Uri.TryCreate(State.ServerUrl, UriKind.Absolute, out var baseUri) || string.IsNullOrEmpty(token))
        {
            return ConnectionCheck.NotConfigured;
        }

        try
        {
            using var request = Request(HttpMethod.Get, baseUri, "/api/incharge/plans", token);
            using var response = await Http.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                Update(s => s.AuthFailed = true);
                return ConnectionCheck.Unauthorized;
            }
            if (response.IsSuccessStatusCode)
            {
                Update(s => s.AuthFailed = false);
                SyncInBackground();
                return ConnectionCheck.Ok;
            }
            return ConnectionCheck.Failed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ConnectionCheck.Unreachable;
        }
    }

    // Queues every entry in the local log once, so activities from before sync was turned on
    // reach the server too. Uploads are idempotent on id, so entries already sent are harmless.
    public int QueueHistory()
    {
        var entries = _log.LoadAll().ToList();
        Update(s =>
        {
            foreach (var entry in entries.Where(e => !s.Outbox.Any(i => i.Id == e.Id)))
            {
                s.Outbox.Add(new OutboxItem { Id = entry.Id, UpdatedAt = entry.EffectiveUpdatedAt });
            }
            s.HistoryQueuedAt = DateTimeOffset.Now;
        });
        SyncInBackground();
        return entries.Count;
    }

    public void SyncInBackground() => _ = SyncAsync();

    // One sync round. Never throws: failures end up in LastError and are retried on the next
    // trigger (app start or resume, a new change, network back, "Sync now").
    public async Task SyncAsync()
    {
        if (!await _gate.WaitAsync(0))
        {
            return;
        }

        try
        {
            var token = await GetTokenAsync();
            if (!State.Enabled || State.AuthFailed || string.IsNullOrEmpty(token)
                || !Uri.TryCreate(State.ServerUrl, UriKind.Absolute, out var baseUri)
                || Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                return;
            }

            IsSyncing = true;
            _roundHadError = false;
            StatusChanged?.Invoke();

            var ok = await UploadOutboxAsync(baseUri, token)
                     && await ImportPlansAsync(baseUri, token)
                     && await ConfirmDeliveredAsync(baseUri, token);

            if (ok && !_roundHadError)
            {
                Update(s =>
                {
                    s.LastSyncAt = DateTimeOffset.Now;
                    s.LastError = null;
                    s.LastErrorAt = null;
                });
            }
        }
        catch (Exception ex)
        {
            RecordError($"Sync failed: {ex.Message}");
        }
        finally
        {
            IsSyncing = false;
            _gate.Release();
            StatusChanged?.Invoke();
        }
    }

    // ---------------------------------------------------------------- upload

    private async Task<bool> UploadOutboxAsync(Uri baseUri, string token)
    {
        while (true)
        {
            List<OutboxItem> batch;
            lock (_stateLock)
            {
                batch = State.Outbox.Take(BatchSize)
                    .Select(i => new OutboxItem { Id = i.Id, Deleted = i.Deleted, UpdatedAt = i.UpdatedAt })
                    .ToList();
            }
            if (batch.Count == 0)
            {
                return true;
            }

            var result = await UploadBatchAsync(baseUri, token, batch);
            if (result is not true)
            {
                // false = stop the round (offline, 401, server error); null = nothing accepted, go on.
                return result is null;
            }
        }
    }

    private async Task<bool?> UploadBatchAsync(Uri baseUri, string token, List<OutboxItem> batch)
    {
        var entries = _log.LoadAll().ToDictionary(e => e.Id);
        var sessions = new List<object>();
        var sent = new List<OutboxItem>();
        foreach (var item in batch)
        {
            if (item.Deleted)
            {
                sessions.Add(new TombstoneDto(item.Id, item.UpdatedAt, true));
                sent.Add(item);
            }
            else if (entries.TryGetValue(item.Id, out var entry))
            {
                sessions.Add(ToDto(entry));
                sent.Add(item);
            }
            else
            {
                // Gone locally without a tombstone (shouldn't happen): nothing left to send.
                RemoveFromOutbox([item]);
            }
        }

        if (sessions.Count == 0)
        {
            return true;
        }

        using var request = Request(HttpMethod.Post, baseUri, "/api/incharge/sessions", token,
            new { contractVersion = ContractVersion, sessions });
        using var response = await SendAsync(request);
        if (response is null)
        {
            return false;
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            // A 400 won't get better by retrying: drop the batch so it doesn't block the rest.
            var body = await response.Content.ReadAsStringAsync();
            RemoveFromOutbox(sent);
            RecordError($"Server rejected {sent.Count} session(s): {Shorten(body)}");
            return true;
        }

        if (!response.IsSuccessStatusCode)
        {
            RecordError($"Server error {(int)response.StatusCode} on upload; will retry.");
            return false;
        }

        var result = await response.Content.ReadFromJsonAsync<UploadResponseDto>(WireJson);
        var accepted = new HashSet<string>(result?.Accepted ?? []);
        RemoveFromOutbox(sent.Where(i => accepted.Contains(i.Id)));
        return accepted.Count > 0 ? true : null;
    }

    private ExecutedSessionDto ToDto(ActivityLogEntry entry)
    {
        var type = _types.Find(entry.TypeId);
        return new ExecutedSessionDto(
            entry.Id,
            entry.EffectiveUpdatedAt,
            entry.Name,
            entry.EffectiveSource,
            entry.SessionId,
            entry.PlanId,
            type is null ? null : new TypeDto(type.Name, type.Icon),
            entry.StartedAt,
            entry.CompletedAt,
            entry.FinishedEarly,
            entry.Structure,
            new TotalsDto(entry.ActiveSeconds, entry.WarmupSeconds, entry.MoveSeconds, entry.RestSeconds, entry.CooldownSeconds, entry.MoveBlocks),
            new PausesDto(entry.PauseCount, entry.PausedSeconds));
    }

    private void OnEntryChanged(string id, bool deleted, DateTimeOffset updatedAt)
    {
        // Without sync on nothing is queued, so turning it on later doesn't dump old history by
        // accident (that is a separate, explicit upload).
        if (!State.Enabled)
        {
            return;
        }

        Update(s =>
        {
            s.Outbox.RemoveAll(i => i.Id == id);
            s.Outbox.Add(new OutboxItem { Id = id, Deleted = deleted, UpdatedAt = updatedAt });
        });
        SyncInBackground();
    }

    // Only removes items that weren't changed again while the upload was in flight.
    private void RemoveFromOutbox(IEnumerable<OutboxItem> sent)
    {
        var done = sent.ToList();
        Update(s => s.Outbox.RemoveAll(i => done.Any(d => d.Id == i.Id && d.Deleted == i.Deleted && d.UpdatedAt == i.UpdatedAt)));
    }

    // ---------------------------------------------------------------- plans

    private async Task<bool> ImportPlansAsync(Uri baseUri, string token)
    {
        var cursor = State.PlanCursor;
        var path = "/api/incharge/plans" + (string.IsNullOrEmpty(cursor) ? "" : "?since=" + Uri.EscapeDataString(cursor));
        using var request = Request(HttpMethod.Get, baseUri, path, token);
        using var response = await SendAsync(request);
        if (response is null)
        {
            return false;
        }

        if (response.StatusCode == HttpStatusCode.BadRequest && !string.IsNullOrEmpty(cursor))
        {
            // The server no longer understands our cursor: start over. Already imported plans are
            // skipped by planId, so that is safe.
            Update(s => s.PlanCursor = null);
            return true;
        }

        if (!response.IsSuccessStatusCode)
        {
            RecordError($"Server error {(int)response.StatusCode} while fetching plans; will retry.");
            return false;
        }

        var result = await response.Content.ReadFromJsonAsync<PlansResponseDto>(WireJson);
        var plans = result?.Plans ?? [];
        var known = new HashSet<string>(State.ImportedPlanIds);
        var fresh = plans
            .Where(p => !string.IsNullOrWhiteSpace(p.PlanId) && !known.Contains(p.PlanId))
            .GroupBy(p => p.PlanId)
            .Select(g => g.Last())
            .ToList();

        var imported = new List<string>();
        var skipped = new List<string>();
        if (fresh.Count > 0)
        {
            // The session and type lists are shared with the UI, which lives on the main thread.
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var sessions = new List<AdvancedSession>();
                foreach (var plan in fresh)
                {
                    if (_sessions.HasPlan(plan.PlanId))
                    {
                        imported.Add(plan.PlanId);
                        continue;
                    }

                    var session = ToSession(plan);
                    if (session is null)
                    {
                        skipped.Add(plan.Name ?? plan.PlanId);
                        continue;
                    }
                    sessions.Add(session);
                    imported.Add(plan.PlanId);
                }

                if (sessions.Count > 0)
                {
                    _sessions.AddImported(sessions);
                }
            });
        }

        var newCount = imported.Count;
        Update(s =>
        {
            foreach (var id in imported.Where(id => !s.ImportedPlanIds.Contains(id)))
            {
                s.ImportedPlanIds.Add(id);
            }

            // Everything the server still offers and we have is confirmed (again): that is idempotent.
            foreach (var id in plans.Select(p => p.PlanId).Where(id => s.ImportedPlanIds.Contains(id) && !s.PendingDelivered.Contains(id)))
            {
                s.PendingDelivered.Add(id);
            }

            if (!string.IsNullOrEmpty(result?.Cursor))
            {
                s.PlanCursor = result.Cursor;
            }

            if (newCount > 0)
            {
                s.LastImportedCount = newCount;
                s.LastImportedAt = DateTimeOffset.Now;
            }
        });

        if (skipped.Count > 0)
        {
            RecordError($"Skipped plan(s) without blocks: {string.Join(", ", skipped)}");
        }
        return true;
    }

    // Turns a coach plan into an Advanced session (docs/contract.md, "On import").
    private AdvancedSession? ToSession(PlanDto plan)
    {
        var groups = (plan.Structure?.Groups ?? [])
            .Where(g => g.Blocks is { Count: > 0 })
            .Select(g => new IntervalGroup
            {
                RepeatCount = Math.Max(1, g.Repeat),
                Blocks = g.Blocks.Select(b => new IntervalBlock
                {
                    IsMove = !string.Equals(b.Kind, StructureBlock.Rest, StringComparison.OrdinalIgnoreCase),
                    DurationSeconds = Math.Max(1, b.Seconds),
                    Label = IntervalBlock.CleanLabel(b.Label)
                }).ToList()
            }).ToList();

        if (groups.Count == 0)
        {
            return null;
        }

        var warmup = plan.Structure?.WarmupSeconds is > 0 ? plan.Structure.WarmupSeconds : null;
        var cooldown = plan.Structure?.CooldownSeconds is > 0 ? plan.Structure.CooldownSeconds : null;
        var durations = groups.SelectMany(g => g.Blocks).Select(b => b.DurationSeconds)
            .Concat(new[] { warmup, cooldown }.OfType<int>());

        return new AdvancedSession
        {
            Name = string.IsNullOrWhiteSpace(plan.Name) ? "Coach session" : plan.Name.Trim(),
            Source = AdvancedSession.CoachSource,
            PlanId = plan.PlanId,
            PlannedDate = plan.PlannedDate,
            Note = string.IsNullOrWhiteSpace(plan.Note) ? null : plan.Note.Trim(),
            TypeId = string.IsNullOrWhiteSpace(plan.Type?.Name) ? null : _types.FindOrCreate(plan.Type.Name, plan.Type.Icon).Id,
            UseMinutes = durations.All(d => d % 60 == 0),
            WarmupEnabled = warmup is not null,
            WarmupDurationSeconds = warmup ?? 300,
            CooldownEnabled = cooldown is not null,
            CooldownDurationSeconds = cooldown ?? 300,
            Groups = groups
        };
    }

    private async Task<bool> ConfirmDeliveredAsync(Uri baseUri, string token)
    {
        foreach (var planId in State.PendingDelivered.ToList())
        {
            using var request = Request(HttpMethod.Post, baseUri, $"/api/incharge/plans/{Uri.EscapeDataString(planId)}/delivered", token,
                new { contractVersion = ContractVersion, deliveredAt = DateTimeOffset.Now });
            using var response = await SendAsync(request);
            if (response is null)
            {
                return false;
            }

            if (!response.IsSuccessStatusCode && (int)response.StatusCode >= 500)
            {
                RecordError($"Server error {(int)response.StatusCode} confirming a plan; will retry.");
                return false;
            }

            // 204, or a 4xx that won't get better by retrying.
            Update(s => s.PendingDelivered.Remove(planId));
        }
        return true;
    }

    // ---------------------------------------------------------------- plumbing

    private static HttpRequestMessage Request(HttpMethod method, Uri baseUri, string path, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, new Uri(baseUri, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: WireJson);
        }
        return request;
    }

    // Null when the server is unreachable or rejects the token (both recorded); otherwise the response.
    private async Task<HttpResponseMessage?> SendAsync(HttpRequestMessage request)
    {
        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            RecordError("Server not reachable; will retry.");
            return null;
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            Update(s => s.AuthFailed = true);
            RecordError("Token rejected (401). Sync is paused until you change the URL or token.");
            return null;
        }
        return response;
    }

    private void RecordError(string message)
    {
        _roundHadError = true;
        Update(s =>
        {
            s.LastError = message;
            s.LastErrorAt = DateTimeOffset.Now;
        });
    }

    private void Update(Action<CoachSyncState> change)
    {
        lock (_stateLock)
        {
            var state = _state ??= LoadState();
            change(state);
            Preferences.Default.Set(StateKey, JsonSerializer.Serialize(state));
        }
        StatusChanged?.Invoke();
    }

    private static CoachSyncState LoadState()
    {
        var json = Preferences.Default.Get(StateKey, string.Empty);
        try
        {
            return string.IsNullOrEmpty(json)
                ? new CoachSyncState()
                : JsonSerializer.Deserialize<CoachSyncState>(json) ?? new CoachSyncState();
        }
        catch (JsonException)
        {
            return new CoachSyncState();
        }
    }

    private async Task<string?> GetTokenAsync()
    {
        if (!_tokenLoaded)
        {
            try
            {
                _token = await SecureStorage.Default.GetAsync(TokenKey);
            }
            catch
            {
                // Keystore trouble (e.g. after a backup restore): treat as no token.
                _token = null;
            }
            _tokenLoaded = true;
        }
        return _token;
    }

    private static string Shorten(string text) => text.Length <= 200 ? text : text[..200] + "…";

    private sealed record TypeDto(string Name, string? Icon);

    private sealed record TotalsDto(int ActiveSeconds, int WarmupSeconds, int MoveSeconds, int RestSeconds, int CooldownSeconds, int MoveBlocks);

    private sealed record PausesDto(int Count, int Seconds);

    private sealed record TombstoneDto(string Id, DateTimeOffset UpdatedAt, bool Deleted);

    private sealed record ExecutedSessionDto(
        string Id,
        DateTimeOffset UpdatedAt,
        string Name,
        string Source,
        string? SessionId,
        string? PlanId,
        TypeDto? Type,
        DateTimeOffset StartedAt,
        DateTimeOffset CompletedAt,
        bool FinishedEarly,
        SessionStructure? Structure,
        TotalsDto Totals,
        PausesDto Pauses);

    private sealed record UploadResponseDto(List<string>? Accepted);

    private sealed record PlansResponseDto(int ContractVersion, string? Cursor, List<PlanDto>? Plans);

    private sealed record PlanDto(string PlanId, DateOnly? PlannedDate, string? Name, string? Note, TypeDto? Type, SessionStructure? Structure);
}
