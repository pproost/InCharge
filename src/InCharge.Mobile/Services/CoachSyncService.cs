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

// Uploads executed sessions to the HealthCoach server (docs/contract.md). Offline-first: every
// change to the activity log goes into an outbox, which is sent whenever sync is configured and
// the network is there. Without URL and token the app behaves exactly as before.
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
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateLock = new();
    private CoachSyncState? _state;
    private string? _token;
    private bool _tokenLoaded;

    public CoachSyncService(ActivityLogService log, ActivityTypeService types)
    {
        _log = log;
        _types = types;
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

    public async Task<bool> IsConfiguredAsync() =>
        State.Enabled && Uri.TryCreate(State.ServerUrl, UriKind.Absolute, out _) && await HasTokenAsync();

    public void SetEnabled(bool enabled)
    {
        Update(s => s.Enabled = enabled);
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

    // Calls the plans endpoint (read-only) to check URL and token without uploading anything.
    public async Task<ConnectionCheck> TestConnectionAsync()
    {
        var token = await GetTokenAsync();
        if (!Uri.TryCreate(State.ServerUrl, UriKind.Absolute, out var baseUri) || string.IsNullOrEmpty(token))
        {
            return ConnectionCheck.NotConfigured;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "/api/incharge/plans"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
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

    public void SyncInBackground() => _ = SyncAsync();

    // Sends the outbox in batches. Never throws: failures end up in LastError and are retried
    // on the next trigger (app start, a new change, network back, "Sync now").
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
                || PendingCount == 0)
            {
                return;
            }

            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                return;
            }

            IsSyncing = true;
            StatusChanged?.Invoke();

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
                    break;
                }

                if (!await UploadBatchAsync(baseUri, token, batch))
                {
                    break;
                }
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

    private async Task<bool> UploadBatchAsync(Uri baseUri, string token, List<OutboxItem> batch)
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

        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "/api/incharge/sessions"))
            {
                Content = JsonContent.Create(new { contractVersion = ContractVersion, sessions }, options: WireJson)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            response = await Http.SendAsync(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            RecordError("Server not reachable; will retry.");
            return false;
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                Update(s => s.AuthFailed = true);
                RecordError("Token rejected (401). Sync is paused until you change the URL or token.");
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
                RecordError($"Server error {(int)response.StatusCode}; will retry.");
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<UploadResponseDto>(WireJson);
            var accepted = new HashSet<string>(result?.Accepted ?? []);
            RemoveFromOutbox(sent.Where(i => accepted.Contains(i.Id)));
            Update(s =>
            {
                s.LastSyncAt = DateTimeOffset.Now;
                s.LastError = null;
                s.LastErrorAt = null;
            });

            // Nothing accepted means nothing will change on a retry right now.
            return accepted.Count > 0;
        }
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
        // Without sync configured nothing is queued, so turning it on later doesn't dump old
        // history by accident (that is a separate, explicit upload).
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

    private void RecordError(string message) => Update(s =>
    {
        s.LastError = message;
        s.LastErrorAt = DateTimeOffset.Now;
    });

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
}
