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
        secret: ""
    );

        /*
        Optional:
        - Set debug to false to disable SDK logs.
        - Set api to your custom domain, for example: https://example.com/api/v2
        */
        static async Task Main(string[] args)
        {
            Console.Title = "AuthlyX";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔══════════════════════════════════════╗");
            Console.WriteLine("║           AUTHLYX C# EXAMPLE         ║");
            Console.WriteLine("║             Console Test             ║");
            Console.WriteLine("╚══════════════════════════════════════╝");
            Console.ResetColor();

            Console.WriteLine("\n Starting...");

            AuthlyXApp.Init();
            if (!AuthlyXApp.response.success)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Couldn't Start: {AuthlyXApp.response.message}");
                Console.ResetColor();
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return;
            }
            string varA = AuthlyXApp.GetVariable("name");
            string aob = varA;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Connection Established Successfully!");
            Console.ResetColor();


            Console.WriteLine(aob);
            bool running = true;
            while (running)
            {
                ShowMainMenu();
                var choice = Console.ReadLine();

                switch (choice)
                {
                    case "1":
                        TestLogin();
                        break;
                    case "2":
                        TestRegister();
                        break;
                    case "3":
                        TestLicenseLogin();
                        break;
                    case "4":
                        TestDeviceLogin();
                        break;
                    case "5":
                        TestVariables();
                        break;
                    case "6":
                        TestUserInfo();
                        break;
                    case "7":
                        TestAllFeatures();
                        break;
                    case "8":
                        TestChatSystem();
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

                if (running && choice != "0")
                {
                    Console.WriteLine("\nPress any key to continue...");
                    Console.ReadKey();
                    Console.Clear();
                }
            }
        }

        static void ShowMainMenu()
        {
            Console.WriteLine("\n" + new string('═', 50));
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("MAIN MENU - Choose an option:");
            Console.ResetColor();
            Console.WriteLine("1. Login (Username + Password)");
            Console.WriteLine("2. Register New Account");
            Console.WriteLine("3. License Login (License Key Only)");
            Console.WriteLine("4. Device Login (Motherboard/Processor ID)");
            Console.WriteLine("5. Variable Operations");
            Console.WriteLine("6. View User Information");
            Console.WriteLine("7. Test All Features");
            Console.WriteLine("8. Chat System");
            Console.WriteLine("0. Exit");
            Console.WriteLine(new string('═', 50));
            Console.Write("Your choice: ");
        }

        static void TestLogin()
        {
            Console.WriteLine("\n" + new string('─', 40));
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("LOGIN TEST");
            Console.ResetColor();

            Console.Write("Enter Username: ");
            string username = Console.ReadLine();

            Console.Write("Enter Password: ");
            string password = ReadPassword();

            Console.WriteLine("\nAuthenticating...");
           AuthlyXApp.Login(username, password);

            DisplayResult("Login", AuthlyXApp.response  );
        }

        static void TestRegister()
        {
            Console.WriteLine("\n" + new string('─', 40));
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("REGISTRATION TEST");
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
        }

        static void TestLicenseLogin()
        {
            Console.WriteLine("\n" + new string('─', 40));
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("LICENSE LOGIN TEST");
            Console.ResetColor();

            Console.Write("Enter License Key: ");
            string licenseKey = Console.ReadLine();

            Console.WriteLine("\nAuthenticating with license...");
            AuthlyXApp.Login(licenseKey);

            DisplayResult("License Login", AuthlyXApp.response);
        }

        static void TestDeviceLogin()
        {
            Console.WriteLine("\n" + new string('─', 40));
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("DEVICE LOGIN TEST");
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
                return;
            }

            Console.WriteLine("\nAuthenticating with device...");
            AuthlyXApp.Login(deviceId, deviceType: deviceType);

            DisplayResult("Device Login", AuthlyXApp.response);
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
                Console.WriteLine("Invalid choice for variable operation.");
                Console.ResetColor();
            }
        }

        static void TestUserInfo()
        {
            Console.WriteLine("\n" + new string('─', 40));
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("USER INFORMATION");
            Console.ResetColor();

            if (string.IsNullOrEmpty(AuthlyXApp.userData.Username) &&
                string.IsNullOrEmpty(AuthlyXApp.userData.LicenseKey) &&
                string.IsNullOrEmpty(AuthlyXApp.userData.Hwid))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("No active session. Please login first.");
                Console.ResetColor();
                return;
            }

            DisplayUserInfo();
        }

        static void TestAllFeatures()
        {
            Console.WriteLine("\n" + new string('─', 40));
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("COMPREHENSIVE FEATURE TEST");
            Console.ResetColor();

            Console.WriteLine("\n1. Testing Login...");
            Console.Write("Enter test username: ");
            string user = Console.ReadLine();
            Console.Write("Enter test password: ");
            string pass = ReadPassword();

            AuthlyXApp.Login(user, pass);
            DisplayResult("Login", AuthlyXApp.response);

            if (AuthlyXApp.response.success)
            {
                Console.WriteLine("\n2. Testing Variable Operations...");
                string testKey = "test_variable";
                string testValue = $"test_value_{DateTime.Now:HHmmss}";

                AuthlyXApp.SetVariable(testKey, testValue);
                DisplayResult("Set Variable", AuthlyXApp.response);

                if (AuthlyXApp.response.success)
                {
                    AuthlyXApp.GetVariable(testKey);
                    DisplayResult("Get Variable", AuthlyXApp.response);
                }

                Console.WriteLine("\n3. Final User Information:");
                DisplayUserInfo();
            }
        }

        static void DisplayResult(string operation, Auth.ResponseStruct response)
        {
            Console.WriteLine("\n" + new string('─', 30));
            if (response.success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"{operation} SUCCESS");
                Console.ResetColor();
                Console.WriteLine($"Message: {response.message}");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"{operation} FAILED");
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

            Console.WriteLine($"Username: {user.Username ?? "N/A"}");
            Console.WriteLine($"Email: {user.Email ?? "N/A"}");
            Console.WriteLine($"License Key: {user.LicenseKey ?? "N/A"}");
            Console.WriteLine($"Subscription: {user.Subscription ?? "N/A"}");
            Console.WriteLine($"License Level: {user.SubscriptionLevel ?? "N/A"}");
            Console.WriteLine($"Expiry Date: {user.ExpiryDate ?? "N/A"}");
            Console.WriteLine($"Days Left: {user.DaysLeft}");
            Console.WriteLine($"Last Login: {user.LastLogin ?? "N/A"}");
            Console.WriteLine($"Registered: {user.RegisteredAt ?? "N/A"}");
            Console.WriteLine($"SID: {user.Hwid ?? "N/A"}");
            Console.WriteLine($"IP Address: {user.IpAddress ?? "N/A"}");
            Console.WriteLine(new string('═', 50));
        }

        static void TestChatSystem()
        {
            Console.WriteLine("\n" + new string('─', 40));
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("CHAT SYSTEM");
            Console.ResetColor();

            Console.WriteLine("1. Get Chats (View Messages)");
            Console.WriteLine("2. Send Chat Message");
            Console.Write("Choose operation: ");

            var choice = Console.ReadLine();

            if (choice == "1")
            {
                Console.Write("Enter Channel Name: ");
                string channelName = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(channelName))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Using default channel: " + AuthlyXApp.appName);
                    Console.ResetColor();
                    channelName = AuthlyXApp.appName;
                }

                Console.WriteLine("\nFetching messages...");
                string result = AuthlyXApp.GetChats(channelName);

                if (AuthlyXApp.response.success)
                {
                    if (AuthlyXApp.chatMessages.Count > 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"\nRetrieved {AuthlyXApp.chatMessages.Count} message(s) from channel '{channelName}':");
                        Console.ResetColor();
                        Console.WriteLine(new string('═', 50));

                        var sortedMessages = AuthlyXApp.chatMessages.Messages
                            .OrderBy(m => m.CreatedAtDateTime ?? DateTime.MinValue)
                            .ToList();

                        foreach (var msg in sortedMessages)
                        {
                            string timeStr = msg.CreatedAtDateTime?.ToString("HH:mm:ss") ?? msg.CreatedAt;
                            Console.WriteLine($"[{timeStr}] {msg.Username}: {msg.Message}");
                        }
                        Console.WriteLine(new string('═', 50));
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"No messages found in channel '{channelName}'");
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
                Console.WriteLine("Invalid choice for chat operation.");
                Console.ResetColor();
            }
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
