using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Krypton.Toolkit;

namespace MassaKWin.Ui
{
    public static class ThemeManager
    {
        private static KryptonManager? _manager;
        private static KryptonPalette? _palette;
        private static readonly Font DefaultFont = new("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);

        public static void Initialize()
        {
            _palette = new KryptonPalette
            {
                BasePaletteMode = PaletteMode.Office2013White
            };

            _palette.ButtonStyles.ButtonCommon.OverrideDefault.Back.Color1 = Color.FromArgb(64, 106, 197);
            _palette.ButtonStyles.ButtonCommon.OverrideDefault.Back.Color2 = Color.FromArgb(64, 106, 197);
            _palette.ButtonStyles.ButtonCommon.OverrideDefault.Border.Color1 = Color.FromArgb(64, 106, 197);
            _palette.ButtonStyles.ButtonCommon.OverrideDefault.Border.Color2 = Color.FromArgb(64, 106, 197);
            _palette.ButtonStyles.ButtonCommon.StateCommon.Content.ShortText.Color1 = Color.White;
            _palette.ButtonStyles.ButtonCommon.StateCommon.Content.ShortText.Font = DefaultFont;

            _palette.ButtonStyles.ButtonCommon.StateCommon.Back.Color1 = Color.FromArgb(64, 106, 197);
            _palette.ButtonStyles.ButtonCommon.StateCommon.Back.Color2 = Color.FromArgb(52, 91, 170);
            _palette.ButtonStyles.ButtonCommon.StateTracking.Back.Color1 = Color.FromArgb(86, 128, 224);
            _palette.ButtonStyles.ButtonCommon.StateTracking.Back.Color2 = Color.FromArgb(68, 108, 196);

            _palette.ButtonStyles.ButtonCommon.StateCommon.Border.Rounding = 6;
            _palette.ButtonStyles.ButtonCommon.StateCommon.Border.Width = 1;
            _palette.ButtonStyles.ButtonCommon.StateCommon.Border.Color1 = Color.FromArgb(52, 91, 170);
            _palette.ButtonStyles.ButtonCommon.StateCommon.Border.Color2 = Color.FromArgb(52, 91, 170);

            _palette.StateCommon.Header.Content.ShortText.Font = DefaultFont;
            _palette.StateCommon.Header.Content.ShortText.Color1 = Color.FromArgb(40, 40, 40);
            _palette.StateCommon.Content.Font = DefaultFont;
            _palette.StateCommon.Content.Color1 = Color.FromArgb(40, 40, 40);
            _palette.FormStyles.FormMain.StateCommon.Back.Color1 = Color.White;
            _palette.FormStyles.FormMain.StateCommon.Back.Color2 = Color.White;

            _manager = new KryptonManager
            {
                GlobalPaletteMode = PaletteModeManager.Custom,
                GlobalPalette = _palette
            };
        }

        public static void Apply(Control root)
        {
            if (root == null)
                return;

            ApplyFontRecursively(root, DefaultFont);
        }

        private static void ApplyFontRecursively(Control control, Font font)
        {
            control.Font = font;

            foreach (Control child in control.Controls.Cast<Control>())
            {
                ApplyFontRecursively(child, font);
            }
        }
    }
}
