using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SocketIOClient;

namespace Client.Main.Networking
{
    /// <summary>
    /// HTTP and Socket.IO transport for the existing MU Mobile web backend.
    /// This is intentionally separate from the classic Season 6 TCP transport:
    /// the Render service exposes HTTP/Socket.IO, not a native MU ConnectServer.
    /// </summary>
    public sealed class MobileBackendClient : IAsyncDisposable
    {
        private readonly HttpClient _http;
        private readonly Uri _baseUri;
        private SocketIO? _socket;
        private string? _token;

        public event Action<string, string>? ServerEvent;

        public MobileBackendClient(string baseUrl, HttpMessageHandler? handler = null)
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw new ArgumentException("Backend URL must be an absolute HTTP(S) URL.", nameof(baseUrl));
            }

            _baseUri = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
            _http = handler is null ? new HttpClient() : new HttpClient(handler);
            _http.BaseAddress = _baseUri;
            _http.Timeout = TimeSpan.FromSeconds(20);
        }

        public async Task<AuthResponse> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
        {
            var response = await _http.PostAsJsonAsync("api/v1/auth/login",
                new CredentialsRequest(username, password), cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
            var result = await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The backend returned an empty login response.");
            _token = result.Token;
            return result;
        }

        public async Task<AuthResponse> RegisterAsync(string username, string password, CancellationToken cancellationToken = default)
        {
            var response = await _http.PostAsJsonAsync("api/v1/auth/register",
                new CredentialsRequest(username, password), cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
            var result = await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The backend returned an empty registration response.");
            _token = result.Token;
            return result;
        }

        public async Task<IReadOnlyList<BackendCharacter>> GetCharactersAsync(CancellationToken cancellationToken = default)
        {
            using var request = CreateAuthenticatedRequest(HttpMethod.Get, "api/v1/characters");
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
            var result = await response.Content.ReadFromJsonAsync<CharacterListResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            return result?.Characters ?? Array.Empty<BackendCharacter>();
        }

        public async Task<BackendCharacter> CreateCharacterAsync(string name, string characterClass, CancellationToken cancellationToken = default)
        {
            using var request = CreateAuthenticatedRequest(HttpMethod.Post, "api/v1/characters");
            request.Content = JsonContent.Create(new CreateCharacterRequest(name, characterClass));
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
            var result = await response.Content.ReadFromJsonAsync<CreateCharacterResponse>(cancellationToken: cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The backend returned an empty character response.");
            return result.Character;
        }

        public async Task<IReadOnlyList<BackendMonster>> GetMonstersAsync(string map = "lorencia", CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(map) || map.Contains('/') || map.Contains('\\\\'))
                throw new ArgumentException("Map name is invalid.", nameof(map));

            using var request = CreateAuthenticatedRequest(HttpMethod.Get, $"api/v1/world/{Uri.EscapeDataString(map)}/monsters");
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
            var result = await response.Content.ReadFromJsonAsync<MonsterListResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            return result?.Monsters ?? Array.Empty<BackendMonster>();
        }

        public async Task ConnectRealtimeAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_token))
                throw new InvalidOperationException("Log in before connecting to the game socket.");

            await DisconnectRealtimeAsync().ConfigureAwait(false);
            _socket = new SocketIO(_baseUri, new SocketIOOptions
            {
                Auth = new { token = _token },
                Reconnection = true,
                ReconnectionAttempts = 5,
                ConnectionTimeout = TimeSpan.FromSeconds(15)
            });

            foreach (var eventName in new[]
            {
                "session:ready", "character:selected", "player:entered", "player:left",
                "player:moved", "monster:updated", "character:progression", "server:error"
            })
            {
                var capturedName = eventName;
                _socket.On(capturedName, response => ServerEvent?.Invoke(capturedName, response.ToString()));
            }

            cancellationToken.ThrowIfCancellationRequested();
            await _socket.ConnectAsync().ConfigureAwait(false);
        }

        public Task SelectCharacterAsync(string characterId) =>
            EmitRequiredAsync("character:select", new { characterId });

        public Task MoveAsync(double x, double y) =>
            EmitRequiredAsync("player:move", new { x, y });

        public Task AttackAsync(string monsterId) =>
            EmitRequiredAsync("combat:attack", new { monsterId });

        public async Task DisconnectRealtimeAsync()
        {
            var socket = _socket;
            _socket = null;
            if (socket is null) return;
            try { await socket.DisconnectAsync().ConfigureAwait(false); }
            finally { socket.Dispose(); }
        }

        private Task EmitRequiredAsync(string eventName, object payload)
        {
            if (_socket is null || !_socket.Connected)
                throw new InvalidOperationException("The game socket is not connected.");
            return _socket.EmitAsync(eventName, payload);
        }

        private HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string path)
        {
            if (string.IsNullOrWhiteSpace(_token))
                throw new InvalidOperationException("Log in before requesting account data.");
            var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            return request;
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            if (response.IsSuccessStatusCode) return;
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"MU Mobile backend returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). {body}",
                null, response.StatusCode);
        }

        public async ValueTask DisposeAsync()
        {
            await DisconnectRealtimeAsync().ConfigureAwait(false);
            _http.Dispose();
        }

        private sealed record CredentialsRequest(
            [property: JsonPropertyName("username")] string Username,
            [property: JsonPropertyName("password")] string Password);

        private sealed record CharacterListResponse(
            [property: JsonPropertyName("characters")] List<BackendCharacter> Characters);

        private sealed record CreateCharacterRequest(
            [property: JsonPropertyName("name")] string Name,
            [property: JsonPropertyName("class")] string CharacterClass);

        private sealed record CreateCharacterResponse(
            [property: JsonPropertyName("character")] BackendCharacter Character);

        private sealed record MonsterListResponse(
            [property: JsonPropertyName("map")] string Map,
            [property: JsonPropertyName("monsters")] List<BackendMonster> Monsters);
    }

    public sealed class AuthResponse
    {
        [JsonPropertyName("player")] public BackendPlayer Player { get; init; } = new();
        [JsonPropertyName("token")] public string Token { get; init; } = string.Empty;
    }

    public sealed class BackendPlayer
    {
        [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
        [JsonPropertyName("username")] public string Username { get; init; } = string.Empty;
    }

    public sealed class BackendCharacter
    {
        [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
        [JsonPropertyName("class")] public string Class { get; init; } = "dark_knight";
        [JsonPropertyName("level")] public int Level { get; init; }
        [JsonPropertyName("experience")] public long Experience { get; init; }
        [JsonPropertyName("map")] public string Map { get; init; } = "lorencia";
        [JsonPropertyName("position_x")] public double PositionX { get; init; }
        [JsonPropertyName("position_y")] public double PositionY { get; init; }
    }
}
public sealed class BackendMonster
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; init; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("x")] public double X { get; init; }
    [JsonPropertyName("y")] public double Y { get; init; }
    [JsonPropertyName("currentHp")] public int CurrentHp { get; init; }
    [JsonPropertyName("maxHp")] public int MaxHp { get; init; }
    [JsonPropertyName("respawnAt")] public DateTimeOffset? RespawnAt { get; init; }
}
