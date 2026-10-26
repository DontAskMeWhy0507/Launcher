# Release Audit & Performance Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix all audited blockers, stability bugs, performance bottlenecks, resource leaks, and packaging issues to prepare Launcher for official v3.1.0 production release.

**Architecture:**
1. Fix data integrity & encoding in `MainViewModel` (Vietnamese diacritics normalization).
2. Clean up memory and COM/GDI resource leaks in `LauncherService` and implement hash-based icon caching to prevent disk thrashing.
3. Fix packaging, silent update restart loop, and Inno Setup configuration in `LauncherSetup.iss` and `MainViewModel`.
4. Fix `NotifyIcon` crash risk on publish and ensure clean tray icon disposal in `MainWindow` and `App`.
5. Fix IPC concurrency in `App` (named pipe non-blocking dispatch) and modal dialog safety in `UserDialog`.
6. Fix registry access rights in `StartupService`.
7. Optimize performance: throttle gamepad polling when minimized/inactive, add ReadyToRun compilation in `Launcher.csproj`, clean up unused NuGet packages and git index.

**Tech Stack:** C#, .NET 8.0 WPF, CommunityToolkit.Mvvm, Inno Setup 6, Windows Win32/COM APIs.

**Spec:** [release_audit_report.md](file:///C:/Users/tuant/.gemini/antigravity/brain/1d11e119-db76-4b2a-b8c6-3910f48274c6/release_audit_report.md)

## Global Constraints
- Target Framework: `net8.0-windows10.0.19041.0` (Windows 10/11 x64).
- Release Version: `3.1.0` (Assembly/File: `3.1.0.0`, Inno Setup: `3.1.0`).
- No hardcoded absolute machine paths in `.csproj`.
- Preserve existing styling and behavior while fixing bugs and leaks.

## Review Focus
- Vietnamese diacritics (`ỨNG DỤNG`, `YÊU THÍCH`) must not be converted to `?` or corrupted when loading or saving.
- Silent auto-update must reliably restart the updated `Launcher.exe`.
- No GDI or COM leaks when extracting icons repeatedly.
- App startup must not crash when `Assembly.Location` is empty.
- Opening a second instance must reliably bring the existing instance to the foreground even if the UI is busy.

---

### Task 1: Fix Vietnamese Encoding & Tab/Category Normalization

**Files:**
- Modify: `ViewModels/MainViewModel.cs:236-284`

**Interfaces:**
- `NormalizeVietnameseText(string? value) -> string`: Cleans leading/trailing whitespace, preserves valid Vietnamese Unicode characters without damaging Latin1 conversion.
- `NormalizeTab(string? tab) -> string`: Safely maps `"YÊU THÍCH"`, `"KHO GAME"`, `"ỨNG DỤNG"`.
- `NormalizeCategory(string? category) -> string`: Safely maps `"KHO GAME"`, `"ỨNG DỤNG"`.

- [ ] **Step 1: Inspect and fix `NormalizeVietnameseText` in `ViewModels/MainViewModel.cs`**
  Remove the destructive `Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(trimmed))` logic that transforms diacritics into `?`. Replace with clean string trimming and only repair mojibake if characteristic corrupted sequences (like `Ã` or `á»`) are present.
- [ ] **Step 2: Verify `NormalizeTab` and `NormalizeCategory` matching**
  Ensure `"YÊU THÍCH"` matches `FavoriteTab`, `"KHO GAME"` matches `GameCategory`, and `"ỨNG DỤNG"` matches `AppCategory`.
- [ ] **Step 3: Run compilation check**
  Run: `dotnet build`
  Expected: Build succeeded with 0 errors.

---

### Task 2: Fix COM & GDI Leaks, and Implement Hash-Based Icon Caching

**Files:**
- Modify: `Services/LauncherService.cs:89-188, 212-268, 303-342`

**Interfaces:**
- `GetIconPathAsync(string filePath) -> Task<string>`: Generates deterministic cached PNG path based on MD5 hash of target path so icons are reused rather than generating endless timestamped files.
- `TryExtractModernAsync(string path, string outputPath) -> Task<bool>`: Releases `IShellItemImageFactory` via `Marshal.ReleaseComObject`.
- `GetLegacyIconPathAsync(string filePath, string iconPath) -> Task<string>`: Properly destroys small icon handle from `ExtractIconEx` via `DestroyIcon` and releases `IImageList`.
- `ResolveShortcut(string path) -> LaunchTarget`: Releases `shortcut` and `shell` COM objects cleanly.

- [ ] **Step 1: Refactor icon caching in `GetIconPathAsync`**
  Compute MD5/SHA256 hex string of normalized file path + last write time:
  If file exists in `%APPDATA%\Launcher\Icons\<hash>.png`, return it immediately.
- [ ] **Step 2: Add COM `Marshal.ReleaseComObject` in `TryExtractModernAsync` and `GetLegacyIconPathAsync`**
  Add `finally { if (factory != null) Marshal.ReleaseComObject(factory); }` and `if (iml != null) Marshal.ReleaseComObject(iml);`.
- [ ] **Step 3: Fix small icon handle leak in `ExtractIconEx`**
  Change `ExtractIconEx(filePath, 0, out hIcon, out IntPtr hSmallIcon, 1)` and destroy `hSmallIcon` if non-zero.
- [ ] **Step 4: Release COM object for shortcut in `ResolveShortcut`**
  Release `shortcut` object in `finally` block before releasing `shell`.
- [ ] **Step 5: Run compilation check**
  Run: `dotnet build`
  Expected: Build succeeded with 0 errors.

---

### Task 3: Fix Inno Setup Auto-Update Restart & Installer Configuration

**Files:**
- Modify: `LauncherSetup.iss:1-25`
- Modify: `ViewModels/MainViewModel.cs:600-607`

**Interfaces:**
- Inno Setup `[Setup]`: Adds `CloseApplications=yes`, sets `AppVersion=3.1.0`.
- Inno Setup `[Run]`: Removes `skipifsilent` so that silent update automatically starts `Launcher.exe`.
- `MainViewModel.UpdateLauncherAsync`: Launches installer with appropriate arguments for clean upgrade.

- [ ] **Step 1: Update `LauncherSetup.iss`**
  - Set `AppVersion=3.1.0`.
  - Add `CloseApplications=yes` and `RestartApplications=no` in `[Setup]`.
  - In `[Run]`, remove `skipifsilent` so the installed application launches upon installation completion.
- [ ] **Step 2: Update `MainViewModel.UpdateLauncherAsync`**
  Use `Arguments = "/silent /closeapplications"` when executing installer.
- [ ] **Step 3: Verify syntax in `LauncherSetup.iss`**

---

### Task 4: Fix `NotifyIcon` Crash Risk and Disposal

**Files:**
- Modify: `MainWindow.xaml.cs:281-302`
- Modify: `App.xaml.cs:208-218`

**Interfaces:**
- `MainWindow.SetupTrayIcon()`: Safely retrieves executable icon using `Environment.ProcessPath`, falls back to `Assets\Icons\apps.ico` if null/empty.
- `MainWindow.DisposeTrayIcon()`: Exposes disposal method so tray icon is removed cleanly on all exit paths.
- `App.OnExit(ExitEventArgs e)`: Calls `_mainWindow?.DisposeTrayIcon()`.

- [ ] **Step 1: Rewrite `SetupTrayIcon` in `MainWindow.xaml.cs`**
  Use `Environment.ProcessPath` with fallback to `Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "apps.ico")`.
- [ ] **Step 2: Add `DisposeTrayIcon` to `MainWindow` and hook into `App.OnExit`**
  Ensures the tray icon is removed from Windows notification area when application terminates.
- [ ] **Step 3: Run compilation check**
  Run: `dotnet build`
  Expected: Build succeeded with 0 errors.

---

### Task 5: Fix NamedPipe Server Concurrency & UserDialog Safety

**Files:**
- Modify: `App.xaml.cs:137-163`
- Modify: `Services/UserDialog.cs:23-34`

**Interfaces:**
- `App.StartNamedPipeServer()`: Uses `Dispatcher.BeginInvoke` instead of synchronous `Dispatcher.Invoke` so pipe server thread is not blocked by UI modals.
- `UserDialog.Show()`: Validates that `Application.Current.MainWindow` is non-null, loaded, and visible before assigning it as `Owner`.

- [ ] **Step 1: Update `StartNamedPipeServer` in `App.xaml.cs`**
  Replace `Dispatcher.Invoke(() => ShowMainWindow())` with `Dispatcher.BeginInvoke(new Action(ShowMainWindow))` and specify `NamedPipeServerStream.MaxAllowedServerInstances`.
- [ ] **Step 2: Update `UserDialog.cs`**
  Safely set `Owner` only if `Application.Current.MainWindow is { IsLoaded: true, IsVisible: true }`.
- [ ] **Step 3: Run compilation check**
  Run: `dotnet build`
  Expected: Build succeeded with 0 errors.

---

### Task 6: Fix `StartupService` Registry Permissions & Metadata

**Files:**
- Modify: `Services/StartupService.cs:11-36`
- Modify: `app.manifest:1-16`

**Interfaces:**
- `StartupService.IsEnabled() -> bool`: Opens registry with `writable: false`. Does not mutate registry inside read query.
- `app.manifest`: Updates assembly identity name to `Launcher.App`.

- [ ] **Step 1: Update `StartupService.IsEnabled`**
  Change `Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false)`. Move the auto-repair of `--background` to `SetEnabled` or a separate upgrade step.
- [ ] **Step 2: Update `app.manifest`**
  Change `name="RevoltLauncher.App"` to `name="Launcher.App"`.
- [ ] **Step 3: Run compilation check**
  Run: `dotnet build`
  Expected: Build succeeded with 0 errors.

---

### Task 7: Performance Optimizations & Dependencies Clean-Up

**Files:**
- Modify: `Launcher.csproj:1-36`
- Modify: `Services/ControllerService.cs:57-95`
- Modify: `ViewModels/MainViewModel.cs:443-465`

**Interfaces:**
- `Launcher.csproj`:
  - Remove hardcoded `Accessibility.dll` `<Reference>`.
  - Remove unused `<PackageReference Include="SharpDX.XInput" />`.
  - Adjust `System.Drawing.Common` to `8.0.0`.
  - Add `<PublishReadyToRun>true</PublishReadyToRun>` for faster cold startup.
- `ControllerService`: Throttles polling when no gamepad is active or window is minimized.
- `MainViewModel.LoadScanResultIconsAsync`: Throttles background icon loading with `SemaphoreSlim` so disk I/O doesn't stutter UI.

- [ ] **Step 1: Clean up `Launcher.csproj`**
  Remove unused references and add ReadyToRun property.
- [ ] **Step 2: Optimize `ControllerService` polling**
  When `_activeGamepad == null`, delay 1500ms; add `Pause()` / `Resume()` or check active status.
- [ ] **Step 3: Optimize `LoadScanResultIconsAsync`**
  Batch/throttle icon extraction to prevent disk thrashing.
- [ ] **Step 4: Clean up Git tracking for `bin/` and `obj/`**
  Run `git rm -r --cached bin obj` to untrack build artifacts.
- [ ] **Step 5: Full verification build and publish**
  Run: `dotnet build -c Release`
  Run: `dotnet publish -c Release -o dist\Launcher`
  Expected: Succeeded with 0 warnings, 0 errors.
