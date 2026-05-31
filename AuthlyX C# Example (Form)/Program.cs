using AuthlyX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AuthlyX_CS_Example_Form
{
    internal static class Program
    {
        public static Auth AuthlyXApp = new Auth(
            ownerId: "",
            appName: "",
            version: "",
            secret: "",
            api: ""
        );

        /*
        Optional:
        - Set debug to false to disable SDK logs.
        - Set api to your custom domain, e.g. https://example.com/api/v2
        - Set antiDebug to false to disable anti-debugger protection (useful for local testing).
        */

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            AuthlyXApp.init();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Login());
        }
    }
}
