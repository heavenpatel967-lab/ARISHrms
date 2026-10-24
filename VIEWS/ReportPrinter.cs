using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using ARISHrms.Services;

namespace ARISHrms.Views
{
    /// <summary>
    /// Prints a <see cref="Report"/> on as many pages as it needs. Choose "Microsoft Print to PDF" in the print
    /// dialog to save it as a PDF. Every page has the report title, the employee and "Page X of Y"; long tables
    /// continue on the next page with their column headers repeated, and the last page shows how many records
    /// were printed so you can see nothing was left out.
    /// </summary>
    public static class ReportPrinter
    {
        private const double Margin = 48;           // ~1.3 cm
        private const double HeaderHeight = 64;
        private const double FooterHeight = 30;

        private static Brush B(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex);

        public static void Print(Report report, Window owner)
        {
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true) return;

            try
            {
                var size = new Size(
                    dialog.PrintableAreaWidth > 0 ? dialog.PrintableAreaWidth : 793.7,
                    dialog.PrintableAreaHeight > 0 ? dialog.PrintableAreaHeight : 1122.5);

                var doc = BuildDocument(report, size, out int pageCount);
                dialog.PrintDocument(doc.DocumentPaginator, $"{report.Title} - {report.Subtitle}");

                MessageBox.Show(owner, $"Printed {pageCount} page(s) - {report.RowCount} record(s).",
                    "Print", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, $"Could not print: {ex.Message}", "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ------------------------------------------------------------------ building the pages

        public static FixedDocument BuildDocument(Report report, Size pageSize, out int pageCount)
        {
            double contentWidth = pageSize.Width - 2 * Margin;
            double contentHeight = pageSize.Height - 2 * Margin - HeaderHeight - FooterHeight;

            // the "N records printed" line at the very end belongs to the report too
            var blocks = report.Blocks.ToList();
            blocks.Add(new ReportNote
            {
                Text = $"End of report - {report.RowCount} record(s) printed.   Printed {report.Printed:dd-MMM-yyyy HH:mm}",
                Color = "#64748B"
            });

            // build and measure every piece once
            var visuals = new List<FrameworkElement>();
            var rowVisuals = new List<List<FrameworkElement>>();
            var heights = new List<double>();
            var rowHeights = new List<IList<double>>();
            var isHeading = new List<bool>();

            foreach (var b in blocks)
            {
                if (b is ReportTable t)
                {
                    var header = HeaderRow(t, contentWidth);
                    var rows = new List<FrameworkElement>();
                    for (int i = 0; i < t.Rows.Count; i++)
                    {
                        string bg = i < t.RowBackgrounds.Count && t.RowBackgrounds[i] != null ? t.RowBackgrounds[i] : (i % 2 == 1 ? "#F8FAFC" : "#FFFFFF");
                        rows.Add(TableRow(t, t.Rows[i], contentWidth, bg));
                    }
                    if (t.Rows.Count == 0)
                        rows.Add(Text(t.EmptyText, contentWidth, 11, "#94A3B8", italic: true, margin: new Thickness(6, 6, 0, 6)));

                    visuals.Add(header);
                    rowVisuals.Add(rows);
                    heights.Add(Measure(header, contentWidth));
                    rowHeights.Add(rows.Select(r => Measure(r, contentWidth)).ToList());
                    isHeading.Add(false);
                }
                else
                {
                    var v = Block(b, contentWidth);
                    visuals.Add(v);
                    rowVisuals.Add(null);
                    heights.Add(Measure(v, contentWidth));
                    rowHeights.Add(null);
                    isHeading.Add(b is ReportHeading);
                }
            }

            var pages = ReportPaginator.Paginate(heights, rowHeights, isHeading, contentHeight);
            pageCount = pages.Count;

            var doc = new FixedDocument();
            doc.DocumentPaginator.PageSize = pageSize;

            for (int p = 0; p < pages.Count; p++)
            {
                var page = new FixedPage { Width = pageSize.Width, Height = pageSize.Height, Background = Brushes.White };

                var stack = new StackPanel { Width = contentWidth };
                foreach (var item in pages[p].Items)
                {
                    FrameworkElement el;
                    if (item.IsTableHeader)
                        el = item.IsContinuation ? HeaderRow((ReportTable)blocks[item.BlockIndex], contentWidth) : visuals[item.BlockIndex];
                    else if (item.RowIndex >= 0)
                        el = rowVisuals[item.BlockIndex][item.RowIndex];
                    else
                        el = visuals[item.BlockIndex];

                    stack.Children.Add(el);
                }

                FixedPage.SetLeft(stack, Margin);
                FixedPage.SetTop(stack, Margin + HeaderHeight);
                page.Children.Add(stack);

                var header = PageHeader(report, contentWidth, p > 0);
                FixedPage.SetLeft(header, Margin);
                FixedPage.SetTop(header, Margin);
                page.Children.Add(header);

                var footer = PageFooter(report, contentWidth, p + 1, pages.Count);
                FixedPage.SetLeft(footer, Margin);
                FixedPage.SetTop(footer, pageSize.Height - Margin - FooterHeight + 8);
                page.Children.Add(footer);

                page.Measure(pageSize);
                page.Arrange(new Rect(pageSize));
                page.UpdateLayout();

                var content = new PageContent();
                ((System.Windows.Markup.IAddChild)content).AddChild(page);
                doc.Pages.Add(content);
            }

            return doc;
        }

        private static double Measure(FrameworkElement el, double width)
        {
            el.Measure(new Size(width, double.PositiveInfinity));
            return Math.Ceiling(el.DesiredSize.Height);
        }

        /// <summary>A fresh copy of a table's column-header row (an element can only sit on one page).</summary>
        private static FrameworkElement HeaderRow(ReportTable t, double width) =>
            TableRow(t, t.Headers.Select(h => new ReportCell(h, "#FFFFFF", true)).ToList(), width, "#334155");

        // ------------------------------------------------------------------ pieces

        private static TextBlock Text(string text, double width, double size, string color, bool bold = false,
                                      bool italic = false, Thickness? margin = null) =>
            new TextBlock
            {
                Text = text ?? "",
                Width = width,
                FontSize = size,
                Foreground = B(color ?? "#0F172A"),
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
                TextWrapping = TextWrapping.Wrap,
                Margin = margin ?? new Thickness(0)
            };

        private static FrameworkElement PageHeader(Report r, double width, bool continued)
        {
            var sp = new StackPanel { Width = width };
            sp.Children.Add(new TextBlock { Text = r.Company, FontSize = 15, FontWeight = FontWeights.Black, HorizontalAlignment = HorizontalAlignment.Center });
            sp.Children.Add(new TextBlock
            {
                Text = continued ? $"{r.Title}  (continued)" : r.Title,
                FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = B("#555555"), HorizontalAlignment = HorizontalAlignment.Center
            });
            sp.Children.Add(new TextBlock { Text = r.Subtitle, FontSize = 11, Foreground = B("#0078D7"), HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            sp.Children.Add(new Rectangle { Height = 1.5, Fill = Brushes.Black, Margin = new Thickness(0, 6, 0, 0) });
            return sp;
        }

        private static FrameworkElement PageFooter(Report r, double width, int page, int pages)
        {
            var g = new Grid { Width = width };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new TextBlock { Text = $"Printed {r.Printed:dd-MMM-yyyy HH:mm}  -  ARIS HRMS", FontSize = 9, Foreground = B("#64748B") };
            var right = new TextBlock { Text = $"Page {page} of {pages}", FontSize = 10, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(right, 1);
            g.Children.Add(left);
            g.Children.Add(right);
            return g;
        }

        private static FrameworkElement Block(ReportBlock b, double width)
        {
            switch (b)
            {
                case ReportHeading h:
                    return Text(h.Text, width, 13, "#0078D7", bold: true, margin: new Thickness(0, 14, 0, 6));

                case ReportNote n:
                    return Text(n.Text, width, 10, n.Color, italic: true, margin: new Thickness(0, 4, 0, 6));

                case ReportFields f:
                {
                    var g = new Grid { Width = width, Margin = new Thickness(0, 0, 0, 4) };
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    for (int i = 0; i < f.Items.Count; i++)
                    {
                        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        var label = new TextBlock { Text = f.Items[i].Label + ":", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 8, 2) };
                        var c = f.Items[i].Value;
                        var value = new TextBlock
                        {
                            Text = string.IsNullOrWhiteSpace(c.Text) ? "-" : c.Text, FontSize = 11, TextWrapping = TextWrapping.Wrap,
                            FontWeight = c.Bold ? FontWeights.Bold : FontWeights.Normal, Foreground = B(c.Color ?? "#0F172A"), Margin = new Thickness(0, 2, 0, 2)
                        };
                        Grid.SetRow(label, i); Grid.SetRow(value, i); Grid.SetColumn(value, 1);
                        g.Children.Add(label); g.Children.Add(value);
                    }
                    return g;
                }

                case ReportDots d:
                {
                    var sp = new StackPanel { Orientation = Orientation.Horizontal, Width = width, Margin = new Thickness(0, 2, 0, 4) };
                    sp.Children.Add(new TextBlock { Text = d.Label + ":", FontSize = 11, FontWeight = FontWeights.SemiBold, Width = 170 });
                    if (!string.IsNullOrWhiteSpace(d.Text))
                        sp.Children.Add(new TextBlock { Text = d.Text, FontSize = 11, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 10, 0) });
                    foreach (var dot in d.Dots)
                        sp.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = B(dot.Color), Margin = new Thickness(0, 2, 3, 0) });
                    return sp;
                }
            }
            return new TextBlock();
        }

        private static FrameworkElement TableRow(ReportTable t, List<ReportCell> cells, double width, string background)
        {
            var g = new Grid { Width = width, Background = B(background) };
            double total = t.Widths.Count == t.Headers.Count ? t.Widths.Sum() : t.Headers.Count;
            for (int i = 0; i < t.Headers.Count; i++)
            {
                double w = t.Widths.Count == t.Headers.Count ? t.Widths[i] : 1;
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w / total * width) });
            }

            for (int i = 0; i < t.Headers.Count; i++)
            {
                var c = i < cells.Count ? cells[i] : new ReportCell("");
                var tb = new TextBlock
                {
                    Text = c.Text ?? "",
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,              // long remarks wrap onto more lines - never cut
                    FontWeight = c.Bold ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = B(c.Color ?? "#0F172A"),
                    Margin = new Thickness(5, 4, 5, 4),
                    VerticalAlignment = VerticalAlignment.Top
                };
                Grid.SetColumn(tb, i);
                g.Children.Add(tb);
            }

            return new Border { Child = g, BorderBrush = B("#E2E8F0"), BorderThickness = new Thickness(0, 0, 0, 1) };
        }
    }
}
