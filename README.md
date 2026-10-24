# ARIS HRMS

A Windows (WPF, .NET) desktop app that combines two earlier tools:

- **ARIS Attendance** - punch import (`.ddd` / `.ren`), missing-punch fixes, export.
- **ARIS Payroll Companion** - reports on top of EasyPay's DBF files.

ARIS HRMS does **not** calculate payroll. EasyPay does that. This app reads EasyPay's output files and
reports on them.

## Where the data comes from

Pick your DBFS folder once: **Employee Master -> Sync DBFS Folder**. The app then reads:

| File | Used for |
|---|---|
| `EMPMAS.DBF`, `mstsal.DBF`, `monsum.DBF` | Employee list (merged in that order; first file to supply a value wins) |
| `monsum.DBF` | Loans (`PAID_LOAN`, `LOAN_REC`, `LOAN_DED`) and advances (`ADVANCE`) |
| `MONDATA.DBF` | Daily Late / Absent report and Employee History |

FoxPro system files (`FOXUSER.DBF`, `FOXCODE.DBF`) are ignored.

## Rules (copied from the old Payroll Companion)

- Pending loan = `PAID_LOAN - LOAN_REC` for the latest month. A month is "paid" when `LOAN_DED > 0`.
- Advance months above the employee's monthly limit (default 5000) are flagged red. Limits can be typed per
  employee or imported from a CSV with `Card No` and `ADV` columns.
- Late = first punch after 08:32. Absent on a Wednesday that `OFF1` marks as `WED` is shown as Week Off.
- Employee is "left" when `LEAVING_DT` is filled in.

## What is local to this app (SQLite, `ArisHRMS.db`)

Employees (synced from DBF), punches and attendance fixes, village visits and leave balances, documents,
advance limits. Details entered in the app (phone, Aadhar, village...) are kept when you re-sync.

## Status

The data-reading logic (`SERVICES/MonsumService.cs`, `AttendanceReportService.cs`, `DbfReader.cs`,
`DbfFolderScanner.cs`) has been tested against the old app's logic on generated data. The WPF screens
have not been run by the author's tooling - build and try them in Visual Studio, and report anything odd.

## Not ported from the old Payroll Companion yet

Active/Inactive loan list reports, Compliance dashboard, Employee Summary, document and leave screens as
the old app had them (this app has its own versions).
