using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.OpenSsl;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AuthlyX
{
    public class Auth
    {
        private const string DefaultBaseUrl = "https://authly.cc/api/v2";
        private const long DefaultClockSkewMs = 300000;
        private const string IpLookupUrl = "https://api.ipify.org";
        private const int AttachParentProcess = -1;
        private static readonly HttpClient SharedHttpClient = CreateHttpClient();

        private readonly string baseUrl;
        private readonly string secret;
        private readonly string serverPublicKeyPem;
        private readonly bool requireSignedResponses;
        private readonly long allowedClockSkewMs;

        private string sessionId;
        private string applicationHash;
        private bool initialized;
        private bool loggingEnabled;
        private string cachedPublicIp;
        private DateTime cachedPublicIpExpiresAt = DateTime.MinValue;

        public string ownerId;
        public string appName;
        public string version;

        public ResponseStruct response = new ResponseStruct();
        public UserData userData = new UserData();
        public VariableData variableData = new VariableData();
        public UpdateData updateData = new UpdateData();
        public ChatMessages chatMessages = new ChatMessages();

        public class ResponseStruct
        {
            public bool success { get; set; }
            public string message { get; set; }
            public string raw { get; set; }
            public string code { get; set; }
            public int statusCode { get; set; }
            public string requestId { get; set; }
            public string nonce { get; set; }
            public string signatureKid { get; set; }
        }

        public class UserData
        {
            public string Username { get; set; }
            public string Email { get; set; }
            public string LicenseKey { get; set; }
            public string Subscription { get; set; }
            public string SubscriptionLevel { get; set; }
            public string ExpiryDate { get; set; }
            public int DaysLeft { get; set; }
            public string LastLogin { get; set; }
            public string Hwid { get; set; }
            public string IpAddress { get; set; }
            public string RegisteredAt { get; set; }
        }

        public class VariableData
        {
            public string VarKey { get; set; }
            public string VarValue { get; set; }
            public string UpdatedAt { get; set; }
        }

        public class UpdateData
        {
            public bool Available { get; set; }
            public string LatestVersion { get; set; }
            public string DownloadUrl { get; set; }
            public bool ForceUpdate { get; set; }
            public string Changelog { get; set; }
            public bool ShowReminder { get; set; }
            public string ReminderMessage { get; set; }
            public string AllowedUntil { get; set; }
        }

        public class ChatMessage
        {
            public int Id { get; set; }
            public string Username { get; set; }
            public string Message { get; set; }
            public string CreatedAt { get; set; }
            public DateTime? CreatedAtDateTime { get; set; }
        }

        public class ChatMessages
        {
            public string ChannelName { get; set; }
            public List<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
            public int Count { get; set; }
            public string NextCursor { get; set; }
            public bool HasMore { get; set; }
        }

        private sealed class SecurityContext
        {
            public string RequestId { get; set; }
            public string Nonce { get; set; }
            public long TimestampMs { get; set; }
        }

        private sealed class SecurityValidationResult
        {
            public bool Success { get; set; }
            public string Code { get; set; }
            public string Message { get; set; }
            public string SignatureKid { get; set; }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        public Auth(
            string ownerId,
            string appName,
            string version,
            string secret,
            bool debug = true,
            string api = DefaultBaseUrl,
            string serverPublicKeyPem = null,
            bool requireSignedResponses = false,
            long allowedClockSkewMs = DefaultClockSkewMs)
        {
            this.ownerId = ownerId ?? string.Empty;
            this.appName = appName ?? string.Empty;
            this.version = version ?? string.Empty;
            this.secret = secret ?? string.Empty;
            this.loggingEnabled = debug;
            this.baseUrl = NormalizeBaseUrl(api);
            this.serverPublicKeyPem = string.IsNullOrWhiteSpace(serverPublicKeyPem) ? null : serverPublicKeyPem.Replace("\\n", "\n");
            this.requireSignedResponses = requireSignedResponses;
            this.allowedClockSkewMs = allowedClockSkewMs > 0 ? allowedClockSkewMs : DefaultClockSkewMs;

            AuthlyXLogger.AppName = this.appName;
            AuthlyXLogger.Enabled = debug;

            CalculateApplicationHash();
            WriteLog($"[SDK] AuthlyX initialized for app '{this.appName}' using '{this.baseUrl}'.");
        }

        public void Init()
        {
            if (!HasRequiredCredentials())
            {
                SetFailure("MISSING_CREDENTIALS", "Owner ID, app name, version, and secret are required.");
                HandleInitFailureAndExit();
                return;
            }

            JObject payload = new JObject
            {
                ["owner_id"] = ownerId,
                ["app_name"] = appName,
                ["version"] = version,
                ["secret"] = secret,
                ["hash"] = applicationHash
            };

            JObject obj = SendJson("init", payload, true);
            if (response.success)
            {
                sessionId = obj?["session_id"]?.ToString();
                initialized = !string.IsNullOrWhiteSpace(sessionId);
                ShowStartupUpdateReminderIfNeeded();
                return;
            }

            HandleInitFailureAndExit();
        }

        public void Init(Action<ResponseStruct> callback)
        {
            RunAsync(Init, callback);
        }

        public void Login(string identifier, string password = null, string deviceType = null, Action<ResponseStruct> callback = null)
        {
            if (callback != null)
            {
                RunAsync(() => Login(identifier, password, deviceType), callback);
                return;
            }

            if (deviceType != null)
            {
                ExecuteDeviceLogin(deviceType, identifier);
                return;
            }

            if (password == null)
            {
                ExecuteLicenseLogin(identifier);
                return;
            }

            ExecuteUserLogin(identifier, password);
        }

        public void Register(string username, string password, string key, string email = null)
        {
            if (!EnsureInitialized()) return;

            JObject payload = new JObject
            {
                ["session_id"] = sessionId,
                ["username"] = username ?? string.Empty,
                ["password"] = password ?? string.Empty,
                ["key"] = key ?? string.Empty,
                ["email"] = email ?? string.Empty,
                ["hwid"] = GetSystemIdentifier()
            };

            SendJson("register", payload, true);
        }

        public void Register(string username, string password, string key, string email, Action<ResponseStruct> callback)
        {
            RunAsync(() => Register(username, password, key, email), callback);
        }

        public void Register(string username, string password, string key, Action<ResponseStruct> callback)
        {
            RunAsync(() => Register(username, password, key, (string)null), callback);
        }

        public void ExtendTime(string username, string licenseKey)
        {
            if (!EnsureInitialized()) return;

            JObject payload = new JObject
            {
                ["session_id"] = sessionId,
                ["username"] = username ?? string.Empty,
                ["license_key"] = licenseKey ?? string.Empty,
                ["hwid"] = GetSystemIdentifier(),
                ["ip"] = GetPublicIp()
            };

            SendJson("extend", payload, false);
        }

        public void ExtendTime(string username, string licenseKey, Action<ResponseStruct> callback)
        {
            RunAsync(() => ExtendTime(username, licenseKey), callback);
        }

        public string GetVariable(string varKey)
        {
            if (!EnsureInitialized()) return null;

            JObject payload = new JObject
            {
                ["session_id"] = sessionId,
                ["var_key"] = varKey ?? string.Empty
            };

            SendJson("variables", payload, false);
            return variableData.VarValue;
        }

        public void GetVariable(string varKey, Action<string, ResponseStruct> callback)
        {
            RunAsync(() => GetVariable(varKey), callback);
        }

        public void SetVariable(string varKey, string varValue)
        {
            if (!EnsureInitialized()) return;

            JObject payload = new JObject
            {
                ["session_id"] = sessionId,
                ["var_key"] = varKey ?? string.Empty,
                ["var_value"] = varValue ?? string.Empty
            };

            SendJson("variables/set", payload, false);
        }

        public void SetVariable(string varKey, string varValue, Action<ResponseStruct> callback)
        {
            RunAsync(() => SetVariable(varKey, varValue), callback);
        }

        public void Log(string message)
        {
            if (!EnsureInitialized()) return;

            JObject payload = new JObject
            {
                ["session_id"] = sessionId,
                ["message"] = message ?? string.Empty
            };

            SendJson("logs", payload, false);
        }

        public void Log(string message, Action<ResponseStruct> callback)
        {
            RunAsync(() => Log(message), callback);
        }

        public string GetChats(string channelName)
        {
            return GetChats(channelName, 50, null);
        }

        public void GetChats(string channelName, Action<string, ResponseStruct> callback)
        {
            RunAsync(() => GetChats(channelName), callback);
        }

        public string GetChats(string channelName, int limit, string cursor)
        {
            if (!EnsureInitialized()) return null;

            JObject payload = new JObject
            {
                ["session_id"] = sessionId,
                ["channel_name"] = channelName ?? string.Empty,
                ["limit"] = limit > 0 ? limit : 50
            };

            if (!string.IsNullOrWhiteSpace(cursor))
            {
                payload["cursor"] = cursor;
            }

            SendJson("chats/get", payload, false);
            return response.raw;
        }

        public void GetChats(string channelName, int limit, string cursor, Action<string, ResponseStruct> callback)
        {
            RunAsync(() => GetChats(channelName, limit, cursor), callback);
        }

        public void SendChat(string message)
        {
            SendChat(message, "general");
        }

        public void SendChat(string message, Action<ResponseStruct> callback)
        {
            RunAsync(() => SendChat(message), callback);
        }

        public void SendChat(string message, string channelName)
        {
            if (!EnsureInitialized()) return;

            JObject payload = new JObject
            {
                ["session_id"] = sessionId,
                ["channel_name"] = string.IsNullOrWhiteSpace(channelName) ? "general" : channelName,
                ["message"] = message ?? string.Empty
            };

            SendJson("chats/send", payload, false);
        }

        public void SendChat(string message, string channelName, Action<ResponseStruct> callback)
        {
            RunAsync(() => SendChat(message, channelName), callback);
        }

        public bool ValidateSession()
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                SetFailure("INVALID_SESSION", "No active session is available.");
                return false;
            }

            JObject payload = new JObject
            {
                ["session_id"] = sessionId
            };

            JObject obj = SendJson("validate-session", payload, false);
            if (!response.success)
            {
                return false;
            }

            return ReadBool(obj?["valid"]) ?? response.success;
        }

        public void ValidateSession(Action<bool, ResponseStruct> callback)
        {
            RunAsync(ValidateSession, callback);
        }

        public bool IsInitialized()
        {
            return initialized;
        }

        public string GetSessionId()
        {
            return sessionId;
        }

        public string GetCurrentApplicationHash()
        {
            return applicationHash;
        }

        public string GetAppName()
        {
            return appName;
        }

        public bool IsUpdateAvailable()
        {
            return updateData != null && updateData.Available;
        }

        public UpdateData GetUpdateInfo()
        {
            return updateData;
        }

        public void OpenDownloadUrl()
        {
            if (string.IsNullOrWhiteSpace(updateData?.DownloadUrl))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = updateData.DownloadUrl,
                UseShellExecute = true
            });
        }

        public void EnableLogging(bool enable)
        {
            loggingEnabled = enable;
            AuthlyXLogger.Enabled = enable;
        }

        public void init()
        {
            Init();
        }

        public void init(Action<ResponseStruct> callback)
        {
            Init(callback);
        }

        public void login(string identifier, string password = null, string deviceType = null, Action<ResponseStruct> callback = null)
        {
            Login(identifier, password, deviceType, callback);
        }

        public void register(string username, string password, string key, string email = null)
        {
            Register(username, password, key, email);
        }

        public void register(string username, string password, string key, string email, Action<ResponseStruct> callback)
        {
            Register(username, password, key, email, callback);
        }

        public void register(string username, string password, string key, Action<ResponseStruct> callback)
        {
            Register(username, password, key, callback);
        }

        public void extendTime(string username, string licenseKey)
        {
            ExtendTime(username, licenseKey);
        }

        public void extendTime(string username, string licenseKey, Action<ResponseStruct> callback)
        {
            ExtendTime(username, licenseKey, callback);
        }

        public string getVariable(string varKey)
        {
            return GetVariable(varKey);
        }

        public void getVariable(string varKey, Action<string, ResponseStruct> callback)
        {
            GetVariable(varKey, callback);
        }

        public void setVariable(string varKey, string varValue)
        {
            SetVariable(varKey, varValue);
        }

        public void setVariable(string varKey, string varValue, Action<ResponseStruct> callback)
        {
            SetVariable(varKey, varValue, callback);
        }

        public void log(string message)
        {
            Log(message);
        }

        public void log(string message, Action<ResponseStruct> callback)
        {
            Log(message, callback);
        }

        public string getChats(string channelName)
        {
            return GetChats(channelName);
        }

        public void getChats(string channelName, Action<string, ResponseStruct> callback)
        {
            GetChats(channelName, callback);
        }

        public string getChats(string channelName, int limit, string cursor)
        {
            return GetChats(channelName, limit, cursor);
        }

        public void getChats(string channelName, int limit, string cursor, Action<string, ResponseStruct> callback)
        {
            GetChats(channelName, limit, cursor, callback);
        }

        public void sendChat(string message)
        {
            SendChat(message);
        }

        public void sendChat(string message, Action<ResponseStruct> callback)
        {
            SendChat(message, callback);
        }

        public void sendChat(string message, string channelName)
        {
            SendChat(message, channelName);
        }

        public void sendChat(string message, string channelName, Action<ResponseStruct> callback)
        {
            SendChat(message, channelName, callback);
        }

        public bool validateSession()
        {
            return ValidateSession();
        }

        public void validateSession(Action<bool, ResponseStruct> callback)
        {
            ValidateSession(callback);
        }

        public bool isInitialized()
        {
            return IsInitialized();
        }

        public string getSessionId()
        {
            return GetSessionId();
        }

        public string getCurrentApplicationHash()
        {
            return GetCurrentApplicationHash();
        }

        public string getAppName()
        {
            return GetAppName();
        }

        public bool isUpdateAvailable()
        {
            return IsUpdateAvailable();
        }

        public UpdateData getUpdateInfo()
        {
            return GetUpdateInfo();
        }

        public void openDownloadUrl()
        {
            OpenDownloadUrl();
        }

        public void enableLogging(bool enable)
        {
            EnableLogging(enable);
        }

        private void ExecuteUserLogin(string username, string password)
        {
            if (!EnsureInitialized()) return;

            JObject payload = new JObject
            {
                ["session_id"] = sessionId,
                ["username"] = username ?? string.Empty,
                ["password"] = password ?? string.Empty,
                ["hwid"] = GetSystemIdentifier(),
                ["ip"] = GetPublicIp()
            };

            SendJson("login", payload, true);
        }

        private void ExecuteLicenseLogin(string licenseKey)
        {
            if (!EnsureInitialized()) return;

            JObject payload = new JObject
            {
                ["session_id"] = sessionId,
                ["license_key"] = licenseKey ?? string.Empty,
                ["hwid"] = GetSystemIdentifier(),
                ["ip"] = GetPublicIp()
            };

            SendJson("licenses", payload, true);
        }

        private void ExecuteDeviceLogin(string deviceType, string deviceId)
        {
            if (!EnsureInitialized()) return;

            JObject payload = new JObject
            {
                ["session_id"] = sessionId,
                ["device_type"] = deviceType ?? string.Empty,
                ["device_id"] = deviceId ?? string.Empty,
                ["ip"] = GetPublicIp()
            };

            SendJson("device-auth", payload, false);
        }

        private bool HasRequiredCredentials()
        {
            return !string.IsNullOrWhiteSpace(ownerId) &&
                   !string.IsNullOrWhiteSpace(appName) &&
                   !string.IsNullOrWhiteSpace(version) &&
                   !string.IsNullOrWhiteSpace(secret);
        }

        private bool EnsureInitialized()
        {
            if (initialized && !string.IsNullOrWhiteSpace(sessionId))
            {
                return true;
            }

            Init();

            if (initialized && !string.IsNullOrWhiteSpace(sessionId) && response.success)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(response.message))
            {
                SetFailure("NOT_INITIALIZED", "AuthlyX is not initialized. Call Init() first.");
            }

            return false;
        }

        private void RunAsync(Action action, Action<ResponseStruct> callback)
        {
            SynchronizationContext syncContext = SynchronizationContext.Current;
            Task.Run(() =>
            {
                action();
                ResponseStruct snapshot = SnapshotResponse();
                DispatchToContext(syncContext, () => callback?.Invoke(snapshot));
            });
        }

        private void RunAsync<TResult>(Func<TResult> action, Action<TResult, ResponseStruct> callback)
        {
            SynchronizationContext syncContext = SynchronizationContext.Current;
            Task.Run(() =>
            {
                TResult result = action();
                ResponseStruct snapshot = SnapshotResponse();
                DispatchToContext(syncContext, () => callback?.Invoke(result, snapshot));
            });
        }

        private static void DispatchToContext(SynchronizationContext syncContext, Action callback)
        {
            if (callback == null)
            {
                return;
            }

            if (syncContext == null)
            {
                callback();
                return;
            }

            syncContext.Post(_ => callback(), null);
        }

        private ResponseStruct SnapshotResponse()
        {
            return new ResponseStruct
            {
                success = response.success,
                message = response.message,
                raw = response.raw,
                code = response.code,
                statusCode = response.statusCode,
                requestId = response.requestId,
                nonce = response.nonce,
                signatureKid = response.signatureKid
            };
        }

        private void HandleInitFailureAndExit()
        {
            string code = response.code ?? string.Empty;
            string message = string.IsNullOrWhiteSpace(response.message)
                ? "Unable to initialize AuthlyX."
                : response.message;

            WriteLog($"[SDK][INIT_FAIL] code={code} message={message}");

            EnsureConsoleForFatalError();
            Console.WriteLine("Initialization failed.");
            Console.WriteLine(message);

            bool isVersionError =
                string.Equals(code, "UPDATE_REQUIRED", StringComparison.OrdinalIgnoreCase);

            bool canOfferDownload =
                updateData != null &&
                !string.IsNullOrWhiteSpace(updateData.DownloadUrl) &&
                (updateData.Available || isVersionError);

            if (isVersionError)
            {
                if (!string.IsNullOrWhiteSpace(updateData?.LatestVersion))
                {
                    Console.WriteLine($"Latest version: {updateData.LatestVersion}");
                }

                if (canOfferDownload)
                {
                    Console.WriteLine();
                    Console.WriteLine("1. Download Latest");
                    Console.WriteLine("2. Exit");
                    Console.WriteLine();
                    Console.Write("Select an option (1 or 2): ");

                    string choice = Console.ReadLine();
                    if (choice == "1")
                    {
                        OpenDownloadUrl();
                        Console.WriteLine("Opening download URL...");
                    }
                }
                else
                {
                    Console.WriteLine("No download URL is available for this update.");
                }
            }

            Console.WriteLine();
            Console.Write("Press any key to close...");
            Console.ReadKey(true);
            Environment.Exit(1);
        }

        private static void EnsureConsoleForFatalError()
        {
            try
            {
                if (!AttachConsole(AttachParentProcess))
                {
                    AllocConsole();
                }
            }
            catch
            {
             
            }
        }

        private void ShowStartupUpdateReminderIfNeeded()
        {
            if (updateData == null || !updateData.ShowReminder)
            {
                return;
            }

            string message = string.IsNullOrWhiteSpace(updateData.ReminderMessage)
                ? "This version is outdated. Download the latest version?"
                : updateData.ReminderMessage;

            if (!string.IsNullOrWhiteSpace(updateData?.DownloadUrl))
            {
                message = string.Concat(
                    message,
                    Environment.NewLine,
                    Environment.NewLine,
                    "Would you like to download the latest Version?");
            }

            if (!string.IsNullOrWhiteSpace(updateData.LatestVersion))
            {
                message = string.Concat(message, Environment.NewLine, Environment.NewLine, "Latest version: ", updateData.LatestVersion);
            }

            if (!string.IsNullOrWhiteSpace(updateData.AllowedUntil))
            {
                message = string.Concat(message, Environment.NewLine, "Grace access until: ", updateData.AllowedUntil);
            }

            bool handled = TryShowGuiReminder(message);
            if (!handled)
            {
                ShowConsoleReminder(message);
            }
        }

        private bool TryShowGuiReminder(string message)
        {
            try
            {
                Type buttonsType =
                    Type.GetType("System.Windows.Forms.MessageBoxButtons, System.Windows.Forms", false) ??
                    Type.GetType("System.Windows.Forms.MessageBoxButtons, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", false);
                Type iconType =
                    Type.GetType("System.Windows.Forms.MessageBoxIcon, System.Windows.Forms", false) ??
                    Type.GetType("System.Windows.Forms.MessageBoxIcon, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", false);
                Type dialogResultType =
                    Type.GetType("System.Windows.Forms.DialogResult, System.Windows.Forms", false) ??
                    Type.GetType("System.Windows.Forms.DialogResult, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", false);
                Type messageBoxType =
                    Type.GetType("System.Windows.Forms.MessageBox, System.Windows.Forms", false) ??
                    Type.GetType("System.Windows.Forms.MessageBox, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", false);

                if (buttonsType == null || iconType == null || dialogResultType == null || messageBoxType == null)
                {
                    return false;
                }

                object buttons = Enum.Parse(buttonsType, string.IsNullOrWhiteSpace(updateData?.DownloadUrl) ? "OK" : "YesNo");
                object icon = Enum.Parse(iconType, "Information");
                MethodInfo showMethod = messageBoxType.GetMethod("Show", new[] { typeof(string), typeof(string), buttonsType, iconType });
                if (showMethod == null)
                {
                    return false;
                }

                object result = showMethod.Invoke(null, new object[] { message, "Update Reminder", buttons, icon });
                if (!string.IsNullOrWhiteSpace(updateData?.DownloadUrl))
                {
                    object yesValue = Enum.Parse(dialogResultType, "Yes");
                    if (result != null && result.Equals(yesValue))
                    {
                        OpenDownloadUrl();
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void ShowConsoleReminder(string message)
        {
            EnsureConsoleForFatalError();
            Console.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(updateData?.DownloadUrl))
            {
                Console.WriteLine();
                Console.WriteLine("Yes - Open browser and download Latest");
                Console.WriteLine("No - Continue to the app");
                Console.Write("Choose Yes or No (Y/N): ");
                string choice = Console.ReadLine();
                if (string.Equals(choice, "y", StringComparison.OrdinalIgnoreCase))
                {
                    OpenDownloadUrl();
                }
            }
        }

        private JObject SendJson(string endpoint, JObject payload, bool expectSignedResponse)
        {
            ResetResponse();

            if (payload == null)
            {
                return Failure("INVALID_PAYLOAD", "Payload cannot be null.");
            }

            SecurityContext securityContext = CreateSecurityContext();
            payload["request_id"] = securityContext.RequestId;
            payload["nonce"] = securityContext.Nonce;
            payload["timestamp"] = securityContext.TimestampMs;

            string requestBody = CanonicalizeToken(payload);
            string url = BuildUrl(endpoint);

            WriteLog($"[SDK][REQUEST] POST {url} {requestBody}");

            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                request.Headers.TryAddWithoutValidation("x-v2-request-id", securityContext.RequestId);
                request.Headers.TryAddWithoutValidation("x-v2-nonce", securityContext.Nonce);
                request.Headers.TryAddWithoutValidation("x-v2-timestamp", securityContext.TimestampMs.ToString(CultureInfo.InvariantCulture));

                try
                {
                    HttpResponseMessage httpResponse = SharedHttpClient.SendAsync(request).GetAwaiter().GetResult();
                    string raw = httpResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    WriteLog($"[SDK][RESPONSE] {(int)httpResponse.StatusCode} {raw}");

                    JObject obj = ParseResponseObject(raw);
                    response.statusCode = (int)httpResponse.StatusCode;
                    response.raw = raw;

                    SecurityValidationResult validation = ValidateResponseSecurity(httpResponse, obj, securityContext, expectSignedResponse);
                    if (!validation.Success)
                    {
                        return Failure(validation.Code, validation.Message, raw, validation.SignatureKid, (int)httpResponse.StatusCode);
                    }

                    response.signatureKid = validation.SignatureKid;
                    response.requestId = securityContext.RequestId;
                    response.nonce = securityContext.Nonce;
                    response.success = ReadBool(obj["success"]) ?? httpResponse.IsSuccessStatusCode;
                    response.code = obj["code"]?.ToString();
                    response.message = obj["message"]?.ToString() ?? httpResponse.ReasonPhrase ?? string.Empty;

                    if (obj["session_id"] != null)
                    {
                        sessionId = obj["session_id"]?.ToString();
                    }

                    LoadUserData(obj);
                    LoadVariableData(obj);
                    LoadUpdateData(obj);
                    LoadChatData(obj);

                    if (!response.success && string.IsNullOrWhiteSpace(response.code))
                    {
                        response.code = httpResponse.StatusCode.ToString().ToUpperInvariant();
                    }

                    return obj;
                }
                catch (HttpRequestException ex)
                {
                    return Failure("NETWORK_ERROR", $"Network error: {ex.Message}");
                }
                catch (TaskCanceledException ex)
                {
                    return Failure("TIMEOUT", $"Request timed out: {ex.Message}");
                }
                catch (Exception ex)
                {
                    return Failure("SDK_ERROR", $"Unexpected SDK error: {ex.Message}");
                }
            }
        }

        private SecurityValidationResult ValidateResponseSecurity(
            HttpResponseMessage httpResponse,
            JObject responseObject,
            SecurityContext securityContext,
            bool expectSignedResponse)
        {
            string responseRequestId = GetHeader(httpResponse, "x-v2-request-id");
            string responseNonce = GetHeader(httpResponse, "x-v2-nonce");
            string signature = GetHeader(httpResponse, "x-v2-signature");
            string signatureTimestamp = GetHeader(httpResponse, "x-v2-signature-ts");
            string signatureKid = GetHeader(httpResponse, "x-v2-signature-kid");

            if (!string.IsNullOrWhiteSpace(responseRequestId) &&
                !string.Equals(responseRequestId, securityContext.RequestId, StringComparison.Ordinal))
            {
                return new SecurityValidationResult
                {
                    Success = false,
                    Code = "AUTH_REQUEST_MISMATCH",
                    Message = "Response request_id does not match the original request."
                };
            }

            if (!string.IsNullOrWhiteSpace(responseNonce) &&
                !string.Equals(responseNonce, securityContext.Nonce, StringComparison.Ordinal))
            {
                return new SecurityValidationResult
                {
                    Success = false,
                    Code = "AUTH_REQUEST_MISMATCH",
                    Message = "Response nonce does not match the original request."
                };
            }

            bool hasSignatureHeaders =
                !string.IsNullOrWhiteSpace(signature) &&
                !string.IsNullOrWhiteSpace(signatureTimestamp);

            if (!hasSignatureHeaders)
            {
                if (expectSignedResponse && requireSignedResponses)
                {
                    return new SecurityValidationResult
                    {
                        Success = false,
                        Code = "AUTH_INVALID_SIGNATURE",
                        Message = "Signed response was expected but signature headers were missing."
                    };
                }

                return new SecurityValidationResult
                {
                    Success = true,
                    SignatureKid = signatureKid
                };
            }

            if (!long.TryParse(signatureTimestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out long signatureTimestampMs))
            {
                return new SecurityValidationResult
                {
                    Success = false,
                    Code = "AUTH_CLOCK_OUT_OF_SYNC",
                    Message = "Response signature timestamp is invalid.",
                    SignatureKid = signatureKid
                };
            }

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (Math.Abs(nowMs - signatureTimestampMs) > allowedClockSkewMs)
            {
                return new SecurityValidationResult
                {
                    Success = false,
                    Code = "AUTH_CLOCK_OUT_OF_SYNC",
                    Message = "Response signature timestamp is outside the allowed clock window.",
                    SignatureKid = signatureKid
                };
            }

            if (string.IsNullOrWhiteSpace(serverPublicKeyPem))
            {
                if (requireSignedResponses)
                {
                    return new SecurityValidationResult
                    {
                        Success = false,
                        Code = "AUTH_INVALID_SIGNATURE",
                        Message = "Response signature could not be verified because no server public key is configured.",
                        SignatureKid = signatureKid
                    };
                }

                return new SecurityValidationResult
                {
                    Success = true,
                    SignatureKid = signatureKid
                };
            }

            string canonicalBody = CanonicalizeToken(responseObject);
            string payloadToVerify = string.Concat(
                signatureTimestamp,
                "\n",
                securityContext.RequestId,
                "\n",
                securityContext.Nonce,
                "\n",
                canonicalBody);

            bool valid = VerifyEd25519Signature(serverPublicKeyPem, payloadToVerify, signature);
            if (!valid)
            {
                return new SecurityValidationResult
                {
                    Success = false,
                    Code = "AUTH_INVALID_SIGNATURE",
                    Message = "Response signature verification failed.",
                    SignatureKid = signatureKid
                };
            }

            return new SecurityValidationResult
            {
                Success = true,
                SignatureKid = signatureKid
            };
        }

        private JObject Failure(string code, string message, string raw = null, string signatureKid = null, int? statusCode = null)
        {
            SetFailure(code, message, raw, signatureKid, statusCode);
            return new JObject
            {
                ["success"] = false,
                ["code"] = code ?? string.Empty,
                ["message"] = message ?? string.Empty
            };
        }

        private void SetFailure(string code, string message, string raw = null, string signatureKid = null, int? statusCode = null)
        {
            response.success = false;
            response.code = code ?? string.Empty;
            response.message = message ?? string.Empty;
            response.raw = raw ?? response.raw;
            response.signatureKid = signatureKid ?? response.signatureKid;
            if (statusCode.HasValue)
            {
                response.statusCode = statusCode.Value;
            }

            WriteLog($"[SDK][ERROR] {code}: {message}");
        }

        private void ResetResponse()
        {
            response = new ResponseStruct();
        }

        private static HttpClient CreateHttpClient()
        {
            HttpClientHandler handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            HttpClient client = new HttpClient(handler, true)
            {
                Timeout = TimeSpan.FromSeconds(25)
            };
            client.DefaultRequestHeaders.ConnectionClose = false;
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "AuthlyX-CSharp-SDK/2.0");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        private SecurityContext CreateSecurityContext()
        {
            return new SecurityContext
            {
                RequestId = GenerateHex(16),
                Nonce = GenerateHex(16),
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        private static string GenerateHex(int byteCount)
        {
            byte[] bytes = new byte[byteCount];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        private static string NormalizeBaseUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return DefaultBaseUrl;
            }

            return url.Trim().TrimEnd('/');
        }

        private string BuildUrl(string endpoint)
        {
            string cleaned = (endpoint ?? string.Empty).Trim().TrimStart('/');
            return string.Concat(baseUrl, "/", cleaned);
        }

        private static string GetHeader(HttpResponseMessage response, string name)
        {
            if (response.Headers.TryGetValues(name, out IEnumerable<string> values))
            {
                return values.FirstOrDefault();
            }

            if (response.Content != null && response.Content.Headers.TryGetValues(name, out IEnumerable<string> contentValues))
            {
                return contentValues.FirstOrDefault();
            }

            return null;
        }

        private static JObject ParseResponseObject(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new JObject
                {
                    ["success"] = false,
                    ["message"] = "Empty response returned by server."
                };
            }

            try
            {
                JToken token;
                using (StringReader stringReader = new StringReader(raw))
                using (JsonTextReader jsonReader = new JsonTextReader(stringReader) { DateParseHandling = DateParseHandling.None })
                {
                    token = JToken.ReadFrom(jsonReader);
                }
                if (token is JObject obj)
                {
                    return obj;
                }

                return new JObject
                {
                    ["success"] = false,
                    ["message"] = "Server returned a non-object JSON payload.",
                    ["data"] = token
                };
            }
            catch (JsonReaderException)
            {
                return new JObject
                {
                    ["success"] = false,
                    ["message"] = "Server returned invalid JSON.",
                    ["raw"] = raw
                };
            }
        }

        private void LoadUserData(JObject obj)
        {
            if (obj == null)
            {
                return;
            }

            JToken user = obj["user"] ?? obj["info"];
            JToken license = obj["license"];
            JToken device = obj["device"];

            if (user != null)
            {
                userData.Username = user["username"]?.ToString() ?? userData.Username;
                userData.Email = user["email"]?.ToString() ?? userData.Email;
                userData.Subscription = user["subscription"]?.ToString() ?? user["subscription_name"]?.ToString() ?? userData.Subscription;
                userData.SubscriptionLevel = user["subscription_level"]?.ToString() ?? user["subscription"]?.ToString() ?? user["subscription_name"]?.ToString() ?? userData.SubscriptionLevel;
                userData.ExpiryDate = user["expiry_date"]?.ToString() ?? userData.ExpiryDate;
                userData.LastLogin = user["last_login"]?.ToString() ?? userData.LastLogin;
                userData.RegisteredAt = user["registered_at"]?.ToString() ?? user["date_created"]?.ToString() ?? userData.RegisteredAt;
                userData.LicenseKey = user["linked_license_key"]?.ToString() ?? userData.LicenseKey;
                userData.Hwid = user["hwid"]?.ToString() ?? user["sid"]?.ToString() ?? userData.Hwid;
                userData.IpAddress = user["ip_address"]?.ToString() ?? userData.IpAddress;
            }

            if (license != null)
            {
                userData.LicenseKey = license["license_key"]?.ToString() ?? userData.LicenseKey;
                userData.Subscription = license["subscription"]?.ToString() ?? userData.Subscription;
                userData.SubscriptionLevel = license["subscription_level"]?.ToString() ?? license["subscription"]?.ToString() ?? userData.SubscriptionLevel;
                userData.ExpiryDate = license["expiry_date"]?.ToString() ?? userData.ExpiryDate;
                userData.Email = license["email"]?.ToString() ?? userData.Email;
                userData.Hwid = license["hwid"]?.ToString() ?? license["sid"]?.ToString() ?? userData.Hwid;
                userData.IpAddress = license["ip_address"]?.ToString() ?? userData.IpAddress;
            }

            if (device != null)
            {
                userData.Email = device["email"]?.ToString() ?? userData.Email;
                userData.Subscription = device["subscription"]?.ToString() ?? device["subscription_name"]?.ToString() ?? userData.Subscription;
                userData.SubscriptionLevel = device["subscription_level"]?.ToString() ?? device["subscription"]?.ToString() ?? device["subscription_name"]?.ToString() ?? userData.SubscriptionLevel;
                userData.Hwid = device["hwid"]?.ToString() ?? device["sid"]?.ToString() ?? userData.Hwid;
                userData.ExpiryDate = device["expiry_date"]?.ToString() ?? userData.ExpiryDate;
                userData.LastLogin = device["last_login"]?.ToString() ?? userData.LastLogin;
                userData.RegisteredAt = device["registered_at"]?.ToString() ?? device["date_created"]?.ToString() ?? userData.RegisteredAt;
                userData.IpAddress = device["ip_address"]?.ToString() ?? userData.IpAddress;
            }

            if (string.IsNullOrWhiteSpace(userData.Hwid))
            {
                userData.Hwid = GetSystemIdentifier();
            }

            if (string.IsNullOrWhiteSpace(userData.IpAddress))
            {
                userData.IpAddress = GetPublicIp();
            }

            userData.DaysLeft = ComputeDaysLeft(userData.ExpiryDate);
        }

        private void LoadVariableData(JObject obj)
        {
            JToken variable = obj?["variable"];
            if (variable == null)
            {
                return;
            }

            variableData.VarKey = variable["var_key"]?.ToString();
            variableData.VarValue = variable["var_value"]?.ToString();
            variableData.UpdatedAt = variable["updated_at"]?.ToString();
        }

        private void LoadUpdateData(JObject obj)
        {
            JToken update = obj?["update"];
            if (update == null)
            {
                if (obj?["auto_update_enabled"] != null || obj?["auto_update_download_url"] != null)
                {
                    updateData.Available = true;
                    updateData.LatestVersion = obj["server_version"]?.ToString() ?? obj["version"]?.ToString();
                    updateData.DownloadUrl = obj["auto_update_download_url"]?.ToString();
                    updateData.ForceUpdate = ReadBool(obj["force_update"]) ?? false;
                }
                return;
            }

            updateData.Available = ReadBool(update["available"]) ?? false;
            updateData.LatestVersion = update["latest_version"]?.ToString();
            updateData.DownloadUrl = update["download_url"]?.ToString();
            updateData.ForceUpdate = ReadBool(update["force_update"]) ?? false;
            updateData.Changelog = update["changelog"]?.ToString();
            updateData.ShowReminder = ReadBool(update["show_reminder"]) ?? false;
            updateData.ReminderMessage = update["reminder_message"]?.ToString();
            updateData.AllowedUntil = update["allowed_until"]?.ToString();
        }

        private void LoadChatData(JObject obj)
        {
            JToken data = obj?["data"];
            if (data == null)
            {
                return;
            }

            chatMessages.ChannelName = data["channel_name"]?.ToString();

            JArray messages = data["messages"] as JArray;
            if (messages != null)
            {
                chatMessages.Messages = messages.Select(message => new ChatMessage
                {
                    Id = int.TryParse(message["id"]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedId) ? parsedId : 0,
                    Username = message["username"]?.ToString(),
                    Message = message["message"]?.ToString(),
                    CreatedAt = message["created_at"]?.ToString()
                }).ToList();
            }

            JToken pagination = data["pagination"];
            if (pagination != null)
            {
                chatMessages.Count = chatMessages.Messages?.Count ?? 0;
                chatMessages.NextCursor = pagination["next_cursor"]?.ToString();
                chatMessages.HasMore = ReadBool(pagination["has_more"]) ?? false;
            }
        }

        private static bool? ReadBool(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token.Type == JTokenType.Boolean)
            {
                return token.Value<bool>();
            }

            if (bool.TryParse(token.ToString(), out bool boolValue))
            {
                return boolValue;
            }

            if (int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
            {
                return intValue != 0;
            }

            return null;
        }

        private static string CanonicalizeToken(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return "null";
            }

            if (token is JObject obj)
            {
                List<string> parts = obj.Properties()
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .Select(property => string.Concat(
                        JsonConvert.ToString(property.Name, '"'),
                        ":",
                        CanonicalizeToken(property.Value)))
                    .ToList();

                return string.Concat("{", string.Join(",", parts), "}");
            }

            if (token is JArray array)
            {
                IEnumerable<string> items = array.Select(CanonicalizeToken);
                return string.Concat("[", string.Join(",", items), "]");
            }

            if (token is JValue value)
            {
                return JsonConvert.SerializeObject(value.Value, InvariantJsonSettings);
            }

            return JsonConvert.SerializeObject(token, InvariantJsonSettings);
        }

        private bool VerifyEd25519Signature(string publicKeyPem, string payload, string signatureBase64)
        {
            try
            {
                using (StringReader reader = new StringReader(publicKeyPem))
                {
                    PemReader pemReader = new PemReader(reader);
                    object keyObject = pemReader.ReadObject();
                    AsymmetricKeyParameter publicKey = keyObject as AsymmetricKeyParameter;

                    if (publicKey == null)
                    {
                        return false;
                    }

                    byte[] signatureBytes = Convert.FromBase64String(signatureBase64);
                    byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);

                    Ed25519Signer signer = new Ed25519Signer();
                    signer.Init(false, publicKey);
                    signer.BlockUpdate(payloadBytes, 0, payloadBytes.Length);
                    return signer.VerifySignature(signatureBytes);
                }
            }
            catch (Exception ex)
            {
                WriteLog($"[SDK][SIGNATURE_ERROR] {ex.Message}");
                return false;
            }
        }

        private void CalculateApplicationHash()
        {
            try
            {
                string executablePath = Process.GetCurrentProcess().MainModule != null
                    ? Process.GetCurrentProcess().MainModule.FileName
                    : null;

                if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                {
                    applicationHash = "UNKNOWN_HASH";
                    return;
                }

                using (SHA256 sha256 = SHA256.Create())
                using (FileStream stream = File.OpenRead(executablePath))
                {
                    byte[] hashBytes = sha256.ComputeHash(stream);
                    applicationHash = BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToLowerInvariant();
                }
            }
            catch (Exception ex)
            {
                applicationHash = "UNKNOWN_HASH";
                WriteLog($"[SDK][HASH_ERROR] {ex.Message}");
            }
        }

        private string GetSystemIdentifier()
        {
            string sid = TryGetWindowsSid();
            if (!string.IsNullOrWhiteSpace(sid))
            {
                return sid;
            }

            try
            {
                string fingerprint = string.Join("|", new[]
                {
                    Environment.MachineName ?? string.Empty,
                    Environment.UserName ?? string.Empty,
                    Environment.OSVersion.VersionString ?? string.Empty,
                    Environment.Is64BitOperatingSystem.ToString(),
                    Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture)
                });

                using (SHA256 sha256 = SHA256.Create())
                {
                    byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(fingerprint));
                    string compactHash = BitConverter.ToString(hashBytes).Replace("-", string.Empty);
                    return string.Concat("MACHINE-", compactHash.Substring(0, 32));
                }
            }
            catch
            {
                return "UNKNOWN_DEVICE";
            }
        }

        private static string TryGetWindowsSid()
        {
            try
            {
                Type identityType =
                    Type.GetType("System.Security.Principal.WindowsIdentity, System.Security.Principal.Windows", false) ??
                    Type.GetType("System.Security.Principal.WindowsIdentity, mscorlib", false);

                if (identityType == null)
                {
                    return null;
                }

                object identity = identityType.GetMethod("GetCurrent", Type.EmptyTypes)?.Invoke(null, null);
                if (identity == null)
                {
                    return null;
                }

                object user = identityType.GetProperty("User")?.GetValue(identity, null);
                if (user == null)
                {
                    return null;
                }

                return user.GetType().GetProperty("Value")?.GetValue(user, null)?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private string GetPublicIp()
        {
            if (!string.IsNullOrWhiteSpace(cachedPublicIp) && cachedPublicIpExpiresAt > DateTime.UtcNow)
            {
                return cachedPublicIp;
            }

            try
            {
                string value = SharedHttpClient.GetStringAsync(IpLookupUrl).GetAwaiter().GetResult().Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    cachedPublicIp = value;
                    cachedPublicIpExpiresAt = DateTime.UtcNow.AddMinutes(10);
                    return cachedPublicIp;
                }
            }
            catch (Exception ex)
            {
                WriteLog($"[SDK][IP_ERROR] {ex.Message}");
            }

            return cachedPublicIp ?? "UNKNOWN_IP";
        }

        private static readonly JsonSerializerSettings InvariantJsonSettings = new JsonSerializerSettings
        {
            Culture = CultureInfo.InvariantCulture
        };

        private int ComputeDaysLeft(string expiryDate)
        {
            if (string.IsNullOrWhiteSpace(expiryDate))
            {
                return 0;
            }

            if (!DateTimeOffset.TryParse(expiryDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset expiry))
            {
                return 0;
            }

            TimeSpan delta = expiry.UtcDateTime - DateTime.UtcNow;
            int days = (int)Math.Ceiling(delta.TotalDays);
            return days < 0 ? 0 : days;
        }

        private void WriteLog(string message)
        {
            if (!loggingEnabled)
            {
                return;
            }

            AuthlyXLogger.Log(message);
        }
    }

    public static class AuthlyXLogger
    {
        public static bool Enabled { get; set; }
        public static string AppName { get; set; } = "AuthlyX";

        public static void Log(string content)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            try
            {
                string root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "AuthlyX",
                    string.IsNullOrWhiteSpace(AppName) ? "default" : AppName);

                Directory.CreateDirectory(root);

                string logPath = Path.Combine(root, $"{DateTime.UtcNow:yyyy_MM_dd}.log");
                string line = $"[{DateTime.UtcNow:HH:mm:ss}] {MaskSensitive(content)}{Environment.NewLine}";
                File.AppendAllText(logPath, line, Encoding.UTF8);
            }
            catch
            {
                
            }
        }

        private static string MaskSensitive(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            string[] keys =
            {
                "session_id",
                "owner_id",
                "secret",
                "password",
                "key",
                "license_key",
                "hash",
                "request_id",
                "nonce",
                "hwid",
                "x-v2-signature"
            };

            foreach (string key in keys)
            {
                text = Regex.Replace(
                    text,
                    $"(\"{Regex.Escape(key)}\"\\s*:\\s*\")([^\"]*)(\")",
                    match => string.Concat(match.Groups[1].Value, MaskValue(key, match.Groups[2].Value), match.Groups[3].Value),
                    RegexOptions.IgnoreCase);

                text = Regex.Replace(
                    text,
                    $"({Regex.Escape(key)}\\s*=\\s*)([^\\s,;]+)",
                    match => string.Concat(match.Groups[1].Value, MaskValue(key, match.Groups[2].Value)),
                    RegexOptions.IgnoreCase);
            }

            return text;
        }

        private static string MaskValue(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            string normalizedKey = (key ?? string.Empty).ToLowerInvariant();
            if (normalizedKey.Contains("password") ||
                normalizedKey.Contains("secret") ||
                normalizedKey.Contains("signature"))
            {
                return "***";
            }

            if (value.Length <= 8)
            {
                return new string('*', value.Length);
            }

            int visible = Math.Min(5, value.Length / 3);
            string prefix = value.Substring(0, visible);
            string suffix = value.Substring(value.Length - visible, visible);
            return string.Concat(prefix, "***", suffix);
        }
    }
}
