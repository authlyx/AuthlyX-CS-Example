// AuthlyX C# SDK Version 3.0 BETA
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System;

using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace AuthlyXSDK
{
    public sealed class AuthlyX : IDisposable
    {
        private UserData currentUserData = new UserData();
        private JObject? identitySnapshot;
        public UserData user => HasAccess ? currentUserData : UserData.Empty;
        public UserData userData => user;
        private readonly ClientOptions options;
        private readonly IReadOnlyDictionary<string, string> responseKeys, offlineKeys, scriptKeys;
        private readonly HttpClient http;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private CancellationTokenSource? heartbeat;
        private readonly byte[] seed = Array.Empty<byte>();
        private readonly AuthlyXException? storageFailure;
        private readonly string storageName, identityName, publicPem, installationHash;
        private string? token, sessionId, appId, identityId, slot;
        private JObject? offline, credential;
        private bool offlineSession;
        private int updateOffered;
        private long serverEpoch, anchorTicks, anchorWall, deadline;
        private double heartbeatSeconds = 600, jitterSeconds = 15;
        private bool disposed, loggingOut;
        public event Action<ConnectionState>? ConnectionChanged;
        public event Action<AuthlyXException>? SessionInvalidated;
        public ConnectionState Connection { get; private set; } = ConnectionState.Disconnected;

#if NET8_0_OR_GREATER
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
        public AuthlyX(string appKey, string version, TimeSpan? requestTimeout = null,
                       int retryLimit = 2, bool debug = false, string? api = null,
                       string heartbeat = "server", bool updatePrompt = true)
            : this(configured_options(appKey, version, requestTimeout, retryLimit, debug, api,
                                      heartbeat, updatePrompt))
        {
        }

        public static string systemId()
        {
            const string windowsAssembly =
                "System.Security.Principal.WindowsIdentity, System.Security.Principal.Windows";
            const string frameworkAssembly = "System.Security.Principal.WindowsIdentity, mscorlib";
            Type? identityType =
                Type.GetType(windowsAssembly, false) ?? Type.GetType(frameworkAssembly, false);

            if (identityType == null)
            {
                throw new PlatformNotSupportedException(
                    "Supply a platform system ID through ClientOptions.");
            }

            var getCurrent = identityType.GetMethod("GetCurrent", Type.EmptyTypes);
            var identity = getCurrent?.Invoke(null, null);

            try
            {
                var user = identityType.GetProperty("User")?.GetValue(identity, null);
                var sid = user?.GetType().GetProperty("Value")?.GetValue(user, null) as string;

                if (sid == null || sid.Length == 0)
                {
                    throw new PlatformNotSupportedException(
                        "The current Windows account has no system ID.");
                }

                return sid;
            }
            finally
            {
                (identity as IDisposable)?.Dispose();
            }
        }

#if NET8_0_OR_GREATER
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
        private static ClientOptions configured_options(string appKey, string version,
                                                        TimeSpan? requestTimeout, int retryLimit,
                                                        bool debug, string? api, string heartbeat,
                                                        bool updatePrompt)
        {
            if (heartbeat != "server" && heartbeat != "manual")
            {
                throw new ArgumentException("Use server or manual heartbeat mode.",
                                            nameof(heartbeat));
            }

            var settings = defaultOptions(appKey, version);
            settings.RequestBudget = requestTimeout ?? TimeSpan.FromSeconds(10);
            settings.RetryLimit = retryLimit;
            settings.Debug = debug;
            settings.AutomaticHeartbeat = heartbeat == "server";
            settings.UpdatePrompt = updatePrompt;
            if (api != null)
            {
                settings.Endpoint = new Uri(api);
            }

            return settings;
        }

#if NET8_0_OR_GREATER
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
        public static ClientOptions defaultOptions(string appKey,
                                                   string version) => new ClientOptions {
            AppKey = appKey,
            Version = version,
            SystemId = systemId(),
            Endpoint = new Uri("https://api.authlyx.dev"),
            AllowLocalHttp = true,
            Storage = new WindowsProtectedStore(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AuthlyX", "v3")),
            ResponseKeys = new Dictionary<
                string,
                string> { ["2318305346828e7b61e0447bfc4b3daac50dcda1b425814120ee3d6947609052"] =
                              "-----BEGIN PUBLIC KEY-----\nMCowBQYDK2VwAyEAaXXIKXRn0uPJ16wc4SX1GLArAGTAPeVhreGxp4f9/6Q=\n-----END PUBLIC KEY-----\n" },
            OfflineKeys = new Dictionary<
                string,
                string> { ["d3ea94e9f098eece181306689713c9f4d984c0321948fbd0007ef471bcf5b7d2"] =
                              "-----BEGIN PUBLIC KEY-----\nMCowBQYDK2VwAyEA7d2GrJ8SIUFor6bZuXo0NCX2k9SkHVz5PxOZDut3ydk=\n-----END PUBLIC KEY-----\n" },
            ScriptKeys = new Dictionary<
                string, string> { ["5aec455eb0b10697"] =
                                      "-----BEGIN PUBLIC KEY-----\nMCowBQYDK2VwAyEA0zpUhvFdbX95R1Q0+105MZsJr0IJ/yFTNxSiUwqfFH0=\n-----END PUBLIC KEY-----\n" }
        };

        public AuthlyX(ClientOptions options)
        {
            if (options.Storage == null || options.ResponseKeys.Count == 0 ||
                string.IsNullOrWhiteSpace(options.AppKey) ||
                string.IsNullOrWhiteSpace(options.Version) ||
                string.IsNullOrWhiteSpace(options.SystemId))
            {
                throw new ArgumentException("Provide AppKey, version, system ID, protected " +
                                            "storage and trusted response keys.");
            }

            if (options.Endpoint.AbsolutePath != "/" || options.Endpoint.Query != "" ||
                options.Endpoint.UserInfo != "" || options.Endpoint.Fragment != "" ||
                (options.Endpoint.Scheme != "https" &&
                 !(options.AllowLocalHttp && options.Endpoint.Scheme == "http" &&
                   options.Endpoint.IsLoopback)))
            {
                throw new ArgumentException("Use an HTTPS API origin. HTTP is allowed " +
                                            "only for explicit loopback testing.");
            }

            if (options.RequestBudget.TotalMilliseconds < 100 ||
                options.RequestBudget.TotalMilliseconds > 60000)
            {
                throw new ArgumentException(
                    "Request budget must be between 100 ms and 60 seconds.");
            }

            if (options.RetryLimit < 0 || options.RetryLimit > 5)
            {
                throw new ArgumentOutOfRangeException(nameof(options.RetryLimit));
            }

            this.options = new ClientOptions { AppKey = options.AppKey,
                                               Version = options.Version,
                                               SystemId = options.SystemId,
                                               BuildHash = options.BuildHash,
                                               Endpoint = options.Endpoint,
                                               Storage = options.Storage,
                                               RequestBudget = options.RequestBudget,
                                               AutomaticHeartbeat = options.AutomaticHeartbeat,
                                               RetryLimit = options.RetryLimit,
                                               Debug = options.Debug };
            responseKeys = CopyKeys(options.ResponseKeys);
            offlineKeys = CopyKeys(options.OfflineKeys);
            scriptKeys = CopyKeys(options.ScriptKeys);
            storageName = "authlyx.v3." + Protocol.Hash(options.Endpoint.AbsoluteUri + "\n" +
                                                        options.AppKey + "\n" + options.SystemId);
            identityName = "authlyx.v3.installation." +
                           Protocol.Hash(options.Endpoint.AbsoluteUri + "\n" + options.SystemId);
            try
            {
                var saved = options.Storage.Load(identityName);
                seed = saved == null ? Protocol.Random(32) : Convert.FromBase64String(saved);
                if (seed.Length != 32)
                {
                    throw new Exception();
                }

                if (saved == null)
                {
                    options.Storage.Save(identityName, Convert.ToBase64String(seed));
                }

                publicPem = Protocol.PublicPem(seed);
                installationHash = Protocol.KeyId(publicPem);
            }
            catch
            {
                Array.Clear(seed, 0, seed.Length);
                seed = Array.Empty<byte>();
                publicPem = "";
                installationHash = "";
                offline = null;
                appId = null;
                identityId = null;
                serverEpoch = 0;
                storageFailure = new AuthlyXException(
                    "STORAGE_INVALID", "Protected SDK state could not be read or saved.",
                    FailureKind.Storage);
            }
            http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) {
                Timeout = Timeout.InfiniteTimeSpan
            };
        }
        private static IReadOnlyDictionary<string, string> CopyKeys(
            IReadOnlyDictionary<string, string> keys)
        {
            var copy = new Dictionary<string, string>();
            foreach (var pair in keys)
            {
                copy.Add(pair.Key, pair.Value);
            }

            return copy;
        }
        private static long WallNow() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        private void Anchor(long serverTime)
        {
            serverEpoch = serverTime;
            anchorTicks = Stopwatch.GetTimestamp();
            anchorWall = WallNow();
        }
        private long TrustedNow() => serverEpoch == 0
                                         ? WallNow()
                                         : serverEpoch +
                                               (long)((Stopwatch.GetTimestamp() - anchorTicks) *
                                                      1000.0 / Stopwatch.Frequency);
        private void SetConnection(ConnectionState state)
        {
            if (Connection == state)
            {
                return;
            }

            Connection = state;
            try
            {
                ConnectionChanged?.Invoke(state);
            }
            catch
            {
            }
        }
        private static string SlotOf(string type, string username, string license)
        {
            string id = type == "user"    ? (username ?? "").Trim().ToLowerInvariant()
                        : type == "license" ? (license ?? "").Trim()
                                            : "";
            return Protocol.Hash(type + "\n" + id);
        }
        private static string Verifier(string secret, byte[] salt)
        {
            var generator = new Org.BouncyCastle.Crypto.Generators.Pkcs5S2ParametersGenerator(
                new Org.BouncyCastle.Crypto.Digests.Sha256Digest());
            generator.Init(Encoding.UTF8.GetBytes(secret ?? ""), salt, 120000);
            return Convert.ToBase64String(
                ((KeyParameter)generator.GenerateDerivedMacParameters(256)).GetKey());
        }
        private string RecordName(string value) => storageName + ".offline." + value;
        private List<string> Slots()
        {
            try
            {
                var saved = options.Storage.Load(storageName + ".offline");
                if (saved != null && Protocol.Parse(saved)["slots"] is JArray list)
                {
                    return list.Select(item => (string?)item).Where(item => item != null)
                        .Select(item => item!).ToList();
                }
            }
            catch
            {
            }

            return new List<string>();
        }
        private void SaveSlots(List<string> list)
        {
            if (list.Count == 0)
            {
                options.Storage.Delete(storageName + ".offline");
            }
            else
            {
                options.Storage.Save(storageName + ".offline",
                                     new JObject { ["slots"] = new JArray(list) }.ToString(
                                         Newtonsoft.Json.Formatting.None));
            }
        }
        private JObject? LoadRecord(string value)
        {
            try
            {
                var saved = options.Storage.Load(RecordName(value));
                return saved == null ? null : Protocol.Parse(saved);
            }
            catch
            {
                return null;
            }
        }
        private void Forget(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            options.Storage.Delete(RecordName(value!));
            SaveSlots(Slots().Where(item => item != value).ToList());
        }
        private void ClearOffline()
        {
            offline = null;
            offlineSession = false;
            Forget(slot);
        }
        private void Invalidate(AuthlyXException error)
        {
            token = null;
            currentUserData = UserData.Empty;
            deadline = 0;
            StopHeartbeat();
            try
            {
                ClearOffline();
            }
            catch
            {
                offline = null;
                offlineSession = false;
            }
            try
            {
                SessionInvalidated?.Invoke(error);
            }
            catch
            {
            }
        }
        private static bool Revoked(string code) =>
            code == "SESSION_EXPIRED" || code == "ACCESS_REVOKED" || code == "APP_PAUSED" ||
            code == "ACCESS_EXPIRED" || code == "ACCESS_DENIED" || code == "IP_BLACKLISTED" ||
            code == "IP_NOT_WHITELISTED" || code == "SID_BLACKLISTED" ||
            code == "SID_NOT_WHITELISTED" || code == "VERSION_NOT_ALLOWED" ||
            code == "HASH_NOT_ALLOWED" || code == "COUNTRY_BLOCKED" || code == "VPN_NOT_ALLOWED" ||
            code == "DEVICE_NOT_ALLOWED" || code == "INVALID_CREDENTIALS" ||
            code == "APP_DISABLED" ||
            code == "IP_BLOCKED" ||
            code == "SID_BLOCKED" ||
            code == "UPDATE_REQUIRED" ||
            code == "VERSION_MISMATCH" ||
            code == "INVALID_HASH" ||
            code == "VPN_BLOCKED" ||
            code == "USER_BANNED" ||
            code == "USER_SUSPENDED" ||
            code == "USER_EXPIRED" ||
            code == "LICENSE_BANNED" ||
            code == "LICENSE_PAUSED" ||
            code == "LICENSE_EXPIRED" ||
            code == "MOTHERBOARD_BANNED" ||
            code == "MOTHERBOARD_PAUSED" ||
            code == "MOTHERBOARD_EXPIRED" ||
            code == "PROCESSOR_BANNED" ||
            code == "PROCESSOR_PAUSED" ||
            code == "PROCESSOR_EXPIRED";

        private async Task<JObject> Send(string path, JObject data, bool authenticated,
                                         bool retryable, CancellationToken cancel)
        {
            if (storageFailure != null)
            {
                throw storageFailure;
            }

            if (authenticated && token == null)
            {
                throw new AuthlyXException("NOT_SIGNED_IN", "Sign in before using this feature.",
                                           FailureKind.Server);
            }

            string body = Protocol.Json(data);
            var timer = Stopwatch.StartNew();
            using (var budget =
                       CancellationTokenSource.CreateLinkedTokenSource(cancel, lifetime.Token))
            {
                budget.CancelAfter(options.RequestBudget);
                for (int attempt = 0;; attempt++)
                {
                    string nonce = Protocol.Hex(Protocol.Random(24)),
                           timestamp = TrustedNow().ToString(CultureInfo.InvariantCulture);
                    try
                    {
                        using (var request = new HttpRequestMessage(
                                   HttpMethod.Post, new Uri(options.Endpoint, path)))
                        {
                            request.Content =
                                new StringContent(body, Encoding.UTF8, "application/json");
                            request.Headers.Add("X-AuthlyX-Nonce", nonce);
                            if (authenticated)
                            {
                                request.Headers.Authorization =
                                    new AuthenticationHeaderValue("Bearer", token);
                                request.Headers.Add("X-AuthlyX-Timestamp", timestamp);
                                request.Headers.Add(
                                    "X-AuthlyX-Signature",
                                    Protocol.Sign(seed,
                                                  Protocol.Json(Protocol.RequestProof(
                                                      path, body, nonce, timestamp, token!))));
                            }
                            using (var response =
                                       await http
                                           .SendAsync(request,
                                                      HttpCompletionOption.ResponseHeadersRead,
                                                      budget.Token)
                                           .ConfigureAwait(false))
                            {
                                string text;
                                using (
                                    var stream =
                                        await response.Content.ReadAsStreamAsync().ConfigureAwait(
                                            false)) using (var bytes = new MemoryStream())
                                {
                                    var buffer = new byte[8192];
                                    int count;
                                    while ((count = await stream
                                                        .ReadAsync(buffer, 0, buffer.Length,
                                                                   budget.Token)
                                                        .ConfigureAwait(false)) > 0)
                                    {
                                        if (bytes.Length + count > 2000000)
                                        {
                                            throw new AuthlyXException(
                                                "RESPONSE_INVALID",
                                                "The server response is too large.",
                                                FailureKind.Security);
                                        }

                                        bytes.Write(buffer, 0, count);
                                    }
                                    text = Encoding.UTF8.GetString(bytes.ToArray());
                                }
                                JObject outer;
                                try
                                {
                                    outer = Protocol.Parse(text);
                                }
                                catch
                                {
                                    if ((int)response.StatusCode >= 500)
                                    {
                                        throw new AuthlyXException(
                                            "SERVICE_UNAVAILABLE",
                                            "The service is temporarily unavailable.",
                                            FailureKind.Network, (int)response.StatusCode);
                                    }

                                    throw new AuthlyXException(
                                        "RESPONSE_INVALID",
                                        "The server response is not valid JSON.",
                                        FailureKind.Security);
                                }
                                if (outer["verification"] == null &&
                                    (int)response.StatusCode >= 500)
                                {
                                    throw new AuthlyXException(
                                        "SERVICE_UNAVAILABLE",
                                        "The service is temporarily unavailable.",
                                        FailureKind.Network, (int)response.StatusCode);
                                }

                                var value = Protocol.VerifyResponse(outer, (int)response.StatusCode,
                                                                    path, body, nonce, responseKeys,
                                                                    out var serverTime);
                                Anchor(serverTime.ToUnixTimeMilliseconds());
                                SetConnection(ConnectionState.Online);
                                if (!(bool)value["success"]! || !response.IsSuccessStatusCode)
                                {
                                    string code = (string)value["code"]!;
                                    int status = (int)response.StatusCode;
                                    var kind = status == 429 || status >= 500 ? FailureKind.Network
                                                                              : FailureKind.Server;
                                    if (retryable && attempt < options.RetryLimit &&
                                        (kind == FailureKind.Network ||
                                         code == "REQUEST_TIME_INVALID"))
                                    {
                                        double delay =
                                            response.Headers.RetryAfter?.Delta?.TotalMilliseconds ??
                                            (response.Headers.RetryAfter?.Date.HasValue == true
                                                 ? (response.Headers.RetryAfter.Date.Value -
                                                    DateTimeOffset.UtcNow)
                                                       .TotalMilliseconds
                                                 : 150 * (attempt + 1));
                                        if (delay >= 0 &&
                                            timer.Elapsed.TotalMilliseconds + delay + 50 <
                                                options.RequestBudget.TotalMilliseconds)
                                        {
                                            await Task
                                                .Delay(TimeSpan.FromMilliseconds(delay),
                                                       budget.Token)
                                                .ConfigureAwait(false);
                                            continue;
                                        }
                                    }
                                    throw new AuthlyXException(code, (string)value["message"]!,
                                                               kind, status,
                                                               (string?)value["requestId"],
                                                               value["data"]);
                                }
                                return value;
                            }
                        }
                    }
                    catch (AuthlyXException error)
                    {
                        if (error.Kind == FailureKind.Security)
                        {
                            SetConnection(ConnectionState.Untrusted);
                            Invalidate(error);
                        }
                        else if (error.Kind == FailureKind.Network)
                        {
                            SetConnection(ConnectionState.Disconnected);
                        }
                        else if (authenticated && Revoked(error.Code))
                        {
                            Invalidate(error);
                        }

                        throw;
                    }
                    catch (OperationCanceledException)
                    {
                        if (cancel.IsCancellationRequested || lifetime.IsCancellationRequested)
                        {
                            throw;
                        }

                        SetConnection(ConnectionState.Disconnected);
                        throw new AuthlyXException(
                            "NETWORK_TIMEOUT", "The request timed out. Try again when connected.",
                            FailureKind.Network);
                    }
                    catch (HttpRequestException)
                    {
                        SetConnection(ConnectionState.Disconnected);
                        if (retryable && attempt < options.RetryLimit &&
                            timer.ElapsedMilliseconds + 200 <
                                options.RequestBudget.TotalMilliseconds)
                        {
                            await Task.Delay(150, budget.Token).ConfigureAwait(false);
                            continue;
                        }
                        throw new AuthlyXException("NETWORK_ERROR", "Could not connect to AuthlyX.",
                                                   FailureKind.Network);
                    }
                }
            }
        }
        private async Task<JObject> Execute(string path, JObject data, bool authenticated,
                                            bool retryable, CancellationToken cancel)
        {
            if (options.Debug)
            {
                System.Diagnostics.Debug.WriteLine("AuthlyX request: " + path);
            }

            if (disposed)
            {
                throw new ObjectDisposedException(nameof(AuthlyX));
            }

            await gate.WaitAsync(cancel).ConfigureAwait(false);
            try
            {
                if (authenticated && deadline <= TrustedNow())
                {
                    var check = await Send("/v3/session/check", new JObject(), true, true, cancel)
                                    .ConfigureAwait(false);
                    AcceptLease((JObject)check["data"]!);
                }
                return await Send(path, data, authenticated, retryable, cancel)
                    .ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }
        private void Complete<T>(Func<Task<Result<T>>> operation, Action<Result<T>> completed)
        {
            if (completed == null)
            {
                throw new ArgumentNullException(nameof(completed));
            }

            var context = SynchronizationContext.Current;
            _ = Task.Run(async () =>
                         {
                             Result<T> result;
                             try
                             {
                                 result = await operation().ConfigureAwait(false);
                             }
                             catch (AuthlyXException error)
                             {
                                 result = new Result<T> { Success = false, Code = error.Code,
                                                          Message = error.Message,
                                                          RequestId = error.RequestId ?? "" };
                                 if (error.Data is JObject data && typeof(T) == typeof(JObject))
                                 {
                                     result.Data = (T)(object)data;
                                 }
                             }
                             catch (OperationCanceledException)
                             {
                                 result = new Result<T> { Success = false, Code = "CANCELLED",
                                                          Message = "The request was cancelled." };
                             }
                             catch (ObjectDisposedException)
                             {
                                 result = new Result<T> { Success = false, Code = "CLIENT_DISPOSED",
                                                          Message = "The SDK has been disposed." };
                             }
                             if (context == null)
                             {
                                 completed(result);
                             }
                             else
                             {
                                 context.Post(
                                     _ => completed(result), null);
                             }
                         });
        }
        public void initialize(Action<Result<JObject>> completed) => init(completed);
        public Task<Result<JObject>> initialize(CancellationToken cancel = default) => init(cancel);
        public void init(Action<Result<JObject>> completed) => Complete(() => init(), completed);
        public void login(string license,
                          Action<Result<SessionInfo>> completed) => Complete(() => login(license),
                                                                             completed);
        public void login(string username, string password,
                          Action<Result<SessionInfo>> completed) => Complete(() => login(username,
                                                                                         password),
                                                                             completed);
        public void deviceLogin(Action<Result<SessionInfo>> completed) =>
            Complete(() => deviceLogin(), completed);
        public void checkSession(Action<Result<JObject>> completed) =>
            Complete(() => checkSession(), completed);
        public bool hasFeature(string name) => HasAccess && userData.Features.Contains(name);
        public async Task<Result<string>> getVariable(string name,
                                                      CancellationToken cancel = default)
        {
            var result = await readVariable(name, cancel).ConfigureAwait(false);
            return new Result<string> { Success = result.Success, Code = result.Code,
                                        Message = result.Message, RequestId = result.RequestId,
                                        Data = result.Data.Value };
        }
        public void getVariable(string name, Action<Result<string>> completed) =>
            Complete(() => getVariable(name), completed);
        public Task<Result<JObject>> getScript(string name,
                                               CancellationToken cancel) => getScript(name, null,
                                                                                      cancel);
        public void getScript(string name,
                              Action<Result<JObject>> completed) => Complete(() => getScript(name),
                                                                             completed);
        public void saveOfflineAccess(Action<Result<JObject>> completed) =>
            Complete(() => saveOfflineAccess(), completed);
        public void logout(Action<Result<JObject>> completed) => Complete(
            async () =>
            {
                await logout().ConfigureAwait(false);
                return new Result<JObject> { Success = true, Code = "SESSION_ENDED",
                                             Message = "You have signed out.",
                                             Data = new JObject() };
            },
            completed);
        public async Task<Result<JObject>> getMetadata(CancellationToken cancel = default) =>
            Result<JObject>.From(await Execute("/v3/metadata/get", new JObject(), true, true,
                                               cancel)
                                     .ConfigureAwait(false));
        public void getMetadata(Action<Result<JObject>> completed) => Complete(() => getMetadata(),
                                                                               completed);
        public async Task<Result<JObject>> updateVariable(string name, string value,
                                                          CancellationToken cancel = default) =>
            Result<JObject>.From(await Execute("/v3/data/set",
                                               new JObject { ["name"] = name, ["value"] = value },
                                               true, false, cancel)
                                     .ConfigureAwait(false));
        public void updateVariable(string name, string value, Action<Result<JObject>> completed) =>
            Complete(() => updateVariable(name, value), completed);
        public async Task<Result<JObject>> getFeatures(CancellationToken cancel = default) =>
            Result<JObject>.From(await Execute("/v3/features/list", new JObject(), true, true,
                                               cancel)
                                     .ConfigureAwait(false));
        public void getFeatures(Action<Result<JObject>> completed) => Complete(() => getFeatures(),
                                                                               completed);
        public async Task<Result<JObject>> getFeature(string name,
                                                      CancellationToken cancel = default) =>
            Result<JObject>.From(await Execute("/v3/features/get", new JObject { ["name"] = name },
                                               true, true, cancel)
                                     .ConfigureAwait(false));
        public void getFeature(string name, Action<Result<JObject>> completed) =>
            Complete(() => getFeature(name), completed);
        public async Task<Result<JObject>> getAllMetadata(CancellationToken cancel = default) =>
            Result<JObject>.From(await Execute("/v3/metadata/list", new JObject(), true, true,
                                               cancel)
                                     .ConfigureAwait(false));
        public void getAllMetadata(Action<Result<JObject>> completed) =>
            Complete(() => getAllMetadata(), completed);
        public async Task<Result<JObject>> getMetadata(string key,
                                                       CancellationToken cancel = default) =>
            Result<JObject>.From(await Execute("/v3/metadata/get", new JObject { ["key"] = key },
                                               true, true, cancel)
                                     .ConfigureAwait(false));
        public void getMetadata(string key, Action<Result<JObject>> completed) =>
            Complete(() => getMetadata(key), completed);
        public async Task<Result<JObject>> checkMetadata(string key,
                                                         CancellationToken cancel = default) =>
            Result<JObject>.From(await Execute("/v3/metadata/check", new JObject { ["key"] = key },
                                               true, true, cancel)
                                     .ConfigureAwait(false));
        public void checkMetadata(string key, Action<Result<JObject>> completed) =>
            Complete(() => checkMetadata(key), completed);
        public async Task<Result<JObject>> checkFeature(string name,
                                                        CancellationToken cancel = default) =>
            Result<JObject>.From(await Execute("/v3/features/check",
                                               new JObject { ["name"] = name }, true, true, cancel)
                                     .ConfigureAwait(false));
        public void checkFeature(string name, Action<Result<JObject>> completed) =>
            Complete(() => checkFeature(name), completed);
        public async Task<Result<JObject>> sendWebhook(string eventName, JToken data,
                                                       CancellationToken cancel = default) =>
            Result<JObject>.From(await Execute("/v3/webhooks/send",
                                               new JObject { ["event"] = eventName,
                                                             ["data"] = data },
                                               true, false, cancel)
                                     .ConfigureAwait(false));
        public void sendWebhook(string eventName, JToken data, Action<Result<JObject>> completed) =>
            Complete(() => sendWebhook(eventName, data), completed);

        public async Task<Result<JObject>> setMetadata(string key, JToken value,
                                                       CancellationToken cancel = default) =>
            Result<JObject>.From(await Execute("/v3/metadata/set",
                                               new JObject { ["key"] = key, ["value"] = value },
                                               true, false, cancel)
                                     .ConfigureAwait(false));
        public void setMetadata(string key, JToken value, Action<Result<JObject>> completed) =>
            Complete(() => setMetadata(key, value), completed);

        public async Task<Result<JObject>> init(CancellationToken cancel = default)
        {
            try
            {
                return Result<JObject>.From(await Execute("/v3/initialize",
                                                          new JObject { ["appKey"] = options.AppKey,
                                                                        ["version"] = options.Version },
                                                          false, true, cancel)
                                                .ConfigureAwait(false));
            }
            catch (AuthlyXException error) when (error.Kind == FailureKind.Network)
            {
                if (HasOfflineAccess)
                    return new Result<JObject> {
                        Success = true, Code = "OFFLINE_MODE",
                        Message = "You are offline. Sign in with the account you saved offline access for.",
                        Data = new JObject()
                    };
                return new Result<JObject> {
                    Code = error.Code,
                    Message = "Could not connect to AuthlyX. Check your internet connection.",
                    RequestId = error.RequestId ?? "", Data = new JObject()
                };
            }
            catch (AuthlyXException error) when (error.Kind == FailureKind.Server)
            {
                var prompt = UpdatePrompt(error.Data, error.Code, error.Message);
                if (prompt != null)
                    await Task.Run(() => ShowUpdate(prompt)).ConfigureAwait(false);
                return new Result<JObject> {
                    Code = error.Code, Message = error.Message, RequestId = error.RequestId ?? "",
                    Data = error.Data as JObject ?? new JObject()
                };
            }
        }

        private string[]? UpdatePrompt(JToken? data, string code, string message)
        {
            if (!options.UpdatePrompt || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                !(data is JObject body) || !(body["update"] is JObject update))
                return null;
            string Text(string key) => update[key]?.Type == JTokenType.String ? (string)update[key]! : "";
            string url = Text("url"), latest = Text("version"), notes = Text("notes");
            if (!url.StartsWith("https://", StringComparison.Ordinal) &&
                !url.StartsWith("http://", StringComparison.Ordinal))
                url = "";
            bool required = code == "UPDATE_REQUIRED" || code == "VERSION_MISMATCH";
            if (!required)
            {
                if (body["updateAvailable"]?.Type != JTokenType.Boolean || !(bool)body["updateAvailable"]! ||
                    url == "" || Interlocked.Exchange(ref updateOffered, 1) == 1)
                    return null;
            }
            string text = required ? message : "Version " + latest + " is available.";
            text += "\n\nYour version: " + options.Version;
            if (latest != "")
                text += "\nLatest version: " + latest;
            if (notes != "")
                text += "\n\n" + notes;
            if (url != "")
                text += "\n\nOpen the download page?";
            string title = code == "UPDATE_REQUIRED"    ? "Update required"
                           : code == "VERSION_MISMATCH" ? "Version not supported"
                                                        : "Update available";
            return new[] { title, text, url };
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

        private static void ShowUpdate(string[] prompt)
        {
            try
            {
                uint flags = 0x40 | 0x40000 | 0x10000 | (prompt[2] == "" ? 0u : 0x4u);
                if (MessageBoxW(IntPtr.Zero, prompt[1], prompt[0], flags) == 6 && prompt[2] != "")
                    Process.Start(new ProcessStartInfo(prompt[2]) { UseShellExecute = true });
            }
            catch
            {
            }
        }
        public Task<Result<SessionInfo>> login(
            string license, CancellationToken cancel = default) => this.license(license, cancel);
        public Task<Result<SessionInfo>> license(string license,
                                                 CancellationToken cancel = default) =>
            Login("license", "", "", license, cancel);
        public Task<Result<SessionInfo>> login(string username, string password,
                                               CancellationToken cancel = default) =>
            Login("user", username, password, "", cancel);
        public Task<Result<SessionInfo>> deviceLogin(CancellationToken cancel = default) =>
            Login("device", "", "", "", cancel);
        public Task<Result<SessionInfo>> registerUser(string username, string password,
                                                      string licenseKey,
                                                      CancellationToken cancel = default) =>
            Login("register", username, password, licenseKey, cancel);
        public void registerUser(string username, string password, string licenseKey,
                                 Action<Result<SessionInfo>> completed) =>
            Complete(() => registerUser(username, password, licenseKey), completed);
        public async Task<Result<JObject>> changePassword(string currentPassword,
                                                          string newPassword,
                                                          CancellationToken cancel = default) =>
            Result<JObject>.From(await Execute("/v3/account/password",
                                               new JObject { ["currentPassword"] = currentPassword,
                                                             ["newPassword"] = newPassword },
                                               true, false, cancel)
                                     .ConfigureAwait(false));
        public async Task<Result<JObject>> redeemLicense(string licenseKey,
                                                         CancellationToken cancel = default) =>
            Result<JObject>.From(await Execute("/v3/licenses/redeem",
                                               new JObject { ["licenseKey"] = licenseKey }, true,
                                               false, cancel)
                                     .ConfigureAwait(false));
        public async Task<Result<JObject>> log(string message,
                                               CancellationToken cancel = default) =>
            Result<JObject>.From(await Execute("/v3/logs/add",
                                               new JObject { ["message"] = message }, true, false,
                                               cancel)
                                     .ConfigureAwait(false));
        public void changePassword(string currentPassword, string newPassword,
                                   Action<Result<JObject>> completed) =>
            Complete(() => changePassword(currentPassword, newPassword), completed);
        public void redeemLicense(string licenseKey, Action<Result<JObject>> completed) =>
            Complete(() => redeemLicense(licenseKey), completed);
        public void log(string message,
                        Action<Result<JObject>> completed) => Complete(() => log(message),
                                                                       completed);
        private async Task<Result<SessionInfo>> Login(string type, string username, string password,
                                                      string license, CancellationToken cancel)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(AuthlyX));
            }

            await gate.WaitAsync(cancel).ConfigureAwait(false);
            try
            {
                if (token != null || offlineSession)
                {
                    throw new AuthlyXException("ALREADY_SIGNED_IN",
                                               "Sign out before starting another session.",
                                               FailureKind.Server);
                }

                loggingOut = false;
                JObject challenge;
                try
                {
                    challenge =
                        (JObject)(await Send("/v3/challenge",
                                             new JObject { ["appKey"] = options.AppKey,
                                                           ["installationPublicKey"] = publicPem },
                                             false, false, cancel)
                                      .ConfigureAwait(false))["data"]!;
                }
                catch (AuthlyXException error) when (error.Kind == FailureKind.Network &&
                                                     type != "register")
                {
                    return OfflineLogin(type, username, password, license);
                }
                var operation = new JObject { ["type"] = type,
                                              ["appKey"] = options.AppKey,
                                              ["version"] = options.Version,
                                              ["sid"] = options.SystemId,
                                              ["hash"] = options.BuildHash,
                                              ["username"] = username,
                                              ["password"] = password,
                                              ["licenseKey"] = license,
                                              ["device"] = DeviceDetails.Read() };
                string signature = Protocol.Sign(
                    seed, Protocol.Json(new JObject { ["challenge"] = challenge["payload"],
                                                      ["operation"] = operation }));
                var request = (JObject)operation.DeepClone();
                request["challengeId"] = challenge["challengeId"];
                request["signature"] = signature;
                var value =
                    await Send("/v3/login", request, false, false, cancel).ConfigureAwait(false);
                appId = (string)Protocol.Parse((string)challenge["payload"]!)["appId"]!;
                var data = (JObject)value["data"]!;
                token = (string)data["token"]!;
                sessionId = (string)data["sessionId"]!;
                identityId = (string)data["identityId"]!;
                string kind = type == "register" ? "user" : type;
                slot = SlotOf(kind, username, license);
                var salt = Protocol.Random(16);
                credential = new JObject {
                    ["kind"] = kind, ["salt"] = Convert.ToBase64String(salt),
                    ["verifier"] = kind == "device" ? ""
                                                    : Verifier(kind == "user" ? password : license, salt)
                };
                AcceptLease(data);
                currentUserData = UserData.From(data);
                identitySnapshot = new JObject { ["identityId"] = data["identityId"],
                                                 ["identity"] = data["identity"]?.DeepClone() };
                StartHeartbeat();
                var prompt = UpdatePrompt(data, (string?)value["code"] ?? "", (string?)value["message"] ?? "");
                if (prompt != null)
                    new Thread(() => ShowUpdate(prompt)) { IsBackground = true }.Start();
                return Result<SessionInfo>.From(value);
            }
            finally
            {
                gate.Release();
            }
        }
        private void AcceptLease(JObject data)
        {
            if (!DateTimeOffset.TryParse((string?)data["accessDeadline"],
                                         CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                                         out var parsed) ||
                parsed.ToUnixTimeMilliseconds() > TrustedNow() + 7200100 ||
                parsed.ToUnixTimeMilliseconds() <= 0)
            {
                var error = new AuthlyXException(
                    "LEASE_INVALID", "The session deadline is invalid.", FailureKind.Security);
                Invalidate(error);
                throw error;
            }
            deadline = parsed.ToUnixTimeMilliseconds();
            if (data["features"] != null || data["featureValues"] != null ||
                data["metadata"] != null)
            {
                currentUserData.UpdateAccess(data);
            }

            heartbeatSeconds =
                Math.Max(1, Math.Min(3600, (double?)data["heartbeatSeconds"] ?? 600));
            jitterSeconds = Math.Max(0, Math.Min(15, (double?)data["jitterSeconds"] ?? 15));
        }
        public async Task<Result<JObject>> checkSession(CancellationToken cancel = default)
        {
            await gate.WaitAsync(cancel).ConfigureAwait(false);
            try
            {
                var value = await Send("/v3/session/check", new JObject(), true, true, cancel)
                                .ConfigureAwait(false);
                AcceptLease((JObject)value["data"]!);
                currentUserData.UpdateAccess((JObject)value["data"]!);
                return Result<JObject>.From(value);
            }
            finally
            {
                gate.Release();
            }
        }
        private async Task<Result<AppValue>> readVariable(string name,
                                                          CancellationToken cancel = default) =>
            Result<AppValue>.From(await Execute("/v3/data/get", new JObject { ["name"] = name },
                                                true, true, cancel)
                                      .ConfigureAwait(false));
        public async Task<Result<JObject>> getScript(string name, int? revision = null,
                                                     CancellationToken cancel = default)
        {
            var request = new JObject { ["name"] = name };
            if (revision.HasValue)
            {
                request["revision"] = revision.Value;
            }

            var value =
                await Execute("/v3/scripts/get", request, true, true, cancel).ConfigureAwait(false);
            var data = (JObject)value["data"]!;
            try
            {
                var manifest = Protocol.Parse((string)data["signedPayload"]!);
                if (manifest["issuedAt"] != null || manifest["expiresAt"] != null)
                {
                    if (manifest["issuedAt"]?.Type != JTokenType.Integer ||
                        manifest["expiresAt"]?.Type != JTokenType.Integer ||
                        (long)manifest["issuedAt"]! > TrustedNow() + 30000 ||
                        (long)manifest["expiresAt"]! <= TrustedNow() ||
                        (long)manifest["expiresAt"]! <= (long)manifest["issuedAt"]!)
                    {
                        throw new Exception();
                    }
                }
                if (manifest["minimumRevision"] != null &&
                    (manifest["minimumRevision"]!.Type != JTokenType.Integer ||
                     (int)manifest["revision"]! < (int)manifest["minimumRevision"]!))
                {
                    throw new Exception();
                }

                var id = (string)manifest["keyId"]!;
                if (!scriptKeys.TryGetValue(id, out var pem) ||
                    !Protocol.Verify(pem, (string)data["signedPayload"]!,
                                     (string)data["signature"]!) ||
                    !JToken.DeepEquals(data["manifest"], manifest) ||
                    (string?)manifest["appId"] != appId || (string?)manifest["name"] != name ||
                    (revision.HasValue && (int?)manifest["revision"] != revision) ||
                    (string?)manifest["sha256"] != Protocol.Hash((string)data["content"]!) ||
                    (int?)manifest["bytes"] != Encoding.UTF8.GetByteCount((string)data["content"]!))
                {
                    throw new Exception();
                }
            }
            catch
            {
                var error = new AuthlyXException(
                    "SCRIPT_INVALID", "The script could not be verified.", FailureKind.Security);
                throw error;
            }
            return Result<JObject>.From(value);
        }
        public async Task<Result<JObject>> saveOfflineAccess(CancellationToken cancel = default)
        {
            await gate.WaitAsync(cancel).ConfigureAwait(false);
            try
            {
                if (token == null || credential == null)
                {
                    throw new AuthlyXException("NOT_SIGNED_IN",
                                               "Sign in online before saving offline access.",
                                               FailureKind.Server);
                }

                var challenge = (JObject)(await Send("/v3/offline/challenge", new JObject(), true,
                                                     false, cancel)
                                              .ConfigureAwait(false))["data"]!;
                var proof = Protocol.Sign(
                    seed,
                    Protocol.Json(
                        new JObject { ["challenge"] = challenge["payload"],
                                      ["operation"] = new JObject { ["action"] = "offlinePermit",
                                                                    ["sessionId"] = sessionId } }));
                var result = await Send("/v3/offline/permit",
                                        new JObject { ["challengeId"] = challenge["challengeId"],
                                                      ["signature"] = proof },
                                        true, false, cancel)
                                 .ConfigureAwait(false);
                var record = new JObject { ["appId"] = appId, ["identityId"] = identityId,
                                           ["kind"] = credential["kind"],
                                           ["salt"] = credential["salt"],
                                           ["verifier"] = credential["verifier"],
                                           ["permit"] = result["data"],
                                           ["trustedTime"] = TrustedNow(),
                                           ["localTime"] = WallNow(),
                                           ["user"] = identitySnapshot?.DeepClone() };
                if (!ValidRecord(record))
                {
                    throw new AuthlyXException("OFFLINE_INVALID",
                                               "The offline permit could not be verified.",
                                               FailureKind.Security);
                }

                try
                {
                    options.Storage.Save(RecordName(slot!),
                                         record.ToString(Newtonsoft.Json.Formatting.None));
                    var list = Slots();
                    if (!list.Contains(slot!))
                    {
                        list.Add(slot!);
                    }

                    SaveSlots(list);
                }
                catch
                {
                    throw new AuthlyXException("STORAGE_INVALID",
                                               "Could not save protected offline state.",
                                               FailureKind.Storage);
                }

                return Result<JObject>.From(result);
            }
            finally
            {
                gate.Release();
            }
        }
        private long RecordNow(JObject record) =>
            serverEpoch != 0 ? TrustedNow()
                             : (long)record["trustedTime"]! + WallNow() - (long)record["localTime"]!;
        private Result<SessionInfo> OfflineLogin(string type, string username, string password,
                                                 string license)
        {
            string name = SlotOf(type, username, license);
            var record = LoadRecord(name);
            if (record == null || !ValidRecord(record))
            {
                if (record != null)
                {
                    Forget(name);
                }

                throw new AuthlyXException(
                    "OFFLINE_UNAVAILABLE",
                    "Could not connect to AuthlyX, and this PC has no saved offline access for " +
                        "that account. Check your internet connection and try again.",
                    FailureKind.Network);
            }

            if (type != "device")
            {
                var expected = Encoding.UTF8.GetBytes((string?)record["verifier"] ?? "");
                var actual = Encoding.UTF8.GetBytes(
                    Verifier(type == "user" ? password : license,
                             Convert.FromBase64String((string?)record["salt"] ?? "")));
                int difference = expected.Length ^ actual.Length;
                for (int i = 0; i < Math.Min(expected.Length, actual.Length); i++)
                {
                    difference |= expected[i] ^ actual[i];
                }

                if (difference != 0)
                {
                    throw type == "user"
                        ? new AuthlyXException("INVALID_CREDENTIALS",
                                               "Username or password is incorrect.",
                                               FailureKind.Server)
                        : new AuthlyXException("LICENSE_NOT_FOUND", "License not found.",
                                               FailureKind.Server);
                }
            }

            if (serverEpoch == 0)
            {
                Anchor(RecordNow(record));
            }

            appId = (string?)record["appId"];
            identityId = (string?)record["identityId"];
            slot = name;
            offline = record;
            offlineSession = true;
            if (record["user"] is JObject savedUser)
            {
                identitySnapshot = savedUser;
                currentUserData = UserData.From(savedUser);
            }

            ValidOffline(true);
            return Result<SessionInfo>.From(new JObject {
                ["success"] = true, ["code"] = "OFFLINE_ACCESS",
                ["message"] = "Signed in with saved offline access. Online features are " +
                              "unavailable until you reconnect.",
                ["requestId"] = "",
                ["data"] = new JObject { ["identity"] = identitySnapshot?["identity"]?.DeepClone(),
                                         ["identityId"] = identityId ?? "",
                                         ["application"] = appId ?? "" }
            });
        }
        private bool ValidOffline(bool save)
        {
            if (!offlineSession || offline == null || !ValidRecord(offline))
            {
                return false;
            }

            if (save)
            {
                try
                {
                    offline["trustedTime"] = RecordNow(offline);
                    offline["localTime"] = WallNow();
                    options.Storage.Save(RecordName(slot!),
                                         offline.ToString(Newtonsoft.Json.Formatting.None));
                }
                catch
                {
                    return false;
                }
            }

            return true;
        }
        private bool ValidRecord(JObject record)
        {
            try
            {
                if (WallNow() < anchorWall || WallNow() < (long)record["localTime"]!)
                {
                    return false;
                }

                var permit = (JObject)record["permit"]!;
                string payload = (string)permit["signedPayload"]!;
                if (payload.Length > 4096)
                {
                    return false;
                }

                var claims = Protocol.Parse(payload);
                string id = (string)claims["keyId"]!;
                if (!offlineKeys.TryGetValue(id, out var pem) || Protocol.KeyId(pem) != id ||
                    !Protocol.Verify(pem, payload, (string)permit["signature"]!))
                {
                    return false;
                }

                long now = RecordNow(record), issued = (long)claims["issuedAt"]!,
                     expires = (long)claims["expiresAt"]!;
                return (string?)claims["type"] == "authlyx.offline.v1" &&
                       (string?)claims["appId"] == (string?)record["appId"] &&
                       (string?)claims["subjectId"] == (string?)record["identityId"] &&
                       (string?)claims["installationKeyHash"] == installationHash &&
                       now >= issued && now < expires && expires > issued &&
                       expires - issued <= 2592000000L && now >= (long)record["trustedTime"]!;
            }
            catch
            {
                return false;
            }
        }
        public bool HasAccess => !disposed && !loggingOut &&
                                 ((token != null && deadline > TrustedNow()) || ValidOffline(true));
        public bool HasOfflineAccess => !disposed && !loggingOut &&
                                        (offlineSession
                                             ? ValidOffline(true)
                                             : Slots().Any(item => LoadRecord(item) is JObject record &&
                                                                   ValidRecord(record)));
        public async Task logout(CancellationToken cancel = default)
        {
            loggingOut = true;
            StopHeartbeat();
            await gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (token != null)
                {
                    await Send("/v3/session/logout", new JObject(), true, false, cancel)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                StopHeartbeat();
                token = null;
                currentUserData = UserData.Empty;
                deadline = 0;
                try
                {
                    ClearOffline();
                    credential = null;
                    slot = null;
                }
                finally
                {
                    gate.Release();
                }
            }
        }
        private void StopHeartbeat()
        {
            var old = heartbeat;
            heartbeat = null;
            if (old != null)
            {
                old.Cancel();
            }
        }
        private void StartHeartbeat()
        {
            StopHeartbeat();
            if (!options.AutomaticHeartbeat)
            {
                return;
            }

            var source = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            heartbeat = source;
            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        while (!source.IsCancellationRequested)
                        {
                            double unit = BitConverter.ToUInt32(Protocol.Random(4), 0) /
                                          (double)uint.MaxValue;
                            await Task
                                .Delay(TimeSpan.FromSeconds(Math.Max(
                                           1, heartbeatSeconds + (unit * 2 - 1) * jitterSeconds)),
                                       source.Token)
                                .ConfigureAwait(false);
                            try
                            {
                                await checkSession(source.Token).ConfigureAwait(false);
                            }
                            catch (AuthlyXException)
                            {
                                if (token == null)
                                {
                                    break;
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                    finally
                    {
                        source.Dispose();
                    }
                });
        }
        public Task<Result<JObject>> onResume(CancellationToken cancel = default) =>
            checkSession(cancel);
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            StopHeartbeat();
            lifetime.Cancel();
            token = null;
            currentUserData = UserData.Empty;
            deadline = 0;
            Array.Clear(seed, 0, seed.Length);
            http.Dispose();
            SetConnection(ConnectionState.Disposed);
        }
    }

    public enum FailureKind
    {
        Server,
        Network,
        Security,
        Cancelled,
        Storage
    }
    public enum ConnectionState
    {
        Online,
        Disconnected,
        Untrusted,
        Disposed
    }
    public sealed class AuthlyXException : Exception
    {
        public string Code { get; }
        public string? RequestId { get; }
        public int Status { get; }
        public FailureKind Kind { get; }
        public new JToken? Data { get; }
        public AuthlyXException(string code, string message, FailureKind kind, int status = 0,
                                string? requestId = null, JToken? data = null)
            : base(message)
        {
            Data = data;
            Code = code;
            Kind = kind;
            Status = status;
            RequestId = requestId;
        }
    }
    public sealed class Result<T>
    {
        public bool Success { get; internal set; }
        public string Code { get; internal set; } = "";
        public string Message { get; internal set; } = "";
        public string RequestId { get; internal set; } = "";
        public T Data { get; internal set; } = default!;
        public bool ok => Success;
        public bool failed => !Success;
        public string code => Code;
        public string message => Message;
        public string requestId => RequestId;
        public T data => Data;
        public override string ToString() => Code;
        internal static Result<T> From(JObject value) => new Result<T> {
            Success = (bool)value["success"]!, Code = (string)value["code"]!,
            Message = (string)value["message"]!, RequestId = (string)value["requestId"]!,
            Data = value["data"]!.ToObject<T>()!
        };
    }
    public sealed class UserData
    {
        internal static readonly UserData Empty = new UserData();
        public string Id { get; private set; } = "";
        public string LoginType { get; private set; } = "";
        public string Username { get; private set; } = "";
        public string Email { get; private set; } = "";
        public string LicenseKey { get; private set; } = "";
        public string SID { get; private set; } = "";
        public string Hwid => SID;
        public string Plan { get; private set; } = "";
        public string Subscription => Plan;
        public string IpAddress { get; private set; } = "";
        public string Country { get; private set; } = "";
        public DateTimeOffset? ExpiryDate { get; private set; }
        public DateTimeOffset? RegisteredAt { get; private set; }
        public DateTimeOffset? LastLogin { get; private set; }
        public bool IsLifetime { get; private set; }
        public int DeviceLimit { get; private set; }
        public IReadOnlyList<string> Features { get; private set; } =
            new List<string>().AsReadOnly();
        public IReadOnlyDictionary<string, object?> Metadata { get; private set; } =
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(
                new Dictionary<string, object?>());
        public IReadOnlyDictionary<string, object?> FeatureValues { get; private set; } =
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(
                new Dictionary<string, object?>());
        internal void UpdateAccess(JObject data)
        {
            FeatureValues = new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(
                (data["featureValues"] as JObject ?? new JObject())
                    .Properties()
                    .ToDictionary(value => value.Name, value => value.Value.Type == JTokenType.Null
                                                                    ? null
                                                                    : ((JValue)value.Value).Value));
            Features = ((data["features"] as JArray)
                            ?.Values<string>()
                            .Where(value => value != null)
                            .Select(value => value!)
                            .ToList() ??
                        new List<string>())
                           .AsReadOnly();
            Metadata = new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(
                (data["metadata"] as JObject ?? new JObject())
                    .Properties()
                    .ToDictionary(value => value.Name, value => value.Value.Type == JTokenType.Null
                                                                    ? null
                                                                    : ((JValue)value.Value).Value));
        }
        internal UserData()
        {
        }
        internal static UserData From(JObject data)
        {
            var identity = data["identity"] as JObject ?? new JObject();
            var type = (string?)identity["type"] ?? "";
            var result =
                new UserData { Id = (string?)data["identityId"] ?? "",
                               LoginType = type,
                               Username = type == "user" ? (string?)identity["name"] ?? "" : "",
                               LicenseKey =
                                   (string?)identity["licenseKey"] ??
                                   (type == "license" ? (string?)identity["name"] ?? "" : ""),
                               Email = (string?)identity["email"] ?? "",
                               SID = (string?)identity["systemId"] ?? "",
                               Plan = (string?)identity["plan"] ?? "",
                               IpAddress = (string?)identity["ipAddress"] ?? "",
                               Country = (string?)identity["country"] ?? "",
                               ExpiryDate = Date(identity["expiry"]),
                               RegisteredAt = Date(identity["registeredAt"]),
                               LastLogin = Date(identity["lastLogin"]),
                               IsLifetime = identity["expiry"] == null ||
                                            identity["expiry"]!.Type == JTokenType.Null,
                               DeviceLimit = (int?)identity["deviceLimit"] ?? 0 };
            result.UpdateAccess(identity);
            return result;
        }
        private static DateTimeOffset? Date(JToken? value) =>
            DateTimeOffset.TryParse((string?)value, CultureInfo.InvariantCulture,
                                    DateTimeStyles.RoundtripKind, out var date)
                ? date
                : (DateTimeOffset?)null;
    }

    public sealed class SessionInfo
    {
        public JObject? Identity { get; set; }
        public string SessionId { get; set; } = "";
        public string IdentityId { get; set; } = "";
        public string Application { get; set; } = "";
        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset AccessDeadline { get; set; }
    }
    public sealed class AppValue
    {
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
    }

    internal static class Protocol
    {
        public static JObject Parse(string text)
        {
            using (var reader = new JsonTextReader(
                       new StringReader(text)) { DateParseHandling = DateParseHandling.None })
            {
                return JObject.Load(reader);
            }
        }
        public static string Json(JToken value)
        {
            switch (value.Type)
            {
            case JTokenType.Object:
                return "{" +
                       string.Join(",", ((JObject)value)
                                            .Properties()
                                            .Select(p => Quote(p.Name) + ":" + Json(p.Value))) +
                       "}";
            case JTokenType.Array:
                return "[" + string.Join(",", ((JArray)value).Select(Json)) + "]";
            case JTokenType.String:
                return Quote((string)value!);
            case JTokenType.Boolean:
                return (bool)value ? "true" : "false";
            case JTokenType.Null:
                return "null";
            case JTokenType.Integer:
                return ((JValue)value).Value!.ToString()!;
            default:
                throw new ArgumentException("Only strings, integers, booleans, null, arrays and " +
                                            "objects are supported in protocol requests.");
            }
        }
        private static string Quote(string value)
        {
            var b = new StringBuilder("\"");
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                case '"':
                    b.Append("\\\"");
                    break;
                case '\\':
                    b.Append("\\\\");
                    break;
                case '\b':
                    b.Append("\\b");
                    break;
                case '\f':
                    b.Append("\\f");
                    break;
                case '\n':
                    b.Append("\\n");
                    break;
                case '\r':
                    b.Append("\\r");
                    break;
                case '\t':
                    b.Append("\\t");
                    break;
                default:
                    if (c < 32 ||
                        (char.IsSurrogate(c) && !(char.IsHighSurrogate(c) && i + 1 < value.Length &&
                                                  char.IsLowSurrogate(value[i + 1]))))
                    {
                        b.Append("\\u").Append(
                            ((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        b.Append(c);
                        if (char.IsHighSurrogate(c))
                        {
                            b.Append(value[++i]);
                        }
                    }
                    break;
                }
            }
            return b.Append('"').ToString();
        }
        public static string Hash(byte[] value)
        {
            using (var sha = SHA256.Create())
            {
                return Hex(sha.ComputeHash(value));
            }
        }
        public static string Hash(string value) => Hash(Encoding.UTF8.GetBytes(value));
        public static string Hex(byte[] value) =>
            BitConverter.ToString(value).Replace("-", "").ToLowerInvariant();
        public static byte[] Random(int length)
        {
            var bytes = new byte[length];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            return bytes;
        }
        public static string PublicPem(byte[] seed)
        {
            var der = SubjectPublicKeyInfoFactory
                          .CreateSubjectPublicKeyInfo(
                              new Ed25519PrivateKeyParameters(seed, 0).GeneratePublicKey())
                          .GetEncoded();
            return "-----BEGIN PUBLIC KEY-----\n" + Convert.ToBase64String(der) +
                   "\n-----END PUBLIC KEY-----\n";
        }
        public static byte[] PublicDer(string pem) =>
            Convert.FromBase64String(pem.Replace("-----BEGIN PUBLIC KEY-----", "")
                                         .Replace("-----END PUBLIC KEY-----", "")
                                         .Trim());
        public static string KeyId(string pem) => Hash(PublicDer(pem));
        public static string Sign(byte[] seed, string payload)
        {
            var signer = new Ed25519Signer();
            signer.Init(true, new Ed25519PrivateKeyParameters(seed, 0));
            var bytes = Encoding.UTF8.GetBytes(payload);
            signer.BlockUpdate(bytes, 0, bytes.Length);
            return Convert.ToBase64String(signer.GenerateSignature());
        }
        public static bool Verify(string pem, string payload, string signature)
        {
            try
            {
                var key = PublicKeyFactory.CreateKey(PublicDer(pem)) as Ed25519PublicKeyParameters;
                if (key == null)
                {
                    return false;
                }

                var verifier = new Ed25519Signer();
                verifier.Init(false, key);
                var bytes = Encoding.UTF8.GetBytes(payload);
                verifier.BlockUpdate(bytes, 0, bytes.Length);
                return verifier.VerifySignature(Convert.FromBase64String(signature));
            }
            catch
            {
                return false;
            }
        }
        public static JObject RequestProof(string path, string body, string nonce, string timestamp,
                                           string token) =>
            new JObject { ["type"] = "authlyx.request.v3",
                          ["method"] = "POST",
                          ["path"] = path,
                          ["bodySha256"] = Hash(body),
                          ["nonce"] = nonce,
                          ["timestamp"] = timestamp,
                          ["tokenHash"] = Hash(token) };
        public static JObject VerifyResponse(JObject outer, int status, string path, string body,
                                             string nonce, IReadOnlyDictionary<string, string> keys,
                                             out DateTimeOffset serverTime)
        {
            serverTime = default;
            try
            {
                var proof = (JObject)outer["verification"]!;
                string id = (string)proof["keyId"]!, payload = (string)proof["signedPayload"]!;
                if (payload.Length > 2000000 || !keys.TryGetValue(id, out var pem) ||
                    KeyId(pem) != id || !Verify(pem, payload, (string)proof["signature"]!))
                {
                    throw new Exception();
                }

                var signed = Parse(payload);
                var request = (JObject) signed["request"]!;
                var envelope = (JObject)outer.DeepClone();
                envelope.Remove("verification");
                if ((string?) signed["type"] != "authlyx.response.v3" ||
                    (string?) signed["keyId"] != id || (int?) signed["httpStatus"] != status ||
                    (string?)request["method"] != "POST" || (string?)request["path"] != path ||
                    (string?)request["nonce"] != nonce ||
                    (string?)request["bodySha256"] != Hash(body) ||
                    !JToken.DeepEquals(signed["response"], envelope) ||
                    envelope["message"]?.Type != JTokenType.String ||
                    envelope["success"]?.Type != JTokenType.Boolean ||
                    envelope["code"]?.Type != JTokenType.String ||
                    envelope["requestId"]?.Type != JTokenType.String)
                {
                    throw new Exception();
                }

                serverTime = DateTimeOffset.Parse((string) signed["serverTime"]!,
                                                  CultureInfo.InvariantCulture,
                                                  DateTimeStyles.RoundtripKind);
                return (JObject) signed["response"]!;
            }
            catch
            {
                throw new AuthlyXException("RESPONSE_INVALID",
                                           "The server response could not be verified.",
                                           FailureKind.Security);
            }
        }
    }

    public interface IProtectedStore
    {
        string? Load(string name);
        void Save(string name, string value);
        void Delete(string name);
    }
#if NET8_0_OR_GREATER
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
    public sealed class WindowsProtectedStore : IProtectedStore
    {
        private readonly string directory;
        public WindowsProtectedStore(string directory)
        {
            this.directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(this.directory);
        }
        private string FileName(string name) => Path.Combine(directory,
                                                             Protocol.Hash(name) + ".dat");
        public string? Load(string name)
        {
            var path = FileName(name);
            if (!File.Exists(path))
            {
                return null;
            }

            var value = File.ReadAllBytes(path);
            if (value.Length > 1048576)
            {
                throw new IOException("Protected state is too large.");
            }

            return Encoding.UTF8.GetString(ProtectedData.Unprotect(
                value, Encoding.UTF8.GetBytes(name), DataProtectionScope.CurrentUser));
        }
        public void Save(string name, string value)
        {
            var path = FileName(name);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value),
                                                  Encoding.UTF8.GetBytes(name),
                                                  DataProtectionScope.CurrentUser);
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                                                   FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path))
                {
                    File.Replace(temp, path, null);
                }
                else
                {
                    File.Move(temp, path);
                }
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
        }
        public void Delete(string name)
        {
            var path = FileName(name);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
    public sealed class MemoryStore : IProtectedStore
    {
        private readonly Dictionary<string, string> values = new Dictionary<string, string>();
        public string? Load(string name)
        {
            lock (values)
            {
                return values.TryGetValue(name, out var value) ? value : null;
            }
        }
        public void Save(string name, string value)
        {
            lock (values)
            {
                values[name] = value;
            }
        }
        public void Delete(string name)
        {
            lock (values)
            {
                values.Remove(name);
            }
        }
    }

    public sealed class ClientOptions
    {
        public string AppKey { get; set; } = "";
        public string Version { get; set; } = "";
        public string SystemId { get; set; } = "";
        public string BuildHash { get; set; } = "";
        public Uri Endpoint { get; set; } = new Uri("https://api.authlyx.dev");
        public IReadOnlyDictionary<string, string> ResponseKeys { get; set; } =
            new Dictionary<string, string>();
        public IReadOnlyDictionary<string, string> OfflineKeys { get; set; } =
            new Dictionary<string, string>();
        public IReadOnlyDictionary<string, string> ScriptKeys { get; set; } =
            new Dictionary<string, string>();
        public IProtectedStore Storage { get; set; } = null!;
        public TimeSpan RequestBudget { get; set; } = TimeSpan.FromSeconds(8);
        public bool AutomaticHeartbeat { get; set; } = true;
        public bool UpdatePrompt { get; set; } = true;
        public int RetryLimit { get; set; } = 2;
        public bool Debug { get; set; }
        public bool AllowLocalHttp { get; set; }
    }
    internal static class DeviceDetails
    {
        [DllImport("kernel32.dll")]
        private static extern uint GetSystemFirmwareTable(uint provider, uint id, byte[]? buffer, uint size);

        private static string Clean(string? value)
        {
            var text = new StringBuilder();
            foreach (char c in value ?? "")
                if (c >= ' ' && c <= '~' && c != '"' && c != '<' && c != '>' && c != '&' && c != (char)92)
                    text.Append(c);
            string result = text.ToString().Trim();
            return result.Length > 100 ? result.Substring(0, 100) : result;
        }

        private static string Chassis(int chassis)
        {
            switch (chassis)
            {
            case 8: case 9: case 10: case 14: case 31: case 32: return "Windows laptop";
            case 30: return "Windows tablet";
            case 17: case 23: case 28: case 29: return "Windows server";
            case 3: case 4: case 5: case 6: case 7: case 13: case 15: case 16: case 24: case 35: case 36: return "Windows desktop";
            default: return "Windows";
            }
        }

        public static JObject Read()
        {
            string type = "Windows", baseboard = "", uuid = "";
            try
            {
                uint size = GetSystemFirmwareTable(0x52534D42, 0, null, 0);
                var raw = new byte[size];
                if (size > 8 && GetSystemFirmwareTable(0x52534D42, 0, raw, size) == size)
                {
                    int end = (int)Math.Min(raw.Length, 8 + (long)BitConverter.ToUInt32(raw, 4));
                    for (int p = 8; p + 4 <= end;)
                    {
                        byte table = raw[p];
                        int header = raw[p + 1];
                        if (header < 4 || p + header > end) break;
                        int stringsEnd = p + header;
                        while (stringsEnd + 1 < end && !(raw[stringsEnd] == 0 && raw[stringsEnd + 1] == 0)) stringsEnd++;
                        string[] parts = Encoding.ASCII.GetString(raw, p + header, stringsEnd - p - header).Split((char)0);
                        Func<byte, string> textAt = index => index == 0 || index > parts.Length ? "" : parts[index - 1];
                        if (table == 1 && header >= 0x18)
                        {
                            int u = p + 8;
                            uuid = string.Format("{0:X2}{1:X2}{2:X2}{3:X2}-{4:X2}{5:X2}-{6:X2}{7:X2}-{8:X2}{9:X2}-{10:X2}{11:X2}{12:X2}{13:X2}{14:X2}{15:X2}",
                                raw[u + 3], raw[u + 2], raw[u + 1], raw[u], raw[u + 5], raw[u + 4], raw[u + 7], raw[u + 6],
                                raw[u + 8], raw[u + 9], raw[u + 10], raw[u + 11], raw[u + 12], raw[u + 13], raw[u + 14], raw[u + 15]);
                        }
                        else if (table == 2 && header >= 8 && baseboard == "") baseboard = textAt(raw[p + 7]);
                        else if (table == 3 && header >= 6 && type == "Windows") type = Chassis(raw[p + 5] & 0x7F);
                        p = stringsEnd + 2;
                        if (table == 127) break;
                    }
                }
            }
            catch (Exception error) when (error is DllNotFoundException || error is EntryPointNotFoundException)
            {
            }
            return new JObject { ["name"] = Clean(Environment.MachineName), ["type"] = type,
                                 ["baseboard"] = Clean(baseboard), ["uuid"] = Clean(uuid) };
        }
    }
}
