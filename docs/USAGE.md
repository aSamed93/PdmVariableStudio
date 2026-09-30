# PDM Variable Studio — User Guide

Bulk-edit the **data card variables** of files in SOLIDWORKS PDM Professional in Excel.
Export → edit in Excel → load back → preview → confirm. Every change is recorded and can be
undone safely later.

It is free. It sends no data anywhere. It is not affiliated with SOLIDWORKS or Dassault
Systèmes.

> This is the English version of [KULLANIM.md](KULLANIM.md). The full installation and
> user guide is available as a PDF:
> [PdmVariableStudio-Installation-and-User-Guide.pdf](PdmVariableStudio-Installation-and-User-Guide.pdf).

---

## Requirements

| | |
|---|---|
| PDM client | SOLIDWORKS PDM **Professional** 2022 (30.0) or later — Standard is not supported |
| Windows | 10 / 11, .NET Framework 4.8.1 (included in Windows 11; delivered by Windows Update on Windows 10) |
| Excel | To edit the file; LibreOffice also works |
| Permissions | Administrator rights once for installation; a PDM administrator account to add the add-in to the vault |

---

## Installation

There are two parts and both are required: the **application** (installed on the computer)
and the **add-in** (added to the vault once; it adds the menu command to PDM Explorer).

### 1. Application — on every computer

**Easiest: the setup file.** Run `PdmVariableStudio-Setup-x.y.z.exe` and follow the wizard.
Choose **English** on the setup language page — the application then starts in English on
this computer. Setup checks that the PDM client is installed, downloads and installs
.NET Framework 4.8.1 if missing, installs the application under
`C:\Program Files\PDM Variable Studio\` and places the add-in's two files in
`C:\Program Files\PDM Variable Studio\AddIn\` (that folder opens at the end). Because the
application is not code-signed, SmartScreen may ask for *More info → Run anyway*.

**With the zip package:** extract the release package (`PdmVariableStudio-x.y.z.zip`). In a
PowerShell window opened as administrator, go to the extracted folder and run:

```powershell
powershell -ExecutionPolicy Bypass -File install-app.ps1
```

The script copies the application to `C:\Program Files\PDM Variable Studio\` and writes its
path to the registry so the add-in can find it. `-ExecutionPolicy Bypass` applies to this
command only and does not change the system setting.

Without administrator rights you can install into your user folder:

```powershell
powershell -ExecutionPolicy Bypass -File install-app.ps1 -CurrentUser -Destination "$env:LOCALAPPDATA\PDM Variable Studio"
```

In a multi-client environment the application can be copied once to a **network share** and
only registered on each client:

```powershell
powershell -ExecutionPolicy Bypass -File install-app.ps1 -RegisterOnly -Destination "\\server\pdm\VariableStudio"
```

### 2. Add-in — once per vault

PDM Administration → vault → **Add-ins** → right-click → **New Add-in…**

Select **both files together** (with Ctrl):

| File | Where |
|---|---|
| `PdmVariableStudio.AddIn.dll` | if installed with the setup file, `C:\Program Files\PDM Variable Studio\AddIn\`; if installed from the zip, the package's `AddIn\` folder |
| `EPDM.Interop.epdm.dll` | if installed with the setup file, ready in the same `AddIn\` folder; if installed from the zip, `C:\Program Files\SOLIDWORKS PDM\` — **not in the package**, take it from your PDM client |

> **Do not skip the second file.** When it is missing, PDM shows a misleading error such as
> *"…is not a multi-threaded COM-server"* while loading the add-in; the message does not name
> the missing file.

There is a single add-in for both languages: each client shows it in its own language.

### 3. Close and reopen Explorer

PDM add-ins are not loaded into a running Explorer. Close **all** PDM Explorer and
Administration windows, then reopen them. The same applies every time you update the add-in.

---

## Usage

### Opening the application

- **From PDM Explorer:** while in a folder of the vault, choose *PDM Variable Studio* from the
  **Tools** menu (or the right-click menu). The files of that folder are loaded.
- **Directly:** `PdmVariableStudio.exe` in the installation folder (default
  `C:\Program Files\PDM Variable Studio\`). If you have more than one vault you are asked
  which one to connect to; the list starts empty.

### Export tab

1. **Collect the files.** Four sources add to the same list; you can mix them:
   - **Add Folder…** — PDM's folder dialog. With *Subfolders* checked, the whole tree.
   - **Add Files…** — PDM's file dialog, multiple selection.
   - **Search and Add…** — a file name pattern (`*.sldprt`, `MIL-*`) and/or a variable value.
   - **Add from Assembly…** — pick an assembly and its configuration; all components of the
     assembly (optionally also the contents of subassemblies) are added, even if they are in
     different folders. Each part gets rows only for **the configuration the assembly uses**
     and `@`. Excel shows *Parent assembly*, *Level* and *Quantity* columns (for information
     only; never written to PDM).
2. **Choose the variables.** Remove the ones you do not need from the list below; the file
   gets smaller and easier to navigate in Excel.
3. **Export…** → save the `.xlsx`. Then use **Open in Excel** to start editing right away.

For SOLIDWORKS files with configurations **each configuration is a separate row.** The `@`
row is SOLIDWORKS's *Custom* tab; it is separate from the named configurations.

### While editing in Excel

- Change only **value cells**. **Clearing** a filled cell means **"delete the value"** and
  shows up as a change in the preview; make sure you did not clear it by accident. If a
  mandatory variable is cleared, that cell is not applied.
- You may delete rows, sort, and hide columns; the application matches rows by hidden IDs,
  not by header or order. Newly added rows are skipped — no files are added to PDM.
- Do **not delete** the `_Rows` and `_Metadata` sheets — the matching information lives there.
  If they are deleted the file is rejected and nothing is written to PDM.
- Type numbers in your own regional format (`12.4` or `12,4`); the application tells Turkish
  and English formats apart correctly.

### Import tab

1. **Select workbook…** → the file you edited.
2. For every cell the application compares **three values**: the value at export, the value in
   Excel and the **current** value in PDM. The result is a preview table:

| Status | Meaning | What happens |
|---|---|---|
| **Safe change** | You changed it; the PDM side has not changed since the export | Applied |
| **Unchanged** | Not edited in Excel | Skipped |
| **Already applied** | PDM already has the same value (someone else wrote it) | Skipped, no unnecessary write |
| **Conflict** | You changed it **and** someone else changed it in PDM too | **Not applied.** Re-evaluate the cell in Excel against the current value and import again |
| **Error** | Type mismatch, file not found, damaged row | Not applied; the reason is in the table |

Use the filters to see only conflicts or errors.

3. **Check-out consent.** A file must be checked out to be written. No file is checked out
   unless you tick the box. Files you already have checked out are left alone; only the
   files the application checked out are checked in at the end (you can turn this off).
4. **Apply.** Progress is shown in the status bar; an error in one file does not stop the
   others.

### Operation History tab

Every apply is listed here. Select an operation and use **Preview undo** to compare with the
current value in PDM:

- The value is still the one you wrote → **undone safely**
- The value is already back to the old one → skipped
- Someone else has written something on top → **conflict, left untouched** (so their work is
  not destroyed)

An undo is also an operation and is recorded in the history.

### Interface language

The application and the add-in work in **Turkish** and **English**; there is a single setup
and package. The language is chosen in this order:

1. The language you pick in the language box at the top right of the application
   (`HKCU\SOFTWARE\PdmVariableStudio\Language`)
2. The language chosen during setup (`HKLM\SOFTWARE\PdmVariableStudio\Language`; the setup
   wizard's language is written here, and IT can deploy it too)
3. The Windows display language — Turkish on Turkish Windows, English otherwise

When you change it in the language box, the application offers to restart (the file list and
preview are reset; the operation history is not affected). The tooltip in the PDM Explorer
menu switches the next time Explorer starts.

The language changes only the texts, **not the data**: numbers and dates are still read with
the Windows regional settings. A workbook exported with the English interface imports without
problems in the Turkish interface (and vice versa); only the headers in Excel are in the
language of the export.

---

## Where the files are

Everything is on this computer, under `%LOCALAPPDATA%\PdmVariableStudio\` (one click away
from the About window):

| | |
|---|---|
| `studio.log` | Application and add-in log. Attach it when reporting a problem. Log lines are in Turkish. |
| `journal\<vault>\` | Operation history; undo works from here. **Do not delete** — a deleted record cannot be undone. |
| `settings.json` | Preferences (subfolders, check-in comment, last folder). If deleted, defaults are used. |

**The operation history is per user and per computer.** An operation can only be undone on
the computer where it was made, with the same Windows account. If a shared team history is
wanted, the history can be redirected to a network share:

- single user: `"journalRoot":"\\\\server\\pdm\\journal"` in `settings.json`
- whole team (administrator): the string value `HKLM\SOFTWARE\PdmVariableStudio\JournalRoot`
  in the registry — this overrides the user setting.

If the share is unreachable the application does **not** fall back to a local folder; the
operation is not started and the reason is shown. Making no change is preferred to making a
change that cannot be undone.

---

## FAQ / troubleshooting

**The command does not appear in the menu.**
Did you close and reopen Explorer? The add-in is only loaded into newly opened windows. If it
is still missing, check that the add-in is installed in Administration and look for the
"Eklenti yüklendi" (add-in loaded) line in `studio.log`.

**"not a multi-threaded COM-server" while loading the add-in.**
`EPDM.Interop.epdm.dll` was not selected **together** with the add-in. Remove the add-in and
add it again with both files.

**I click the command and nothing happens.**
The application is not installed or is in a different path. Compare the path in the
"Uygulama başlatılıyor" (starting application) line of `studio.log` with the actual
installation folder; `install-app.ps1` fixes the registry value.

**"Startup failed" / "Could not connect to vault".**
This computer must have a *vault view* for that vault and you must be logged in to PDM.
Details are in `studio.log`.

**Windows SmartScreen "unrecognized app" warning.**
The application is not code-signed (signing certificates are not free). Compare the SHA-256
value on the release page with the downloaded file, then choose *More info → Run anyway*.

**The workbook was rejected.**
The `_Rows`/`_Metadata` sheet was deleted, or the file is from another vault or an old
version. Export again and move your changes to that file. Nothing from a rejected file is
written to PDM.

**Rows in the preview are blocked with "File not found".**
The file was deleted after the export, moved to another folder, or deleted and re-added with
the same name. A re-added file gets a new ID in PDM; the old workbook does not recognize it.
These rows are skipped on purpose to avoid writing to the wrong file. Export the folder again
and move your changes to the new file.

**A file reports "open in another application".**
The file is open in SOLIDWORKS. Close it and apply again for that file; the other files are
not affected.

---

## Upgrading

- **Application:** run the new setup file (or `install-app.ps1` from the new zip). Explorer
  does not need to be closed; the operation history and settings are kept.
- **Add-in:** only if the release notes say "add-in updated" ("eklenti güncellendi"). Update
  the add-in in Administration (both files) and close and reopen all Explorer windows.

## Reporting a problem

[GitHub Issues](https://github.com/aSamed93/PdmVariableStudio/issues). Attach the output of
**Copy Info** in the About window and the `studio.log` file. Remember that a workbook
contains vault data before attaching it.
