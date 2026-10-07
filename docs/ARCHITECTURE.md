# Architecture

Acorn is an MVC application hosted in a Photino.Blazor window (Blazor Hybrid). ASP.NET Core MVC is not
used because it needs a web server and a port; the MVC split is applied to plain C# classes and Razor
components instead.

```
┌──────────────────────────── Acorn.App (View + host) ───────────────────────────┐
│ Views/, Components/   Razor: render AppState, call controllers. No logic.       │
│ Shell/                Root component + JsBridge (Ctrl/Cmd+K, activity pings)    │
│ Platform/             Windows / macOS / Linux implementations of abstractions   │
│ Program.cs            Composition root: DI, Photino window, shutdown hooks      │
│ wwwroot/              index.html (CSP), CSS, fonts, acorn.js — all local         │
└──────────────┬───────────────────────────────────────────────▲─────────────────┘
               │ calls                                          │ Changed event
┌──────────────▼──────────── Acorn.Application (Controller) ────┴─────────────────┐
│ Controllers/  Unlock, Vault, Entry, Settings, Backup                             │
│ AppState      single UI state; mutated only by controllers (internal setters)    │
│ ViewModels/   DTOs for views — no secret values except an explicitly revealed one │
│ Abstractions/ IClipboardService, ISessionEvents, IScreenProtection,              │
│               IAutoLockTimer, INavigator                                         │
│ Services/     ClipboardGuard (R5 policy), AutoLockTimer, AppStateNavigator, …    │
└──────────────┬──────────────────────────────────────────────────────────────────┘
               │ IVaultSession
┌──────────────▼──────────────────── Acorn.Core (Model) ──────────────────────────┐
│ VaultSession  create / unlock / recover / lock / save / rekey / restore         │
│ Crypto/       Kdf, Aead, KeyWrap, RecoveryKey, SecretText                       │
│ Format/       VaultCodec, VaultCrypto, VaultSerializer, Migrations/             │
│ Storage/      AtomicFileWriter, BackupRotator, VaultLock, VaultStore, VaultPaths │
│ Models/       VaultData, Entry, CustomField, VaultSettings, SshCommand          │
│ Security/     PasswordPolicy, PasswordStrength, PasswordGenerator               │
└─────────────────────────────────────────────────────────────────────────────────┘
```

## Layer rules (SPEC 3.1) and how they are enforced

| Rule | Enforced by |
|---|---|
| Core references neither Application nor App, nor any UI/platform assembly | `LayerTests.Core_does_not_reference_application_app_or_ui` |
| Application references no App, Blazor, Photino, TextCopy, `Microsoft.Win32`, `System.Windows` | `LayerTests.Application_does_not_reference_app_or_ui` and `…_do_not_depend_on_ui_platform_interop_or_network_apis` (also forbids P/Invoke and `System.Net.Http`/sockets) |
| Core and Application contain no network APIs | NetArchTest rules in `LayerTests` |
| Controllers get dependencies through one public constructor and hold no static state | `Controllers_take_dependencies_only_through_one_public_constructor_and_hold_no_static_state` |
| Views/Components inject only controllers and `AppState` | `ViewLayerTests.Views_and_components_inject_only_controllers_and_app_state` (reflection over `[Inject]` properties of the compiled components) |
| Views/Components never touch Core, abstractions, services, platform code, JS interop, `System.IO`, `System.Net` or crypto | `ViewLayerTests.Views_do_not_depend_on_…` (NetArchTest over the compiled Razor types) |
| Views unsubscribe from `AppState` | all state-bound views derive from `AppStateView`, which implements `IDisposable`; checked by `Views_unsubscribe_from_app_state_by_implementing_IDisposable` |
| The entry-list view model has no password value | `LayerTests.Entry_list_view_model_has_no_password_value`, plus `EntryControllerTests.List_and_detail_view_models_never_contain_secret_values` |

`Shell/` (root component and `JsBridge`) is host glue rather than a view: it is the only UI code allowed to
inject `IJSRuntime`. It passes keyboard shortcuts and activity pings to controllers and never handles a
secret value.

## Key flows

**Unlock** — `UnlockView` → `UnlockController.UnlockWithPasswordAsync(pw)` → `Task.Run(IVaultSession.UnlockWithPassword)`
(Argon2id off the UI thread) → `VaultController.EnterUnlocked` applies settings (theme, screen protection, auto-lock
timer), builds view models through `VaultProjection`, sets `AppState.Status = Unlocked` →
`INavigator.GoTo(Screen.Vault)` → `AppState.Changed` → views re-render.

**Lock** (manual, idle timer, screen lock, sleep, sign-out, minimize) — `VaultController.LockAsync`:
1. `ClipboardGuard.ClearIfOwnedAsync()` — clears only if the clipboard still holds Acorn's value
2. `IVaultSession.Lock()` — zeroes the DEK and drops decrypted data
3. stop the auto-lock timer; `AppState.ResetForLock` drops view models, revealed secrets and editors
4. screen-capture protection back on; navigate to the Unlock screen

The order is verified with NSubstitute's `Received.InOrder` in `VaultControllerTests`. Window close and
`ProcessExit` call `VaultController.ShutdownAsync` (clipboard + zero keys). `ISessionEvents` and
`IAutoLockTimer` raise events that the controller turns into `LockAsync`.

**Copy** — `EntryController.CopyAsync(id, field)` reads the value from the session and hands it to
`ClipboardGuard`. Secrets get a clear timer (settings, default 30 s); the value is never returned to the view.

**Reveal** — `EntryController.RevealSecretAsync(secretRef)` puts one value into `AppState.RevealedSecrets`
with a timer (default 12 s). Navigation, selecting another entry, opening the editor and locking all clear it.

**Save** — controllers mutate `IVaultSession.Data` and call `Save()`; on failure they roll the in-memory
change back so the UI never shows unsaved data as saved.

## Threading

* Controllers run on the Blazor dispatcher. KDF work (`Create`, `Unlock`, `Recover`, password changes) is
  pushed to the thread pool with `Task.Run` while `AppState.IsBusy` disables the UI.
* Timers (`TimeProvider`) and OS events arrive on other threads. `VaultSession` serialises access with a
  lock. `AppState.RevealedSecrets` is swapped atomically, and views marshal `Changed` through
  `InvokeAsync(StateHasChanged)`.
* `TimeProvider` (BCL) plays the role of the `IClock` abstraction mentioned in the SPEC. Tests use
  `FakeTimeProvider` to drive the clipboard, reveal and auto-lock timers.

## Platform services

| Abstraction | Windows | macOS | Linux |
|---|---|---|---|
| `IClipboardService` | Win32 clipboard with history/cloud exclusion formats | TextCopy | TextCopy |
| `ISessionEvents` | `SystemEvents` (SessionSwitch, PowerModeChanged, SessionEnding) + minimize | Obj-C runtime observer (screenIsLocked, will sleep, screens sleep, session resign) + minimize | minimize only |
| `IScreenProtection` | `SetWindowDisplayAffinity` | `NSWindow.sharingType` | unsupported |
| `IAutoLockTimer` | `AutoLockTimer` (shared, in Application) | same | same |
| `INavigator` | `AppStateNavigator` (shared, in Application) | same | same |

`MainWindowHolder` gives platform services the Photino window (native handle, created/minimized events)
without exposing Photino to the controllers.

## Adding a feature

1. Model rules and data go into `Acorn.Core` with unit tests.
2. A controller method validates input, calls `IVaultSession`/services and updates `AppState`; it returns
   `Result`/`Result<T>` with a generic Thai message (`Messages`), never exception text.
3. The view binds to `AppState` or a view model and calls the controller. If it holds typed secrets,
   it clears them in `ClearSecrets()`.
4. Run `dotnet test --solution Acorn.sln`; the architecture tests catch layer violations.
