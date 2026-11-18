# AuthlyX C# SDK

The SDK project. It builds `AuthlyX.dll` for .NET Standard 2.0, .NET 8 and .NET Framework 4.8.

Reference this project from your app, or copy `AuthlyX.cs` into your project and add these NuGet packages:

- `BouncyCastle.Cryptography`
- `Newtonsoft.Json`
- `System.Security.Cryptography.ProtectedData`

```csharp
using AuthlyXSDK;

public static AuthlyX app = new AuthlyX(appKey: "YOUR_APP_KEY", version: "1.0.0");
```

Usage and examples: https://docs.authlyx.dev/sdks/methods/login
