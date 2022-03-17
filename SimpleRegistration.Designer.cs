using System;
using System.Windows.Forms;

namespace Bojalib
{
    namespace Security 
    {
        namespace Verification 
        {
            partial class SimpleRegistration : Form
            {
                /// <summary>
                /// Required designer variable.
                /// </summary>
                private System.ComponentModel.IContainer components = null;

                /// <summary>
                /// Clean up any resources being used.
                /// </summary>
                /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
                protected override void Dispose(bool disposing)
                {
                    if (disposing && (components != null))
                    {
                        components.Dispose();
                    }
                    base.Dispose(disposing);
                }

                #region Windows Form Designer generated code

                /// <summary>
                /// Required method for Designer support - do not modify
                /// the contents of this method with the code editor.
                /// </summary>
                private void InitializeComponent()
                {
                    this.tbPassword = new System.Windows.Forms.TextBox();
                    this.btnRegister = new System.Windows.Forms.Button();
                    this.tbConfirmPassword = new System.Windows.Forms.TextBox();
                    this.label1 = new System.Windows.Forms.Label();
                    this.label2 = new System.Windows.Forms.Label();
                    this.SuspendLayout();
                    // 
                    // tbPassword
                    // 
                    this.tbPassword.Location = new System.Drawing.Point(12, 29);
                    this.tbPassword.Name = "tbPassword";
                    this.tbPassword.PasswordChar = '*';
                    this.tbPassword.Size = new System.Drawing.Size(246, 23);
                    this.tbPassword.TabIndex = 0;
                    // 
                    // btnRegister
                    // 
                    this.btnRegister.BackColor = System.Drawing.Color.Lavender;
                    this.btnRegister.Location = new System.Drawing.Point(13, 114);
                    this.btnRegister.Name = "btnRegister";
                    this.btnRegister.Size = new System.Drawing.Size(245, 23);
                    this.btnRegister.TabIndex = 1;
                    this.btnRegister.Text = "Register";
                    this.btnRegister.UseVisualStyleBackColor = false;
                    this.btnRegister.Click += new System.EventHandler(this.btnRegister_Click);
                    // 
                    // tbConfirmPassword
                    // 
                    this.tbConfirmPassword.Location = new System.Drawing.Point(12, 76);
                    this.tbConfirmPassword.Name = "tbConfirmPassword";
                    this.tbConfirmPassword.PasswordChar = '*';
                    this.tbConfirmPassword.Size = new System.Drawing.Size(246, 23);
                    this.tbConfirmPassword.TabIndex = 2;
                    // 
                    // label1
                    // 
                    this.label1.AutoSize = true;
                    this.label1.Location = new System.Drawing.Point(13, 8);
                    this.label1.Name = "label1";
                    this.label1.Size = new System.Drawing.Size(63, 15);
                    this.label1.TabIndex = 3;
                    this.label1.Text = "Password: ";
                    // 
                    // label2
                    // 
                    this.label2.AutoSize = true;
                    this.label2.Location = new System.Drawing.Point(12, 58);
                    this.label2.Name = "label2";
                    this.label2.Size = new System.Drawing.Size(110, 15);
                    this.label2.TabIndex = 4;
                    this.label2.Text = "Confirm Password: ";
                    // 
                    // SimplePasswordAdd
                    // 
                    this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
                    this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
                    this.BackColor = System.Drawing.SystemColors.ActiveCaption;
                    this.ClientSize = new System.Drawing.Size(270, 149);
                    this.Controls.Add(this.label2);
                    this.Controls.Add(this.label1);
                    this.Controls.Add(this.tbConfirmPassword);
                    this.Controls.Add(this.btnRegister);
                    this.Controls.Add(this.tbPassword);
                    this.KeyPreview = true;
                    this.MaximizeBox = false;
                    this.MinimizeBox = false;
                    this.Name = "SimplePasswordAdd";
                    this.ShowIcon = false;
                    this.Text = "Register Your Password";
                    this.ResumeLayout(false);
                    this.PerformLayout();

                }

                #endregion

                private System.Windows.Forms.TextBox tbPassword;
                private System.Windows.Forms.Button btnRegister;
                private System.Windows.Forms.TextBox tbConfirmPassword;
                private System.Windows.Forms.Label label1;
                private System.Windows.Forms.Label label2;
            }

        }
    } 
}