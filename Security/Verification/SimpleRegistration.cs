using System;
using System.Windows.Forms;

namespace Bojalib
{
    namespace Security
    {
        namespace Verification
        {
            public partial class SimpleRegistration : Form
            {
                public SimpleRegistration()
                {
                    InitializeComponent();
                }

                private string password;
                private string Password
                {
                    get
                    {
                        string temp = password;
                        password = null;
                        return temp;

                    }
                }

                /// <summary>
                /// Blocks the window and asks for registration
                /// </summary>
                /// <returns></returns>
                public string ShowPrompt()
                {
                    ShowDialog();
                    string temp = password;
                    password = null;
                    return temp;
                }

                private void btnRegister_Click(object sender, EventArgs e)
                {
                    if (tbPassword.Text != tbConfirmPassword.Text)
                        return;
                    else
                    {
                        password = tbPassword.Text;
                        Close();
                    }
                }

            }
        }
    }
}
