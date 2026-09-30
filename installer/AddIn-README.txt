PDM Variable Studio — add-in files
==================================

The TWO files in this folder are added to the PDM vault TOGETHER. This step is for the PDM
administrator and is done once per vault:

  1. Open PDM Administration and log in to the vault.
  2. Add-ins → right-click → New Add-in…
  3. In the file dialog go to this folder and select BOTH with Ctrl:
         PdmVariableStudio.AddIn.dll
         EPDM.Interop.epdm.dll
  4. OK → the add-in appears in the list.
  5. Close and reopen ALL PDM Explorer and Administration windows.

Why two files?
  The add-in needs PDM's own library (EPDM.Interop.epdm.dll), and because PDM runs a vault
  add-in from a separate folder, it cannot find that library there. If only one file is
  added, PDM shows a misleading error:
  "... is not a multi-threaded COM-server" — and does NOT name the missing file.

Languages:
  There is one add-in for both Turkish and English. Each client shows it in the language
  chosen on that computer (application language box, setup language or Windows language).

When upgrading:
  Repeat this step only if the release notes say "add-in updated" ("eklenti güncellendi")
  (select the add-in and Update with the same two files). Not needed for application updates.

Details: USAGE.md in the parent folder
