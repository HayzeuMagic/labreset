# Lab Reset

A small Windows app that resets lab VMs and files at the end of a class. Install it on each lab PC. It works only on the PC it runs on:

- extract fresh copies of the lab archives, and
- find and delete the copies students extracted.

All lab archives are stored **inside the app** (in its `Library` folder), so the teacher just ticks one and clicks Extract.

## What's in the folder

| File | What it is |
|---|---|
| `LabReset.exe` | The app. It asks for administrator rights when it starts. |
| `Library\` | All lab archives (built-in and imported) plus `builtin.txt`, the list of built-in ones. |
| `7zip\` | 7-Zip, used for `.7z`, `.rar` and `.tar.gz` archives (free software, see `7zip\License.txt`). |
| `LabReset.settings.ini` | Saved settings (created automatically). |
| `Logs\` | One log file per day: everything extracted, scanned and deleted. |
| `add-builtin.bat` | For the admin: adds every archive in a folder as built-in. |
| `build.bat` + `src\` | Source code. Double-click `build.bat` to rebuild `LabReset.exe`. |

### Built-in archives
These were copied from `C:\__RivanVMs`. They can't be removed from inside the app:

- `.7z`: c9800-CL, csr1000v (VOIP Features), Forti, IR-ElasticSys, NetOPS, PaloAltoVM, RSTHayup-v2, yVM-v6
- `.zip`: D3PentestVM, FGT_VM64 (FortiGate)
- `.rar`: w2022, WAZUHv2
- `.tar.gz`: zabbix_appliance-7.4.6-vmx

The `.ova` files are not included. They are imported into VMware, not extracted.

The whole app folder is about 33 GB because of the archives.

## Installing on each lab PC

### Lab PCs that already have the VMs (recommended)
If a PC already has the archives in **`C:\__RivanVMs`** or **`D:\__RivanVMs`**, the app uses them where they are. Nothing big is copied.

1. Get the app folder onto the PC with `git clone <repo-url> C:\LabReset`, or copy the small folder (without `Library\`).
2. Double-click **`build.bat`**. It creates `LabReset.exe` using the compiler built into Windows.
3. Run `LabReset.exe`. The archives appear with Source `C:\__RivanVMs` (or `D:\__RivanVMs`).
4. The destination and search folders are set automatically to the drive that has the VMs.

Archives in `__RivanVMs` are never deleted by the app; only the extracted VM folders are.

### Lab PCs without the VMs
Copy the whole `C:\LabReset` folder, including `Library\` (about 31 GB), to each PC, for example with a USB drive or a network share.

### GitHub
`.gitignore` keeps the archives (`Library\*`), `LabReset.exe`, logs and settings out of Git. GitHub rejects files over 100 MB, so the archives can't be pushed.

Students can't misuse it: the app needs an administrator password to start.

## Using it

**1. Lab archives**
- Tick the archive(s) you want.
- **Import archive...** (or drag files onto the list) adds a new `.zip`, `.7z`, `.rar`, `.tar` or `.tar.gz`. It is copied into `Library\`, so it stays in the app even if the original is deleted.
- **Remove imported** deletes an archive you imported. Built-in archives can't be removed.

**2. Extract (start of class)**
- *Destination folder*: default `C:\__RivanVMs`.
- *Put each archive in its own folder*: wraps loose files in a folder named after the archive. Archives that already contain one folder (like `Forti.7z` → `Forti\`) are not wrapped twice.
- *Remove the old copy first*: deletes the previous copy before extracting, for a clean reset.
- Click **Extract ticked archives**. Progress is shown as a %. **Stop** cancels.
- Close VMware first if the VM is open.

**3. Find and delete (end of class)**
- *Search only in*: default `C:\Users; C:\__RivanVMs`. Leave it empty to search every drive (slower).
- Click **Scan**. The list shows the path, source archive, how closely it matches (e.g. `94% (17/18 files)`) and date modified.
- Tick what to remove, click **Delete ticked items...** and confirm. Items are deleted permanently.
- Students may leave extra files in a VM folder (logs, snapshots); these are deleted along with the folder.

### Safety rules (built in)
- **Never searched:** Windows, Program Files, ProgramData, AppData, $Recycle.Bin and other system folders.
- **Never deleted:**
  - drive roots
  - the `Users` folder and each user's profile
  - Desktop, Documents, Downloads, Pictures, Music, Videos
  - the app's own folder (including `Library\`)
  - the destination folder itself (e.g. `C:\__RivanVMs`); only the VM folders inside it

## Adding more built-in archives (admin)
Run `add-builtin.bat "D:\SomeFolder"`. It copies every archive in that folder into `Library\` and marks it built-in. Then copy the updated `C:\LabReset` folder to the lab PCs again.

## Tips
- The first time the app starts, it reads each archive's file list. It saves the lists in `Library\.cache`, so later starts are instant.
- "File is in use" when deleting or extracting: a VM is still running, so close VMware and try again.
- Make sure the disk has room: extracted VMs are often 2–3× the archive size.
