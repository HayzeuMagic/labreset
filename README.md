# Lab Reset

A small Windows app that resets the lab VMs at the end of a class, on this PC and on the other lab PCs over the network:

- extract fresh copies of the lab VM archives, and
- find and delete the VMs students extracted.

Every PC keeps its VMs in a **`__RivanVMs`** folder on any drive (`C:\__RivanVMs`, `D:\__RivanVMs`, ...). The app finds that folder on each PC by itself. It **only ever extracts and deletes inside `__RivanVMs`**, and never deletes the archives themselves.

## What's in the folder

| File | What it is |
|---|---|
| `LabReset.exe` | The app (made by `build.bat`). It asks for administrator rights when it starts. |
| `Library\` | Extra archives you import, plus `builtin.txt`. |
| `7zip\` | 7-Zip, used for `.7z`, `.rar` and `.tar.gz` archives (free software, see `7zip\License.txt`). |
| `LabReset.settings.ini` | Saved settings and your PC list (created automatically). |
| `Logs\` | One log file per day: everything extracted, scanned and deleted, and every PC that couldn't be reached. |
| `add-builtin.bat` | For the admin: copies every archive in a folder into `Library\` as built-in. |
| `build.bat` + `src\` | Source code. Double-click `build.bat` to build `LabReset.exe`. |

## Setting it up on the teacher PC
1. Clone the repo (or copy the folder) to `C:\LabReset`:
   ```
   git clone https://github.com/HayzeuMagic/labreset.git C:\LabReset
   ```
2. Double-click **`build.bat`**. It creates `LabReset.exe` with the compiler built into Windows.
3. Run `LabReset.exe`. The archives in this PC's own `__RivanVMs` folder are listed automatically, so nothing big is copied. Archives you import are kept in `Library\`.

GitHub rejects files over 100 MB, so `.gitignore` keeps the archives (`Library\*`), `LabReset.exe`, logs and settings out of Git.

## Using it

**1. Lab archives**
- Tick the archive(s) you want.
- **Import archive...** (or drag files onto the list) adds a new `.zip`, `.7z`, `.rar`, `.tar` or `.tar.gz`. It is copied into `Library\`, so it stays in the app permanently.
- **Remove imported** deletes an archive you imported. Built-in archives and archives in `__RivanVMs` can't be removed.

**2. Lab computers**
- Type a PC's IP address and click **Add**. The PC number is the last 2 digits: `10.3.3.162` is **PC 62**.
- Add several at once with commas (`10.3.3.162, 10.3.3.163`) or a range (`10.3.3.{101-130}`).
- Tick the PCs to work on. *This PC* is always in the list.
- **Test connection** checks each ticked PC and shows where its VMs folder is, e.g. `D:\__RivanVMs`.

**3. Extract (start of class)**
- Click **Extract ticked archives on ticked PCs**.
- Each PC gets the VMs in its own `__RivanVMs` folder, whichever drive that is on. The archive is read from this PC and written over the network, up to 4 PCs at a time.
- *Put each archive in its own folder*: wraps loose files in a folder named after the archive. Archives that already contain one folder (like `Forti.7z` → `Forti\`) are not wrapped twice.
- *Remove the old copy first*: deletes the previous copy before extracting, for a clean reset.
- VMware must be closed on those PCs.

**4. Find and delete (end of class)**
- Click **Scan ticked PCs**. It searches every drive of each ticked PC for its `__RivanVMs` folder and lists the extracted VMs: PC number, path, source archive, match % and date modified.
- Tick what to remove, click **Delete ticked items...** and confirm. Items are deleted permanently.
- PCs that are off or can't be reached are skipped and listed in the log as `UNREACHABLE`.

### Safety rules (built in)
- Only things **inside** a `__RivanVMs` folder can be deleted. The `__RivanVMs` folder itself, and everything outside it, can't be.
- Archive files (`.zip`, `.7z`, `.rar`, `.tar`, `.tar.gz`) are never deleted.
- The app's own folder is never touched. Windows, Program Files and other system folders are never searched.
- These rules are checked again right before every delete.

Scan also lists `.ova` files in `__RivanVMs` that match an archive's contents (for example `yVM.ova` from `yVM-v6.7z`). Untick them if you want to keep them.

## Network setup for the lab PCs
The app reaches other PCs through their hidden admin shares (`\\10.3.3.162\D$`), the same way File Explorer does. For each lab PC:

- **File and Printer Sharing** must be allowed in Windows Firewall.
- The Windows account running Lab Reset must be an **administrator on that PC**. If it isn't, create the same local admin account (same password) on every PC and enter it with **Network login...**, e.g. `.\LabAdmin`. The password is never saved.
- If you use local accounts, run this once on each lab PC (as administrator):
  ```
  reg add HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System /v LocalAccountTokenFilterPolicy /t REG_DWORD /d 1 /f
  ```

**Test connection** tells you what's wrong for each PC:
- **Unreachable**: the PC is off, or the firewall is blocking it.
- **Access is denied**: the account isn't an admin there.
- **No __RivanVMs folder**: the VMs folder is missing on that PC.

## Tips
- The first time the app starts, it reads each archive's file list. It saves the lists in `Library\.cache`, so later starts are instant.
- "File is in use" when deleting or extracting: a VM is still running on that PC, so close VMware and try again.
- Make sure the disk has room: extracted VMs are often 2–3× the archive size.
