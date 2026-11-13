# Lab Reset

A small Windows app that resets the lab VMs at the end of a class, on this PC and on the other lab PCs over the network:

- extract fresh copies of the lab VM archives, and
- find and delete the VMs students extracted.

Every PC keeps its VMs in a **`__RivanVMs`** folder on any drive (`C:\__RivanVMs`, `D:\__RivanVMs`, ...). The app finds that folder on each PC by itself. It **only ever extracts and deletes inside `__RivanVMs`**, and never deletes the archives themselves.

## What's in the folder

| File | What it is |
|---|---|
| `LabReset.exe` | The app (made by `build.bat`). It asks for administrator rights when it starts. |
| `LabResetAgent.exe` | Small helper (made by `build.bat`) that Lab Reset copies to each lab PC so the PC can extract its own copy of an archive. Keep it next to `LabReset.exe`. |
| `Library\` | Extra archives you import, plus `builtin.txt`. |
| `7zip\` | 7-Zip, used for `.7z`, `.rar` and `.tar.gz` archives (free software, see `7zip\License.txt`). |
| `LabReset.settings.ini` | Saved settings and your PC list (created automatically). |
| `Logs\` | One log file per day: everything extracted and deleted, and every PC that couldn't be reached. |
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

**3. Reset the lab**
- **Extract ticked archives on ticked PCs**: each ticked PC extracts its OWN copy of each ticked archive from its own `__RivanVMs` folder (C:, D:, ...), right into that folder. The old copy is removed first. Nothing big is sent over the network, and all PCs work at the same time. A PC that doesn't have the archive shows *Archive missing*.
- **Delete extracted VMs on ticked PCs...**: removes what the ticked archives extracted (e.g. `D:\__RivanVMs\Forti`) from each ticked PC. The archives are kept. If no archive is ticked, it offers to delete the VMs of all archives.
- **Stop** cancels the job on every PC.
- VMware must be closed on those PCs.

**4. Lab PCs**
Every PC is shown as a tile with its number and its own progress bar, so you can see which PCs are done:
- **Blue, moving stripes**: working (with % done)
- **Green ✓**: done
- **Red ✗**: problem (see the log)
- **Gray –**: off, unreachable, or no `__RivanVMs` folder
- **Faded**: not ticked. Click a tile to tick or untick that PC.

### Safety rules (built in)
- Only things **inside** a `__RivanVMs` folder can be deleted. The `__RivanVMs` folder itself, and everything outside it, can't be.
- Archive files (`.zip`, `.7z`, `.rar`, `.tar`, `.tar.gz`) are never deleted.
- The app's own folder is never touched. Windows, Program Files and other system folders are never searched.
- These rules are checked again right before every delete.

## Network setup for the lab PCs
The app reaches other PCs through their hidden admin shares (`\\10.3.3.162\D$`), the same way File Explorer does. For each lab PC:

- **File and Printer Sharing** must be allowed in Windows Firewall.
- The Windows account running Lab Reset must be an **administrator on that PC**. If it isn't, create the same local admin account (same password) on every PC and enter it with **Network login...**, e.g. `.\LabAdmin`. The password is never saved.
- If you use local accounts, run this once on each lab PC (as administrator):
  ```
  reg add HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System /v LocalAccountTokenFilterPolicy /t REG_DWORD /d 1 /f
  ```

### How "extracts its own copy" works
Lab Reset copies a small helper, `LabResetAgent.exe` (plus 7-Zip), to `C:\ProgramData\LabReset` on the lab PC. It then starts the helper as the **Lab Reset Agent** Windows service, the same way PsExec does. The helper finds the archive in that PC's own `__RivanVMs` folder, extracts it there, and writes its progress to `C:\ProgramData\LabReset\status.txt`, which Lab Reset reads to show progress and the log. **Stop** cancels it on every PC.

This uses the same admin access and File and Printer Sharing as everything else, so no extra setup is needed. The helper's own log is in `C:\ProgramData\LabReset\Logs` on each lab PC. If antivirus blocks the helper, add `C:\ProgramData\LabReset` as an exclusion on the lab PCs.

**Test connection** tells you what's wrong for each PC:
- **Unreachable**: the PC is off, or the firewall is blocking it.
- **Access is denied**: the account isn't an admin there.
- **No __RivanVMs folder**: the VMs folder is missing on that PC.

## Tips
- The first time the app starts, it reads each archive's file list. It saves the lists in `Library\.cache`, so later starts are instant.
- "File is in use" when deleting or extracting: a VM is still running on that PC, so close VMware and try again.
- Make sure the disk has room: extracted VMs are often 2–3× the archive size.
