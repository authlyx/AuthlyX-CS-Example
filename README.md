# AuthlyX C# SDK

This is a C# authentication SDK for desktop and .NET applications that want simple integration with the AuthlyX API.

This folder is primarily for SDK users. The Console and WinForms apps here are only reference examples to help you integrate faster.

## Supported Targets

The SDK supports:

- `.NET Framework 4.8`
- `.NET Standard 2.0`
- `.NET`
- `.NET Core`

Modern `.NET` and `.NET Core` projects can consume the `netstandard2.0` build.

## Install

### NuGet

Install the `AuthlyX` package from NuGet when published.

### Manual

If you are using the raw `AuthlyX.cs` file manually, you also need:

- `Newtonsoft.Json`
- `Portable.BouncyCastle`

## Quick Start

```csharp
public static Auth AuthlyXApp = new Auth(
    ownerId: "12345678",
    appName: "MYAPP",
    version: "1.0.0",
    secret: "qIBFoBJWQH4jaOZr6Sf8BJZyEVnT0LiN4QfGxJGn"
);

/*
Optional:
- Set debug to false to disable SDK logs.
- Set api to your custom domain, for example: https://example.com/api/v2
*/
```

Then initialize:

```csharp
AuthlyXApp.Init();
```

## Optional Parameters

```csharp
public static Auth AuthlyXApp = new Auth(
    ownerId: "12345678",
    appName: "MYAPP",
    version: "1.0.0",
    secret: "qIBFoBJWQH4jaOZr6Sf8BJZyEVnT0LiN4QfGxJGn",
    debug: false,
    api: "https://example.com/api/v2"
);
```

### Available options

- `debug`
  - Default: `true`
  - Set `false` to disable SDK logs

- `api`
  - Default: `https://authly.cc/api/v2`
  - Use this for your custom domain

## Available Methods

- `Init()`
- `Login(identifier, password = null, deviceType = null)`
- `Register(username, password, licenseKey, email = null)`
- `ExtendTime(username, licenseKey)`
- `GetVariable(key)`
- `SetVariable(key, value)`
- `Log(message)`
- `GetChats(channelName)`
- `SendChat(message, channelName = null)`
- `ValidateSession()`

## Authentication Example

```csharp
// Username + password
AuthlyXApp.Login("username", "password");

// License key only
AuthlyXApp.Login("XXXXX-XXXXX-XXXXX-XXXXX-XXXXX");

// Device login
AuthlyXApp.Login("YOUR_MOTHERBOARD_ID", deviceType: "motherboard");
```

The SDK routes `Login(...)` automatically:

- `password + identifier` for username login
- `identifier only` for license login
- `deviceType + identifier` for device login

## Username Login Example

```csharp
AuthlyXApp.Login("username", "password");

if (AuthlyXApp.response.success)
{
    Console.WriteLine("Login success");
    Console.WriteLine(AuthlyXApp.userData.Username);
    Console.WriteLine(AuthlyXApp.userData.SubscriptionLevel);
}
else
{
    Console.WriteLine(AuthlyXApp.response.message);
}
```

`userData.SubscriptionLevel` is populated automatically after username, license, and device authentication flows.

## License Login Example

```csharp
AuthlyXApp.Login("XXXXX-XXXXX-XXXXX-XXXXX-XXXXX");

if (AuthlyXApp.response.success)
{
    Console.WriteLine("License login success");
}
else
{
    Console.WriteLine(AuthlyXApp.response.message);
}
```

## Device Login Example

### Motherboard

```csharp
AuthlyXApp.Login("YOUR_MOTHERBOARD_ID", deviceType: "motherboard");

if (AuthlyXApp.response.success)
{
    Console.WriteLine("Motherboard login success");
}
else
{
    Console.WriteLine(AuthlyXApp.response.message);
}
```

### Processor

```csharp
AuthlyXApp.Login("YOUR_PROCESSOR_ID", deviceType: "processor");

if (AuthlyXApp.response.success)
{
    Console.WriteLine("Processor login success");
}
else
{
    Console.WriteLine(AuthlyXApp.response.message);
}
```

## Variable Example

```csharp
AuthlyXApp.SetVariable("theme", "dark");

string value = AuthlyXApp.GetVariable("theme");
Console.WriteLine(value);
```

## Chat Example

```csharp
AuthlyXApp.SendChat("Hello world", "MAIN");

string chats = AuthlyXApp.GetChats("MAIN");
Console.WriteLine(chats);
```

## Non-Blocking UI Usage

For WinForms or UI apps, use the callback overloads so the app does not freeze:

```csharp
AuthlyXApp.Login("user", "pass", callback: response =>
{
    if (response.success)
    {
        MessageBox.Show("Logged in");
    }
    else
    {
        MessageBox.Show(response.message);
    }
});
```

You can also use callback-based versions of the main methods, including:

- `Init`
- `Login`
- `Register`
- `GetVariable`
- `GetChats`
- `ValidateSession`

## Logging

By default, SDK logging is enabled.

Logs are written to:

`C:\ProgramData\AuthlyX\{AppName}\YYYY_MM_DD.log`

To disable logs:

```csharp
debug: false
```

Sensitive values such as passwords, secrets, and signatures are masked automatically.

## Method Name Casing

The SDK accepts both PascalCase and lowercase method names.

Examples:

```csharp
AuthlyXApp.Login("username", "password");
AuthlyXApp.login("username", "password");

AuthlyXApp.Init();
AuthlyXApp.init();
```

## Notes

- The SDK currently supports both `sid` and `hwid` for compatibility with older integrations.
- If you are starting fresh, treat `sid` as the preferred system identifier concept.
- The example apps in this folder are reference integrations, not required project structure.

## Reference Examples

Reference apps included in this folder:

- `AuthlyX C# Example (Console)`
- `AuthlyX C# Example (Form)`
