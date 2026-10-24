using System;
using System.Collections.Generic;
using System.Linq;
using ARISHrms.Models;

namespace ARISHrms.Services
{
    /// <summary>
    /// Employee search used by the Loan, Advance, Leave and Document screens.
    /// Finds people by card number, employee code or name. Numbers are compared as numbers, so typing
    /// "7", "07" or "00000007" all find card 00000007 / code 0007.
    /// </summary>
    public static class EmployeeFilter
    {
        /// <summary>Best matches first: exact card/code, then "starts with", then "contains".</summary>
        public static List<Employee> Filter(IEnumerable<Employee> all, string term)
        {
            var list = all.ToList();
            term = (term ?? "").Trim();
            if (term.Length == 0) return list;

            var ranked = new List<(Employee emp, int rank)>();
            foreach (var e in list)
            {
                int r = Rank(e, term);
                if (r >= 0) ranked.Add((e, r));
            }

            return ranked
                .OrderBy(x => x.rank)
                .ThenBy(x => CodeSortKey(x.emp.EmpCode))
                .ThenBy(x => x.emp.EmpCode, StringComparer.Ordinal)
                .Select(x => x.emp)
                .ToList();
        }

        private static long CodeSortKey(string code) =>
            long.TryParse((code ?? "").Trim(), out var n) ? n : long.MaxValue;

        /// <summary>-1 = no match, 0 = exact card or code, 1 = starts with, 2 = contains.</summary>
        private static int Rank(Employee e, string term)
        {
            string card = (e.CardNo ?? "").Trim();
            string code = (e.EmpCode ?? "").Trim();
            string name = (e.FullName ?? "").Trim();

            bool digitsOnly = term.All(char.IsDigit);

            // A complete card number (8 digits, the length cards are stored with) means "this exact card":
            // do not also list people whose number merely contains those digits.
            if (digitsOnly && term.Length >= 8)
            {
                string tt = term.TrimStart('0');
                string cardFull = card.TrimStart('0');
                return tt.Length > 0 && cardFull == tt ? 0 : -1;
            }

            if (digitsOnly)
            {
                string t = term.TrimStart('0');          // "0007" and "7" are the same number
                if (t.Length > 0)
                {
                    string cardN = card.TrimStart('0');
                    string codeN = code.TrimStart('0');

                    if ((cardN.Length > 0 && cardN == t) || (codeN.Length > 0 && codeN == t)) return 0;
                    if (cardN.StartsWith(t, StringComparison.Ordinal) || codeN.StartsWith(t, StringComparison.Ordinal)) return 1;
                    if (cardN.Contains(t, StringComparison.Ordinal) || codeN.Contains(t, StringComparison.Ordinal)) return 2;
                }
            }

            // text as typed: names, or a card / code typed with its leading zeros
            if (card.StartsWith(term, StringComparison.OrdinalIgnoreCase) ||
                code.StartsWith(term, StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(term, StringComparison.OrdinalIgnoreCase)) return 1;

            if (card.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                code.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(term, StringComparison.OrdinalIgnoreCase)) return 2;

            return -1;
        }
    }
}
