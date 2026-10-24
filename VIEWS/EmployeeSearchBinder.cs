using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using ARISHrms.Models;
using ARISHrms.Services;

namespace ARISHrms.Views
{
    /// <summary>
    /// Connects a search box to an employee drop-down. Typing a card number, employee code or name narrows the
    /// list and selects the best match, so the screen immediately shows that person.
    ///   Enter  = select the best match      Down arrow = open the list      Esc = clear the search
    /// </summary>
    public static class EmployeeSearchBinder
    {
        public static void Attach(ComboBox combo, TextBox searchBox, TextBlock hint,
                                  IList<Employee> allEmployees, Action onNothingSelected)
        {
            void Apply()
            {
                var current = combo.SelectedItem as Employee;
                string term = searchBox.Text ?? "";
                var matches = EmployeeFilter.Filter(allEmployees, term);

                // the drop-down only offers the matches (all employees again when the box is empty)
                combo.ItemsSource = matches;

                if (matches.Count == 0)
                {
                    hint.Text = "No employee found";
                    onNothingSelected?.Invoke();
                    return;
                }

                hint.Text = string.IsNullOrWhiteSpace(term) ? "" : $"{matches.Count} found";

                // keep the person already on screen if they still match, otherwise jump to the best match
                var keep = current == null ? null : matches.FirstOrDefault(m => m.EmpCode == current.EmpCode);
                combo.SelectedItem = keep ?? matches[0];
            }

            searchBox.TextChanged += (s, e) => Apply();

            // the cursor is already in the box when the screen opens: just type or scan a card number
            searchBox.Loaded += (s, e) => searchBox.Focus();

            searchBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Down)
                {
                    combo.Focus();
                    combo.IsDropDownOpen = true;
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    searchBox.Clear();
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter)
                {
                    if (combo.SelectedItem == null && combo.Items.Count > 0) combo.SelectedIndex = 0;
                    e.Handled = true;
                }
            };
        }
    }
}
