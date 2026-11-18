# AuthlyX C# SDK

Official C# SDK for AuthlyX V3 (3.0.0 beta), with a console example and a WinForms example.

Full documentation: https://docs.authlyx.dev

## Requirements

- Windows
- .NET Framework 4.8, or any target that supports .NET Standard 2.0
- Visual Studio 2022 or newer, or the .NET SDK

## Run the example

Open `AuthlyX C# Example.sln` in Visual Studio. Put your app key and version in `Program.cs` of the project you want to run:

```csharp
public static AuthlyX app = new AuthlyX(appKey: "YOUR_APP_KEY", version: "1.0.0");
```

Then set the Console or Form project as the startup project and press F5. NuGet restores the packages on the first build.

- **Console:** a menu for every SDK method.
- **Form:** a sign-in window, then a dashboard with tabs for Account, Variables, Features, Scripts, Logs and Session. Results show in the status bar at the bottom.

You can find your app key under your app's credentials in the [dashboard](https://dash.authlyx.dev).

## Add it to your own project

Copy `AuthlyX.cs` into your project and add these NuGet packages:

- `BouncyCastle.Cryptography`
- `Newtonsoft.Json`
- `System.Security.Cryptography.ProtectedData`

You can also reference the `AuthlyX.SDK` project instead of copying the file.

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
```

`initialize` returns one of these codes:

| Code | Success | Meaning |
| --- | --- | --- |
| `APP_INITIALIZED` | Yes | Connected. Continue to sign-in. |
| `OFFLINE_MODE` | Yes | No internet, but this PC has saved offline access. |
| `NETWORK_...` | No | No internet and nothing saved. Offer to try again. |
| `UPDATE_REQUIRED` | No | This version is older than the one set in your dashboard. |
| `VERSION_MISMATCH` | No | This version is newer than the dashboard version and is not allowed. |

Every method takes a callback, and most also have a Task version you can `await`. In a WinForms or WPF app, never call `.Result` or `GetAwaiter().GetResult()` on the UI thread.

## Signing in

```csharp
app.login("YOUR_USERNAME", "YOUR_PASSWORD", result => Console.WriteLine(result.ok ? "Signed in" : result.message));
app.login("YOUR_LICENSE_KEY", result => Console.WriteLine(result.ok ? "Signed in" : result.message));
app.deviceLogin(result => Console.WriteLine(result.ok ? "Signed in" : result.message));
```

Device login signs in the current Windows user without a form. The device has to be approved in your app first. See [Device login](https://docs.authlyx.dev/guides/device-login).

After signing in, the account details are in `userData`:

```csharp
var username = app.userData.Username;
var plan = app.userData.Plan;
var expiry = app.userData.ExpiryDate;
```

Every method has its own page with examples under [SDK methods](https://docs.authlyx.dev/sdks/methods/login).

## Updates

The SDK shows update messages for you on Windows, so you do not need to build that screen:

- **Refused version** (`UPDATE_REQUIRED` or `VERSION_MISMATCH`): an **Update required** or **Version not supported** box when the app starts, with the customer's version, the latest version, your release notes and an option to open the download page. `initialize` then returns a failed result.
- **Allowed version with a newer release:** an **Update available** box once after sign-in. The customer can choose No and keep working.

Set the version, allowed versions and download links in the dashboard under your app's **Updates** settings. To show your own screen instead, turn the box off:

```csharp
public static AuthlyX app = new AuthlyX(appKey: "YOUR_APP_KEY", version: "1.0.0", updatePrompt: false);
```

The failed result's `data["update"]` then has `version`, `notes` and `url`. More in [Updates](https://docs.authlyx.dev/guides/updates) and [Allowed Versions](https://docs.authlyx.dev/guides/allowed-versions).

## Offline access

Once someone is signed in, saving offline access stores a signed permit on that PC:

```csharp
app.saveOfflineAccess(result => Console.WriteLine(result.ok ? "Offline access saved" : result.message));
var available = app.HasOfflineAccess;
```

If they open your app later without internet, `initialize` succeeds with the code `OFFLINE_MODE`, and they sign in with the same username and password or license key, and the SDK checks it against the saved permit. Each permit belongs to one app and one account, and signing out removes it. More in [Offline access](https://docs.authlyx.dev/guides/offline).

## Errors

A failed call gives you a `code` and a `message` you can show to the user. `initialize` always returns a result, even without internet. The callback versions of the other methods do too. The Task versions you `await` raise `AuthlyXException` for network problems and setup errors, so wrap those in `try`/`catch`. All codes are listed in [Errors](https://docs.authlyx.dev/api-reference/errors).

## Coming from 2.4

V3 is a new SDK, not a drop-in update. Apps use an app key instead of the old owner ID, app name and secret, and the method names changed. Read [Move from V2 to V3](https://docs.authlyx.dev/api-reference/migration) before you switch.

## License

MIT. See `LICENSE.txt`.
