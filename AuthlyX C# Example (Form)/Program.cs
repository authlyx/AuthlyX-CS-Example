using System;
using System.Windows.Forms;
using AuthlyXSDK;

namespace AuthlyX_CS_Example_Form
{
    internal static class Program
    {
        public static AuthlyX app = new AuthlyX(appKey: "", version: "");

        [STAThread]
        internal static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Login());
            app.Dispose();
            return 0;
        }
    }
}
