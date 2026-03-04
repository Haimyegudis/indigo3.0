using System.Windows;
using System.Windows.Media;

namespace IndiLogs_3._0.ViewModels
{
    public partial class MainViewModel
    {
        // --- THEME ---
        private void ApplyTheme(bool isDark)
        {
            var dict = Application.Current.Resources;
            if (isDark)
            {
                // ── Main backgrounds ──
                UpdateResource(dict, "BgDark", new SolidColorBrush(Color.FromRgb(10, 18, 30)));       // #0A121E - very deep navy
                UpdateResource(dict, "BgPanel", new SolidColorBrush(Color.FromRgb(15, 25, 40)));      // #0F1928 - dark navy panel
                UpdateResource(dict, "BgCard", new SolidColorBrush(Color.FromRgb(20, 35, 55)));       // #142337 - navy card
                UpdateResource(dict, "BgCardHover", new SolidColorBrush(Color.FromRgb(30, 50, 75)));  // #1E324B - lighter navy hover

                // ── Sidebar ──
                UpdateResource(dict, "SidebarBg", new SolidColorBrush(Color.FromRgb(15, 25, 40)));    // Match BgPanel in dark
                UpdateResource(dict, "SidebarText", new SolidColorBrush(Color.FromRgb(220, 230, 240)));
                UpdateResource(dict, "SidebarBorder", new SolidColorBrush(Color.FromRgb(40, 60, 85)));

                // ── Text & borders ──
                UpdateResource(dict, "TextPrimary", new SolidColorBrush(Color.FromRgb(220, 230, 240)));
                UpdateResource(dict, "TextSecondary", new SolidColorBrush(Color.FromRgb(140, 160, 180)));
                UpdateResource(dict, "BorderColor", new SolidColorBrush(Color.FromRgb(40, 60, 85)));  // #283C55

                // ── Primary accent (consistent across themes) ──
                UpdateResource(dict, "PrimaryColor", new SolidColorBrush(Color.FromRgb(59, 130, 246)));  // #3B82F6
                UpdateResource(dict, "PrimaryHover", new SolidColorBrush(Color.FromRgb(96, 165, 250)));  // #60A5FA
                UpdateResource(dict, "PrimaryGlow", new SolidColorBrush(Color.FromArgb(0x20, 0x3B, 0x82, 0xF6)));

                // ── Diff / comparison ──
                UpdateResource(dict, "DiffRowDifferent", new SolidColorBrush(Color.FromRgb(42, 21, 21)));  // #2A1515 dark red tint
                UpdateResource(dict, "DiffAddedBg", new SolidColorBrush(Color.FromRgb(144, 238, 144)));
                UpdateResource(dict, "DiffRemovedBg", new SolidColorBrush(Color.FromRgb(240, 128, 128)));

                // ── Gap indicator ──
                UpdateResource(dict, "GapIndicatorBg", new SolidColorBrush(Color.FromRgb(27, 53, 84)));   // #1B3554
                UpdateResource(dict, "GapIndicatorFg", new SolidColorBrush(Color.FromRgb(107, 140, 174))); // #6B8CAE

                // ── Hover overlays ──
                UpdateResource(dict, "RowHoverBg", new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255))); // 10% white
                UpdateResource(dict, "TabHoverBg", new SolidColorBrush(Color.FromArgb(0x10, 0x88, 0x88, 0x88)));

                // ── Animation / loading ──
                UpdateResource(dict, "AnimColor1", new SolidColorBrush(Color.FromRgb(0, 200, 220)));
                UpdateResource(dict, "AnimColor2", new SolidColorBrush(Color.FromRgb(245, 0, 87)));
                UpdateResource(dict, "AnimColor3", new SolidColorBrush(Color.FromRgb(255, 255, 0)));
                UpdateResource(dict, "AnimText", new SolidColorBrush(Colors.White));

                // ── Scrollbar thumb ──
                UpdateResource(dict, "ScrollThumb", new SolidColorBrush(Color.FromRgb(0x68, 0x68, 0x68)));
                UpdateResource(dict, "ScrollThumbHover", new SolidColorBrush(Color.FromRgb(0x8C, 0x8C, 0x8C)));
                UpdateResource(dict, "ScrollThumbDrag", new SolidColorBrush(Color.FromRgb(0xAD, 0xAD, 0xAD)));
                UpdateResource(dict, "ScrollThumbH", new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5A)));
                UpdateResource(dict, "ScrollThumbHoverH", new SolidColorBrush(Color.FromRgb(0x7A, 0x7A, 0x7A)));
                UpdateResource(dict, "ScrollThumbDragH", new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)));

                // ── Row selection / highlights ──
                UpdateResource(dict, "RowSelectedBg", new SolidColorBrush(Color.FromRgb(0xFF, 0xFA, 0xCD))); // #FFFACD
                UpdateResource(dict, "RowMarkedBg", new SolidColorBrush(Color.FromRgb(0x90, 0xEE, 0x90)));   // #90EE90
                UpdateResource(dict, "RowErrorFg", new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)));    // #FF6B6B
            }
            else
            {
                // ── Main backgrounds ──
                UpdateResource(dict, "BgDark", new SolidColorBrush(Color.FromRgb(240, 242, 245)));    // #F0F2F5 - soft gray
                UpdateResource(dict, "BgPanel", new SolidColorBrush(Color.FromRgb(243, 244, 246)));    // #F3F4F6
                UpdateResource(dict, "BgCard", new SolidColorBrush(Colors.White));                      // #FFFFFF
                UpdateResource(dict, "BgCardHover", new SolidColorBrush(Color.FromRgb(235, 238, 242))); // #EBEEF2

                // ── Sidebar ──
                UpdateResource(dict, "SidebarBg", new SolidColorBrush(Color.FromRgb(243, 244, 246)));  // #F3F4F6
                UpdateResource(dict, "SidebarText", new SolidColorBrush(Color.FromRgb(31, 41, 55)));   // #1F2937
                UpdateResource(dict, "SidebarBorder", new SolidColorBrush(Color.FromRgb(229, 231, 235))); // #E5E7EB

                // ── Text & borders ──
                UpdateResource(dict, "TextPrimary", new SolidColorBrush(Color.FromRgb(31, 41, 55)));   // #1F2937
                UpdateResource(dict, "TextSecondary", new SolidColorBrush(Color.FromRgb(107, 114, 128))); // #6B7280
                UpdateResource(dict, "BorderColor", new SolidColorBrush(Color.FromRgb(209, 213, 219))); // #D1D5DB

                // ── Primary accent (slightly darker for light bg readability) ──
                UpdateResource(dict, "PrimaryColor", new SolidColorBrush(Color.FromRgb(37, 99, 235)));   // #2563EB
                UpdateResource(dict, "PrimaryHover", new SolidColorBrush(Color.FromRgb(59, 130, 246)));   // #3B82F6
                UpdateResource(dict, "PrimaryGlow", new SolidColorBrush(Color.FromArgb(0x18, 0x25, 0x63, 0xEB)));

                // ── Diff / comparison ──
                UpdateResource(dict, "DiffRowDifferent", new SolidColorBrush(Color.FromRgb(254, 226, 226))); // #FEE2E2 light pink tint
                UpdateResource(dict, "DiffAddedBg", new SolidColorBrush(Color.FromRgb(187, 247, 208)));     // #BBF7D0
                UpdateResource(dict, "DiffRemovedBg", new SolidColorBrush(Color.FromRgb(254, 202, 202)));   // #FECACA

                // ── Gap indicator ──
                UpdateResource(dict, "GapIndicatorBg", new SolidColorBrush(Color.FromRgb(224, 231, 240))); // #E0E7F0
                UpdateResource(dict, "GapIndicatorFg", new SolidColorBrush(Color.FromRgb(100, 116, 139))); // #64748B

                // ── Hover overlays ──
                UpdateResource(dict, "RowHoverBg", new SolidColorBrush(Color.FromArgb(0x18, 0, 0, 0)));       // 10% black
                UpdateResource(dict, "TabHoverBg", new SolidColorBrush(Color.FromArgb(0x12, 0, 0, 0)));       // 7% black

                // ── Animation / loading ──
                UpdateResource(dict, "AnimColor1", new SolidColorBrush(Color.FromRgb(0, 120, 215)));
                UpdateResource(dict, "AnimColor2", new SolidColorBrush(Color.FromRgb(220, 0, 80)));
                UpdateResource(dict, "AnimColor3", new SolidColorBrush(Color.FromRgb(200, 160, 0)));
                UpdateResource(dict, "AnimText", new SolidColorBrush(Color.FromRgb(31, 41, 55)));

                // ── Scrollbar thumb ──
                UpdateResource(dict, "ScrollThumb", new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)));
                UpdateResource(dict, "ScrollThumbHover", new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90)));
                UpdateResource(dict, "ScrollThumbDrag", new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)));
                UpdateResource(dict, "ScrollThumbH", new SolidColorBrush(Color.FromRgb(0xB8, 0xB8, 0xB8)));
                UpdateResource(dict, "ScrollThumbHoverH", new SolidColorBrush(Color.FromRgb(0x98, 0x98, 0x98)));
                UpdateResource(dict, "ScrollThumbDragH", new SolidColorBrush(Color.FromRgb(0x78, 0x78, 0x78)));

                // ── Row selection / highlights ──
                UpdateResource(dict, "RowSelectedBg", new SolidColorBrush(Color.FromRgb(0xDB, 0xED, 0xFF))); // #DBEDFF light blue
                UpdateResource(dict, "RowMarkedBg", new SolidColorBrush(Color.FromRgb(0xD4, 0xED, 0xDA)));   // #D4EDDA light green
                UpdateResource(dict, "RowErrorFg", new SolidColorBrush(Color.FromRgb(0xDC, 0x35, 0x45)));    // #DC3545 bootstrap red
            }
        }

        private void UpdateResource(ResourceDictionary dict, string key, object value)
        {
            if (dict.Contains(key))
                dict.Remove(key);
            dict.Add(key, value);
        }

        private void UpdateContentFont(string fontName) { if (!string.IsNullOrEmpty(fontName) && Application.Current != null) UpdateResource(Application.Current.Resources, "ContentFontFamily", new FontFamily(fontName)); }
        private void UpdateContentFontWeight(bool isBold)
        {
            if (Application.Current != null)
            {
                UpdateResource(Application.Current.Resources, "ContentFontWeight",
                    isBold ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal);
            }
        }
    }
}
