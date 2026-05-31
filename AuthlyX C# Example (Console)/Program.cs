using AuthlyX;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AuthlyX_CSharp_Example__Console_
{
    internal class Program
    {
        public static Auth AuthlyXApp = new Auth(
            ownerId: "",
            appName: "",
            version: "",
            secret: "",
            api: ""
        );

        /*
        Optional:
        - Set debug to false to disable SDK logs.
        - Set api to your custom domain, e.g. https://example.com/api/v2
        - Set antiDebug to false to disable anti-debugger protection (useful for local testing).
        */
        static void Main(string[] args)
        {
            Console.Title = "AuthlyX";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==============================================");
            Console.WriteLine("              AUTHLYX C# EXAMPLE              ");
            Console.WriteLine("==============================================");
            Console.ResetColor();

            Console.WriteLine("\nInitializing AuthlyX connection...");

            AuthlyXApp.Init();
            if (!AuthlyXApp.response.success)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Connection Failed: {AuthlyXApp.response.message}");
                Console.ResetColor();
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[OK] Connected Successfully!");
            Console.ResetColor();

            bool running = true;
            bool loggedIn = false;

            while (running)
            {
                if (!loggedIn)
                {
                    ShowLoginMenu();
                    var choice = Console.ReadLine();

                    switch (choice)
                    {
                        case "1":
                            loggedIn = TestLogin();
                            break;
                        case "2":
                            loggedIn = TestRegister();
                            break;
                        case "3":
                            loggedIn = TestLicenseLogin();
                            break;
                        case "4":
                            loggedIn = TestDeviceLogin();
                            break;
                        case "0":
                            running = false;
                            Console.WriteLine("Thank you for using AuthlyX!");
                            break;
                        default:
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("Invalid choice. Please try again.");
                            Console.ResetColor();
                            break;
                    }
                }
                else
                {
                    ShowMainMenu();
                    var choice = Console.ReadLine();

                    switch (choice)
                    {
                        case "1":
                            TestVariables();
                            break;
                        case "2":
                            TestUserInfo();
                            break;
                        case "3":
                            TestAllFeatures();
                            break;
                        case "4":
                            TestChatSystem();
                            break;
                        case "9":
                            loggedIn = false;
                            AuthlyXApp.userData = new Auth.UserData();
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("Logged out successfully. Returning to login menu.");
                            Console.ResetColor();
                            break;
                        case "0":
                            running = false;
                            Console.WriteLine("Thank you for using AuthlyX!");
                            break;
                        default:
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("Invalid choice. Please try again.");
                            Console.ResetColor();
                            break;
                    }
                }

                if (running)
                {
                    Console.WriteLine("\nPress any key to continue...");
                    Console.ReadKey();
                    Console.Clear();

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    if (loggedIn)
                    {
                        string displayName = !string.IsNullOrEmpty(AuthlyXApp.userData.Username)
                            ? AuthlyXApp.userData.Username
                            : (AuthlyXApp.userData.LicenseKey?.Length > 8
                                ? AuthlyXApp.userData.LicenseKey.Substring(0, 8) + "..."
                                : AuthlyXApp.userData.LicenseKey ?? "User");

                        Console.WriteLine("==============================================");
                        Console.WriteLine($"         AUTHLYX - Welcome, {displayName}");
                        Console.WriteLine("==============================================");
                    }
                    else
                    {
                        Console.WriteLine("==============================================");
                        Console.WriteLine("              AUTHLYX C# EXAMPLE              ");
                        Console.WriteLine("==============================================");
                    }
                    Console.ResetColor();
                }
            }
        }

        static void ShowLoginMenu()
        {
            Console.WriteLine("\n" + new string('═', 50));
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("LOGIN MENU - Choose an option:");
            Console.ResetColor();
            Console.WriteLine("1. Login (Username + Password)");
            Console.WriteLine("2. Register New Account");
            Console.WriteLine("3. License Login (License Key Only)");
            Console.WriteLine("4. Device Login (Motherboard/Processor ID)");
            Console.WriteLine("0. Exit");
            Console.WriteLine(new string('═', 50));
            Console.Write("Your choice: ");
        }

        static void ShowMainMenu()
        {
            Console.WriteLine("\n" + new string('═', 50));
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("MAIN MENU - Choose an option:");
            Console.ResetColor();
            Console.WriteLine("1. Variable Operations");
            Console.WriteLine("2. View User Information");
            Console.WriteLine("3. Test All Features");
            Console.WriteLine("4. Chat System");
            Console.WriteLine("9. Logout");
            Console.WriteLine("0. Exit");
            Console.WriteLine(new string('═', 50));
            Console.Write("Your choice: ");
        }

        static bool TestLogin()
        {
            Console.WriteLine("\n" + new string('─', 40));
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("LOGIN");
            Console.ResetColor();

            Console.Write("Enter Username: ");
            string username = Console.ReadLine();

            Console.Write("Enter Password: ");
            string password = ReadPassword();

            Console.WriteLine("\nAuthenticating...");
            AuthlyXApp.Login(username, password);

            DisplayResult("Login", AuthlyXApp.response);
            return AuthlyXApp.response.success;
        }

        static bool TestRegister()
        {
            Console.WriteLine("\n" + new string('─', 40));
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("REGISTER");
            Console.ResetColor();

            Console.Write("Enter Username: ");
            string username = Console.ReadLine();

            Console.Write("Enter Password: ");
            string password = ReadPassword();

            Console.Write("Enter License Key: ");
            string licenseKey = Console.ReadLine();

            Console.Write("Enter Email (optional): ");
            string email = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(email))
                email = null;

            Console.WriteLine("\nRegistering account...");
            AuthlyXApp.Register(username, password, licenseKey, email);

            DisplayResult("Registration", AuthlyXApp.response);
            return AuthlyXApp.response.success;
        }

        static bool TestLicenseLogin()
        {
            Console.WriteLine("\n" + new string('─', 40));
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("LICENSE LOGIN");
            Console.ResetColor();

            Console.Write("Enter License Key: ");
            string licenseKey = Console.ReadLine();

            Console.WriteLine("\nAuthenticating with license...");
            AuthlyXApp.Login(licenseKey);

            DisplayResult("License Login", AuthlyXApp.response);
            return AuthlyXApp.response.success;
        }

        static bool TestDeviceLogin()
        {
            Console.WriteLine("\n" + new string('─', 40));
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("DEVICE LOGIN");
            Console.ResetColor();

            Console.WriteLine("1. Motherboard ID");
            Console.WriteLine("2. Processor ID");
            Console.Write("Choose device type: ");
            var typeChoice = Console.ReadLine();

            string deviceType = typeChoice == "2" ? "processor" : "motherboard";

            Console.Write("Enter Device ID: ");
            string deviceId = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(deviceId))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Device ID cannot be empty.");
                Console.ResetColor();
                return false;
            }

            Console.WriteLine("\nAuthenticating with device...");
            AuthlyXApp.Login(deviceId, deviceType: deviceType);

            DisplayResult("Device Login", AuthlyXApp.response);
            return AuthlyXApp.response.success;
        }

        static void TestVariables()
        {
            Console.WriteLine("\n" + new string('─', 40));
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("VARIABLE OPERATIONS");
            Console.ResetColor();

            Console.WriteLine("1. Get Variable");
            Console.WriteLine("2. Set Variable");
            Console.Write("Choose operation: ");

            var choice = Console.ReadLine();

            if (choice == "1")
            {
                Console.Write("Enter Variable Key: ");
                string varKey = Console.ReadLine();

                Console.WriteLine("\nFetching variable...");
                string value = AuthlyXApp.GetVariable(varKey);

                if (AuthlyXApp.response.success)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Variable '{varKey}': {value}");
                    Console.ResetColor();
                }
                else
                {
                    DisplayResult("Get Variable", AuthlyXApp.response);
                }
            }
            else if (choice == "2")
            {
                Console.Write("Enter Variable Key: ");
                string varKey = Console.ReadLine();

                Console.Write("Enter Variable Value: ");
                string varValue = Console.ReadLine();

                Console.WriteLine("\nSetting variable...");
                AuthlyXApp.SetVariable(varKey, varValue);

                DisplayResult("Set Variable", AuthlyXApp.response);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Invalid choice.");
                Console.ResetColor();
            }
        }

        static void TestUserInfo()
        {
            Console.WriteLine("\n" + new string('─', 40));
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("USER INFORMATION");
            Console.ResetColor();

            DisplayUserInfo();
        }

        static void TestAllFeatures()
        {
            Console.WriteLine("\n" + new string('─', 40));
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("TEST ALL FEATURES");
            Console.ResetColor();

            Console.WriteLine("\n1. Testing Variable Operations...");
            string testKey = "test_variable";
            string testValue = $"test_value_{DateTime.Now:HHmmss}";

            AuthlyXApp.SetVariable(testKey, testValue);
            DisplayResult("Set Variable", AuthlyXApp.response);

            if (AuthlyXApp.response.success)
            {
                string fetched = AuthlyXApp.GetVariable(testKey);
                DisplayResult("Get Variable", AuthlyXApp.response);
                if (AuthlyXApp.response.success)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Variable value: {fetched}");
                    Console.ResetColor();
                }
            }

            Console.WriteLine("\n2. User Information:");
            DisplayUserInfo();
        }

        static void TestChatSystem()
        {
            Console.WriteLine("\n" + new string('─', 40));
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("CHAT SYSTEM");
            Console.ResetColor();

            Console.WriteLine("1. Get Messages");
            Console.WriteLine("2. Send Message");
            Console.Write("Choose operation: ");

            var choice = Console.ReadLine();

            if (choice == "1")
            {
                Console.Write("Enter Channel Name (leave empty for default): ");
                string channelName = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(channelName))
                {
                    channelName = AuthlyXApp.appName;
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Using default channel: {channelName}");
                    Console.ResetColor();
                }

                Console.WriteLine("\nFetching messages...");
                AuthlyXApp.GetChats(channelName);

                if (AuthlyXApp.response.success)
                {
                    if (AuthlyXApp.chatMessages.Count > 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"\nRetrieved {AuthlyXApp.chatMessages.Count} message(s) from '{channelName}':");
                        Console.ResetColor();
                        Console.WriteLine(new string('═', 50));

                        var sorted = AuthlyXApp.chatMessages.Messages
                            .OrderBy(m => m.CreatedAtDateTime ?? DateTime.MinValue)
                            .ToList();

                        foreach (var msg in sorted)
                        {
                            string timeStr = msg.CreatedAtDateTime?.ToString("HH:mm:ss") ?? msg.CreatedAt;
                            Console.WriteLine($"[{timeStr}] {msg.Username}: {msg.Message}");
                        }
                        Console.WriteLine(new string('═', 50));
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"No messages found in channel '{channelName}'.");
                        Console.ResetColor();
                    }
                }
                else
                {
                    DisplayResult("Get Chats", AuthlyXApp.response);
                }
            }
            else if (choice == "2")
            {
                Console.Write("Enter Channel Name (leave empty for default): ");
                string channelName = Console.ReadLine();

                Console.Write("Enter Message: ");
                string message = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(message))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Message cannot be empty.");
                    Console.ResetColor();
                    return;
                }

                if (string.IsNullOrWhiteSpace(channelName))
                {
                    channelName = AuthlyXApp.appName;
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Using default channel: {channelName}");
                    Console.ResetColor();
                }

                Console.WriteLine("\nSending message...");
                AuthlyXApp.SendChat(message, channelName);

                DisplayResult("Send Chat", AuthlyXApp.response);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Invalid choice.");
                Console.ResetColor();
            }
        }

        static void DisplayResult(string operation, Auth.ResponseStruct response)
        {
            Console.WriteLine("\n" + new string('─', 30));
            if (response.success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[OK] {operation} SUCCESS");
                Console.ResetColor();
                Console.WriteLine($"Message: {response.message}");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[X] {operation} FAILED");
                Console.ResetColor();
                Console.WriteLine($"Message: {response.message}");
            }
            Console.WriteLine(new string('─', 30));
        }

        static void DisplayUserInfo()
        {
            var user = AuthlyXApp.userData;

            Console.WriteLine("\n" + new string('═', 50));
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("USER PROFILE");
            Console.ResetColor();
            Console.WriteLine(new string('═', 50));

            Console.WriteLine($"Username:     {user.Username ?? "N/A"}");
            Console.WriteLine($"Email:        {user.Email ?? "N/A"}");
            Console.WriteLine($"License Key:  {user.LicenseKey ?? "N/A"}");
            Console.WriteLine($"Subscription: {user.Subscription ?? "N/A"}");
            Console.WriteLine($"Level:        {user.SubscriptionLevel ?? "N/A"}");
            Console.WriteLine($"Expiry Date:  {user.ExpiryDate ?? "N/A"}");
            Console.WriteLine($"Days Left:    {user.DaysLeft}");
            Console.WriteLine($"Last Login:   {user.LastLogin ?? "N/A"}");
            Console.WriteLine($"Registered:   {user.RegisteredAt ?? "N/A"}");
            Console.WriteLine($"SID:          {user.Hwid ?? "N/A"}");
            Console.WriteLine($"IP Address:   {user.IpAddress ?? "N/A"}");
            Console.WriteLine(new string('═', 50));
        }

        static string ReadPassword()
        {
            var password = new StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                    break;
                if (key.Key == ConsoleKey.Backspace && password.Length > 0)
                {
                    password.Remove(password.Length - 1, 1);
                    Console.Write("\b \b");
                }
                else if (key.Key != ConsoleKey.Backspace)
                {
                    password.Append(key.KeyChar);
                    Console.Write("*");
                }
            }
            Console.WriteLine();
            return password.ToString();
        }
    }
}
