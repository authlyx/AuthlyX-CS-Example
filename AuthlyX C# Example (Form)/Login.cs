using AuthlyX;
using Guna.UI2.WinForms;
using System;
using System.Windows.Forms;
namespace AuthlyX_CS_Example_Form
{
    public partial class Login : Form
    {
        private bool isInitializing;

        public static Auth AuthlyXApp = new Auth(
            ownerId: "",
            appName: "",
            version: "",
            secret: ""
        );

        /*
        Optional:
        - Set debug to false to disable SDK logs.
        - Set api to your custom domain, for example: https://example.com/api/v2
        */
        public Login()
        {
            InitializeComponent();
        }

        private void Login_Load(object sender, EventArgs e)
        {
            BeginInit();
        }

        private void btn_Login_Click(object sender, EventArgs e)
        {
            string username = txtUser.Text;
            string password = txtPass.Text;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Please enter both username and password",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetFormBusy(true, btn_Login, "Logging in...");

            try
            {
                AuthlyXApp.Login(username, password, callback: response =>
                {
                    if (response.success)
                    {
                        MessageBox.Show($"Welcome {AuthlyXApp.userData.Username}!",
                            "Login Success", MessageBoxButtons.OK, MessageBoxIcon.Information);

                        Home home = new Home();
                        home.Show();
                        Hide();
                    }
                    else
                    {
                        MessageBox.Show($"Login failed: {response.message}",
                            "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }

                    SetFormBusy(false, btn_Login, "Login");
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during login: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetFormBusy(false, btn_Login, "Login");
            }
        }

        private void btn_Register_Click(object sender, EventArgs e)
        {
            string username = txt_regUser.Text;
            string password = txt_regPass.Text;
            string key = txt_regKey.Text;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(key))
            {
                MessageBox.Show("Please enter both username, password and key",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetFormBusy(true, btn_Register, "Registering...");

            try
            {
                AuthlyXApp.Register(username, password, key, response =>
                {
                    if (response.success)
                    {
                        MessageBox.Show($"Welcome {AuthlyXApp.userData.Username}!",
                            "Registration Success", MessageBoxButtons.OK, MessageBoxIcon.Information);

                        Home home = new Home();
                        home.Show();
                        Hide();
                    }
                    else
                    {
                        MessageBox.Show($"Registration failed: {response.message}",
                            "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }

                    SetFormBusy(false, btn_Register, "Register");
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during Registration: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetFormBusy(false, btn_Register, "Register");
            }
        }

        private void btn_simpleLogin_Click(object sender, EventArgs e)
        {
            string key = txt_Key.Text;

            if (string.IsNullOrEmpty(key))
            {
                MessageBox.Show("Please enter a key to continue",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetFormBusy(true, btn_simpleLogin, "Logging in...");

            try
            {
                AuthlyXApp.Login(key, callback: response =>
                {
                    if (response.success)
                    {
                        MessageBox.Show($"Welcome {AuthlyXApp.userData.LicenseKey}!",
                            "Login Success", MessageBoxButtons.OK, MessageBoxIcon.Information);

                        Home home = new Home();
                        home.Show();
                        Hide();
                    }
                    else
                    {
                        MessageBox.Show($"Login failed: {response.message}",
                            "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }

                    SetFormBusy(false, btn_simpleLogin, "Login");
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during Login: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetFormBusy(false, btn_simpleLogin, "Login");
            }
        }

        private void exit_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void BeginInit()
        {
            if (isInitializing)
            {
                return;
            }

            isInitializing = true;
            SetFormBusy(true);

            AuthlyXApp.Init(response =>
            {
                isInitializing = false;
                SetFormBusy(false);

                if (!response.success)
                {
                    MessageBox.Show(
                        $"AuthlyX init failed: {response.message}",
                        "Initialization Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    Close();
                }
            });
        }

        private void SetFormBusy(bool busy, Guna2Button activeButton = null, string activeText = null)
        {
            txtUser.Enabled = !busy;
            txtPass.Enabled = !busy;
            txt_regUser.Enabled = !busy;
            txt_regPass.Enabled = !busy;
            txt_regKey.Enabled = !busy;
            txt_Key.Enabled = !busy;

            btn_Login.Enabled = !busy;
            btn_Register.Enabled = !busy;
            btn_simpleLogin.Enabled = !busy;
            exit.Enabled = !busy;

            btn_Login.Text = "Login";
            btn_Register.Text = "Register";
            btn_simpleLogin.Text = "Login";

            if (busy && activeButton != null)
            {
                activeButton.Enabled = false;
                activeButton.Text = activeText;
            }
        }
    }
}
