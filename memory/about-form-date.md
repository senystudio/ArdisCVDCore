---
name: about-form-date
description: "At the start of every work session on ArdisCVDCore, set AboutForm.ReleaseDate to today's date"
metadata:
  type: feedback
---

**Standing rule, given 13.10.2026:** every time work on the project starts, change `ReleaseDate` in `AboutForm.cs` to today's date («каждый раз при начале работы с проектом нужно менять дату на сегодняшнюю в AboutForm»).

**How to apply:** do it once per session, as the first edit, in the existing `dd.MM.yyyy` format (`private const string ReleaseDate = "13.10.2026";`). Only the date: `Version` stays as it is unless the user asks. No comment next to it, per [[ardis-cvdcore-working-style]]. First applied 13.10.2026 (22.09.2026 → 13.10.2026).
