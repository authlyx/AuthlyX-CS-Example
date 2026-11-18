using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using AuthlyXSDK;
using Newtonsoft.Json.Linq;

internal static class Program
{
    public static AuthlyX app = new AuthlyX(appKey: "", version: "");

    private static string notice = "";

    internal static async Task<int> Main(string[] args)
    {
        Console.Title = "AuthlyX";
        while (true)
        {
            Title("Connecting...");
            var initialized = await app.initialize();
            if (initialized.ok)
            {
                notice = initialized.code == "OFFLINE_MODE" ? initialized.message : "";
                break;
            }
            Title("Could not connect");
            Console.WriteLine(initialized.message);
            Console.WriteLine();
            Console.WriteLine("  1  Try again");
            Console.WriteLine("  0  Exit");
            Console.WriteLine();
            if (Ask("Choose") != "1")
            {
                return 0;
            }
        }

        while (app.HasAccess ? await MainMenu() : await SignInMenu())
        {
        }
        return 0;
    }

    private static async Task<bool> SignInMenu()
    {
        Title("Sign in");
        ShowNotice();
        Console.WriteLine("  1  Username and password\n  2  License key\n  3  This device\n  0  Exit\n");
        string choice = Ask("Choose");
        try
        {
            Result<SessionInfo> result;
            switch (choice)
            {
            case "1":
                string username = Ask("Username");
                result = await app.login(username, ReadPassword());
                break;
            case "2":
                result = await app.login(Ask("License key"));
                break;
            case "3":
                result = await app.deviceLogin();
                break;
            case "0":
            case "":
                return false;
            default:
                notice = "Choose 1, 2, 3 or 0.";
                return true;
            }
            if (!result.ok)
            {
                notice = result.message;
            }
        }
        catch (AuthlyXException error)
        {
            notice = error.Message;
        }
        return true;
    }

    private static async Task<bool> MainMenu()
    {
        var user = app.userData;
        Title("Signed in as " + Name(user) + "  |  " + Text(user.Plan) + " plan  |  Expires " +
              Expiry(user));
        ShowNotice();
        Console.WriteLine("  1  Account details          6  Check session\n" +
                          "  2  Read a variable          7  Send a log\n" +
                          "  3  Update a variable        8  Save offline access\n" +
                          "  4  Features and metadata    9  Sign out\n" +
                          "  5  Fetch a script           0  Exit\n");
        string choice = Ask("Choose");
        try
        {
            switch (choice)
            {
            case "1":
                ShowAccount(user);
                break;
            case "2":
            {
                string name = Ask("Variable name");
                var result = await app.getVariable(name);
                Title("Variable");
                if (result.ok)
                {
                    Row("Name", name);
                    Row("Value", Text(result.data));
                }
                else
                {
                    Console.WriteLine(result.message);
                }
                break;
            }
            case "3":
            {
                string name = Ask("Variable name");
                string value = Ask("New value");
                var result = await app.updateVariable(name, value);
                Title("Update a variable");
                Console.WriteLine(result.message);
                break;
            }
            case "4":
            {
                var features = await app.getFeatures();
                var metadata = await app.getAllMetadata();
                Title("Features and metadata");
                ShowList("Features", features.data?["features"] as JObject, "No features on this plan.");
                Console.WriteLine();
                ShowList("Metadata", metadata.data?["metadata"] as JObject, "No metadata.");
                break;
            }
            case "5":
            {
                string name = Ask("Script name");
                var result = await app.getScript(name);
                Title("Script: " + name);
                Console.WriteLine(result.ok ? Text((string?)result.data?["content"]) : result.message);
                break;
            }
            case "6":
            {
                var result = await app.checkSession();
                Title("Session");
                Console.WriteLine(result.message);
                break;
            }
            case "7":
            {
                string message = Ask("Log message");
                var result = await app.log(message);
                Title("Send a log");
                Console.WriteLine(result.message);
                break;
            }
            case "8":
            {
                var result = await app.saveOfflineAccess();
                Title("Offline access");
                Console.WriteLine(result.message);
                break;
            }
            case "9":
                await app.logout();
                notice = "You are signed out.";
                return true;
            case "0":
            case "":
                return false;
            default:
                notice = "Choose one of the listed options.";
                return true;
            }
        }
        catch (AuthlyXException error)
        {
            Title("Something went wrong");
            Console.WriteLine(error.Message);
        }
        Pause();
        return true;
    }

    private static void ShowAccount(UserData user)
    {
        Title("Account details");
        if (user.LoginType == "license")
        {
            Row("License key", Text(user.LicenseKey));
        }
        else if (user.LoginType == "device")
        {
            Row("Device", Text(user.SID));
        }
        else
        {
            Row("Username", Text(user.Username));
            if (user.LicenseKey != "")
            {
                Row("License key", user.LicenseKey);
            }
        }
        Row("Email", Text(user.Email));
        Row("Plan", Text(user.Plan));
        Row("Expires", Expiry(user));
        Row("Device limit", user.DeviceLimit == 0 ? "Unlimited" : user.DeviceLimit.ToString());
        Row("Registered", When(user.RegisteredAt));
        Row("Last sign-in", When(user.LastLogin));
        Row("System ID", Text(user.SID));
        Row("IP address", Text(user.IpAddress));
        Row("Country", Text(user.Country));
    }

    private static void ShowList(string heading, JObject? values, string empty)
    {
        Console.WriteLine(heading);
        if (values == null || values.Count == 0)
        {
            Console.WriteLine("  " + empty);
            return;
        }
        foreach (KeyValuePair<string, JToken?> item in values)
        {
            Row(item.Key, item.Value == null || item.Value.Type == JTokenType.Null
                              ? "-"
                              : item.Value.Type == JTokenType.String ? Text((string?)item.Value)
                                                                     : item.Value.ToString());
        }
    }

    private static string Name(UserData user) =>
        user.LoginType == "license" ? Text(user.LicenseKey)
        : user.LoginType == "device" ? "this device"
                                     : Text(user.Username);

    private static string Expiry(UserData user) =>
        user.IsLifetime || user.ExpiryDate == null ? "Never" : user.ExpiryDate.Value.ToString("yyyy-MM-dd");

    private static string When(DateTimeOffset? value) =>
        value == null ? "-" : value.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm") + " UTC";

    private static string Text(string? value) => string.IsNullOrEmpty(value) ? "-" : value!;

    private static void Row(string label, string value) =>
        Console.WriteLine("  " + label.PadRight(16) + value);

    private static void Title(string text)
    {
        if (!Console.IsOutputRedirected)
        {
            Console.Clear();
        }
        Console.WriteLine("AuthlyX  |  C# example\n\n" + text + "\n");
    }

    private static void ShowNotice()
    {
        if (notice != "")
        {
            Console.WriteLine(notice + "\n");
            notice = "";
        }
    }

    private static string Ask(string label)
    {
        Console.Write(label + ": ");
        string? value = Console.ReadLine();
        if (value == null)
        {
            Environment.Exit(0);
        }
        return value!.Trim();
    }

    private static void Pause()
    {
        Console.Write("\nPress Enter to go back.");
        if (Console.ReadLine() == null)
        {
            Environment.Exit(0);
        }
    }

    private static string ReadPassword()
    {
        if (Console.IsInputRedirected)
        {
            return Ask("Password");
        }
        Console.Write("Password: ");
        var password = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return password.ToString();
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password.Length--;
                    Console.Write("\b \b");
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
                Console.Write('*');
            }
        }
    }
}
