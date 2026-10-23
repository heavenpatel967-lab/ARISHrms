# 🔧 ARISHrms Build Errors & Fixes

This document covers common build errors you might encounter and their solutions.

---

## ❌ Error 1: "Microsoft.Data.Sqlite Not Found"

### Symptoms
```
error: Could not find 'Microsoft.Data.Sqlite' version '10.0.13'
```

### Fix
**In Visual Studio:**
1. Right-click solution → **Manage NuGet Packages**
2. Search for `Microsoft.Data.Sqlite`
3. Version **10.0.13** should be available
4. Click **Install** if not already installed

**From Terminal:**
```bash
dotnet add package Microsoft.Data.Sqlite --version 10.0.13
```

---

## ❌ Error 2: "Dapper Not Found"

### Symptoms
```
error CS0246: The type or namespace name 'Dapper' could not be found
```

### Fix
**In Visual Studio:**
1. Right-click solution → **Manage NuGet Packages**
2. Search for `Dapper`
3. Version **2.1.89** should be available
4. Click **Install**

**From Terminal:**
```bash
dotnet add package Dapper --version 2.1.89
```

---

## ❌ Error 3: "ClosedXML Not Found"

### Symptoms
```
error CS0246: The type or namespace name 'ClosedXML' could not be found
```

### Fix
**In Visual Studio:**
1. Right-click solution → **Manage NuGet Packages**
2. Search for `ClosedXML`
3. Version **0.105.1** should be available
4. Click **Install**

**From Terminal:**
```bash
dotnet add package ClosedXML --version 0.105.1
```

---

## ❌ Error 4: "System.Data.OleDb Not Found"

### Symptoms
```
error CS0246: The type or namespace name 'System.Data.OleDb' could not be found
```

### Fix
**In Visual Studio:**
1. Right-click solution → **Manage NuGet Packages**
2. Search for `System.Data.OleDb`
3. Version **10.0.13** should be available
4. Click **Install**

**From Terminal:**
```bash
dotnet add package System.Data.OleDb --version 10.0.13
```

---

## ❌ Error 5: "View XYZ not found"

### Symptoms
```
error XAML1001: Cannot find type 'Views:DashboardView' in XAML namespace
```

### Root Cause
The view class doesn't exist or namespace is wrong.

### Fix

1. **Verify the file exists:**
   - Check that `/VIEWS/DashboardView.xaml.cs` exists
   - Check that `/VIEWS/DashboardView.xaml` exists

2. **Verify namespace in code-behind:**
   ```csharp
   namespace ARISHrms.Views
   {
       public partial class DashboardView : UserControl
       {
           // ...
       }
   }
   ```

3. **If not found, create the file:**
   - Right-click `VIEWS` folder → **Add** → **New Item**
   - Select **User Control (WPF)**
   - Name: `DashboardView.xaml`

4. **Rebuild Solution:**
   ```
   Build → Clean Solution
   Build → Rebuild Solution
   ```

---

## ❌ Error 6: "Type or namespace 'DashboardView' not found"

### Symptoms
```
error CS0246: The type or namespace name 'DashboardView' could not be found
```

### Root Cause
The view namespace is not imported in MainWindow.xaml.cs

### Fix

**In MainWindow.xaml.cs**, add the using statement:
```csharp
using ARISHrms.Views;  // ← Add this line
```

Full correct header should be:
```csharp
using System.Windows;
using System.Windows.Controls;
using ARISHrms.Data;
using ARISHrms.Views;    // ← View namespace

namespace ARISHrms
{
    public partial class MainWindow : Window
    {
        // ...
    }
}
```

---

## ❌ Error 7: "Models.Employee not found"

### Symptoms
```
error CS0246: The type or namespace name 'Employee' could not be found
```

### Root Cause
The Models namespace is not imported in the view file.

### Fix

**In VIEWS/LoansView.xaml.cs**, add:
```csharp
using ARISHrms.Models;  // ← Add this line
```

Full correct header:
```csharp
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ARISHrms.Data;
using ARISHrms.Models;       // ← Models namespace
using ARISHrms.Services;
using Microsoft.Data.Sqlite;
using Dapper;
using ClosedXML.Excel;
using System.Linq;
```

**Apply to all view files:**
- LoansView.xaml.cs
- AdvancesView.xaml.cs
- EmployeesView.xaml.cs
- LeavesView.xaml.cs
- DocumentsView.xaml.cs
- PayrollView.xaml.cs
- AttendanceView.xaml.cs

---

## ❌ Error 8: "LoanService not found"

### Symptoms
```
error CS0246: The type or namespace name 'LoanService' could not be found
```

### Root Cause
The Services namespace is not imported.

### Fix

**In any view file**, add:
```csharp
using ARISHrms.Services;  // ← Add this line
```

---

## ❌ Error 9: "DatabaseInitializer not found"

### Symptoms
```
error CS0246: The type or namespace name 'DatabaseInitializer' could not be found
```

### Root Cause
The Data namespace is not imported.

### Fix

**In MainWindow.xaml.cs**, add:
```csharp
using ARISHrms.Data;  // ← Add this line
```

---

## ❌ Error 10: "Cannot connect to database"

### Symptoms
```
FileNotFoundException: Cannot open database file
```

### Root Cause
The database file path is incorrect or permission denied.

### Fix

**Option 1: Verify database path**
In `DATA/DatabaseInitializer.cs`:
```csharp
public static string DbPath => 
    Path.Combine(Directory.GetCurrentDirectory(), "ArisHRMS.db");
```

This should create `ArisHRMS.db` in:
```
ARISHrms/bin/Debug/net10.0-windows/ArisHRMS.db
```

**Option 2: Check folder permissions**
```bash
# Ensure the project folder has write permissions
chmod -R 755 /path/to/ARISHrms
```

**Option 3: Delete old database and recreate**
1. Stop the application
2. Delete `ArisHRMS.db`
3. Restart application (will recreate)

---

## ❌ Error 11: XAML Binding Error

### Symptoms
```
error XAML1001: Cannot add a binding to property 'X' of object 'DashboardView' 
because it is not a known bindable property.
```

### Root Cause
The TextBlock binding doesn't exist or property name is wrong.

### Fix

**Check the XAML file matches the code-behind:**

**DashboardView.xaml:**
```xml
<TextBlock x:Name="TxtTotalEmployees" Text="0" FontSize="24"/>
```

**DashboardView.xaml.cs:**
```csharp
TxtTotalEmployees.Text = totalEmployees.ToString();
```

Names must match exactly!

---

## ❌ Error 12: "CS1061: 'Object' does not contain a definition"

### Symptoms
```
error CS1061: 'LoansView' does not contain a definition for 'LoanGrid'
```

### Root Cause
The XAML element name doesn't match the code-behind reference.

### Fix

**Check all DataGrid/ListBox/TextBlock names:**
```xml
<!-- XAML -->
<DataGrid x:Name="LoanGrid" />
<TextBlock x:Name="TxtInfo" />
<TextBlock x:Name="TxtTotalOutstanding" />
<ComboBox x:Name="CmbSelectEmployee" />
<Button x:Name="BtnExportExcel" />
```

**Must be referenced in code-behind:**
```csharp
// Code-behind
LoanGrid.ItemsSource = loans;
TxtInfo.Text = "...";
TxtTotalOutstanding.Text = "...";
CmbSelectEmployee.ItemsSource = employees;
```

Names must be EXACT match!

---

## ❌ Error 13: "No parameterless constructor found"

### Symptoms
```
error CS0121: The call is ambiguous between the following methods or properties: 'Method'
```

### Root Cause
Model class needs a default constructor.

### Fix

**In MODELS/DataModels.cs:**
```csharp
public class Employee
{
    // Properties
    public string EmpCode { get; set; }
    public string FullName { get; set; }
    
    // Add parameterless constructor
    public Employee() { }
}
```

---

## ❌ Error 14: ".NET 10 not installed"

### Symptoms
```
error NETSDK1001: The TFM 'net10.0-windows' is not recognized
```

### Root Cause
.NET 10.0 SDK is not installed on your machine.

### Fix

**Option 1: Update .NET SDK**
Download from: https://dotnet.microsoft.com/download/dotnet

**Option 2: Change target framework (Fallback)**

Edit `ARISHrms.csproj`:
```xml
<TargetFramework>net8.0-windows</TargetFramework>  <!-- Change from net10.0-windows -->
```

---

## ❌ Error 15: WPF XAML Designer not opening

### Symptoms
```
Designer cannot be shown for this file
```

### Root Cause
XAML file structure is invalid or missing namespace.

### Fix

**Verify XAML header:**
```xml
<UserControl x:Class="ARISHrms.Views.DashboardView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="#F8FAFC">
```

**If still broken:**
1. Close the XAML file
2. Right-click → **View Code**
3. Fix any red underlines
4. Save and reopen

---

## ✅ General Troubleshooting Steps

If you encounter any unknown build error:

### Step 1: Clean & Rebuild
```bash
# Terminal
dotnet clean
dotnet build

# OR in Visual Studio
Build → Clean Solution
Build → Rebuild Solution
```

### Step 2: Restore NuGet Packages
```bash
dotnet restore
```

### Step 3: Check all using statements

Every C# file needs these namespaces:
```csharp
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ARISHrms.Data;
using ARISHrms.Models;
using ARISHrms.Services;
using Microsoft.Data.Sqlite;
using Dapper;
```

### Step 4: Verify file names & casing

Windows is NOT case-sensitive for filenames, but C# IS case-sensitive for namespaces:
- ✅ `DashboardView.xaml` (file name)
- ✅ `ARISHrms.Views.DashboardView` (namespace reference - exact case!)

### Step 5: Check the Error List

In Visual Studio:
1. View → **Error List** (Ctrl + \, Ctrl + E)
2. Click first error
3. Read the error message carefully
4. It usually tells you exactly what's wrong

---

## 🆘 If All Else Fails

1. **Verify all files exist:**
   ```bash
   ls -la ARISHrms/SERVICES/
   ls -la ARISHrms/VIEWS/
   ls -la ARISHrms/MODELS/
   ls -la ARISHrms/DATA/
   ```

2. **Check git status:**
   ```bash
   git status
   git log --oneline -5
   ```

3. **Reset to latest:**
   ```bash
   git pull origin master
   ```

4. **Nuke and rebuild:**
   ```bash
   rm -rf bin obj
   dotnet clean
   dotnet restore
   dotnet build
   ```

---

## 📊 Build Success Indicators

✅ No red squigglies in code
✅ Error List shows 0 errors
✅ Output window shows "Build succeeded"
✅ Application window opens with F5
✅ Database file created: `ArisHRMS.db`

---

If you hit an error not listed here, please share the exact error message and I'll help debug! 🎯

