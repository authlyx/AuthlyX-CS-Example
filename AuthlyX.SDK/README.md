# AuthlyX C# SDK

Official C# SDK for [AuthlyX](https://authlyx.dev) V3 (3.0.0 beta). It handles user and license sign-in, device locks, variables, scripts, offline access and update prompts for your Windows app.

Targets .NET Standard 2.0, .NET 8 and .NET Framework 4.8.

```bat
dotnet add package AuthlyX --prerelease
```

## Quick start

```csharp
using AuthlyXSDK;

public static AuthlyX app = new AuthlyX(appKey: "YOUR_APP_KEY", version: "1.0.0");

app.initialize(result =>
{
    if (!result.ok)
        Console.WriteLine(result.message);
    else if (result.code == "OFFLINE_MODE")
        Console.WriteLine(result.message);
});

app.login("YOUR_USERNAME", "YOUR_PASSWORD", result =>
    Console.WriteLine(result.ok ? "Signed in as " + app.userData.Username : result.message));
```

You can find your app key under your app's credentials in the [dashboard](https://dash.authlyx.dev).

## Updates

On Windows the SDK shows an **Update required**, **Version not supported** or **Update available** box with your release notes and a link to the download page. Set versions and download links under your app's **Updates** settings. To show your own screen instead, pass `updatePrompt: false` to the constructor.

## Links

- [Documentation](https://docs.authlyx.dev)
- [SDK methods](https://docs.authlyx.dev/sdks/methods/login)
- [Console and WinForms examples](https://github.com/authlyx/AuthlyX-CS-Example)
- [Move from V2 to V3](https://docs.authlyx.dev/api-reference/migration)
