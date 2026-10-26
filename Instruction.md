# Developer Instruction

Tai lieu nay mo ta workflow chuan cho developer khi phat trien, test, dong goi va release Launcher.

## 1. Nhan requirement

Moi yeu cau nen duoc viet lai thanh cac dau viec co the kiem tra duoc.

Vi du:

- Them trang Settings rieng, khong phai tab.
- Scan khong lam lag UI.
- Ket qua scan co icon preview.
- Update phai co buoc kiem tra truoc khi tai/cai.
- Ban release moi phai hien dung version trong app.

Nen tach requirement thanh cac nhom:

- UI
- Logic
- Luu setting
- Build/package
- Test
- Release

## 2. Chon version target

Dung SemVer:

```text
MAJOR.MINOR.PATCH
```

Vi du:

- `1.2.2` da public nhung sai version.
- Ban sua tiep theo phai la `1.2.3`.
- Khong nen upload lai asset vao tag cu neu user hoac auto-updater da co the nhin thay tag do.

Best practice:

- Moi GitHub release tag nen duoc xem nhu immutable.
- Neu release sai, tao tag moi.
- Dung ten tag nhat quan, vi du `V1.2.3`.

## 3. Code feature

Nhung file thuong can sua:

- `MainWindow.xaml`: UI.
- `MainWindow.xaml.cs`: hanh vi cua window.
- `ViewModels/MainViewModel.cs`: logic, command, binding state.
- `Models/LauncherSettings.cs`: setting luu JSON.
- `Launcher.csproj`: version app.
- `LauncherSetup.iss`: version installer va dong goi Inno Setup.

Best practice khi code:

- Binding vao property chi-doc trong WPF phai dat `Mode=OneWay`.
- Cac tac vu nang nhu scan o dia, registry, icon extraction phai chay background.
- Khong extract icon hang loat truoc khi hien UI neu lam lag; hien ket qua truoc, load icon sau.
- Updater khong nen tu ghi de exe dang chay; nen tai installer, chay installer, roi app thoat.
- Khong sua output `bin`, `obj`, `dist`, `Output` bang tay.

## 4. Bump version truoc khi release

Phai sua ca app version va installer version.

Trong `Launcher.csproj`:

```xml
<Version>1.2.3</Version>
<FileVersion>1.2.3.0</FileVersion>
<AssemblyVersion>1.2.3.0</AssemblyVersion>
```

Trong `LauncherSetup.iss`:

```ini
AppVersion=1.2.3
```

Best practice:

- `Launcher.csproj`, `LauncherSetup.iss`, va GitHub tag phai cung version.
- Neu tag la `V1.2.3`, app nen hien `v1.2.3`.

## 5. Build Debug

Chay:

```powershell
dotnet build
```

Ket qua mong doi:

```text
Build succeeded.
0 Warning(s)
0 Error(s)
```

Neu build fail:

- Doc error dau tien.
- Sua code.
- Build lai.
- Khong publish khi build con loi.

## 6. Publish Release

Chay:

```powershell
dotnet publish -c Release -o dist\Launcher
```

Output chinh:

```text
F:\launcher\dist\Launcher\Launcher.exe
```

## 7. Kiem tra version cua ban publish

Chay:

```powershell
[System.Diagnostics.FileVersionInfo]::GetVersionInfo('F:\launcher\dist\Launcher\Launcher.exe') |
Select-Object FileVersion,ProductVersion,OriginalFilename
```

Ket qua phai dung version target, vi du:

```text
FileVersion    1.2.3.0
ProductVersion 1.2.3+...
```

Neu van hien version cu:

- Kiem tra lai `Launcher.csproj`.
- Chay lai `dotnet build`.
- Chay lai `dotnet publish`.

## 8. Smoke test runtime

Chay app publish vai giay:

```powershell
F:\launcher\dist\Launcher\Launcher.exe
```

Sau do kiem tra log:

```text
%APPDATA%\Launcher\Launcher-error.log
```

Neu co log loi moi thi khong release.

Nhung loi can de y:

- WPF binding vao read-only property ma khong co `Mode=OneWay`.
- Crash khi startup.
- Icon/path asset bi mat.
- Update command loi.
- Scan lam UI dung.

## 9. Build installer bang Inno Setup

Chay:

```powershell
& 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' 'LauncherSetup.iss'
```

Output:

```text
F:\launcher\Output\LauncherSetup.exe
```

Neu `ISCC.exe` khong ton tai trong PATH, dung duong dan mac dinh tren. Neu van khong co, cai Inno Setup 6.

## 10. Kiem tra installer

Kiem tra file ton tai:

```powershell
Get-Item Output\LauncherSetup.exe | Select-Object FullName,Length,LastWriteTime
```

Kiem tra version installer:

```powershell
[System.Diagnostics.FileVersionInfo]::GetVersionInfo('F:\launcher\Output\LauncherSetup.exe') |
Select-Object FileVersion,ProductVersion
```

`ProductVersion` phai trung voi version release.

## 11. Cai thu local

Truoc khi cai thu, thoat app dang chay:

```powershell
Get-Process Launcher -ErrorAction SilentlyContinue
```

Neu con process:

```powershell
Stop-Process -Name Launcher -Force
```

Sau do chay:

```text
F:\launcher\Output\LauncherSetup.exe
```

Kiem tra sau khi cai:

- App mo duoc.
- Settings hien dung version.
- Scan chay duoc.
- Ket qua scan co icon.
- Nut `KIEM TRA` update hoat dong.
- Neu co ban moi, nut `UPDATE` tai va chay installer.

Best practice:

- Luon test installer, khong chi test file trong `dist`.
- User dung installer, nen loi packaging phai duoc bat truoc release.

## 12. Tao GitHub Release

Tao tag moi, vi du:

```text
V1.2.3
```

Title:

```text
V1.2.3
```

Release notes vi du:

```text
- Fix app version metadata
- Add scan result icons
- Add check update before update
- Improve scan responsiveness
```

Upload asset:

```text
F:\launcher\Output\LauncherSetup.exe
```

Best practice:

- Asset nen ten la `LauncherSetup.exe`.
- Updater dang tim asset `.exe` co chu `Setup`.
- Khong upload source zip thay cho installer.
- Khong upload installer cu vao tag moi.
- Khong sua lai release da public neu user co the da thay release do; tao release moi.

## 13. Verify GitHub Release

Sau khi upload:

- Mo trang GitHub Releases.
- Dam bao latest release la tag vua tao.
- Dam bao asset co `LauncherSetup.exe`.
- Neu can, tai thu asset ve may.

Updater goi:

```text
https://api.github.com/repos/DontAskMeWhy0507/Launcher/releases/latest
```

Vi vay latest release phai dung.

## 14. Test auto update

Quy trinh test:

1. Cai mot ban cu hon, vi du `1.2.2`.
2. Mo app.
3. Vao Settings.
4. Bam `KIEM TRA`.
5. App phai bao co ban moi, vi du `V1.2.3`.
6. Bam `UPDATE`.
7. App tai installer ve `%TEMP%`.
8. Installer chay silent.
9. App thoat de installer ghi de.
10. Mo lai app.
11. Version trong app phai la version moi.

Neu app van bao version cu:

- Kiem tra `Launcher.csproj`.
- Kiem tra ban publish co dung version khong.
- Kiem tra asset upload len GitHub co phai installer moi khong.
- Kiem tra latest release co dung tag moi khong.

## 15. Checklist truoc moi release

```text
[ ] Requirement da ro
[ ] Code xong
[ ] Version trong Launcher.csproj da bump
[ ] AppVersion trong LauncherSetup.iss da bump
[ ] dotnet build pass
[ ] dotnet publish pass
[ ] Launcher.exe publish bao dung version
[ ] Smoke test khong sinh log loi moi
[ ] ISCC build installer pass
[ ] Cai thu installer local pass
[ ] GitHub release tag moi
[ ] Upload dung LauncherSetup.exe
[ ] Test check/update tu ban cu
```

## 16. Nhung viec khong nen lam

Tranh cac loi sau:

- Quen bump version trong `Launcher.csproj`.
- Quen bump `AppVersion` trong `LauncherSetup.iss`.
- Reuse tag cu cho ban da public.
- Release khi chua smoke test.
- Upload nham installer cu.
- Chi test `dist\Launcher\Launcher.exe` ma khong test installer.
- De GitHub latest release tro vao ban loi.
- De binding WPF vao read-only property ma khong co `Mode=OneWay`.

## 17. Lenh nhanh cho release

Thay version trong `Launcher.csproj` va `LauncherSetup.iss`, sau do chay:

```powershell
dotnet build
dotnet publish -c Release -o dist\Launcher
[System.Diagnostics.FileVersionInfo]::GetVersionInfo('F:\launcher\dist\Launcher\Launcher.exe') | Select-Object FileVersion,ProductVersion
& 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' 'LauncherSetup.iss'
Get-Item Output\LauncherSetup.exe | Select-Object FullName,Length,LastWriteTime
```

Neu tat ca pass, upload:

```text
F:\launcher\Output\LauncherSetup.exe
```

vao GitHub Release tag moi.
