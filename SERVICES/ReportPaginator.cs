using System;
using System.Collections.Generic;
using System.Linq;

namespace ARISHrms.Services
{
    /// <summary>
    /// Splits a report into pages. It only needs the height of each piece, so the page-breaking rules are plain
    /// code and can be tested without a printer:
    ///  - every block and every table row is placed exactly once, in order (nothing is skipped);
    ///  - a table that does not fit continues on the next page, with its header row repeated;
    ///  - a heading is never left alone at the bottom of a page (it moves down with the first lines that follow);
    ///  - a single item taller than a whole page still gets a page of its own instead of looping forever.
    /// </summary>
    public static class ReportPaginator
    {
        /// <summary>One thing to draw on a page.</summary>
        public class Placed
        {
            public int BlockIndex;
            public int RowIndex = -1;          // -1 = the block itself (or a table's header row when IsTableHeader)
            public bool IsTableHeader;
            public bool IsContinuation;        // header repeated on a new page
            public double Height;
        }

        public class Page
        {
            public List<Placed> Items = new List<Placed>();
            public double Used;
        }

        /// <param name="blockHeights">height of each non-table block (tables: the table header row height)</param>
        /// <param name="tableRowHeights">for a table block: height of each row; null for other blocks</param>
        /// <param name="isHeading">true for section headings (kept with what follows)</param>
        public static List<Page> Paginate(IList<double> blockHeights, IList<IList<double>> tableRowHeights,
                                          IList<bool> isHeading, double pageHeight)
        {
            if (pageHeight <= 0) throw new ArgumentOutOfRangeException(nameof(pageHeight));

            var pages = new List<Page> { new Page() };
            Page cur() => pages[pages.Count - 1];

            void NewPage() => pages.Add(new Page());

            void Put(Placed p)
            {
                if (cur().Items.Count > 0 && cur().Used + p.Height > pageHeight) NewPage();
                cur().Items.Add(p);
                cur().Used += p.Height;
            }

            // height of the first line that follows a heading (a table counts its header + first row)
            double FirstLine(int i)
            {
                if (i >= blockHeights.Count) return 0;
                var nr = tableRowHeights[i];
                return blockHeights[i] + (nr != null && nr.Count > 0 ? nr[0] : 0);
            }

            for (int b = 0; b < blockHeights.Count; b++)
            {
                var rows = tableRowHeights[b];
                double h = blockHeights[b];

                if (isHeading[b])
                {
                    // Never leave a heading alone at the bottom. It needs room for itself, any headings directly
                    // after it, and the first line of real content that follows - capped at a page, so a huge
                    // first line can't push the heading forward forever. Only the FIRST heading of a run decides.
                    bool startsRun = b == 0 || !isHeading[b - 1];
                    if (startsRun)
                    {
                        double need = 0;
                        int i = b;
                        while (i < blockHeights.Count && isHeading[i]) { need += blockHeights[i]; i++; }
                        need = Math.Min(need + FirstLine(i), pageHeight);
                        if (cur().Items.Count > 0 && cur().Used + need > pageHeight) NewPage();
                    }
                    cur().Items.Add(new Placed { BlockIndex = b, Height = h });
                    cur().Used += h;
                    continue;
                }

                if (rows == null)
                {
                    Put(new Placed { BlockIndex = b, Height = h });
                    continue;
                }

                // table: start it on a new page if not even its header + first row fit, unless it follows its heading
                bool followsHeading = cur().Items.Count > 0 && isHeading[cur().Items[cur().Items.Count - 1].BlockIndex]
                                      && cur().Items[cur().Items.Count - 1].RowIndex == -1;
                double firstRow = rows.Count > 0 ? rows[0] : 0;
                if (!followsHeading && cur().Items.Count > 0 && cur().Used + h + firstRow > pageHeight) NewPage();

                cur().Items.Add(new Placed { BlockIndex = b, IsTableHeader = true, Height = h });
                cur().Used += h;

                for (int r = 0; r < rows.Count; r++)
                {
                    // continue on a new page (header repeated) - but never leave a page with only a header on it
                    bool pageHasRowsOrOtherContent = cur().Items.Any(i => !(i.IsTableHeader && i.BlockIndex == b) && !(isHeading[i.BlockIndex] && i.RowIndex == -1))
                                                     || cur().Items.Any(i => i.BlockIndex == b && i.RowIndex >= 0);
                    if (cur().Used + rows[r] > pageHeight && pageHasRowsOrOtherContent)
                    {
                        NewPage();
                        cur().Items.Add(new Placed { BlockIndex = b, IsTableHeader = true, IsContinuation = true, Height = h });
                        cur().Used += h;
                    }
                    cur().Items.Add(new Placed { BlockIndex = b, RowIndex = r, Height = rows[r] });
                    cur().Used += rows[r];
                }
            }

            // a trailing empty page can only appear if the last item forced a break - remove it
            if (pages.Count > 1 && pages[pages.Count - 1].Items.Count == 0) pages.RemoveAt(pages.Count - 1);
            return pages;
        }
    }
}
